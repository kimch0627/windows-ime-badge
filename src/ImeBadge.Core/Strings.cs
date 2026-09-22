using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImeBadge;

/// <summary>UI 언어. 설정 파일에는 이름 문자열("Auto")로 저장된다.</summary>
public enum UiLanguage { Auto, Korean, English }

/// <summary>
/// UI 문구 테이블(한국어·영어). resx 대신 코드 안의 사전을 쓴다. 트리밍·단일 파일 exe 에서 위성 어셈블리(satellite assembly)를
/// 신경 쓸 필요가 없고, 두 언어의 키가 같은지 단위 테스트로 검사할 수 있기 때문이다.
/// 비유하면 두 권의 사전을 나란히 놓고 같은 표제어를 찾는 것이다. 표제어가 한쪽에만 있으면 테스트가 잡는다.
/// 니모닉(&amp;)은 문구 안에 들어 있으므로 언어마다 겹치지 않게 골라야 한다.
/// </summary>
public static class Strings
{
    static UiLanguage _setting = UiLanguage.Auto;
    static bool _korean = DetectKorean();

    /// <summary>설정값. Auto 면 Windows 의 표시 언어(CurrentUICulture)가 한국어인지로 정한다.</summary>
    public static UiLanguage Setting
    {
        get => _setting;
        set
        {
            _setting = value;
            _korean = value switch { UiLanguage.Korean => true, UiLanguage.English => false, _ => DetectKorean() };
        }
    }

    public static bool IsKorean => _korean;

    /// <summary>현재 언어의 두 글자 코드("ko"/"en"). 진단 정보용.</summary>
    public static string Code => _korean ? "ko" : "en";

    static bool DetectKorean()
    {
        try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>키에 해당하는 문구. 없는 키는 키 이름 그대로 돌려주어 눈에 띄게 한다(테스트가 먼저 잡는다).</summary>
    public static string Get(string key) =>
        (_korean ? Ko : En).TryGetValue(key, out var s) ? s : (_korean ? En : Ko).TryGetValue(key, out var t) ? t : key;

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);

    /// <summary>테스트용: 한 언어의 전체 키·문구.</summary>
    public static IReadOnlyDictionary<string, string> Table(UiLanguage lang) => lang == UiLanguage.Korean ? Ko : En;

    static readonly Dictionary<string, string> Ko = new()
    {
        // ── 앱 ──
        ["app.name"] = "ImeBadge (한/영 배지)",
        ["app.tagline"] = "글자를 입력하기 전에 지금 한글인지 영문인지 알 수 있도록\n텍스트 커서 옆에 작은 배지를 띄워 줍니다.",
        ["app.firstRun"] = "커서 옆에 한/영 배지가 표시됩니다. 트레이 아이콘을 우클릭하면 모양과 동작을 바꿀 수 있습니다.",

        // ── 상태 ──
        ["state.paused"] = "일시 중지",
        ["state.hangul"] = "한글 입력",
        ["state.english"] = "영문 입력",
        ["state.englishCaps"] = "영문 입력 (Caps Lock)",
        ["state.other"] = "다른 언어 입력",
        ["state.none"] = "입력 위치 없음",
        ["tray.resumeWith"] = " · {0} 로 재개",
        ["tray.pauseWith"] = " · {0} 일시 중지",

        // ── 트레이 메뉴 ──
        ["menu.current"] = "현재: {0}",
        ["menu.pause"] = "일시 중지(&P)",
        ["menu.settings"] = "설정(&S)...",
        ["menu.style"] = "모양(&M)",
        ["menu.placement"] = "위치(&L)",
        ["menu.size"] = "크기(&Z)",
        ["menu.opacity"] = "불투명도(&O)",
        ["menu.autostart"] = "로그인 시 자동 시작(&A)",
        ["menu.checkUpdates"] = "업데이트 확인(&U)",
        ["menu.about"] = "정보(&I)...",
        ["menu.exit"] = "종료(&X)",
        ["menu.custom"] = "사용자 지정 ({0}%)",

        // ── 프리셋 ──
        ["style.box"] = "사각 배지  [한]",
        ["style.pill"] = "둥근 배지  (한)",
        ["style.dot"] = "점  ●",
        ["style.underline"] = "밑줄  ▬",
        ["style.dotFlash"] = "점, 바뀔 때 1.5초 글자",
        ["place.aboveRight"] = "커서 오른쪽 위",
        ["place.belowRight"] = "커서 오른쪽 아래",
        ["place.aboveLeft"] = "커서 왼쪽 위",
        ["place.belowLeft"] = "커서 왼쪽 아래",
        ["size.small"] = "작게 (80%)",
        ["size.normal"] = "보통 (100%)",
        ["size.large"] = "크게 (130%)",
        ["size.xlarge"] = "아주 크게 (160%)",
        ["opacity.100"] = "불투명 (100%)",
        ["opacity.85"] = "살짝 비침 (85%)",
        ["opacity.70"] = "반투명 (70%)",
        ["opacity.50"] = "많이 비침 (50%)",
        ["language.auto"] = "시스템 언어 (자동)",
        ["language.ko"] = "한국어",
        ["language.en"] = "English",

        // ── 설정 창 ──
        ["settings.title"] = "{0} 설정",
        ["settings.ok"] = "확인",
        ["settings.cancel"] = "취소",
        ["settings.reset"] = "기본값 복원(&R)",
        ["group.look"] = "모양",
        ["group.behavior"] = "동작",
        ["group.preview"] = "미리보기",
        ["group.exclude"] = "배지를 띄우지 않을 앱",
        ["look.style"] = "배지 모양(&M)",
        ["look.placement"] = "위치(&L)",
        ["look.placement.tip"] = "위쪽은 다음 줄 글자를 덜 가립니다. 화면 가장자리라 자리가 없으면 반대쪽으로 옮깁니다.",
        ["look.size"] = "크기(&Z)",
        ["look.size.tip"] = "모니터 DPI 배율에 추가로 곱해집니다.",
        ["look.opacity"] = "불투명도(&O)",
        ["look.opacity.tip"] = "배지 배경만 비치고 글자는 또렷하게 유지됩니다.",
        ["look.hangulColor"] = "한글 배지 색(&H)",
        ["look.englishColor"] = "영문 배지 색(&E)",
        ["look.color.tip"] = "글자색(흰/검)은 고른 색의 밝기에 맞춰 자동으로 정해집니다.",
        ["look.animate"] = "나타날 때 서서히, 바뀔 때 살짝 커지는 효과(&N)",
        ["look.animate.tip"] = "Windows 설정 → 접근성 → 시각 효과 → 애니메이션 효과가 꺼져 있으면 여기와 상관없이 생략합니다.",
        ["look.capsLock"] = "영문일 때 Caps Lock 표시  (A → ABC)(&C)",
        ["look.capsLock.tip"] = "Caps Lock 이 켜져 있으면 배지 글자가 ABC 로, 트레이 아이콘은 A 아래 줄로 바뀝니다. 점·밑줄 모양에서는 차이가 없습니다.",
        ["behavior.autostart"] = "Windows 로그인 시 자동 시작(&A)",
        ["behavior.fullscreen"] = "전체 화면 앱(게임·동영상)에서는 숨김(&F)",
        ["behavior.trayState"] = "트레이 아이콘에도 한/영 상태 표시(&T)",
        ["behavior.hotkey"] = "단축키로 일시 중지 켜기/끄기(&K)",
        ["behavior.hotkey.tip"] = "여기를 클릭한 뒤 원하는 키 조합을 누르세요. Ctrl, Alt, Win 중 하나가 들어가야 합니다.",
        ["behavior.hotkey.placeholder"] = "키 조합을 누르세요",
        ["behavior.updates"] = "새 버전이 나오면 알림 (하루 한 번 확인)(&U)",
        ["behavior.updates.tip"] = "GitHub Releases 에서 정식 버전만 확인합니다. 끄면 네트워크 접속이 전혀 없습니다.",
        ["behavior.poll"] = "확인 주기 (ms)(&I)",
        ["behavior.poll.note"] = "작을수록 빨리 반응하고 CPU 를 조금 더 씁니다. 기본 100.",
        ["behavior.poll.tip"] = "한/영 상태를 다시 읽는 간격입니다. 배지가 한동안 안 보이면 자동으로 3배 느려집니다.",
        ["behavior.language"] = "언어(&G)",
        ["behavior.language.tip"] = "설정 창·트레이 메뉴·알림의 언어입니다. 배지 글자(한/A)는 바뀌지 않습니다.",
        ["preview.tip"] = "왼쪽은 밝은 배경(메모장), 오른쪽은 어두운 배경(VS Code 등)에서의 모습입니다.\n배지가 옆줄 글자를 얼마나 가리는지, 불투명도를 낮추면 얼마나 비치는지도 볼 수 있습니다.",
        ["exclude.new.tip"] = "실행 중인 앱을 고르거나 실행 파일 이름을 직접 입력하세요. 끝에 * 를 붙이면 앞부분만 맞으면 됩니다.",
        ["exclude.add"] = "추가(&D)",
        ["exclude.remove"] = "삭제(&X)",
        ["exclude.note"] = "실행 파일 이름(.exe 생략 가능). 끝에 * 를 붙이면 앞부분만 맞으면 됩니다.\n예)  mstsc  /  vmware-vmx  /  Unreal*",
        ["group.corner"] = "커서를 못 찾는 앱 (창 모서리에 표시)",
        ["corner.add"] = "추가",
        ["corner.remove"] = "삭제",
        ["corner.note"] = "자체 커서를 그리는 앱(Xshell 같은 터미널)은 커서 위치를 알 수 없을 때가 있습니다. 이 목록의 앱은\n그럴 때 배지를 입력 창의 왼쪽 아래 모서리에 고정해 띄웁니다(위치 설정 무시). 예)  Xshell*",

        // ── 정보 창 ──
        ["about.title"] = "{0} 정보",
        ["about.version"] = "버전 {0}",
        ["about.checkUpdates"] = "새 버전 확인",
        ["about.repo"] = "GitHub 저장소 열기",
        ["about.releases"] = "릴리스(다운로드) 페이지 열기",
        ["about.settingsDir"] = "설정 폴더 열기",
        ["about.logDir"] = "로그 폴더 열기",
        ["about.copyDiag"] = "진단 정보 복사 (이슈 신고용)",
        ["about.copied"] = "{0} — 복사했습니다",
        ["about.close"] = "닫기",
        ["diag.dpi"] = "DPI: {0} ({1}%), 모니터 {2}대",
        ["diag.theme"] = "테마: {0}{1}, 애니메이션 효과: {2}, UI 언어: {3}",
        ["diag.dark"] = "어둡게",
        ["diag.light"] = "밝게",
        ["diag.highContrast"] = ", 고대비",
        ["diag.on"] = "켜짐",
        ["diag.off"] = "꺼짐",
        ["diag.settings"] = "설정: 모양={0}, 위치={1}, 크기={2}%, 불투명도={3}%, 주기={4}ms, 단축키={5}, 제외 앱={6}개",
        ["diag.exe"] = "실행 파일: {0}",
        ["diag.settingsFile"] = "설정 파일: {0}",
        ["diag.logDir"] = "로그 폴더: {0}",
        ["diag.unknown"] = "(알 수 없음)",

        // ── 대화상자 ──
        ["dialog.details"] = "자세히",
        ["dialog.brief"] = "간단히",
        ["dialog.openLogDir"] = "로그 폴더 열기",
        ["dialog.close"] = "닫기",
        ["dialog.yesPrefix"] = "[예] {0}",
        ["crash.heading"] = "{0}에 문제가 생겨 종료합니다.",
        ["crash.text"] = "자세한 내용은 로그 폴더의 errors.log 에 기록되어 있습니다. 문제가 반복되면 이 파일과 함께 이슈로 알려 주세요.",
        ["hotkey.failed"] = "단축키 {0} 을(를) 등록하지 못했습니다.",
        ["hotkey.failed.text"] = "다른 프로그램이 같은 조합을 쓰고 있을 수 있습니다. 설정에서 다른 조합을 고르세요.",
        ["autostart.failed"] = "자동 시작 설정을 바꾸지 못했습니다.",
        ["autostart.failed.text"] = "로그 폴더의 errors.log 에 원인이 기록되어 있습니다. (트레이 메뉴 → 정보 → 로그 폴더 열기)",

        // ── 업데이트 ──
        ["update.balloon.title"] = "새 버전이 있습니다",
        ["update.balloon.text"] = "{0} {1} 을(를) 받을 수 있습니다. (현재 {2})\n클릭하면 다운로드 페이지가 열립니다.",
        ["update.none"] = "아직 정식 릴리스가 없습니다.",
        ["update.latest"] = "최신 버전을 쓰고 있습니다.",
        ["update.current"] = "현재 {0}",
        ["update.currentLatest"] = "현재 {0}, 최신 {1}",
        ["update.failed"] = "업데이트 정보를 가져오지 못했습니다.",
        ["update.failed.text"] = "네트워크 연결을 확인한 뒤 다시 시도하세요.",
        ["update.offer.heading"] = "새 버전 {0} 이(가) 있습니다.",
        ["update.offer.text"] = "현재 {0} 을(를) 쓰고 있습니다.",
        ["update.open"] = "다운로드 페이지 열기",
        ["update.open.note"] = "브라우저에서 릴리스 페이지를 엽니다.",
        ["update.later"] = "나중에",
        ["update.later.note"] = "다음에 다시 알립니다.",
        ["update.skip"] = "이 버전 건너뛰기",
        ["update.skip.note"] = "{0} 은(는) 자동으로 알리지 않습니다. 더 새 버전이 나오면 다시 알립니다.",
    };

    static readonly Dictionary<string, string> En = new()
    {
        // ── App ──
        ["app.name"] = "ImeBadge (Korean/English badge)",
        ["app.tagline"] = "Shows a small badge next to the text caret so you know\nwhether you're typing Korean or English before you type.",
        ["app.firstRun"] = "A Korean/English badge now appears next to the caret. Right-click the tray icon to change its look and behavior.",

        // ── State ──
        ["state.paused"] = "Paused",
        ["state.hangul"] = "Korean",
        ["state.english"] = "English",
        ["state.englishCaps"] = "English (Caps Lock)",
        ["state.other"] = "Other language",
        ["state.none"] = "No text field",
        ["tray.resumeWith"] = " · {0} to resume",
        ["tray.pauseWith"] = " · {0} to pause",

        // ── Tray menu (mnemonics: P S H L Z O W U A X) ──
        ["menu.current"] = "Now: {0}",
        ["menu.pause"] = "&Pause",
        ["menu.settings"] = "&Settings...",
        ["menu.style"] = "S&hape",
        ["menu.placement"] = "&Location",
        ["menu.size"] = "Si&ze",
        ["menu.opacity"] = "&Opacity",
        ["menu.autostart"] = "Start with &Windows",
        ["menu.checkUpdates"] = "Check for &updates",
        ["menu.about"] = "&About...",
        ["menu.exit"] = "E&xit",
        ["menu.custom"] = "Custom ({0}%)",

        // ── Presets ──
        ["style.box"] = "Square badge  [한]",
        ["style.pill"] = "Rounded badge  (한)",
        ["style.dot"] = "Dot  ●",
        ["style.underline"] = "Underline  ▬",
        ["style.dotFlash"] = "Dot, text for 1.5 s on change",
        ["place.aboveRight"] = "Above right of caret",
        ["place.belowRight"] = "Below right of caret",
        ["place.aboveLeft"] = "Above left of caret",
        ["place.belowLeft"] = "Below left of caret",
        ["size.small"] = "Small (80%)",
        ["size.normal"] = "Normal (100%)",
        ["size.large"] = "Large (130%)",
        ["size.xlarge"] = "Extra large (160%)",
        ["opacity.100"] = "Opaque (100%)",
        ["opacity.85"] = "Slightly see-through (85%)",
        ["opacity.70"] = "Translucent (70%)",
        ["opacity.50"] = "Mostly see-through (50%)",
        ["language.auto"] = "System language (auto)",
        ["language.ko"] = "한국어",
        ["language.en"] = "English",

        // ── Settings window (mnemonics: S L Z O K E N C W F T H U I G R D M) ──
        ["settings.title"] = "{0} Settings",
        ["settings.ok"] = "OK",
        ["settings.cancel"] = "Cancel",
        ["settings.reset"] = "&Reset to defaults",
        ["group.look"] = "Appearance",
        ["group.behavior"] = "Behavior",
        ["group.preview"] = "Preview",
        ["group.exclude"] = "Apps without the badge",
        ["look.style"] = "Badge &shape",
        ["look.placement"] = "&Location",
        ["look.placement.tip"] = "Above hides less of the next line. If there is no room at the screen edge, the badge moves to the other side.",
        ["look.size"] = "Si&ze",
        ["look.size.tip"] = "Multiplied on top of the monitor's DPI scaling.",
        ["look.opacity"] = "&Opacity",
        ["look.opacity.tip"] = "Only the badge background becomes see-through; the text stays crisp.",
        ["look.hangulColor"] = "&Korean badge color",
        ["look.englishColor"] = "&English badge color",
        ["look.color.tip"] = "Text color (white/black) is chosen automatically from the brightness of the color you pick.",
        ["look.animate"] = "Fade in when shown, pulse when the mode cha&nges",
        ["look.animate.tip"] = "Skipped regardless of this setting when Windows Settings → Accessibility → Visual effects → Animation effects is off.",
        ["look.capsLock"] = "Show &Caps Lock in English mode  (A → ABC)",
        ["look.capsLock.tip"] = "With Caps Lock on, the badge reads ABC and the tray icon shows A with a bar under it. Dot and underline shapes look the same either way.",
        ["behavior.autostart"] = "Start with &Windows",
        ["behavior.fullscreen"] = "Hide in &fullscreen apps (games, videos)",
        ["behavior.trayState"] = "Show the mode on the &tray icon too",
        ["behavior.hotkey"] = "&Hotkey to pause/resume",
        ["behavior.hotkey.tip"] = "Click here, then press the key combination you want. It must include Ctrl, Alt or Win.",
        ["behavior.hotkey.placeholder"] = "Press a key combination",
        ["behavior.updates"] = "&Update notifications (checks once a day)",
        ["behavior.updates.tip"] = "Only stable releases on GitHub Releases are checked. When off, the app never touches the network.",
        ["behavior.poll"] = "Poll &interval (ms)",
        ["behavior.poll.note"] = "Lower reacts faster and uses a bit more CPU. Default 100.",
        ["behavior.poll.tip"] = "How often the mode is re-read. Slows down 3× automatically while the badge has been hidden for a while.",
        ["behavior.language"] = "Lan&guage",
        ["behavior.language.tip"] = "Language of the settings window, tray menu and notifications. The badge text (한/A) does not change.",
        ["preview.tip"] = "Left: on a light background (Notepad). Right: on a dark background (VS Code etc.).\nIt also shows how much of the neighboring line the badge covers, and how much shows through at lower opacity.",
        ["exclude.new.tip"] = "Pick a running app or type an executable name. A trailing * matches by prefix.",
        ["exclude.add"] = "A&dd",
        ["exclude.remove"] = "Re&move",
        ["exclude.note"] = "Executable name (.exe optional). A trailing * matches by prefix.\ne.g.  mstsc  /  vmware-vmx  /  Unreal*",
        ["group.corner"] = "Apps without a findable caret (corner badge)",
        ["corner.add"] = "Add",
        ["corner.remove"] = "Remove",
        ["corner.note"] = "Apps that draw their own cursor (terminals such as Xshell) may expose no caret position. For apps in this list\nthe badge is then pinned to the bottom-left corner of the focused window (placement setting ignored). e.g.  Xshell*",

        // ── About window ──
        ["about.title"] = "About {0}",
        ["about.version"] = "Version {0}",
        ["about.checkUpdates"] = "Check for updates",
        ["about.repo"] = "Open GitHub repository",
        ["about.releases"] = "Open releases (download) page",
        ["about.settingsDir"] = "Open settings folder",
        ["about.logDir"] = "Open log folder",
        ["about.copyDiag"] = "Copy diagnostics (for bug reports)",
        ["about.copied"] = "{0} — copied",
        ["about.close"] = "Close",
        ["diag.dpi"] = "DPI: {0} ({1}%), {2} monitor(s)",
        ["diag.theme"] = "Theme: {0}{1}, animation effects: {2}, UI language: {3}",
        ["diag.dark"] = "dark",
        ["diag.light"] = "light",
        ["diag.highContrast"] = ", high contrast",
        ["diag.on"] = "on",
        ["diag.off"] = "off",
        ["diag.settings"] = "Settings: shape={0}, placement={1}, size={2}%, opacity={3}%, interval={4}ms, hotkey={5}, excluded apps={6}",
        ["diag.exe"] = "Executable: {0}",
        ["diag.settingsFile"] = "Settings file: {0}",
        ["diag.logDir"] = "Log folder: {0}",
        ["diag.unknown"] = "(unknown)",

        // ── Dialogs ──
        ["dialog.details"] = "Details",
        ["dialog.brief"] = "Less",
        ["dialog.openLogDir"] = "Open log folder",
        ["dialog.close"] = "Close",
        ["dialog.yesPrefix"] = "[Yes] {0}",
        ["crash.heading"] = "{0} ran into a problem and has to close.",
        ["crash.text"] = "Details are in errors.log in the log folder. If this keeps happening, please open an issue and attach that file.",
        ["hotkey.failed"] = "Could not register the hotkey {0}.",
        ["hotkey.failed.text"] = "Another program may be using the same combination. Pick a different one in Settings.",
        ["autostart.failed"] = "Could not change the startup setting.",
        ["autostart.failed.text"] = "The cause is recorded in errors.log in the log folder (tray menu → About → Open log folder).",

        // ── Updates ──
        ["update.balloon.title"] = "New version available",
        ["update.balloon.text"] = "{0} {1} is available (you have {2}).\nClick to open the download page.",
        ["update.none"] = "There is no stable release yet.",
        ["update.latest"] = "You're on the latest version.",
        ["update.current"] = "Current: {0}",
        ["update.currentLatest"] = "Current {0}, latest {1}",
        ["update.failed"] = "Could not fetch update information.",
        ["update.failed.text"] = "Check your network connection and try again.",
        ["update.offer.heading"] = "Version {0} is available.",
        ["update.offer.text"] = "You are using {0}.",
        ["update.open"] = "Open download page",
        ["update.open.note"] = "Opens the release page in your browser.",
        ["update.later"] = "Later",
        ["update.later.note"] = "Remind me next time.",
        ["update.skip"] = "Skip this version",
        ["update.skip.note"] = "{0} won't be announced again. You'll be notified when a newer version is out.",
    };
}
