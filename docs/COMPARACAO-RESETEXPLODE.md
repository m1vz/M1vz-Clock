# Comparação com o ResetExplode (fase 2)

Comparação lado a lado dos dados de hardware do m1vClock (build Release) com o ResetExplode v0.16.0, no mesmo PC e como administrador, com o PawnIO instalado.

## Como foi feito

- O original foi baixado da página de Releases do repositório dele (`ResetExplode.exe`, 197.011.287 bytes). O tamanho e o SHA-256 (`4e3fc4e4…19f3cf`) batem com o que o GitHub publica. O arquivo **não tem assinatura digital**; foi aberto só para esta comparação.
- Os dois apps foram abertos elevados por um roteiro que lê os textos de cada página pela automação de interface do Windows (UI Automation) e tira capturas de tela. Nada foi alterado no sistema por nenhum dos dois.
- Páginas comparadas: sistema operacional, placa-mãe e BIOS, processador, memória, placa de vídeo, discos, sensores, energia e rede (a de rede só no estado inicial, porque o teste de lag exige um clique e usa a internet).

## Resultado por área

| Área | ResetExplode | m1vClock | Veredito |
|---|---|---|---|
| Sistema operacional | Windows 11 Home, 10.0.26200, build 26200, 64 bits | Igual, e ainda mostra 25H2 e a revisão (26200.9711) | Igual, nós com mais detalhe |
| Processador | Ryzen 7 5700X, AM4, 8 núcleos, 16 threads, 3401 MHz, L2 4096 KB, L3 32768 KB, virtualização ligada | Os mesmos campos e valores | Igual |
| Memória | 16 GB, 2 de 4 slots, máximo 128 GB, DDR4 3200, EXPO/XMP provavelmente ativo, 2 canais, sem ECC, 1,20 V, uso físico e virtual, dois módulos Kingston | Os mesmos campos e valores; números de série ocultos até clicar | Igual |
| Placa-mãe | ASUS TUF GAMING B550M-PLUS, Rev X.0x | Igual | Igual |
| BIOS | AMI 3636, data **03/01/2026** | AMI 3636, data **04/01/2026** | Nós estamos certos: o Windows informa `20260104000000.000000+000` (data do SMBIOS, sem fuso); o original converte com o fuso e erra um dia |
| Placa de vídeo | RX 5500 XT, 8 GB. Chip "—", "GPU fora do banco de especificações" | Navi 14, RDNA 1, TSMC 7 nm, 158 mm², 6,4 bilhões de transistores, lançamento 12/12/2019, 8 GB | Nós temos mais |
| Discos: HDD | 27 °C, falha prevista: não, firmware SC11 | Igual, mais 7.385 h ligado, 3.067 ciclos e 0 erros de leitura | Igual, nós com mais |
| Discos: NVMe | 41 °C, desgaste **50 %**, vida restante 50 %, 81.565 GB gravados, 62.009 GB lidos | Antes: desgaste 0 % e sem totais. Agora: 50 %, 8.530 h, 2.184 ciclos, 81,6 TB gravados, 62 TB lidos | **Falha nossa, corrigida** (abaixo) |
| Sensores | 231 sensores | Antes 326, agora 226 | **Falha nossa, corrigida** (abaixo) |
| Energia | Tomada (PC de mesa), plano Atlas Power Scheme, lista a potência de cada núcleo | Mesma fonte e plano; a potência dos núcleos fica na página Sensores | Equivalente |
| Rede | Teste de lag para o roteador, 1.1.1.1 e 8.8.8.8; avisa "cerca de 100 MB" | Roteador, 1.1.1.1 e 1.0.0.1; agora com limite de 250 MB de download e 100 MB de upload, avisado na tela | Equivalente, nós com limite de dados |

## O que a comparação achou de errado no m1vClock

1. **Desgaste do NVMe.** O Windows devolve 0 % no contador de confiabilidade deste drive, mas o próprio drive informa 50 % usado e 81 TB gravados pelo S.M.A.R.T. A página mostrava "0 % de desgaste, 100 % de vida", o que era enganoso. Agora o serviço completa temperatura, desgaste (vale o maior dos dois), horas ligado, ciclos e os totais gravado e lido com os sensores do LibreHardwareMonitor, sem sobrescrever o que o Windows informou e sem adivinhar quando há dois discos do mesmo modelo.
2. **Ruído na lista de sensores.** O LibreHardwareMonitor expõe como "adaptadores de rede" os filtros do Windows e do antivírus e os adaptadores virtuais (eram 100 sensores a mais). Agora só entram os adaptadores ativos e com endereço IPv4 de uso real.
3. **Dados do teste de rede.** Numa conexão de 750 Mbps o download baixava algumas centenas de MB sem avisar. Agora há um teto (250 MB e 100 MB) e o texto da página diz o limite.

## Onde o original está à frente

- A página de energia dele lista a potência por núcleo; a nossa remete à página Sensores.
- A Visão geral dele já tem painéis de benchmark, eventos e jogos, que no m1vClock chegam nas próximas fases.

## Limites desta comparação

- Foi um único PC (Ryzen 7 5700X, RX 5500 XT, NVMe e HDD, sem bateria, sem NVIDIA).
- Só os dados de hardware foram comparados; as outras páginas do original (jogos, desempenho, ferramentas, maker) não fazem parte da fase 2.
- O teste de lag do original não foi executado.
