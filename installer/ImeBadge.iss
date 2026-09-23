; ImeBadge 설치 프로그램 (Inno Setup 6 스크립트)
;
; 빌드:  ISCC.exe /DMyAppVersion=1.2.3 /DMyArch=x64   /DMySourceExe=..\out\sc-x64\ImeBadge.exe   installer\ImeBadge.iss
;        ISCC.exe /DMyAppVersion=1.2.3 /DMyArch=arm64 /DMySourceExe=..\out\sc-arm64\ImeBadge.exe installer\ImeBadge.iss
; 결과:  installer\Output\ImeBadge-Setup-1.2.3-x64.exe, installer\Output\ImeBadge-Setup-1.2.3-arm64.exe
;
; 설계:
;  - 아키텍처(x64 / arm64)마다 설치 프로그램을 따로 만든다. 같은 AppId 라서 x64 → arm64 로 덮어 설치해도 한 항목으로 관리된다.
;    x64 설치 프로그램은 ARM64 Windows 에서도 돌아가지만(x64 에뮬레이션) 네이티브 arm64 판이 빠르고 배터리를 덜 쓴다.
;    arm64 설치 프로그램은 ARM64 Windows 에서만 실행된다.
;  - 사용자별 설치(관리자 권한 불필요). 설치 위치 {localappdata}\Programs\ImeBadge.
;    "모든 사용자용" 설치를 원하면 설치 시작 화면에서 고를 수 있다(PrivilegesRequiredOverridesAllowed).
;  - 실행 중이면 먼저 종료시키고 설치한다(AppMutex 는 프로그램의 SingleInstance 뮤텍스 이름과 같다).
;  - "로그인 시 자동 시작" 작업(task)은 프로그램이 쓰는 것과 같은 HKCU\...\Run 값을 만든다.
;  - 제거 시 Run 값은 지우고, 설정(%APPDATA%\ImeBadge)과 로그는 남긴다. (다시 설치하면 그대로 이어 쓴다.)

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
; VersionInfoVersion(exe 파일 속성의 버전)은 숫자 4자리("1.2.3.0")만 허용한다. "0.0.0-dev.abc" 같은 표시용 버전과 따로 받는다.
#ifndef MyAppFileVersion
  #define MyAppFileVersion "0.0.0.0"
#endif
; 대상 CPU 아키텍처: "x64"(기본) 또는 "arm64". 출력 파일 이름과 [Setup] 의 Architectures* 값이 이에 따라 정해진다.
#ifndef MyArch
  #define MyArch "x64"
#endif
#if MyArch != "x64" && MyArch != "arm64"
  #error MyArch must be "x64" or "arm64"
#endif
#ifndef MySourceExe
  #define MySourceExe "..\out\sc-" + MyArch + "\ImeBadge.exe"
#endif
#define MyAppName "ImeBadge"
#define MyAppPublisher "kimch0627"
#define MyAppURL "https://github.com/kimch0627/windows-ime-badge"
#define MyAppExeName "ImeBadge.exe"

[Setup]
AppId={{7E2C1D7A-9B1E-4E63-9A2B-5F1C0B7D3A21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppFileVersion}
VersionInfoProductTextVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}-{#MyArch}
SetupIconFile=..\src\ImeBadge\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
#if MyArch == "arm64"
; arm64 exe 는 ARM64 Windows 에서만 실행된다.
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
; x64compatible: x64 Windows 와, x64 에뮬레이션이 되는 ARM64 Windows 모두 허용. 32-bit Windows 는 거부.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0
; AppMutex 는 쓰지 않는다: 그러면 설치 시작부터 "먼저 종료하세요" 안내가 뜬다. 대신 [Code] 에서 직접 정상 종료시킨다.
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
; 한국어: 컴파일러에 딸린 Korean.isl(6.3+)이 있으면 그것을, 없으면 저장소에 넣어 둔 복사본을 쓴다.
#if FileExists(AddBackslash(CompilerPath) + "Languages\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#else
Name: "korean"; MessagesFile: "Languages\Korean.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Windows 로그인 시 자동 시작"; GroupDescription: "추가 작업:"
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; GroupDescription: "추가 작업:"; Flags: unchecked

[Files]
Source: "{#MySourceExe}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 프로그램의 Autostart 클래스와 같은 값. 제거 시 함께 지운다.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 지금 실행"; Flags: nowait postinstall skipifsilent

[Code]
// 실행 중인 프로그램을 끝낸다. 먼저 WM_CLOSE(트레이 아이콘을 스스로 정리할 기회)를 보내고, 1.5초 뒤에도 남아 있으면 강제 종료.
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;
