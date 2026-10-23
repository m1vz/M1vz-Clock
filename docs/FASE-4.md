# Fase 4: diagnóstico, otimização e reparo

Entrega da fase 4 do [plano](PLANO.md): doze páginas novas nos grupos **Diagnóstico** (Eventos, Telas azuis, Confiabilidade, Logs do app, Relatório), **Otimização** (Inicialização, Serviços, Limpeza, Memória em espera, Aplicativos, Ajustes) e **Reparo**. Tudo que muda o sistema passa pelo diário de alterações (`ChangeJournal`) e pode ser desfeito; as exceções estão marcadas abaixo.

## Como compilar, rodar e testar

```
dotnet build m1vClock.slnx
dotnet test --solution m1vClock.slnx
```

Para ver uma página direto: `m1vClock.exe --page events|crashes|reliability|logs|report|startup|services|cleanup|standby|apps|tweaks|repair --theme light|dark --screenshot arquivo.png --screenshot-render --screenshot-delay <segundos>`.

Use sempre `--screenshot-render`: a captura normal copia a região da tela e mostra qualquer janela que esteja na frente (isso já aconteceu e a imagem foi apagada). Com `--scroll` no modo render use `--screenshot-delay 9`.

## Regras que valem para toda a fase

- **Ler não muda nada.** Eventos, telas azuis, confiabilidade, logs, relatório, inventário de programas e as verificações de reparo só leem.
- **Mudar passa pelo diário.** Cada valor gravado guarda o estado de antes e o de depois, é conferido por leitura e pode ser desfeito. Mudanças em `HKLM` pedem ponto de restauração do dia (uma vez) e administrador.
- **Antes de gravar, a tela mostra o que vai mudar** e como desfazer. Recusar não muda nada.
- **Processos externos só pelo `ProcessRunner`** (caminho completo em `System32`, sem shell). Um teste de arquitetura barra qualquer outro `Process.Start`.
- **Nada de destrutivo sem volta aqui:** apagar arquivos (Limpeza) e desinstalar programas (Aplicativos) **não** são desfazíveis e dizem isso na confirmação.

## Diagnóstico

| Página | O que mostra | Detalhes que importam |
|---|---|---|
| Eventos | Eventos do Windows dos últimos 14 dias, agrupados por origem e ID | Ruído (como o ESENT, 30 mil de 31 mil eventos neste PC) fica escondido por padrão, com interruptor |
| Telas azuis | Reinícios sem desligar (Kernel-Power 41 com código de verificação), falhas de programas agrupadas e o driver apontado (AMD, NVIDIA, Intel), cabeçalho dos dumps e a configuração de dump | Lê o cabeçalho `DUMP_HEADER64` sem abrir o dump inteiro |
| Confiabilidade | Índice de 1 a 10 do Windows com gráfico de histórico | WMI, só leitura |
| Logs do app | Final do log (4 MB), filtro por nível e busca, copiar e abrir a pasta | |
| Relatório | HTML ou texto com prévia, para salvar ou copiar | Sem número de série e **sem o nome do computador** por padrão (um teste pegou o nome vazando na seção do sistema) |

## Otimização

| Página | O que faz | Segurança |
|---|---|---|
| Inicialização | Lista Run, Run32, pastas e o estado em `StartupApproved`; liga e desliga pelo diário | `HKLM` só com administrador e ponto de restauração; entrada sem arquivo é sinalizada |
| Serviços | Lista pelo WMI; muda o início (automático, atrasado, manual, desativado) de **terceiros** | Antivírus, anti-cheat e serviços do Windows (ou sem arquivo) não mudam; a mudança vale no próximo boot |
| Limpeza | Varre seis pastas fixas e apaga o que tem mais de um dia | Nunca segue link ou junção; recusa pasta pessoal, `Users` e `Program Files`; **não desfaz** |
| Memória em espera | Mostra as listas de memória e esvazia só o cache em espera | Exige administrador; não usa `EmptyWorkingSet` (um teste proíbe) |
| Aplicativos | Inventário de programas (chaves Uninstall do usuário e da máquina, 64 e 32 bits) com tamanho, data e empresa | Sem componentes do sistema nem atualizações; o botão só abre o **desinstalador do próprio programa**, se o executável ainda existir, depois de confirmar; o m1vClock não apaga nada |
| Ajustes | Seis ajustes de jogos, todos pelo diário | Ver abaixo |

### Ajustes

Plano de energia Alto desempenho, Modo de Jogo, gravação em segundo plano (Game DVR, dois valores), agendamento de GPU por hardware (`HKLM`, pede reiniciar), otimizações de jogos em janela (uma opção dentro de um texto que guarda outras) e aceleração do mouse (aplicada na hora pela API do Windows).

- **Ligar** grava o valor do ajuste; **desligar** grava o padrão do Windows. Cada gravação é uma entrada do diário.
- **Restaurar originais** desfaz as entradas do módulo Ajustes, da mais nova para a mais antiga, e devolve o estado de antes de o m1vClock mexer (inclusive a ausência do valor). Só fica ativo depois de o m1vClock mudar algo.
- Se o segundo valor de um ajuste falhar, o primeiro é desfeito: nada fica pela metade.
- Nada reduz a segurança do Windows: não há ajuste de Defender, UAC, Windows Update nem isolamento de núcleo.
- O plano de energia nunca é criado: se o Windows não tem Alto desempenho, o ajuste aparece como indisponível. Ao desligar volta o plano que estava ativo antes (e, sem histórico, o Equilibrado).
- Os ajustes de usuário valem para a conta que abriu o m1vClock; a página mostra o nome da conta (sem o nome do computador). Se alguém eleva com a senha de outra conta, o `HKCU` é o da conta elevada (problema S10 do plano, só sinalizado).

## Reparo

Três verificações, **nenhuma repara**: DISM rápida, DISM completa e SFC (`/verifyonly`). Todas exigem administrador, mostram progresso quando existe, podem ser canceladas, e sair da página cancela a que estiver rodando. Quando acham problema, a página mostra o que achou e o comando para o usuário rodar por conta própria (`DISM /Online /Cleanup-Image /RestoreHealth` ou `sfc /scannow`), com botão de copiar, e avisa que num Windows enxugado de propósito (como o Atlas) o reparo pode trazer de volta o que foi removido.

- **DISM** é chamado pela API (`DismCheckImageHealth`), que devolve o estado como número: saudável, reparável ou não reparável. A saída de texto não serve: neste PC o `DISM /CheckHealth` escreve "O repositório de componentes é reparável" e sai com **código 0**.
- **SFC**: neste PC o `sfc /verifyonly` escreveu "encontrou violações de integridade" e também saiu com **código 0**; as marcas `[SR]` do `CBS.log` só mostram o trabalho normal. O arquivo aparece como `[Pnp] Corrupt file:` no fim do log. O veredito vem do `CBS.log` (em inglês, sem depender do idioma): `Corrupt file:` ou qualquer linha `[SR]` fora das três de rotina conta como problema, e uma execução sem nenhuma linha "Verify complete" ou com código diferente de zero conta como **falha**, nunca como saudável.
- O SFC só despeja o progresso em blocos grandes quando não escreve num console (de 35% a 73% numa escrita só), então a página mostra um aviso em vez de uma barra que mentiria.

## Achados reais neste PC

- **Windows enxugado (Atlas):** plano de energia "Atlas Power Scheme", Modo de Jogo desligado, aceleração do mouse já desligada.
- **Dumps desligados** (`CrashDumpEnabled=0`, `LogEvent=0`) e **dwm.exe caindo em `amdxx64.dll`** (driver AMD) em 21/10; mais de seis reinícios sem causa registrada; índice de confiabilidade 7,05.
- **DISM:** repositório de componentes **reparável**. **SFC:** um arquivo diferente, `bthmodem.sys` (driver de Bluetooth, provavelmente removido de propósito).
- Entrada de inicialização do `AMDNoiseSuppression` aponta para um arquivo que não existe.
- 84 programas instalados, 46,1 GB declarados; o maior é o VALORANT (34,54 GB).

Correções que a fase trouxe para o resto do app: ids de log do Diagnóstico começam em 5001 (os de Benchmarks usam 4xxx); `BenchTempArea.Remove` tenta cinco vezes (causa do teste que às vezes falhava); a caixa de busca de Serviços e de Logs agora mostra o texto de ajuda; a opção `--screenshot-render` existe.

## O que foi testado de verdade e o que não foi

**Testado com dados reais deste PC:** leitura de eventos, de telas azuis, de confiabilidade e de logs; relatório; inventário de programas pelo registro; leitura de serviços pelo WMI; leitura do plano de energia e do mouse (inclusive reaplicar o mesmo valor do mouse); `DISM.exe /CheckHealth` e `sfc /verifyonly` rodados elevados, com o texto e o `CBS.log` capturados e usados nos testes; a chamada do DISM **pela API** rodada elevada (verificação rápida devolvendo um veredito, e a completa com progresso entre 0 e 100 sempre crescente, interrompida ao passar de 15%; 3 testes em 52,7 s, 0 falhas, 0 pulados); ida e volta no registro real (aplicar, conferir, desfazer, conferir) com chaves de teste `m1vClock-Teste-*` no `HKCU`, apagadas no fim; telas conferidas por captura nos dois temas.

**Testado só com registro em memória e dados falsos:** mudança de serviços (`HKLM`), gravação do HAGS, troca de plano de energia, esvaziar a memória em espera, o ramo "problemas encontrados" do SFC (as linhas são sintéticas; a de `Corrupt file:` é real).

**Nunca rodado de verdade, de propósito:** a Limpeza real (a pasta temporária do usuário contém os arquivos da própria sessão de trabalho), qualquer desinstalação, qualquer reparo.

**Ainda sem validação:** Release elevado das páginas novas (a validação elevada foi feita nos testes, não clicando na interface); Windows 10; um Windows sem enxugar; notebook. Não há VM neste PC e instalar uma não foi autorizado.

## Testes

2.114 testes, 0 falhas; os 6 pulados são os que pedem administrador (rodar elevado) e o teste lento do DISM (defina `M1VCLOCK_SLOW_TESTS=1` e rode elevado). Uma rodada da suíte teve uma falha intermitente que não consegui identificar (aconteceu durante a varredura de disco do DISM elevado); depois disso houve quatro rodadas limpas, uma delas com todos os núcleos ocupados. Regras de arquitetura novas que protegem a fase: sem `Process.Start` fora do `ProcessRunner` e do `ShellLauncher`, sem `EmptyWorkingSet`, projetos com `LibraryImport` precisam fixar a busca de DLL em `System32`.
