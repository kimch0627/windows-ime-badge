"""앱 아이콘(.ico) 생성 스크립트.

    pip install pillow
    python tools/make_icons.py [경로/NotoSansKR.ttf]

파란 둥근 사각형 위에 흰 "한" 글자. paused.ico 는 회색 버전.
글꼴 파일을 주지 않으면 시스템에서 Malgun Gothic / Noto Sans KR 을 찾고, 없으면 글자 없이 도형만 그린다.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "ImeBadge", "Assets")

CANDIDATE_FONTS = [
    r"C:\Windows\Fonts\malgunbd.ttf",
    r"C:\Windows\Fonts\malgun.ttf",
    "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc",
    "/usr/share/fonts/truetype/noto/NotoSansKR-Bold.ttf",
]


def find_font(explicit: str | None) -> str | None:
    for p in ([explicit] if explicit else []) + CANDIDATE_FONTS:
        if p and os.path.exists(p):
            return p
    return None


def load_font(path: str | None, px: int):
    if path is None:
        return None
    f = ImageFont.truetype(path, px)
    try:
        f.set_variation_by_name("Bold")   # 가변 글꼴(variable font)이면 굵게
    except Exception:
        pass
    return f


def render(size: int, fill: tuple, font_path: str | None) -> Image.Image:
    # 4배로 그린 뒤 축소해 가장자리를 부드럽게
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = int(s * 0.04)
    radius = int(s * 0.22)
    d.rounded_rectangle((pad, pad, s - pad, s - pad), radius=radius, fill=fill,
                        outline=(255, 255, 255, 110), width=max(1, s // 64))

    font = load_font(font_path, int(s * 0.62))
    if font is not None:
        text = "한"
        bbox = d.textbbox((0, 0), text, font=font, anchor="lt")
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        x = (s - tw) / 2 - bbox[0]
        y = (s - th) / 2 - bbox[1]
        d.text((x, y), text, font=font, fill=(255, 255, 255, 255), anchor="lt")
    else:
        # 글꼴이 없으면 흰 점으로 대신
        r = s * 0.18
        d.ellipse((s / 2 - r, s / 2 - r, s / 2 + r, s / 2 + r), fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


def save_ico(path: str, fill: tuple, font_path: str | None) -> None:
    frames = [render(sz, fill, font_path) for sz in SIZES]
    frames[-1].save(path, format="ICO", sizes=[(sz, sz) for sz in SIZES], append_images=frames[:-1])
    print(f"wrote {path} ({os.path.getsize(path)} bytes)")


def main() -> None:
    font_path = find_font(sys.argv[1] if len(sys.argv) > 1 else None)
    print("font:", font_path or "(none)")
    os.makedirs(OUT_DIR, exist_ok=True)
    save_ico(os.path.join(OUT_DIR, "app.ico"), (0, 120, 215, 255), font_path)       # #0078D7
    save_ico(os.path.join(OUT_DIR, "paused.ico"), (140, 140, 140, 255), font_path)  # 회색: 일시 중지


if __name__ == "__main__":
    main()
