# Instalador

Um único `m1vClock-Setup-<versão>.exe` para baixar e instalar, feito com o Inno Setup.

## Como gerar

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

O script publica o app em Release, self-contained para Windows x64 (quem instala não precisa ter o .NET), baixa o Inno Setup como pacote NuGet só para montar o instalador (nada é instalado no PC) e grava em `artifacts\installer\`:

- `m1vClock-Setup-<versão>.exe` (cerca de 50 MB);
- `m1vClock-Setup-<versão>.exe.sha256`, o hash para publicar junto no site.

A versão vem do `Directory.Build.props`. Para lançar uma versão nova, suba o número lá e gere de novo: o instalador novo atualiza por cima do anterior (mesmo `AppId`).

## O que o instalador faz

- Pede administrador e instala em `C:\Program Files\m1vClock` (pasta que só administrador altera; o app roda como administrador, então ele não pode ficar numa pasta que o usuário comum consegue trocar).
- Cria o atalho no menu Iniciar e, se a pessoa marcar, na área de trabalho. Oferece abrir o app no fim.
- Em português ou inglês, conforme o idioma do Windows.
- Exige Windows 10 1809 ou mais novo, 64 bits.
- Leva junto o `THIRD-PARTY-NOTICES.md` (exigido pelas licenças).
- Desinstalar remove a pasta do programa, os atalhos e a entrada em "Aplicativos instalados". Depois pergunta se apaga também as configurações, os logs e o histórico de alterações (`C:\ProgramData\m1vClock`); o padrão é **não** apagar, porque sem o histórico não dá mais para desfazer pelo app as mudanças feitas no Windows. Na desinstalação silenciosa os dados ficam.

## Validado neste PC (26/10/2026)

Instalação silenciosa (`/VERYSILENT`) elevada: 313 arquivos, 154 MB em `Program Files`, atalho no menu Iniciar, entrada no registro com nome, versão 0.1.0, editor e ícone. Desinstalação silenciosa: pasta, atalho e entrada removidos; dados em `ProgramData` mantidos. O app instalado não foi aberto nesse teste porque já havia um m1vClock aberto (ele é de instância única e só trouxe a janela aberta para a frente).

## Antes de publicar no site

- **Assinatura de código.** O instalador e o app não são assinados. Quem baixar vai ver o aviso "O Windows protegeu o computador" (SmartScreen) e precisará clicar em "Mais informações" e "Executar assim mesmo"; o UAC vai mostrar "Editor desconhecido". Para tirar isso é preciso um certificado de assinatura de código (pago, ou o programa gratuito da SignPath Foundation para projetos de código aberto públicos). A reputação no SmartScreen também cresce com o número de downloads.
- **Temperatura da CPU** depende do driver PawnIO, que o instalador não inclui; o app avisa quando ele falta.
- Publique o `.sha256` junto, para quem quiser conferir o arquivo baixado.
