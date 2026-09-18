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
2. `GetGUIThreadInfo(tid)`로 다른 프로세스 창의 `hwndFocus`, `hwndCaret`, `rcCaret`을 얻고
   `ClientToScreen()`으로 화면 좌표로 바꿉니다. `hwndCaret == 0`이면 caret이 없는 것으로 보고 숨깁니다.
3. `GetKeyboardLayout(tid)`의 하위 16비트가 `0x0412`(ko-KR)가 아니면 `OtherLang`(`?` 표시)입니다.
4. `ImmGetDefaultIMEWnd(hwndFocus)`로 IME 창을 얻고 `WM_IME_CONTROL` 메시지를 보냅니다.
   - `IMC_GETOPENSTATUS` 결과가 0이 아니면 **한글**.
   - 0이면 `IMC_GETCONVERSIONMODE`의 `IME_CMODE_NATIVE` 비트로 한 번 더 판정합니다.
   - `SendMessageTimeout(SMTO_ABORTIFHUNG, 50ms)`를 써서 응답 없는 앱 때문에 멈추지 않게 합니다.

Windows 11 기본 한글 IME는 TSF(Text Services Framework) 기반이지만, 각 창에 붙는
기본 IME 창(default IME window)이 IMM32 메시지를 TSF 상태로 변환해 주므로 위 방식이 동작합니다.

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

- **Chrome / Edge / VS Code / Electron 앱**은 caret을 직접 그리기 때문에 `hwndCaret`이 0으로
  나와 배지가 숨겨집니다. 한/영 상태 자체는 정상적으로 읽힙니다. 지원하려면 UI Automation의
  `IUIAutomationTextPattern2.GetCaretRange()`로 caret 위치를 얻는 두 번째 경로를 추가하세요.
- **UWP 앱**(설정, 일부 스토어 앱)은 `hwndFocus`가 0일 수 있어 판정이 `Unknown`이 될 수 있습니다.
- 폴링 주기 100ms는 `BadgeForm._timer.Interval`에서 조정합니다. CPU 사용량은 사실상 0%입니다.
- 이벤트 기반으로 바꾸려면 `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, OBJID_CARET)`로
  caret 이동을 받고, 한/영 상태만 폴링하면 됩니다.
