# 릴리스 절차와 배포 준비

## 정식 릴리스 만들기

```powershell
# 1. CHANGELOG.md 의 [Unreleased] 를 버전 제목으로 바꾸고 커밋
# 2. 태그를 밀면 CI 가 버전을 새겨 빌드하고 Release 를 만든다
git tag v1.0.0
git push origin v1.0.0
```

태그를 직접 푸시할 수 없는 환경이면 `main` 에 제목이 `release: v1.0.0 ...` 인 커밋을 푸시해도 됩니다.
워크플로가 그 커밋에 태그를 만들고 같은 실행에서 Release 까지 이어 갑니다.

CI(`.github/workflows/build.yml`)가 하는 일:

1. Linux 에서 단위 테스트와 서식 검사.
2. Windows 에서 전체 솔루션 빌드(경고 = 오류), 테스트.
3. exe 두 개 publish (`-p:Version=1.0.0` 으로 버전을 새김).
4. 서명 시크릿이 있으면 exe 서명.
5. Inno Setup 으로 설치 프로그램 생성, 있으면 서명.
6. `SHA256SUMS.txt` 와 함께 Release 에 첨부. 릴리스 노트는 PR 제목으로 자동 생성.

프로그램의 "업데이트 확인"은 이 정식 Release(`prerelease=false`)만 봅니다. 롤링 사전 릴리스(`latest`, `dev-*`)는 무시합니다.

## 코드 서명 (SmartScreen 경고 없애기)

서명이 없으면 처음 실행할 때 Windows SmartScreen 이 "알 수 없는 게시자" 경고를 띄웁니다.
비유하면 서명은 택배 상자의 봉인 스티커입니다. 누가 보냈고 중간에 뜯기지 않았음을 증명합니다.

개인 개발자에게 가장 저렴한 경로는 **Azure Trusted Signing** 입니다(월 과금, OV 인증서 발급 절차 대신 신원 검증).

1. Azure 구독 → Trusted Signing 계정 생성 → 신원 검증(개인은 여권/신분증) → 인증서 프로필 생성.
2. 앱 등록(서비스 주체)을 만들고 "Trusted Signing Certificate Profile Signer" 역할을 준다.
3. 저장소 **Settings → Secrets and variables → Actions** 에 다음 시크릿을 넣는다.

| 시크릿 | 값 |
|---|---|
| `AZURE_TENANT_ID` | 테넌트 ID |
| `AZURE_CLIENT_ID` | 앱 등록의 클라이언트 ID |
| `AZURE_CLIENT_SECRET` | 앱 등록의 비밀 |
| `SIGNING_ENDPOINT` | 예: `https://eus.codesigning.azure.net` |
| `SIGNING_ACCOUNT` | Trusted Signing 계정 이름 |
| `SIGNING_PROFILE` | 인증서 프로필 이름 |

여섯 개가 모두 있을 때만 서명 단계가 돌고, 없으면 건너뜁니다(워크플로 로그의 `code signing: false`).

SmartScreen 평판은 서명 후에도 다운로드 횟수가 쌓여야 경고가 사라집니다. EV 인증서는 즉시 평판을 받지만 비용이 큽니다.

## 저장소 공개 여부 (중요)

현재 저장소는 **비공개(private)** 입니다. 비공개 상태에서는 다음이 동작하지 않습니다.

| 기능 | 이유 |
|---|---|
| 프로그램의 "업데이트 확인" | 인증 없이 `api.github.com/.../releases/latest` 를 읽는데, 비공개 저장소는 404 를 돌려준다 |
| winget 등록 | winget-pkgs 검증기가 설치 프로그램 URL 을 인증 없이 내려받아 해시를 확인한다 |
| README 의 다운로드 링크 | 로그인하지 않은 사용자에게는 404 |

배포를 시작하려면 **Settings → General → Danger Zone → Change visibility → Public** 으로 바꿉니다.
공개 전에 시크릿·개인정보가 커밋 이력에 없는지 확인하세요(이 저장소는 코드·문서·아이콘만 있습니다).

## winget 등록

패키지 ID 는 `kimch0627.ImeBadge` 입니다. 등록되면 사용자는 `winget install kimch0627.ImeBadge` 한 줄로 설치합니다.

매니페스트는 `winget/templates/` 의 템플릿에서 만들어집니다(`tools/winget/New-WingetManifest.ps1`).
릴리스마다 `{{VERSION}}`, `{{SHA256}}`(릴리스의 SHA256SUMS.txt 에서), `{{DATE}}` 만 채워 넣습니다.
1.0.0 매니페스트는 `winget/manifests/k/kimch0627/ImeBadge/1.0.0/` 에 있고, winget 스키마 1.10.0 으로 검증했습니다.

### 자동 제출 (권장)

1. GitHub 에서 **개인 액세스 토큰(classic)** 을 만듭니다. 권한은 `public_repo` 하나면 됩니다.
   (wingetcreate 가 이 토큰으로 내 계정에 `winget-pkgs` 포크를 만들고 PR 을 엽니다.)
2. 저장소 **Settings → Secrets and variables → Actions** 에 `WINGET_TOKEN` 으로 넣습니다.
3. 이후 정식 릴리스가 만들어질 때마다 `build.yml` 의 `winget` 잡이 `winget.yml` 을 호출해 PR 을 냅니다.
   이미 만들어진 릴리스(예: 1.0.0)는 Actions 탭 → **winget** → Run workflow 에 버전을 넣어 수동으로 제출합니다.

토큰이 없으면 워크플로는 매니페스트만 만들고 `winget submit: false` 를 남기고 끝납니다.

### 수동 제출

```powershell
winget install wingetcreate
./tools/winget/New-WingetManifest.ps1 -Version 1.0.0          # winget/manifests/k/kimch0627/ImeBadge/1.0.0/
wingetcreate submit winget/manifests/k/kimch0627/ImeBadge/1.0.0  # 브라우저 로그인 또는 --token <PAT>
```

### 심사에서 확인하는 것

- 첫 등록은 사람이 검토합니다(보통 며칠). `Publisher`(kimch0627)와 `PackageName`(ImeBadge)이 exe 파일 속성의
  회사·제품 이름과 같아야 하며, csproj 의 `Company`/`Product` 가 그 값입니다.
- 설치 프로그램은 조용히(`/VERYSILENT`) 설치·제거되어야 합니다. Inno Setup 이 기본으로 지원하고, `Scope: user` +
  `/CURRENTUSER` 로 관리자 권한 없이 설치됩니다.
- `ProductCode` 는 Inno 의 `AppId` + `_is1` 입니다. 설치 프로그램의 `AppId` 를 바꾸면 매니페스트도 바꿔야 합니다.
- 미서명 설치 프로그램도 등록은 되지만, SmartScreen 경고는 그대로입니다.

## Microsoft Store (선택, 서명 비용 없음)

Store 에 올리면 Microsoft 가 패키지에 서명하므로 인증서가 필요 없지만, MSIX 패키징과 심사가 필요합니다.

- Partner Center 개발자 계정(개인 1회 등록비).
- MSIX 패키징: Windows Application Packaging Project 또는 `MakeAppx` + `Package.appxmanifest`. `RegisterHotKey`, `SetWinEventHook`, UI Automation 은 `runFullTrust` 기능이 필요합니다.
- 자동 시작은 Run 레지스트리 대신 `StartupTask` 확장으로 바꿔야 합니다.

현재 우선순위는 GitHub Releases + 설치 프로그램이며, Store 는 사용자 요청이 쌓이면 진행합니다.

## 로컬 빌드

```powershell
winget install Microsoft.DotNet.SDK.8
dotnet build ImeBadge.sln                 # 전체 (경고 = 오류)
dotnet test                               # Core 단위 테스트
dotnet run --project src/ImeBadge         # 실행 (버전은 0.0.0-dev, 자동 업데이트 확인 안 함)
dotnet run --project src/ImeBadge -- --debug

# 배포용 exe
dotnet publish src/ImeBadge -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=1.0.0

# 설치 프로그램 (Inno Setup 6 설치 후)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.0 /DMyAppFileVersion=1.0.0.0 "/DMySourceExe=..\src\ImeBadge\bin\Release\net8.0-windows\win-x64\publish\ImeBadge.exe" installer\ImeBadge.iss
```

아이콘을 다시 만들려면 `pip install pillow` 후 `python tools/make_icons.py`. 글꼴 없이 도형(텍스트 커서 + 배지)만 그리므로
어느 OS 에서 만들어도 같고, 특정 언어 글자가 없어 다른 언어 IME 를 지원하게 되어도 바꿀 필요가 없습니다.
