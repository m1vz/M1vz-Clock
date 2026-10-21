using M1vClock.App.DesignSystem.Tokens;

namespace M1vClock.App.Tests;

public sealed class TypeScaleTests
{
    [Fact]
    public void ScaleIsTheFixedSetFromTheBrief()
    {
        Assert.Equal([34.0, 22.0, 17.0, 15.0, 13.0, 11.0], TypeScale.All.Select(s => s.Size));
    }

    [Fact]
    public void LineHeightsFollowTheDesignSpec()
    {
        Assert.Equal([40.0, 28.0, 24.0, 22.0, 18.0, 14.0], TypeScale.All.Select(s => s.LineHeight));
    }

    [Fact]
    public void LeadingIsTightOnLargeTextAndLooserOnSmallText()
    {
        Assert.All(TypeScale.All, style =>
        {
            var minimum = style.Size >= 22 ? 1.1 : 1.2;
            Assert.True(style.LineHeight >= style.Size * minimum, style.Name);
        });
        Assert.True(TypeScale.Display.LineHeight / TypeScale.Display.Size < TypeScale.Body.LineHeight / TypeScale.Body.Size);
    }

    [Fact]
    public void OnlyLargeSizesUseTheDisplayCut()
    {
        Assert.All(TypeScale.All, style =>
            Assert.Equal(style.Size >= 22 ? OpticalCut.Display : OpticalCut.Text, style.Cut));
    }

    [Fact]
    public void NamesAreUnique()
    {
        Assert.Equal(TypeScale.All.Count, TypeScale.All.Select(s => s.Name).Distinct().Count());
    }
}
