using System;
using System.Runtime.InteropServices;

namespace ImeBadge;

// ─────────────────────────────────────────────────────────────────
// Windows 내장 COM UI Automation(UIAutomationCore.dll) 인터페이스 선언.
//
// WPF의 System.Windows.Automation 대신 COM을 직접 호출한다. 관리형 래퍼를 쓰면 csproj에 UseWPF=true가
// 필요해 self-contained exe에 WPF 전체(수십 MB)가 딸려 들어가기 때문이다.
//
// 아래 COM 인터페이스 선언은 UIAutomationClient.h의 vtable 순서를 그대로 따른다. 실제로 쓰지 않는
// 메서드는 자리만 맞추는 placeholder(_Slot*)로 두었다. 순서가 하나라도 어긋나면 엉뚱한 함수가
// 불리므로, 메서드를 추가할 때는 반드시 헤더의 순서를 확인한다. (ILLink.Descriptors.xml 이 이 형식들을
// 트리밍에서 통째로 보존한다.)
// ─────────────────────────────────────────────────────────────────
static class Uia
{
    public const int UIA_BoundingRectanglePropertyId = 30001;
    public const int UIA_ControlTypePropertyId = 30003;
    public const int UIA_ClassNamePropertyId = 30012;
    public const int UIA_HasKeyboardFocusPropertyId = 30008;
    public const int UIA_FrameworkIdPropertyId = 30024;
    public const int UIA_ValuePatternId = 10002;
    public const int UIA_TextPatternId = 10014;
    public const int UIA_ComboBoxControlTypeId = 50003;
    public const int UIA_EditControlTypeId = 50004;

    public enum TextPatternRangeEndpoint { Start = 0, End = 1 }
    public enum TextUnit { Character = 0, Format, Word, Line, Paragraph, Page, Document }

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), ClassInterface(ClassInterfaceType.None)]
    public class CUIAutomation { }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomation
    {
        void _Slot_CompareElements();
        void _Slot_CompareRuntimeIds();
        void _Slot_GetRootElement();
        void _Slot_ElementFromHandle();
        void _Slot_ElementFromPoint();
        IUIAutomationElement GetFocusedElement();
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationElement
    {
        void _Slot_SetFocus();
        void _Slot_GetRuntimeId();
        void _Slot_FindFirst();
        void _Slot_FindAll();
        void _Slot_FindFirstBuildCache();
        void _Slot_FindAllBuildCache();
        void _Slot_BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
        void _Slot_GetCurrentPropertyValueEx();
        void _Slot_GetCachedPropertyValue();
        void _Slot_GetCachedPropertyValueEx();
        void _Slot_GetCurrentPatternAs();
        void _Slot_GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)] object? GetCurrentPattern(int patternId);
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationValuePattern
    {
        void _Slot_SetValue();
        void _Slot_get_CurrentValue();
        [return: MarshalAs(UnmanagedType.Bool)] bool get_CurrentIsReadOnly();
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextPattern
    {
        void _Slot_RangeFromPoint();
        void _Slot_RangeFromChild();
        IUIAutomationTextRangeArray GetSelection();
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextRangeArray
    {
        int get_Length();
        IUIAutomationTextRange GetElement(int index);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextRange
    {
        void _Slot_Clone();
        void _Slot_Compare();
        void _Slot_CompareEndpoints();
        void ExpandToEnclosingUnit(TextUnit unit);
        void _Slot_FindAttribute();
        void _Slot_FindText();
        void _Slot_GetAttributeValue();
        /// <summary>사각형마다 (left, top, width, height) 4개씩 이어진 double 배열.</summary>
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] double[] GetBoundingRectangles();
        void _Slot_GetEnclosingElement();
        void _Slot_GetText();
        void _Slot_Move();
        void _Slot_MoveEndpointByUnit();
        void MoveEndpointByRange(TextPatternRangeEndpoint srcEndPoint, IUIAutomationTextRange range, TextPatternRangeEndpoint targetEndPoint);
        // 이후 메서드는 쓰지 않으므로 생략
    }

    static IUIAutomation? _automation;

    /// <summary>프로세스에 하나만 만들어 재사용하는 UIA 클라이언트 객체.</summary>
    public static IUIAutomation Client => _automation ??= (IUIAutomation)new CUIAutomation();

    /// <summary>COM 객체를 즉시 놓아준다. 100ms마다 만드는 객체가 GC까지 쌓이지 않게.</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            try { Marshal.ReleaseComObject(com); } catch { }
    }
}
