using AgentDemo.Web.Game;

namespace AgentDemo.Tests;

public class BoardNavigatorTests
{
    [Theory]
    [InlineData(4, "ArrowLeft", 3)]
    [InlineData(4, "ArrowRight", 5)]
    [InlineData(4, "ArrowUp", 1)]
    [InlineData(4, "ArrowDown", 7)]
    [InlineData(0, "ArrowLeft", 0)]
    [InlineData(0, "ArrowUp", 0)]
    [InlineData(2, "ArrowRight", 2)]
    [InlineData(2, "ArrowDown", 5)]
    [InlineData(3, "ArrowLeft", 3)]
    [InlineData(5, "ArrowRight", 5)]
    [InlineData(8, "ArrowDown", 8)]
    [InlineData(8, "ArrowRight", 8)]
    [InlineData(6, "ArrowUp", 3)]
    [InlineData(4, "Enter", 4)]
    [InlineData(4, null, 4)]
    public void Move_ClampsAtEdges(int from, string? key, int expected)
    {
        Assert.Equal(expected, BoardNavigator.Move(from, key));
    }

    [Fact]
    public void Move_OutOfRangeIndexIsClamped()
    {
        Assert.Equal(0, BoardNavigator.Move(-3, "ArrowDown"));
        Assert.Equal(8, BoardNavigator.Move(12, "ArrowUp"));
    }

    [Theory]
    [InlineData("1", 0)]
    [InlineData("9", 8)]
    [InlineData("5", 4)]
    public void CellForDigit_Maps1To9(string key, int cell)
    {
        Assert.Equal(cell, BoardNavigator.CellForDigit(key));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("a")]
    [InlineData("10")]
    [InlineData(null)]
    public void CellForDigit_RejectsOthers(string? key)
    {
        Assert.Null(BoardNavigator.CellForDigit(key));
    }
}

public class MatchSettingsValidationTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        new MatchSettings().Validate();
    }

    [Fact]
    public void UndefinedRules_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSettings { Rules = (RuleSet)9 }.Validate());
    }

    [Fact]
    public void UndefinedOpponent_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSettings { Opponent = (Opponent)5 }.Validate());
    }

    [Fact]
    public void UndefinedDifficulty_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSettings { Difficulty = (Difficulty)(-1) }.Validate());
    }

    [Fact]
    public void Match_RejectsInvalidEnums()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Match(new MatchSettings { Rules = (RuleSet)3 }));
    }
}
