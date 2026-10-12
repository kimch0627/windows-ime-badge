using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 개발용: 설정 창의 페이지마다 그림(PNG)을 남긴다. <c>ImeBadge.exe --render-settings [폴더]</c>. 창을 화면 밖에 띄워 <see cref="Control.DrawToBitmap"/>
/// 으로 뜨므로 화면이 잠겨 있어도 되고, 떠 있는 인스턴스와 상관없다. 설정은 기본값(<see cref="Settings"/>)이고 저장하지 않는다(설정 창은
/// <see cref="SettingsForm.Applied"/> 를 받는 쪽이 저장하는데 여기서는 아무도 받지 않는다). 개발 빌드에서는 <c>IMEBADGE_THEME=light|dark</c> 로
/// 밝게/어둡게를 고른다(<see cref="Theme.IsDark"/>). <c>--lang en</c> 이면 영어 화면. 제목 표시줄은 화면 밖 그리기라 실제와 다를 수 있다.
/// </summary>
static class RenderSettings
{
    public const string Switch = "--render-settings";

    /// <summary>미리보기 [보기] 단추가 불러도 아무것도 하지 않는 자리.</summary>
    sealed class NoHost : IExperimentHost
    {
        public void PreviewSonar(Rectangle screen) { }
        public void PreviewCallout(CalloutPreview which, Rectangle screen) { }
    }

    public static bool TryRun(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        string dir = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : "settings-pages";
        try
        {
            dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(dir);
            ApplicationConfiguration.Initialize();
            int lang = Array.FindIndex(args, a => string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase));
            if (lang >= 0 && lang + 1 < args.Length)
                Strings.Setting = args[lang + 1].StartsWith("en", StringComparison.OrdinalIgnoreCase) ? UiLanguage.English : UiLanguage.Korean;
            var live = new Settings();
            live.Language = Strings.Setting;   // 설정 창이 언어 칸을 채울 때 화면 언어와 맞게
            string mode = (Theme.IsDark ? "dark" : "light") + (Strings.IsKorean ? "" : "-en");
            foreach (SettingsPage page in Enum.GetValues(typeof(SettingsPage)))
            {
                using var form = new SettingsForm(live, AppPaths.Default(), () => { }, new NoHost(), page)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false,
                };
                form.Show();
                Application.DoEvents();   // 숨긴 페이지의 첫 배치·지연 그리기를 끝낸다
                using var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                string path = Path.Combine(dir, $"settings-{mode}-{(int)page}-{page.ToString().ToLowerInvariant()}.png");
                bmp.Save(path, ImageFormat.Png);
                Log.Write($"render-settings: {path}");
                form.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error("render-settings failed", ex);
            Environment.ExitCode = 1;
        }
        return true;
    }
}
