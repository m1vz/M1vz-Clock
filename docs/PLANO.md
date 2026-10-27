# m1vClock · Plano (fase 0)

Data: 21/10/2026
Referência lida: app original v0.16 (commit `ce1d9b3`), clonado em `_ref/` e fora do git do m1vClock.

Foram lidos os 115 arquivos do repositório: 13.971 linhas de C#, 3.641 de XAML, README, NOVIDADES, LEIA-ME, workflow, `publicar.bat`, manifest e `.csproj`. Os binários de driver em `Drivers/` foram conferidos pelo `Drivers/LEIA-ME.md` e pela origem declarada.

Sumário

1. Resumo
2. Máquina de desenvolvimento e o que dá para testar aqui
3. Mapa de funcionalidades: App original → m1vClock
4. Problemas encontrados no app original
5. Arquitetura do m1vClock
6. Dependências e licenças
7. Conexões com a internet
8. Riscos
9. Fases
10. Decisões que dependem de você
11. Fontes consultadas

---

## 1. Resumo

- Os 6 problemas citados no briefing foram confirmados no código, com arquivo e linha na seção 4.
- Há mais 10 problemas de segurança. O mais grave: o app roda como administrador, mas lê e executa coisas de pastas que qualquer programa do usuário pode alterar (`%AppData%`, `%TEMP%`, `Downloads`, `%LocalAppData%\Microsoft\WindowsApps`). Isso permite que um programa sem privilégio ganhe administrador, ou até instale um driver vulnerável. A correção entra na arquitetura (seção 5.3).
- **Licenças dos drivers seriais:** a licença do CP210x **proíbe** redistribuir (Silicon Labs, seção 4(g)); a do FTDI só permite para quem vende o dispositivo; a do CH340 não é clara. O m1vClock não vai embutir nenhum. Ver seção 6.3.
- A DLL oficial do iCUE SDK (v4.0.84) é assinada por `Corsair Memory, Inc.` (conferido hoje). Dá para exigir assinatura sem perder o recurso.
- **.NET 10** é viável: LibreHardwareMonitorLib 0.9.6, Vortice 3.8 e TraceEvent 3.x usam alvos compatíveis (net8.0/netstandard2.0). A compilação na fase 1 confirma.
- Esta máquina tem **GPU AMD sem ray tracing por hardware**, **Windows 11 Home** (sem Windows Sandbox) e **nenhum SDK do .NET**. Isso muda o plano de testes (seções 2 e 8).
- A skill **apple-design não está instalada** neste ambiente. É a primeira decisão da seção 10.

---

## 2. Máquina de desenvolvimento e o que dá para testar aqui

| Item | Valor |
|---|---|
| Sistema | Windows 11 Home 25H2, build 26200 |
| CPU | AMD Ryzen 7 5700X (8 núcleos, 16 threads) |
| GPU | AMD Radeon RX 5500 XT (RDNA 1, `1002:7340`), driver 32.0.21045.16010 |
| Placa-mãe | ASUS TUF GAMING B550M-PLUS |
| RAM | 16 GB |
| Discos | Seagate ST1000VM002 (HDD 1 TB), NX-512 (SSD 512 GB) |
| Bateria | Não tem (PC de mesa) |
| .NET | Só runtime 8.0.31. **Nenhum SDK instalado** |
| PawnIO | Não instalado (sem temperatura/consumo da CPU até instalar) |
| Windows Sandbox / Hyper-V | Indisponíveis no Windows 11 Home |
| Virtualização instalada | Nenhuma (sem VirtualBox ou VMware) |
| Terminal de desenvolvimento | Sem administrador |

O que **não** dá para testar nesta máquina, e como fica:

| Recurso | Motivo | Como fica |
|---|---|---|
| Overclock NVIDIA (NVML) | GPU AMD | Código com testes unitários do que é lógica pura; teste real depende de um PC com NVIDIA |
| Benchmark de ray tracing (DXR 1.1) | RX 5500 XT não tem RT por hardware | Testo o caminho "placa sem suporte"; nota real depende de RTX 20+, RX 6000+ ou Arc |
| Bateria e `powercfg /batteryreport` | PC de mesa | Parser testado com relatórios de exemplo; teste real depende de um notebook |
| Corsair iCUE, Razer Chroma | Sem esses periféricos (a confirmar com você) | Testo carga segura da DLL, conexão recusada e erros; cores reais dependem do hardware |
| OpenRGB | Depende do OpenRGB rodando | A placa ASUS B550M deve aparecer no OpenRGB (Aura); dá para testar se você instalar o OpenRGB |
| Placas Arduino, ESP32, CH340, FTDI | Depende de você conectar | Diagnóstico testado com dispositivos simulados (parsers); teste real se você plugar uma placa |
| Alterações no sistema em sandbox | Home não tem Sandbox | Precisa de VM (decisão 3) |

O que **dá** para testar bem aqui: sensores AMD de CPU e GPU (com PawnIO), ADLX (overclock AMD, que hoje não existe no original), benchmarks de CPU, memória, disco e cena D3D11, stress, S.M.A.R.T. de HDD e SSD, rede, ETW/overlay, limpeza, ajustes, serviços, inicialização e logs.

---

## 3. Mapa de funcionalidades: App original → m1vClock

Legenda dos projetos: **App**, **Core**, **Hardware**, **Benchmarks**, **Tuning**, **Optimize**, **Diagnostics**, **Games**, **Integrations**, **Maker** (todos `M1vClock.*`).

### 3.1 Hardware

| Funcionalidade | Original | Novo | O que muda |
|---|---|---|---|
| SO, placa-mãe/BIOS, CPU por WMI | `SystemInfoCollector.cs` | Hardware `SystemInfo` | Modelos tipados em vez de árvore de strings; seriais marcados como sensíveis; `ReleaseDate` nulo não derruba a seção |
| RAM por módulo, resumo, EXPO/XMP estimado, canais, ECC | `SystemInfoCollector.CollectRam` | Hardware `Memory` | Mesma lógica, com testes (regex de velocidade no part number, canal pelo nome do slot); tipos LPDDR4/4X que faltam |
| GPU no resumo do sistema | `SystemInfoCollector.CollectGpu` | Hardware `Gpu` | Unificado com a página de GPU |
| Discos físicos | `SystemInfoCollector.CollectDisks` | Hardware `Storage` | — |
| Unidades com anel de ocupação | `DriveMap.cs` | Hardware `Storage.Volumes` + componente `Ring` no App | Leitura fora da thread da interface |
| Aviso de USB conectado/removido | `MainWindow.xaml.cs` (`OnDeviceChange`, `DiffUsb`), `ToastWindow` | Hardware `DeviceWatcher` + App `Notifications` | Serviço próprio, independente da janela principal; debounce testável |
| Sensores ao vivo (LibreHardwareMonitor) | `SensorMonitor.cs`, `SensorRow.cs` | Hardware `SensorService` | Serviço único a 1 s numa thread própria, publica instantâneos imutáveis; pausa durante benchmark de GPU |
| Diagnóstico do PawnIO e instalação | `SensorMonitor.DiagnoseCpuSensors`, `MainWindow.BtnFixCpuSensors_Click` | Hardware `PawnIo` + Integrations `Winget` | winget localizado de forma segura (seção 4, S2) |
| Página GPU estilo GPU-Z | `GpuInfoCollector.cs`, `Nvml.cs`, `Dxgi.cs`, `GpuSpecDb.cs` | Hardware `Gpu` (NVML só leitura, DXGI, PnP, registro, banco de specs) | Banco ampliado (RTX 20/16, RDNA 1/2, Intel Arc, notebooks); a GPU desta máquina (`1002:7340`) nem está no banco hoje |
| Ao vivo da GPU (P-state, PerfCap, NVENC/NVDEC, PCIe) | `GpuInfo.LiveExtras` | Hardware `Gpu.Live` | API de throttle reasons atualizada |
| S.M.A.R.T. com veredito | `DiskHealth.cs` | Hardware `Storage.Health` | Regras do veredito em função pura com testes |
| Bateria e energia | `BatteryInfo.cs` | Hardware `Power` | Novo: `powercfg /batteryreport /xml` interpretado |
| Teste de rede (latência, jitter, perda, DNS, velocidade, bufferbloat) | `NetworkTest.cs` | Hardware `Network` | Cálculos (jitter, perda, veredito) em funções puras com testes; sem 8.8.8.8 (decisão 9) |
| Relatório HTML/JSON sem seriais | `ReportBuilder.cs`, `MainWindow.Disks.BtnReport_Click` | Diagnostics `Report` | Filtro de privacidade por tipo de campo (não por texto "série"); nome do computador opcional |
| Visão geral | `MainWindow.Dash.cs` | App `Overview` | Gráfico com interpolação; saúde do PC calculada fora da interface |

### 3.2 Desempenho

| Funcionalidade | Original | Novo | O que muda |
|---|---|---|---|
| CPU path tracer multi/single, determinístico | `BenchRenderer.cs` | Benchmarks `Cpu.PathTracer` | Mesmo algoritmo, cena e fórmula de nota, para comparar com o app original |
| Simulação de jogo na CPU | `GameSimBench.cs` | Benchmarks `Cpu.GameSim` | Mesmo algoritmo |
| Cena de jogo D3D11 | `GpuGameBench.cs` | Benchmarks `Gpu.Scene` | Janela Win32 compartilhada (hoje copiada 3 vezes) |
| Ray tracing DXR 1.1, aquecimento + 3 passadas + mediana | `GpuRtBench.cs`, `MainWindow.Rt.cs` | Benchmarks `Gpu.RayTracing` | Mediana e variação em funções testadas |
| Memória e carregamento | `MemoryBench.cs` | Benchmarks `Memory` | Pasta temporária protegida (S5) |
| Disco estilo CrystalDiskMark | `DiskBench.cs` | Benchmarks `Disk` | Fila real com E/S assíncrona (hoje são 8 threads síncronas, não QD32); arquivo apagado mesmo se o app fechar |
| Stress CPU/RAM com limite e erro de memória | `StressTester.cs`, `MainWindow.xaml.cs` (stress) | Benchmarks `Stress.CpuRam` | Limites vigiados por um monitor de segurança próprio, fora da interface |
| Stress de GPU estilo FurMark | `GpuBurn.cs` | Benchmarks `Stress.Gpu` | Idem |
| Histórico | `AppSettings.BenchRuns/BenchRecords` | Benchmarks `Results` | Histórico próprio, sem limite de 10, com comparação e importação de arquivo de outro PC |

### 3.3 Jogos

| Funcionalidade | Original | Novo | O que muda |
|---|---|---|---|
| FPS estimado (HowManyFPS + estimativa local) | `GameCatalog.cs`, `HowManyFpsClient.cs`, `GameRow.cs` | Games `FpsEstimate` | Estimativa em função pura com testes; chave por DPAPI |
| Gargalo ao vivo e por sessão | `BottleneckAnalyzer.cs`, `MainWindow.xaml.cs` | Games `Bottleneck` | Classificação e distribuição testadas |
| Medição de FPS por ETW | `FrameMonitor.cs` | Games `Frames` | Limpa processos encerrados; investigar DxgKrnl para Vulkan/OpenGL |
| Overlay configurável + atalho global | `OverlayWindow.*`, `OverlayMetrics.cs`, `MainWindow.Overlay.cs` | Games `Overlay` (dados) + App `OverlayWindow` | Atalho configurável |
| Biblioteca Steam/Epic/GOG/Ubisoft/manual | `GameLibrary.cs` | Games `Library` | Testes com manifestos de exemplo |
| Perfil por jogo, argumentos por engine | `GameLauncher.cs`, `AppSettings.GameProfile` | Games `Profiles`, `Launcher` | Jogo e auxiliares abrem **sem** administrador (S6) |
| `localconfig.vdf` com backup | `Vdf.cs`, `SteamInfo` | Games `Steam` | Parser com testes; backup que não é sobrescrito; gravação atômica |

### 3.4 Overclock

| Funcionalidade | Original | Novo | O que muda |
|---|---|---|---|
| NVIDIA: limite de energia, trava de clock, restaurar | `MainWindow.Oc.cs`, `Nvml.cs` | Tuning `Nvidia` | Reescrito do zero; offset de clock se a NVML instalada permitir |
| AMD | Não existe | Tuning `Amd` via ADLX | Novo; testável nesta máquina |
| Verificação de estabilidade (WHEA, reset de driver, temperatura) | `MainWindow.Oc.cs`, `OcCheck.cs`, `HardwareErrors.cs` | Tuning `Stability` | Contagem de reset de driver corrigida; veredito testado |
| Guia de BIOS por fabricante | `MainWindow.Oc.BuildOcGuide` | Tuning `BiosGuide` | Dados por fabricante e família de CPU, fora do code-behind |
| Restaurar padrão após crash/TDR | Não existe | Tuning `SessionGuard` | Novo |

### 3.5 Otimização, reparo e diagnóstico

| Funcionalidade | Original | Novo | O que muda |
|---|---|---|---|
| Limpar lista standby | `Optimizer.CleanMemory` | Optimize `Memory.Standby` | Só standby, só abaixo de um limite; manual e automático; **sem** `EmptyWorkingSet` |
| Fechar apps em segundo plano | `Optimizer.ListApps/CloseApps` | Optimize `BackgroundApps` | Lista fora da interface; nada é morto sem confirmação |
| Ajustes do Windows com "restaurar originais" | `WindowsTweaks.cs` | Optimize `Tweaks` | Tudo pelo `ChangeJournal`; novos ajustes da seção 5 do briefing |
| Inicialização (Run, Run32, Startup, StartupApproved) | `StartupManager.cs`, `MainWindow.Startup.cs` | Optimize `Startup` | + tarefas agendadas de logon e serviços de terceiros; backups em pasta protegida |
| Instalar apps pelo winget | `AppInstaller.cs`, `MainWindow.Apps.cs` | Integrations `Winget` + Optimize `Apps` | Catálogo com categorias; desinstalador novo |
| Limpeza, serviços, reparo (SFC/DISM/rede/WU) | Não existem | Optimize `Cleanup`, `Services`, `Repair` | Novos |
| Eventos, telas azuis, confiabilidade, logs do app | Não existem | Diagnostics | Novos |

### 3.6 Drivers, RGB e Maker

| Funcionalidade | Original | Novo | O que muda |
|---|---|---|---|
| Inventário de drivers | `DriverManager.Inventory` | Integrations `Drivers` | — |
| NVIDIA automático (consulta, download, assinatura) | `DriverManager.CheckNvidiaAsync/DownloadAndLaunchAsync` | Integrations `Drivers.Nvidia` | Download em pasta protegida, editor conferido por nome exato |
| AMD pela página oficial | `DriverManager.CheckAmdAsync` | Integrations `Drivers.Amd` | "Melhor esforço" com saída clara para a página oficial |
| Windows Update (drivers) | `DriverManager.SearchWindowsUpdateAsync/Install...` | Integrations `Drivers.WindowsUpdate` | Ponto de restauração antes |
| Backup com pnputil | `DriverManager.BackupAsync` | Integrations `Drivers.Backup` | Saída do pnputil decodificada certo |
| Corsair iCUE | `CorsairSdk.cs`, `CorsairSdkInstaller.cs` | Integrations `Rgb.Corsair` | Carga segura da DLL (S1) |
| OpenRGB | `LightBackends.OpenRgbBackend` | Integrations `Rgb.OpenRgb` | Envio fora da interface |
| Razer Chroma | `LightBackends.RazerChromaBackend` | Integrations `Rgb.Razer` | Sem `.GetResult()` na interface |
| Efeitos por temperatura/carga | `MainWindow.Integ.cs` | Integrations `Rgb.Effects` | + perfis salvos e efeito por jogo |
| Portas COM fantasmas e ComDB | `UsbCleaner.cs`, `MainWindow.xaml.cs` | Maker `Devices`, `ComDb` | Bitmap do ComDB com testes; alteração pelo journal |
| Drivers seriais (diagnóstico, reinstalar, remover versão, restaurar) | `MakerDrivers.cs`, `MainWindow.Maker.cs` | Maker `SerialDrivers` | + ESP32-S2/S3/C3 (VID 303A), RP2040/RP2350 (2E8A); drivers baixados do fabricante, não embutidos |
| Arduino IDE (winget, portas presas, cache) | `MakerDrivers`, `MainWindow.Maker.cs` | Maker `ArduinoIde` | — |

### 3.7 Aplicativo

| Funcionalidade | Original | Novo |
|---|---|---|
| Instância única | `App.xaml.cs` (mutex + `FindWindow` pelo título) | App: mutex + pipe nomeado para trazer a janela |
| Tema claro/escuro | `ThemeManager.cs`, `Theme.xaml` | App `DesignSystem` (claro e escuro desenhados separadamente) |
| Abertura rápida com carga paralela | `MainWindow.LoadHardwareAsync` | App + Hardware (skeleton por página) |
| Configurações | `AppSettings.cs` | Core `Settings` (versão de schema, migração, gravação atômica) |
| Publicação | `publicar.yml`, `publicar.bat` | `.github/workflows` + Velopack |

---

## 4. Problemas encontrados no app original

### 4.1 Os seis do briefing (confirmados)

| # | Problema | Onde |
|---|---|---|
| 1 | `EmptyWorkingSet` em todos os processos | `Optimizer.cs:140-153` |
| 2 | DLL da Corsair aceita sem assinatura e carregada de `%AppData%` | `CorsairSdk.cs:85-93`, `CorsairSdkInstaller.cs:118` |
| 3 | 99 `catch { }` vazios (mais 7 `catch (Tipo) { }`, total 106) | 12 arquivos; os piores: `GameLibrary.cs` (11), `MainWindow.Integ.cs` (8), `GameLauncher.cs` (8), `DriverManager.cs` (8) |
| 4 | `MainWindow.xaml` com 2.905 linhas; code-behind de 4.503 linhas em 13 arquivos (o principal tem 1.686); nenhum teste | `MainWindow*.cs` |
| 5 | `MainWindow.Oc.cs` com cara de código descompilado (`valueOrDefault3`, `num2`, `obj4`) | `MainWindow.Oc.cs` inteiro |
| 6 | Raspagem frágil da página da AMD | `DriverManager.cs:205-231` |

Detalhe do item 2 que vai além do briefing: depois de procurar em `%AppData%`, a última tentativa é `NativeLibrary.TryLoad("iCUESDK.x64_2019.dll")` só pelo nome (`CorsairSdk.cs:93`), que usa a ordem de busca padrão do Windows, incluindo pastas do `PATH`. E o editor é conferido com `Contains("Corsair")` no texto do certificado (`CorsairSdkInstaller.cs:130`), o que aceita qualquer certificado com "Corsair" no nome.

### 4.2 Segurança: problemas não citados

Contexto: o app roda como administrador. Qualquer arquivo que ele lê, executa ou carrega de uma pasta onde um programa comum pode escrever vira um caminho para esse programa ganhar administrador.

| # | Problema | Onde | Impacto |
|---|---|---|---|
| S1 | Carga da DLL Corsair por nome, com busca no `PATH` | `CorsairSdk.cs:93` | DLL plantada vira código com administrador |
| S2 | O winget é executado a partir de `%LocalAppData%\Microsoft\WindowsApps\winget.exe`, pasta do usuário; o botão do PawnIO roda `cmd.exe /k winget ...` achando o winget pelo `PATH` | `AppInstaller.cs:59-60`, `MainWindow.xaml.cs:221-239` | Programa comum troca o `winget.exe` e ganha administrador |
| S3 | `settings.json` fica em `%AppData%` e alimenta ações privilegiadas: "Restaurar removidos" faz `File.Move(Backup, Original)` com caminhos lidos do JSON; restaura comando em `HKLM\...\Run`; regrava ComDB; executa `.exe` de jogos e auxiliares | `StartupManager.cs:157-170`, `AppSettings.cs`, `GameLauncher.cs:163-182` | Mover arquivo arbitrário como administrador = escalação de privilégio completa |
| S4 | Backup de drivers seriais em `%AppData%\<app>\DriversSeriais`, restaurado com `pnputil /add-driver *.inf /subdirs /install` | `MakerDrivers.cs:77, 191-195` | Programa comum planta um driver assinado porém vulnerável e o app instala no kernel |
| S5 | Pastas de nome fixo em locais graváveis: `%TEMP%\<app>\drivers-incluidos\<chip>` (com `Directory.Delete(recursive)` antes de extrair), `%TEMP%\<app>Bench`, `X:\<app>Bench` na raiz da unidade (usuário comum pode criar pasta em `C:\`), `%TEMP%\<app>\drivers` | `MakerDrivers.cs:255-256`, `MemoryBench.cs:99,160`, `DiskBench.cs:52-54`, `DriverManager.cs:80` | Uma junção no lugar da pasta faz o app apagar ou gravar onde não devia |
| S6 | Instalador de driver baixado é verificado e só depois executado, em pasta do usuário | `DriverManager.cs:316-321` | Troca do arquivo entre a verificação e a execução |
| S7 | Jogos, programas auxiliares, Steam, OpenRGB, navegador e o relatório HTML são abertos **com administrador** (herdam o token). O OpenRGB é procurado em `Downloads` e na Área de Trabalho | `GameLauncher.cs:168,177-182`, `MainWindow.Integ.cs:223-270`, vários `Process.Start(url)` | Jogo e navegador como administrador; `OpenRGB.exe` plantado em Downloads roda como administrador |
| S8 | `nvml.dll` carregada pelo nome via `DllImport` | `Nvml.cs:9` | A pasta do `.exe` vem antes do System32; com o `.exe` rodando de Downloads, DLL plantada roda como administrador |
| S9 | Distribuído como `.exe` avulso para rodar de qualquer pasta (normalmente Downloads) | README, `publicar.yml` | A pasta inteira do app é gravável pelo usuário |
| S10 | Ajustes em `HKCU` feitos pelo processo elevado | `WindowsTweaks.cs`, `StartupManager.cs`, `GameLauncher.cs:76-93` | Se um usuário padrão eleva com a senha de outra conta, os ajustes vão para o perfil errado |
| S11 | Revogação de certificado desligada na verificação Authenticode | `CorsairSdkInstaller.cs:178-181` | Certificado revogado é aceito |

### 4.3 Robustez e correção

| # | Problema | Onde |
|---|---|---|
| R1 | Gravação das configurações não é atômica, e uma leitura com erro devolve configurações vazias em silêncio. Na próxima gravação somem os backups de ajustes, de inicialização e do ComDB, ou seja, o "desfazer" | `AppSettings.cs:100-115` |
| R2 | "Restaurar originais" apaga os backups mesmo quando algum ajuste falhou ao restaurar | `WindowsTweaks.cs:137-143` |
| R3 | O backup do `localconfig.vdf` é sobrescrito a cada gravação; o original some na segunda vez | `GameLibrary.cs:239-240` |
| R4 | Thread da interface bloqueada: leitura de todos os sensores a cada 1 s, enumeração de USB, lista de processos (`MainModule` de cada um), consultas ao registro de eventos (saúde do painel a cada 60 s, verificador de OC a cada 5 s), envio RGB por TCP a cada 33 ms, `.GetResult()` no Razer | `MainWindow.xaml.cs:104-119`, `MainWindow.Dash.cs:336-373`, `MainWindow.Oc.cs:411-419`, `MainWindow.Integ.cs:74`, `LightBackends.cs:329` |
| R5 | Limites de segurança dependem da interface: o corte por temperatura roda no timer da janela; dá para iniciar o stress sem sensor de temperatura; o limite da CPU aceita até 110 °C; durante a cena de GPU, o ray tracing e a nota do verificador de OC os sensores ficam pausados e **não existe corte por temperatura** | `MainWindow.xaml.cs:109, 1426-1460, 1611-1614`, `MainWindow.Oc.cs:306` |
| R6 | Ponto de restauração só antes do driver NVIDIA (via PowerShell). Nada antes de ajustes, inicialização, ComDB, remoção de driver serial ou drivers do Windows Update | `DriverManager.cs:349-362` |
| R7 | O README diz "QD32" no teste de disco, mas o código usa 8 threads síncronas (fila efetiva 8) | `DiskBench.cs:133-167` |
| R8 | Todo evento do `amdkmdag` conta como "reset de driver de vídeo", o que dá falso "instável" em AMD | `OcCheck.cs:16` |
| R9 | O "overclock NVIDIA" só reduz: limite de energia e teto de clock. Não há offset. A API de throttle reasons usada está obsoleta e a NVML nunca é finalizada | `MainWindow.Oc.cs:56-60`, `Nvml.cs:49` |
| R10 | Filas de frames por processo nunca são limpas; Vulkan/OpenGL ficam de fora | `FrameMonitor.cs:27-73` |
| R11 | A preferência de GPU por jogo sobrescreve outras opções que o Windows guarda no mesmo valor | `GameLauncher.cs:76-81` |
| R12 | Teste de rede faz ping no 8.8.8.8 (Google), fora da lista de conexões permitidas do briefing | `NetworkTest.cs:152` |
| R13 | Banco de GPUs pequeno: não tem a RX 5500 XT desta máquina, RTX 20/16 nem Intel Arc; Tensor/RT cores calculados por fórmula | `GpuSpecDb.cs` |
| R14 | `Win32_BIOS.ReleaseDate` nulo derruba a seção inteira de placa-mãe; tipo de RAM sem LPDDR4/4X | `SystemInfoCollector.cs:75, 95-102` |
| R15 | Saída do `pnputil` lida como UTF-8; no Windows em português ela vem na página de código OEM e os acentos quebram | `MakerDrivers.cs:333-345` |
| R16 | "Fechar programas que prendem a porta" mata o processo à força, sem pedir para fechar | `MainWindow.Maker.cs:188` |
| R17 | Histórico de benchmark limitado a 10 itens e guardado dentro das configurações | `MainWindow.xaml.cs:759-762, 1071-1075` |
| R18 | Código repetido: janela Win32 dos testes de GPU (3 cópias), `PowerCfg` (2), `RefId` (2), formatação de bytes (5) | vários |
| R19 | 203 ocorrências de "RESET" e do nome do app: sessão ETW `<nome>-Frames`, user-agent, pastas, nome do cliente no OpenRGB, título e autor no Razer, classes de janela, mutex, relatório, textos | vários |

### 4.4 Interface e acessibilidade

| # | Problema | Onde |
|---|---|---|
| U1 | Templates personalizados removem o foco visível; os grupos do menu não recebem foco (`Focusable=False`); nenhum `AutomationProperties` | `Theme.xaml:186` e templates |
| U2 | Estados só por inversão de cor (cartão branco = alerta), sem cor semântica nem ícone | `MainWindow.xaml.cs:1537-1543`, `Theme.xaml:612-635` |
| U3 | Tamanhos de fonte fixos, sem respeitar a escala de texto do Windows | todo o XAML |
| U4 | Painel copiado de uma referência de app financeiro ("Total Balance", "Transactions", "Rewards" nos comentários); botões todos em maiúsculas | `MainWindow.xaml:377-541` |
| U5 | Emoji na interface (`✔`, `⚠`) | `MainWindow.Rt.cs:73-74` |
| U6 | `MessageBox` padrão do Windows para confirmações, sem "o que vai mudar" e "como desfazer" padronizados | vários |

### 4.5 Licenças

| # | Problema |
|---|---|
| L1 | `Drivers/CP210x` é redistribuído contra a licença da Silicon Labs (seção 4(g): proíbe redistribuir para terceiros) |
| L2 | `Drivers/FTDI`: a licença da FTDI permite distribuir junto com o dispositivo, não num utilitário geral |
| L3 | `Drivers/CH340`: sem licença clara de redistribuição |
| L4 | Os pacotes foram reduzidos a `.inf/.cat/.sys/.dll` e os arquivos de licença dos fabricantes ficaram de fora |

Observação técnica: no Windows 10 e 11 as placas com USB nativo (Arduino oficiais, ESP32-S2/S3/C3, RP2040/RP2350) usam o driver `usbser.sys` que já vem no Windows. O `.inf` do Arduino só é necessário em versões antigas.

---

## 5. Arquitetura do m1vClock

### 5.1 Solução

```
m1vClock.slnx
Directory.Build.props        net10.0-windows, Nullable, AnalysisLevel latest-recommended, TreatWarningsAsErrors
Directory.Packages.props     versões centralizadas
src/
  M1vClock.App               WPF, views, viewmodels, navegação, design system, bandeja, overlay
  M1vClock.Core              modelos, interfaces, Result, ChangeJournal, settings, segurança, processos
  M1vClock.Hardware          WMI, LibreHardwareMonitor, NVML/ADLX (leitura), DXGI, SMART, bateria, rede, USB
  M1vClock.Benchmarks        CPU, simulação, GPU D3D11/D3D12, memória, disco, stress, monitor de segurança
  M1vClock.Tuning            NVIDIA (NVML), AMD (ADLX), estabilidade, guia de BIOS, SessionGuard
  M1vClock.Optimize          limpeza, memória, ajustes, serviços, inicialização, apps, reparo
  M1vClock.Diagnostics       eventos, telas azuis, confiabilidade, logs, relatório
  M1vClock.Games             biblioteca, perfis, launcher, Steam/VDF, ETW, overlay (dados), FPS, gargalo
  M1vClock.Integrations      RGB, drivers, winget
  M1vClock.Maker             dispositivos, ComDB, drivers seriais, Arduino IDE
tests/
  M1vClock.<Projeto>.Tests   um por projeto, xUnit
  M1vClock.Architecture.Tests  regras: dependências entre projetos, nada de comentário em src, nada de catch vazio
docs/
.github/
```

Dependências: todos dependem de Core; App depende de todos; os módulos não se conhecem entre si, exceto Games e Benchmarks, que leem sensores de Hardware por interfaces do Core.

As regras "sem comentários" e "sem `catch` vazio" viram testes automáticos no projeto de arquitetura, que leem os `.cs` de `src/` e falham o build.

### 5.2 Serviços do Core que todo módulo usa

| Serviço | Papel |
|---|---|
| `ChangeJournal` | Toda alteração do sistema passa por ele: grava o valor original **antes**, data/hora, módulo, descrição em português; desfazer individual e "desfazer tudo". Cada tipo de alteração (`RegistryValueChange`, `ServiceStartTypeChange`, `PowerSchemeChange`, `FileQuarantine`, `DriverPackageRemoval`, `ComDbChange`, `StartupApprovedChange`...) implementa `Capture`, `Apply`, `Undo` e `Verify` |
| `RestorePointService` | Antes da primeira alteração do dia cria ponto de restauração via WMI `SystemRestore`; se a proteção estiver desligada, avisa e oferece ligar. Trata o limite de 24 h do Windows |
| `ProcessRunner` | Processos externos só com `ArgumentList` (nada de concatenar texto), caminho absoluto do executável no System32, timeout, saída decodificada na página de código certa e gravada no log |
| `TrustVerifier` | `WinVerifyTrust` com revogação + editor esperado comparado pelo nome exato do certificado (`CN` e `O`), não por "contém" |
| `SecureStore` | `%ProgramData%\m1vClock` com ACL só Administradores e SYSTEM (usuário só lê). Recusa pasta que seja junção/link. Subpastas: `journal`, `backups`, `native`, `downloads`, `temp` |
| `ShellLauncher` | Abre jogos, auxiliares, URLs, pastas e o relatório **sem administrador**, pelo shell do usuário (`IShellDispatch2.ShellExecute` do Explorer) |
| `SettingsStore` | Versão de schema, migração, gravação atômica (arquivo temporário + `File.Replace`), cópia de segurança da última versão boa; segredos com DPAPI |
| `SafeNativeLoader` | `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 \| LOAD_LIBRARY_SEARCH_APPLICATION_DIR)` no primeiro instante do `Main`; DLLs de terceiros só por caminho absoluto e com assinatura |
| `CrashReporter` | Exceções não tratadas vão para o log e para um marcador de sessão; na próxima abertura o app mostra o que aconteceu e oferece gerar o relatório (nada é enviado sozinho) |

### 5.3 Onde cada coisa fica

| Dado | Briefing | Proposta | Motivo |
|---|---|---|---|
| Configurações | `%AppData%\m1vClock\settings.json` | `%ProgramData%\m1vClock\users\<SID>\settings.json` | Perfis de jogo guardam `.exe` e argumentos; em pasta do usuário, um programa comum poderia trocá-los (S3). Decisão 7 |
| Journal e backups | — | `%ProgramData%\m1vClock\journal`, `backups` | O "desfazer" grava em HKLM, serviços e drivers: precisa de origem confiável |
| Logs | `%LocalAppData%\m1vClock\logs` | `%ProgramData%\m1vClock\logs` | Um processo administrador escrevendo em pasta do usuário pode ser redirecionado por link. Decisão 7 |
| DLL Corsair | — | `%ProgramData%\m1vClock\native` | Exigido no briefing |
| Downloads (drivers, SDK) | — | `%ProgramData%\m1vClock\downloads`, verificado com o arquivo aberto sem permitir escrita até a execução | S6 |
| Arquivos temporários de benchmark | — | Na unidade testada, pasta com nome aleatório e `FILE_FLAG_DELETE_ON_CLOSE`; varredura de sobras ao abrir o app | S5 e "limpar mesmo se o app fechar no meio" |
| App instalado | — | `Program Files\m1vClock` (MSI por máquina do Velopack) | S9. Decisão 6 |

### 5.4 Threads e dados ao vivo

- `SensorService`: thread própria, 1 leitura/s, publica um instantâneo imutável (`SensorSnapshot`) para quem assinar. A interface só recebe o instantâneo pronto e atualiza com interpolação.
- Durante benchmark de GPU a leitura geral pausa (como no original). O `SafetyMonitor` continua lendo **só** a temperatura do componente sob teste a cada 2 s, pela API mais leve disponível, e mede se isso afeta o 1% low (fase 3). Se afetar, a leitura cai para 5 s, mas nunca para.
- `SafetyMonitor` roda fora da interface: corta stress e benchmark por temperatura, por erro de memória, pela tecla Esc e se a janela do teste parar de responder. Os limites ficam em configuração própria, com valor máximo travado (ex.: CPU nunca acima de TjMax − 5 °C) e confirmação dupla para mudar.
- Páginas abrem com skeleton e se preenchem quando os dados chegam; a janela aparece antes de qualquer leitura de hardware.

### 5.5 Interface

- MVVM com CommunityToolkit.Mvvm, DI com `Microsoft.Extensions.Hosting`, uma página por View e ViewModel, navegação por serviço (sem `Visibility` em 21 grids).
- Design system em `App/DesignSystem`: tokens (cores claro e escuro, tipografia, espaçamento 8 px, raios, sombras, durações), controles (botão, cartão de métrica, gráfico ao vivo, lista, interruptor, diálogo de confirmação com "o que muda" e "como desfazer", barra lateral, skeleton, estados vazio/erro).
- Busca global Ctrl+K que indexa páginas e ações.
- Mica só na barra lateral e na barra de título, via `DwmSetWindowAttribute` (Windows 11 22H2+); no Windows 10, fundo sólido.
- Fonte Inter embutida, com números tabulares (`Typography.NumeralAlignment="Tabular"`) em todo valor ao vivo.
- Escala de texto do Windows: WPF só segue o DPI, não a "Tamanho do texto" da Acessibilidade. O app lê esse fator e aplica na escala tipográfica.
- Português (pt-BR) e inglês em `.resx`.

---

## 6. Dependências e licenças

### 6.1 Pacotes

| Pacote | Uso | Licença | Observação |
|---|---|---|---|
| .NET 10 / WPF | Base | MIT | SDK precisa ser instalado nesta máquina |
| CommunityToolkit.Mvvm | MVVM | MIT | |
| Microsoft.Extensions.Hosting | DI, ciclo de vida | MIT | |
| Serilog, Serilog.Sinks.File, Serilog.Extensions.Hosting | Log | Apache-2.0 | |
| LibreHardwareMonitorLib 0.9.6 | Sensores | MPL-2.0 | Alvos net8.0/netstandard2.0, roda no .NET 10. Binário sem modificação; aviso e link para o código no THIRD-PARTY-NOTICES. Dependências transitivas (HidSharp etc.) listadas no build |
| PawnIO | Driver de baixo nível usado pelo LHM | — | **Não redistribuído**: instalado pelo winget com consentimento |
| Microsoft.Diagnostics.Tracing.TraceEvent | ETW (FPS) | MIT | |
| Vortice.Direct3D11, Direct3D12, DXGI, D3DCompiler, Dxc | GPU | MIT | `dxcompiler.dll`/`dxil.dll` da Microsoft vêm com o pacote; licença redistribuível da Microsoft e NCSA. Carregados só da pasta do app |
| System.Management | WMI | MIT | |
| System.Security.Cryptography.ProtectedData | DPAPI | MIT | |
| Velopack | Instalador e atualização | MIT | |
| xUnit | Testes | Apache-2.0 | Sem FluentAssertions (a versão 8 virou licença comercial) |
| ADLX SDK (AMD) | Overclock AMD | MIT | Só os cabeçalhos para gerar a ponte; a DLL `amdadlx64.dll` vem com o driver da AMD |
| NVML | NVIDIA | — | `nvml.dll` vem com o driver da NVIDIA; carregada do System32 por caminho absoluto |
| iCUE SDK (Corsair) | RGB | Termos da Corsair | **Não redistribuído**: baixado do GitHub oficial quando o usuário pede; assinado por `Corsair Memory, Inc.` (v4.0.84, conferido em 21/10/2026) |
| OpenRGB | RGB | GPL-2.0 (o programa) | Só implementamos o cliente do protocolo de rede; nada do código do OpenRGB entra no m1vClock |
| Razer Chroma REST | RGB | Termos do Chroma SDK | API local do Synapse |
| HowManyFPS API | FPS estimado | Termos do serviço | Opcional, chave do usuário |

### 6.2 Fontes e ícones

| Item | Licença | Observação |
|---|---|---|
| Inter | SIL OFL 1.1 | Embutida no app; texto da licença no THIRD-PARTY-NOTICES |
| Lucide | ISC | Recomendado por ter traço único e peso uniforme. ISC é equivalente ao MIT na prática, mas não é MIT (decisão 12) |
| Phosphor | MIT | Alternativa; tem vários pesos, usaríamos só o Regular |

### 6.3 Drivers seriais

| Driver | Licença | Decisão proposta |
|---|---|---|
| CP210x (Silicon Labs) | Proíbe redistribuir (seção 4(g)) | Não embutir. Baixar de silabs.com ou abrir a página |
| FTDI | Distribuição só junto com o dispositivo | Não embutir. Baixar de ftdichip.com ou abrir a página |
| CH340/CH341 (WCH) | Sem licença clara | Não embutir. Baixar de wch-ic.com ou abrir a página |
| Arduino | Repositório GPL do Arduino IDE 1.x | Não embutir; desnecessário no Windows 10/11 (driver `usbser` nativo). Oferecer download do GitHub para quem precisar |

Todo download de driver: HTTPS, tamanho conferido, hash publicado quando houver, catálogo `.cat` com assinatura WHQL da Microsoft ou do fabricante esperado, arquivo apagado se não bater. Os domínios dos fabricantes não estão na lista de conexões permitidas do briefing (decisão 8).

### 6.4 Código reaproveitado do app original

MIT. O aviso de licença exigido fica no `THIRD-PARTY-NOTICES.md`. Código aproveitado: algoritmos dos benchmarks (para manter as notas comparáveis), parser VDF, regras de diagnóstico serial, mapa de fabricantes de GPU, textos do guia de BIOS. Toda a marca "RESET" sai.

---

## 7. Conexões com a internet

Zero telemetria. Lista completa (vai para o README e para Configurações › Privacidade):

| Destino | Quando | Recurso |
|---|---|---|
| `www.nvidia.com`, `gfwsl.geforce.com`, `us.download.nvidia.com` | Usuário verifica/baixa driver NVIDIA | Drivers |
| `www.amd.com`, `drivers.amd.com` | Usuário verifica/baixa driver AMD | Drivers |
| `api.github.com`, `github.com`, `objects.githubusercontent.com` | Usuário baixa o SDK da Corsair | RGB |
| `speed.cloudflare.com`, `www.cloudflare.com`, `1.1.1.1`, `1.0.0.1` | Usuário roda o teste de rede | Rede |
| `howmanyfps.com` | Só com chave cadastrada | FPS estimado |
| Fontes do winget (Microsoft e sites dos fabricantes dos apps) | Usuário instala ou atualiza app, PawnIO ou Arduino IDE | Apps |
| Windows Update | Usuário procura drivers no Windows Update | Drivers |
| GitHub Releases do m1vClock | Verificação de atualização (desligável) | Atualização |
| `localhost:6742`, `localhost:54235` | OpenRGB e Razer Synapse locais | RGB (não sai do PC) |
| Pendentes: `wch-ic.com`, `silabs.com`, `ftdichip.com`, `raw.githubusercontent.com` (Arduino) | Usuário baixa driver serial | Maker (decisão 8) |

---

## 8. Riscos

| Risco | Impacto | Mitigação |
|---|---|---|
| Sem VM: Windows 11 Home não tem Sandbox nem Hyper-V | Alto: o briefing exige testar alterações do sistema primeiro em sandbox | VMware Workstation Pro (gratuito para uso pessoal) ou VirtualBox com ISO de avaliação do Windows 11 (decisão 3) |
| UAC: o app exige administrador e eu não consigo clicar no UAC daqui | Alto: impede rodar o app de verdade para capturas | Builds de desenvolvimento com manifesto `asInvoker` para trabalhar na interface (sensores limitados); para dados reais, terminal de desenvolvimento aberto como administrador ou você aprova o UAC (decisão 5) |
| Repositório dentro do OneDrive | Médio: sincronização de `bin/obj/.git`, arquivos travados durante o build | Mover para `C:\dev\m1vClock` (decisão 2) |
| .NET 10 com dependências nativas | Baixo | Alvos compatíveis confirmados; plano B é .NET 8 |
| `win-arm64` | Médio | PawnIO, NVML e ADLX são x64. Proposta: v1 só x64 (decisão 10) |
| Velopack por máquina | Médio | MSI com `--instLocation PerMachine`; falta confirmar onde os pacotes de atualização ficam antes de aplicar (se em pasta do usuário, verificar assinatura do pacote antes de aplicar) |
| Assinatura de código | Médio: sem assinatura, SmartScreen avisa e o UAC mostra "editor desconhecido" | SignPath Foundation exige projeto público com algum histórico; pedir cedo, na fase 7 |
| ADLX em placa RDNA 1 | Médio | A RX 5500 XT pode expor menos ajustes; se faltar, a página mostra só leitura com o motivo |
| Offset de clock NVIDIA | Médio | Depende da versão da NVML do driver; sem testar aqui |
| Medição de FPS em Vulkan/OpenGL | Médio | ETW do DxgKrnl é mais complexo; se não ficar confiável, documentar a limitação como hoje |
| Overlay em tela cheia exclusiva | Baixo | Impossível sem injeção; limitação documentada |
| Leitura de temperatura durante benchmark de GPU | Médio | Medir o efeito no 1% low (fase 3) |
| Notas diferentes do app original | Médio | Mesmos algoritmos; o JIT do .NET 10 pode mudar a nota. Medir lado a lado e documentar a diferença. O teste de disco muda de propósito (fila real) e ganha versão própria |
| Escala de texto no WPF | Baixo | Ler o fator do Windows e aplicar à escala tipográfica |
| Pontos de restauração | Baixo | Windows ignora pedidos a menos de 24 h de outro ponto; tratar e avisar |
| Tamanho da v1 | Alto | Fases curtas com parada; cada fase entrega algo que roda |

---

## 9. Fases

Cada fase termina com: `dotnet build` sem erros e sem avisos, testes passando, app rodando com capturas, commits pequenos em português numa branch `fase/N-...`, resumo curto e espera do seu "ok" para juntar na `main`.

| Fase | Entrega | Testes principais | Tamanho |
|---|---|---|---|
| 0 | Este plano | — | aguardando ok |
| 1 | Solução e projetos, `Directory.Build.props`, DI, Serilog, settings com schema e migração, `ChangeJournal`, `SecureStore`, `ProcessRunner`, `ShellLauncher`, `SafeNativeLoader`, instância única, crash reporter, navegação, barra lateral, Ctrl+K, tema claro/escuro, página **Design system** (paleta, tipografia, botões, cartão de métrica, gráfico ao vivo, lista, interruptor, diálogo de confirmação, barra lateral) | Journal aplicar/desfazer/desfazer tudo; migração de settings; montagem de argumentos; regras de arquitetura; capturas claro e escuro | G |
| 2 | Visão geral, Sistema, Sensores (busca e filtros), GPU, Memória, Armazenamento + S.M.A.R.T., Energia/Bateria, Rede, USB/dispositivos | Veredito SMART, jitter/perda, parser do battery report, EXPO/canais, banco de GPU; comparação com o app original lado a lado | G |
| 3 | Benchmarks (todos), histórico, comparação e importação, stress CPU/RAM/GPU com `SafetyMonitor` | 1% low, mediana, variação; duas execuções com diferença abaixo de 3% nos determinísticos; mesma faixa de nota do app original; limpeza de temporários após encerrar o app à força | G |
| 4 | Otimização (limpeza, memória, ajustes, serviços, inicialização, apps), reparo, logs e diagnóstico (eventos, telas azuis, confiabilidade, relatório) | Regras de limpeza (caminhos proibidos nunca entram), parsers (powercfg, winget, minidump/BugCheck), cada alteração: aplicar → conferir → desfazer → conferir, primeiro na VM | G |
| 5 | Jogos (biblioteca, perfis, Steam, FPS estimado, gargalo, overlay), overclock NVIDIA e AMD, estabilidade, guia de BIOS, SessionGuard | VDF, argumentos por engine, gargalo, veredito de estabilidade; ADLX nesta máquina | G |
| 6 | Drivers, RGB (Corsair, OpenRGB, Razer, perfis, efeito por jogo), Maker, bandeja e alertas | Saída do pnputil, ComDB, diagnóstico serial; carga da DLL só com assinatura válida; DLL adulterada é recusada | M |
| 7 | Inglês, acessibilidade (teclado, foco, contraste AA, escala de texto), abertura em menos de 1 s, Velopack, CI, Dependabot, README pt/en, CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, templates, `docs/assinatura.md` | Navegação só por teclado em todas as páginas; tempo de abertura medido; instalação e atualização na VM | M |

---

## 10. Decisões que dependem de você

Bloqueiam a fase 1:

1. **Skill apple-design:** não está instalada neste ambiente (só `find-skills`). Você instala e eu carrego antes de desenhar, ou sigo com as regras de design do briefing?
2. **Pasta do projeto:** mover de `OneDrive\Documentos\projects\m1v-test` para `C:\dev\m1vClock`? Recomendo mover.
3. **Instalações nesta máquina:** posso instalar o .NET 10 SDK (winget, agora) e o PawnIO (fase 2)?
4. **Administrador para rodar o app:** Terminal de desenvolvimento aberto como administrador nas fases 2 a 7, ou você aprova o UAC quando eu pedir? Na fase 1 trabalho sem elevação.

Podem esperar a fase indicada (se não disser nada, sigo a recomendação):

5. **VM para testes que alteram o sistema (fase 4):** VMware Workstation Pro ou VirtualBox com Windows 11 de avaliação. Recomendo VMware.
6. **Instalador por máquina (fase 7):** Velopack MSI em `Program Files` em vez do padrão por usuário. Recomendo por máquina.
7. **Dados em `%ProgramData%\m1vClock` (fase 1):** configurações por usuário, journal, backups e logs, em vez de `%AppData%`/`%LocalAppData%`, pelos motivos da seção 5.3. Recomendo.
8. **Drivers seriais (fase 6):** liberar `wch-ic.com`, `silabs.com`, `ftdichip.com` e `raw.githubusercontent.com` para download com verificação, ou só abrir a página oficial e instalar de um arquivo que você baixar? Recomendo liberar.
9. **Teste de rede (fase 2):** trocar o 8.8.8.8 (Google) por 1.0.0.1 (Cloudflare). Recomendo trocar.
10. **ARM64:** deixar para depois da v1. Recomendo.
11. **Benchmarks (fase 3):** manter algoritmos e fórmulas idênticos ao app original para comparar notas; o teste de disco ganha fila real e versão nova. Recomendo.
12. **Ícones:** Lucide (ISC, traço único) ou Phosphor (MIT). Recomendo Lucide.
13. **Copyright e repositório (fase 7):** nome para o `LICENSE` (seu nome, "m1vClock contributors" ou outro) e o endereço do repositório no GitHub.

---

## 11. Fontes consultadas

- Velopack, instaladores e MSI por máquina: https://docs.velopack.io/packaging/installer
- LibreHardwareMonitorLib no NuGet (0.9.6, MPL-2.0): https://www.nuget.org/packages/LibreHardwareMonitorLib/
- ADLX (AMD): https://github.com/GPUOpen-LibrariesAndSDKs/ADLX e https://gpuopen.com/manuals/adlx/adlx-page_guide_bindcsharp/
- Exemplo de ADLX em C#: https://github.com/Rem0o/FanControl.ADLX
- Licença do driver CP210x (texto da Silicon Labs incluído no pacote): https://github.com/samdenty/Wi-PWN/blob/master/drivers/CP210x/SLAB_License_Agreement_VCP_Windows.txt
- Termos de licença dos drivers FTDI: https://ftdichip.com/driver-licence-terms/
- Página oficial do CH341SER (WCH): https://www.wch-ic.com/downloads/CH341SER_EXE.html
- Release do iCUE SDK conferida: https://github.com/CorsairOfficial/cue-sdk/releases/tag/v4.0.84
