# Avisos de terceiros

O m1vClock reaproveita código e usa componentes de terceiros. Este arquivo reúne os avisos exigidos pelas licenças.

## ResetExplode (MIT)

Projeto: https://github.com/GustavoReset/ResetExplode

Os algoritmos dos benchmarks foram mantidos iguais aos do ResetExplode para que as notas sejam comparáveis, e partes do código e dos shaders foram aproveitadas:

- Render de CPU por path tracing (cena, semente por pixel e fórmula da nota) e simulação de jogo na CPU, conferidos contra o original.
- Latência e banda de memória e o teste de carregamento de mapa.
- A cena de jogo em Direct3D 11 (geração do terreno, árvores, câmera e shaders) e o estresse de GPU com pelos em camadas (shaders).
- O banco de especificações de GPU e o mapa de fabricantes, na fase 2.

Todo o resto (organização em projetos, camada de hardware, monitor de segurança, histórico, pasta temporária, interface) é do m1vClock. O código foi reescrito, não copiado por inteiro.

```
MIT License

Copyright (c) 2026 Gustavo (GustavoReset)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Os drivers seriais que o ResetExplode embute na pasta `Drivers/` **não** são MIT e não fazem parte deste projeto. Eles serão baixados na hora, com verificação de assinatura.

## Componentes usados

| Componente | Licença | Uso | Onde ler a licença |
|---|---|---|---|
| LibreHardwareMonitorLib 0.9.6 | MPL-2.0 | Leitura de sensores, sem modificações | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| Vortice.Direct3D11, Vortice.Direct3D12, Vortice.DXGI, Vortice.D3DCompiler | MIT | Cena e estresse de GPU, memória de vídeo e verificação de ray tracing | https://github.com/amerkoleci/Vortice.Windows |
| CommunityToolkit.Mvvm | MIT | Camada de interface | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.*, System.Diagnostics.EventLog e System.Management | MIT | Hospedagem, injeção de dependência, logs, log de eventos do Windows e WMI | https://github.com/dotnet/runtime |
| Serilog | Apache-2.0 | Registro em arquivo | https://github.com/serilog/serilog |
| Ícones Lucide | ISC | Ícones da interface | `src/M1vClock.App/DesignSystem/Styles/Lucide-LICENSE.txt` |
| Fonte Inter | SIL OFL 1.1 | Tipografia | `src/M1vClock.App/Assets/Fonts/Inter-OFL.txt` |

## Não redistribuído

- **PawnIO** (driver): necessário para a temperatura e o consumo da CPU. É instalado pelo usuário (`winget install namazso.PawnIO`); o m1vClock não o distribui.
