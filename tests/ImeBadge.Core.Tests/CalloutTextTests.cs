using System.Linq;
using Xunit;

namespace ImeBadge.Tests;

public sealed class CalloutTextTests
{
    /// <summary>글자 하나 = 폭 1(고정폭). 줄바꿈 자리만 본다.</summary>
    static float Chars(string s) => s.Length;

    [Fact]
    public void FitsOnOneLine_Unchanged()
    {
        Assert.Equal("가나 다라", CalloutText.WrapAtSpaces("가나 다라", 10, Chars));
    }

    [Fact]
    public void BreaksOnlyAtSpaces()
    {
        const string text = "KakaoTalk 의 다른 창이 앞으로 나왔어요. 쓰던 곳으로 돌아가려면 Alt+Tab 을 누르세요.";
        var lines = CalloutText.WrapAtSpaces(text, 30, Chars).Split('\n');
        Assert.True(lines.Length > 1);
        Assert.All(lines, l => Assert.True(l.Length <= 30, l));
        Assert.Equal(text, string.Join(" ", lines));   // 낱말이 갈라지지 않았다("돌아가려" + "면" 이 아님)
        Assert.Contains(lines, l => l.Split(' ').Contains("돌아가려면"));
    }

    [Fact]
    public void WordLongerThanALine_IsLeftWhole()
    {
        Assert.Equal("가\n아주아주아주긴낱말\n나", CalloutText.WrapAtSpaces("가 아주아주아주긴낱말 나", 4, Chars));
    }

    [Fact]
    public void KeepsExistingLineBreaks()
    {
        Assert.Equal("가나\n다라 마", CalloutText.WrapAtSpaces("가나\n다라 마", 10, Chars));
        Assert.Equal("\n가", CalloutText.WrapAtSpaces("\n가", 10, Chars));
    }
}
