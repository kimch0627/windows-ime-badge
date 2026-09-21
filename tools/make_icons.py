"""앱 아이콘(.ico) 생성 스크립트.

    pip install pillow
    python tools/make_icons.py

디자인: 파란 둥근 사각형 위에 흰 텍스트 커서(I-beam)와 그 오른쪽 위의 작은 배지.
"한" 같은 특정 언어 글자를 쓰지 않는다. 나중에 일본어·중국어 IME 를 지원해도 아이콘은 그대로 쓸 수 있고,
글꼴 없이 도형만으로 그리므로 어느 환경에서나 같은 결과가 나온다.
paused.ico 는 회색 버전(일시 중지 상태의 트레이 아이콘).
"""
import os

from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "ImeBadge", "Assets")

BLUE = (0, 120, 215, 255)       # #0078D7, 한글 배지 기본색과 같다
GRAY = (140, 140, 140, 255)
WHITE = (255, 255, 255, 255)
BADGE = (255, 211, 77, 255)     # 배지: 배경과 대비되는 따뜻한 색


def render(size: int, fill: tuple) -> Image.Image:
    # 4배로 그린 뒤 축소해 가장자리를 부드럽게
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = int(s * 0.04)
    d.rounded_rectangle((pad, pad, s - pad, s - pad), radius=int(s * 0.22), fill=fill,
                        outline=(255, 255, 255, 110), width=max(1, s // 64))

    # 텍스트 커서(I-beam): 세로 막대 + 위아래 짧은 가로선. 왼쪽으로 조금 치우쳐 오른쪽 위에 배지 자리를 남긴다.
    cx = int(s * 0.42)
    top, bottom = int(s * 0.24), int(s * 0.80)
    bar = max(2, int(s * 0.075))
    serif = int(s * 0.13)
    d.rounded_rectangle((cx - bar // 2, top, cx + bar // 2, bottom), radius=bar // 2, fill=WHITE)
    for y in (top, bottom):
        d.rounded_rectangle((cx - serif, y - bar // 2, cx + serif, y + bar // 2), radius=bar // 2, fill=WHITE)

    # 커서 오른쪽 위의 배지(원). 실제 프로그램이 caret 옆에 띄우는 배지의 위치와 같다.
    r = int(s * 0.14)
    bx, by = int(s * 0.70), int(s * 0.30)
    d.ellipse((bx - r, by - r, bx + r, by + r), fill=BADGE, outline=WHITE, width=max(1, s // 48))

    return img.resize((size, size), Image.LANCZOS)


def save_ico(path: str, fill: tuple) -> None:
    frames = [render(sz, fill) for sz in SIZES]
    frames[-1].save(path, format="ICO", sizes=[(sz, sz) for sz in SIZES], append_images=frames[:-1])
    print(f"wrote {path} ({os.path.getsize(path)} bytes)")


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    save_ico(os.path.join(OUT_DIR, "app.ico"), BLUE)
    save_ico(os.path.join(OUT_DIR, "paused.ico"), GRAY)


if __name__ == "__main__":
    main()
