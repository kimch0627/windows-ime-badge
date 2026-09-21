using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>정보 대화상자: 아이콘·버전, 링크(저장소·릴리스·업데이트 확인·폴더), 진단 정보 복사.</summary>
sealed class AboutForm : Form
{
    readonly AppPaths _paths;
    readonly Settings _settings;

    public AboutForm(AppPaths paths, Settings settings, Action checkUpdates)
    {
        _paths = paths;
        _settings = settings;

        Text = $"{AppInfo.ProductName} 정보";
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = Theme.DialogFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        // AutoSize 폼 안에서는 Dock 을 쓰지 않는다(SettingsForm 의 설명 참고). 왼쪽 위에 두면 폼이 내용 + Padding 만큼 자란다.
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(16, 16) };

        // 머리: 큰 아이콘 + 이름 + 버전
        var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 12) };
        var pic = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) };
        using (var big = new Icon(Icons.App, 128, 128)) pic.Image = big.ToBitmap();   // 큰 프레임을 줄여 그려 고DPI 에서도 또렷하게
        header.Controls.Add(pic, 0, 0);
        header.SetRowSpan(pic, 2);
        header.Controls.Add(new Label { Text = AppInfo.DisplayName, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 4, 0, 2) }, 1, 0);
        header.Controls.Add(new Label { Text = $"버전 {AppVersion.Display}", AutoSize = true, Margin = new Padding(0, 0, 0, 0) }, 1, 1);
        root.Controls.Add(header);

        root.Controls.Add(new Label
        {
            Text = "글자를 입력하기 전에 지금 한글인지 영문인지 알 수 있도록\n텍스트 커서 옆에 작은 배지를 띄워 줍니다.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        });
        root.Controls.Add(Link("새 버전 확인", checkUpdates));
        root.Controls.Add(Link("GitHub 저장소 열기", () => Open(AppInfo.RepoUrl)));
        root.Controls.Add(Link("릴리스(다운로드) 페이지 열기", () => Open(AppInfo.ReleasesUrl)));
        root.Controls.Add(Link("설정 폴더 열기", () => OpenFolder(paths.SettingsDir)));
        root.Controls.Add(Link("로그 폴더 열기", () => OpenFolder(paths.LogDir)));

        // 진단 정보: 이슈에 붙여 넣으면 재현 환경을 바로 알 수 있다.
        LinkLabel diag = null!;
        diag = Link("진단 정보 복사 (이슈 신고용)", () =>
        {
            try
            {
                Clipboard.SetText(Diagnostics());
                diag.Text = "진단 정보 복사 (이슈 신고용) — 복사했습니다";
                var t = new System.Windows.Forms.Timer { Interval = 2500 };
                t.Tick += (_, _) => { t.Dispose(); if (!diag.IsDisposed) diag.Text = "진단 정보 복사 (이슈 신고용)"; };
                t.Start();
            }
            catch (Exception ex) { Log.Error("clipboard failed", ex); }
        });
        root.Controls.Add(diag);

        root.Controls.Add(new Label { Text = "Apache License 2.0", ForeColor = SystemColors.GrayText, AutoSize = true, Margin = new Padding(0, 12, 0, 8) });

        var ok = new Button { Text = "닫기", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right };
        ok.Click += (_, _) => Close();
        root.Controls.Add(ok);
        AcceptButton = ok; CancelButton = ok;
        Controls.Add(root);
        Theme.Apply(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    /// <summary>버그 신고에 필요한 환경 요약. 개인 정보(사용자 이름이 든 경로)는 %APPDATA% 식으로 줄인다.</summary>
    string Diagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.ProductName} {AppVersion.Display}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"DPI: {DeviceDpi} ({DeviceDpi * 100 / 96}%), 모니터 {Screen.AllScreens.Length}대");
        sb.AppendLine($"테마: {(Theme.IsDark ? "어둡게" : "밝게")}{(SystemInformation.HighContrast ? ", 고대비" : "")}, 애니메이션 효과: {(Native.AnimationsEnabled() ? "켜짐" : "꺼짐")}");
        sb.AppendLine($"설정: 모양={_settings.Style}, 위치={_settings.Placement}, 크기={_settings.SizePercent}%, 불투명도={_settings.OpacityPercent}%, " +
                      $"주기={_settings.PollIntervalMs}ms, 단축키={(_settings.HotkeyEnabled ? _settings.Hotkey : "끔")}, 제외 앱={_settings.ExcludedProcesses.Count}개");
        sb.AppendLine($"실행 파일: {Shorten(Environment.ProcessPath)}");
        sb.AppendLine($"설정 파일: {Shorten(_paths.SettingsFile)}");
        sb.AppendLine($"로그 폴더: {Shorten(_paths.LogDir)}");
        return sb.ToString();
    }

    static string Shorten(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "(알 수 없음)";
        foreach (var (name, folder) in new[]
        {
            ("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            ("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            ("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        })
            if (folder.Length > 0 && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return name + path[folder.Length..];
        return path;
    }

    static LinkLabel Link(string text, Action onClick)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        l.LinkClicked += (_, _) => onClick();
        return l;
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("open url failed", ex); }
    }

    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("open folder failed", ex); }
    }
}
