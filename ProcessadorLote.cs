using EN301549PdfProcessor.Models;
using EN301549PdfProcessor.Reports;
using EN301549PdfProcessor.Validators;
using Microsoft.Extensions.Logging;

namespace EN301549PdfProcessor.Services;

/// <summary>
/// Processador em lote de múltiplos PDFs
/// </summary>
public class ProcessadorLote
{
    private readonly ILogger<ProcessadorLote> _logger;
    private readonly ValidadorAcessibilidadePdf _validador;
    private readonly ServicodeRemediacao _remediador;
    private readonly GeradorRelatorio _geradorRelatorio;

    public ProcessadorLote(
        ILogger<ProcessadorLote> logger,
        ValidadorAcessibilidadePdf validador,
        ServicodeRemediacao remediador,
        GeradorRelatorio geradorRelatorio)
    {
        _logger = logger;
        _validador = validador;
        _remediador = remediador;
        _geradorRelatorio = geradorRelatorio;
    }

    /// <summary>
    /// Analisa todos os PDFs numa pasta
    /// </summary>
    public List<AccessibilityAnalysisResult> AnalisarPasta(
        string caminhoPasta,
        bool recursivo = false)
    {
        var opcaoBusca = recursivo ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var arquivos = Directory.GetFiles(caminhoPasta, "*.pdf", opcaoBusca);

        _logger.LogInformation("Encontrados {Count} PDFs em '{Pasta}'", arquivos.Length, caminhoPasta);

        var resultados = new List<AccessibilityAnalysisResult>();

        foreach (var arquivo in arquivos)
        {
            try
            {
                _logger.LogInformation("Analisando: {Arquivo}", Path.GetFileName(arquivo));
                var resultado = _validador.Analisar(arquivo);
                resultados.Add(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar {Arquivo}", arquivo);
            }
        }

        return resultados;
    }

    /// <summary>
    /// Analisa e remedia todos os PDFs de uma pasta
    /// </summary>
    public List<ResultadoRemediacao> RemediарPasta(
        string caminhoPasta,
        string? pastaDestino = null,
        bool recursivo = false)
    {
        var opcaoBusca = recursivo ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var arquivos = Directory.GetFiles(caminhoPasta, "*.pdf", opcaoBusca);

        pastaDestino ??= Path.Combine(caminhoPasta, "remediados_EN301549");
        Directory.CreateDirectory(pastaDestino);

        _logger.LogInformation(
            "Iniciando remediação em lote de {Count} PDFs. Destino: {Destino}",
            arquivos.Length, pastaDestino);

        var resultados = new List<ResultadoRemediacao>();

        foreach (var arquivo in arquivos)
        {
            try
            {
                _logger.LogInformation("Remediando: {Arquivo}", Path.GetFileName(arquivo));
                var resultado = _remediador.Remediar(arquivo, pastaDestino);
                resultados.Add(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao remediar {Arquivo}", arquivo);
            }
        }

        return resultados;
    }

    /// <summary>
    /// Gera relatório consolidado de múltiplas análises
    /// </summary>
    public string GerarRelatorioConsolidado(List<AccessibilityAnalysisResult> analises)
    {
        var sb = new System.Text.StringBuilder();
        var sep = new string('═', 70);

        double a = 0;
        if (analises.Count > 0)
        {
            a = (double)analises.Count(a => a.Conforme) / analises.Count * 100;
        }

        sb.AppendLine(sep);
        sb.AppendLine("  RELATÓRIO CONSOLIDADO EN 301 549 – PROCESSAMENTO EM LOTE");
        sb.AppendLine(sep);
        sb.AppendLine();
        sb.AppendLine($"  Total de documentos : {analises.Count}");
        sb.AppendLine($"  Conformes           : {analises.Count(a => a.Conforme)} ({a}%)");
        sb.AppendLine($"  Não conformes       : {analises.Count(a => !a.Conforme)}");
        sb.AppendLine($"  Pontuação média     : {(analises.Any() ? analises.Average(a => a.PontuacaoConformidade) : 0):F1}%");
        sb.AppendLine();

        sb.AppendLine("  DETALHE POR DOCUMENTO");
        sb.AppendLine(new string('─', 70));

        foreach (var analise in analises.OrderBy(a => a.PontuacaoConformidade))
        {
            var estado = analise.Conforme ? "✅" : "❌";
            sb.AppendLine($"  {estado} {analise.FileName,-40} {analise.PontuacaoConformidade,6:F1}%  V:{analise.Violacoes.Count} A:{analise.Advertencias.Count}");
        }

        sb.AppendLine();
        sb.AppendLine("  VIOLAÇÕES MAIS COMUNS");
        sb.AppendLine(new string('─', 70));

        var violacoesPorCriterio = analises
            .SelectMany(a => a.Violacoes)
            .GroupBy(v => v.CodigoCriterio)
            .OrderByDescending(g => g.Count())
            .Take(10);

        foreach (var grupo in violacoesPorCriterio)
        {
            var primeiraViolacao = grupo.First();
            sb.AppendLine($"  [{grupo.Key}] {primeiraViolacao.NomeCriterio,-35} {grupo.Count(),3}x");
        }

        sb.AppendLine();
        sb.AppendLine(sep);

        return sb.ToString();
    }
}
