using M1vClock.Core.RegistryValues;
using Microsoft.Win32;

namespace M1vClock.Optimize.Tweaks;

public static class TweakCatalog
{
    public const string Module = "Tweaks";

    public const string MouseKey = @"Control Panel\Mouse";

    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public static IReadOnlyList<TweakDefinition> Create() =>
    [
        new("power", "Plano de energia Alto desempenho", false, LiveEffect.None, [new PowerStep(HighPerformance, Balanced)]),
        new("gamemode", "Modo de Jogo", false, LiveEffect.None,
        [
            new DwordStep(User(@"Software\Microsoft\GameBar", "AutoGameModeEnabled"), on: 1, off: 0, absentIsOn: true),
        ]),
        new("gamedvr", "Gravação em segundo plano do Game Bar", false, LiveEffect.None,
        [
            new DwordStep(User(@"System\GameConfigStore", "GameDVR_Enabled"), on: 0, off: 1, absentIsOn: false),
            new DwordStep(User(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"), on: 0, off: 1, absentIsOn: false),
        ]),
        new("hags", "Agendamento de GPU acelerado por hardware", true, LiveEffect.None,
        [
            new DwordStep(Machine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), on: 2, off: 1, absentIsOn: false),
        ]),
        new("windowed", "Otimizações para jogos em janela", false, LiveEffect.None,
        [
            new OptionStep(User(@"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings"), "SwapEffectUpgradeEnable", on: "1", off: "0"),
        ]),
        new("mouse", "Aceleração do mouse", false, LiveEffect.Mouse,
        [
            new StringStep(User(MouseKey, "MouseSpeed"), on: "0", off: "1"),
            new StringStep(User(MouseKey, "MouseThreshold1"), on: "0", off: "6") { Decides = false },
            new StringStep(User(MouseKey, "MouseThreshold2"), on: "0", off: "10") { Decides = false },
        ]),
    ];

    private static RegistryValueAddress User(string key, string name) => new(RegistryHive.CurrentUser, RegistryView.Registry64, key, name);

    private static RegistryValueAddress Machine(string key, string name) => new(RegistryHive.LocalMachine, RegistryView.Registry64, key, name);
}
