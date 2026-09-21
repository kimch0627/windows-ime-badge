using System;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 사용자에게 보여 주는 대화상자. Windows Vista 이후의 <see cref="TaskDialog"/>(제목 줄 + 본문 + 명령 링크 + "자세히" 펼침)를 쓰고,
/// 어떤 이유로든 실패하면(비 UI 스레드, 시각 스타일 없음) 예전 <see cref="MessageBox"/> 로 물러난다.
/// 비유하면 MessageBox 는 쪽지, TaskDialog 는 제목이 있는 편지다.
/// </summary>
static class Dialogs
{
    public static void Info(string heading, string? text = null) =>
        Show(heading, text, TaskDialogIcon.Information, MessageBoxIcon.Information);

    public static void Warning(string heading, string? text = null, string? details = null) =>
        Show(heading, text, TaskDialogIcon.Warning, MessageBoxIcon.Warning, details);

    /// <summary>
    /// 명령 링크(command link) 중 하나를 고르게 한다. 돌려주는 값은 눌린 항목의 인덱스, 닫거나 취소하면 -1.
    /// 각 항목은 (제목, 설명) 이며 설명은 작은 글씨로 제목 아래에 보인다.
    /// </summary>
    public static int Choose(string heading, string? text, TaskDialogIcon icon, params (string title, string? note)[] commands)
    {
        try
        {
            var page = NewPage(heading, text, icon);
            page.AllowCancel = true;
            var buttons = new TaskDialogButton[commands.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                buttons[i] = new TaskDialogCommandLinkButton(commands[i].title, commands[i].note);
                page.Buttons.Add(buttons[i]);
            }
            var result = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
            return Array.IndexOf(buttons, result);
        }
        catch (Exception ex)
        {
            Log.Error("TaskDialog failed; falling back to MessageBox", ex);
            // 첫 항목을 "예", 나머지를 "아니요"로 뭉뚱그린다. 드문 경로라 정확한 선택지보다 동작하는 쪽을 택한다.
            string body = text is null ? heading : heading + "\n\n" + text;
            body += $"\n\n[예] {commands[0].title}";
            return MessageBox.Show(body, AppInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes ? 0 : -1;
        }
    }

    /// <summary>
    /// 치명적 오류 안내. "로그 폴더 열기" 버튼을 누르면 폴더를 연 뒤 true 를 돌려준다.
    /// 예외 처리기 안에서도 불리므로 어떤 스레드에서든 안전해야 한다.
    /// </summary>
    public static bool Fatal(string heading, string text, string? details, string logDir)
    {
        try
        {
            var page = NewPage(heading, text, TaskDialogIcon.Error, details);
            var open = new TaskDialogButton("로그 폴더 열기");
            page.Buttons.Add(open);
            page.Buttons.Add(new TaskDialogButton("닫기"));
            page.Footnote = new TaskDialogFootnote(logDir);
            return TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == open;
        }
        catch (Exception ex)
        {
            Log.Error("TaskDialog failed; falling back to MessageBox", ex);
            MessageBox.Show(heading + "\n\n" + text + (details is null ? "" : "\n\n" + details) + "\n\n" + logDir,
                AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    static void Show(string heading, string? text, TaskDialogIcon icon, MessageBoxIcon fallbackIcon, string? details = null)
    {
        try
        {
            TaskDialog.ShowDialog(NewPage(heading, text, icon, details), TaskDialogStartupLocation.CenterScreen);
        }
        catch (Exception ex)
        {
            Log.Error("TaskDialog failed; falling back to MessageBox", ex);
            string body = text is null ? heading : heading + "\n\n" + text;
            if (details is not null) body += "\n\n" + details;
            MessageBox.Show(body, AppInfo.ProductName, MessageBoxButtons.OK, fallbackIcon);
        }
    }

    static TaskDialogPage NewPage(string heading, string? text, TaskDialogIcon icon, string? details = null)
    {
        var page = new TaskDialogPage
        {
            Caption = AppInfo.ProductName,
            Heading = heading,
            Text = text,
            Icon = icon,
            SizeToContent = true,
        };
        if (!string.IsNullOrWhiteSpace(details))
            page.Expander = new TaskDialogExpander(details) { CollapsedButtonText = "자세히", ExpandedButtonText = "간단히" };
        return page;
    }
}
