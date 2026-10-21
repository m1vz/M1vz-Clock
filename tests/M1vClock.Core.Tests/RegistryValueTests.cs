using System.Text.Json.Nodes;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace M1vClock.Core.Tests;

public sealed class RegistryValueTests
{
    private static readonly string[] MultiStringSample = ["a", "b"];
    private static readonly byte[] BinarySample = [0, 1, 254, 255];

    public static TheoryData<RegistryValueState> States => new()
    {
        RegistryValueState.DWord(-1),
        RegistryValueState.QWord(long.MaxValue),
        RegistryValueState.Text("Alto desempenho"),
        new RegistryValueState(true, true, RegistryValueKind.ExpandString, @"%SystemRoot%\System32"),
        new RegistryValueState(true, true, RegistryValueKind.MultiString, MultiStringSample),
        new RegistryValueState(true, true, RegistryValueKind.Binary, BinarySample),
        RegistryValueState.Absent(keyExists: true),
        RegistryValueState.Absent(keyExists: false),
    };

    [Theory]
    [MemberData(nameof(States))]
    public void StateSurvivesJsonRoundTrip(RegistryValueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = state.ToJson();

        var restored = RegistryValueState.FromJson(JsonNode.Parse(json.ToJsonString())!.AsObject());

        Assert.True(JsonNode.DeepEquals(json, restored.ToJson()));
    }

    [Fact]
    public void AbsentValuesMatchRegardlessOfKeyExistence()
    {
        var handler = new RegistryValueHandler(new FakeRegistry());

        var match = handler.StatesMatch(RegistryValueState.Absent(true).ToJson(), RegistryValueState.Absent(false).ToJson());

        Assert.True(match);
    }

    [Fact]
    public void DifferentDataDoesNotMatch()
    {
        var handler = new RegistryValueHandler(new FakeRegistry());

        Assert.False(handler.StatesMatch(RegistryValueState.DWord(1).ToJson(), RegistryValueState.DWord(0).ToJson()));
        Assert.False(handler.StatesMatch(RegistryValueState.DWord(1).ToJson(), RegistryValueState.Absent(true).ToJson()));
    }

    [Fact]
    public void TargetIsCaseInsensitiveLikeTheRegistry()
    {
        var a = new RegistryValueAddress(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Teste", "Valor");
        var b = new RegistryValueAddress(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\teste", "VALOR");

        Assert.Equal(a.Target, b.Target);
    }

    [Fact]
    public async Task RealRegistryChangeIsUndoneAndCreatedKeyIsRemoved()
    {
        var keyPath = $@"Software\m1vClock.Tests\{Guid.NewGuid():N}";
        var address = new RegistryValueAddress(RegistryHive.CurrentUser, RegistryView.Default, keyPath, "Valor");
        using var temp = new TempDirectory();
        using var journal = new ChangeJournal(
            new JournalStore(temp.File("journal.json"), NullLogger<JournalStore>.Instance),
            [new RegistryValueHandler(new WindowsRegistryAccess())],
            new FakeRestorePointGate(),
            new FakeTimeProvider(),
            NullLogger<ChangeJournal>.Instance);

        try
        {
            var applied = await journal.ApplyAsync(
                RegistryValueChanges.Set("Testes", "Valor de teste", address, RegistryValueState.DWord(42)),
                TestContext.Current.CancellationToken);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                Assert.Equal(42, key?.GetValue("Valor"));
            }

            var undo = journal.Undo(applied.Entry.Id);

            Assert.Equal(UndoStatus.Undone, undo.Status);
            using var removed = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Null(removed);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\m1vClock.Tests", throwOnMissingSubKey: false);
        }
    }
}
