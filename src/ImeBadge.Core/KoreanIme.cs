using System;

namespace ImeBadge;

/// <summary>
/// 한국어 IME 를 읽고 쓸 때 앱마다 다른 점과, IME 에 쓸 값. Win32 없이 테스트한다.
/// </summary>
public static class KoreanIme
{
    /// <summary>
    /// IMM 이 한/영을 틀리게 보고하고, IMM 으로 바꾸라는 요청(WM_IME_CONTROL)도 실제 입력을 바꾸지 못하는 앱. TSF 로만 IME 를 쓴다.
    /// 이름은 <see cref="ProcessFilter.Normalize"/> 의 결과(소문자, .exe 없음)와 견준다.
    /// </summary>
    static readonly string[] TsfOnlyProcesses = { "windowsterminal", "openconsole" };

    /// <summary>이 프로세스가 TSF 로만 IME 를 쓰는가. 경로·대소문자·".exe" 는 상관없다.</summary>
    public static bool PrefersTsf(string? process) =>
        !string.IsNullOrEmpty(process) && Array.IndexOf(TsfOnlyProcesses, ProcessFilter.Normalize(process)) >= 0;

    /// <summary>
    /// 한국어 IME 가 실제로 쓰는 변환 모드 비트: 한글(NATIVE 0x1), 전자(FULLSHAPE 0x8), 한자 변환(HANJACONVERT 0x40), 화상 키보드(SOFTKBD 0x80).
    /// </summary>
    const uint KnownBits = 0x0001 | 0x0008 | 0x0040 | 0x0080;

    /// <summary>
    /// IMM 이 돌려준 변환 모드가 한국어 IME 의 값으로 볼 만한가. 앱이 IMM 대신 TSF 로 IME 를 쓰거나 아직 한/영 키를 한 번도 누르지 않은
    /// 입력칸은 0xFFFF·0xFBB 처럼 가타카나·로마자 비트까지 켜진 값을 돌려준다(Windows Terminal·카카오톡에서 봄). 그런 값은 실제 입력과
    /// 맞지 않을 수 있고, 그 값을 바꿔도 실제 입력은 그대로다.
    /// </summary>
    public static bool IsPlausibleMode(long mode) => mode >= 0 && (mode & ~(long)KnownBits) == 0;

    /// <summary>IMM 변환 모드의 한글 비트(IME_CMODE_HANGUL, IME_CMODE_NATIVE 와 같은 값).</summary>
    public const long HangulBit = 0x0001;

    /// <summary>
    /// 닫혀 있던 IME 를 한글로 열 때 쓰는 변환 모드: 한글 비트만. 열면서 변환 모드를 읽어 되쓰지 않는다
    /// (Chrome 에서 열면서 읽은 값에 있던 전자 비트가 그대로 이어져 한글+전자(0x9)가 된 적이 있다).
    /// </summary>
    public const long ModeOnOpen = HangulBit;

    /// <summary>
    /// 열려 있는 IME 의 변환 모드에서 한글 비트만 바꾼 값(IMC_SETCONVERSIONMODE 에 쓸 값). 사용자가 고른 전자 같은 다른 비트는 그대로 둔다.
    /// 한국어 IME 의 값이 아니면(<see cref="IsPlausibleMode"/>) null 이고 쓰지 않는다: 0xFFFF·0xFBB 같은 값을 되쓰면 IME 가 그 비트를
    /// 실제 상태로 받아들여, 문자 너비가 전자로 바뀌고 화상 키보드·기호 같은 비트까지 켜진다(0xFFFF 에 영문을 요청하면 0xFBA 가 됐다).
    /// </summary>
    public static long? SwitchedMode(long mode, bool hangul)
    {
        if (!IsPlausibleMode(mode)) return null;
        return hangul ? mode | HangulBit : mode & ~HangulBit;
    }

    /// <summary>
    /// MapVirtualKey(MAPVK_VK_TO_VSC_EX) 의 값을 SendInput 에 넣을 스캔 코드와 확장 키 여부로 나눈다(0xE0·0xE1 접두가 있으면 확장 키).
    /// 한/영 키가 오른쪽 Alt 자리인 배열(kbd101a 등)에서 VK_HANGUL 은 E0 38 이다. 접두를 버리고 0x38 만 보내면 스캔 코드를 보는
    /// 프로그램(원격 데스크톱·가상 머신 등)에는 왼쪽 Alt 를 누른 것으로 보인다.
    /// </summary>
    public static (ushort Scan, bool Extended) SplitScanCode(uint scanEx) =>
        ((ushort)(scanEx & 0xFF), (scanEx & 0xFF00) is 0xE000 or 0xE100);
}
