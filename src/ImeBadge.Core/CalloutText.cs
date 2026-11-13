using System;
using System.Text;

namespace ImeBadge;

/// <summary>
/// 말풍선 설명 글의 줄바꿈. GDI+ 는 한글을 글자 사이 어디서나 끊어서("쓰던 곳으로 돌아가려 | 면") 낱말이 두 줄로 갈라진다. 한국어는 띄어쓰기
/// 단위로 끊어야 읽기 쉬우므로(CSS 의 word-break: keep-all) 띄어쓰기 자리에 줄바꿈을 미리 넣어 GDI+ 에 넘긴다.
/// 한 낱말이 한 줄보다 길면 그 낱말만은 GDI+ 가 끊는다.
/// </summary>
public static class CalloutText
{
    /// <summary>말풍선에 넣는 앱 이름의 최대 길이. 넘치면 잘라 말줄임표를 붙인다.</summary>
    public const int MaxAppName = 40;

    /// <summary>
    /// 실행 파일 설명(FileDescription)을 말풍선에 넣을 앱 이름으로 줄인다. "Xshell: Powerful TELNET/SSH terminal emulator" 처럼
    /// "이름: 설명" 꼴이면 콜론 앞의 이름만 쓴다. 긴 설명이 경고 문장을 밀어내 정작 "Alt+Tab 을 누르세요" 가 잘리지 않게.
    /// 그래도 길면 <see cref="MaxAppName"/> 자에서 자른다.
    /// </summary>
    public static string ShortAppName(string description)
    {
        string name = description.Trim();
        int colon = name.IndexOf(": ", StringComparison.Ordinal);
        if (colon >= 2) name = name[..colon].TrimEnd();
        return name.Length > MaxAppName ? name[..MaxAppName].TrimEnd() + "…" : name;
    }

    /// <param name="width">한 줄의 최대 폭.</param>
    /// <param name="measure">글을 한 줄로 그렸을 때의 폭.</param>
    public static string WrapAtSpaces(string text, float width, Func<string, float> measure)
    {
        var sb = new StringBuilder(text.Length + 4);
        var paragraphs = text.Split('\n');
        for (int i = 0; i < paragraphs.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            string line = "";
            foreach (var word in paragraphs[i].Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && measure(candidate) > width)
                {
                    sb.Append(line).Append('\n');
                    line = word;
                }
                else line = candidate;
            }
            sb.Append(line);
        }
        return sb.ToString();
    }
}
