using EN301549PdfProcessor.Reports;
using EN301549PdfProcessor.Services;
using EN301549PdfProcessor.Validators;
using Microsoft.Extensions.Logging;

namespace EN301549PdfProcessor;

/// <summary>
/// EN 301 549 PDF Processor
/// Ferramenta de linha de comando para analisar e remediar PDFs em português
/// conforme a norma europeia de acessibilidade EN 301 549.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(opts =>
            {
                opts.TimestampFormat = "[HH:mm:ss] ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<Program>();
        var geradorRelatorio = new GeradorRelatorio();

        var validador = new ValidadorAcessibilidadePdf(
            loggerFactory.CreateLogger<ValidadorAcessibilidadePdf>());

        var remediador = new ServicodeRemediacao(
            loggerFactory.CreateLogger<ServicodeRemediacao>(),
            validador);

        var processadorLote = new ProcessadorLote(
            loggerFactory.CreateLogger<ProcessadorLote>(),
            validador,
            remediador,
            geradorRelatorio);

        MostrarBanner();

        if (args.Length == 0)
        {
            MostrarAjuda();
            return 0;
        }

        var comando = args[0].ToLower();

        switch (comando)
        {
            case "analisar":
                return await ComandoAnalisar(args, validador, geradorRelatorio, logger);

            case "remediar":
                return await ComandoRemediar(args, remediador, geradorRelatorio, logger);

            case "lote":
                return await ComandoLote(args, processadorLote, geradorRelatorio, logger);

            case "ajuda":
            case "--ajuda":
            case "-h":
            case "--help":
                MostrarAjuda();
                return 0;

            default:
                Console.WriteLine($"Comando desconhecido: {comando}");
                MostrarAjuda();
                return 1;
        }
    }

    static async Task<int> ComandoAnalisar(
        string[] args,
        ValidadorAcessibilidadePdf validador,
        GeradorRelatorio gerador,
        ILogger logger)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Uso: analisar <caminho-pdf> [--json] [--guardar <ficheiro-relatorio>]");
            return 1;
        }

        var caminhoPdf = args[1];
        var formatoJson = args.Contains("--json");
        var indiceGuardar = Array.IndexOf(args, "--guardar");
        var caminhoRelatorio = indiceGuardar >= 0 && args.Length > indiceGuardar + 1
            ? args[indiceGuardar + 1]
            : null;

        try
        {
            Console.WriteLine($"\nAnalisando: {caminhoPdf}\n");
            var resultado = validador.Analisar(caminhoPdf);

            string relatorio = formatoJson
                ? gerador.GerarRelatorioJson(resultado)
                : gerador.GerarRelatorioTexto(resultado);

            Console.WriteLine(relatorio);

            if (caminhoRelatorio != null)
            {
                await File.WriteAllTextAsync(caminhoRelatorio, relatorio, System.Text.Encoding.UTF8);
                Console.WriteLine($"Relatório guardado em: {caminhoRelatorio}");
            }

            return resultado.Conforme ? 0 : 2; // Exit code 2 = não conforme
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro durante análise");
            return 1;
        }
    }

    static async Task<int> ComandoRemediar(
        string[] args,
        ServicodeRemediacao remediador,
        GeradorRelatorio gerador,
        ILogger logger)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Uso: remediar <caminho-pdf> [--destino <pasta-destino>] [--guardar <relatorio>]");
            return 1;
        }

        var caminhoPdf = args[1];
        var indiceDestino = Array.IndexOf(args, "--destino");
        var pastaDestino = indiceDestino >= 0 && args.Length > indiceDestino + 1
            ? args[indiceDestino + 1]
            : null;

        var indiceGuardar = Array.IndexOf(args, "--guardar");
        var caminhoRelatorio = indiceGuardar >= 0 && args.Length > indiceGuardar + 1
            ? args[indiceGuardar + 1]
            : null;

        try
        {
            Console.WriteLine($"\nRemediando: {caminhoPdf}\n");
            var resultado = remediador.Remediar(caminhoPdf, pastaDestino);

            var relatorio = gerador.GerarRelatorioRemediacao(resultado);
            Console.WriteLine(relatorio);

            if (resultado.AnaliseDepois != null)
            {
                Console.WriteLine("\nANÁLISE DO DOCUMENTO REMEDIADO:");
                Console.WriteLine(gerador.GerarRelatorioTexto(resultado.AnaliseDepois));
            }

            if (caminhoRelatorio != null)
            {
                var conteudo = relatorio + (resultado.AnaliseDepois != null
                    ? "\n\n" + gerador.GerarRelatorioTexto(resultado.AnaliseDepois)
                    : "");
                await File.WriteAllTextAsync(caminhoRelatorio, conteudo, System.Text.Encoding.UTF8);
                Console.WriteLine($"Relatório guardado em: {caminhoRelatorio}");
            }

            return resultado.Sucesso ? 0 : 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro durante remediação");
            return 1;
        }
    }

    static async Task<int> ComandoLote(
        string[] args,
        ProcessadorLote processador,
        GeradorRelatorio gerador,
        ILogger logger)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Uso: lote <pasta> [--remediar] [--recursivo] [--destino <pasta>] [--guardar <relatorio>]");
            return 1;
        }

        var pasta = args[1];
        var remediar = args.Contains("--remediar");
        var recursivo = args.Contains("--recursivo");

        var indiceDestino = Array.IndexOf(args, "--destino");
        var pastaDestino = indiceDestino >= 0 && args.Length > indiceDestino + 1
            ? args[indiceDestino + 1]
            : null;

        var indiceGuardar = Array.IndexOf(args, "--guardar");
        var caminhoRelatorio = indiceGuardar >= 0 && args.Length > indiceGuardar + 1
            ? args[indiceGuardar + 1]
            : null;

        try
        {
            if (remediar)
            {
                var resultados = processador.RemediарPasta(pasta, pastaDestino, recursivo);
                var analises = resultados
                    .Where(r => r.AnaliseDepois != null)
                    .Select(r => r.AnaliseDepois!)
                    .ToList();
                var relatorio = processador.GerarRelatorioConsolidado(analises);
                Console.WriteLine(relatorio);

                if (caminhoRelatorio != null)
                    await File.WriteAllTextAsync(caminhoRelatorio, relatorio, System.Text.Encoding.UTF8);
            }
            else
            {
                var analises = processador.AnalisarPasta(pasta, recursivo);
                var relatorio = processador.GerarRelatorioConsolidado(analises);
                Console.WriteLine(relatorio);

                if (caminhoRelatorio != null)
                    await File.WriteAllTextAsync(caminhoRelatorio, relatorio, System.Text.Encoding.UTF8);
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro durante processamento em lote");
            return 1;
        }
    }

    static void MostrarBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
  ╔═══════════════════════════════════════════════════════╗
  ║      EN 301 549 PDF Processor – Acessibilidade        ║
  ║      Norma Europeia de Acessibilidade Digital          ║
  ╚═══════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    static void MostrarAjuda()
    {
        Console.WriteLine(@"
UTILIZAÇÃO:
  EN301549PdfProcessor <comando> [opções]

COMANDOS:
  analisar <pdf>          Analisa um PDF e gera relatório de conformidade
  remediar <pdf>          Analisa e aplica correções automáticas ao PDF
  lote <pasta>            Processa múltiplos PDFs numa pasta
  ajuda                   Mostra esta ajuda

OPÇÕES (analisar):
  --json                  Exportar relatório em formato JSON
  --guardar <ficheiro>    Guardar relatório num ficheiro

OPÇÕES (remediar):
  --destino <pasta>       Pasta de destino para o PDF remediado
  --guardar <ficheiro>    Guardar relatório num ficheiro

OPÇÕES (lote):
  --remediar              Também remedia os PDFs (não só analisa)
  --recursivo             Incluir subpastas
  --destino <pasta>       Pasta de destino para PDFs remediados
  --guardar <ficheiro>    Guardar relatório consolidado num ficheiro

EXEMPLOS:
  EN301549PdfProcessor analisar documento.pdf
  EN301549PdfProcessor analisar documento.pdf --json --guardar relatorio.json
  EN301549PdfProcessor remediar documento.pdf --destino ./acessiveis
  EN301549PdfProcessor lote ./pdfs --remediar --recursivo --guardar relatorio.txt

CRITÉRIOS VERIFICADOS (EN 301 549 / WCAG 2.1 Nível AA):
  9.1.1.1  Conteúdo não textual (texto alternativo em imagens)
  9.1.3.1  Informações e relações (estrutura de tags)
  9.1.3.2  Sequência com significado (ordem de leitura)
  9.1.4.3  Contraste (verificação manual necessária)
  9.2.4.1  Ignorar blocos (marcadores/bookmarks)
  9.2.4.2  Título de página (metadadas)
  9.2.4.4  Finalidade do link
  9.3.1.1  Idioma da página
");
    }
}
