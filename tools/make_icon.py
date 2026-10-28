"""アプリアイコン（src/ValoRecorder.App/Assets/app.ico）を生成する。

デザイン: 暗い角丸の背景に、アプリのタイムラインを大きく描く（キル=緑、デス=赤の印、白い再生位置）。
再生位置のつまみを小さな赤丸にして「録画」を控えめに示す。
32px 以下は印を 3 本に減らし、太く描く。
usage: python tools/make_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024  # 1024px で描いて各サイズに縮小する
BG_TOP, BG_BOTTOM = (0x2A, 0x2E, 0x37), (0x14, 0x16, 0x1B)
TRACK, PLAYED = (0x3A, 0x40, 0x4C), (0x55, 0x5D, 0x6C)
GREEN, RED, WHITE = (0x4A, 0xDE, 0x9B), (0xFF, 0x46, 0x55), (0xFF, 0xFF, 0xFF)

out_dir = Path(__file__).resolve().parent.parent / "src" / "ValoRecorder.App" / "Assets"


def background() -> Image.Image:
    grad = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        d.line([(0, y), (S, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=220, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, mask=mask)
    return img


def timeline(track: tuple[int, int, int, int], markers: list[tuple[int, int, int, tuple]], marker_w: int,
             playhead_x: int, playhead: tuple[int, int, int], knob: int) -> Image.Image:
    """track=(x0, y0, x1, y1)、markers=[(x, 上端, 下端, 色)]、playhead=(幅, 上端, 下端)、knob=つまみの半径（0 で無し）"""
    img = background()
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = track
    radius = (y1 - y0) // 2
    d.rounded_rectangle(track, radius=radius, fill=TRACK)
    d.rounded_rectangle([x0, y0, playhead_x, y1], radius=radius, fill=PLAYED)

    for x, top, bottom, color in markers:
        d.rounded_rectangle([x - marker_w // 2, top, x + marker_w // 2, bottom], radius=marker_w // 2, fill=color)

    w, top, bottom = playhead
    d.rounded_rectangle([playhead_x - w // 2, top, playhead_x + w // 2, bottom], radius=w // 2, fill=WHITE)
    if knob:
        d.ellipse([playhead_x - knob, top - knob, playhead_x + knob, top + knob], fill=WHITE)
        inner = knob * 2 // 3
        d.ellipse([playhead_x - inner, top - inner, playhead_x + inner, top + inner], fill=RED)
    return img


# 48px 以上: 印 6 本（高さに差をつけてリズムを出す）
large = timeline(
    track=(150, 470, 874, 554),
    markers=[(250, 360, 664, GREEN), (338, 300, 724, RED), (430, 380, 644, GREEN),
             (640, 330, 694, GREEN), (730, 390, 634, RED), (812, 350, 674, GREEN)],
    marker_w=48,
    playhead_x=535, playhead=(26, 270, 790), knob=70,
)

# 32px 以下: 印 3 本を太く、つまみ無し
small = timeline(
    track=(130, 440, 894, 584),
    markers=[(290, 270, 754, GREEN), (450, 330, 694, RED), (790, 290, 734, GREEN)],
    marker_w=110,
    playhead_x=620, playhead=(60, 190, 834), knob=0,
)

sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [(small if s <= 32 else large).resize((s, s), Image.LANCZOS) for s in sizes]

out_dir.mkdir(parents=True, exist_ok=True)
frames[-1].save(out_dir / "app.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
large.resize((256, 256), Image.LANCZOS).save(out_dir / "app.png")
print(f"wrote {out_dir / 'app.ico'}")
