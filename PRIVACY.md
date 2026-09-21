# 개인정보 처리 안내

ImeBadge 는 사용자의 개인정보를 **수집·전송하지 않습니다.**

## 프로그램이 읽는 것

- 활성 창의 텍스트 커서(caret) 위치와 키보드 IME 의 한/영 상태. 배지를 그릴 위치와 글자를 정하는 데만 쓰이고 저장하지 않습니다.
- 활성 창 프로세스의 실행 파일 이름. "배지를 띄우지 않을 앱" 목록과 비교하는 데만 씁니다.
- 입력하는 글자 자체는 읽지 않습니다. (키 입력 훅을 걸지 않습니다.)

## 프로그램이 저장하는 것 (모두 내 PC 안)

| 파일 | 내용 |
|---|---|
| `%APPDATA%\ImeBadge\settings.json` | 모양·색·동작 설정, 마지막 업데이트 확인 시각 |
| `%LOCALAPPDATA%\ImeBadge\logs\errors.log` | 오류 발생 시각과 예외 내용 |
| `%LOCALAPPDATA%\ImeBadge\logs\imebadge.log` | `--debug` 로 실행했을 때만. 활성 창 클래스 이름, caret 좌표, IME 원시 값 |

## 네트워크 접속

- **업데이트 확인** 한 가지뿐입니다. 하루 한 번 `https://api.github.com/repos/kimch0627/windows-ime-badge/releases/latest` 에 접속해 최신 버전 번호를 읽습니다. 보내는 것은 프로그램 이름과 버전이 든 User-Agent 헤더뿐입니다.
- 설정 창 → "새 버전이 나오면 알림" 을 끄면 접속하지 않습니다.
- 그 밖의 통계·원격 진단(telemetry)은 없습니다.

## 문의

https://github.com/kimch0627/windows-ime-badge/issues
