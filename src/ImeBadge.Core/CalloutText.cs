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
