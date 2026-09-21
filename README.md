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

### 그냥 쓰고 싶다면 (exe 다운로드)

GitHub **Releases** 페이지에서 최신 버전의 exe를 받으면 됩니다. 두 가지가 있습니다.

| 파일 | 크기 | 조건 |
|---|---|---|
| `ImeBadge-win-x64-selfcontained.exe` | 약 14 MB | 아무것도 설치할 필요 없음. **처음 쓰는 분은 이 파일** |
| `ImeBadge-win-x64.exe` | 약 200 KB | PC에 .NET 8 데스크톱 런타임이 있어야 함. 없으면 실행 시 설치 안내 창이 뜸 (`winget install Microsoft.DotNet.DesktopRuntime.8`) |

기본 Windows에는 .NET 8 런타임이 들어 있지 않습니다. 작은 exe는 이미 런타임이 있는 PC(다른 .NET 8
프로그램이나 개발 도구를 설치한 경우)에서 쓰는 보조 파일입니다.

exe는 GitHub Actions가 자동으로 빌드합니다.

| 언제 | 어디서 받나 |
|---|---|
| 어느 브랜치든 푸시 | Actions 탭 → 해당 실행 → Artifacts (3일 보관, 로그인 필요) |
| `main`에 푸시 | Releases의 **`latest`** 사전 릴리스가 항상 최신 빌드로 갱신됨 |
| `v1.2.3` 형태의 태그 푸시 | 그 버전의 정식 Release |

```powershell
git tag v0.5.0
git push origin v0.5.0
```

### 직접 빌드해서 실행

1. .NET 8 SDK를 설치합니다. PowerShell에서: (`global.json`이 SDK 8.0.x를 고정합니다. SDK 10에서는
   `--self-contained false`가 달리 처리되어 작은 exe가 130 MB로 나옵니다.)
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
| 투명도 | 100 / 85 / 70 / 50% | 배지 전체의 투명도. 낮출수록 뒤 글자가 비쳐 보임 |

### exe 하나로 만들기 (배포용)

```powershell
# 런타임 포함 (아무 PC에서나 실행, 약 14 MB)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 런타임 없이 (.NET 8 런타임이 있는 PC 전용, 약 200 KB)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

결과물: `bin\Release\net8.0-windows\win-x64\publish\ImeBadge.exe`

self-contained 크기를 줄이는 설정은 `ImeBadge.csproj`에 있습니다. 비유하면 이삿짐을 쌀 때
안 쓰는 물건은 버리고(트리밍), 남은 것은 압축팩에 넣는(압축) 것입니다.

| 설정 | 효과 | 크기 |
|---|---|---|
| (아무것도 안 함) | .NET 런타임 + WinForms + WPF 전체가 그대로 들어감 | 154 MB |
| `EnableCompressionInSingleFile` | 번들 안의 어셈블리를 압축. 첫 실행이 수백 ms 느려짐 | 66 MB |
| `PublishTrimmed` + `TrimMode=full` | 트리머(ILLink)가 실제로 쓰이는 코드만 남김 | **약 14 MB** |
| `SatelliteResourceLanguages=en` | 프레임워크의 13개 언어 번역 리소스 DLL 제외 | (위에 포함) |

트리밍이 되게 하려고 손본 것들:

- **WPF를 참조하지 않음.** UI Automation을 WPF 래퍼 대신 COM으로 직접 호출합니다(`Uia` 클래스).
- **`ILLink.LinkAttributes.xml`.** WinForms가 쓰는 `ICommand` 인터페이스에 `[TypeConverter("...CommandConverter, PresentationFramework")]`
  속성이 붙어 있어, 트리머가 이 문자열을 따라가 WPF 전체(약 45 MB)를 살려 둡니다. 이 속성 인스턴스만 지워 고리를 끊습니다.
- **`ILLink.Descriptors.xml`.** COM 인터페이스는 메서드 선언 순서가 곧 vtable 슬롯이라, 안 쓰는 자리표시자 메서드를
  트리머가 지우면 엉뚱한 함수가 호출됩니다. `Uia` 형식을 통째로 보존합니다.
- **JSON source generator.** 트리밍하면 리플렉션 기반 `JsonSerializer`가 꺼지므로 설정 저장은 컴파일 시점에
  생성된 코드(`SettingsJsonContext`)를 씁니다.
- **`BuiltInComInteropSupport=true`.** 트리밍 기본값은 COM 호출을 끄는 것이라 명시적으로 켭니다.
- **디버깅 전용 파일 제외.** `mscordaccore`, `createdump` 등 디버거·크래시 덤프용 파일은 실행에 필요 없어 뺍니다.

WinForms는 .NET 8에서 공식적으로 트리밍 미지원(`NETSDK1175`)이므로 `_SuppressWinFormsTrimError`로 경고를 끄고 씁니다.
트리밍된 빌드에서 특정 기능이 깨지면 다음처럼 트리밍만 끄면 66 MB짜리 안전한 빌드가 됩니다.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

Native AOT는 .NET 9부터 WinForms에서 실험적으로 지원되지만, 트리밍만으로 이미 약 14 MB라 추가 이득이 작고
런타임 검증 부담이 커서 적용하지 않았습니다.

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
| `Uia` | Windows 내장 COM UI Automation 인터페이스 선언(`IUIAutomation`, `IUIAutomationTextRange` 등) |
| `UiaCaret` | UI Automation으로 caret 위치 찾기 (Win32 caret이 없는 앱용) |
| `ImeReader` | 활성 창의 caret 위치와 한/영 상태를 한 번 읽어 `Snapshot`으로 반환 |
| `Settings` | 모양·위치·크기 설정. exe 옆 JSON에 저장 (`SettingsJsonContext`로 source-generated 직렬화) |
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
   - 2순위: UI Automation. `IUIAutomation.GetFocusedElement()` → `IUIAutomationTextPattern.GetSelection()`.
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
| `TopMost` + 필요할 때마다 `SetWindowPos(HWND_TOPMOST)` | FlowLauncher처럼 자기도 최상위인 창보다 위에 오도록, 배지가 보이기 시작할 때·활성 창이 바뀔 때·위치가 바뀔 때 다시 맨 위로 올림. 활성 창이 최상위 창이면 매 틱 z-order를 확인하고, 일반 `SetWindowPos`가 막히면(Windows는 마지막 입력을 받지 않은 프로세스가 활성 창 위로 올라가는 것을 제한) `AttachThreadInput`으로 활성 창 스레드의 권한을 잠깐 빌려 올림 |
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
- UI Automation은 WPF의 `System.Windows.Automation` 래퍼가 아니라 COM 인터페이스를 직접 선언해서
  씁니다(`Uia` 클래스). 래퍼를 쓰려면 csproj에 `UseWPF=true`가 필요한데, 그러면 WPF 어셈블리를 정적으로
  참조하게 되어 트리밍으로도 뺄 수 없습니다. COM 인터페이스 선언은 `UIAutomationClient.h`의 vtable
  순서를 그대로 따라야 하므로, 쓰지 않는 메서드도 `_Slot_*` 자리표시자로 남겨 두었습니다.

## 라이선스

Apache License 2.0 — [LICENSE](LICENSE) 참고.
