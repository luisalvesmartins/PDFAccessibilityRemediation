using EN301549PdfProcessor.Models;
using System.Text;
using System.Text.Json;

namespace EN301549PdfProcessor.Reports;

/// <summary>
/// Gerador de relatórios de conformidade EN 301 549 / WCAG 2.1 / WCAG 2.2
/// </summary>
public class GeradorRelatorio
{
    public string GerarRelatorioTexto(AccessibilityAnalysisResult analise)
    {
        var sb  = new StringBuilder();
        var sep = new string('═', 72);
        var sl  = new string('─', 72);

        sb.AppendLine(sep);
        sb.AppendLine("  RELATÓRIO DE CONFORMIDADE DE ACESSIBILIDADE");
        sb.AppendLine("  EN 301 549 v3.2.1  ·  WCAG 2.1 AA  ·  WCAG 2.2 AA");
        sb.AppendLine(sep);
        sb.AppendLine();

        // ── Documento ─────────────────────────────────────────────────────────
        sb.AppendLine("📄 DOCUMENTO");
        sb.AppendLine(sl);
        sb.AppendLine($"  Ficheiro       : {analise.FileName}");
        sb.AppendLine($"  Versão PDF     : {analise.Metadatas.VersaoPdf}");
        sb.AppendLine($"  Título         : {(analise.Metadatas.TemTitulo ? analise.Metadatas.Titulo : "(não definido)")}");
        sb.AppendLine($"  Idioma         : {(analise.Metadatas.TemIdioma ? analise.Metadatas.Idioma : "(não definido)")}");
        sb.AppendLine($"  Autor          : {(string.IsNullOrEmpty(analise.Metadatas.Autor) ? "(não definido)" : analise.Metadatas.Autor)}");
        sb.AppendLine($"  Tagged/Marcado : {(analise.Metadatas.ETagged ? "Sim ✓" : "Não ✗")}");
        sb.AppendLine($"  DisplayDocTitle: {(analise.Metadatas.DisplayDocTitle ? "Sim ✓" : "Não ✗")}");
        sb.AppendLine($"  Formulário     : {(analise.Metadatas.EFormulario ? "Sim" : "Não")}");
        sb.AppendLine($"  Analisado em   : {analise.AnalisadoEm:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine();

        // ── Painel de Conformidade ────────────────────────────────────────────
        sb.AppendLine("📊 PAINEL DE CONFORMIDADE");
        sb.AppendLine(sl);

        AdicionarLinhaNorma(sb, "Global",          analise.PontuacaoConformidade, analise.Conforme);
        AdicionarLinhaNorma(sb, "EN 301 549 v3.2.1",
            analise.ConformidadeEN301549.Pontuacao, analise.ConformidadeEN301549.Conforme,
            $"V:{analise.ConformidadeEN301549.Reprovados} A:{analise.ConformidadeEN301549.Aprovados}");
        AdicionarLinhaNorma(sb, "WCAG 2.1 AA",
            analise.ConformidadeWCAG21.Pontuacao, analise.ConformidadeWCAG21.Conforme,
            $"V:{analise.ConformidadeWCAG21.Reprovados} A:{analise.ConformidadeWCAG21.Aprovados}");
        AdicionarLinhaNorma(sb, "WCAG 2.2 AA",
            analise.ConformidadeWCAG22.Pontuacao, analise.ConformidadeWCAG22.Conforme,
            $"V:{analise.ConformidadeWCAG22.Reprovados} A:{analise.ConformidadeWCAG22.Aprovados}");
        sb.AppendLine();

        // ── Estatísticas ──────────────────────────────────────────────────────
        sb.AppendLine("📈 ESTATÍSTICAS");
        sb.AppendLine(sl);
        var e = analise.Estatisticas;
        sb.AppendLine($"  Páginas        : {e.TotalPaginas}");
        sb.AppendLine($"  Imagens        : {e.TotalImagens} total  ({e.ImagensComAlt} c/alt · {e.ImagemSemAlt} sem alt · {e.ImagensDecorativas} decorativas)");
        sb.AppendLine($"  Links          : {e.TotalLinks} ({e.LinksSemTextoDescritivo} sem descrição)");
        sb.AppendLine($"  Formulários    : {e.TotalFormularios} campo(s) ({e.CamposFormularioSemRotulo} sem rótulo)");
        sb.AppendLine($"  Alvos UI       : {e.TotalAlvosInteracao} ({e.AlvosAbaixoTamanhoMinimo} abaixo do mínimo 24px [WCAG 2.2])");
        sb.AppendLine($"  Marcadores     : {(analise.Metadatas.TemTabelaDeConteudo ? "Presentes ✓" : "Ausentes")}");
        sb.AppendLine($"  Áudio/Vídeo    : {(e.TemAudioVideo ? "Sim – verificar legendas" : "Não")}");
        sb.AppendLine();

        // ── Violações ─────────────────────────────────────────────────────────
        if (analise.Violacoes.Any())
        {
            sb.AppendLine($"❌ VIOLAÇÕES ({analise.Violacoes.Count}) — Agrupadas por Princípio");
            sb.AppendLine(sl);

            var grupos = analise.Violacoes
                .OrderBy(v => v.Principio)
                .ThenByDescending(v => (int)v.Gravidade * -1)
                .GroupBy(v => v.Principio.Length > 0 ? v.Principio : "Outro");

            foreach (var grupo in grupos)
            {
                sb.AppendLine($"  ▶ {grupo.Key.ToUpper()}");
                foreach (var v in grupo)
                {
                    var tag    = NormaTag(v.Norma);
                    var nivel  = $"[{v.Nivel}]";
                    var grav   = GravidadeIcon(v.Gravidade);
                    var en     = v.CodigoEN301549 != v.CodigoCriterio ? $" | EN:{v.CodigoEN301549}" : "";
                    sb.AppendLine($"  {grav} WCAG {v.CodigoCriterio}{en} {nivel} {tag}");
                    sb.AppendLine($"     {v.NomeCriterio}");
                    sb.AppendLine($"     Problema      : {Wrap(v.Descricao, 62, "                    ")}");
                    sb.AppendLine($"     Recomendação  : {Wrap(v.Recomendacao, 62, "                    ")}");
                    if (!string.IsNullOrEmpty(v.UrlReferencia))
                        sb.AppendLine($"     Referência    : {v.UrlReferencia}");
                    sb.AppendLine();
                }
            }
        }

        // ── Advertências ──────────────────────────────────────────────────────
        if (analise.Advertencias.Any())
        {
            var advertWcag22  = analise.Advertencias.Where(a => a.Norma == NormaOrigem.WCAG22).ToList();
            var advertOutras  = analise.Advertencias.Where(a => a.Norma != NormaOrigem.WCAG22).ToList();

            if (advertOutras.Any())
            {
                sb.AppendLine($"⚠️  ADVERTÊNCIAS — Revisão Manual ({advertOutras.Count})");
                sb.AppendLine(sl);
                foreach (var a in advertOutras)
                    EscreverAdvertencia(sb, a);
            }

            if (advertWcag22.Any())
            {
                sb.AppendLine($"🆕 NOVOS CRITÉRIOS WCAG 2.2 — Verificação Manual ({advertWcag22.Count})");
                sb.AppendLine(sl);
                foreach (var a in advertWcag22)
                    EscreverAdvertencia(sb, a);
            }
        }

        // ── Aprovações ────────────────────────────────────────────────────────
        if (analise.Aprovacoes.Any())
        {
            sb.AppendLine($"✅ CRITÉRIOS APROVADOS ({analise.Aprovacoes.Count})");
            sb.AppendLine(sl);
            foreach (var ap in analise.Aprovacoes)
                sb.AppendLine($"  {ap}");
            sb.AppendLine();
        }

        // ── Rodapé ────────────────────────────────────────────────────────────
        sb.AppendLine(sl);
        sb.AppendLine("  Normas: ETSI EN 301 549 V3.2.1 (2021-03) · WCAG 2.1 (2018) · WCAG 2.2 (2023)");
        sb.AppendLine("  Implementação: C# / iText 8 — EN 301 549 PDF Processor");
        sb.AppendLine("  Atenção: Validação automática cobre ~40% dos critérios. Revisão manual obrigatória.");
        sb.AppendLine(sep);

        return sb.ToString();
    }

    public string GerarRelatorioRemediacao(ResultadoRemediacao resultado)
    {
        var sb  = new StringBuilder();
        var sep = new string('═', 72);
        var sl  = new string('─', 72);

        sb.AppendLine(sep);
        sb.AppendLine("  RELATÓRIO DE REMEDIAÇÃO — EN 301 549 / WCAG 2.1 / 2.2");
        sb.AppendLine(sep);
        sb.AppendLine();
        sb.AppendLine($"  Original   : {Path.GetFileName(resultado.CaminhoArquivoOriginal)}");
        sb.AppendLine($"  Remediado  : {Path.GetFileName(resultado.CaminhoArquivoRemediado)}");
        sb.AppendLine($"  Estado     : {(resultado.Sucesso ? "✅ Sucesso" : "❌ Erro")}");
        sb.AppendLine();

        if (resultado.AnaliseAntes != null && resultado.AnaliseDepois != null)
        {
            sb.AppendLine("📊 COMPARATIVO ANTES / DEPOIS");
            sb.AppendLine(sl);
            var ant = resultado.AnaliseAntes;
            var dep = resultado.AnaliseDepois;
            AdicionarLinhaComparativo(sb, "Global",       ant.PontuacaoConformidade,            dep.PontuacaoConformidade);
            AdicionarLinhaComparativo(sb, "EN 301 549",   ant.ConformidadeEN301549.Pontuacao,   dep.ConformidadeEN301549.Pontuacao);
            AdicionarLinhaComparativo(sb, "WCAG 2.1",     ant.ConformidadeWCAG21.Pontuacao,     dep.ConformidadeWCAG21.Pontuacao);
            AdicionarLinhaComparativo(sb, "WCAG 2.2",     ant.ConformidadeWCAG22.Pontuacao,     dep.ConformidadeWCAG22.Pontuacao);
            sb.AppendLine();
        }

        if (resultado.AcoesRealizadas.Any())
        {
            sb.AppendLine($"✅ AÇÕES REALIZADAS AUTOMATICAMENTE ({resultado.AcoesRealizadas.Count})");
            sb.AppendLine(sl);
            foreach (var a in resultado.AcoesRealizadas)
                sb.AppendLine($"  • {Wrap(a, 65, "    ")}");
            sb.AppendLine();
        }

        if (resultado.AcoesNaoRealizadas.Any())
        {
            sb.AppendLine($"🔧 AÇÕES MANUAIS NECESSÁRIAS ({resultado.AcoesNaoRealizadas.Count})");
            sb.AppendLine(sl);
            foreach (var a in resultado.AcoesNaoRealizadas)
                sb.AppendLine($"  • {Wrap(a, 65, "    ")}");
            sb.AppendLine();
        }

        if (resultado.Erros.Any())
        {
            sb.AppendLine($"❌ ERROS ({resultado.Erros.Count})");
            sb.AppendLine(sl);
            foreach (var e in resultado.Erros)
                sb.AppendLine($"  • {e}");
            sb.AppendLine();
        }

        sb.AppendLine(sep);
        return sb.ToString();
    }

    public string GerarRelatorioJson(AccessibilityAnalysisResult analise)
    {
        return JsonSerializer.Serialize(analise, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AdicionarLinhaNorma(StringBuilder sb, string norma, double score,
        bool conforme, string extra = "")
    {
        var icon = score >= 80 ? "✅" : score >= 50 ? "⚠️ " : "❌";
        var barra = Barra(score);
        var estado = conforme ? "CONFORME" : "NÃO CONFORME";
        sb.AppendLine($"  {icon} {norma,-22} {score,6:F1}%  {barra}  {estado,-13} {extra}");
    }

    private void AdicionarLinhaComparativo(StringBuilder sb, string norma, double antes, double depois)
    {
        var delta = depois - antes;
        var sinal = delta >= 0 ? "+" : "";
        var icon  = delta > 0 ? "📈" : delta < 0 ? "📉" : "➡️ ";
        sb.AppendLine($"  {icon} {norma,-14}  Antes: {antes,6:F1}%  →  Depois: {depois,6:F1}%  ({sinal}{delta:F1}%)");
    }

    private void EscreverAdvertencia(StringBuilder sb, AdvertenciaAcessibilidade a)
    {
        var tag = NormaTag(a.Norma);
        var en  = a.CodigoEN301549 != a.CodigoCriterio ? $" | EN:{a.CodigoEN301549}" : "";
        sb.AppendLine($"  ⚠ [{a.CodigoCriterio}{en}] {a.NomeCriterio} {tag}");
        sb.AppendLine($"     {Wrap(a.Descricao, 65, "     ")}");
        sb.AppendLine($"     ➜ {Wrap(a.Recomendacao, 63, "       ")}");
        sb.AppendLine();
    }

    private static string NormaTag(NormaOrigem norma) => norma switch
    {
        NormaOrigem.WCAG22   => "[🆕 WCAG 2.2]",
        NormaOrigem.EN301549 => "[EN 301 549]",
        NormaOrigem.WCAG21   => "[WCAG 2.1]",
        _                    => "[WCAG 2.1+2.2+EN]"
    };

    private static string GravidadeIcon(GravidadeViolacao g) => g switch
    {
        GravidadeViolacao.Critica => "🔴 CRÍTICA",
        GravidadeViolacao.Alta    => "🟠 ALTA   ",
        GravidadeViolacao.Media   => "🟡 MÉDIA  ",
        _                         => "🟢 BAIXA  "
    };

    private static string Barra(double pct)
    {
        int fill = (int)Math.Round(pct / 5);
        return "[" + new string('█', fill).PadRight(20, '░') + "]";
    }

    private static string Wrap(string texto, int largura, string prefixo)
    {
        if (texto.Length <= largura) return texto;
        var palavras = texto.Split(' ');
        var sb = new StringBuilder();
        var linha = new StringBuilder();
        foreach (var p in palavras)
        {
            if (linha.Length + p.Length + 1 > largura)
            {
                sb.AppendLine(linha.ToString().TrimEnd());
                sb.Append(prefixo);
                linha.Clear();
            }
            linha.Append(p + " ");
        }
        sb.Append(linha.ToString().TrimEnd());
        return sb.ToString();
    }
}
