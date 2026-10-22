# Fase 2: hardware

Entrega da fase 2 do [plano](PLANO.md): leitura do hardware e as nove páginas da seção Hardware. Tudo roda sem administrador; o que só aparece com administrador está dito em cada página.

## Como compilar, rodar e testar

```
dotnet build m1vClock.slnx
dotnet test --solution m1vClock.slnx
```

Para ver uma página direto (útil para capturas): `m1vClock.exe --page <id> --theme light|dark --screenshot arquivo.png --screenshot-delay <segundos>`. O atraso é em segundos (0,5 a 30). Páginas novas: `overview`, `system`, `sensors`, `gpu`, `memory`, `storage`, `power`, `network`, `devices`. A opção `--overlay toast` mostra dois avisos de exemplo.

## O que cada página mostra

| Página | Conteúdo | Observações |
|---|---|---|
| Visão geral | Quatro cartões ao vivo (CPU, GPU, memória, disco do sistema), gráfico de CPU contra GPU (temperatura, carga ou consumo; 1, 5 ou 15 min), saúde do PC e atividade recente | A saúde é refeita a cada 60 s e quando os sensores abrem. O histórico de 15 min é guardado desde que o app abre |
| Sistema | Windows, placa-mãe, BIOS e processador | Serial da placa-mãe mascarado até clicar no olho |
| Sensores | Todos os sensores do LibreHardwareMonitor, agrupados por componente, com busca, filtro por tipo, mín e máx, congelar e a explicação quando a CPU não mostra temperatura | Usa a leitura compartilhada de 1 s; o histórico da Visão geral a mantém ligada enquanto o app está aberto |
| Placa de vídeo | Identificação, unidades de processamento, memória (8 GB via DXGI), APIs e driver | Banco de especificações próprio; campos só NVIDIA ficam para a fase 5 |
| Memória | Módulos, canais, perfil XMP/EXPO e análise | |
| Armazenamento | Volumes com anel de uso, discos com veredito S.M.A.R.T. e motivos | Contadores e previsão de falha exigem administrador; a página avisa |
| Energia | Fonte de energia, plano ativo e bateria (se houver) | Desktop mostra a nota sobre a fonte (PSU) |
| Rede | Adaptador ativo, outros adaptadores e o diagnóstico de lag | O diagnóstico só roda quando você pede e pode ser cancelado |
| Dispositivos | USB conectados (nome, categoria, fabricante, COM, VID:PID), os com problema, atividade recente e a chave dos avisos | Avisos de conectar e remover USB e unidades |

## Diagnóstico de rede

Mede com ping a cada 200 ms (30 pacotes) o roteador, `1.1.1.1` e `1.0.0.1`, a resolução de DNS de `www.cloudflare.com`, download (8 s) e upload (6 s) em `speed.cloudflare.com`, e a latência sob carga durante o download. Dura cerca de 25 s e não grava nada no disco.

- Só fala com esses hosts. Download e upload usam HTTPS, sem seguir redirecionamento, com o tamanho da resposta conferido e um limite de bytes por requisição.
- Veredito (estável, atenção ou ruim) e doze achados em português, por regras puras testadas: perda e latência do roteador, perda, latência e jitter até a internet (separando rede local de provedor), bufferbloat, DNS lento, download baixo e servidor de velocidade inacessível.
- Limites: roteador 30 ms, 2 % de perda, 15 ms de jitter; internet 80 ms, 1 % de perda, 20 ms de jitter (ruim acima de 150 ms ou 5 % de perda); bufferbloat quando a latência sobe mais de 100 ms; DNS acima de 150 ms; download abaixo de 10 Mbps.

## Sensores e saúde

Um único `SensorService` lê o LibreHardwareMonitor a cada segundo e publica um instantâneo imutável. Roda enquanto houver quem use (páginas Sensores e Visão geral e o histórico em segundo plano, que fica ligado com o app aberto) e pode ser pausado, por exemplo durante benchmarks de GPU.

A saúde do PC junta sete verificações, cada uma com a sua frase em português e inglês:

| Verificação | Atenção | Crítico |
|---|---|---|
| Sensores da CPU | sem temperatura (falta administrador, PawnIO ou a leitura falhou) | n/a |
| Discos | algum disco com alerta no S.M.A.R.T. | algum disco com risco de falha |
| Memória em uso | 85 % ou mais | 95 % ou mais |
| Espaço livre do disco do sistema | menos de 10 % | menos de 5 % |
| Plano de energia | Equilibrado ou Economia na tomada | n/a |
| Bateria | capacidade perdida | desgastada |
| USB | algum dispositivo com erro | n/a |

Item que não pôde ser lido (por exemplo S.M.A.R.T. sem administrador) fica como "sem dados" e não piora o resultado.

## Avisos de dispositivos

Um serviço em segundo plano assina os eventos de dispositivo e de volume do Windows (WMI, sem administrador), espera 1,5 s para assentar, relê os USB e compara com a lista anterior. Mostra um aviso no canto da janela e registra na atividade recente. Pendrive e disco externo mostram o rótulo e o espaço livre; dispositivos de armazenamento não geram um segundo aviso de USB. A chave "Avisos de dispositivos" (página Dispositivos) liga e desliga os avisos; a atividade continua sendo registrada.

## Projetos e testes

| Projeto de testes | Testes |
|---|---|
| `M1vClock.Core.Tests` | 72 |
| `M1vClock.Hardware.Tests` | 479 |
| `M1vClock.App.Tests` | 467 |
| `M1vClock.Architecture.Tests` | 35 |

Total 1.053 (2 pulados que exigem administrador), 0 avisos de compilação. Há testes contra a máquina real para WMI, adaptadores de rede, USB, assinatura de eventos de dispositivo e abertura do LibreHardwareMonitor. Os que dependem de tempo usam relógio falso.

## Terceiros

| Componente | Licença | Uso |
|---|---|---|
| LibreHardwareMonitorLib 0.9.6 | MPL-2.0 | Leitura de sensores, sem modificações |
| Vortice.DXGI e Direct3D12 | MIT | Memória de vídeo e recursos da GPU |
| PawnIO (driver) | não redistribuído | Necessário para temperatura e consumo da CPU; instalado pelo usuário |

## O que a fase 2 não verifica

- **Administrador.** Contadores e previsão de falha do S.M.A.R.T., clocks e temperatura da CPU, e os 2 testes `HardenedDirectory` não foram exercitados. Falta uma execução elevada (com o seu OK para o UAC) e a instalação do PawnIO.
- **Plugar e desplugar de verdade.** A assinatura dos eventos foi confirmada e a lógica de diferença, espera e avisos tem testes, mas ainda ninguém plugou um dispositivo com o app aberto.
- **Bateria e notebook.** O código de bateria nunca rodou numa bateria real (este PC é de mesa) e não há leitura do `powercfg /batteryreport`.
- **NVIDIA.** NVML, link PCIe, Resizable BAR e VBIOS não foram implementados; não há placa NVIDIA aqui. O banco de especificações de algumas placas AMD veio de especificações públicas lembradas, não de uma fonte lida na hora.
- **Teste de velocidade da rede** só foi rodado à mão neste PC (cabo gigabit); a lógica tem testes, mas a medição não.
- **Comparação lado a lado com o ResetExplode.** Exige abrir o original, que pede administrador.
- Alto contraste, texto acima de 100 % e Windows 10.

## Decisões que precisam de OK

- O histórico de 15 minutos mantém a leitura de sensores ligada o tempo todo (medido: menos de 1 % de um núcleo com uma página parada). Dá para virar uma opção em Configurações.
- A barra lateral tem uma seção chamada "Dispositivos" (drivers, RGB, Maker) e agora também uma página "Dispositivos" em Hardware. Sugestão: renomear a seção para "Periféricos".
- A instalação do PawnIO pelo app (com winget localizado de forma segura) fica para a fase 4; por enquanto a página Sensores mostra o comando.
