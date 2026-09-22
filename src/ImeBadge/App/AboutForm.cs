using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>정보 대화상자: 큰 아이콘·이름·버전 칩, 한 줄 소개, 링크 목록, 진단 정보 복사.</summary>
sealed class AboutForm : Form
{
    readonly AppPaths _paths;
    readonly Settings _settings;

    public AboutForm(AppPaths paths, Settings settings, Action checkUpdates)
    {
        _paths = paths;
        _settings = settings;

        Text = Strings.Format("about.title", AppInfo.ProductName);
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = Theme.DialogFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        // AutoSize 폼 안에서는 Dock 을 쓰지 않는다(SettingsForm 의 설명 참고). 왼쪽 위에 두면 폼이 내용 + Padding 만큼 자란다.
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(24, 24) };

        // 머리: 큰 아이콘 + 이름 + 버전 칩
        var header = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        var pic = new PictureBox { Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 16, 0) };
        using (var big = new Icon(Icons.App, 128, 128)) pic.Image = big.ToBitmap();   // 큰 프레임을 줄여 그려 고DPI 에서도 또렷하게
        var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        texts.Controls.Add(new Label { Text = Strings.Get("app.name"), Font = Theme.TitleFont(Font), AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        texts.Controls.Add(new VersionChip { Text = $"v{AppVersion.Display}", Margin = Padding.Empty });
        header.Controls.Add(pic);
        header.Controls.Add(texts);
        root.Controls.Add(header);

        root.Controls.Add(new Label { Text = Strings.Get("app.tagline"), AutoSize = true, Margin = new Padding(0, 0, 0, 16) });

        // 링크 목록: 자주 쓰는 순서. 사이에 얇은 구분선.
        root.Controls.Add(Link(Strings.Get("about.checkUpdates"), checkUpdates));
        root.Controls.Add(Link(Strings.Get("about.releases"), () => Open(AppInfo.ReleasesUrl)));
        root.Controls.Add(Link(Strings.Get("about.repo"), () => Open(AppInfo.RepoUrl)));
        root.Controls.Add(Divider());
        root.Controls.Add(Link(Strings.Get("about.settingsDir"), () => OpenFolder(paths.SettingsDir)));
        root.Controls.Add(Link(Strings.Get("about.logDir"), () => OpenFolder(paths.LogDir)));

        // 진단 정보: 이슈에 붙여 넣으면 재현 환경을 바로 알 수 있다.
        string copyText = Strings.Get("about.copyDiag");
        LinkLabel diag = null!;
        diag = Link(copyText, () =>
        {
            try
            {
                Clipboard.SetText(Diagnostics());
                diag.Text = Strings.Format("about.copied", copyText);
                var t = new System.Windows.Forms.Timer { Interval = 2500 };
                t.Tick += (_, _) => { t.Dispose(); if (!diag.IsDisposed) diag.Text = copyText; };
                t.Start();
            }
            catch (Exception ex) { Log.Error("clipboard failed", ex); }
        });
        root.Controls.Add(diag);

        root.Controls.Add(new Label { Text = Strings.Get("about.license"), ForeColor = SystemColors.GrayText, AutoSize = true, Margin = new Padding(0, 16, 0, 12) });

        var ok = new AccentButton { Text = Strings.Get("about.close"), DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right, Primary = true };
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
        sb.AppendLine(Strings.Format("diag.dpi", DeviceDpi, DeviceDpi * 100 / 96, Screen.AllScreens.Length));
        sb.AppendLine(Strings.Format("diag.theme",
            Strings.Get(Theme.IsDark ? "diag.dark" : "diag.light"),
            SystemInformation.HighContrast ? Strings.Get("diag.highContrast") : "",
            Strings.Get(Native.AnimationsEnabled() ? "diag.on" : "diag.off"),
            $"{Strings.Code} ({_settings.Language})"));
        sb.AppendLine(Strings.Format("diag.settings", _settings.Style, _settings.Placement, _settings.SizePercent, _settings.OpacityPercent,
            _settings.PollIntervalMs, _settings.HotkeyEnabled ? _settings.Hotkey : Strings.Get("diag.off"), _settings.ExcludedProcesses.Count));
        sb.AppendLine(Strings.Format("diag.exe", Shorten(Environment.ProcessPath)));
        sb.AppendLine(Strings.Format("diag.settingsFile", Shorten(_paths.SettingsFile)));
        sb.AppendLine(Strings.Format("diag.logDir", Shorten(_paths.LogDir)));
        return sb.ToString();
    }

    static string Shorten(string? path)
    {
        if (string.IsNullOrEmpty(path)) return Strings.Get("diag.unknown");
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
        var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 3, 0, 3), LinkBehavior = LinkBehavior.HoverUnderline };
        l.LinkClicked += (_, _) => onClick();
        return l;
    }

    /// <summary>얇은 구분선. 테마 적용 단계에서 배경색으로 덮이지 않도록 직접 색을 정한다.</summary>
    static Control Divider() => new Panel { Height = 1, Width = 260, Margin = new Padding(0, 6, 0, 6), BackColor = Theme.Current.Border, Tag = "custom-paint" };

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

/// <summary>버전처럼 짧은 정보를 담는 둥근 칩. 강조색을 옅게 깐 배경에 강조색 글자.</summary>
sealed class VersionChip : ThemedControl
{
    public VersionChip() { TabStop = false; AutoSize = true; }

    public override Size GetPreferredSize(Size proposed)
    {
        var ts = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(ts.Width + Px(16), ts.Height + Px(6));
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Size = GetPreferredSize(Size.Empty); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Size = GetPreferredSize(Size.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        using var path = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f);
        using var fill = new SolidBrush(Alpha(p.Accent, p.Dark ? 40 : 28));
        using var pen = new Pen(Alpha(p.Accent, 90), 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), p.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
