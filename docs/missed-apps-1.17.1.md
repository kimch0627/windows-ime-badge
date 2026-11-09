# 배지가 안 뜬 앱 보고서 분석 (1.17.1)

1.17.1 의 "배지가 안 뜬 앱" 페이지에서 복사한 보고서 두 건(2026-11-09)을 분석하고 고친 내용입니다. 보고서에 오른 다섯 앱 가운데
세 앱(Xshell·Excel·ChatGPT)은 배지가 실제로 안 뜬 것이 아니라 창이나 입력칸을 옮기는 0.1초 남짓한 순간을 세어 오른 것이었습니다.
ChatGPT 에서는 그 순간 배지가 실제로 깜빡이기도 해서 표시도 함께 고쳤습니다.

## 받은 보고서

```yaml
ImeBadge 1.17.1 — 배지가 안 뜬 앱 (2026-11-09 08:10)
OS: Microsoft Windows 10.0.26100 (X64)
- snipaste: 커서 위치를 알려 주지 않는 앱 [noCaret] · 한 번도 안 뜸 · 안 뜬 순간 4 / 뜬 순간 0 · 마지막 17:19 · fg='Qt624QWindowToolSaveBits' uia=50032
- ttalk: 커서 위치를 알려 주지 않는 앱 [noCaret] · 일부 입력칸에서 안 뜸 · 안 뜬 순간 20 / 뜬 순간 179 · 마지막 08:09 · fg='TfrmSendMessage' focus='Internet Explorer_Server'
- excel: 커서 위치를 알려 주지 않는 앱 [noCaret] · 일부 입력칸에서 안 뜸 · 안 뜬 순간 26 / 뜬 순간 318 · 마지막 07:14 · fg='XLMAIN' uia=50033
- xshell: 커서 위치를 알려 주지 않는 앱 [noCaret] · 일부 입력칸에서 안 뜸 · 안 뜬 순간 5 / 뜬 순간 43 · 마지막 11:14 · fg='Xshell8::MainFrame_1' uia=50032

ImeBadge 1.17.1 — 배지가 안 뜬 앱 (2026-11-09 08:11)
OS: Microsoft Windows 10.0.26300 (X64)
- chatgpt: 커서 위치를 알려 주지 않는 앱 [noCaret] · 일부 입력칸에서 안 뜸 · 안 뜬 순간 5 / 뜬 순간 531 · 마지막 08:08 · fg='Chrome_WidgetWin_1'
```

근거로는 두 번째 보고서를 낸 PC(Windows 10.0.26300)의 디버그 로그(`--debug`, 2026-09-30 ~ 11-06)를 썼습니다. 같은 창 종류가 나온 줄과
그 앞뒤 줄을 비교했습니다. ttalk 는 이 PC 에 없어 로그가 없습니다.

## 앱마다 일어난 일

| 앱 | 로그에서 본 것 | 판정 |
|---|---|---|
| Xshell | 창에 들어올 때 `focus='Xshell8::MainFrame_0' … uia:none(ct=50032 …)` 이 한 번 찍히고 13~100ms 뒤 `focus='Edit' … caret:win32('Edit')` 로 바뀝니다. `focus='(null)'`·`Xshell8::CoreApp`·`AfxFrameOrView140u … wnd:hidden` 도 같은 자리에 나옵니다. 주 창과 입력칸의 한/영이 달라 그 사이 한/영이 바뀐 것으로 보였습니다 | 집계 오류. 배지는 곧 떴습니다 |
| Excel | 보통 셀에서는 `caret:win32('EXCEL7')` 로 배지가 뜹니다. 시트와 수식 입력줄을 오가는 동안 UI Automation 포커스가 `XLDESK`·`EXCEL6` Pane(50033)으로 잠깐 잡힌 뒤 `caret:win32('EXCEL6')` 로 이어집니다 | 대부분 집계 오류. 커서가 없는 상태가 더 길게 이어지는 경우가 있는지는 확인하지 못했습니다 |
| ChatGPT | `uia:EXC COMException 0x80040201` 과 `uia:none(ct=50033 class='View' fw=Chrome …)` 이 입력 도중 한 번씩 찍힙니다. 보고서에 `uia=` 가 없는 것(컨트롤 종류 0)이 접근성 오류 경로와 맞습니다 | 실제로 배지가 잠깐 사라졌고, 집계도 됐습니다 |
| Snipaste | `fg='Qt624QWindowToolSaveBits' … uia:none(ct=50033 … fw=Win32 …) open:timeout tsf(open=- conv=-)`. Qt 창이 UI Automation 을 내놓지 않고(프레임워크가 Win32), 입력기도 한/영 질문에 답하지 않습니다 | 캡처 화면은 입력기를 끈 곳이라 세지 않는 것이 맞습니다. 글자 도구는 커서 정보가 없어 지원할 수 없습니다 |
| ttalk | 보고서뿐입니다. 메시지 칸(`Internet Explorer_Server`)에서 컨트롤 종류가 0 이라 UI Automation 이 오류를 냈거나 포커스된 요소를 주지 않은 경우입니다 | 원인 미확정. 재시도·재사용이 들어가고, 다음 보고서에 오류 코드가 남습니다 |

## 고친 내용

**안 뜬 순간은 0.5초 넘게 계속 안 보일 때만 셉니다.** `MissConfirm`(src/ImeBadge.Core/MissedApps.cs)이 입력하는 순간에 기다리기 시작합니다.
그사이 배지가 뜨거나, 입력칸이 아닌 곳으로 가거나, 활성 창이 바뀌면 버립니다. 키를 계속 누르고 있지 않아도 됩니다. 한/영 키를 한 번 누른 뒤
0.5초 동안 배지가 계속 안 보이면 놓친 것입니다. 배지가 뜬 순간은 예전처럼 바로 셉니다.

**한/영 변화는 같은 입력칸(포커스 창)에서 바뀐 때만 입력으로 봅니다.** 예전에는 같은 활성 창이면 됐습니다. 그래서 Xshell 주 창 → 입력칸처럼
포커스가 옮겨 가며 칸마다 다른 한/영이 보인 것도 사용자가 한/영을 바꾼 것으로 잡혔습니다.

**UI Automation 이 포커스된 요소를 주지 못하면 한 번 다시 묻습니다.** 오류(Chromium 의 0x80040201 등)이거나 요소가 없을 때입니다.
그래도 안 되면 같은 창에서 1초 안에 찾은 결과를 그대로 씁니다(A11yCaret). 응답이 늦을 때(0.4초 초과) 하던 것과 같은 규칙입니다.
ChatGPT 같은 Chromium 앱에서 배지가 깜빡이지 않게 하는 부분입니다.

**커서도 없고 입력기도 한/영을 답하지 않는 곳은 세지 않습니다.** Snipaste 캡처 화면이나 게임처럼 입력기를 끈 창입니다. 이런 곳은 커서를 찾아도
한/영을 모르니 배지를 띄울 수 없습니다. 관리자 권한 앱은 둘 다 막히지만 그 판정이 먼저라 그대로 "관리자 권한으로 실행된 앱" 으로 오릅니다.

**보고서에 커서를 못 찾은 사정이 들어갑니다.**

| 표시 | 뜻 |
|---|---|
| `uia-err=0x80040201` | UI Automation 이 오류를 냈다(다시 물어도 같음) |
| `uia=nofocus` | UI Automation 이 포커스된 요소를 주지 않았다 |
| `caret=empty` | Windows 커서(caret) 창은 있는데 높이가 0 이다 |

같은 변경에 Paseo(Electron)의 키보드 포커스만 받는 요소를 입력칸으로 세지 않는 수정(`InputSurface.MayTakeText` 에 Chromium 규칙)도 함께 들어 있습니다.

## 바뀐 파일

| 파일 | 내용 |
|---|---|
| src/ImeBadge.Core/MissedApps.cs | `MissConfirm` 추가, Chromium 묶음(Group) 규칙 |
| src/ImeBadge/App/BadgeForm.cs | `TrackMissed` 가 확인 대기와 같은 입력칸 조건을 쓴다. 보고서에 `MissNote` |
| src/ImeBadge/Ime/UiaCaret.cs, FocusFacts.cs | 실패를 `A11yHit.Failure` 로 돌려준다 |
| src/ImeBadge/Ime/A11yCaret.cs | 실패하면 한 번 다시 묻고, 그래도 안 되면 1초 안의 결과를 쓴다 |
| src/ImeBadge/Ime/ImeReader.cs | `Snapshot.MissNote`, 커서도 입력기 응답도 없으면 세지 않음 |
| src/ImeBadge.Core/Strings.cs | 설정 창 설명에 "0.5초 안에 다시 뜬 것도 세지 않습니다"(한·영) |
| tests/ImeBadge.Core.Tests/MissedAppTrackerTests.cs | 확인 대기 3건, Chromium 규칙 1건 |
| docs/test-matrix.md | 3-3 절에 "잠깐 사라짐"·"입력기를 끈 창"·"접근성 오류" |

## 확인한 것과 못 한 것

솔루션 빌드(경고 = 오류)와 단위 테스트 562개가 통과했습니다. 새 테스트는 로그의 장면을 옮겼습니다. 0.1초 만에 다시 뜬 경우는 세지 않고,
끝까지 안 뜨는 경우는 키를 뗀 뒤에도 세며, 창이 바뀌거나 입력칸이 아닌 곳으로 가면 기다리던 것을 버립니다.

실제 앱 화면을 조작하는 확인은 하지 않았습니다. 확인하려던 때 사용자가 다른 앱에서 입력 중이라, 화면을 조작하면 입력이 엉뚱한 창으로 갈 수
있었습니다. 같은 소스로 빌드한 진단 프로그램을 45초 동안 지켜만 봤고, Win32 커서 경로(카카오톡 입력칸)에서 오류가 없었습니다.
Xshell·Excel·ChatGPT·Snipaste 확인 방법은 docs/test-matrix.md 3-3 절에 있습니다.

## 남은 한계

- **Snipaste 글자 도구:** Qt 가 접근성 없이 빌드돼 커서 위치를 알 길이 없어, 이번 변경으로도 배지가 뜨지 않습니다. Qt 가 입력기에 알려 주는
  후보 창 위치는 다른 프로세스에서 읽을 수 없습니다.
- **ttalk:** 원인이 확정되지 않았습니다. 다시 물어도 계속 실패하면 다음 보고서의 `uia-err=`·`uia=nofocus`·`caret=empty` 로 갈립니다.
  `caret=empty` 라면 MSHTML 이 한글 조합 중에 커서를 숨기는 경우이고, 그때는 같은 칸의 직전 커서를 잠깐 쓰는 방법을 검토합니다.
- **Excel:** 시트에서 Windows 커서가 0.5초 넘게 없는 상태가 실제로 있다면 계속 "일부 입력칸에서 안 뜸" 으로 오릅니다. 그 상태에서
  `--debug` 로그를 받으면 활성 셀 위치를 쓸 수 있는지 볼 수 있습니다.

