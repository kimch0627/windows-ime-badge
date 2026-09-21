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

## winget 등록 (선택)

정식 Release 가 생기면 `wingetcreate` 로 매니페스트를 만들어 microsoft/winget-pkgs 에 PR 을 냅니다.

```powershell
winget install wingetcreate
wingetcreate new https://github.com/kimch0627/windows-ime-badge/releases/download/v1.0.0/ImeBadge-Setup-1.0.0.exe
# PackageIdentifier: kimch0627.ImeBadge, InstallerType: inno
wingetcreate submit <생성된 매니페스트 폴더>
```

이후 버전은 `wingetcreate update kimch0627.ImeBadge --urls <새 URL> --version 1.0.1 --submit` 한 줄입니다.

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

아이콘을 다시 만들려면 `pip install pillow` 후 `python tools/make_icons.py` (Windows 에서는 Malgun Gothic 을 자동으로 찾습니다).
