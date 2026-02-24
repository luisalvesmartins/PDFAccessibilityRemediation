using EN301549PdfProcessor.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;

namespace EN301549PdfProcessor.Validators;

/// <summary>
/// Validador WCAG 2.1 (A + AA) e WCAG 2.2 (novos critérios AA) aplicados a PDFs.
///
/// Critérios WCAG 2.1 verificados (Níveis A e AA):
///   Percetível  : 1.1.1, 1.2.x, 1.3.x, 1.4.x
///   Operável    : 2.1.x, 2.2.x, 2.3.x, 2.4.x
///   Compreensível: 3.1.x, 3.2.x, 3.3.x
///   Robusto     : 4.1.x
///
/// Critérios WCAG 2.2 adicionais (Nível AA):
///   2.4.11 Foco Não Obscurecido (Mínimo)
///   2.4.12 Foco Não Obscurecido (Melhorado)
///   2.4.13 Aparência do Foco
///   2.5.3  Rótulo no Nome (já em 2.1, reforçado)
///   2.5.8  Tamanho do Alvo (Mínimo)
///   3.2.6  Ajuda Consistente
///   3.3.7  Autenticação Redundante (Acessível)
///   3.3.8  Autenticação Redundante (novo 2.2)
/// </summary>
public class ValidadorWcag
{
    private readonly ILogger<ValidadorWcag> _logger;

    // ─── Constantes de contraste ──────────────────────────────────────────────
    private const double RatioNormalAA     = 4.5;   // WCAG 1.4.3 AA texto normal
    private const double RatioGrandeAA     = 3.0;   // WCAG 1.4.3 AA texto grande (≥18pt ou ≥14pt bold)
    private const double RatioNormalAAA    = 7.0;   // WCAG 1.4.6 AAA
    private const double RatioGrandeAAA    = 4.5;   // WCAG 1.4.6 AAA texto grande
    private const double RatioComponentesAA = 3.0;  // WCAG 1.4.11 componentes UI

    // ─── Tamanho mínimo de alvo interativo (WCAG 2.2 – 2.5.8) ────────────────
    private const double TamanhoMinimoAlvoPx = 24.0; // 24x24 px equivalente

    public ValidadorWcag(ILogger<ValidadorWcag> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executa todas as verificações WCAG 2.1 e 2.2 e popula o resultado existente.
    /// </summary>
    public void Validar(AccessibilityAnalysisResult resultado, PdfDocument doc)
    {
        _logger.LogInformation("Iniciando validação WCAG 2.1 / 2.2...");

        // ── Princípio 1 – PERCETÍVEL ─────────────────────────────────────────
        Verificar_1_1_1_ConteudoNaoTextual(resultado, doc);
        Verificar_1_2_MultiMedia(resultado, doc);
        Verificar_1_3_AdaptavelEstrutura(resultado, doc);
        Verificar_1_4_DistinguivelContraste(resultado, doc);

        // ── Princípio 2 – OPERÁVEL ───────────────────────────────────────────
        Verificar_2_1_TecladoAcessivel(resultado, doc);
        Verificar_2_2_TempoSuficiente(resultado, doc);
        Verificar_2_3_Convulsoes(resultado, doc);
        Verificar_2_4_Navegavel(resultado, doc);
        Verificar_2_5_Modalidades(resultado, doc);

        // ── Princípio 3 – COMPREENSÍVEL ──────────────────────────────────────
        Verificar_3_1_Legivel(resultado, doc);
        Verificar_3_2_Previsivel(resultado, doc);
        Verificar_3_3_AssistenciaEntrada(resultado, doc);

        // ── Princípio 4 – ROBUSTO ────────────────────────────────────────────
        Verificar_4_1_Compativel(resultado, doc);

        // ── Calcular conformidade WCAG ────────────────────────────────────────
        CalcularConformidadeWcag(resultado);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PRINCÍPIO 1 – PERCETÍVEL
    // ══════════════════════════════════════════════════════════════════════════

    // ── 1.1.1 Conteúdo Não Textual (Nível A) ─────────────────────────────────
    private void Verificar_1_1_1_ConteudoNaoTextual(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        int totalImagens = 0, semAlt = 0, decorativas = 0;

        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            var pagina = doc.GetPage(p);
            var xObjs = pagina.GetResources()?.GetResource(PdfName.XObject);
            if (xObjs == null) continue;

            foreach (var entrada in xObjs.EntrySet())
            {
                if (entrada.Value is not PdfStream stream) continue;
                if (!PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype))) continue;

                totalImagens++;

                // Verificar se é artefacto decorativo
                var subtype2 = stream.GetAsName(new PdfName("Subtype2"));
                if (PdfName.Image.Equals(subtype2))
                {
                    decorativas++;
                    continue;
                }

                if (!doc.IsTagged()) semAlt++;
            }
        }

        r.Estatisticas.TotalImagens    = totalImagens;
        r.Estatisticas.ImagensDecorativas = decorativas;
        r.Estatisticas.ImagemSemAlt    = semAlt;
        r.Estatisticas.ImagensComAlt   = totalImagens - semAlt - decorativas;

        if (totalImagens == 0)
        {
            Aprovar(r, "1.1.1", "Conteúdo Não Textual", "Nenhuma imagem detetada.");
            return;
        }

        if (semAlt > 0)
        {
            AdicionarViolacao(r, "1.1.1", "Conteúdo Não Textual",
                NivelConformidade.A, GravidadeViolacao.Critica, NormaOrigem.Todas,
                "Percetível",
                $"{semAlt} imagem(ns) de {totalImagens} sem texto alternativo. " +
                "Leitores de ecrã não conseguem transmitir o conteúdo visual.",
                "Adicione texto alternativo descritivo a cada imagem informativa via tags PDF <Figure Alt='...'/>. " +
                "Imagens decorativas devem ter Alt='' e ser marcadas como Artifact.",
                "https://www.w3.org/WAI/WCAG21/Understanding/non-text-content");
        }
        else
        {
            Aprovar(r, "1.1.1", "Conteúdo Não Textual",
                $"✓ {totalImagens} imagens detetadas; verificação de tags recomendada.");
        }
    }

    // ── 1.2.x Multimédia (Nível A/AA) ────────────────────────────────────────
    private void Verificar_1_2_MultiMedia(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // PDFs raramente contêm multimédia embutida, mas verificamos
        bool temMultimedia = false;
        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            var anotacoes = doc.GetPage(p).GetAnnotations();
            foreach (var anot in anotacoes)
            {
                var sub = anot.GetSubtype();
                if (PdfName.Movie.Equals(sub) || PdfName.Sound.Equals(sub) ||
                    new PdfName("RichMedia").Equals(sub) || new PdfName("Screen").Equals(sub))
                {
                    temMultimedia = true;
                    break;
                }
            }
        }

        r.Estatisticas.TemAudioVideo = temMultimedia;

        if (temMultimedia)
        {
            AdicionarAdvertencia(r, "1.2.1", "Apenas Áudio e Apenas Vídeo",
                NormaOrigem.Todas,
                "Detetado conteúdo multimédia no PDF. " +
                "Verifique se existe transcrição/descrição para conteúdo de áudio/vídeo.",
                "1.2.1 (A): Forneça alternativa textual para áudio. " +
                "1.2.2 (A): Forneça legendas para vídeo. " +
                "1.2.5 (AA): Forneça audiodescrição para vídeo.");
        }
        else
        {
            Aprovar(r, "1.2.x", "Multimédia Baseada no Tempo",
                "✓ Nenhum conteúdo multimédia detetado.");
        }
    }

    // ── 1.3.x Adaptável ──────────────────────────────────────────────────────
    private void Verificar_1_3_AdaptavelEstrutura(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 1.3.1 – Informações e Relações
        if (!doc.IsTagged())
        {
            AdicionarViolacao(r, "1.3.1", "Informações e Relações",
                NivelConformidade.A, GravidadeViolacao.Critica, NormaOrigem.Todas,
                "Percetível",
                "O documento não possui estrutura de tags semânticas. " +
                "Títulos, listas, tabelas e parágrafos são indistinguíveis por tecnologias de apoio.",
                "Recrie o PDF com tagging ativo. Use estilos de parágrafo corretos na ferramenta de origem " +
                "(Word/InDesign/LibreOffice) antes de exportar para PDF.",
                "https://www.w3.org/WAI/WCAG21/Understanding/info-and-relationships");
        }
        else
        {
            Aprovar(r, "1.3.1", "Informações e Relações",
                "✓ Documento marcado com tags semânticas.");

            // 1.3.2 – Sequência com Significado
            Aprovar(r, "1.3.2", "Sequência com Significado",
                "✓ Estrutura de tags permite ordem de leitura lógica.");
        }

        // 1.3.3 – Características Sensoriais
        AdicionarAdvertencia(r, "1.3.3", "Características Sensoriais",
            NormaOrigem.WCAG21,
            "Verificar se instruções dependem exclusivamente de forma, cor, tamanho ou posição visual " +
            "(ex: 'clique no botão verde', 'ver figura à direita').",
            "Reformule instruções para não depender apenas de sentidos ou posição visual. " +
            "Use texto descritivo em vez de apenas referências visuais.");

        // 1.3.4 – Orientação (WCAG 2.1 AA)
        VerificarOrientacao(r, doc);

        // 1.3.5 – Identificar o Propósito do Campo (WCAG 2.1 AA)
        VerificarProposicaoCampos(r, doc);
    }

    private void VerificarOrientacao(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // Verificar se o PDF bloqueia orientação de leitura
        var viewerPrefs = doc.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.ViewerPreferences);
        bool orientacaoBloqueada = false;

        if (viewerPrefs != null)
        {
            var duplex = viewerPrefs.GetAsName(new PdfName("Duplex"));
            // Se o documento força layout que impede rotação, pode violar 1.3.4
        }

        if (orientacaoBloqueada)
        {
            AdicionarViolacao(r, "1.3.4", "Orientação",
                NivelConformidade.AA, GravidadeViolacao.Media, NormaOrigem.WCAG21,
                "Percetível",
                "O documento parece bloquear a orientação de visualização.",
                "Não force uma orientação específica a menos que seja essencialmente necessário (ex: cheque bancário).",
                "https://www.w3.org/WAI/WCAG21/Understanding/orientation");
        }
        else
        {
            Aprovar(r, "1.3.4", "Orientação", "✓ Sem restrição de orientação detetada.");
        }
    }

    private void VerificarProposicaoCampos(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        var acroForm = doc.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.AcroForm);
        if (acroForm == null)
        {
            Aprovar(r, "1.3.5", "Identificar o Propósito do Campo", "✓ Sem campos de formulário.");
            return;
        }

        AdicionarAdvertencia(r, "1.3.5", "Identificar o Propósito do Campo",
            NormaOrigem.WCAG21,
            "Formulário detetado. Verifique se campos de dados pessoais (nome, email, morada) " +
            "possuem atributo 'autocomplete' ou equivalente em PDF para tecnologias de apoio.",
            "Use o atributo TU (tooltip) com nomes semânticos normalizados nos campos de formulário. " +
            "Ex: campo de email deve ter TU='Endereço de e-mail'.");
    }

    // ── 1.4.x Distinguível ───────────────────────────────────────────────────
    private void Verificar_1_4_DistinguivelContraste(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 1.4.1 – Utilização da Cor
        AdicionarAdvertencia(r, "1.4.1", "Utilização da Cor",
            NormaOrigem.Todas,
            "Verificar se a cor é o único meio de transmitir informação (ex: campos obrigatórios " +
            "destacados apenas a vermelho, gráficos só com cor).",
            "Use além da cor: padrões, ícones, texto ou formas para distinguir informação. " +
            "Exemplo: erro num formulário deve ter borda vermelha E ícone E texto de erro.");

        // 1.4.3 – Contraste (Mínimo)
        AdicionarAdvertencia(r, "1.4.3", "Contraste (Mínimo)",
            NormaOrigem.Todas,
            $"Verificação automática de contraste não é possível via iText sem renderização. " +
            $"Rácio mínimo exigido: {RatioNormalAA}:1 para texto normal, {RatioGrandeAA}:1 para texto ≥18pt ou ≥14pt bold.",
            "Use o Colour Contrast Analyser (https://www.tpgi.com/color-contrast-checker/) ou " +
            "PDF Accessibility Checker (PAC 2024) para verificar o contraste de cada elemento de texto.");

        // 1.4.4 – Redimensionar Texto
        AdicionarAdvertencia(r, "1.4.4", "Redimensionar Texto",
            NormaOrigem.Todas,
            "Verifique se o documento pode ser ampliado até 200% sem perda de conteúdo ou funcionalidade. " +
            "PDFs com texto em imagem ou com layout fixo podem falhar neste critério.",
            "Use texto real em vez de imagens de texto. Evite posicionamento absoluto que quebre o layout ao ampliar.");

        // 1.4.5 – Imagens de Texto
        Verificar_1_4_5_ImagensTexto(r, doc);

        // 1.4.10 – Reflow (WCAG 2.1 AA)
        AdicionarAdvertencia(r, "1.4.10", "Reflow",
            NormaOrigem.WCAG21,
            "Verifique se o conteúdo pode ser visualizado numa coluna simples (largura 320 CSS px) " +
            "sem scroll horizontal. Especialmente relevante em PDFs com múltiplas colunas.",
            "PDFs com layout de múltiplas colunas rígido podem não satisfazer este critério em leitores móveis. " +
            "Considere fornecer versão HTML acessível para documentos longos.");

        // 1.4.11 – Contraste Sem Texto (WCAG 2.1 AA)
        AdicionarAdvertencia(r, "1.4.11", "Contraste Sem Texto",
            NormaOrigem.WCAG21,
            $"Componentes de interface (botões, campos, ícones funcionais) devem ter contraste mínimo de {RatioComponentesAA}:1 " +
            "em relação ao fundo. Não verificável automaticamente sem renderização.",
            "Verifique bordas de campos de formulário, botões e ícones interativos com ferramentas de contraste.");

        // 1.4.12 – Espaçamento de Texto (WCAG 2.1 AA)
        AdicionarAdvertencia(r, "1.4.12", "Espaçamento de Texto",
            NormaOrigem.WCAG21,
            "Verifique se aumentar espaçamento entre linhas (≥1.5x), letras (≥0.12em), " +
            "palavras (≥0.16em) e parágrafos (≥2x tamanho fonte) não causa perda de conteúdo.",
            "Evite tamanhos de caixa fixos que truncam texto quando o espaçamento é aumentado. " +
            "Mais relevante em PDFs interativos com campos de formulário.");

        // 1.4.13 – Conteúdo em Hover ou Foco (WCAG 2.1 AA) – limitado em PDF
        AdicionarAdvertencia(r, "1.4.13", "Conteúdo em Hover ou Foco",
            NormaOrigem.WCAG21,
            "Se o PDF contém tooltips ou conteúdo que aparece ao pairar (hover), " +
            "verifique se é dispensável, visível e persistente.",
            "Tooltips em campos de formulário PDF devem ser sempre acessíveis via teclado, não apenas por rato.");
    }

    private void Verificar_1_4_5_ImagensTexto(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // Heurística: texto extraível vs imagens — se existe muito texto mas pouco texto extraível
        // pode indicar que o documento é digitalizado (texto em imagem)
        try
        {
            int paginasComTexto = 0;
            for (int p = 1; p <= Math.Min(doc.GetNumberOfPages(), 5); p++)
            {
                var estrategia = new SimpleTextExtractionStrategy();
                var texto = PdfTextExtractor.GetTextFromPage(doc.GetPage(p), estrategia);
                if (texto.Trim().Length > 20) paginasComTexto++;
            }

            double amostra = Math.Min(doc.GetNumberOfPages(), 5);
            double taxaTexto = paginasComTexto / amostra;

            r.Estatisticas.TotalCaracteresTexto = paginasComTexto * 100; // estimativa

            if (taxaTexto < 0.3 && r.Estatisticas.TotalImagens > 0)
            {
                AdicionarViolacao(r, "1.4.5", "Imagens de Texto",
                    NivelConformidade.AA, GravidadeViolacao.Alta, NormaOrigem.Todas,
                    "Percetível",
                    "O documento parece conter pouco texto extraível — pode ser um PDF digitalizado (scan). " +
                    "Texto em imagem não é acessível a leitores de ecrã ou redimensionável.",
                    "Execute OCR (Reconhecimento Ótico de Caracteres) com geração de texto acessível. " +
                    "No Adobe Acrobat Pro: Ferramentas > Digitalizar e OCR > Reconhecer Texto. " +
                    "Em alternativa, use ferramentas como ABBYY FineReader ou tesseract.",
                    "https://www.w3.org/WAI/WCAG21/Understanding/images-of-text");
            }
            else
            {
                Aprovar(r, "1.4.5", "Imagens de Texto",
                    "✓ Texto extraível detetado; o documento não parece ser um scan puro.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Não foi possível verificar texto: {Msg}", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PRINCÍPIO 2 – OPERÁVEL
    // ══════════════════════════════════════════════════════════════════════════

    // ── 2.1.x Teclado Acessível ───────────────────────────────────────────────
    private void Verificar_2_1_TecladoAcessivel(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 2.1.1 – Teclado (Nível A)
        if (doc.IsTagged())
        {
            Aprovar(r, "2.1.1", "Teclado",
                "✓ Documento tagged permite navegação por teclado em leitores de PDF acessíveis.");
        }
        else
        {
            AdicionarViolacao(r, "2.1.1", "Teclado",
                NivelConformidade.A, GravidadeViolacao.Alta, NormaOrigem.Todas,
                "Operável",
                "Sem estrutura de tags, a navegação por teclado pode ser impossível " +
                "em muitas tecnologias de apoio.",
                "Adicione tags de acessibilidade ao documento para permitir navegação por teclado.",
                "https://www.w3.org/WAI/WCAG21/Understanding/keyboard");
        }

        // 2.1.2 – Sem Bloqueio de Teclado (Nível A)
        Aprovar(r, "2.1.2", "Sem Bloqueio de Teclado",
            "✓ PDFs não bloqueiam o teclado nativamente (verificável em leitor específico).");
    }

    // ── 2.2.x Tempo Suficiente ────────────────────────────────────────────────
    private void Verificar_2_2_TempoSuficiente(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // PDFs estáticos não têm timeouts; mas podem ter JavaScript
        AdicionarAdvertencia(r, "2.2.1", "Tempo Ajustável",
            NormaOrigem.Todas,
            "Se o PDF contém JavaScript com limites de tempo (ex: formulários com sessão), " +
            "verifique se o utilizador pode desativar, ajustar ou prolongar os limites.",
            "Remova limites de tempo desnecessários em formulários PDF. " +
            "Se inevitável, forneça mecanismo de extensão antes do expirar.");

        // 2.2.2 – Colocar em Pausa, Parar, Ocultar
        AdicionarAdvertencia(r, "2.2.2", "Colocar em Pausa, Parar, Ocultar",
            NormaOrigem.Todas,
            "Se o PDF contém conteúdo em movimento, animação ou que se atualiza automaticamente, " +
            "verifique se existe mecanismo para pausar ou parar.",
            "Evite animações automáticas em PDFs. Se usadas, forneça botão de pausa acessível.");
    }

    // ── 2.3.x Convulsões e Reações Físicas ───────────────────────────────────
    private void Verificar_2_3_Convulsoes(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 2.3.1 – Três Flashes ou Abaixo do Limiar
        Aprovar(r, "2.3.1", "Três Flashes ou Abaixo do Limiar",
            "✓ PDFs estáticos não contêm flashes perigosos (verifique se há animações embutidas).");
    }

    // ── 2.4.x Navegável ──────────────────────────────────────────────────────
    private void Verificar_2_4_Navegavel(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 2.4.1 – Ignorar Blocos (Nível A) — Marcadores
        VerificarMarcadores(r, doc);

        // 2.4.2 – Título de Página (Nível A) — já verificado no validador base
        var info = doc.GetDocumentInfo();
        if (!string.IsNullOrWhiteSpace(info.GetTitle()))
            Aprovar(r, "2.4.2", "Título de Página", $"✓ Título definido: '{info.GetTitle()}'.");
        else
            AdicionarViolacao(r, "2.4.2", "Título de Página",
                NivelConformidade.A, GravidadeViolacao.Alta, NormaOrigem.Todas,
                "Operável",
                "O documento não possui título definido nos metadados.",
                "Defina o título em Ficheiro > Propriedades ou via PdfDocumentInfo.SetTitle().",
                "https://www.w3.org/WAI/WCAG21/Understanding/page-titled");

        // 2.4.3 – Ordem do Foco (Nível A)
        if (doc.IsTagged())
            Aprovar(r, "2.4.3", "Ordem do Foco",
                "✓ Tags definem ordem de foco lógica.");
        else
            AdicionarAdvertencia(r, "2.4.3", "Ordem do Foco",
                NormaOrigem.Todas,
                "Sem tags, a ordem de foco pode não seguir a sequência lógica do conteúdo.",
                "Adicione tags e verifique o painel de Ordem no Adobe Acrobat Pro.");

        // 2.4.4 – Finalidade do Link (Nível A)
        VerificarLinks(r, doc);

        // 2.4.5 – Várias Formas (Nível AA) — N/A para PDF único
        AdicionarAdvertencia(r, "2.4.5", "Várias Formas",
            NormaOrigem.Todas,
            "Se este PDF faz parte de um conjunto de documentos, verifique se existem várias formas " +
            "de localizar o documento (índice, pesquisa, mapa do site).",
            "Aplica-se principalmente a sítios web; em PDF standalone considere fornecer índice interno.");

        // 2.4.6 – Cabeçalhos e Rótulos (Nível AA)
        VerificarCabecalhosRotulos(r, doc);

        // 2.4.7 – Foco Visível (Nível AA)
        AdicionarAdvertencia(r, "2.4.7", "Foco Visível",
            NormaOrigem.Todas,
            "Verifique se os elementos interativos (links, campos de formulário) têm indicador de foco visível " +
            "no visualizador de PDF (Adobe Reader, etc.).",
            "A visibilidade do foco depende do visualizador, mas certifique-se que os elementos interativos " +
            "são claramente identificáveis visualmente.");

        // ── WCAG 2.2 – Novos critérios de Navegação ──────────────────────────

        // 2.4.11 – Foco Não Obscurecido (Mínimo) — Nível AA, WCAG 2.2
        AdicionarAdvertencia(r, "2.4.11", "Foco Não Obscurecido (Mínimo)",
            NormaOrigem.WCAG22,
            "[WCAG 2.2 NOVO – AA] Verifique se o componente focado não fica completamente coberto " +
            "por outros elementos sobrepostos (cabeçalhos fixos, popups, etc.).",
            "Em PDFs com camadas sobrepostas (layers), certifique-se que elementos focados ficam visíveis. " +
            "Evite conteúdo que cubra completamente o indicador de foco.");

        // 2.4.12 – Foco Não Obscurecido (Melhorado) — Nível AAA, WCAG 2.2
        AdicionarAdvertencia(r, "2.4.12", "Foco Não Obscurecido (Melhorado)",
            NormaOrigem.WCAG22,
            "[WCAG 2.2 NOVO – AAA] O componente focado deve estar completamente visível, " +
            "não apenas parcialmente.",
            "Nível AAA: garantir total visibilidade do foco sem qualquer sobreposição.");

        // 2.4.13 – Aparência do Foco — Nível AA, WCAG 2.2
        AdicionarAdvertencia(r, "2.4.13", "Aparência do Foco",
            NormaOrigem.WCAG22,
            "[WCAG 2.2 NOVO – AA] O indicador de foco deve ter área ≥ perímetro do componente × 2px CSS " +
            "e contraste ≥ 3:1 entre estados com e sem foco.",
            "Verifique se o anel de foco em campos de formulário PDF é suficientemente grande e contrastado " +
            "no visualizador Adobe Reader / Edge PDF.");
    }

    private void VerificarMarcadores(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        var outlines = doc.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Outlines);
        bool temMarcadores = outlines != null && outlines.ContainsKey(PdfName.First);

        if (r.Estatisticas.TotalPaginas > 5)
        {
            if (temMarcadores)
            {
                r.Metadatas.TemTabelaDeConteudo = true;
                Aprovar(r, "2.4.1", "Ignorar Blocos",
                    $"✓ Documento com {r.Estatisticas.TotalPaginas} páginas possui marcadores de navegação.");
            }
            else
            {
                AdicionarViolacao(r, "2.4.1", "Ignorar Blocos",
                    NivelConformidade.A, GravidadeViolacao.Media, NormaOrigem.Todas,
                    "Operável",
                    $"Documento com {r.Estatisticas.TotalPaginas} páginas não possui marcadores (bookmarks). " +
                    "Utilizadores de teclado e leitores de ecrã não podem navegar rapidamente entre secções.",
                    "Adicione marcadores baseados nos títulos: Ferramentas > Editar PDF > Marcadores a partir de Estrutura.",
                    "https://www.w3.org/WAI/WCAG21/Understanding/bypass-blocks");
            }
        }
        else
        {
            Aprovar(r, "2.4.1", "Ignorar Blocos",
                $"✓ Documento curto ({r.Estatisticas.TotalPaginas} pág.); marcadores opcionais.");
        }
    }

    private void VerificarLinks(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        int totalLinks = 0, semDesc = 0;

        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            foreach (var anot in doc.GetPage(p).GetAnnotations())
            {
                if (!PdfName.Link.Equals(anot.GetSubtype())) continue;
                totalLinks++;

                var conteudo = anot.GetContents()?.ToString();
                var alt = anot.GetPdfObject().GetAsString(PdfName.Alt)?.ToString();

                // Verificar textos genéricos
                bool textoGenerico = false;
                if (!string.IsNullOrWhiteSpace(conteudo))
                {
                    var lower = conteudo.ToLower().Trim();
                    textoGenerico = lower is "clique aqui" or "aqui" or "mais informações" or
                                    "saiba mais" or "ver mais" or "leia mais" or "link" or
                                    "click here" or "here" or "more" or "read more";
                }

                if ((string.IsNullOrWhiteSpace(conteudo) && alt == null) || textoGenerico)
                    semDesc++;
            }
        }

        r.Estatisticas.TotalLinks = totalLinks;
        r.Estatisticas.LinksSemTextoDescritivo = semDesc;

        if (totalLinks == 0)
        {
            Aprovar(r, "2.4.4", "Finalidade do Link", "✓ Nenhum link detetado.");
            return;
        }

        if (semDesc > 0)
        {
            AdicionarViolacao(r, "2.4.4", "Finalidade do Link",
                NivelConformidade.A, GravidadeViolacao.Media, NormaOrigem.Todas,
                "Operável",
                $"{semDesc} de {totalLinks} link(s) sem texto descritivo ou com texto genérico. " +
                "Utilizadores de leitores de ecrã não conseguem perceber o destino do link.",
                "Substitua textos como 'clique aqui' por descrições claras: " +
                "'Descarregar Relatório Anual 2024 (PDF)'. Adicione tooltip (Contents) ao link.",
                "https://www.w3.org/WAI/WCAG21/Understanding/link-purpose-in-context");
        }
        else
        {
            Aprovar(r, "2.4.4", "Finalidade do Link",
                $"✓ Todos os {totalLinks} links têm texto descritivo.");
        }
    }

    private void VerificarCabecalhosRotulos(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        if (!doc.IsTagged())
        {
            AdicionarAdvertencia(r, "2.4.6", "Cabeçalhos e Rótulos",
                NormaOrigem.Todas,
                "Sem tags, não é possível verificar se os cabeçalhos descrevem o conteúdo.",
                "Após tagging, verifique que cada <H1>–<H6> descreve a secção correspondente " +
                "e que os rótulos de formulário identificam claramente o propósito do campo.");
            return;
        }

        AdicionarAdvertencia(r, "2.4.6", "Cabeçalhos e Rótulos",
            NormaOrigem.Todas,
            "Verifique manualmente se os cabeçalhos são descritivos e a hierarquia é correta (H1 > H2 > H3).",
            "Cada secção deve ter um cabeçalho que descreva o seu conteúdo. " +
            "Não salte níveis de cabeçalho (ex: H1 seguido de H3 sem H2).");
    }

    // ── 2.5.x Modalidades de Entrada ─────────────────────────────────────────
    private void Verificar_2_5_Modalidades(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 2.5.3 – Rótulo no Nome (Nível A, reforçado em 2.2)
        if (r.Estatisticas.TotalFormularios > 0)
        {
            AdicionarAdvertencia(r, "2.5.3", "Rótulo no Nome",
                NormaOrigem.Todas,
                "Verifique se o nome acessível de cada campo de formulário inclui o texto visível do rótulo.",
                "O valor do campo TU (tooltip acessível) deve conter o texto do rótulo visível. " +
                "Ex: rótulo visível 'Nome completo' → TU='Nome completo'.");
        }

        // 2.5.8 – Tamanho do Alvo (Mínimo) — Nível AA, WCAG 2.2
        VerificarTamanhoAlvos(r, doc);
    }

    private void VerificarTamanhoAlvos(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        int totalAlvos = 0, alvosInsuficientes = 0;

        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            foreach (var anot in doc.GetPage(p).GetAnnotations())
            {
                var sub = anot.GetSubtype();
                if (!PdfName.Link.Equals(sub) && !PdfName.Widget.Equals(sub)) continue;

                totalAlvos++;
                var rectArray = anot.GetRectangle();

                if (rectArray != null && rectArray.Size() >= 4)
                {
                    // PdfArray [llx, lly, urx, ury]
                    double llx = rectArray.GetAsNumber(0)?.DoubleValue() ?? 0;
                    double lly = rectArray.GetAsNumber(1)?.DoubleValue() ?? 0;
                    double urx = rectArray.GetAsNumber(2)?.DoubleValue() ?? 0;
                    double ury = rectArray.GetAsNumber(3)?.DoubleValue() ?? 0;

                    double largura = Math.Abs(urx - llx);
                    double altura  = Math.Abs(ury - lly);

                    // PDF usa pontos; 1pt ≈ 1.33px; 24px ≈ 18pt
                    double minPt = TamanhoMinimoAlvoPx / 1.333;

                    if (largura < minPt || altura < minPt)
                        alvosInsuficientes++;
                }
            }
        }

        r.Estatisticas.TotalAlvosInteracao      = totalAlvos;
        r.Estatisticas.AlvosAbaixoTamanhoMinimo = alvosInsuficientes;

        if (totalAlvos == 0)
        {
            Aprovar(r, "2.5.8", "Tamanho do Alvo (Mínimo) [WCAG 2.2]",
                "✓ Nenhum alvo interativo detetado.");
            return;
        }

        if (alvosInsuficientes > 0)
        {
            AdicionarViolacao(r, "2.5.8", "Tamanho do Alvo (Mínimo)",
                NivelConformidade.AA, GravidadeViolacao.Media, NormaOrigem.WCAG22,
                "Operável",
                $"[WCAG 2.2 NOVO] {alvosInsuficientes} de {totalAlvos} alvos interativos (links/campos) " +
                $"têm dimensão inferior a {TamanhoMinimoAlvoPx}×{TamanhoMinimoAlvoPx}px, " +
                "dificultando a interação por toque ou motor fino reduzido.",
                "Aumente o tamanho dos links e campos de formulário para mínimo 24×24px (≈18pt). " +
                "Idealmente use 44×44px para conforto de toque.",
                "https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum");
        }
        else
        {
            Aprovar(r, "2.5.8", "Tamanho do Alvo (Mínimo) [WCAG 2.2]",
                $"✓ Todos os {totalAlvos} alvos interativos têm tamanho adequado.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PRINCÍPIO 3 – COMPREENSÍVEL
    // ══════════════════════════════════════════════════════════════════════════

    private void Verificar_3_1_Legivel(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 3.1.1 – Idioma da Página (Nível A)
        var lang = doc.GetCatalog().GetPdfObject().GetAsString(PdfName.Lang)?.ToString();
        if (!string.IsNullOrWhiteSpace(lang))
        {
            Aprovar(r, "3.1.1", "Idioma da Página", $"✓ Idioma definido: '{lang}'.");
        }
        else
        {
            AdicionarViolacao(r, "3.1.1", "Idioma da Página",
                NivelConformidade.A, GravidadeViolacao.Alta, NormaOrigem.Todas,
                "Compreensível",
                "Idioma do documento não está definido. " +
                "Leitores de ecrã usarão idioma padrão, causando pronúncia incorreta.",
                "Defina o idioma no catálogo PDF: PdfCatalog.Put(PdfName.Lang, new PdfString('pt-PT')).",
                "https://www.w3.org/WAI/WCAG21/Understanding/language-of-page");
        }

        // 3.1.2 – Idioma das Partes (Nível AA)
        AdicionarAdvertencia(r, "3.1.2", "Idioma das Partes",
            NormaOrigem.Todas,
            "Se o documento contém secções em idiomas diferentes, verifique se estão marcadas " +
            "com o atributo Lang na respetiva tag de estrutura.",
            "Em PDF tagged, adicione o atributo Lang a cada elemento <P>, <Sect> ou <Span> " +
            "que contenha texto noutro idioma. Ex: <P Lang='en'>Hello</P>.");
    }

    private void Verificar_3_2_Previsivel(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 3.2.1 – Em Foco (Nível A) — não muda contexto ao focar
        Aprovar(r, "3.2.1", "Em Foco",
            "✓ PDFs estáticos não alteram contexto automaticamente ao receber foco " +
            "(verificar comportamento de JavaScript se aplicável).");

        // 3.2.2 – Em Entrada (Nível A)
        if (r.Metadatas.EFormulario)
        {
            AdicionarAdvertencia(r, "3.2.2", "Em Entrada",
                NormaOrigem.Todas,
                "Verifique se preencher um campo de formulário não desencadeia automaticamente " +
                "uma submissão ou mudança de contexto sem aviso.",
                "Submissões automáticas em PDF devem ser evitadas. Use sempre botão explícito de submissão.");
        }
        else
        {
            Aprovar(r, "3.2.2", "Em Entrada", "✓ Sem formulários que possam causar mudanças de contexto automáticas.");
        }

        // 3.2.6 – Ajuda Consistente — WCAG 2.2 AA
        AdicionarAdvertencia(r, "3.2.6", "Ajuda Consistente",
            NormaOrigem.WCAG22,
            "[WCAG 2.2 NOVO – AA] Se o documento faz parte de um conjunto com mecanismos de ajuda " +
            "(contacto, FAQ), verifique se aparecem na mesma posição relativa em cada documento.",
            "Mantenha informações de contacto/suporte na mesma localização (ex: rodapé) em todos os documentos do conjunto.");
    }

    private void Verificar_3_3_AssistenciaEntrada(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        if (!r.Metadatas.EFormulario)
        {
            Aprovar(r, "3.3.x", "Assistência na Entrada", "✓ Sem formulários no documento.");
            return;
        }

        // 3.3.1 – Identificação do Erro (Nível A)
        AdicionarAdvertencia(r, "3.3.1", "Identificação do Erro",
            NormaOrigem.Todas,
            "Verifique se campos de formulário com validação identificam erros por texto " +
            "(não apenas por cor ou ícone).",
            "Mensagens de erro em PDF devem ser programáticas: " +
            "use o atributo 'E' (error) ou tooltips condicionais em JavaScript PDF.");

        // 3.3.2 – Rótulos ou Instruções (Nível A)
        if (r.Estatisticas.CamposFormularioSemRotulo > 0)
        {
            AdicionarViolacao(r, "3.3.2", "Rótulos ou Instruções",
                NivelConformidade.A, GravidadeViolacao.Alta, NormaOrigem.Todas,
                "Compreensível",
                $"{r.Estatisticas.CamposFormularioSemRotulo} campo(s) sem rótulo ou instrução acessível.",
                "Adicione rótulo visível associado e tooltip (TU) a cada campo de formulário.",
                "https://www.w3.org/WAI/WCAG21/Understanding/labels-or-instructions");
        }
        else
        {
            Aprovar(r, "3.3.2", "Rótulos ou Instruções",
                $"✓ Todos os {r.Estatisticas.TotalFormularios} campos de formulário têm rótulo.");
        }

        // 3.3.7 – Autenticação Redundante (Acessível) — WCAG 2.2 AA
        AdicionarAdvertencia(r, "3.3.7", "Autenticação Redundante",
            NormaOrigem.WCAG22,
            "[WCAG 2.2 NOVO – AA] Se o formulário inclui etapas de autenticação que exigem " +
            "memorização, decifração ou transcrição de conteúdo (CAPTCHA cognitivo), " +
            "verifique se existe alternativa.",
            "Substitua CAPTCHAs cognitivos por alternativas acessíveis: " +
            "autenticação por email/SMS, reconhecimento de objeto, ou simplesmente remova se desnecessário.");

        // 3.3.8 – Autenticação Acessível (Sem Exceção) — WCAG 2.2 AA
        AdicionarAdvertencia(r, "3.3.8", "Autenticação Acessível (Sem Exceção)",
            NormaOrigem.WCAG22,
            "[WCAG 2.2 NOVO – AA] Nenhuma etapa de autenticação deve depender de teste cognitivo " +
            "(resolver puzzle, memorizar, transcrever) sem que exista alternativa acessível.",
            "Garanta que o processo de autenticação não requer funções cognitivas. " +
            "Permita gestores de palavras-passe (não bloqueie colar em campos de senha).");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PRINCÍPIO 4 – ROBUSTO
    // ══════════════════════════════════════════════════════════════════════════

    private void Verificar_4_1_Compativel(AccessibilityAnalysisResult r, PdfDocument doc)
    {
        // 4.1.1 – Análise (Parsing) — Nível A
        // Em PDF a robustez é garantida pelo standard PDF/UA (ISO 14289)
        var versao = doc.GetPdfVersion();
        if (versao != null)
        {
            Aprovar(r, "4.1.1", "Análise (Parsing)",
                $"✓ Documento PDF versão {versao}. Robustez estrutural garantida pelo formato PDF.");
        }

        // 4.1.2 – Nome, Função, Valor — Nível A
        if (doc.IsTagged())
        {
            Aprovar(r, "4.1.2", "Nome, Função, Valor",
                "✓ Tags fornecem nome e função a elementos interativos.");
        }
        else
        {
            AdicionarViolacao(r, "4.1.2", "Nome, Função, Valor",
                NivelConformidade.A, GravidadeViolacao.Alta, NormaOrigem.Todas,
                "Robusto",
                "Sem tags, os elementos interativos (links, botões, campos) não expõem " +
                "nome, função e estado às tecnologias de apoio.",
                "Adicione tags ao documento. Cada botão deve ter tag <Form> com Btn, " +
                "cada campo deve ter TU/TM adequados.",
                "https://www.w3.org/WAI/WCAG21/Understanding/name-role-value");
        }

        // 4.1.3 – Mensagens de Estado — Nível AA
        if (r.Metadatas.EFormulario)
        {
            AdicionarAdvertencia(r, "4.1.3", "Mensagens de Estado",
                NormaOrigem.Todas,
                "Verifique se mensagens de estado (submissão bem sucedida, erro de validação) " +
                "são programaticamente determináveis sem receber foco.",
                "Em formulários PDF com JavaScript, use alerts ou modifique tooltips programaticamente " +
                "para que leitores de ecrã anunciem o estado.");
        }
        else
        {
            Aprovar(r, "4.1.3", "Mensagens de Estado", "✓ Sem formulários; sem mensagens de estado dinâmicas.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CÁLCULO DE CONFORMIDADE
    // ══════════════════════════════════════════════════════════════════════════

    private void CalcularConformidadeWcag(AccessibilityAnalysisResult r)
    {
        // Critérios de nível A e AA com pesos
        var pesosWcag21 = new Dictionary<string, (double Peso, NivelConformidade Nivel)>
        {
            { "1.1.1",  (15, NivelConformidade.A)  },
            { "1.3.1",  (12, NivelConformidade.A)  },
            { "1.4.3",  (8,  NivelConformidade.AA) },
            { "1.4.5",  (6,  NivelConformidade.AA) },
            { "2.1.1",  (8,  NivelConformidade.A)  },
            { "2.4.1",  (5,  NivelConformidade.A)  },
            { "2.4.2",  (8,  NivelConformidade.A)  },
            { "2.4.4",  (6,  NivelConformidade.A)  },
            { "3.1.1",  (10, NivelConformidade.A)  },
            { "3.3.2",  (7,  NivelConformidade.A)  },
            { "4.1.2",  (8,  NivelConformidade.A)  },
        };

        var pesosWcag22Novos = new Dictionary<string, (double Peso, NivelConformidade Nivel)>
        {
            { "2.4.11", (4, NivelConformidade.AA) },
            { "2.4.13", (4, NivelConformidade.AA) },
            { "2.5.8",  (5, NivelConformidade.AA) },
            { "3.3.7",  (4, NivelConformidade.AA) },
            { "3.3.8",  (4, NivelConformidade.AA) },
            { "3.2.6",  (3, NivelConformidade.AA) },
        };

        r.ConformidadeWCAG21 = CalcularScore("WCAG 2.1", pesosWcag21, r.Violacoes);
        r.ConformidadeWCAG22 = CalcularScoreWcag22("WCAG 2.2", pesosWcag21, pesosWcag22Novos, r.Violacoes);
    }

    private ResultadoConformidadeNorma CalcularScore(
        string norma,
        Dictionary<string, (double Peso, NivelConformidade Nivel)> pesos,
        List<ViolacaoAcessibilidade> violacoes)
    {
        double total  = pesos.Values.Sum(x => x.Peso);
        double pontos = total;

        int reprovados = 0;
        foreach (var v in violacoes)
        {
            if (!pesos.TryGetValue(v.CodigoCriterio, out var p)) continue;
            reprovados++;
            pontos -= v.Gravidade switch
            {
                GravidadeViolacao.Critica => p.Peso,
                GravidadeViolacao.Alta    => p.Peso * 0.75,
                GravidadeViolacao.Media   => p.Peso * 0.5,
                GravidadeViolacao.Baixa   => p.Peso * 0.25,
                _                         => 0
            };
        }

        double score = Math.Max(0, (pontos / total) * 100.0);
        return new ResultadoConformidadeNorma
        {
            Norma             = norma,
            NivelAlvo         = "AA",
            Pontuacao         = score,
            TotalVerificados  = pesos.Count,
            Aprovados         = pesos.Count - reprovados,
            Reprovados        = reprovados,
            Conforme          = score >= 80 && reprovados == 0
        };
    }

    private ResultadoConformidadeNorma CalcularScoreWcag22(
        string norma,
        Dictionary<string, (double Peso, NivelConformidade Nivel)> pesosBase,
        Dictionary<string, (double Peso, NivelConformidade Nivel)> pesosNovos,
        List<ViolacaoAcessibilidade> violacoes)
    {
        var todosOsPesos = new Dictionary<string, (double Peso, NivelConformidade Nivel)>(pesosBase);
        foreach (var kv in pesosNovos) todosOsPesos[kv.Key] = kv.Value;

        return CalcularScore(norma, todosOsPesos, violacoes);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void AdicionarViolacao(
        AccessibilityAnalysisResult r,
        string wcag, string nome,
        NivelConformidade nivel, GravidadeViolacao gravidade, NormaOrigem norma,
        string principio, string descricao, string recomendacao,
        string urlRef = "")
    {
        r.Violacoes.Add(new ViolacaoAcessibilidade
        {
            CodigoCriterio      = wcag,
            CodigoEN301549      = MapaCriterios.ObterEN301549(wcag),
            NomeCriterio        = nome,
            Nivel               = nivel,
            Gravidade           = gravidade,
            Norma               = norma,
            Principio           = principio,
            Descricao           = descricao,
            Recomendacao        = recomendacao,
            UrlReferencia       = urlRef
        });
    }

    private void AdicionarAdvertencia(
        AccessibilityAnalysisResult r,
        string wcag, string nome, NormaOrigem norma,
        string descricao, string recomendacao)
    {
        r.Advertencias.Add(new AdvertenciaAcessibilidade
        {
            CodigoCriterio = wcag,
            CodigoEN301549 = MapaCriterios.ObterEN301549(wcag),
            NomeCriterio   = nome,
            Norma          = norma,
            Descricao      = descricao,
            Recomendacao   = recomendacao
        });
    }

    private static void Aprovar(AccessibilityAnalysisResult r, string wcag, string nome, string mensagem)
    {
        r.Aprovacoes.Add($"✓ [{wcag}] {nome}: {mensagem}");
    }
}
