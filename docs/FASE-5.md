# Fase 5: jogos e overclock

Entrega da fase 5 do [plano](PLANO.md): o grupo **Jogos** (Biblioteca, FPS, Gargalo, Sobreposição) e a página **Overclock** (placa de vídeo, verificação de estabilidade, erros de hardware recentes e guia de BIOS), com o módulo `M1vClock.Tuning` e o `SessionGuard`.

## Como compilar, rodar e testar

```
dotnet build m1vClock.slnx
dotnet test --solution m1vClock.slnx
```

Para ver uma página direto: `m1vClock.exe --page library|fps|bottleneck|overlay|overclock --theme light|dark --screenshot arquivo.png --screenshot-render --screenshot-delay <segundos>`.

## Regras que valem para toda a fase

- **Jogos e programas auxiliares abrem sem administrador**, pelo shell do Explorer, mesmo com o m1vClock elevado (problema S7 do plano).
- **Só endereços montados e validados** são abertos: `steam://rungameid/<número>`, `com.epicgames.launcher://apps/<nome seguro>`, `uplay://launch/<número>/0`, o cliente da Riot com produto e patchline conferidos, ou um `.exe` local (nunca de rede) com argumentos de até 1024 caracteres e sem quebra de linha.
- **Mudança no Windows passa pelo diário.** As preferências de GPU e de tela cheia de um jogo vão para o `HKCU` pelo `ChangeJournal` e aparecem no Histórico, onde podem ser desfeitas.
- **Hardware não é gravado neste PC.** A AMD só é lida; a NVIDIA (sem placa aqui) só foi exercitada com uma NVML falsa.

## Jogos

| Página | O que faz | Detalhes que importam |
|---|---|---|
| Biblioteca | Steam (as duas bibliotecas), Epic, Riot, **GOG e Ubisoft** (registro, visão 32 bits) e **jogos adicionados à mão** (`.exe`) | Jogos manuais e perfis ficam em `games.json` na pasta do usuário; remover um jogo manual só tira da lista, nunca apaga arquivo |
| Perfil por jogo | Engine (detectada pela pasta), modo de janela, resolução, taxa, argumentos extras, prioridade, GPU de alto desempenho, desativar otimizações de tela cheia, executável e programas auxiliares | Ver abaixo |
| FPS | Estimativa por jogo para a CPU e a GPU deste PC, em 1080p/1440p/4K, quatro qualidades e upscaling; marca os jogos instalados | Tabelas **aproximadas**; HowManyFPS opcional com chave |
| Gargalo | Já existia: o que limita o jogo, com FPS e 1% low pelo ETW | |
| Sobreposição | Caixa por cima do jogo com FPS, tempo de quadro, 1% low, gráfico e sensores | Ver abaixo |

### Perfis e argumentos

- **Engine detectada pela pasta:** `UnityPlayer.dll` (na pasta ou numa subpasta) é Unity, `Engine\Binaries` é Unreal, `game\bin\win64\engine2.dll` ou `bin\engine.dll` é Source. Neste PC: CS2 e Deadlock (Source), TaskbarHero e Kandidatos (Unity), PUBG e FPSAimTrainer (Unreal); GTA V fica sem engine reconhecida.
- **Argumentos por engine:** Source `-fullscreen`/`-windowed`/`-windowed -noborder`, `-w -h`, `-freq`; Unity `-screen-fullscreen` e `-window-mode`, `-screen-width -screen-height`; Unreal `-fullscreen`/`-windowed`, `-ResX= -ResY=`. Valores fora de 320–16384 px ou 24–1000 Hz são recusados antes de qualquer coisa.
- **Para onde vão os argumentos:** jogos manuais e GOG recebem direto no `.exe`. A **Steam não aceita argumentos pelo atalho**: o botão "Gravar na Steam" grava nas opções de inicialização do `localconfig.vdf`, só com a Steam fechada. Antes de gravar, o arquivo é lido e escrito de volta em memória; se não sair idêntico, nada é gravado. A primeira versão do arquivo fica em `localconfig.vdf.m1vclock-original` e nunca é sobrescrita. Epic, Riot e Ubisoft abrem o jogo pelo próprio aplicativo: os argumentos são ignorados e a sessão avisa.
- **Preferências do Windows:** GPU de alto desempenho (`UserGpuPreferences`) mexe só no item `GpuPreference` e preserva as outras opções do mesmo valor (problema R11 do plano). Desativar otimizações de tela cheia mexe só no seu marcador dentro de `AppCompatFlags\Layers`. Ambas precisam do executável: Steam e Epic não dizem qual é, então o perfil tem um campo para escolher.
- **Sessão:** com perfil, Jogar aplica as preferências, abre os auxiliares, abre o jogo e o acompanha até ele fechar (até 3 minutos para aparecer; acompanha também o launcher que fecha e abre o jogo de verdade). A prioridade é definida no processo achado pela pasta do jogo; se o anti-cheat recusar, vira aviso. **Auxiliares só fecham se foi o m1vClock que os abriu** (um auxiliar que já estava aberto antes nunca é fechado). A sessão continua ao trocar de página e pode ser parada.
- **Não feito:** trocar o plano de energia durante o jogo (o módulo de jogos não depende do de otimização; o plano fica na página Ajustes).

### FPS estimado

- O catálogo e os índices vieram do app original, com placas e processadores mais antigos acrescentados (deste PC: RX 5500 XT = 36, Ryzen 7 5700X = 77; RTX 4070 e Ryzen 7 7700X = 100). Exemplo deste PC: CS2 em 1080p Alta dá 122 FPS, limitado pela placa de vídeo.
- **HowManyFPS:** só com chave do usuário, cifrada por DPAPI para o usuário do Windows; envia só nomes de CPU, GPU, jogo, resolução e upscaling; respostas guardadas por 7 dias. **O formato da API não foi testado** (sem chave); os cabeçalhos de autenticação são os mesmos do original.
- Os gêneros do catálogo estão só em português.

### Sobreposição

- Janela transparente, sempre no topo, que **ignora clique e não ativa** (conferido pela UIA: estilos `0x80800A8`), e não aparece na barra de tarefas. Segue o monitor da última janela em primeiro plano que não é o m1vClock e se reposiciona a cada atualização (duas por segundo).
- Atalho global configurável (`RegisterHotKey`, padrão Ctrl+Shift+F10); se outro programa já usa o atalho, o anterior continua valendo. Exige um modificador, exceto teclas F.
- Itens: FPS, tempo de quadro, 1% low, gráfico, uso/temperatura/hotspot/clock/clock da memória/consumo/ventoinha da GPU, VRAM usada e %, uso total/núcleo mais ocupado/temperatura/clock/consumo da CPU, RAM usada e %, processo e hora; atalhos "Só FPS", "Padrão" e "Tudo".
- Sensores e quadros só são lidos com o overlay ou a página abertos. **Não aparece em tela cheia exclusiva antiga** (exigiria injetar código no jogo).

## Overclock

| Parte | O que faz | Segurança |
|---|---|---|
| Placa de vídeo AMD | Lê pela **ADLX** (`amdadlx64.dll` do driver): faixas de clock e tensão, limite de energia atual, padrão e faixa, e se está de fábrica | **Só leitura** nesta versão |
| Placa de vídeo NVIDIA | Lê e ajusta pela **NVML**: limite de energia em W e teto de clock (sem teto, máximo −300/−500/−800 MHz); "Voltar ao padrão" | Exige administrador e confirmação; recusa valores fora da faixa da placa antes de chamar o driver; nunca sobe clock |
| Verificação de estabilidade | CPU+RAM, GPU ou tudo, por 5, 15 ou 30 minutos, usando a sessão de estresse da fase 3 com os mesmos limites de temperatura | Vigia o log do Windows a cada 5 s e para no primeiro erro |
| Erros recentes | WHEA, resets do driver de vídeo e desligamentos inesperados dos últimos 7 dias | Só leitura do log System |
| Guia de BIOS | Passos por fabricante da placa (ASUS, Gigabyte, MSI, ASRock, outros) e família da CPU (AM4, AM5, Intel K, Intel sem K) | Só texto; dados fora do code-behind |

- **Veredito:** erro WHEA, reset do driver (só `Display` 4101 e `nvlddmkm` 13/153; o original contava qualquer evento do `amdkmdag`), desligamento inesperado, erro de memória ou carga que caiu = **instável**. Parada por temperatura, sensor perdido, janela travada, parada pelo usuário, menos de 5 minutos ou log ilegível = **inconclusivo**. Sem nada disso = **estável**, com o lembrete de usar o PC um dia para Curve Optimizer.
- **SessionGuard:** quando um ajuste da NVIDIA é aplicado, um marcador fica em `state\gpu-tuning.json`. Na abertura seguinte, se a sessão anterior caiu, se houve reset do driver ou desligamento inesperado desde o ajuste, a placa volta ao padrão e um aviso aparece. "Voltar ao padrão" apaga o marcador.
- **Bibliotecas do driver:** carregadas só de `System32`. A assinatura não é conferida pelo `TrustVerifier` porque essas DLLs são assinadas por catálogo (a `amdadlx64.dll` deste PC, versão 1.4.0.121, é assinada pelo catálogo WHQL da Microsoft); `System32` só é gravável por administrador.
- A interface da ADLX foi feita a partir dos cabeçalhos C do SDK (MIT, GPUOpen), chamando as tabelas de funções diretamente; não há DLL de ponte.

## Achados reais neste PC

- **ADLX na RX 5500 XT:** clock ajustável de 800 a 2.200 MHz, tensão de 700 a 1.150 mV (pela interface de ajuste da geração RDNA 1), limite de energia de −50% a +20%, atual 0%, padrão 0%, de fábrica.
- **Log System (30 dias):** 0 WHEA, 0 resets do driver de vídeo, 7 desligamentos inesperados (Kernel-Power 41).
- **Placa-mãe** ASUS TUF GAMING B550M-PLUS com Ryzen 7 5700X: o guia sai para AM4 com D.O.C.P., PBO e Curve Optimizer (−10 a −25).
- **Steam:** o `localconfig.vdf` real volta idêntico depois de lido; o Deadlock (1422450) tem opções de inicialização, o CS2 não.
- A Epic não tem mais jogos instalados neste PC (só a pasta `Pending`); GOG e Ubisoft não estão instalados.
- O PC agora se chama `m1v`, o que fazia um teste de vazamento achar o nome do PC dentro de "m1vclock" (corrigido no teste).

Correções que a fase trouxe para o resto do app: o leitor de logs entende linhas sem origem (`[FTL] : Falha ao iniciar`, que um travamento real gravou); a limpeza da pasta dos testes de benchmark tenta de novo se um arquivo ainda está aberto; há um estilo de campo de senha.

## O que foi testado de verdade e o que não foi

**Testado com dados reais deste PC:** biblioteca real (Steam e Riot), detecção de engine nas pastas reais, leitura e escrita em memória do `localconfig.vdf` real, preferências de GPU e de tela cheia de um executável fictício gravadas no `HKCU` real pelo diário e desfeitas, lista de processos real, ADLX real (duas leituras iguais), log System real, estimativa com a CPU e a GPU reais, página Overlay e janela do overlay pela UIA (estilos, sensores reais, esconder), verificação de estabilidade de GPU pela interface parada aos 21 s (veredito inconclusivo, máx. GPU 60 °C), todas as páginas novas por captura. **Em Release elevado (26/10/2026, um UAC):** as quatro páginas novas abriram; o overlay mostrou "aguardando jogo" (a leitura de quadros por ETW ligou, sem o aviso de administrador) e a CPU com temperatura, clock e consumo (49 °C, 4,19 GHz, 57 W); a verificação de CPU+RAM rodou 28 s pela interface (máx. CPU 53 °C), parou pelo botão e deu inconclusivo.

Um defeito real apareceu ao rodar pela interface e foi corrigido: o botão Parar da verificação ficava desabilitado enquanto ela rodava (o comando assíncrono não aceitava uma segunda execução), então ninguém conseguia parar antes do fim.

**Testado só com dados falsos:** GOG e Ubisoft (formato do registro conforme os lançadores gravam, sem instalação aqui), NVML e todo o ajuste NVIDIA, SessionGuard restaurando de verdade, cliente da HowManyFPS (servidor falso), sessão de jogo (processos simulados).

**Nunca rodado de verdade, de propósito:** clicar em Jogar com perfil (abriria um jogo com anti-cheat), gravar o `localconfig.vdf` real, qualquer gravação na placa de vídeo.

**Ainda sem validação:** FPS no overlay com um jogo de verdade; atalho global apertado no teclado; overlay sobre jogo em tela cheia; uma placa NVIDIA; GOG e Ubisoft instalados; a verificação de estabilidade completa de 5 minutos ou mais; a API da HowManyFPS com chave.

## Testes

Projeto novo `M1vClock.Tuning.Tests`. Os testes que dependem do hardware deste PC (ADLX, pastas de jogos, `localconfig.vdf`) são pulados quando o item não existe.
