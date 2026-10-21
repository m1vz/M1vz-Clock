using Microsoft.Extensions.Logging;

namespace M1vClock.Core;

internal static partial class CoreLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Aberto pelo shell: {Target}")]
    public static partial void OpenedByShell(ILogger logger, string target);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Aberto sem administrador pelo Explorer: {Target}")]
    public static partial void OpenedDeElevated(ILogger logger, string target);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Histórico de alterações ilegível; cópia guardada em {Path}.")]
    public static partial void JournalUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Histórico de alterações recuperado da cópia {Path}.")]
    public static partial void JournalRecovered(ILogger logger, string path);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "A cópia do histórico {Path} também não pôde ser lida.")]
    public static partial void JournalBackupUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Information, Message = "Alteração {Status}: {Title} em {Target} ({Module}).")]
    public static partial void ChangeRecorded(ILogger logger, Journal.ChangeStatus status, string title, string target, string module);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "O Windows não manteve a alteração {Title} em {Target}; voltando ao valor original.")]
    public static partial void ChangeNotKept(ILogger logger, string title, string target);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Error, Message = "Falha ao aplicar {Title} em {Target}.")]
    public static partial void ChangeApplyFailed(ILogger logger, Exception exception, string title, string target);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Error, Message = "Falha ao voltar {Target} ao valor original depois de um erro.")]
    public static partial void ChangeRollbackFailed(ILogger logger, Exception exception, string target);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Information, Message = "Alteração desfeita: {Title} em {Target}.")]
    public static partial void ChangeUndone(ILogger logger, string title, string target);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Warning, Message = "Desfazer {Title} não restaurou o valor original de {Target}.")]
    public static partial void UndoNotVerified(ILogger logger, string title, string target);

    [LoggerMessage(EventId = 1116, Level = LogLevel.Error, Message = "Falha ao desfazer {Title} em {Target}.")]
    public static partial void UndoFailed(ILogger logger, Exception exception, string title, string target);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Configurações em {Path} foram criadas por uma versão mais nova (schema {Version}); usando padrões sem gravar.")]
    public static partial void SettingsNewerSchema(ILogger logger, string path, int version);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Error, Message = "Não foi possível ler as configurações em {Path}.")]
    public static partial void SettingsUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "Configurações recuperadas da cópia {Path}.")]
    public static partial void SettingsRecovered(ILogger logger, string path);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Error, Message = "A cópia {Path} também não pôde ser lida.")]
    public static partial void SettingsBackupUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Warning, Message = "Gravação ignorada: as configurações pertencem a uma versão mais nova do m1vClock.")]
    public static partial void SettingsSaveSkipped(ILogger logger);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Warning, Message = "Arquivo de configurações ilegível guardado em {Path}.")]
    public static partial void SettingsQuarantined(ILogger logger, string path);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Proteção do sistema desligada: alteração sem ponto de restauração.")]
    public static partial void RestoreProtectionDisabled(ILogger logger);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "Ponto de restauração criado.")]
    public static partial void RestorePointCreated(ILogger logger);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "O Windows não criou outro ponto de restauração porque já existe um recente.")]
    public static partial void RestorePointRecent(ILogger logger);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Error, Message = "Falha ao criar ponto de restauração (código {Code}).")]
    public static partial void RestorePointFailed(ILogger logger, int code);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "Estado do ponto de restauração ilegível em {Path}; será recriado.")]
    public static partial void RestoreStateUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Warning, Message = "SRSetRestorePoint recusou o pedido (status {Status}).")]
    public static partial void RestorePointRefused(ILogger logger, int status);

    [LoggerMessage(EventId = 1307, Level = LogLevel.Warning, Message = "Não foi possível listar os pontos de restauração.")]
    public static partial void RestorePointListFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Executando {CommandLine}")]
    public static partial void ProcessStarting(ILogger logger, string commandLine);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "Cancelado pelo usuário: {CommandLine}")]
    public static partial void ProcessCancelled(ILogger logger, string commandLine);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Information, Message = "{CommandLine} terminou com código {ExitCode} em {Seconds} s. Saída: {Output} {Error}")]
    public static partial void ProcessFinished(ILogger logger, string commandLine, int exitCode, double seconds, string output, string error);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning, Message = "{CommandLine} não terminou em {Seconds} s e foi encerrado.")]
    public static partial void ProcessTimedOut(ILogger logger, string commandLine, double seconds);
}
