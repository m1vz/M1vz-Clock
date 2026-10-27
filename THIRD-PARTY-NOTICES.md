# Avisos de terceiros

O m1vClock reaproveita código e usa componentes de terceiros. Este arquivo reúne os avisos exigidos pelas licenças.

Drivers seriais de terceiros não fazem parte deste projeto. Eles serão baixados na hora, com verificação de assinatura.

## Componentes usados

| Componente | Licença | Uso | Onde ler a licença |
|---|---|---|---|
| LibreHardwareMonitorLib 0.9.6 | MPL-2.0 | Leitura de sensores, sem modificações | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| Vortice.Direct3D11, Vortice.Direct3D12, Vortice.DXGI, Vortice.D3DCompiler | MIT | Cena e estresse de GPU, memória de vídeo e verificação de ray tracing | https://github.com/amerkoleci/Vortice.Windows |
| Microsoft.Diagnostics.Tracing.TraceEvent 3.2.8 | MIT | Medição de quadros por segundo pelos eventos de apresentação do Windows (ETW), sem injetar nada nos jogos | https://github.com/microsoft/perfview |
| CommunityToolkit.Mvvm | MIT | Camada de interface | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.*, System.Diagnostics.EventLog e System.Management | MIT | Hospedagem, injeção de dependência, logs, log de eventos do Windows e WMI | https://github.com/dotnet/runtime |
| Serilog | Apache-2.0 | Registro em arquivo | https://github.com/serilog/serilog |
| Ícones Lucide | ISC | Ícones da interface | `src/M1vClock.App/DesignSystem/Styles/Lucide-LICENSE.txt` |
| Fonte Inter | SIL OFL 1.1 | Tipografia | `src/M1vClock.App/Assets/Fonts/Inter-OFL.txt` |

## Não redistribuído

- **PawnIO** (driver): necessário para a temperatura e o consumo da CPU. É instalado pelo usuário (`winget install namazso.PawnIO`); o m1vClock não o distribui.
