# EN 301 549 PDF Processor — v2

Solução C# para analisar e remediar PDFs em português conforme:

| Norma | Versão | Nível |
|---|---|---|
| **ETSI EN 301 549** | V3.2.1 (2021-03) | AA |
| **WCAG 2.1** | W3C Recommendation 2018 | AA |
| **WCAG 2.2** | W3C Recommendation 2023 | AA (novos critérios) |

---

## Novidades v2 — WCAG 2.1 + 2.2

### Arquitetura

```
ValidadorAcessibilidadePdf   ← Orquestrador principal
    └── ValidadorWcag        ← Todos os critérios WCAG 2.1 e 2.2 por princípio
```

### Relatório multi-norma

O relatório agora mostra pontuação independente para cada norma:

```
📊 PAINEL DE CONFORMIDADE
──────────────────────────────────────────────────────────────────────────
  ⚠️  Global                  62.3%  [████████████░░░░░░░░]  NÃO CONFORME
  ⚠️  EN 301 549 v3.2.1       65.1%  [█████████████░░░░░░░]  NÃO CONFORME
  ⚠️  WCAG 2.1 AA             68.0%  [█████████████░░░░░░░]  NÃO CONFORME
  ❌  WCAG 2.2 AA             55.0%  [███████████░░░░░░░░░]  NÃO CONFORME
```

---

## Critérios Verificados

### WCAG 2.1 — Princípio 1: Percetível

| Critério | Nome | Nível | Verificação |
|---|---|---|---|
| **1.1.1** | Conteúdo Não Textual (imagens) | A | Auto |
| **1.2.1–5** | Multimédia (áudio/vídeo) | A/AA | Advertência |
| **1.3.1** | Informações e Relações (tags) | A | Auto |
| **1.3.2** | Sequência com Significado | A | Auto |
| **1.3.3** | Características Sensoriais | A | Advertência |
| **1.3.4** | Orientação | AA | Auto |
| **1.3.5** | Propósito do Campo | AA | Advertência |
| **1.4.1** | Utilização da Cor | A | Advertência |
| **1.4.3** | Contraste Mínimo (4.5:1) | AA | Advertência |
| **1.4.4** | Redimensionar Texto | AA | Advertência |
| **1.4.5** | Imagens de Texto / PDF scan | AA | Auto (heurística) |
| **1.4.10** | Reflow | AA | Advertência |
| **1.4.11** | Contraste Sem Texto | AA | Advertência |
| **1.4.12** | Espaçamento de Texto | AA | Advertência |
| **1.4.13** | Conteúdo em Hover/Foco | AA | Advertência |

### WCAG 2.1 — Princípio 2: Operável

| Critério | Nome | Nível | Verificação |
|---|---|---|---|
| **2.1.1** | Teclado | A | Auto |
| **2.1.2** | Sem Bloqueio de Teclado | A | Auto |
| **2.2.1** | Tempo Ajustável | A | Advertência |
| **2.2.2** | Pausar, Parar, Ocultar | A | Advertência |
| **2.3.1** | Três Flashes | A | Auto |
| **2.4.1** | Ignorar Blocos / Marcadores | A | Auto |
| **2.4.2** | Título de Página | A | Auto |
| **2.4.3** | Ordem do Foco | A | Auto |
| **2.4.4** | Finalidade do Link | A | Auto |
| **2.4.5** | Várias Formas | AA | Advertência |
| **2.4.6** | Cabeçalhos e Rótulos | AA | Advertência |
| **2.4.7** | Foco Visível | AA | Advertência |
| **2.5.3** | Rótulo no Nome | A | Advertência |

### 🆕 WCAG 2.2 — Novos critérios AA

| Critério | Nome | Nível | Verificação |
|---|---|---|---|
| **2.4.11** | Foco Não Obscurecido (Mínimo) | AA | Advertência |
| **2.4.12** | Foco Não Obscurecido (Melhorado) | AAA | Advertência |
| **2.4.13** | Aparência do Foco | AA | Advertência |
| **2.5.8** | Tamanho do Alvo (Mínimo) 24×24px | AA | **Auto** |
| **3.2.6** | Ajuda Consistente | AA | Advertência |
| **3.3.7** | Autenticação Redundante (Acessível) | AA | Advertência |
| **3.3.8** | Autenticação Sem Teste Cognitivo | AA | Advertência |

### WCAG 2.1 — Princípios 3 e 4

| Critério | Nome | Nível | Verificação |
|---|---|---|---|
| **3.1.1** | Idioma da Página | A | Auto |
| **3.1.2** | Idioma das Partes | AA | Advertência |
| **3.2.1** | Em Foco | A | Auto |
| **3.2.2** | Em Entrada | A | Advertência |
| **3.3.1** | Identificação do Erro | A | Advertência |
| **3.3.2** | Rótulos ou Instruções | A | Auto |
| **4.1.1** | Análise (Parsing) | A | Auto |
| **4.1.2** | Nome, Função, Valor | A | Auto |
| **4.1.3** | Mensagens de Estado | AA | Advertência |

### EN 301 549 — Cláusulas Específicas

| Cláusula | Descrição | Verificação |
|---|---|---|
| **10.x** | Permissões de acessibilidade (PDF encriptado) | Auto |
| **PDF/UA** | Conformidade ISO 14289 (XMP, DisplayDocTitle) | Auto |
| **12.1.2** | Documentação acessível | Advertência |

---

## Instalação

```bash
dotnet restore
dotnet build --configuration Release
```

## Utilização

```bash
# Análise com painel multi-norma
dotnet run -- analisar documento.pdf

# Análise com relatório JSON completo
dotnet run -- analisar documento.pdf --json --guardar relatorio.json

# Remediação automática
dotnet run -- remediar documento.pdf --destino ./acessiveis

# Lote com relatório consolidado
dotnet run -- lote ./documentos --recursivo --guardar resumo.txt
```

## Integração em C\#

```csharp
var lf = LoggerFactory.Create(b => b.AddConsole());
var validador = new ValidadorAcessibilidadePdf(lf.CreateLogger<ValidadorAcessibilidadePdf>());

var resultado = validador.Analisar("documento.pdf");

// Pontuações por norma
Console.WriteLine($"EN 301 549 : {resultado.ConformidadeEN301549.Pontuacao:F1}%");
Console.WriteLine($"WCAG 2.1   : {resultado.ConformidadeWCAG21.Pontuacao:F1}%");
Console.WriteLine($"WCAG 2.2   : {resultado.ConformidadeWCAG22.Pontuacao:F1}%");

// Filtrar apenas violações WCAG 2.2
var novasWcag22 = resultado.Violacoes
    .Where(v => v.Norma == NormaOrigem.WCAG22)
    .ToList();
```

## Executar Testes (20 testes)

```bash
cd Tests
dotnet test --verbosity normal
```

---

## Limitações da Verificação Automática

A verificação automática cobre ~40% dos critérios WCAG. Os restantes requerem:

- **Contraste** — Use Colour Contrast Analyser ou PAC 2024
- **Texto alternativo** — Validação semântica manual (é descritivo?)
- **Ordem de leitura** — Painel de Ordem do Adobe Acrobat Pro
- **Cabeçalhos** — Hierarquia H1>H2>H3 correta
- **Foco visível** — Teste em Adobe Reader / Edge PDF com teclado

---

## Referências

- [EN 301 549 V3.2.1 (PDF)](https://www.etsi.org/deliver/etsi_en/301500_302000/301549/03.02.01_60/en_301549v030201p.pdf)
- [WCAG 2.1 — W3C](https://www.w3.org/TR/WCAG21/)
- [WCAG 2.2 — W3C (2023)](https://www.w3.org/TR/WCAG22/)
- [What's New in WCAG 2.2](https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/)
- [PAC 2024 — PDF Accessibility Checker](https://www.access-for-all.ch/en/pdf-accessibility-checker.html)
- [iText 8 Docs](https://itextpdf.com/products/itext-core)

> **Licença iText**: AGPL v3 (open source) ou comercial.
