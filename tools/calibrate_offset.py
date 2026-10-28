"""時刻補正（session.json の OffsetMs）の測定ツール。

録画のキルフィード領域で「自分の行（緑）」が現れた時刻を検出し、timeline.json の予測位置と比べる。
表示される diff の中央値（特に Kill 行）を現在の OffsetMs に足したものが、正しい補正値の目安。
各イベントは予測位置にいちばん近い出現と対応させるので、イベントが密集していると取り違えることがある（中央値で吸収する）。

前提: 1920x1080 録画、キルフィードが画面右上（既定の HUD 配置）
usage: python tools/calibrate_offset.py <ffmpeg.exe> <セッションフォルダ>
"""
import json, subprocess, sys
import numpy as np

ffmpeg, session = sys.argv[1], sys.argv[2]
FPS, W, H = 20, 380, 150
cmd = [ffmpeg, "-v", "error", "-i", f"{session}/video.mp4",
       "-vf", f"fps={FPS},crop=760:300:1150:40,scale={W}:{H}", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
proc = subprocess.Popen(cmd, stdout=subprocess.PIPE)
counts = []
while True:
    buf = proc.stdout.read(W * H * 3)
    if len(buf) < W * H * 3:
        break
    a = np.frombuffer(buf, np.uint8).reshape(H, W, 3).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g > 140) & (g - r > 50) & (g - b > 25)
    counts.append(int(green.sum()))
counts = np.array(counts)

# 0.5 秒以内に緑ピクセルが大きく増えた瞬間を「自分の行の出現」とみなす
onsets = []
for i in range(1, len(counts)):
    base = counts[max(0, i - 10):i].min()
    if counts[i] - base > 300 and (not onsets or i / FPS - onsets[-1] > 1.0):
        onsets.append(i / FPS)

timeline = json.load(open(f"{session}/timeline.json", encoding="utf-8"))
offset = json.load(open(f"{session}/session.json", encoding="utf-8-sig"))["OffsetMs"]
events = [e for e in timeline["Events"] if e["Type"] in ("Kill", "Death") and e["VideoTimeMs"] is not None]
diffs = {"Kill": [], "Death": []}
for e in events:
    t = e["VideoTimeMs"] / 1000
    near = [o for o in onsets if abs(o - t) <= 3.0]
    if near:
        o = min(near, key=lambda x: abs(x - t))
        diffs[e["Type"]].append(o - t)
        print(f"{e['Type']:5} pred={t:7.2f} seen={o:7.2f} diff={o - t:+.2f}")
    else:
        print(f"{e['Type']:5} pred={t:7.2f} seen=   none")

print(f"current OffsetMs={offset}")
for kind, ds in diffs.items():
    if ds:
        m = float(np.median(ds))
        print(f"{kind}: n={len(ds)} median diff={m:+.2f}s -> suggested OffsetMs={offset + m * 1000:.0f}")
