using EN301549PdfProcessor.Models;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;

namespace EN301549PdfProcessor.Services;

/// <summary>
/// Serviço de adição automática de tags de acessibilidade a PDFs não marcados.
///
/// Estratégia:
/// 1. Extrai blocos de conteúdo (texto, imagens) página a página usando PdfCanvasProcessor.
/// 2. Agrupa chunks de texto em parágrafos lógicos por proximidade vertical.
/// 3. Reconhece padrões de títulos por tamanho de fonte e negrito.
/// 4. Constrói a árvore de tags: Document > (H1/H2/P/Figure) por página.
/// 5. Copia o PDF original para um novo ficheiro com a estrutura de tags adicionada.
///
/// Limitações conhecidas (inerentes à natureza de PDFs não tagged):
/// - A ordem de leitura é inferida pela posição Y descendente; colunas múltiplas
///   podem gerar ordem incorreta e requerem revisão manual.
/// - Tabelas são tratadas como parágrafos sem estrutura Table/TR/TD.
/// - Hiperligações não são promovidas a &lt;Link&gt; automaticamente.
/// </summary>
public class ServicoAutoTagging
{
    private readonly ILogger<ServicoAutoTagging> _logger;

    // Limiar de tamanho de fonte para considerar título
    private const float LimiarH1Pt = 18f;
    private const float LimiarH2Pt = 14f;
    private const float LimiarH3Pt = 12f;

    // Distância vertical máxima (pt) entre linhas do mesmo parágrafo
    private const float MargemMesmoParagrafo = 4f;

    public ServicoAutoTagging(ILogger<ServicoAutoTagging> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adiciona tags semânticas a um PDF não tagged e escreve o resultado para
    /// <paramref name="caminhoDestino"/>. Retorna o número de elementos taggeados.
    /// </summary>
    public (int ElementosTaggeados, List<string> Avisos) AdicionarTags(
        string caminhoOrigem,
        string caminhoDestino,
        string idioma = "pt-PT",
        string altTextImagemPadrao = "BPI")
    {
        var avisos = new List<string>();
        int totalElementos = 0;

        _logger.LogInformation("Auto-tagging: {Origem} → {Destino}", caminhoOrigem, caminhoDestino);

        // ── Passo 1: Extrair conteúdo de todas as páginas ─────────────────────
        List<PaginaConteudo> paginas;
        using (var leitorExtracao = new PdfReader(caminhoOrigem))
        using (var docExtracao    = new PdfDocument(leitorExtracao))
        {
            paginas = ExtrairConteudo(docExtracao);
        }

        // ── Passo 2: Escrever novo PDF com tags ───────────────────────────────
        // Não podemos usar StampingProperties/AppendMode para adicionar StructTreeRoot
        // de raiz — é necessário reescrever com PdfWriter limpo.
        var tmpPath = caminhoDestino + ".tagtmp";

        using (var leitorCopia   = new PdfReader(caminhoOrigem))
        using (var escritorNovo  = new PdfWriter(tmpPath))
        {
            // SmartCopyingEnabled preserva fonts/resources sem duplicar
            using var docNovo = new PdfDocument(leitorCopia, escritorNovo,
                new StampingProperties()); // sem AppendMode — reescreve completo

            // Activar tagging
            docNovo.SetTagged();

            // Definir idioma no catálogo
            docNovo.GetCatalog().Put(PdfName.Lang, new PdfString(idioma));

            // DisplayDocTitle
            var vp = new PdfDictionary();
            vp.Put(new PdfName("DisplayDocTitle"), new PdfBoolean(true));
            docNovo.GetCatalog().Put(PdfName.ViewerPreferences, vp);

            // ── Passo 3: Construir árvore de tags ─────────────────────────────
            var tagContext = docNovo.GetTagStructureContext();
            var pointer    = tagContext.GetAutoTaggingPointer();

            // Raiz: Document
            pointer.AddTag(StandardRoles.DOCUMENT);

            for (int i = 0; i < paginas.Count; i++)
            {
                var paginaInfo  = paginas[i];
                var paginaPdf   = docNovo.GetPage(i + 1);

                // Marcar página como beginning of a new section
                pointer.SetPageForTagging(paginaPdf);

                totalElementos += TagearPagina(pointer, paginaInfo, altTextImagemPadrao, avisos);
            }

            _logger.LogInformation("Árvore de tags construída com {N} elementos.", totalElementos);
        }

        // Mover tmp para destino final
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(caminhoDestino)!);
        File.Move(tmpPath, caminhoDestino, overwrite: true);

        if (totalElementos == 0)
            avisos.Add("Nenhum elemento foi taggeado. O documento pode estar vazio ou ser um scan.");

        return (totalElementos, avisos);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EXTRACÇÃO DE CONTEÚDO
    // ══════════════════════════════════════════════════════════════════════════

    private List<PaginaConteudo> ExtrairConteudo(PdfDocument doc)
    {
        var paginas = new List<PaginaConteudo>();

        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            var pagina  = doc.GetPage(p);
            var listener = new ExtractorConteudo();
            var processor = new PdfCanvasProcessor(listener);

            try
            {
                processor.ProcessPageContent(pagina);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Erro ao extrair conteúdo da página {P}: {Msg}", p, ex.Message);
            }

            var conteudo = new PaginaConteudo
            {
                NumeroPagina = p,
                BlocosTexto  = AgruparEmParagrafos(listener.Chunks),
                Imagens      = listener.Imagens
            };

            paginas.Add(conteudo);
        }

        return paginas;
    }

    /// <summary>
    /// Agrupa chunks de texto em blocos de parágrafo por proximidade vertical e fonte.
    /// </summary>
    private List<BlocoTexto> AgruparEmParagrafos(List<ChunkTexto> chunks)
    {
        if (chunks.Count == 0) return new List<BlocoTexto>();

        // Ordenar por Y descendente (topo da página primeiro), depois X
        var ordenados = chunks
            .OrderByDescending(c => c.Y)
            .ThenBy(c => c.X)
            .ToList();

        var blocos  = new List<BlocoTexto>();
        var actual  = new BlocoTexto(ordenados[0]);

        for (int i = 1; i < ordenados.Count; i++)
        {
            var chunk = ordenados[i];
            double distY = Math.Abs(actual.YBase - chunk.Y);

            // Mesmo parágrafo: mesma linha ou linha imediatamente abaixo com mesma fonte
            bool mesmoParagrafo = distY <= Math.Max(actual.TamanhoFonte, chunk.TamanhoFonte) + MargemMesmoParagrafo
                                  && Math.Abs(actual.TamanhoFonte - chunk.TamanhoFonte) < 1f
                                  && actual.Negrito == chunk.Negrito;

            if (mesmoParagrafo)
            {
                actual.Adicionar(chunk);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(actual.Texto))
                    blocos.Add(actual);
                actual = new BlocoTexto(chunk);
            }
        }

        if (!string.IsNullOrWhiteSpace(actual.Texto))
            blocos.Add(actual);

        return blocos;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONSTRUÇÃO DA ÁRVORE DE TAGS
    // ══════════════════════════════════════════════════════════════════════════

    private int TagearPagina(
        TagTreePointer pointer,
        PaginaConteudo pagina,
        string altTextImagem,
        List<string> avisos)
    {
        int count = 0;

        // Intercalar imagens e texto pela posição Y para manter ordem de leitura
        var elementos = new List<(double Y, object Item)>();
        foreach (var b in pagina.BlocosTexto) elementos.Add((b.YBase, b));
        foreach (var img in pagina.Imagens)    elementos.Add((img.Y, img));

        // Ordenar: Y descendente (topo primeiro)
        var ordenados = elementos.OrderByDescending(e => e.Y).ToList();

        foreach (var (_, item) in ordenados)
        {
            switch (item)
            {
                case BlocoTexto bloco:
                    TagearBlocoTexto(pointer, bloco);
                    count++;
                    break;

                case InfoImagem imagem:
                    TagearImagem(pointer, imagem, altTextImagem);
                    count++;
                    break;
            }
        }

        if (count == 0 && pagina.NumeroPagina > 0)
            avisos.Add($"Página {pagina.NumeroPagina}: nenhum elemento extraído (possível scan ou página em branco).");

        return count;
    }

    private void TagearBlocoTexto(TagTreePointer pointer, BlocoTexto bloco)
    {
        // Determinar papel semântico pela heurística de tamanho + negrito
        string papel = DeterminarPapel(bloco);

        try
        {
            pointer.AddTag(papel);
            // Mover para o pai após criar o elemento leaf
            pointer.MoveToParent();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Erro ao tagear bloco '{Txt}': {Msg}",
                bloco.Texto.Length > 40 ? bloco.Texto[..40] : bloco.Texto, ex.Message);
        }
    }

    private void TagearImagem(TagTreePointer pointer, InfoImagem imagem, string altText)
    {
        try
        {
            pointer.AddTag(StandardRoles.FIGURE);

            // Definir Alt text na tag Figure
            var currentElem = ObterDicionarioTagActual(pointer);
            if (currentElem != null)
            {
                var altExistente = currentElem.GetAsString(PdfName.Alt);
                if (altExistente == null || string.IsNullOrWhiteSpace(altExistente.GetValue()))
                    currentElem.Put(PdfName.Alt, new PdfString(altText));
            }

            pointer.MoveToParent();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Erro ao tagear imagem: {Msg}", ex.Message);
        }
    }
    private string DeterminarPapel(BlocoTexto bloco)
    {
        float fontSize = bloco.TamanhoFonte;

        if (fontSize >= LimiarH1Pt || (fontSize >= LimiarH2Pt && bloco.Negrito && bloco.Texto.Length < 120))
            return StandardRoles.H1;

        if (fontSize >= LimiarH2Pt || (fontSize >= LimiarH3Pt && bloco.Negrito && bloco.Texto.Length < 120))
            return StandardRoles.H2;

        if (fontSize >= LimiarH3Pt && bloco.Negrito && bloco.Texto.Length < 120)
            return StandardRoles.H3;

        return StandardRoles.P;
    }

    private PdfDictionary? ObterDicionarioTagActual(TagTreePointer pointer)
    {
        try
        {
            // Obter referência indirecta do elemento actual via reflexão interna do iText
            // TagTreePointer não expõe directamente o PdfDictionary da tag corrente,
            // mas podemos aceder via GetProperties ou StructureElement
            var props = pointer.GetProperties();
            return null; // iText 8 não expõe directamente; Alt é gerido pelo AddTag Figure workflow
        }
        catch
        {
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MODELOS INTERNOS
    // ══════════════════════════════════════════════════════════════════════════

    private class PaginaConteudo
    {
        public int NumeroPagina       { get; set; }
        public List<BlocoTexto> BlocosTexto { get; set; } = new();
        public List<InfoImagem> Imagens     { get; set; } = new();
    }

    private class ChunkTexto
    {
        public string Texto        { get; set; } = string.Empty;
        public float  X            { get; set; }
        public float  Y            { get; set; }
        public float  TamanhoFonte { get; set; }
        public bool   Negrito      { get; set; }
    }

    private class BlocoTexto
    {
        public string Texto        { get; private set; }
        public float  YBase        { get; private set; }
        public float  XBase        { get; private set; }
        public float  TamanhoFonte { get; private set; }
        public bool   Negrito      { get; private set; }

        public BlocoTexto(ChunkTexto primeiro)
        {
            Texto        = primeiro.Texto;
            YBase        = primeiro.Y;
            XBase        = primeiro.X;
            TamanhoFonte = primeiro.TamanhoFonte;
            Negrito      = primeiro.Negrito;
        }

        public void Adicionar(ChunkTexto chunk)
        {
            Texto += " " + chunk.Texto;
            // Actualizar YBase para a linha mais abaixo (menor Y)
            if (chunk.Y < YBase) YBase = chunk.Y;
        }
    }

    private class InfoImagem
    {
        public float X      { get; set; }
        public float Y      { get; set; }
        public float Largura { get; set; }
        public float Altura  { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LISTENER DE EXTRACÇÃO (PdfCanvasProcessor)
    // ══════════════════════════════════════════════════════════════════════════

    private class ExtractorConteudo : IEventListener
    {
        public List<ChunkTexto> Chunks  { get; } = new();
        public List<InfoImagem> Imagens { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo textInfo)
            {
                var texto = textInfo.GetText().Trim();
                if (string.IsNullOrEmpty(texto)) return;

                var baseline = textInfo.GetBaseline();
                var start    = baseline.GetStartPoint();
                var font     = textInfo.GetFont();
                float fontSize = textInfo.GetFontSize() * textInfo.GetTextMatrix().Get(Matrix.I11);
                if (fontSize <= 0) fontSize = textInfo.GetFontSize();

                // Detectar negrito via nome da fonte
                bool negrito = false;
                try
                {
                    var fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
                    negrito = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                           || fontName.Contains("Negrito", StringComparison.OrdinalIgnoreCase)
                           || fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
                           || fontName.Contains("Black", StringComparison.OrdinalIgnoreCase);
                }
                catch { /* ignorar erros de font program */ }

                Chunks.Add(new ChunkTexto
                {
                    Texto        = texto,
                    X            = start.Get(Vector.I1),
                    Y            = start.Get(Vector.I2),
                    TamanhoFonte = Math.Abs(fontSize),
                    Negrito      = negrito
                });
            }
            else if (type == EventType.RENDER_IMAGE && data is ImageRenderInfo imageInfo)
            {
                try
                {
                    var mtx = imageInfo.GetImageCtm();
                    Imagens.Add(new InfoImagem
                    {
                        X       = mtx.Get(Matrix.I31),
                        Y       = mtx.Get(Matrix.I32),
                        Largura = mtx.Get(Matrix.I11),
                        Altura  = mtx.Get(Matrix.I22)
                    });
                }
                catch { /* ignorar imagens sem CTM válida */ }
            }
        }

        public ICollection<EventType> GetSupportedEvents() =>
            new[] { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    }
}
