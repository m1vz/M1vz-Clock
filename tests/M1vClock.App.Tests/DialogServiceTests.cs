using M1vClock.App.Dialogs;

namespace M1vClock.App.Tests;

public sealed class DialogServiceTests
{
    private static DialogRequest Request(string title = "Título") =>
        new(title, "Mensagem", ["Mudança"], "Como desfazer", "Confirmar", "Cancelar", DialogTone.Neutral);

    [Fact]
    public async Task ConfirmingReturnsTrueAndCancellingReturnsFalse()
    {
        var dialogs = new DialogService();

        var first = dialogs.ShowAsync(Request());
        dialogs.ConfirmCommand.Execute(null);
        var second = dialogs.ShowAsync(Request());
        dialogs.CancelCommand.Execute(null);

        Assert.True(await first);
        Assert.False(await second);
    }

    [Fact]
    public async Task AnAnswerGivenWhileTheDialogIsOpeningIsNotLost()
    {
        var dialogs = new DialogService();
        dialogs.Opened += (_, _) => dialogs.ConfirmCommand.Execute(null);

        var answer = await dialogs.ShowAsync(Request());

        Assert.True(answer);
    }

    [Fact]
    public async Task OpeningASecondDialogDismissesTheFirstAsCancelled()
    {
        var dialogs = new DialogService();

        var first = dialogs.ShowAsync(Request("primeiro"));
        var second = dialogs.ShowAsync(Request("segundo"));

        Assert.False(await first);
        Assert.Equal("segundo", dialogs.Current?.Title);
        dialogs.ConfirmCommand.Execute(null);
        Assert.True(await second);
    }

    [Fact]
    public void AnAnswerWithNothingOpenIsHarmless()
    {
        var dialogs = new DialogService();

        dialogs.ConfirmCommand.Execute(null);
        dialogs.CancelCommand.Execute(null);

        Assert.Null(dialogs.Current);
    }

    [Fact]
    public void TheRequestKnowsWhatItCarries()
    {
        var plain = new DialogRequest("T", "M", [], null, "Ok", null, DialogTone.Error);

        Assert.False(plain.HasChanges);
        Assert.False(plain.HasUndo);
        Assert.False(plain.HasCancel);
        Assert.True(plain.IsError);
        Assert.True(Request() is { HasChanges: true, HasUndo: true, HasCancel: true });
    }
}
