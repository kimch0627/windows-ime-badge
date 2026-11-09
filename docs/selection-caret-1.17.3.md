# 글을 선택하면 배지 자리가 어긋나던 문제 (1.17.3)

2026-11-09 에 받은 신고: 티톡(ttalk) 대화 입력칸에서 글을 선택하면 배지가 엉뚱한 곳에 뜬다. 화면을 보면 밑줄 배지(점선, 선택 영역 표시)가
선택한 글 아래가 아니라 위, 글의 첫 글자 왼쪽 위에 겹쳐 있었습니다. 선택은 Shift+방향키·Home·End 로 했습니다.

입력칸은 글을 선택한 동안 Windows 커서(caret)를 평소와 다르게 알려 줍니다. 배지는 그 사각형을 그대로 믿고 자리를 잡았기 때문에 어긋났습니다.
1.17.3 부터는 Windows 표준 입력칸(Edit·RichEdit 계열)에서 글을 선택한 동안 선택 시작·끝 글자의 위치를 따로 물어 커서를 바로잡습니다.

## 신고 화면에서 잰 것

신고 화면(배율 100%, 사용자 설정은 밑줄 모양·크기 130%·자리 "아래 왼쪽")의 픽셀을 읽었습니다.

| 무엇 | 위치 |
|---|---|
| 선택한 글(파란 바탕 `#0078D7`) | x=24 부터, y=757~771 |
| 밑줄 배지 막대 | x=17~36, y=759~762 |

밑줄 배지는 커서 아래 1px 에 막대의 위쪽을 둡니다(`BadgeLayout`). 막대 위쪽이 759 라는 것은 배지가 받은 커서의 아래쪽 끝이 758,
곧 선택한 글의 맨 위였다는 뜻입니다. 커서가 한 줄 위에 있다고 알려 온 것입니다. 막대 가운데(x≈26)는 글 앞(24)과 맞아,
가로 자리(Shift+Home 이라 글 앞)는 맞았습니다.

티톡은 이 PC 에 없어 직접 재지 못했습니다. 대화 창 클래스가 `TfrmSendMessage` 인 것으로 보아([missed-apps-1.17.1.md](missed-apps-1.17.1.md)) Delphi 로 만든 앱이고,
입력칸은 Windows RichEdit 을 감싼 Delphi 컨트롤일 가능성이 큽니다.

## 같은 계열 입력칸에서 잰 것

같은 RichEdit 을 쓰는 카카오톡 대화 입력칸(`RICHEDIT50W`, 배율 125%)에서 `GetGUIThreadInfo` 의 caret 사각형(입력칸 기준 좌표)과
`EM_GETSEL`·`EM_POSFROMCHAR` 를 50ms 마다 기록했습니다. 입력칸에는 `asdfsadfsadf`(x=0~66)를 넣었습니다.

| 상태 | caret 사각형 | 선택 | 선택 시작·끝 글자 위치 |
|---|---|---|---|
| 글 끝에 커서 | (66,0)-(67,19) | 12-12 | (66,0) |
| Ctrl+A | (65,0)-(73,19) | 0-12 | (0,0) · (66,0) |
| 글 끝에서 Shift+Home | (0,0)-(8,19) | 0-12 | (0,0) · (66,0) |

RichEdit 은 선택이 있으면 caret 을 **8px 넓이**로 바꾸고, 그 왼쪽 끝을 커서가 있는 쪽 끝(Ctrl+A 면 글 끝, Shift+Home 이면 글 앞)에 둡니다.
배지는 caret 의 가운데(밑줄·가운데 자리)나 오른쪽 끝(오른쪽 자리)에 붙으므로 4~8px 밀려났습니다. 이 PC 의 카카오톡에서는 줄은 맞았습니다.
직접 만든 `RICHEDIT50W` 창과 Windows 표준 Edit 에서도 같은 것을 확인했습니다(Edit 는 선택해도 caret 넓이가 1px 그대로).

| 확인한 것 | 결과 |
|---|---|
| RichEdit 에서 Ctrl+A | 보이지 않는 문단 끝 표시까지 골라 선택 끝이 13(글은 12자). 그 위치는 실제 커서보다 2px 오른쪽(90 대 88) |
| Edit 의 글 끝 위치 | `EM_POSFROMCHAR` 가 -1(답하지 않음) |
| 다른 프로세스에 묻기 | `EM_POSFROMCHAR` 는 wParam = 글자 번호, 반환값 = x·y 16비트씩이라 포인터 없이 32비트 카카오톡에도 맞게 답함 |

## 고친 내용

caret 을 찾는 1순위 경로(Win32 caret, `ImeReader`)에서, caret 을 가진 창이 표준 입력칸이면 `EditSelectionCaret.Adjust`
(`src/ImeBadge/Ime/EditSelectionCaret.cs`)가 다음을 합니다.

1. `EM_GETSEL` 로 선택 시작·끝을 받습니다(두 포인터로 받는 꼴. 반환값은 다른 프로세스에서 엉뚱하게 옵니다).
2. 선택이 없으면 caret 높이를 그 입력칸의 줄 높이로 기억하고 caret 을 그대로 씁니다.
3. 선택이 있으면 `EM_POSFROMCHAR` 로 시작·끝 글자의 위치를 묻습니다. Edit 처럼 글 끝 위치를 답하지 않으면 마지막으로 고른 글자를 묻습니다.
4. `SelectionCaret.Fix`(`src/ImeBadge.Core/SelectionCaret.cs`, 단위 테스트 `SelectionCaretTests`)가 커서를 정합니다.
   - 가로: caret 의 왼쪽 끝 그대로(커서가 있는 쪽 끝). 끝 글자 위치는 문단 끝 표시 때문에 2px 어긋날 수 있어 쓰지 않습니다.
   - 줄: 시작·끝 글자 중 caret 에 가까운 쪽의 줄.
   - 넓이 1px, 높이는 선택하기 전에 본 caret 높이(모르면 caret 높이).

묻는 입력칸은 Windows 표준 계열로 한정합니다: `RichEdit*`(카카오톡 `RICHEDIT50W`, 메모장 `RichEditD2DPT` 등), `Edit`,
WinForms(`….EDIT.…`), Delphi `TEdit`, 그리고 이름이 T 로 시작하고 `RichEdit` 이 들어 있거나 `Memo` 로 끝나는 Delphi 컨트롤
(`TRichEdit`·`TMemo`·`TcxRichEdit` 등). RichEdit 2.0 꼴로 `EM_POSFROMCHAR` 의 wParam 을 주소로 읽는 컨트롤에 글자 번호를 보내면
그 앱이 엉뚱한 메모리에 쓰기 때문에, 이름에 edit 가 든 아무 컨트롤에나 묻지 않습니다. `EM_POSFROMCHAR` 는 `EM_GETSEL` 이 선택이 있다고 답한
입력칸에만 보냅니다. 메시지는 모두 `SMTO_ABORTIFHUNG`, 50ms 로 보냅니다.

선택이 없을 때와 그 밖의 입력칸(UI Automation 경로의 Chrome·VS Code, 커서 창 경로의 Xshell)은 바뀌지 않습니다.

## 확인

- 카카오톡 "나와의 채팅"에서 개발 빌드: Ctrl+A, 글 끝에서 Shift+Home, 오른쪽에서 왼쪽으로 끌어 고르기 모두 밑줄 배지가 커서 바로 아래 가운데.
  로그 `caret:sel(1506,924,8x19 -> 1506,924,h=19)`, `caret:sel(1375,924,8x19 -> 1375,924,h=19)`.
- Windows 11 메모장(`RichEditD2DPT`) 두 줄에서 Ctrl+A·Shift+Home: 배지가 둘째 줄 끝·앞에 그대로. 로그 `caret:sel(761,741,8x18 -> 761,741,h=18)`.
- 신고 화면처럼 caret 이 한 줄 위로 오는 경우는 화면에서 잰 좌표로 단위 테스트(`CaretReportedOneLineUp_MovesBackToSelectedLine`)에 넣었습니다.

## 남은 것

- **티톡에서 직접 확인하지 못했습니다.** 1.17.3 에서도 어긋나면 티톡 입력칸이 위 표준 계열이 아닌 것입니다. `--debug` 로 실행해
  선택했을 때 로그의 `focus='…'` 클래스와 `caret:sel(...)` 유무를 보면 압니다. `caret:sel` 이 없으면 그 클래스를 표준 계열에 더할 수 있는지
  (Windows RichEdit·Edit 을 감쌌는지) 확인합니다.
- 선택한 채로 다른 입력칸에서 들어오면 그 입력칸의 줄 높이를 아직 모르므로 caret 높이를 그대로 씁니다. caret 높이까지 망가진 입력칸이면
  선택을 한 번 풀 때까지 배지 높이가 어긋날 수 있습니다.
