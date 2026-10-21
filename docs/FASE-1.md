# Fase 1: esqueleto e design system

Entrega da fase 1 do [plano](PLANO.md): solução, projetos, injeção de dependências, log, configurações, histórico de alterações, navegação, barra lateral, busca, tema claro e escuro e a página "Design system".

## Como compilar, rodar e testar

Requisitos: Windows 10 22H2 ou 11, .NET SDK 10.0.402 (fixado em `global.json`).

```
dotnet build m1vClock.slnx
dotnet test --solution m1vClock.slnx
```

O app pede administrador (`app.manifest`). Para trabalhar na interface sem o prompt do UAC, compile com o manifesto de usuário comum:

```
dotnet build src/M1vClock.App -p:M1vAsInvoker=true
src/M1vClock.App/bin/Debug/net10.0-windows/win-x64/m1vClock.exe
```

Nesse modo os dados ficam em `%LocalAppData%\m1vClock-dev`. Com administrador, ficam em `%ProgramData%\m1vClock`, numa pasta protegida por ACL.

Atenção: `dotnet test` recompila o App sem `M1vAsInvoker`. Recompile com a opção acima antes de abrir o executável de desenvolvimento.

## Opções de linha de comando

Servem para desenvolvimento e capturas de tela.

| Opção | Efeito |
|---|---|
| `--page <id>` | Abre a página indicada (por exemplo `settings`, `history`, `design-system`). |
| `--theme light\|dark\|system` | Tema só desta sessão; não grava nas configurações. |
| `--overlay palette\|dialog` | Abre a busca ou um diálogo de exemplo. |
| `--scroll <px>` | Rola a página até o deslocamento indicado. |
| `--screenshot <arquivo.png>` | Grava a janela em PNG e fecha o app. |
| `--screenshot-delay <s>` | Espera antes de capturar (0,5 a 30 s; padrão 3 s). |

## Projetos

| Projeto | Papel |
|---|---|
| `M1vClock.Core` | Segurança, caminhos, configurações, histórico com desfazer, ponto de restauração, execução de processos, registro. Sem WPF e sem dependência de outros projetos. |
| `M1vClock.App` | Interface WPF: design system, shell, navegação, busca, diálogos, páginas. |
| `Hardware`, `Benchmarks`, `Tuning`, `Optimize`, `Diagnostics`, `Games`, `Integrations`, `Maker` | Módulos das próximas fases. Hoje só registram seus serviços e dependem só do Core. |

Testes:

| Projeto | O que cobre |
|---|---|
| `M1vClock.Core.Tests` | Gravação atômica, configurações, histórico, registro, ponto de restauração, processos, segurança, sessão. |
| `M1vClock.App.Tests` | Contraste AA das duas paletas, molas, busca, catálogo de páginas, opções de linha de comando, escala tipográfica, idioma, página Configurações. |
| `M1vClock.Architecture.Tests` | Regras do projeto aplicadas ao código (abaixo). |

## Regras verificadas automaticamente

Os testes de arquitetura leem o código com o Roslyn e falham se alguém quebrar uma regra:

- Nenhum comentário em C# nem em XAML.
- Nenhum `catch` vazio. Todo `catch` relança, registra no log ou repassa a exceção; só cancelamento e uma sonda de codificação no `ProcessRunner` ficam de fora.
- Toda chave de texto usada existe em `Strings.resx`; o inglês tem as mesmas chaves e os mesmos marcadores `{n}`.
- Sem endereços `http://`, sem `EmptyWorkingSet`, processos externos só por `ProcessRunner` e `ShellLauncher`, P/Invoke só com a busca de DLL restrita a `System32`, sem pacotes de telemetria, só o nuget.org como fonte.
- O Core não depende de WPF nem de outros projetos; os módulos dependem só do Core; versões de pacote centralizadas e exatas; a solução lista todos os projetos.

## Design

- Fonte Inter (texto) e Inter Display (22 px ou mais), números de largura fixa.
- Escala de texto fixa: 34, 22, 17, 15, 13 e 11 px. Cartões com raio 12, controles com raio 8, grade de 8 px.
- Uma cor de destaque; verde, amarelo e vermelho só indicam estado, sempre com texto e ícone.
- Temas claro e escuro desenhados separadamente, com contraste AA medido por teste.
- Mica só na barra lateral e na barra de título (Windows 11 22H2 ou mais novo); sem Mica, usa cor sólida.
- Movimento por molas interrompíveis (amortecimento e resposta), com resposta ao pressionar, impulso e resistência elástica nas bordas. Com as animações do Windows desligadas, vira transição de opacidade.
- Ícones Lucide gerados por `tools/icons/build-icons.mjs` a partir de `tools/icons/icons.json`.

## Idiomas

Português (padrão) e inglês, em `Localization/Strings.resx` e `Strings.en.resx`. A escolha fica em Configurações e vale depois de reiniciar.

## O que a fase 1 não verifica

- Execução como administrador e os 2 testes do Core que exigem administrador (`HardenedDirectory`); ficam pulados.
- Ponto de restauração real (`SRSetRestorePointW`); previsto para a máquina virtual da fase 4.
- Tema de alto contraste, tamanho de texto do Windows acima de 100 % e Windows 10; o código trata esses casos, mas não foram exercitados.
- Dados dos sensores: a página Design system usa valores simulados.
