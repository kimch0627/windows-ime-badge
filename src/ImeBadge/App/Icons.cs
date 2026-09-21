using System;
using System.Drawing;

namespace ImeBadge;

/// <summary>exe 에 포함(embedded resource)된 아이콘. 파일이 빠져 있어도 기본 아이콘으로 동작한다.</summary>
static class Icons
{
    static Icon? _app, _paused;

    public static Icon App => _app ??= Load("ImeBadge.Assets.app.ico");
    public static Icon Paused => _paused ??= Load("ImeBadge.Assets.paused.ico");

    static Icon Load(string resourceName)
    {
        try
        {
            using var s = typeof(Icons).Assembly.GetManifestResourceStream(resourceName);
            if (s is not null) return new Icon(s);
            Log.Error($"icon resource missing: {resourceName}");
        }
        catch (Exception ex) { Log.Error($"icon load failed: {resourceName}", ex); }
        return SystemIcons.Application;
    }
}
