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
4. 작업 표시줄 오른쪽 트레이 아이콘을 **우클릭**하면 모양·위치·크기를 바로 바꿀 수 있습니다.
   바꾼 설정은 exe 옆 `imebadge.settings.json`에 저장되어 다음 실행에도 유지됩니다.
5. 종료는 같은 메뉴의 **종료**입니다.

### 트레이 메뉴로 고를 수 있는 것

| 메뉴 | 선택지 | 설명 |
|---|---|---|
| 모양 | 사각 배지 `[한]` | 글자가 든 네모 배지 |
| | 둥근 배지 `(한)` | 알약 모양. 기본값 |
| | 점 `●` | 지름 9px 색 점. 파랑=한글, 회색=영문. 가장 조용함 |
| | 밑줄 `▬` | 커서 바로 아래 짧은 색 선. 글줄을 가리지 않음 |
| | 점 + 바뀔 때만 글자 | 평소엔 점, 한/영이 바뀐 직후 1.5초만 글자 배지 |
| 위치 | 커서 오른쪽 위 / 아래 | 위쪽이 다음 줄 글자를 덜 가림. 기본값은 위 |
| 크기 | 80 / 100 / 130 / 160% | 모니터 DPI 배율에 추가로 곱해짐 |

### exe 하나로 만들기 (배포용)

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

결과물: `bin\Release\net8.0-windows\win-x64\publish\ImeBadge.exe`
(실행 PC에 .NET 8 런타임이 필요합니다. 없으면 `--self-contained true`로 빌드하세요.)

### 디버그 모드

```powershell
dotnet run -- --debug
```

exe 옆에 `imebadge.log`가 생기고, 활성 창·caret 탐색 경로·IME 원시 값이 바뀔 때마다
기록됩니다. 특정 앱에서 판정이 틀리면 이 로그로 원인을 찾습니다.

## 개발자를 위한 상세 설명

### 구조

파일은 `Program.cs` 하나이며 다음 부분으로 나뉩니다.

| 부분 | 역할 |
|---|---|
| `Native` | Win32 API P/Invoke 선언과 상수 |
| `Log` | `--debug`일 때만 쓰는 파일 로그 |
| `UiaCaret` | UI Automation으로 caret 위치 찾기 (Win32 caret이 없는 앱용) |
| `ImeReader` | 활성 창의 caret 위치와 한/영 상태를 한 번 읽어 `Snapshot`으로 반환 |
| `Settings` | 모양·위치·크기 설정. exe 옆 JSON에 저장 |
| `BadgeRenderer` | GDI+로 투명 비트맵(배지·점·밑줄)을 그림 |
| `BadgeForm` | 100ms 타이머 + IME 변경 이벤트 훅으로 `Read()`를 호출하고, 레이어드 창에 비트맵을 올려 caret 옆에 배치. 트레이 메뉴 |

### 한/영 판정: 핵심 함정

한국어 IME에는 두 가지 상태가 따로 있습니다.

| 상태 | 뜻 | 한/영 키를 누르면 |
|---|---|---|
| 열림(open status, `IMC_GETOPENSTATUS`) | IME가 켜져 있는가 | **안 바뀜**. 한 번 한글을 쓰면 계속 1 |
| 변환 모드(conversion mode, `IMC_GETCONVERSIONMODE`) | 비트 0(`IME_CMODE_HANGUL`)이 한글 입력 여부 | **이 비트가 뒤집힘** |

그래서 "열림 = 한글"로 판정하면 IME가 처음 켜질 때 한 번만 바뀌고 그 뒤로는 영원히
`한`에 묶입니다. 올바른 판정은 다음과 같습니다.

```
열림 == 0            → 영문 (이 창에서 아직 한글을 안 씀)
열림 == 1 → 변환 모드 & 0x01 → 1이면 한글, 0이면 영문
```

이 순서는 Windows 11용 한/영 표시기 오픈소스(KoEnVue, IMEIndicatorClockW)의 한국어
판정 코드와 같습니다.

### 상태를 읽는 순서

1. `GetForegroundWindow()` → `GetWindowThreadProcessId()`로 활성 창의 **thread ID**를 얻습니다.
2. **caret 위치** (두 경로)
   - 1순위: `GetGUIThreadInfo(tid)`의 `hwndCaret`/`rcCaret` → `ClientToScreen()`. 메모장·Word 등
     Win32 caret을 만드는 앱.
   - 2순위: UI Automation. `AutomationElement.FocusedElement` → `TextPattern.GetSelection()`.
     크롬 주소창처럼 "문서 처음~커서" 범위를 주는 컨트롤이 있어 시작점을 끝점으로 옮겨 커서 한
     점으로 접은 뒤, 넓이 0이면 `ExpandToEnclosingUnit(Character)`로 한 글자 넓혀 사각형을 얻습니다.
     Chrome/Edge/Electron(Claude 앱, VS Code) 같은 앱용. TextPattern이 없으면 Edit/ComboBox
     컨트롤의 왼쪽 아래 모서리를 씁니다. 읽기 전용이라고 밝힌 요소에는 배지를 띄우지 않습니다.
3. `GetKeyboardLayout(tid)`의 하위 16비트가 `0x0412`(ko-KR)가 아니면 `OtherLang`(`?` 표시)입니다.
4. **한/영 상태**: `ImmGetDefaultIMEWnd(hwndFocus)`(hwndFocus가 0이면 최상위 창)에
   `WM_IME_CONTROL`로 열림 상태와 변환 모드를 순서대로 묻습니다. `SendMessageTimeout(100ms)`로
   응답 없는 앱 때문에 멈추지 않게 합니다.

### 갱신 타이밍

- 100ms 폴링(polling)이 기본입니다.
- 추가로 `SetWinEventHook(EVENT_OBJECT_IME_CHANGE)`를 걸어 OS가 IME 변경을 알려 주면 즉시
  다시 읽습니다. 훅 델리게이트는 필드에 붙잡아 두어 GC 회수를 막습니다.

### 배지 창의 속성

배지는 **레이어드 창(layered window)** 입니다. `UpdateLayeredWindow`에 픽셀별 알파가 있는
32비트 비트맵(`Format32bppPArgb`, 미리 곱한 알파)을 올리므로 둥근 모서리와 부드러운 가장자리가
그대로 나옵니다. 비유하면 종이 스티커가 아니라 유리에 그린 그림입니다. 이 방식에서는
`Form.Opacity`나 `TransparencyKey`를 쓰면 안 됩니다(둘 다 다른 API로 창 전체 투명도를 바꾸어 충돌).
비트맵은 (상태, 모양, 배율)이 바뀔 때만 다시 그리고, 위치만 바뀌면 `SetWindowPos`로 옮깁니다.

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

- **Chrome / Edge / Electron 앱**은 UI Automation 경로로 caret을 찾습니다. 접근성 API를 처음
  건드리는 순간 브라우저가 접근성 트리를 켜므로 아주 무거운 페이지에서는 약간 느려질 수 있습니다.
- **UWP 앱**(설정, 일부 스토어 앱)은 포커스가 다른 프로세스(ApplicationFrameHost)에 있어
  판정이 `Unknown`이 될 수 있습니다.
- **터미널**(Windows Terminal, conhost)은 IME 상태 보고가 부정확한 것으로 알려져 있습니다.
- 폴링 주기 100ms는 `BadgeForm._timer.Interval`에서 조정합니다. CPU 사용량은 1% 미만입니다.
- csproj의 `UseWPF=true`는 WPF 창을 만들기 위한 것이 아니라 `System.Windows.Automation`
  어셈블리를 쓰기 위한 것입니다.
