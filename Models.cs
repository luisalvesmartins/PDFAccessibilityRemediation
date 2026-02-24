namespace EN301549PdfProcessor.Models;

/// <summary>
/// Resultado global da análise de acessibilidade EN 301 549 / WCAG 2.1 / WCAG 2.2
/// </summary>
public class AccessibilityAnalysisResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public DateTime AnalisadoEm { get; set; } = DateTime.Now;

    // Conformidade global
    public bool Conforme { get; set; }
    public double PontuacaoConformidade { get; set; }

    // Conformidade por norma
    public ResultadoConformidadeNorma ConformidadeEN301549 { get; set; } = new();
    public ResultadoConformidadeNorma ConformidadeWCAG21  { get; set; } = new();
    public ResultadoConformidadeNorma ConformidadeWCAG22  { get; set; } = new();

    public List<ViolacaoAcessibilidade>   Violacoes   { get; set; } = new();
    public List<AdvertenciaAcessibilidade> Advertencias { get; set; } = new();
    public List<string>                    Aprovacoes   { get; set; } = new();
    public MetadatasPdf                    Metadatas    { get; set; } = new();
    public EstatisticasDocumento           Estatisticas { get; set; } = new();
}

public class ResultadoConformidadeNorma
{
    public string Norma            { get; set; } = string.Empty;
    public string NivelAlvo        { get; set; } = "AA";
    public bool   Conforme         { get; set; }
    public double Pontuacao        { get; set; }
    public int    TotalVerificados { get; set; }
    public int    Aprovados        { get; set; }
    public int    Reprovados       { get; set; }
    public List<string> NaoAplicaveis { get; set; } = new();
}

public class ViolacaoAcessibilidade
{
    public string            CodigoCriterio      { get; set; } = string.Empty;
    public string            CodigoEN301549      { get; set; } = string.Empty;
    public string            NomeCriterio        { get; set; } = string.Empty;
    public NivelConformidade Nivel               { get; set; }
    public NormaOrigem       Norma               { get; set; }
    public string            Principio           { get; set; } = string.Empty;
    public string            Descricao           { get; set; } = string.Empty;
    public string            Recomendacao        { get; set; } = string.Empty;
    public int?              NumeroPagina        { get; set; }
    public string            LocalizacaoElemento { get; set; } = string.Empty;
    public GravidadeViolacao Gravidade           { get; set; }
    public string            UrlReferencia       { get; set; } = string.Empty;
}

public class AdvertenciaAcessibilidade
{
    public string     CodigoCriterio { get; set; } = string.Empty;
    public string     CodigoEN301549 { get; set; } = string.Empty;
    public string     NomeCriterio   { get; set; } = string.Empty;
    public NormaOrigem Norma         { get; set; }
    public string     Descricao      { get; set; } = string.Empty;
    public string     Recomendacao   { get; set; } = string.Empty;
    public int?       NumeroPagina   { get; set; }
}

public class MetadatasPdf
{
    public string Titulo              { get; set; } = string.Empty;
    public string Autor               { get; set; } = string.Empty;
    public string Assunto             { get; set; } = string.Empty;
    public string PalavrasChave       { get; set; } = string.Empty;
    public string Idioma              { get; set; } = string.Empty;
    public string CriadorSoftware     { get; set; } = string.Empty;
    public bool   TemTitulo           => !string.IsNullOrWhiteSpace(Titulo);
    public bool   TemIdioma           => !string.IsNullOrWhiteSpace(Idioma);
    public bool   TemTagsAcessibilidade { get; set; }
    public bool   ETagged             { get; set; }
    public bool   EFormulario         { get; set; }
    public bool   TemTabelaDeConteudo { get; set; }
    public string VersaoPdf           { get; set; } = string.Empty;
    public bool   DisplayDocTitle          { get; set; }
    public bool   TemPermissoesLeituraEcra { get; set; }
    public bool   TemXmpAcessibilidade     { get; set; }
    public string? EncriptacaoTipo         { get; set; }
}

public class EstatisticasDocumento
{
    public int  TotalPaginas                { get; set; }
    public int  TotalImagens                { get; set; }
    public int  ImagensComAlt               { get; set; }
    public int  ImagemSemAlt               { get; set; }
    public int  ImagensDecorativas          { get; set; }
    public int  TotalLinks                  { get; set; }
    public int  LinksSemTextoDescritivo     { get; set; }
    public int  TotalFormularios            { get; set; }
    public int  CamposFormularioSemRotulo   { get; set; }
    public int  TotalTabelas                { get; set; }
    public int  TabelasSemCabecalho         { get; set; }
    public int  TotalTitulos                { get; set; }
    public bool TemOrdemLeituraCorreta      { get; set; }
    public bool TemContrasteAdequado        { get; set; }
    public int  TotalCaracteresTexto        { get; set; }
    public bool TemAudioVideo               { get; set; }
    // WCAG 2.2 específico
    public int  TotalAlvosInteracao         { get; set; }
    public int  AlvosAbaixoTamanhoMinimo    { get; set; }
    public bool TemAutenticacaoComTeste     { get; set; }
}

public enum NivelConformidade { A, AA, AAA }
public enum GravidadeViolacao { Critica, Alta, Media, Baixa }

public enum NormaOrigem
{
    WCAG21,
    WCAG22,
    EN301549,
    Todas
}

public class ResultadoRemediacao
{
    public bool   Sucesso                   { get; set; }
    public string CaminhoArquivoOriginal    { get; set; } = string.Empty;
    public string CaminhoArquivoRemediado   { get; set; } = string.Empty;
    public List<string> AcoesRealizadas     { get; set; } = new();
    public List<string> AcoesNaoRealizadas  { get; set; } = new();
    public List<string> Erros               { get; set; } = new();
    public AccessibilityAnalysisResult? AnaliseAntes  { get; set; }
    public AccessibilityAnalysisResult? AnaliseDepois { get; set; }
}

/// <summary>
/// Mapeamento canónico WCAG ↔ EN 301 549 (cláusula 9.x para conteúdo web/PDF)
/// </summary>
public static class MapaCriterios
{
    public static readonly Dictionary<string, string> WcagParaEN301549 = new()
    {
        // Princípio 1 – Percetível
        { "1.1.1",  "9.1.1.1"  },
        { "1.2.1",  "9.1.2.1"  }, { "1.2.2",  "9.1.2.2"  }, { "1.2.3",  "9.1.2.3"  },
        { "1.2.4",  "9.1.2.4"  }, { "1.2.5",  "9.1.2.5"  },
        { "1.3.1",  "9.1.3.1"  }, { "1.3.2",  "9.1.3.2"  }, { "1.3.3",  "9.1.3.3"  },
        { "1.3.4",  "9.1.3.4"  }, { "1.3.5",  "9.1.3.5"  },
        { "1.4.1",  "9.1.4.1"  }, { "1.4.2",  "9.1.4.2"  }, { "1.4.3",  "9.1.4.3"  },
        { "1.4.4",  "9.1.4.4"  }, { "1.4.5",  "9.1.4.5"  }, { "1.4.10", "9.1.4.10" },
        { "1.4.11", "9.1.4.11" }, { "1.4.12", "9.1.4.12" }, { "1.4.13", "9.1.4.13" },
        // Princípio 2 – Operável
        { "2.1.1",  "9.2.1.1"  }, { "2.1.2",  "9.2.1.2"  },
        { "2.2.1",  "9.2.2.1"  }, { "2.2.2",  "9.2.2.2"  },
        { "2.3.1",  "9.2.3.1"  },
        { "2.4.1",  "9.2.4.1"  }, { "2.4.2",  "9.2.4.2"  }, { "2.4.3",  "9.2.4.3"  },
        { "2.4.4",  "9.2.4.4"  }, { "2.4.5",  "9.2.4.5"  }, { "2.4.6",  "9.2.4.6"  },
        { "2.4.7",  "9.2.4.7"  },
        // WCAG 2.2 (AA)
        { "2.4.11", "9.2.4.11" }, { "2.4.12", "9.2.4.12" }, { "2.4.13", "9.2.4.13" },
        { "2.5.3",  "9.2.5.3"  }, { "2.5.8",  "9.2.5.8"  },
        // Princípio 3 – Compreensível
        { "3.1.1",  "9.3.1.1"  }, { "3.1.2",  "9.3.1.2"  },
        { "3.2.1",  "9.3.2.1"  }, { "3.2.2",  "9.3.2.2"  },
        { "3.3.1",  "9.3.3.1"  }, { "3.3.2",  "9.3.3.2"  },
        // WCAG 2.2
        { "3.3.7",  "9.3.3.7"  }, { "3.3.8",  "9.3.3.8"  },
        // Princípio 4 – Robusto
        { "4.1.1",  "9.4.1.1"  }, { "4.1.2",  "9.4.1.2"  }, { "4.1.3",  "9.4.1.3"  },
    };

    public static string ObterEN301549(string codigoWcag) =>
        WcagParaEN301549.TryGetValue(codigoWcag, out var en) ? en : $"9.{codigoWcag}";
}
