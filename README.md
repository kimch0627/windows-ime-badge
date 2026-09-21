# ImeBadge — 한/영 입력 상태 배지

Windows 10/11에서 **글자를 입력하기 전에** 지금 키보드가 한글인지 영문인지 알 수 있도록,
텍스트 커서(caret) 바로 옆에 작은 배지(`한` / `A`)를 띄워 주는 프로그램입니다.

```
  안녕하세요|한        ← 한글 모드 (파란 배지)
  hello|A             ← 영문 모드 (회색 배지)
```

무료이며 소스가 공개되어 있습니다(Apache License 2.0). 개인정보를 수집하지 않습니다([PRIVACY.md](PRIVACY.md)).

## 처음 쓰는 분을 위한 설명

키보드 왼쪽 아래의 **한/영 키**는 조명 스위치 같아서, 지금 어느 쪽으로 눌려 있는지
눈으로 확인할 방법이 없습니다. 그래서 글을 쓰기 시작한 뒤에야 "ㅗㄷㅣㅣㅐ" 같은 오타를
보고 알아채게 됩니다. 이 프로그램은 그 스위치 상태를 0.1초마다 확인해서, 커서 옆에
조그맣게 써 줍니다. 커서가 없는 곳(바탕화면, 그림 등)에서는 저절로 사라집니다.

### 설치

GitHub **[Releases](https://github.com/kimch0627/windows-ime-badge/releases)** 페이지에서 받습니다. 세 가지 중 하나면 됩니다.

| 파일 | 어떤 분께 | 비고 |
|---|---|---|
| `ImeBadge-Setup-<버전>.exe` | **대부분의 분. 권장** | 설치 프로그램. 관리자 권한 불필요. 자동 시작·바로 가기 옵션. 제거는 Windows 설정 → 앱에서 |
| `ImeBadge-win-x64-selfcontained.exe` | 설치 없이 그냥 실행하고 싶은 분 | 약 19 MB 단일 파일. 아무 폴더에 두고 실행 |
| `ImeBadge-win-x64.exe` | PC에 .NET 8 데스크톱 런타임이 이미 있는 분 | 약 200 KB. 없으면 실행 시 설치 안내 창이 뜸 (`winget install Microsoft.DotNet.DesktopRuntime.8`) |

winget 을 쓰신다면 (등록 심사가 끝난 뒤부터):

```powershell
winget install kimch0627.ImeBadge
```

처음 실행하면 Windows SmartScreen 이 "알 수 없는 게시자" 경고를 띄울 수 있습니다(아직 코드 서명 전).
**추가 정보 → 실행** 을 누르면 됩니다. 파일이 바뀌지 않았는지 확인하려면 Release 의 `SHA256SUMS.txt` 와 비교하세요.

| 언제 | 어디서 받나 (모두 Releases 탭) |
|---|---|
| `v1.2.3` 형태의 태그 | 그 버전의 **정식 Release**. 프로그램의 "업데이트 확인"은 이것만 봅니다 |
| `main`에 제목이 `release: v1.2.3`로 시작하는 커밋 푸시 | 워크플로가 `v1.2.3` 태그를 만들고 정식 Release 생성 (태그를 직접 푸시할 수 없는 환경용) |
| `main`에 푸시 | `latest` 사전 릴리스가 항상 최신 빌드로 갱신됨 |
| 다른 브랜치에 푸시 | `dev-<브랜치명>` 사전 릴리스가 그 브랜치의 최신 빌드로 갱신됨 |

### 사용법

1. 실행하면 작업 표시줄 오른쪽 트레이에 파란 커서 모양 아이콘이 생깁니다. 첫 실행이면 안내 풍선이 한 번 뜹니다.
2. 메모장을 열고 한/영 키를 눌러 보세요. 커서 옆 배지가 `한` ↔ `A`로 바뀝니다.
3. 트레이 아이콘을 **더블클릭**하면 설정 창, **우클릭**하면 메뉴가 열립니다.

| 트레이 메뉴 | 설명 |
|---|---|
| 일시 중지 (Ctrl+Alt+H) | 배지를 잠시 끕니다. 화면 공유·게임 중에. 아이콘이 회색으로 바뀝니다 |
| 설정... | 아래 설정 창 |
| 모양 / 위치 / 크기 / 불투명도 | 자주 쓰는 항목을 메뉴에서 바로. 설정 창에서 프리셋에 없는 값을 골랐으면 "사용자 지정 (90%)" 로 표시 |
| 로그인 시 자동 시작 | 체크하면 Windows 에 로그인할 때 같이 실행됩니다 |
| 업데이트 확인 | 새 정식 버전이 있으면 다운로드 페이지 열기 / 나중에 / 이 버전 건너뛰기 중에서 고릅니다 |
| 정보... | 버전, 설정·로그 폴더 열기 |

### 설정 창

| 구역 | 항목 | 설명 |
|---|---|---|
| 모양 | 배지 모양 | 사각 `[한]` / 둥근 `(한)`(기본) / 점 `●` / 밑줄 `▬` / 점, 바뀔 때 1.5초 글자 |
| | 위치 | 커서 오른쪽 위(기본) / 아래. 위쪽이 다음 줄 글자를 덜 가림 |
| | 크기 | 슬라이더 50~300 %. 모니터 DPI 배율에 추가로 곱해짐 |
| | 불투명도 | 슬라이더 30~100 %. 낮출수록 배지 배경이 비쳐 보임. 글자는 항상 또렷하게 유지 |
| | 한글/영문 배지 색 | 색 고르기 대화상자. 글자색(흰/검)은 고른 색의 밝기에 맞춰 자동으로 정해짐 |
| 동작 | 로그인 시 자동 시작 | 트레이 메뉴와 같은 설정 |
| | 전체 화면 앱에서 숨김 | 게임·전체 화면 동영상처럼 모니터 전체를 덮는 창에서는 배지를 띄우지 않음 (기본 켜짐) |
| | Ctrl+Alt+H 단축키 | 다른 프로그램과 겹치면 끌 수 있음 |
| | 새 버전 알림 | 하루 한 번 GitHub 에서 확인. 끄면 네트워크 접속이 전혀 없음 |
| | 확인 주기 | 50~1000 ms. 기본 100. 작을수록 빨리 반응하고 CPU 를 조금 더 씀 |
| 미리보기 | | 현재 설정으로 실제 렌더러가 그린 배지. 왼쪽은 밝은 배경(메모장), 오른쪽은 어두운 배경(VS Code 등) |
| 배지를 띄우지 않을 앱 | 프로세스 이름 목록 | 한 줄에 하나. `.exe` 생략 가능, 끝에 `*` 는 앞부분 일치. 예: `mstsc`, `Unreal*` |

설정은 `%APPDATA%\ImeBadge\settings.json` 에 저장됩니다. 0.x 버전이 exe 옆에 남긴 `imebadge.settings.json` 은 첫 실행 때 자동으로 옮겨 옵니다.

### 문제가 생기면

- 트레이 아이콘 우클릭 → 정보 → **로그 폴더 열기**. `errors.log` 에 오류가 기록됩니다.
- 특정 앱에서 배지가 안 뜨거나 위치가 틀리면 프로그램을 종료한 뒤 `ImeBadge.exe --debug` 로 실행하고 다시 시도하세요.
  `imebadge.log` 에 활성 창·caret 탐색 경로·IME 원시 값이 남습니다.
- [이슈](https://github.com/kimch0627/windows-ime-badge/issues)에 로그와 함께 올려 주세요. 템플릿이 준비되어 있습니다.

## 개발자를 위한 상세 설명

### 빌드와 실행

```powershell
winget install Microsoft.DotNet.SDK.8        # global.json 이 SDK 8.0.x 를 고정
dotnet build ImeBadge.sln                    # 전체 빌드 (경고 = 오류)
dotnet test                                  # Core 단위 테스트 (Windows 없이도 돌아감)
dotnet run --project src/ImeBadge            # 실행
dotnet run --project src/ImeBadge -- --debug # 디버그 로그 켜고 실행
```

배포용 exe·설치 프로그램 만들기, 코드 서명, winget/Store 등록은 [docs/release.md](docs/release.md) 를 보세요.
릴리스 전 수동 확인 목록은 [docs/test-matrix.md](docs/test-matrix.md) 입니다.

### 구조

```
src/ImeBadge.Core/      순수 로직. WinForms·Win32 의존 없음 → Linux 에서도 컴파일·테스트
  AppInfo.cs            제품 이름·URL 상수, AppPaths(설정·로그 폴더)
  Settings.cs           설정 모델, JSON source generator, SettingsStore(읽기·쓰기·이관), ColorHex
  BadgeLayout.cs        caret 과 배지 크기로 배지 위치를 계산
  ProcessFilter.cs      제외 앱 목록 매칭
  VersionInfo.cs        "v1.2.3" 비교 (업데이트 확인)
  Log.cs                디버그 로그·오류 로그, 1 MB 회전
src/ImeBadge/           Windows 앱
  Native/Native.cs      Win32 P/Invoke 와 상수
  Native/Uia.cs         COM UI Automation 인터페이스 선언 (vtable 순서 고정)
  Ime/UiaCaret.cs       UIA 로 caret 찾기 (Chrome/Electron/UWP)
  Ime/ImeReader.cs      활성 창의 caret + 한/영 상태를 Snapshot 으로
  Render/BadgeRenderer.cs  GDI+ 로 투명 비트맵 그리기
  App/BadgeForm.cs      레이어드 창 + 타이머·이벤트 훅 + 트레이 메뉴 + 단축키 + 업데이트 알림
  App/SettingsForm.cs   설정 창 (미리보기 포함)
  App/AboutForm.cs      정보 대화상자
  App/Autostart.cs      HKCU Run 키
  App/SingleInstance.cs 뮤텍스 + 창 메시지로 중복 실행 방지
  App/CrashHandler.cs   잡히지 않은 예외 → errors.log, 반복되면 안내 후 종료
  App/UpdateChecker.cs  GitHub Releases latest 조회
  App/Icons.cs          포함 아이콘 로드
  Assets/*.ico          tools/make_icons.py 로 생성. 언어 중립 도형(커서 + 배지)이라 다른 언어 IME 를 지원해도 그대로 쓴다
tests/ImeBadge.Core.Tests/  xUnit
installer/ImeBadge.iss  Inno Setup 스크립트
```

비유하면 Core 는 "계산기", 앱은 "계산기를 들고 Windows 에 물어보고 그리는 손"입니다. 손은 Windows 에서만 시험할 수 있지만
계산기는 어디서나 시험할 수 있어서, 위치 계산·설정 이관·버전 비교 같은 실수는 CI 가 먼저 잡습니다.

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

### 상태를 읽는 순서 (`ImeReader.Read`)

1. `GetForegroundWindow()` → `GetWindowThreadProcessId()`로 활성 창의 **thread ID** 와 **process ID** 를 얻습니다.
2. 프로세스 이름이 제외 목록에 있거나(`ProcessFilter`), 창이 모니터 전체를 덮으면(`Native.IsFullscreen`) 여기서 끝. 배지를 숨깁니다.
3. **caret 위치** (두 경로)
   - 1순위: `GetGUIThreadInfo(tid)`의 `hwndCaret`/`rcCaret` → `ClientToScreen()`. 메모장·Word 등
     Win32 caret을 만드는 앱.
   - 2순위: UI Automation. `IUIAutomation.GetFocusedElement()` → `IUIAutomationTextPattern.GetSelection()`.
     크롬 주소창처럼 "문서 처음~커서" 범위를 주는 컨트롤이 있어 시작점을 끝점으로 옮겨 커서 한
     점으로 접은 뒤, 넓이 0이면 `ExpandToEnclosingUnit(Character)`로 한 글자 넓혀 사각형을 얻습니다.
     Chrome/Edge/Electron(VS Code) 같은 앱용. TextPattern이 없으면 Edit/ComboBox
     컨트롤의 왼쪽 아래 모서리를 씁니다(높이 0 = 근사). 읽기 전용이라고 밝힌 요소에는 배지를 띄우지 않습니다.
4. `GetKeyboardLayout(tid)`의 하위 16비트가 `0x0412`(ko-KR)가 아니면 `OtherLang`(`?` 표시)입니다.
5. **한/영 상태**: `ImmGetDefaultIMEWnd(hwndFocus)`(hwndFocus가 0이면 최상위 창)에
   `WM_IME_CONTROL`로 열림 상태와 변환 모드를 순서대로 묻습니다. `SendMessageTimeout(100ms)`로
   응답 없는 앱 때문에 멈추지 않게 합니다.

### 갱신 타이밍

- 폴링(polling) 기본 100 ms (`PollIntervalMs`). 배지가 2초 이상 숨겨져 있으면 3배(최소 300 ms)로 늘리고,
  화면이 잠기면(`SystemEvents.SessionSwitch`) 멈춥니다.
- `SetWinEventHook` 으로 IME 변경(`EVENT_OBJECT_IME_CHANGE`)·활성 창 변경(`EVENT_SYSTEM_FOREGROUND`)·
  포커스 변경(`EVENT_OBJECT_FOCUS`)을 받으면 타이머를 기다리지 않고 즉시 다시 읽습니다. 훅 델리게이트는 필드에 붙잡아 두어 GC 회수를 막습니다.
- 타이머 틱과 훅 콜백이 겹쳐도 `_polling` 가드로 한 번에 하나만 실행됩니다.

### 배지 창의 속성

배지는 **레이어드 창(layered window)** 입니다. `UpdateLayeredWindow`에 픽셀별 알파가 있는
32비트 비트맵(`Format32bppPArgb`, 미리 곱한 알파)을 올리므로 둥근 모서리와 부드러운 가장자리가
그대로 나옵니다. 비유하면 종이 스티커가 아니라 유리에 그린 그림입니다. 이 방식에서는
`Form.Opacity`나 `TransparencyKey`를 쓰면 안 됩니다(둘 다 다른 API로 창 전체 투명도를 바꾸어 충돌).
비트맵은 (상태, 모양, 배율, 투명도, 색)이 바뀔 때만 다시 그리고, 위치만 바뀌면 `SetWindowPos`로 옮깁니다.
위치 계산은 `BadgeLayout.Compute`(Core) 이며 단위 테스트가 있습니다.

`CreateParams`에서 확장 스타일을 덧붙입니다.

| 스타일 | 효과 |
|---|---|
| `WS_EX_NOACTIVATE` + `ShowWithoutActivation` | 배지가 떠도 입력 중인 창의 포커스를 뺏지 않음 |
| `TopMost` + 필요할 때마다 `SetWindowPos(HWND_TOPMOST)` | FlowLauncher처럼 자기도 최상위인 창보다 위에 오도록, 배지가 보이기 시작할 때·활성 창이 바뀔 때·위치가 바뀔 때 다시 맨 위로 올림. 활성 창이 최상위 창이면 매 틱 z-order를 확인하고, 일반 `SetWindowPos`가 막히면 `AttachThreadInput`으로 활성 창 스레드의 권한을 잠깐 빌려 올림. 응답 없는 창(`IsHungAppWindow`)에는 붙지 않고, 같은 창에 3회 실패하면 포기 |
| `WS_EX_TRANSPARENT` + `WS_EX_LAYERED` | 배지를 클릭해도 아래 창으로 클릭이 통과 |
| `WS_EX_TOOLWINDOW` + `ShowInTaskbar=false` | 작업 표시줄과 Alt+Tab에 나타나지 않음 |
| `SetVisibleCore` override | 첫 판정 전에는 창을 보이지 않아 시작 시 깜빡임 방지 |

`ApplicationHighDpiMode=PerMonitorV2`(csproj)가 없으면 DPI 스케일링이 켜진 모니터에서
caret 좌표와 배지 위치가 어긋납니다.

### 앱 수명 주기

| 단계 | 하는 일 |
|---|---|
| 시작 | `--debug` 확인 → 로그 폴더 준비 → **뮤텍스**로 중복 실행 확인(두 번째면 기존 인스턴스에 `ImeBadge.ShowSettings` 창 메시지를 broadcast 하고 종료) → 예외 처리기 설치 → 설정 로드(예전 위치 이관) → 자동 시작 경로 갱신 |
| 3초 후 | 첫 실행이면 풍선 알림. 마지막 확인이 24시간 전이면 업데이트 확인(개발 빌드 0.0.0 은 건너뜀) |
| 예외 | UI 스레드 예외는 `errors.log` 에 남기고 계속. 20회 넘으면 안내 후 종료. Poll 안의 예외는 처음 5회만 자세히 기록 |
| 종료 | 훅 해제, 단축키 해제, 트레이 아이콘 제거 |

### 트리밍(self-contained 약 19 MB)

self-contained exe 크기를 줄이는 설정은 `src/ImeBadge/ImeBadge.csproj`에 있습니다. 비유하면 이삿짐을 쌀 때
안 쓰는 물건은 버리고(트리밍), 남은 것은 압축팩에 넣는(압축) 것입니다.

| 설정 | 효과 |
|---|---|
| `EnableCompressionInSingleFile` | 번들 안의 어셈블리를 압축. 첫 실행이 수백 ms 느려짐 |
| `PublishTrimmed` + `TrimMode=full` | 트리머(ILLink)가 실제로 쓰이는 코드만 남김 |
| `SatelliteResourceLanguages=en` | 프레임워크의 13개 언어 번역 리소스 DLL 제외 |
| `ILLinkTreatWarningsAsErrors=false` | C# 경고는 오류로 다루되(TreatWarningsAsErrors) WinForms 프레임워크가 내는 트리머 경고는 제외 |

트리밍이 되게 하려고 손본 것들:

- **WPF를 참조하지 않음.** UI Automation을 WPF 래퍼 대신 COM으로 직접 호출합니다(`Native/Uia.cs`).
- **`ILLink.LinkAttributes.xml`.** WinForms가 쓰는 `ICommand` 인터페이스에 `[TypeConverter("...CommandConverter, PresentationFramework")]`
  속성이 붙어 있어, 트리머가 이 문자열을 따라가 WPF 전체(약 45 MB)를 살려 둡니다. 이 속성 인스턴스만 지워 고리를 끊습니다.
- **`ILLink.Descriptors.xml`.** COM 인터페이스는 메서드 선언 순서가 곧 vtable 슬롯이라, 안 쓰는 자리표시자 메서드를
  트리머가 지우면 엉뚱한 함수가 호출됩니다. `Uia` 형식을 통째로 보존합니다.
- **WinForms 어셈블리 통째로 보존 (`TrimmerRootAssembly`).** WinForms는 실행 중에야 필요해지는 COM 인터페이스가 많아
  멤버 단위로 자르면 창을 만드는 순간 `TypeLoadException`으로 죽습니다(`Control.SetAcceptDrops`의 `IDropTarget`).
  `System.Windows.Forms`와 `System.Windows.Forms.Primitives`는 자르지 않고, 나머지 런타임만 자릅니다. 약 5 MB를 더 쓰는 대신 안전합니다.
- **JSON source generator.** 트리밍하면 리플렉션 기반 `JsonSerializer`가 꺼지므로 설정 저장과 GitHub API 응답 파싱은 컴파일 시점에
  생성된 코드(`SettingsJsonContext`, `GitHubJsonContext`)를 씁니다.
- **`BuiltInComInteropSupport=true`.** 트리밍 기본값은 COM 호출을 끄는 것이라 명시적으로 켭니다.
- **디버깅 전용 파일 제외.** `mscordaccore`, `createdump` 등 디버거·크래시 덤프용 파일은 실행에 필요 없어 뺍니다.

WinForms는 .NET 8에서 공식적으로 트리밍 미지원(`NETSDK1175`)이므로 `_SuppressWinFormsTrimError`로 경고를 끄고 씁니다.
트리밍된 빌드에서 특정 기능이 깨지면 `-p:PublishTrimmed=false` 로 트리밍만 끄면 66 MB짜리 안전한 빌드가 됩니다.

### 알려진 한계와 다음 단계

- **Chrome / Edge / Electron 앱**은 UI Automation 경로로 caret을 찾습니다. 접근성 API를 처음
  건드리는 순간 브라우저가 접근성 트리를 켜므로 아주 무거운 페이지에서는 약간 느려질 수 있습니다.
- **UWP 앱**(설정, 일부 스토어 앱)은 포커스가 다른 프로세스(ApplicationFrameHost)에 있어
  판정이 `Unknown`이 될 수 있습니다.
- **터미널**(Windows Terminal, conhost)은 IME 상태 보고가 부정확한 것으로 알려져 있습니다.
- **코드 서명**은 아직 없습니다. 시크릿을 넣으면 CI 가 자동으로 서명합니다([docs/release.md](docs/release.md)).
- **일본어·중국어 IME** 는 지원하지 않습니다(`?` 표시). 한국 사용자 우선으로 개발 중입니다.

## 라이선스

Apache License 2.0 — [LICENSE](LICENSE) 참고.
