using Xunit;

namespace ImeBadge.Tests;

public sealed class KoreanImeTests
{
    [Theory]
    [InlineData("windowsterminal", true)]                                                       // ImeReader.ProcessName 이 주는 값
    [InlineData("WindowsTerminal", true)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24\WindowsTerminal.exe", true)]
    [InlineData("OpenConsole.exe", true)]
    [InlineData("chrome", false)]
    [InlineData("powershell", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TsfOnlyApps_AreMatchedRegardlessOfCase(string? process, bool expected) =>
        Assert.Equal(expected, KoreanIme.PrefersTsf(process));

    [Fact]
    public void ProcessNamesAsTheReaderSeesThem_StillMatch() =>
        Assert.True(KoreanIme.PrefersTsf(ProcessFilter.Normalize(@"C:\x\WindowsTerminal.exe")));

    [Theory]
    [InlineData(0x0, true)]      // 영문
    [InlineData(0x1, true)]      // 한글
    [InlineData(0x8, true)]      // 크롬: 전자
    [InlineData(0x9, true)]      // 크롬: 한글 + 전자
    [InlineData(0x41, true)]     // 한자 변환 중
    [InlineData(0xFFFF, false)]  // Windows Terminal·카카오톡: 아직 한/영 키를 누르지 않은 입력칸
    [InlineData(0xFBB, false)]
    [InlineData(0xFBA, false)]   // 위 값에 영문으로 바꾸라고 요청한 결과(실제 입력은 한글 그대로였다)
    [InlineData(0x3, false)]     // 가타카나 비트
    [InlineData(-1, false)]
    public void ConversionMode_IsTrustedOnlyWithKoreanBits(long mode, bool expected) =>
        Assert.Equal(expected, KoreanIme.IsPlausibleMode(mode));
}
