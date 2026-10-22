using Microsoft.Extensions.Logging;

namespace M1vClock.App;

internal static partial class AppLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "m1vClock {Version} iniciado (administrador: {Elevated}, dados em {Root}).")]
    public static partial void Started(ILogger logger, string version, bool elevated, string root);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "m1vClock encerrado normalmente.")]
    public static partial void Stopped(ILogger logger);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Critical, Message = "Erro não tratado na interface.")]
    public static partial void UnhandledUiException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Critical, Message = "Erro não tratado fora da interface.")]
    public static partial void UnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Tarefa com erro não observado.")]
    public static partial void UnobservedTaskException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning, Message = "A sessão anterior ({Version}, iniciada em {StartedAt}) terminou de forma inesperada.")]
    public static partial void PreviousSessionCrashed(ILogger logger, string version, DateTimeOffset startedAt);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning, Message = "Pasta de dados com permissões erradas foi isolada: {Path}.")]
    public static partial void DirectoryQuarantined(ILogger logger, string path);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Error, Message = "Não foi possível gravar as configurações.")]
    public static partial void SettingsSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Information, Message = "Configurações carregadas: {Status}.")]
    public static partial void SettingsLoaded(ILogger logger, Core.Settings.SettingsLoadStatus status);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Página aberta: {Page}.")]
    public static partial void Navigated(ILogger logger, string page);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Error, Message = "Falha ao executar a ação {Action}.")]
    public static partial void ActionFailed(ILogger logger, Exception exception, string action);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Warning, Message = "Material Mica indisponível: {Reason}.")]
    public static partial void MaterialUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Information, Message = "Outra instância pediu para trazer a janela à frente.")]
    public static partial void ActivationRequested(ILogger logger);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Warning, Message = "Falha no canal de ativação da instância única.")]
    public static partial void ActivationChannelFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Information, Message = "Captura de tela gravada em {Path}.")]
    public static partial void ScreenshotSaved(ILogger logger, string path);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Error, Message = "Falha ao carregar a página {Page}.")]
    public static partial void PageLoadFailed(ILogger logger, Exception exception, string page);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Error, Message = "O diagnóstico de rede não terminou.")]
    public static partial void NetworkDiagnosticFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2018, Level = LogLevel.Warning, Message = "O Windows não deixou vigiar a conexão de dispositivos; os avisos ficam indisponíveis.")]
    public static partial void DeviceWatcherUnavailable(ILogger logger);

    [LoggerMessage(EventId = 2019, Level = LogLevel.Error, Message = "Falha ao tratar uma mudança de dispositivos.")]
    public static partial void DeviceNotifierFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2020, Level = LogLevel.Information, Message = "Teste pesado iniciado: {Name}. A leitura geral dos sensores ficou em pausa.")]
    public static partial void HeavyTestEntered(ILogger logger, string name);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Information, Message = "Teste pesado terminado: {Name}. A leitura dos sensores voltou.")]
    public static partial void HeavyTestLeft(ILogger logger, string name);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Warning, Message = "Os sensores demoraram demais para abrir; o teste segue sem esperar.")]
    public static partial void SensorsSlowToOpen(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2023, Level = LogLevel.Warning, Message = "Não foi possível montar o perfil da máquina para o histórico de benchmarks.")]
    public static partial void MachineProfileFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2024, Level = LogLevel.Error, Message = "A limpeza de sobras dos testes falhou.")]
    public static partial void TempSweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2025, Level = LogLevel.Error, Message = "O benchmark {Test} terminou com erro.")]
    public static partial void BenchmarkFailed(ILogger logger, Exception exception, string test);

    [LoggerMessage(EventId = 2026, Level = LogLevel.Information, Message = "Benchmark {Test} concluído: nota {Score}.")]
    public static partial void BenchmarkCompleted(ILogger logger, string test, double score);

    [LoggerMessage(EventId = 2027, Level = LogLevel.Warning, Message = "Benchmark {Test} interrompido: {Reason}.")]
    public static partial void BenchmarkStopped(ILogger logger, string test, string reason);

    [LoggerMessage(EventId = 2028, Level = LogLevel.Error, Message = "O teste de estresse terminou com erro.")]
    public static partial void StressFailed(ILogger logger, Exception exception);
}
