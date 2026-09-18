# ImeBadge — 한/영 입력 상태 배지

Windows 11에서 **글자를 입력하기 전에** 지금 키보드가 한글인지 영문인지 알 수 있도록,
텍스트 커서(caret) 바로 옆에 작은 배지(`한` / `A`)를 띄워 주는 프로그램입니다.

```
  안녕하세요|한        ← 한글 모드 (파란 배지)
  hello|A             ← 영문 모드 (회색 배지)
```

## 처음 쓰는 분을 위한 설명

키보드 왼쪽 아래의 **한/영 키**는 조명 스위치 같아서, 지금 어느 쪽으로 눌려 있는지
눈으로 확인할 방법이 없습니다. 그래서 글을 쓰기 시작한 뒤에야 "ㅗㄷㅣㅣㅐ" 같은 오타를
보고 알아채게 됩니다. 이 프로그램은 그 스위치 상태를 0.1초마다 확인해서, 커서 옆에
조그맣게 써 줍니다. 커서가 없는 곳(바탕화면, 그림 등)에서는 저절로 사라집니다.

### 설치와 실행

1. .NET 8 SDK를 설치합니다. PowerShell에서:
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
2. 이 폴더에서 실행합니다.
   ```powershell
   dotnet run
   ```
3. 메모장을 열고 한/영 키를 눌러 보세요. 커서 옆 배지가 `한` ↔ `A`로 바뀝니다.
4. 종료는 작업 표시줄 오른쪽 트레이 아이콘을 **우클릭 → 종료**입니다.

### exe 하나로 만들기 (배포용)

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

결과물: `bin\Release\net8.0-windows\win-x64\publish\ImeBadge.exe`
(실행 PC에 .NET 8 런타임이 필요합니다. 없으면 `--self-contained true`로 빌드하세요.)

## 개발자를 위한 상세 설명

### 구조

파일은 `Program.cs` 하나이며 세 부분으로 나뉩니다.

| 부분 | 역할 |
|---|---|
| `Native` | Win32 API P/Invoke 선언과 상수 |
| `ImeReader.Read()` | 활성 창의 caret 위치와 한/영 상태를 한 번 읽어 `Snapshot`으로 반환 |
| `BadgeForm` | 100ms 타이머로 `Read()`를 호출하고 배지 창을 caret 옆으로 이동 |

### 상태를 읽는 순서

1. `GetForegroundWindow()` → `GetWindowThreadProcessId()`로 활성 창의 **thread ID**를 얻습니다.
2. **caret 위치** (두 경로)
   - 1순위: `GetGUIThreadInfo(tid)`의 `hwndCaret`/`rcCaret` → `ClientToScreen()`. 메모장·Word 등
     Win32 caret을 만드는 앱.
   - 2순위: UI Automation. `AutomationElement.FocusedElement` → `TextPattern.GetSelection()`의
     사각형. 선택이 없는 커서는 넓이가 0이라 `ExpandToEnclosingUnit(Character)`로 한 글자 넓혀 묻습니다.
     Chrome/Edge/Electron(Claude 앱, VS Code) 같은 앱용. TextPattern이 없으면 입력 컨트롤의
     왼쪽 아래 모서리를 씁니다.
3. `GetKeyboardLayout(tid)`의 하위 16비트가 `0x0412`(ko-KR)가 아니면 `OtherLang`(`?` 표시)입니다.
4. **한/영 상태** (두 경로)
   - 1순위: **TSF 언어 표시줄**. `CLSID_TF_LangBarMgr` → `GetThreadLangBarItemMgr(tid)`로 대상
     thread의 언어 표시줄 항목 관리자를 얻고, `GUID_LBI_INPUTMODE` 항목의 텍스트/토글 상태를
     읽습니다. 윈도우 트레이의 "한/A" 표시기가 쓰는 것과 같은 통로라 TSF 앱에서도 실시간입니다.
   - 2순위: **IMM32**. `ImmGetDefaultIMEWnd(hwndFocus)`에 `WM_IME_CONTROL`로 `IMC_GETOPENSTATUS`와
     `IMC_GETCONVERSIONMODE`를 묻습니다. `SendMessageTimeout(50ms)`로 멈춤을 방지합니다.

**왜 IMM32만으로는 안 되나?** Windows 11 메모장, 브라우저, Office처럼 TSF를 직접 쓰는 앱은
한/영 전환이 TSF 안에서만 일어나고 IMM32 쪽 입력 컨텍스트에는 통보되지 않습니다. 그래서
IMM32로 물으면 처음 값만 계속 돌아옵니다(첫 1회만 맞고 그 뒤로는 안 바뀌는 증상).

### 디버그 모드

```powershell
dotnet run -- --debug
```

exe 옆에 `imebadge.log`가 생기고, 활성 창·판정 경로·언어 표시줄 항목 목록이 값이 바뀔 때마다
기록됩니다. 특정 앱에서 판정이 틀리면 이 로그로 원인을 찾습니다.

### 배지 창의 속성

`CreateParams`에서 확장 스타일을 덧붙입니다.

| 스타일 | 효과 |
|---|---|
| `WS_EX_NOACTIVATE` + `ShowWithoutActivation` | 배지가 떠도 입력 중인 창의 포커스를 뺏지 않음 |
| `WS_EX_TRANSPARENT` + `WS_EX_LAYERED` | 배지를 클릭해도 아래 창으로 클릭이 통과 |
| `WS_EX_TOOLWINDOW` + `ShowInTaskbar=false` | 작업 표시줄과 Alt+Tab에 나타나지 않음 |
| `SetVisibleCore` override | 첫 판정 전에는 창을 보이지 않아 시작 시 깜빡임 방지 |

`ApplicationHighDpiMode=PerMonitorV2`(csproj)가 없으면 DPI 스케일링이 켜진 모니터에서
caret 좌표와 배지 위치가 어긋납니다.

### 알려진 한계와 다음 단계

- **Chrome / Edge / Electron 앱**은 UI Automation 경로로 caret을 찾습니다. 앱이 TextPattern을
  제공하지 않으면 입력창의 왼쪽 아래에 배지가 붙습니다. 접근성 API를 처음 건드리는 순간
  브라우저가 접근성 트리를 켜므로 아주 무거운 페이지에서는 약간 느려질 수 있습니다.
- **UWP 앱**(설정, 일부 스토어 앱)은 포커스가 다른 프로세스(ApplicationFrameHost)에 있어
  판정이 `Unknown`이 될 수 있습니다.
- 폴링 주기 100ms는 `BadgeForm._timer.Interval`에서 조정합니다. TSF·UIA 호출은 프로세스 간
  통신이라 100ms에 한 번이면 충분하고, CPU 사용량은 1% 미만입니다.
- 이벤트 기반으로 바꾸려면 `ITfLangBarMgr.AdviseEventSink`(IME 모드 변경)와
  `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, OBJID_CARET)`(caret 이동)를 받으면 됩니다.
- csproj의 `UseWPF=true`는 WPF 창을 만들기 위한 것이 아니라 `System.Windows.Automation`
  어셈블리를 쓰기 위한 것입니다.
