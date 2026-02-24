namespace EN301549PdfProcessor.Models;

/// <summary>
/// Controla quais correções automáticas são aplicadas durante a remediação.
/// Todas as opções são activadas por omissão.
/// </summary>
public class OpcoesRemediacao
{
    /// <summary>1.3.1 — Adicionar tags semânticas (H1/H2/H3/P/Figure) via auto-tagging.</summary>
    public bool AdicionarTags { get; set; } = true;

    /// <summary>1.1.1 — Adicionar texto alternativo "BPI" a imagens sem Alt.</summary>
    public bool AltTextImagens { get; set; } = true;

    /// <summary>2.4.2 — Definir título nos metadados se ausente.</summary>
    public bool CorrigirTitulo { get; set; } = true;

    /// <summary>3.1.1 — Definir idioma do documento se ausente (pt-PT).</summary>
    public bool CorrigirIdioma { get; set; } = true;

    /// <summary>PDF/UA — Definir DisplayDocTitle nas ViewerPreferences.</summary>
    public bool CorrigirViewerPreferences { get; set; } = true;

    /// <summary>PDF/UA — Atualizar metadados XMP (Creator).</summary>
    public bool CorrigirMetadadasXMP { get; set; } = true;
}
