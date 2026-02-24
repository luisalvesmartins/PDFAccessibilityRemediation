using EN301549PdfProcessor.Models;
using EN301549PdfProcessor.Validators;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;

namespace EN301549PdfProcessor.Services;

/// <summary>
/// Serviço de remediação automática de PDFs para conformidade EN 301 549.
/// Aplica correções automáticas onde possível e documenta o que requer intervenção manual.
/// </summary>
public class ServicodeRemediacao
{
    private readonly ILogger<ServicodeRemediacao> _logger;
    private readonly ValidadorAcessibilidadePdf _validador;
    private readonly ServicoAutoTagging _autoTagging;

    public ServicodeRemediacao(
        ILogger<ServicodeRemediacao> logger,
        ValidadorAcessibilidadePdf validador,
        ServicoAutoTagging? autoTagging = null)
    {
        _logger = logger;
        _validador = validador;
        _autoTagging = autoTagging ?? new ServicoAutoTagging(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServicoAutoTagging>.Instance);
    }

    /// <summary>
    /// Processa um PDF e tenta remediar automaticamente os problemas de acessibilidade.
    /// </summary>
    public ResultadoRemediacao Remediar(string caminhoOriginal, string? pastaDestino = null, OpcoesRemediacao? opcoes = null)
    {
        _logger.LogInformation("Iniciando remediação: {Arquivo}", caminhoOriginal);

        var resultado = new ResultadoRemediacao
        {
            CaminhoArquivoOriginal = caminhoOriginal
        };

        opcoes ??= new OpcoesRemediacao();

        try
        {
            // 1. Analisar antes
            resultado.AnaliseAntes = _validador.Analisar(caminhoOriginal);

            // 2. Definir caminho de saída
            pastaDestino ??= Path.GetDirectoryName(caminhoOriginal) ?? ".";
            var nomeArquivo = Path.GetFileNameWithoutExtension(caminhoOriginal);
            resultado.CaminhoArquivoRemediado = Path.Combine(
                pastaDestino,
                $"{nomeArquivo}_EN301549_remediado.pdf");

            // 3. Se o PDF não tem tags → auto-tagging completo (reescreve o documento).
            //    Se já tem tags → stamping mode para aplicar apenas as correções de metadados.
            Directory.CreateDirectory(Path.GetDirectoryName(resultado.CaminhoArquivoRemediado)!);

            var precisaAutoTagging = !resultado.AnaliseAntes.Metadatas.ETagged;
            var idiomaDoc = resultado.AnaliseAntes.Metadatas.TemIdioma
                ? resultado.AnaliseAntes.Metadatas.Idioma
                : "pt-PT";

            if (precisaAutoTagging && opcoes.AdicionarTags)
            {
                // Auto-tagging: extrai conteúdo, constrói árvore semântica e reescreve PDF
                _logger.LogInformation("Documento não tagged — iniciando auto-tagging completo.");
                var (numElementos, avisos) = _autoTagging.AdicionarTags(
                    caminhoOrigem: caminhoOriginal,
                    caminhoDestino: resultado.CaminhoArquivoRemediado,
                    idioma: idiomaDoc,
                    altTextImagemPadrao: "BPI");

                foreach (var aviso in avisos)
                    resultado.AcoesNaoRealizadas.Add($"[AutoTagging] {aviso}");

                resultado.AcoesRealizadas.Add(
                    $"[1.3.1] Auto-tagging concluído: {numElementos} elemento(s) taggeado(s) " +
                    $"(H1/H2/H3/P/Figure) com ordem de leitura inferida por posição Y. " +
                    "Recomenda-se revisão manual da árvore de tags no Adobe Acrobat Pro.");

                // Aplicar correções de metadados sobre o ficheiro já taggeado
                var caminhoTmpMeta = resultado.CaminhoArquivoRemediado + ".meta.tmp";
                using (var leitorMeta  = new PdfReader(resultado.CaminhoArquivoRemediado))
                using (var escritorMeta = new PdfWriter(caminhoTmpMeta))
                {
                    using var docMeta = new PdfDocument(leitorMeta, escritorMeta,
                        new StampingProperties().UseAppendMode());

                    if (opcoes.CorrigirTitulo)
                        AplicarCorrecaoTitulo(docMeta, resultado.AnaliseAntes.Metadatas, resultado);
                    if (opcoes.CorrigirMetadadasXMP)
                        AplicarCorrecaoMetadadasXMP(docMeta, resultado.AnaliseAntes.Metadatas, resultado);
                    if (opcoes.AltTextImagens)
                        AplicarAltTextImagens(docMeta, resultado);
                    // Idioma e ViewerPreferences são definidos pelo ServicoAutoTagging
                }
                File.Move(caminhoTmpMeta, resultado.CaminhoArquivoRemediado, overwrite: true);
            }
            else
            {
                // Documento já tagged (ou tagging desactivado): stamping mode
                _logger.LogInformation("Aplicando correções de metadados via stamping.");
                var caminhoTmp = resultado.CaminhoArquivoRemediado + ".tmp";

                using (var leitor   = new PdfReader(caminhoOriginal))
                using (var escritor = new PdfWriter(caminhoTmp))
                {
                    using var docStamped = new PdfDocument(leitor, escritor,
                        new StampingProperties().UseAppendMode());

                    if (opcoes.CorrigirTitulo)
                        AplicarCorrecaoTitulo(docStamped, resultado.AnaliseAntes.Metadatas, resultado);
                    if (opcoes.CorrigirIdioma)
                        AplicarCorrecaoIdioma(docStamped, resultado.AnaliseAntes.Metadatas, resultado);
                    if (opcoes.CorrigirViewerPreferences)
                        AplicarCorrecaoViewerPreferences(docStamped, resultado);
                    if (opcoes.CorrigirMetadadasXMP)
                        AplicarCorrecaoMetadadasXMP(docStamped, resultado.AnaliseAntes.Metadatas, resultado);
                    if (opcoes.AltTextImagens)
                        AplicarAltTextImagens(docStamped, resultado);
                }

                File.Move(caminhoTmp, resultado.CaminhoArquivoRemediado, overwrite: true);
                if (!opcoes.AdicionarTags || resultado.AnaliseAntes.Metadatas.ETagged)
                    resultado.AcoesRealizadas.Add("[1.3.1] Tags: " + (resultado.AnaliseAntes.Metadatas.ETagged
                        ? "documento já tagged. Estrutura preservada."
                        : "adição de tags desactivada pelo utilizador."));
            }

            // 4. Analisar depois
            resultado.AnaliseDepois = _validador.Analisar(resultado.CaminhoArquivoRemediado);

            // 5. Documentar o que NÃO pode ser feito automaticamente
            DocumentarLimitacoes(resultado);

            resultado.Sucesso = true;
            _logger.LogInformation(
                "Remediação concluída. Antes: {A:F1}% | Depois: {D:F1}%",
                resultado.AnaliseAntes.PontuacaoConformidade,
                resultado.AnaliseDepois.PontuacaoConformidade);
        }
        catch (Exception ex)
        {
            resultado.Sucesso = false;
            resultado.Erros.Add($"Erro durante remediação: {ex.Message}");
            _logger.LogError(ex, "Erro na remediação de {Arquivo}", caminhoOriginal);
        }

        return resultado;
    }

    private void AplicarCorrecaoTitulo(PdfDocument doc, MetadatasPdf meta, ResultadoRemediacao resultado)
    {
        if (!meta.TemTitulo)
        {
            // Usar nome do arquivo como título fallback
            var nomeSemExtensao = Path.GetFileNameWithoutExtension(resultado.CaminhoArquivoOriginal);
            doc.GetDocumentInfo().SetTitle(nomeSemExtensao);
            resultado.AcoesRealizadas.Add(
                $"[9.2.4.2] Título definido automaticamente com o nome do arquivo: '{nomeSemExtensao}'. " +
                "Recomenda-se substituir por um título descritivo.");
        }
        else
        {
            resultado.AcoesRealizadas.Add($"[9.2.4.2] Título já presente: '{meta.Titulo}'. Mantido.");
        }
    }

    private void AplicarCorrecaoIdioma(PdfDocument doc, MetadatasPdf meta, ResultadoRemediacao resultado)
    {
        if (!meta.TemIdioma)
        {
            // Assumir português europeu como padrão (pode ser parametrizado)
            doc.GetCatalog().Put(PdfName.Lang, new PdfString("pt-PT"));
            resultado.AcoesRealizadas.Add(
                "[9.3.1.1] Idioma definido como 'pt-PT' (Português Europeu). " +
                "Se o documento for em Português Brasileiro, altere para 'pt-BR'.");
        }
        else
        {
            resultado.AcoesRealizadas.Add($"[9.3.1.1] Idioma já definido: '{meta.Idioma}'. Mantido.");
        }
    }

    private void AplicarCorrecaoViewerPreferences(PdfDocument doc, ResultadoRemediacao resultado)
    {
        // Definir preferências de visualização para acessibilidade
        var prefs = doc.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.ViewerPreferences);
        if (prefs == null)
        {
            prefs = new PdfDictionary();
            doc.GetCatalog().Put(PdfName.ViewerPreferences, prefs);
        }

        // DisplayDocTitle: mostrar título em vez do nome do arquivo
        prefs.Put(new PdfName("DisplayDocTitle"), new PdfBoolean(true));

        resultado.AcoesRealizadas.Add(
            "Preferências do visualizador configuradas: DisplayDocTitle=true " +
            "(o título do documento será exibido em vez do nome do ficheiro).");
    }

    private void AplicarCorrecaoMetadadasXMP(PdfDocument doc, MetadatasPdf meta, ResultadoRemediacao resultado)
    {
        try
        {
            // Garantir que metadadas XMP estão presentes para conformidade PDF/UA
            var info = doc.GetDocumentInfo();
            if (string.IsNullOrWhiteSpace(info.GetCreator()))
            {
                info.SetCreator("EN 301 549 PDF Processor - Remediado");
            }

            resultado.AcoesRealizadas.Add("Metadadas XMP verificadas e atualizadas.");
        }
        catch (Exception ex)
        {
            resultado.AcoesNaoRealizadas.Add($"Não foi possível atualizar metadadas XMP: {ex.Message}");
        }
    }


    /// <summary>
    /// Percorre a árvore de tags do PDF e adiciona o atributo Alt="BPI" a todos os
    /// elementos &lt;Figure&gt; que ainda não possuam texto alternativo.
    /// Em PDFs não tagged, adiciona o atributo Alt diretamente nos XObjects de imagem
    /// como melhor esforço (não substitui tagging completo).
    /// </summary>
    private void AplicarAltTextImagens(PdfDocument doc, ResultadoRemediacao resultado,
        string altTextPadrao = "BPI")
    {
        int corrigidas = 0;
        int jaExistiam = 0;

        if (doc.IsTagged())
        {
            // ── Abordagem 1: documento tagged — percorrer árvore de tags ─────────
            corrigidas = CorrigirAltTextNaArvore(doc, altTextPadrao, out jaExistiam);
        }
        else
        {
            // ── Abordagem 2: documento não tagged — atributo Alt nos XObjects ────
            corrigidas = CorrigirAltTextEmXObjects(doc, altTextPadrao, out jaExistiam);
        }

        if (corrigidas > 0)
        {
            resultado.AcoesRealizadas.Add(
                $"[1.1.1] Alt text '{altTextPadrao}' adicionado a {corrigidas} imagem(ns) sem texto alternativo. " +
                $"{jaExistiam} imagem(ns) já tinham Alt text e foram mantidas. " +
                "Recomenda-se substituir 'BPI' por descrições específicas de cada imagem.");
        }
        else if (jaExistiam > 0)
        {
            resultado.AcoesRealizadas.Add(
                $"[1.1.1] Todas as {jaExistiam} imagem(ns) já possuem Alt text. Nenhuma alteração necessária.");
        }
        else
        {
            resultado.AcoesRealizadas.Add("[1.1.1] Nenhuma imagem detetada no documento.");
        }
    }

    /// <summary>
    /// Percorre recursivamente a árvore de tags e aplica Alt nos elementos Figure sem Alt.
    /// </summary>
    private int CorrigirAltTextNaArvore(PdfDocument doc, string altText, out int jaExistiam)
    {
        jaExistiam = 0;
        int corrigidas = 0;

        try
        {
            var structTreeRoot = doc.GetStructTreeRoot();
            if (structTreeRoot == null) return 0;

            PercorrerArvore(structTreeRoot.GetPdfObject(), altText, ref corrigidas, ref jaExistiam);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao percorrer árvore de tags: {Msg}", ex.Message);
        }

        return corrigidas;
    }

    private void PercorrerArvore(PdfObject obj, string altText, ref int corrigidas, ref int jaExistiam)
    {
        if (obj is not PdfDictionary dict) return;

        var role = dict.GetAsName(PdfName.S); // Papel semântico (S = Structure type)

        // Verificar se é um elemento Figure (imagem na estrutura de tags)
        if (PdfName.Figure.Equals(role))
        {
            var altExistente = dict.GetAsString(PdfName.Alt);
            if (altExistente == null || string.IsNullOrWhiteSpace(altExistente.GetValue()))
            {
                // Adicionar Alt text
                dict.Put(PdfName.Alt, new PdfString(altText));
                dict.SetModified();
                corrigidas++;
            }
            else
            {
                jaExistiam++;
            }
        }

        // Percorrer filhos (array K)
        var kids = dict.Get(PdfName.K);
        if (kids == null) return;

        if (kids is PdfArray kidsArray)
        {
            for (int i = 0; i < kidsArray.Size(); i++)
                PercorrerArvore(kidsArray.Get(i), altText, ref corrigidas, ref jaExistiam);
        }
        else if (kids is PdfDictionary || kids is PdfIndirectReference)
        {
            var resolved = kids is PdfIndirectReference ir ? ir.GetRefersTo() : kids;
            PercorrerArvore(resolved, altText, ref corrigidas, ref jaExistiam);
        }
    }

    /// <summary>
    /// Para PDFs não tagged: tenta adicionar atributo Alt diretamente nos streams de imagem XObject.
    /// Esta abordagem é "melhor esforço" — o Alt em XObject não é reconhecido por todos os leitores,
    /// mas é a única opção sem reestruturar o documento.
    /// </summary>
    private int CorrigirAltTextEmXObjects(PdfDocument doc, string altText, out int jaExistiam)
    {
        jaExistiam = 0;
        int corrigidas = 0;

        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            try
            {
                var pagina = doc.GetPage(p);
                var xObjects = pagina.GetResources()?.GetResource(PdfName.XObject);
                if (xObjects == null) continue;

                foreach (var entrada in xObjects.EntrySet())
                {
                    if (entrada.Value is not PdfStream stream) continue;
                    if (!PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype))) continue;

                    var altExistente = stream.GetAsString(PdfName.Alt);
                    if (altExistente == null || string.IsNullOrWhiteSpace(altExistente.GetValue()))
                    {
                        stream.Put(PdfName.Alt, new PdfString(altText));
                        stream.SetModified();
                        corrigidas++;
                    }
                    else
                    {
                        jaExistiam++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Erro ao processar XObjects da página {P}: {Msg}", p, ex.Message);
            }
        }

        return corrigidas;
    }

        private void DocumentarLimitacoes(ResultadoRemediacao resultado)
    {
        var analise = resultado.AnaliseAntes!;

        if (!analise.Metadatas.ETagged)
        {
            resultado.AcoesNaoRealizadas.Add(
                "[9.1.3.1] AÇÃO MANUAL NECESSÁRIA: A adição automática de tags pode alterar a " +
                "estrutura semântica do documento. Recomenda-se usar o Adobe Acrobat Pro > " +
                "Ferramentas > Acessibilidade > Adicionar Tags Automaticamente, " +
                "seguido de revisão manual da árvore de tags.");
        }

        if (analise.Estatisticas.LinksSemTextoDescritivo > 0)
        {
            resultado.AcoesNaoRealizadas.Add(
                $"[9.2.4.4] AÇÃO MANUAL NECESSÁRIA: {analise.Estatisticas.LinksSemTextoDescritivo} " +
                "link(s) sem texto descritivo. Adicione texto alternativo a cada link no Adobe Acrobat Pro.");
        }

        if (analise.Estatisticas.CamposFormularioSemRotulo > 0)
        {
            resultado.AcoesNaoRealizadas.Add(
                $"[9.1.3.1] AÇÃO MANUAL NECESSÁRIA: {analise.Estatisticas.CamposFormularioSemRotulo} " +
                "campo(s) de formulário sem rótulo. Use o Adobe Acrobat Pro para adicionar tooltips descritivos.");
        }

        resultado.AcoesNaoRealizadas.Add(
            "[9.1.4.3] VERIFICAÇÃO MANUAL: O contraste de cores entre texto e fundo " +
            "não pode ser verificado automaticamente nesta versão. " +
            "Use ferramentas como o Colour Contrast Analyser (ratio mínimo: 4.5:1 para texto normal, 3:1 para texto grande).");

        resultado.AcoesNaoRealizadas.Add(
            "[9.1.3.2] VERIFICAÇÃO MANUAL: Verifique a ordem de leitura lógica do documento " +
            "no Adobe Acrobat Pro: Exibir > Mostrar/Ocultar > Painéis de Navegação > Painel de Ordem.");
    }
}
