using System;

namespace ImeBadge;

/// <summary>
/// 배지 몸통의 가로·세로 길이 설정(<see cref="Settings.BadgeWidthPercent"/>·<see cref="Settings.BadgeHeightPercent"/>).
/// 100% 는 모양마다 다듬어 둔 기본 치수이고, 이것이 권장값이다: 둥근·사각 배지는 높이 20px(배율 1)로 9~11pt 글줄 높이와 비슷해
/// 커서 옆에서 글줄을 넘지 않고, 폭은 글자 좌우에 7px 씩 두어 "a" 는 동그라미가 된다. 점은 지름 9px, 밑줄은 16×3px, 캐릭터는 24 단위 상자.
/// 가로·세로를 따로 늘이면 모양이 그만큼 늘어나고, 글자는 둘 중 작은 쪽 비율을 따라 배지 안에 들어간다.
/// 특수 키 표시(▲ ▁ ■)와 테두리 굵기는 비율과 상관없이 그대로라 작게 줄여도 알아볼 수 있다.
/// </summary>
public static class BadgeStretch
{
    public const int Min = 50, Max = 200, Recommended = 100;

    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>설정값(%)을 렌더러가 곱하는 배수로. 범위 밖이면 범위 안으로.</summary>
    public static float Factor(int percent) => Clamp(percent) / 100f;
}
