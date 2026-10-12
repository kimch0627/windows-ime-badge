using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>대화상자 종류. 아이콘 모양과 색이 다르다.</summary>
enum DialogKind { Info, Warning, Error }

/// <summary>
/// 설정 창과 같은 테마(밝게/어둡게, 디자인 테마 색)를 입힌 대화상자(VW-12). Win32 TaskDialog 는 늘 밝은 창이라 어두운 설정 창·트레이 메뉴 위에서
/// 흰 상자가 튀었다. 모양은 TaskDialog 를 따른다: 왼쪽 아이콘, 굵은 제목, 본문, 명령 링크(제목 + 작은 설명), "자세히" 펼침, 아래 [닫기].
/// 명령 링크가 있으면 [닫기] 대신 그중 하나를 고르고, Esc·X 는 고르지 않은 것(-1)이다. 고대비 모드는 시스템이 그리는 TaskDialog 가 맞으므로
/// 쓰지 않는다(<see cref="Dialogs"/>).
/// </summary>
sealed class ThemedDialog : Form
{
    /// <summary>본문 열의 폭(96 DPI 기준 px). TaskDialog 의 기본 폭과 비슷하다.</summary>
    const int ContentWidth = 400;

    int _choice = -1;
    readonly Label? _details;
    readonly LinkLabel? _toggle;
    readonly AccentButton? _close;

    /// <summary>고른 명령 링크의 인덱스. 명령 링크가 없거나 고르지 않고 닫으면 -1.</summary>
    public int Choice => _choice;

    /// <param name="dark">확인 시트용: 밝게/어둡게를 정해 칠한다. 주지 않으면 Windows 앱 모드를 따른다.</param>
    public ThemedDialog(string heading, string? text, DialogKind kind, string? details = null, (string title, string? note)[]? commands = null,
                        bool? dark = null)
    {
        Text = AppInfo.ProductName;
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = Theme.DialogFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 20, 24, 16);
        KeyPreview = true;

        // AutoSize 폼에서는 Dock 을 쓰지 않는다(UpdateProgressForm 과 같은 이유).
        var root = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(20, 20) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var icon = new DialogIcon(kind) { Margin = new Padding(0, 2, 14, 0) };
        root.Controls.Add(icon, 0, 0);

        var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        var title = new Label { Text = heading, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 8), UseMnemonic = false };
        title.Font = new Font(Font.FontFamily, Font.Size * 1.25f, FontStyle.Bold);
        column.Controls.Add(title);
        if (!string.IsNullOrWhiteSpace(text))
            column.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 12), UseMnemonic = false });

        if (commands is { Length: > 0 })
        {
            for (int i = 0; i < commands.Length; i++)
            {
                int index = i;
                var link = new CommandLink(commands[i].title, commands[i].note, ContentWidth) { Margin = new Padding(0, 0, 0, 4), TabIndex = i };
                link.Click += (_, _) => { _choice = index; DialogResult = DialogResult.OK; Close(); };
                column.Controls.Add(link);
            }
        }

        if (!string.IsNullOrWhiteSpace(details))
        {
            _toggle = new LinkLabel { Text = Strings.Get("dialog.details"), AutoSize = true, Margin = new Padding(0, 4, 0, 4), LinkBehavior = LinkBehavior.HoverUnderline };
            _details = new Label { Text = details, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Visible = false, Margin = new Padding(0, 0, 0, 8), ForeColor = SystemColors.GrayText, UseMnemonic = false };
            _toggle.LinkClicked += (_, _) => ToggleDetails();
            column.Controls.Add(_toggle);
            column.Controls.Add(_details);
        }
        root.Controls.Add(column, 1, 0);

        if (commands is not { Length: > 0 })
        {
            _close = new AccentButton { Text = Strings.Get("dialog.close"), AutoSize = true, Primary = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
            root.Controls.Add(_close, 1, 1);
            AcceptButton = _close;
            CancelButton = _close;
        }
        Controls.Add(root);
        if (dark is { } d) Theme.Apply(this, Theme.Palette.From(d ? Theme.Design.Dark : Theme.Design.Light, Theme.Design.CornerRadius));
        else Theme.Apply(this);
    }

    /// <summary>자세히를 펼친 모습(확인 시트용).</summary>
    public void ExpandDetails() { if (_details is { Visible: false }) ToggleDetails(); }

    void ToggleDetails()
    {
        if (_details is null || _toggle is null) return;
        _details.Visible = !_details.Visible;
        _toggle.Text = Strings.Get(_details.Visible ? "dialog.brief" : "dialog.details");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    /// <summary>보인 뒤에 [닫기](명령 링크가 있으면 첫 링크)에 포커스. 만들 때 정하면 아직 보이지 않는 컨트롤이라 예외가 난다.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_close is not null) ActiveControl = _close;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { _choice = -1; DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    /// <summary>
    /// 띄운다. 이 프로그램의 창(설정 창 등)이 앞에 있으면 그 가운데에, 아니면 화면 가운데에. 트레이에서 띄운 것은 작업 표시줄에도 보인다.
    /// </summary>
    public int ShowModal()
    {
        var owner = ActiveForm is { IsDisposed: false, Visible: true } active && active != this ? active : null;
        ShowInTaskbar = owner is null;
        if (owner is not null) { StartPosition = FormStartPosition.CenterParent; ShowDialog(owner); }
        else ShowDialog();
        return _choice;
    }

    /// <summary>왼쪽의 큰 아이콘(아이콘 글꼴 한 글자). 정보 = 강조색, 주의 = InfoBar 의 주의 색, 오류 = InfoBar 의 오류 색.</summary>
    sealed class DialogIcon : ThemedControl
    {
        readonly DialogKind _kind;

        public DialogIcon(DialogKind kind) { _kind = kind; TabStop = false; Size = new Size(32, 32); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Prepare(g);
            g.Clear(BackColor);
            using var font = Theme.IconFont(Px(28));
            if (font is null) return;
            bool dark = Palette.Dark;
            var (glyph, color) = _kind switch
            {
                DialogKind.Warning => ("\uE7BA", dark ? Color.FromArgb(0xFC, 0xE1, 0x00) : Color.FromArgb(0x9D, 0x5D, 0x00)),
                DialogKind.Error => ("\uE783", dark ? Color.FromArgb(0xFF, 0x99, 0xA4) : Color.FromArgb(0xC4, 0x2B, 0x1C)),
                _ => ("\uE946", Palette.Accent),
            };
            TextRenderer.DrawText(g, glyph, font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// TaskDialog 의 명령 링크: 화살표 + 굵은 제목 + 작은 설명. 마우스를 올리면 살짝 칠해지고, Tab 으로 와서 Enter·Space 로 누른다.
    /// 화면 읽기 프로그램에는 단추(제목, 설명)로 알린다.
    /// </summary>
    sealed class CommandLink : ThemedControl
    {
        const int PadX = 10, PadY = 8, Arrow = 16, Gap = 10, LineGap = 2;
        readonly string _title;
        readonly string? _note;
        readonly int _width;

        public CommandLink(string title, string? note, int width)
        {
            _title = title; _note = note; _width = width;
            Text = title;
            AccessibleName = title;
            AccessibleDescription = note;
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            AutoSize = true;   // 크기는 GetPreferredSize(지금 DPI 의 물리 px). 폼의 DPI 배율(AutoScaleMode)이 한 번 더 키우지 않는다.
        }

        Font TitleFont => new(Font.FontFamily, Font.Size * 1.08f, FontStyle.Bold);

        int TextWidth => Px(_width) - Px(PadX) * 2 - Px(Arrow) - Px(Gap);

        public override Size GetPreferredSize(Size proposed)
        {
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            using var tf = TitleFont;
            int h = TextRenderer.MeasureText(_title, tf, new Size(TextWidth, 0), flags).Height;
            if (!string.IsNullOrEmpty(_note)) h += Px(LineGap) + TextRenderer.MeasureText(_note, Font, new Size(TextWidth, 0), flags).Height;
            return new Size(Px(_width), h + Px(PadY) * 2);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Prepare(g);
            g.Clear(BackColor);
            var p = Palette;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (Hot || Pressed)
            {
                using var path = Rounded(r, Px(p.Radius / 2f));
                using var fill = new SolidBrush(Pressed ? Alpha(p.Text, p.Dark ? 30 : 20) : p.Hover);
                g.FillPath(fill, path);
            }
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int x = Px(PadX), y = Px(PadY);
            using (var icon = Theme.IconFont(Px(Arrow)))
                if (icon is not null)
                    TextRenderer.DrawText(g, "\uE72A", icon, new Rectangle(x, y + Px(2), Px(Arrow), Px(Arrow)), p.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int tx = x + Px(Arrow) + Px(Gap);
            using var tf = TitleFont;
            var titleSize = TextRenderer.MeasureText(_title, tf, new Size(TextWidth, 0), flags);
            TextRenderer.DrawText(g, _title, tf, new Rectangle(tx, y, TextWidth, titleSize.Height), p.Text, flags);
            if (!string.IsNullOrEmpty(_note))
                TextRenderer.DrawText(g, _note, Font, new Rectangle(tx, y + titleSize.Height + Px(LineGap), TextWidth, Height - y - titleSize.Height), p.SubtleText, flags);
            DrawFocusRing(g, r, Px(p.Radius / 2f));
        }
    }
}
