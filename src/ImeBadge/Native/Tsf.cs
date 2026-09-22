using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

// ─────────────────────────────────────────────────────────────────
// Text Services Framework(TSF, msctf.dll) 의 전역 compartment 읽기.
//
// IMM32(ImmGetDefaultIMEWnd + WM_IME_CONTROL)는 예전 방식이라 TSF 로만 IME 를 쓰는 앱(Windows Terminal, UWP 일부)에서는
// 값이 없거나 부정확하다. Windows 8 이후의 Microsoft IME 는 "입력 모드"를 전역 compartment
// (GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION)에 두고 언어 표시기가 그것을 읽는다. 여기서는 그 값을 같은 방법으로 읽는다.
// 비유하면 IMM32 는 창마다 붙은 이름표를 읽는 것이고, TSF 전역 compartment 는 게시판에 적힌 현재 모드를 읽는 것이다.
//
// COM 인터페이스 선언은 msctf.h 의 vtable 순서를 그대로 따른다(Uia.cs 와 같은 규칙). 쓰지 않는 메서드는 _Slot_* 로 자리만
// 맞춘다. ILLink.Descriptors.xml 이 이 형식들을 트리밍에서 통째로 보존한다.
// ─────────────────────────────────────────────────────────────────
static class Tsf
{
    static readonly Guid CompartmentKeyboardInputModeConversion = new("CCF05DD8-4A87-11D7-A6E2-00065B84435C");
    static readonly Guid CompartmentKeyboardOpenClose = new("58273AAD-01BB-4164-95C6-755BA0B5162D");

    /// <summary>TF_CONVERSIONMODE_NATIVE. 한국어 IME 에서는 한글 입력 비트(IMM 의 IME_CMODE_HANGUL 과 같은 값).</summary>
    public const int TF_CONVERSIONMODE_NATIVE = 0x0001;

    [ComImport, Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50"), ClassInterface(ClassInterfaceType.None)]
    public class TfThreadMgr { }

    [ComImport, Guid("AA80E801-2021-11D2-93E0-0060B067B86E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITfThreadMgr
    {
        void Activate(out uint clientId);
        void Deactivate();
        void _Slot_CreateDocumentMgr();
        void _Slot_EnumDocumentMgrs();
        void _Slot_GetFocus();
        void _Slot_SetFocus();
        void _Slot_AssociateFocus();
        void _Slot_IsThreadFocus();
        void _Slot_GetFunctionProvider();
        void _Slot_EnumFunctionProviders();
        ITfCompartmentMgr GetGlobalCompartment();
    }

    [ComImport, Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITfCompartmentMgr
    {
        ITfCompartment GetCompartment(ref Guid guid);
        void _Slot_ClearCompartment();
        void _Slot_EnumCompartments();
    }

    [ComImport, Guid("BB08F7A9-607A-4384-8623-056892B64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITfCompartment
    {
        void _Slot_SetValue();
        /// <summary>VARIANT. 값이 한 번도 설정되지 않았으면 VT_EMPTY(null).</summary>
        [return: MarshalAs(UnmanagedType.Struct)] object? GetValue();
    }

    static ITfThreadMgr? _threadMgr;
    static ITfCompartment? _conversion, _openClose;
    static int _failures;
    const int MaxFailures = 3;

    /// <summary>초기화나 읽기가 거듭 실패하면(TSF 가 없는 세션 등) 더 시도하지 않는다.</summary>
    public static bool Available => _failures < MaxFailures;

    /// <summary>
    /// 전역 compartment 의 한/영 상태. TSF 를 쓸 수 없거나 값이 없으면 null.
    /// IMM 과 마찬가지로 열림(open/close)이 0 이면 영문, 아니면 변환 모드의 NATIVE 비트로 판정한다.
    /// 열림 값은 전역 compartment 에 없을 수 있어(스레드별 값) 없으면 변환 모드만 본다.
    /// </summary>
    public static ImeState? ReadState(StringBuilder? dump)
    {
        if (!Available) return null;
        try
        {
            EnsureInit();
            int? conv = ReadInt(_conversion);
            int? open = ReadInt(_openClose);
            dump?.Append($" tsf(open={(open is null ? "-" : open.Value.ToString())} conv={(conv is null ? "-" : "0x" + conv.Value.ToString("X"))})");
            if (conv is null) return null;
            _failures = 0;
            if (open == 0) return ImeState.English;
            return (conv.Value & TF_CONVERSIONMODE_NATIVE) != 0 ? ImeState.Hangul : ImeState.English;
        }
        catch (Exception ex)
        {
            _failures++;
            dump?.Append($" tsf:EXC {ex.GetType().Name} 0x{ex.HResult:X8}");
            if (_failures == MaxFailures) Log.Error("TSF global compartment unavailable; giving up", ex);
            Reset();
            return null;
        }
    }

    static void EnsureInit()
    {
        if (_conversion is not null && _openClose is not null) return;
        _threadMgr ??= (ITfThreadMgr)new TfThreadMgr();
        ITfCompartmentMgr mgr;
        try { mgr = _threadMgr.GetGlobalCompartment(); }
        catch (COMException)
        {
            // 일부 환경에서는 스레드 관리자를 활성화해야 전역 compartment 를 준다. 비활성화는 하지 않는다(프로세스 수명 동안 유지).
            _threadMgr.Activate(out _);
            mgr = _threadMgr.GetGlobalCompartment();
        }
        try
        {
            var conv = CompartmentKeyboardInputModeConversion;
            var open = CompartmentKeyboardOpenClose;
            _conversion = mgr.GetCompartment(ref conv);
            _openClose = mgr.GetCompartment(ref open);
        }
        finally { Uia.Release(mgr); }
    }

    static int? ReadInt(ITfCompartment? c)
    {
        if (c is null) return null;
        return c.GetValue() switch
        {
            int i => i,
            uint u => unchecked((int)u),
            short s => s,
            _ => null,
        };
    }

    static void Reset()
    {
        Uia.Release(_conversion); Uia.Release(_openClose);
        _conversion = null; _openClose = null;
    }
}
