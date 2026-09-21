# 변경 이력

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 [SemVer](https://semver.org/lang/ko/)를 따릅니다.

## [Unreleased]

### 수정
- winget 매니페스트 생성이 비공개 저장소에서 SHA256SUMS.txt 를 못 받아 실패하던 문제. GitHub API + 토큰으로 받는다.
  (winget 등록과 프로그램의 업데이트 확인은 저장소가 공개여야 동작한다. docs/release.md 참고.)

## [1.0.1] - 2026-09-21

### 추가
- winget 매니페스트(`kimch0627.ImeBadge`)와 자동 제출 워크플로(`WINGET_TOKEN` 시크릿이 있을 때).

### 변경
- 앱·트레이 아이콘을 언어 중립 도형(텍스트 커서 + 배지)으로 교체. 다른 언어 IME 를 지원해도 그대로 쓴다.

### 수정
- 설정 창의 "모양"·"동작"·"배지를 띄우지 않을 앱" 구역이 폭 0 으로 접혀 제목이 세로로 찍히고 항목이 보이지 않던 문제.
  (자동 크기 그룹박스 안에서 Dock 을 쓰지 않도록 레이아웃 변경.) 정보 창도 같은 방식으로 정리.
- 롤링 사전 릴리스(`latest`, `dev-*`)에 설치 프로그램이 버전 이름으로 쌓이던 문제. 고정 이름 `ImeBadge-Setup.exe` 로 덮어쓴다.

## [1.0.0] - 2026-09-21

첫 정식 릴리스. 설치 프로그램과 설정 창이 추가되고, 설정·로그 위치가 사용자 프로필 폴더로 옮겨졌습니다.
0.7.x 에서 올라오면 예전 설정은 첫 실행 때 자동으로 이관됩니다.

### 추가
- 설치 프로그램(`ImeBadge-Setup-<버전>.exe`): 사용자별 설치, 자동 시작 옵션, 제거 시 정리.
- 설정 창(트레이 아이콘 더블클릭 또는 메뉴 → 설정): 미리보기, 색상, 동작, 제외 앱.
- 트레이 메뉴: 일시 중지(Ctrl+Alt+H), 로그인 시 자동 시작, 업데이트 확인, 정보.
- 한글/영문 배지 색 변경.
- 전체 화면 앱(게임·동영상)에서 배지 자동 숨김.
- 배지를 띄우지 않을 앱 목록(프로세스 이름, `*` 접두어 일치).
- 하루 한 번 GitHub Releases 에서 새 버전 확인 후 알림(끌 수 있음).
- 첫 실행 안내 풍선 알림.
- 앱 아이콘(exe·트레이). 일시 중지 중에는 회색 아이콘.
- 오류 로그(`errors.log`)를 항상 기록. 잡히지 않은 예외로 프로그램이 조용히 사라지지 않음.
- 중복 실행 방지. 두 번째 실행은 기존 인스턴스의 설정 창을 연다.
- 버전 번호를 CI 태그에서 exe 에 새김. 릴리스에 SHA256SUMS.txt 첨부.
- 코드 서명 단계(Azure Trusted Signing 시크릿이 있을 때만).
- 단위 테스트(`tests/ImeBadge.Core.Tests`), Linux CI 에서 실행.

### 변경
- 설정 파일 위치: exe 옆 `imebadge.settings.json` → `%APPDATA%\ImeBadge\settings.json`. 예전 파일은 첫 실행 때 자동으로 옮겨 온다.
- 로그 위치: exe 옆 → `%LOCALAPPDATA%\ImeBadge\logs\`. 1 MB 를 넘으면 `.1` 로 밀어 둔다. 0.7.x 의 exe 옆 `imebadge-crash.log` 는 `errors.log` 로 대체.
- 소스를 `src/ImeBadge`(앱)와 `src/ImeBadge.Core`(순수 로직)로 나눔.
- 배지가 한동안 숨겨져 있으면 폴링 간격을 늘리고, 화면 잠금 중에는 폴링을 멈춘다(CPU·배터리).
- 포커스 변경 이벤트(EVENT_OBJECT_FOCUS)에도 즉시 갱신.

### 수정
- 응답 없는 창에는 `AttachThreadInput` 을 시도하지 않고, 같은 창에 3회 실패하면 더 시도하지 않는다(프리징 방지).
- 타이머와 이벤트 훅이 동시에 들어와도 갱신이 겹치지 않는다.

## [0.7.2] 이전

`git log` 를 참고하세요. (배지 렌더러, 트레이 메뉴, 트리밍된 self-contained exe, GitHub Actions 빌드, WinForms 어셈블리 통째 보존)

[Unreleased]: https://github.com/kimch0627/windows-ime-badge/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/kimch0627/windows-ime-badge/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/kimch0627/windows-ime-badge/compare/v0.7.2...v1.0.0
