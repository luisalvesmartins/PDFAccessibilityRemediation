using EN301549PdfProcessor.Models;
using EN301549PdfProcessor.Reports;
using EN301549PdfProcessor.Validators;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EN301549PdfProcessor.Tests;

/// <summary>
/// Testes unitários — EN 301 549 + WCAG 2.1 + WCAG 2.2
/// </summary>
public class ValidadorAcessibilidadePdfTests : IDisposable
{
    private readonly ValidadorAcessibilidadePdf _validador;
    private readonly GeradorRelatorio _gerador;
    private readonly List<string> _temporarios = new();

    public ValidadorAcessibilidadePdfTests()
    {
        var lf = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        _validador = new ValidadorAcessibilidadePdf(lf.CreateLogger<ValidadorAcessibilidadePdf>());
        _gerador   = new GeradorRelatorio();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 2.4.2 / EN 9.2.4.2 – Título
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void Titulo_Ausente_GeraViolacao_2_4_2()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: null, idioma: "pt-PT", tagged: false));
        Assert.Contains(resultado.Violacoes, v => v.CodigoCriterio == "2.4.2");
    }

    [Fact]
    public void Titulo_Presente_NaoGeraViolacao_2_4_2()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "Relatório Anual 2024", idioma: "pt-PT", tagged: false));
        Assert.DoesNotContain(resultado.Violacoes, v => v.CodigoCriterio == "2.4.2");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 3.1.1 / EN 9.3.1.1 – Idioma
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void Idioma_Ausente_GeraViolacao_3_1_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "Teste", idioma: null, tagged: false));
        Assert.Contains(resultado.Violacoes, v => v.CodigoCriterio == "3.1.1");
    }

    [Fact]
    public void Idioma_Presente_NaoGeraViolacao_3_1_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "Teste", idioma: "pt-PT", tagged: false));
        Assert.DoesNotContain(resultado.Violacoes, v => v.CodigoCriterio == "3.1.1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 1.3.1 / EN 9.1.3.1 – Estrutura Tags
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void SemTags_GeraViolacaoCritica_1_3_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: false));
        var v = resultado.Violacoes.FirstOrDefault(x => x.CodigoCriterio == "1.3.1");
        Assert.NotNull(v);
        Assert.Equal(GravidadeViolacao.Critica, v.Gravidade);
    }

    [Fact]
    public void ComTags_NaoGeraViolacao_1_3_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true));
        Assert.DoesNotContain(resultado.Violacoes, v => v.CodigoCriterio == "1.3.1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 2.1.1 – Teclado
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void SemTags_GeraViolacao_2_1_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: false));
        Assert.Contains(resultado.Violacoes, v => v.CodigoCriterio == "2.1.1");
    }

    [Fact]
    public void ComTags_Aprova_2_1_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true));
        Assert.DoesNotContain(resultado.Violacoes, v => v.CodigoCriterio == "2.1.1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 4.1.2 – Nome, Função, Valor
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void SemTags_GeraViolacao_4_1_2()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: false));
        Assert.Contains(resultado.Violacoes, v => v.CodigoCriterio == "4.1.2");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 2.4.1 – Ignorar Blocos / Marcadores
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void DocumentoLongoSemMarcadores_GeraViolacao_2_4_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true, paginas: 10));
        Assert.Contains(resultado.Violacoes, v => v.CodigoCriterio == "2.4.1");
    }

    [Fact]
    public void DocumentoCurto_NaoGeraViolacao_2_4_1()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true, paginas: 3));
        Assert.DoesNotContain(resultado.Violacoes, v => v.CodigoCriterio == "2.4.1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WCAG 2.5.8 (WCAG 2.2) – Tamanho do Alvo
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void LinkPequeno_GeraViolacao_2_5_8_WCAG22()
    {
        var caminho = CriarPdfComLinkPequeno();
        var resultado = _validador.Analisar(caminho);
        // Links muito pequenos devem acionar violação 2.5.8
        Assert.Contains(resultado.Violacoes, v => v.CodigoCriterio == "2.5.8");
        Assert.Equal(NormaOrigem.WCAG22, resultado.Violacoes.First(v => v.CodigoCriterio == "2.5.8").Norma);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Mapeamento WCAG → EN 301 549
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void ViolacoesDevemTerCodigoEN301549()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: null, idioma: null, tagged: false));
        foreach (var v in resultado.Violacoes)
        {
            Assert.False(string.IsNullOrEmpty(v.CodigoEN301549),
                $"Violação {v.CodigoCriterio} não tem código EN 301 549");
        }
    }

    [Fact]
    public void MapaCriterios_WCAG_1_1_1_MapeiaPara_9_1_1_1()
    {
        Assert.Equal("9.1.1.1", MapaCriterios.ObterEN301549("1.1.1"));
    }

    [Fact]
    public void MapaCriterios_WCAG22_2_5_8_MapeiaPara_9_2_5_8()
    {
        Assert.Equal("9.2.5.8", MapaCriterios.ObterEN301549("2.5.8"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Conformidade por norma
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void ResultadoDeveTerConformidadeParaTodasAsNormas()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true));
        Assert.NotNull(resultado.ConformidadeEN301549);
        Assert.NotNull(resultado.ConformidadeWCAG21);
        Assert.NotNull(resultado.ConformidadeWCAG22);
        Assert.Equal("EN 301 549 v3.2.1", resultado.ConformidadeEN301549.Norma);
        Assert.Equal("WCAG 2.1",  resultado.ConformidadeWCAG21.Norma);
        Assert.Equal("WCAG 2.2",  resultado.ConformidadeWCAG22.Norma);
    }

    [Fact]
    public void WCAG22_ScoreNaoDeveExceder100()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true));
        Assert.InRange(resultado.ConformidadeWCAG22.Pontuacao, 0.0, 100.0);
    }

    [Fact]
    public void PontuacaoGlobal_DeveSerMediaPonderada()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: true));
        Assert.InRange(resultado.PontuacaoConformidade, 0.0, 100.0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Relatórios
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void Relatorio_DeveConterTodasAsNormas()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: false));
        var relatorio = _gerador.GerarRelatorioTexto(resultado);

        Assert.Contains("EN 301 549", relatorio);
        Assert.Contains("WCAG 2.1",   relatorio);
        Assert.Contains("WCAG 2.2",   relatorio);
    }

    [Fact]
    public void Relatorio_DeveIndicarCriteriosWcag22()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: false));
        var relatorio = _gerador.GerarRelatorioTexto(resultado);
        Assert.Contains("WCAG 2.2", relatorio);
    }

    [Fact]
    public void RelatorioJson_DeveSerDeserializavel()
    {
        var resultado = _validador.Analisar(CriarPdf(titulo: "T", idioma: "pt-PT", tagged: false));
        var json = _gerador.GerarRelatorioJson(resultado);
        Assert.NotEmpty(json);
        Assert.Contains("ConformidadeWCAG22", json);
        Assert.Contains("ConformidadeEN301549", json);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private string CriarPdf(
        string? titulo,
        string? idioma,
        bool tagged,
        int paginas = 1)
    {
        var path = Path.GetTempFileName() + ".pdf";
        _temporarios.Add(path);

        using var writer = new PdfWriter(path);
        using var pdf    = new PdfDocument(writer);

        if (tagged) pdf.SetTagged();
        if (titulo  != null) pdf.GetDocumentInfo().SetTitle(titulo);
        if (idioma  != null) pdf.GetCatalog().Put(PdfName.Lang, new PdfString(idioma));

        using var doc = new Document(pdf);
        for (int i = 0; i < paginas; i++)
        {
            if (i > 0) doc.Add(new AreaBreak());
            doc.Add(new Paragraph($"Página {i + 1} — conteúdo de teste EN 301 549 / WCAG 2.1 / WCAG 2.2."));
        }

        return path;
    }

    private string CriarPdfComLinkPequeno()
    {
        var path = Path.GetTempFileName() + ".pdf";
        _temporarios.Add(path);

        using var writer = new PdfWriter(path);
        using var pdf    = new PdfDocument(writer);
        pdf.GetDocumentInfo().SetTitle("Teste Links Pequenos");
        pdf.GetCatalog().Put(PdfName.Lang, new PdfString("pt-PT"));

        var pagina = pdf.AddNewPage();

        // Replace the problematic code with the following:
        var link = new iText.Kernel.Pdf.Annot.PdfLinkAnnotation(
           new iText.Kernel.Geom.Rectangle(50, 700, 5, 5));
        link.SetAction(iText.Kernel.Pdf.Action.PdfAction.CreateURI("https://example.com"));
        pagina.AddAnnotation(link);

        using var doc = new Document(pdf);
        doc.Add(new Paragraph("Documento com link muito pequeno para teste WCAG 2.5.8."));

        return path;
    }

    public void Dispose()
    {
        foreach (var f in _temporarios)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
