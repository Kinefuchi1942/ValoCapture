"""アプリアイコン（src/ValoCapture.App/Assets/app.ico）を生成する。

デザイン: 暗い角丸の背景に、左の腕がキル（緑）・右の腕がデス（赤）の「V」を描き、下に再生バーを置く。
赤の腕は緑より太くして目立たせる。32px 以下は再生バーを省き、V を太く大きく描く。
usage: python tools/make_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024  # 1024px で描いて各サイズに縮小する
BG_TOP, BG_BOTTOM = (0x22, 0x2B, 0x38), (0x10, 0x15, 0x1C)
TRACK = (0x3A, 0x48, 0x5A)
GREEN, RED, WHITE = (0x6F, 0xD3, 0xA8), (0xFF, 0x46, 0x55), (0xFF, 0xFF, 0xFF)

out_dir = Path(__file__).resolve().parent.parent / "src" / "ValoCapture.App" / "Assets"


def background() -> Image.Image:
    grad = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        d.line([(0, y), (S, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=230, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, mask=mask)
    return img


def pill(d: ImageDraw.ImageDraw, p0: tuple[int, int], p1: tuple[int, int], w: int, fill: tuple) -> None:
    """両端の丸い太線"""
    d.line([p0, p1], fill=fill, width=w)
    for x, y in (p0, p1):
        d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=fill)


def v_mark(left: tuple[int, int], right: tuple[int, int], tip: tuple[int, int], w: int, red_w: int,
           bar: tuple[int, int, int, int] | None = None, played_x: int = 0) -> Image.Image:
    """left/right=V の腕の上端、tip=V の先端、w=緑の腕の太さ、red_w=赤の腕の太さ、
    bar=再生バー (x0, y0, x1, y1)（None で無し）、played_x=再生済みの右端"""
    img = background()
    d = ImageDraw.Draw(img)
    pill(d, left, tip, w, GREEN)
    pill(d, right, tip, red_w, RED)  # 赤を後に描いて先端で緑に重ねる
    if bar:
        x0, y0, x1, y1 = bar
        radius = (y1 - y0) // 2
        d.rounded_rectangle(bar, radius=radius, fill=TRACK)
        d.rounded_rectangle([x0, y0, played_x, y1], radius=radius, fill=WHITE)
    return img


# 48px 以上: V + 再生バー
large = v_mark(left=(250, 230), right=(774, 230), tip=(512, 650), w=120, red_w=170,
               bar=(190, 770, 834, 840), played_x=512)

# 32px 以下: 再生バーを省き、V を太く大きく
small = v_mark(left=(220, 220), right=(804, 220), tip=(512, 790), w=170, red_w=230)

sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [(small if s <= 32 else large).resize((s, s), Image.LANCZOS) for s in sizes]

out_dir.mkdir(parents=True, exist_ok=True)
frames[-1].save(out_dir / "app.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
large.resize((256, 256), Image.LANCZOS).save(out_dir / "app.png")
print(f"wrote {out_dir / 'app.ico'}")
