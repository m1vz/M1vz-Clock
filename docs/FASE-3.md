# Fase 3: benchmarks e estresse

Entrega da fase 3 do [plano](PLANO.md): sete benchmarks, histórico com comparação e importação, e o teste de estresse de CPU, RAM e GPU com um monitor de segurança que roda fora da interface. Duas páginas novas, **Benchmarks** e **Teste de estresse**, na seção Desempenho.

## Como compilar, rodar e testar

```
dotnet build m1vClock.slnx
dotnet test --solution m1vClock.slnx
```

Para ver uma página direto: `m1vClock.exe --page benchmarks|stress --theme light|dark --screenshot arquivo.png --screenshot-delay <segundos>`.

## O que cada teste mede

| Teste | Mede | Nota | Observações |
|---|---|---|---|
| Render de CPU | Path tracing de uma cena 3D, multi-core (todos os threads) e single-core, com opção de 10 min sustentado | amostras por segundo ÷ 10.000 (mesma fórmula do original) | Imagem aparecendo ao vivo, com os blocos de cada thread em volta. Pixel a pixel idêntica ao original em 5 blocos conferidos |
| Simulação de jogo | FPS que a CPU aguenta com 30 mil unidades (grade espacial, física, IA e preparo do desenho) | FPS médio, mediana de 3 passadas | Estado idêntico ao original após 120 quadros. Os trabalhadores ficam nos núcleos físicos (ver "Achados") |
| Memória | Latência da RAM e banda de leitura e de cópia, fora do cache | banda de leitura em GB/s | Usa memória nativa e devolve tudo ao terminar; precisa de cerca de 2,5 GB livres e recusa antes de começar se não houver |
| Carregamento | Um pacote de 512 MB e mil arquivos de 64 KB lidos sem o cache do Windows | tempo para carregar o mapa (menor é melhor) | Passada de aquecimento para o antivírus não distorcer a medida |
| Disco | Sequencial de 1 MB e aleatório de 4 KB com fila de verdade (1, 8 e 32 requisições ao mesmo tempo) | nota versão 2 | Entrada e saída assíncronas de verdade; o original usava 8 threads síncronas e o README dizia QD32. A nota v2 **não** se compara com a do app original |
| Cena de jogo (GPU) | Cena Direct3D 11 com terreno, 40 mil árvores, sombras 4096², água e pós-processamento; câmera fixa por tempo | FPS médio × 10 | Mesma cena e mesma câmera do original. 1% low pelo mesmo cálculo |
| Ray tracing (DXR 1.1) | Verifica se a placa tem DXR 1.1 por hardware | n/a | Ver "O que a fase 3 não verifica" |

Cada resultado vai para o histórico com a máquina (CPU, GPU, RAM, Windows e versão do app) e **sem** o nome do computador. O histórico não tem o limite de 10 do original. Depois de cada teste a página compara com a execução anterior neste PC (margem de ruído de 3 %), e clicar numa linha do histórico a usa como base de comparação com o resultado mais recente do mesmo teste, métrica por métrica. Exportar e importar usam um arquivo próprio; a importação trata o arquivo como não confiável (ver "Segurança").

## Teste de estresse

- **CPU**: carga de ponto flutuante e inteiros em todos os núcleos, prioridade abaixo do normal para a interface continuar respondendo.
- **RAM**: grava e confere padrões na memória livre, em blocos nativos de 128 MB. Usa 40, 60 ou 80 % da livre e sempre deixa 1 GB para o Windows. Qualquer erro encerra o teste.
- **GPU**: torus com pelos em camadas (estilo FurMark), com resolução (720p, 1080p, 1440p ou tela cheia), MSAA (off, 4x, 8x) e intensidade (normal, alta, extrema). Janela própria; Esc ou fechar a janela para o teste.
- Duração: sem limite, 5, 15, 30 ou 60 minutos.
- Antes de começar, uma confirmação diz o que vai acontecer, o corte por temperatura e como parar. Nada no Windows é alterado.

### Monitor de segurança

Roda numa tarefa própria, **fora da interface**, e para o teste por: temperatura acima do limite, sensor de temperatura que some (3 leituras seguidas sem valor), erro de memória, janela da GPU que para de responder (3 verificações) ou pedido do usuário (Parar, Esc ou sair da página).

| | CPU | GPU |
|---|---|---|
| Padrão | 85 °C | 83 °C |
| Livre | 60 a 90 °C | 60 a 90 °C |
| Com confirmação dupla | até 95 °C | até 95 °C |
| Máximo travado | 95 °C | 95 °C |
| Leitura | a cada 1 s | a cada 2 s |

- **Sem sensor de temperatura o teste não começa.** O original deixava iniciar sem sensor; aqui a tela explica e mostra o comando do PawnIO. Só o teste de RAM roda sem sensor, porque não esquenta nada.
- Os limites escolhidos até 90 °C são lembrados; valores de 91 a 95 °C valem só para a sessão e pedem a confirmação dupla de novo. Um arquivo de configurações adulterado não consegue subir o corte.
- Durante qualquer teste pesado a leitura geral dos sensores fica **pausada**, e só a CPU ou a GPU em teste é lida (`ReadComponent`, que funciona com a leitura geral pausada e nunca se sobrepõe a ela). Um teste pesado por vez.
- Esc para o teste em qualquer página.

## Pastas temporárias

Os testes de carregamento e disco gravam numa pasta de **nome aleatório** (`m1vClock-bench-<16 hex>`) na unidade testada. Como administrador, a pasta é criada com permissões restritas (só Administradores e SYSTEM escrevem) e nunca dentro de um link ou junção; sem administrador, a unidade do sistema usa a pasta temporária do próprio app. Os arquivos são abertos com `FILE_FLAG_DELETE_ON_CLOSE`.

Validado **matando o app à força no meio do teste de disco**: o Windows apagou o arquivo de teste na hora (a pasta ficou vazia) e, na abertura seguinte, o app removeu a pasta e registrou "Sobras de teste anteriores removidas: 1 pasta(s)". A varredura só toca em pastas com esse prefixo, mais velhas que 1 minuto, que não sejam links e que só contenham arquivos.

## Segurança da importação

O arquivo de outro PC é tratado como não confiável: no máximo 5 MB e 5.000 registros; só testes conhecidos, com a mesma versão da nota; só métricas conhecidas; números finitos e dentro de uma faixa; datas entre 2020 e amanhã; textos cortados em 120 caracteres e sem caracteres de controle; ids repetidos descartados. Registros recusados são contados e mostrados. O arquivo exportado não leva nome do computador nem do usuário.

## Medições nesta máquina

| Teste | Execuções | Variação |
|---|---|---|
| Render de CPU, multi-core | 3.630 / 3.701 / 3.678 pts (e 3.549 / 3.589 / 3.573 horas depois) | 1,9 % (1,1 %) |
| Render de CPU, single-core | 369,7 / 369,9 pts | 0,07 % |
| Simulação de jogo | 144,3 / 144,1 / 144,7 / 143,9 FPS | 0,53 % |
| Disco, nota v2 | 2.709 / 2.723 | 0,52 % |
| Cena de jogo (GPU) | 172,4 / 172,3 FPS (1% low 142) | 0,08 % |
| Memória, banda de leitura | 34,8 / 33,3 / 35,5 GB/s | **6,4 %** |
| Carregamento, tempo do mapa | 0,21 / 0,22 / 0,22 s | **5,2 %** (a medida é de 0,2 s) |

Os testes determinísticos de CPU, disco (nota) e GPU ficam abaixo dos 3 % pedidos. **Memória e carregamento não**: dependem do estado da RAM e do disco no momento e variaram 6,4 % e 5,2 % entre execuções. As métricas individuais do disco variam mais que a nota (leitura sequencial fila 1 foi de 1.992 para 2.205 MB/s).

Dentro do app, com a interface aberta, o render de CPU dá cerca de 10 % menos que o mesmo código sem interface (3.0xx a 3.3xx contra 3.5xx a 3.7xx) e varia 1,4 % entre execuções. A prévia ao vivo custa uns 4 % (3.363 sem prévia, 3.213 com); ela foi reduzida a meia resolução e 4 atualizações por segundo. O restante é a janela do app e a atividade do Windows. A nota dentro do app é consistente consigo mesma, mas **não é igual** à medida sem interface.

### Ler a temperatura da GPU atrapalha o 1% low?

Medido na cena de jogo, 3 rodadas de 20 s por variante:

| Variante | FPS médio | 1% low |
|---|---|---|
| Sem leitura nenhuma | 171,7 | 141,6 |
| Leitura geral pausada + só a GPU a cada 2 s (como o app faz) | 171,7 | 141,9 |
| Leitura geral a cada 1 s, sem pausar (como o original) | 171,5 | 140,8 |

A leitura de 2 s não tem efeito mensurável, então ficou como o plano previa e não precisou cair para 5 s. Ressalva: medido sem administrador e sem PawnIO, que lê menos sensores.

## Achados dos testes reais (e o que mudou)

Vários bugs só apareceram rodando de verdade, não nos testes unitários:

1. **Memória: a 2ª rodada era recusada.** Os 2 GB da primeira esperavam o coletor de lixo, e o app via só 1,2 GB livres. Passou a usar memória nativa devolvida na hora; teste novo confere a memória livre antes e depois.
2. **Simulação de jogo variava de 20 % a 45 %** entre execuções (129 a 194 FPS) com 16 threads lógicas: o SMT atrapalha um teste que sincroniza a cada quadro. Com os trabalhadores nos núcleos físicos e nota pela mediana de 3 passadas, caiu para 0,53 %. O estado da simulação continua idêntico ao original.
3. **`InsufficientMemoryException` escapava do tratamento de erros** (ela herda de `OutOfMemoryException`, que eu excluía): a falta de memória do próprio teste derrubaria o app. Achado pelos testes da página.
4. **O histórico nunca era lido ao abrir o app**, e o primeiro resultado novo sobrescrevia todos. Achado repetindo testes pela interface; os testes anteriores chamavam `Load()` à mão. Teste novo reabre o app três vezes pelo registro de serviços e falha sem a correção.
5. **Parar o estresse esperava o próximo tick do relógio**, e uma corrida entre a parada e o fim da sessão podia lançar `ObjectDisposedException`.
6. **O serviço de diálogos falhava** (`NullReferenceException`) se a resposta chegasse dentro do evento de abertura.
7. O botão Iniciar continuava habilitado ao trocar para um teste indisponível.

## Terceiros

| Componente | Licença | Uso |
|---|---|---|
| Vortice.Direct3D11, Vortice.D3DCompiler | MIT | Cena de jogo e estresse de GPU (shaders compilados em tempo de execução com o `d3dcompiler_47.dll` do Windows) |
| Vortice.Direct3D12, Vortice.DXGI | MIT | Verificação de ray tracing e escolha da placa |
| Algoritmos dos benchmarks do app original | MIT | Para manter as notas comparáveis; aviso em `THIRD-PARTY-NOTICES.md` |

## Testes

| Projeto de testes | Testes |
|---|---|
| `M1vClock.Core.Tests` | 72 |
| `M1vClock.Hardware.Tests` | 544 |
| `M1vClock.Benchmarks.Tests` (novo) | 229 |
| `M1vClock.App.Tests` | 611 |
| `M1vClock.Architecture.Tests` | 35 |

Total 1.491, com 4 pulados que exigem administrador, 0 avisos de compilação. Há testes contra a máquina real para a pasta temporária, a E/S sem cache, a cena e o estresse de GPU (abrem uma janela por alguns segundos), a verificação de ray tracing e o conjunto de CPU. Os que dependem de tempo usam relógio falso.

## Validação elevada do estresse (22/10/2026)

Release sob administrador com o PawnIO, roteiro de interface com corte no mínimo:

- **GPU**: corte em 63 °C, disparou duas vezes aos 32 s ("Temperature (Gpu) valor 63, limite 63") e o teste encerrou na hora.
- **CPU**: corte em 60 °C, a CPU ficou em 58 °C por 15 min a 100 % e cerca de 78 W; terminou como "Concluído (tempo esgotado)".
- Achados corrigidos: o selo de status não aparecia para leitores de tela; o limite da GPU parava em 63 °C sem chegar a 60 °C; fechar a página depois de um teste encerrado gravava uma parada falsa no log.

## Comparação lado a lado com o app original (v0.16.0) (22/10/2026)

Mesmo PC (Ryzen 7 5700X, RX 5500 XT, NVMe), os dois programas elevados, um de cada vez:

| Teste | m1vClock | Original | Situação |
|---|---|---|---|
| Render single-core | 370 pts | 360 pts | mesma faixa |
| Memória (leitura / cópia / latência) | 35,6 GB/s / 35,1 GB/s / 87,6 ns | 36,3 GB/s / 35,0 GB/s / 89,0 ns | mesma faixa |
| Carregamento do mapa | 0,283 s | 0,28 s | mesma faixa |
| Disco, fila 1 (leitura / escrita seq.) | 1.813 / 1.578 MB/s | 1.857 / 1.722 MB/s | mesma faixa (a nota v2 é diferente de propósito) |
| Cena de GPU 1080p | 166,8 FPS, 1% low 129,4 | 166,9 FPS, 1% low 129,4 | idêntico |
| Simulação de jogo | 152 FPS, 1% low 109 | 188 FPS, 1% low 111 | fora da faixa de propósito: só núcleos físicos |
| Render multi-core | 2.905 a 3.125 pts no app elevado | 3.393 pts | **abaixo, causa não isolada** |

**Simulação.** O m1vClock usa os 8 núcleos físicos e a mediana de 3 passadas porque, com as 16 threads, a nota variava até 45 % entre execuções; assim varia 0,5 %. A página avisa que por isso a nota não bate com a do original.

**Render multi-core.** O motor sozinho, sem interface, dá 3.300 a 3.800 pts, a mesma faixa do original. Dentro do app a nota cai: 3.170 a 3.270 sem administrador e 2.905 a 3.125 com administrador (de 2.750 a 2.785 na primeira medição, em que o roteiro de automação também competia pela CPU). O que foi medido:

- o monitor de segurança térmica custa cerca de 3 % (média de 2.978 com ele, 3.078 sem);
- a prévia do render custa 3 a 5 % e a atualização de 250 ms da página, 5 a 8 %;
- a leitura de sensores a cada segundo custa 1 % sem administrador e cerca de 6 % com o PawnIO;
- tirar o processo da economia de energia do Windows, animar ou não a barra de progresso, a resolução do temporizador e o uso de um token de cancelamento não mudaram nada;
- subir a prioridade das threads de render piorou muito a nota.

O que sobra, em torno de 10 %, não foi explicado. O render single-core não sofre disso (370 contra 360).

## O que a fase 3 não verifica

- **O renderizador de ray tracing (DXR 1.1) não foi portado.** A verificação de suporte está pronta e foi testada de verdade: esta RX 5500 XT não tem ray tracing por hardware, e a página diz isso com o nome da placa. Para uma placa que tem DXR 1.1 a entrada avisa que o teste não está disponível, porque o código de renderização e a nota não podem ser validados sem uma placa assim. O original tem 776 linhas desse teste; portar às cegas arriscaria dar uma nota errada.
- **Corte por temperatura da CPU** não disparou de verdade: com o Release elevado e o PawnIO, 15 min de CPU a 100 % (cerca de 78 W) estabilizaram em 58 °C, abaixo do menor corte (60 °C), e o teste terminou pelo tempo. O corte é o mesmo código do da GPU.
- **Nota do render multi-core dentro do app** fica 10 a 15 % abaixo da do app original e da medida sem interface; a causa restante não foi isolada (detalhes na comparação acima).
- Estresse de GPU em tela cheia, MSAA 8x e durações longas (acima de 1 minuto) não foram rodados.
- Notas em outro PC, Windows 10, alto contraste e texto acima de 100 %.
