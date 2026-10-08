using System;

namespace ImeBadge;

/// <summary>
/// 한국어 IME 를 읽고 쓸 때 앱마다 다른 점. Win32 없이 테스트한다.
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
}
