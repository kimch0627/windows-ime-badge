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

    const long FullShape = 0x8;   // IME_CMODE_FULLSHAPE: 언어 표시기의 "문자 너비(전자)"

    [Theory]
    [InlineData(0x0, true, 0x1)]     // 영문 → 한글
    [InlineData(0x1, false, 0x0)]    // 한글 → 영문
    [InlineData(0x1, true, 0x1)]     // 이미 한글
    [InlineData(0x8, true, 0x9)]     // 사용자가 고른 전자는 그대로 둔다
    [InlineData(0x9, false, 0x8)]
    [InlineData(0x80, true, 0x81)]   // 화상 키보드도 그대로
    public void SwitchedMode_ChangesOnlyTheHangulBit(long mode, bool hangul, long expected) =>
        Assert.Equal(expected, KoreanIme.SwitchedMode(mode, hangul));

    // 실제 로그: Windows Terminal·카카오톡의 0xFFFF 에 영문을 요청하자 0xFBA(전자·화상 키보드·기호 비트가 켜짐)가 됐다.
    // 이런 값은 되쓰지 않는다.
    [Theory]
    [InlineData(0xFFFF)]
    [InlineData(0xFBB)]
    [InlineData(0xFBA)]
    [InlineData(0x3)]
    [InlineData(-1)]
    public void UntrustedModes_AreNeverWrittenBack(long mode)
    {
        Assert.Null(KoreanIme.SwitchedMode(mode, hangul: true));
        Assert.Null(KoreanIme.SwitchedMode(mode, hangul: false));
    }

    [Fact]
    public void TrustedModeWithoutFullShape_NeverGainsFullShape()
    {
        for (long mode = 0; mode <= 0xFF; mode++)
        {
            if (!KoreanIme.IsPlausibleMode(mode) || (mode & FullShape) != 0) continue;
            foreach (bool hangul in new[] { true, false })
                Assert.Equal(0, KoreanIme.SwitchedMode(mode, hangul)!.Value & FullShape);
        }
    }

    // 실제 로그: IME 가 닫혀 있던 Chrome 을 한글로 열며 읽은 값을 되쓰자 한글+전자(0x9)가 됐다. 열 때는 한글 비트만 쓴다.
    [Fact]
    public void OpeningAClosedIme_WritesOnlyTheHangulBit()
    {
        Assert.Equal(KoreanIme.HangulBit, KoreanIme.ModeOnOpen);
        Assert.Equal(0, KoreanIme.ModeOnOpen & FullShape);
    }

    [Theory]
    [InlineData(0xE038u, 0x38, true)]    // kbd101a 의 한/영 키(오른쪽 Alt 자리)
    [InlineData(0xE01Du, 0x1D, true)]    // 한자 키(오른쪽 Ctrl 자리)
    [InlineData(0xE11Du, 0x1D, true)]    // E1 접두(Pause)
    [InlineData(0x38u, 0x38, false)]     // 왼쪽 Alt
    [InlineData(0xF2u, 0xF2, false)]     // 103/106 키보드의 전용 한/영 키
    [InlineData(0u, 0, false)]           // 매핑 없음
    public void ScanCode_KeepsTheExtendedPrefixAsAFlag(uint scanEx, int scan, bool extended)
    {
        var (s, e) = KoreanIme.SplitScanCode(scanEx);
        Assert.Equal(scan, s);
        Assert.Equal(extended, e);
    }
}
