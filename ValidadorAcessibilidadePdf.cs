using EN301549PdfProcessor.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;

namespace EN301549PdfProcessor.Validators;

/// <summary>
/// Orquestrador principal de validação de acessibilidade.
/// Coordena validação EN 301 549 (cláusulas 9.x) + WCAG 2.1 + WCAG 2.2.
///
/// Critérios EN 301 549 específicos adicionais (não-web):
///   11.x  Software
///   12.x  Documentação e Suporte
///   13.x  TIC com Comunicação em Tempo Real
/// </summary>
public class ValidadorAcessibilidadePdf
{
    private readonly ILogger<ValidadorAcessibilidadePdf> _logger;
    private readonly ValidadorWcag _validadorWcag;

    public ValidadorAcessibilidadePdf(ILogger<ValidadorAcessibilidadePdf> logger)
    {
        _logger = logger;
        _validadorWcag = new ValidadorWcag(
            logger as ILogger<ValidadorWcag> ??
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ValidadorWcag>());
    }

    public AccessibilityAnalysisResult Analisar(string caminhoArquivo)
    {
        _logger.LogInformation("Iniciando análise EN 301 549 + WCAG 2.1/2.2: {Arquivo}", caminhoArquivo);

        var resultado = new AccessibilityAnalysisResult
        {
            FilePath  = caminhoArquivo,
            AnalisadoEm = DateTime.Now
        };

        if (!File.Exists(caminhoArquivo))
            throw new FileNotFoundException($"Arquivo não encontrado: {caminhoArquivo}");

        using var leitor    = new PdfReader(caminhoArquivo);
        using var documento = new PdfDocument(leitor);

        // ── 1. Metadadas e estatísticas base ──────────────────────────────────
        resultado.Metadatas              = ExtrairMetadadas(documento);
        resultado.Estatisticas.TotalPaginas = documento.GetNumberOfPages();
        ExtrairEstatisticasFormularios(resultado, documento);

        // ── 2. Validação WCAG 2.1 + 2.2 (todos os critérios) ─────────────────
        _validadorWcag.Validar(resultado, documento);

        // ── 3. Critérios EN 301 549 específicos (além dos WCAG) ───────────────
        VerificarEN301549Especificos(resultado, documento);

        // ── 4. Calcular conformidade EN 301 549 ───────────────────────────────
        CalcularConformidadeEN301549(resultado);

        // ── 5. Pontuação global ────────────────────────────────────────────────
        resultado.PontuacaoConformidade = CalcularPontuacaoGlobal(resultado);
        resultado.Conforme = resultado.PontuacaoConformidade >= 80.0
                             && !resultado.Violacoes.Any(v => v.Gravidade == GravidadeViolacao.Critica);

        _logger.LogInformation(
            "Análise concluída | Global: {G:F1}% | EN301549: {E:F1}% | WCAG2.1: {W1:F1}% | WCAG2.2: {W2:F1}% | V:{V} A:{A}",
            resultado.PontuacaoConformidade,
            resultado.ConformidadeEN301549.Pontuacao,
            resultado.ConformidadeWCAG21.Pontuacao,
            resultado.ConformidadeWCAG22.Pontuacao,
            resultado.Violacoes.Count,
            resultado.Advertencias.Count);

        return resultado;
    }

    // ── EN 301 549 – Critérios específicos (além do WCAG 2.1/2.2) ─────────────
    private void VerificarEN301549Especificos(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // Cláusula 6 – ICT with Two-Way Voice Communication (N/A para PDF)
        // Cláusula 7 – ICT with Video Capabilities  (N/A para PDF estático)
        // Cláusula 8 – Hardware (N/A)

        // ── Cláusula 9 – Web (= WCAG — já executado pelo ValidadorWcag) ───────

        // ── Cláusula 10 – Non-Web Documents ──────────────────────────────────
        VerificarClausula10NaoWeb(r, doc);

        // ── Cláusula 11 – Software ────────────────────────────────────────────
        // PDFs vistos como documento não-web; alguns pontos de 11.x aplicam-se

        // ── Cláusula 12 – Documentation and Support Services ─────────────────
        VerificarClausula12Documentacao(r);
    }

    private void VerificarClausula10NaoWeb(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 10.1.1.1 – Equivalente de texto para conteúdo não-textual (= 1.1.1)
        // já coberto pelo ValidadorWcag

        // 10.5 – Caption positioning
        AdicionarAdvertencia(r, "10.5", "Posicionamento de Legendas",
            NormaOrigem.EN301549,
            "[EN 301 549 Cláusula 10.5] Se o documento contém legendas de vídeo (não aplicável a PDF estático), " +
            "estas não devem obscurecer informação relevante.",
            "Para PDFs com vídeo embutido, garanta que as legendas não cobrem informação visual crítica.");

        // 10.6 – Audio description timing
        if (r.Estatisticas.TemAudioVideo)
        {
            AdicionarAdvertencia(r, "10.6", "Sincronização de Audiodescrição",
                NormaOrigem.EN301549,
                "[EN 301 549 Cláusula 10.6] Se o documento contém vídeo, a audiodescrição deve ser sincronizada " +
                "com o conteúdo de vídeo.",
                "Inclua faixas de audiodescrição sincronizadas em qualquer vídeo embutido no PDF.");
        }

        // Verificar permissões de acessibilidade no PDF
        VerificarPermissoesAcessibilidade(r, doc);

        // PDF/UA conformidade
        VerificarConformidadePdfUA(r, doc);
    }

    private void VerificarPermissoesAcessibilidade(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        var encriptacao = doc.GetReader()?.IsEncrypted() ?? false;

        if (encriptacao)
        {
            // PDF encriptado pode bloquear leitores de ecrã
            r.Metadatas.EncriptacaoTipo = "Encriptado";
            AdicionarViolacao(r, "10.x", "Permissões de Acessibilidade",
                NivelConformidade.A, GravidadeViolacao.Alta, NormaOrigem.EN301549,
                "Robusto",
                "O documento está encriptado/protegido por palavra-passe. " +
                "Restrições de permissão podem impedir leitores de ecrã de aceder ao conteúdo.",
                "Remova restrições que bloqueiem acessibilidade. " +
                "Nos direitos do PDF, certifique-se que 'Acessibilidade de Conteúdo' está permitida. " +
                "Em Adobe Acrobat: Ficheiro > Propriedades > Segurança > Editar Definições.",
                "");
        }
        else
        {
            r.Metadatas.TemPermissoesLeituraEcra = true;
            r.Aprovacoes.Add("✓ [10.x] Documento sem encriptação restritiva; leitores de ecrã têm acesso.");
        }
    }

    private void VerificarConformidadePdfUA(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // Verificar se existe entrada XMP de conformidade PDF/UA (ISO 14289)
        var catalogo  = doc.GetCatalog().GetPdfObject();
        var metadata  = catalogo.GetAsStream(PdfName.Metadata);

        bool temPdfUA = false;
        if (metadata != null)
        {
            try
            {
                var bytes = metadata.GetBytes();
                var xmp = System.Text.Encoding.UTF8.GetString(bytes);
                temPdfUA = xmp.Contains("pdfuaid") || xmp.Contains("PDF/UA");
                r.Metadatas.TemXmpAcessibilidade = true;
            }
            catch { /* ignorar */ }
        }

        if (temPdfUA)
        {
            r.Aprovacoes.Add("✓ [PDF/UA] Metadadas XMP indicam conformidade PDF/UA (ISO 14289).");
        }
        else
        {
            AdicionarAdvertencia(r, "PDF/UA", "Conformidade PDF/UA (ISO 14289)",
                NormaOrigem.EN301549,
                "O documento não declara conformidade PDF/UA nas metadadas XMP. " +
                "PDF/UA é o standard técnico de implementação que EN 301 549 e WCAG referenciam para PDFs.",
                "Para declarar conformidade PDF/UA: adicione entrada XMP pdfuaid:part=1. " +
                "Use o PAC 2024 (PDF Accessibility Checker) para validação completa de PDF/UA. " +
                "Download: https://www.access-for-all.ch/en/pdf-accessibility-checker.html");
        }

        // Verificar DisplayDocTitle
        var viewerPrefs = catalogo.GetAsDictionary(PdfName.ViewerPreferences);
        var displayTitle = viewerPrefs?.GetAsBoolean(new PdfName("DisplayDocTitle"));
        r.Metadatas.DisplayDocTitle = displayTitle?.GetValue() ?? false;

        if (r.Metadatas.DisplayDocTitle)
        {
            r.Aprovacoes.Add("✓ [PDF/UA] DisplayDocTitle=true; o título é exibido na barra do visualizador.");
        }
        else
        {
            AdicionarAdvertencia(r, "PDF/UA", "DisplayDocTitle",
                NormaOrigem.EN301549,
                "A preferência DisplayDocTitle não está ativa. " +
                "O visualizador pode exibir o nome do ficheiro em vez do título acessível.",
                "Ative: PdfViewerPreferences.SetDisplayDocTitle(true) ou no Acrobat: " +
                "Ficheiro > Propriedades > Vista Inicial > Mostrar: Título do Documento.");
        }
    }

    private void VerificarClausula12Documentacao(AccessibilityAnalysisResult r)
    {
        // 12.1.1 – Accessibility and compatibility features (documentação do produto)
        // 12.1.2 – Accessible documentation
        AdicionarAdvertencia(r, "12.1.2", "Documentação Acessível",
            NormaOrigem.EN301549,
            "[EN 301 549 Cláusula 12.1.2] A documentação fornecida com o produto/serviço " +
            "deve estar disponível num formato acessível.",
            "Se este PDF é documentação de um produto ou serviço, assegure que existe versão " +
            "acessível (HTML acessível, PDF/UA ou texto simples) disponível.");
    }

    // ── Conformidade EN 301 549 ────────────────────────────────────────────────
    private void CalcularConformidadeEN301549(AccessibilityAnalysisResult r)
    {
        // EN 301 549 cláusula 9 é mapeada 1:1 com WCAG 2.1 AA para conteúdo web/PDF
        // A pontuação EN 301 549 combina os critérios WCAG com os específicos da norma
        var violacoesEn = r.Violacoes.Where(v =>
            v.Norma == NormaOrigem.EN301549 ||
            v.Norma == NormaOrigem.Todas ||
            v.Norma == NormaOrigem.WCAG21).ToList();

        int total     = r.Violacoes.Count + r.Aprovacoes.Count;
        int reprovados = violacoesEn.Count;
        int aprovados  = Math.Max(0, total - reprovados);

        double score = total > 0
            ? Math.Max(0, ((double)aprovados / total) * 100.0)
            : 100.0;

        r.ConformidadeEN301549 = new ResultadoConformidadeNorma
        {
            Norma            = "EN 301 549 v3.2.1",
            NivelAlvo        = "AA",
            Pontuacao        = score,
            TotalVerificados = total,
            Aprovados        = aprovados,
            Reprovados       = reprovados,
            Conforme         = score >= 80 && !violacoesEn.Any(v => v.Gravidade == GravidadeViolacao.Critica)
        };
    }

    // ── Pontuação Global ──────────────────────────────────────────────────────
    private double CalcularPontuacaoGlobal(AccessibilityAnalysisResult r)
    {
        // Média ponderada: EN 301 549 (40%) + WCAG 2.1 (40%) + WCAG 2.2 (20%)
        return (r.ConformidadeEN301549.Pontuacao * 0.40)
             + (r.ConformidadeWCAG21.Pontuacao   * 0.40)
             + (r.ConformidadeWCAG22.Pontuacao   * 0.20);
    }

    // ── Metadadas ─────────────────────────────────────────────────────────────
    private MetadatasPdf ExtrairMetadadas(PdfDocument doc)
    {
        var info     = doc.GetDocumentInfo();
        var catalogo = doc.GetCatalog().GetPdfObject();
        var viewerPrefs = catalogo.GetAsDictionary(PdfName.ViewerPreferences);

        return new MetadatasPdf
        {
            Titulo          = info.GetTitle()    ?? string.Empty,
            Autor           = info.GetAuthor()   ?? string.Empty,
            Assunto         = info.GetSubject()  ?? string.Empty,
            PalavrasChave   = info.GetKeywords() ?? string.Empty,
            CriadorSoftware = info.GetCreator()  ?? string.Empty,
            Idioma          = catalogo.GetAsString(PdfName.Lang)?.ToString() ?? string.Empty,
            ETagged         = doc.IsTagged(),
            VersaoPdf       = doc.GetPdfVersion().ToString(),
            DisplayDocTitle = viewerPrefs?.GetAsBoolean(new PdfName("DisplayDocTitle"))?.GetValue() ?? false
        };
    }

    private void ExtrairEstatisticasFormularios(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        var acroForm = doc.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.AcroForm);
        if (acroForm == null) return;

        r.Metadatas.EFormulario = true;
        var campos = acroForm.GetAsArray(PdfName.Fields);
        int totalCampos = campos?.Size() ?? 0;
        int semRotulo   = 0;

        if (campos != null)
        {
            for (int i = 0; i < campos.Size(); i++)
            {
                var campo = campos.GetAsDictionary(i);
                if (campo == null) continue;
                var tu  = campo.GetAsString(PdfName.TU);
                var alt = campo.GetAsString(new PdfName("Alt"));
                if (tu == null && alt == null) semRotulo++;
            }
        }

        r.Estatisticas.TotalFormularios             = totalCampos;
        r.Estatisticas.CamposFormularioSemRotulo     = semRotulo;
    }

    // ── Helpers privados ──────────────────────────────────────────────────────
    private void AdicionarViolacao(
        AccessibilityAnalysisResult r,
        string codigo, string nome,
        NivelConformidade nivel, GravidadeViolacao gravidade, NormaOrigem norma,
        string principio, string descricao, string recomendacao, string url = "")
    {
        r.Violacoes.Add(new ViolacaoAcessibilidade
        {
            CodigoCriterio      = codigo,
            CodigoEN301549      = MapaCriterios.ObterEN301549(codigo),
            NomeCriterio        = nome,
            Nivel               = nivel,
            Gravidade           = gravidade,
            Norma               = norma,
            Principio           = principio,
            Descricao           = descricao,
            Recomendacao        = recomendacao,
            UrlReferencia       = url
        });
    }

    private void AdicionarAdvertencia(
        AccessibilityAnalysisResult r,
        string codigo, string nome, NormaOrigem norma,
        string descricao, string recomendacao)
    {
        r.Advertencias.Add(new AdvertenciaAcessibilidade
        {
            CodigoCriterio = codigo,
            CodigoEN301549 = MapaCriterios.ObterEN301549(codigo),
            NomeCriterio   = nome,
            Norma          = norma,
            Descricao      = descricao,
            Recomendacao   = recomendacao
        });
    }
}
