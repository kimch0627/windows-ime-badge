using System;
using System.Runtime.InteropServices;

namespace ImeBadge;

// ─────────────────────────────────────────────────────────────────
// MSAA(Microsoft Active Accessibility, oleacc.dll)의 IAccessible 중 caret 위치를 읽는 데 필요한 부분만 선언한다.
//
// 크롬(Chromium)·엣지는 주소창 같은 자체 입력칸에 Win32 caret 을 만들지 않지만, 화면 돋보기가 커서를 따라가도록
// 창의 OBJID_CARET 으로 "가상 caret" 객체를 내보낸다. 그 객체의 accLocation 이 커서 사각형이다.
//
// Uia.cs 와 같은 규칙: 메서드 선언 순서가 곧 vtable 슬롯 번호다(oleacc.idl 순서). IDispatch 를 거치지 않도록
// IUnknown 으로 선언하고 IDispatch 의 4개 슬롯을 자리표시자로 둔다. VARIANT 는 관리형 object 로 마샬링하지 않고
// 같은 크기의 blittable 구조체(Variant)로 넘긴다. (ILLink.Descriptors.xml 이 이 형식들을 트리밍에서 통째로 보존한다.)
// ─────────────────────────────────────────────────────────────────
static class Acc
{
    public const ushort VT_I4 = 3;
    public const int CHILDID_SELF = 0;
    public const int STATE_SYSTEM_INVISIBLE = 0x8000;

    static Guid IID_IAccessible = new("618736e0-3c3d-11cf-810c-00aa00389b71");

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid,
                                                 [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);

    /// <summary>창의 접근성 객체(<paramref name="objectId"/>: OBJID_CARET 등). 없으면 null.</summary>
    public static IAccessible? FromWindow(IntPtr hwnd, int objectId)
    {
        int hr = AccessibleObjectFromWindow(hwnd, unchecked((uint)objectId), ref IID_IAccessible, out object? obj);
        if (hr < 0 || obj is null) return null;
        if (obj is IAccessible acc) return acc;
        Uia.Release(obj);
        return null;
    }

    /// <summary>
    /// VARIANT 와 같은 배치(64비트 24바이트, 32비트 16바이트). 여기서는 VT_I4(정수) 값만 주고받는다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Variant
    {
        public ushort Vt;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Data;
        public IntPtr Data2;

        public Variant(ushort vt, IntPtr data)
        {
            Vt = vt; Reserved1 = 0; Reserved2 = 0; Reserved3 = 0; Data = data; Data2 = IntPtr.Zero;
        }

        public static Variant I4(int value) => new(VT_I4, (IntPtr)value);

        /// <summary>VT_I4 값이면 그 정수, 아니면 null.</summary>
        public readonly int? AsI4 => Vt == VT_I4 ? unchecked((int)(Data.ToInt64() & 0xFFFFFFFF)) : null;
    }

    [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAccessible
    {
        // IDispatch
        void _Slot_GetTypeInfoCount();
        void _Slot_GetTypeInfo();
        void _Slot_GetIDsOfNames();
        void _Slot_Invoke();
        // IAccessible
        void _Slot_get_accParent();
        void _Slot_get_accChildCount();
        void _Slot_get_accChild();
        void _Slot_get_accName();
        void _Slot_get_accValue();
        void _Slot_get_accDescription();
        void _Slot_get_accRole();
        [PreserveSig] int get_accState(Variant varChild, out Variant pvarState);
        void _Slot_get_accHelp();
        void _Slot_get_accHelpTopic();
        void _Slot_get_accKeyboardShortcut();
        void _Slot_get_accFocus();
        void _Slot_get_accSelection();
        void _Slot_get_accDefaultAction();
        void _Slot_accSelect();
        [PreserveSig] int accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, Variant varChild);
        // 이후 메서드는 쓰지 않으므로 생략
    }
}
