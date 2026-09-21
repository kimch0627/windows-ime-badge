using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>정보 대화상자: 버전, 폴더 열기, 저장소 링크.</summary>
sealed class AboutForm : Form
{
    public AboutForm(AppPaths paths)
    {
        Text = $"{AppInfo.ProductName} 정보";
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font(BadgeRenderer.FontFamily, 9f);
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        // AutoSize 폼 안에서는 Dock 을 쓰지 않는다(SettingsForm 의 설명 참고). 왼쪽 위에 두면 폼이 내용 + Padding 만큼 자란다.
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(16, 16) };
        root.Controls.Add(new Label { Text = AppInfo.DisplayName, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        root.Controls.Add(new Label { Text = $"버전 {AppVersion.Display}", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        root.Controls.Add(new Label
        {
            Text = "글자를 입력하기 전에 지금 한글인지 영문인지 알 수 있도록\n텍스트 커서 옆에 작은 배지를 띄워 줍니다.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        });
        root.Controls.Add(Link("GitHub 저장소 열기", () => Open(AppInfo.RepoUrl)));
        root.Controls.Add(Link("릴리스(다운로드) 페이지 열기", () => Open(AppInfo.ReleasesUrl)));
        root.Controls.Add(Link("설정 폴더 열기", () => OpenFolder(paths.SettingsDir)));
        root.Controls.Add(Link("로그 폴더 열기", () => OpenFolder(paths.LogDir)));
        root.Controls.Add(new Label { Text = "Apache License 2.0", ForeColor = SystemColors.GrayText, AutoSize = true, Margin = new Padding(0, 12, 0, 8) });

        var ok = new Button { Text = "닫기", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right };
        root.Controls.Add(ok);
        AcceptButton = ok; CancelButton = ok;
        Controls.Add(root);
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
