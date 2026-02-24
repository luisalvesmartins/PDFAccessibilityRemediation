using Microsoft.AspNetCore.Mvc;
using EN301549PdfProcessor.Models;
using EN301549PdfProcessor.Reports;
using EN301549PdfProcessor.Services;
using EN301549PdfProcessor.Validators;
using System.IO.Compression;

namespace PDFAccessWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PdfController : ControllerBase
{
    private readonly ILogger<PdfController> _logger;
    private readonly ValidadorAcessibilidadePdf _validador;
    private readonly ServicodeRemediacao _remediador;
    private readonly GeradorRelatorio _gerador;
    private readonly IWebHostEnvironment _env;

    private string PastaUploads  => Path.Combine(_env.WebRootPath, "uploads");
    private string PastaOutputs  => Path.Combine(_env.WebRootPath, "outputs");

    public PdfController(ILogger<PdfController> logger, ValidadorAcessibilidadePdf validador,
        ServicodeRemediacao remediador, GeradorRelatorio gerador, IWebHostEnvironment env)
    {
        _logger = logger; _validador = validador; _remediador = remediador;
        _gerador = gerador; _env = env;
        Directory.CreateDirectory(PastaUploads);
        Directory.CreateDirectory(PastaOutputs);
    }

    [HttpPost("remediar")]
    [RequestSizeLimit(250 * 1024 * 1024)]
    public async Task<IActionResult> Remediar(IList<IFormFile> files)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { erro = "Nenhum ficheiro enviado." });

        var pdfs = files.Where(f =>
            f.ContentType == "application/pdf" ||
            f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

        if (pdfs.Count == 0) return BadRequest(new { erro = "Apenas PDFs são aceites." });

        var sessionId    = Guid.NewGuid().ToString("N")[..12];
        var pastaSession = Path.Combine(PastaOutputs, sessionId);
        Directory.CreateDirectory(pastaSession);

        var resultados = new List<ResultadoFicheiro>();

        foreach (var file in pdfs)
        {
            var nomeSeguro    = SanitizarNome(file.FileName);
            var caminhoUpload = Path.Combine(PastaUploads, $"{sessionId}_{nomeSeguro}");

            try
            {
                await using (var stream = System.IO.File.Create(caminhoUpload))
                    await file.CopyToAsync(stream);

                var resultado = _remediador.Remediar(caminhoUpload, pastaSession);

                var relatorio    = resultado.AnaliseDepois != null
                    ? _gerador.GerarRelatorioTexto(resultado.AnaliseDepois)
                    : "Análise não disponível.";
                var nomeRelatorio    = Path.GetFileNameWithoutExtension(nomeSeguro) + "_relatorio.txt";
                await System.IO.File.WriteAllTextAsync(Path.Combine(pastaSession, nomeRelatorio), relatorio);

                resultados.Add(new ResultadoFicheiro
                {
                    NomeOriginal       = file.FileName,
                    Sucesso            = resultado.Sucesso,
                    Erros              = resultado.Erros,
                    PontuacaoAntes     = Math.Round(resultado.AnaliseAntes?.PontuacaoConformidade ?? 0, 1),
                    PontuacaoDepois    = Math.Round(resultado.AnaliseDepois?.PontuacaoConformidade ?? 0, 1),
                    EN301549Antes      = Math.Round(resultado.AnaliseAntes?.ConformidadeEN301549.Pontuacao ?? 0, 1),
                    EN301549Depois     = Math.Round(resultado.AnaliseDepois?.ConformidadeEN301549.Pontuacao ?? 0, 1),
                    WCAG21Antes        = Math.Round(resultado.AnaliseAntes?.ConformidadeWCAG21.Pontuacao ?? 0, 1),
                    WCAG21Depois       = Math.Round(resultado.AnaliseDepois?.ConformidadeWCAG21.Pontuacao ?? 0, 1),
                    WCAG22Antes        = Math.Round(resultado.AnaliseAntes?.ConformidadeWCAG22.Pontuacao ?? 0, 1),
                    WCAG22Depois       = Math.Round(resultado.AnaliseDepois?.ConformidadeWCAG22.Pontuacao ?? 0, 1),
                    ConformeDepois     = resultado.AnaliseDepois?.Conforme ?? false,
                    ViolacoesAntes     = resultado.AnaliseAntes?.Violacoes.Count ?? 0,
                    ViolacoesDepois    = resultado.AnaliseDepois?.Violacoes.Count ?? 0,
                    AcoesRealizadas    = resultado.AcoesRealizadas,
                    AcoesNaoRealizadas = resultado.AcoesNaoRealizadas,
                    UrlDownloadPdf     = resultado.Sucesso
                        ? $"/outputs/{sessionId}/{Path.GetFileName(resultado.CaminhoArquivoRemediado)}"
                        : null,
                    UrlDownloadRelatorio = $"/outputs/{sessionId}/{nomeRelatorio}",
                    Estatisticas = resultado.AnaliseDepois != null
                        ? new EstatisticasDto(resultado.AnaliseDepois.Estatisticas, resultado.AnaliseDepois.Metadatas)
                        : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro: {Nome}", file.FileName);
                resultados.Add(new ResultadoFicheiro { NomeOriginal = file.FileName, Sucesso = false,
                    Erros = new List<string> { ex.Message } });
            }
            finally
            {
                if (System.IO.File.Exists(caminhoUpload)) System.IO.File.Delete(caminhoUpload);
            }
        }

        string? urlZip = null;
        int ok = resultados.Count(r => r.Sucesso);
        if (ok > 1) urlZip = await CriarZip(pastaSession, sessionId);

        return Ok(new { sessionId, total = resultados.Count, sucesso = ok,
            falhas = resultados.Count(r => !r.Sucesso), urlZip, ficheiros = resultados });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });

    private async Task<string> CriarZip(string pasta, string sessionId)
    {
        var zipPath = Path.Combine(PastaOutputs, $"remediated_{sessionId}.zip");
        var arquivos = Directory.GetFiles(pasta, "*_EN301549_remediado.pdf");
        await Task.Run(() => {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var f in arquivos) zip.CreateEntryFromFile(f, Path.GetFileName(f));
        });
        return $"/outputs/remediated_{sessionId}.zip";
    }

    private static string SanitizarNome(string nome)
    {
        return string.Join("_", nome.Split(Path.GetInvalidFileNameChars()));
    }
}

public class ResultadoFicheiro
{
    public string NomeOriginal { get; set; } = string.Empty;
    public bool Sucesso { get; set; }
    public List<string> Erros { get; set; } = new();
    public double PontuacaoAntes { get; set; }
    public double PontuacaoDepois { get; set; }
    public double EN301549Antes { get; set; }
    public double EN301549Depois { get; set; }
    public double WCAG21Antes { get; set; }
    public double WCAG21Depois { get; set; }
    public double WCAG22Antes { get; set; }
    public double WCAG22Depois { get; set; }
    public bool ConformeDepois { get; set; }
    public int ViolacoesAntes { get; set; }
    public int ViolacoesDepois { get; set; }
    public List<string> AcoesRealizadas { get; set; } = new();
    public List<string> AcoesNaoRealizadas { get; set; } = new();
    public string? UrlDownloadPdf { get; set; }
    public string? UrlDownloadRelatorio { get; set; }
    public EstatisticasDto? Estatisticas { get; set; }
}

public class EstatisticasDto
{
    public int Paginas { get; set; }
    public int Imagens { get; set; }
    public int ImagensComAlt { get; set; }
    public int Links { get; set; }
    public int Formularios { get; set; }
    public bool Tagged { get; set; }

    public EstatisticasDto(EN301549PdfProcessor.Models.EstatisticasDocumento e, EN301549PdfProcessor.Models.MetadatasPdf m)
    {
        Paginas = e.TotalPaginas; Imagens = e.TotalImagens; ImagensComAlt = e.ImagensComAlt;
        Links = e.TotalLinks; Formularios = e.TotalFormularios; Tagged = m.ETagged;
    }
}
