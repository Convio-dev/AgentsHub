"""Re-cut icons that picked up stray fragments (detached sparkles, dots) during extraction.

Reads assets/icons/manifest.json for the source sheet and box, cuts the background again,
drops every alpha island that is small next to the main one, centres the result on a square
canvas and writes it to ui/src/assets/icons/<group>/<name>.png.

    python tools/recut-icons.py ai
    python tools/recut-icons.py --sweep      # report islands in the UI copies only
"""
import json
import os
import sys
from collections import deque

import numpy as np
from PIL import Image

ROOT = "assets/icons"
UI = "ui/src/assets/icons"
SIZE = 128
PAD = 0.06
KEEP_SHARE = 0.45  # islands below this fraction of the main island are dropped


def load_manifest():
    with open(os.path.join(ROOT, "manifest.json")) as fh:
        return json.load(fh)


def background(arr):
    corners = []
    h, w = arr.shape[:2]
    m = max(4, min(h, w) // 40)
    for ys in (slice(0, m), slice(-m, None)):
        for xs in (slice(0, m), slice(-m, None)):
            corners.append(arr[ys, xs].reshape(-1, 3))
    return np.median(np.concatenate(corners, axis=0), axis=0)


def alpha_of(rgb):
    arr = np.asarray(rgb).astype(np.int16)
    bg = background(arr)
    dist = np.abs(arr - bg).max(axis=2).astype(np.float32)
    return np.clip((dist - 16) / (44 - 16), 0.0, 1.0)


def islands(mask):
    h, w = mask.shape
    seen = np.zeros((h, w), bool)
    out = []
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or seen[y, x]:
                continue
            q = deque([(y, x)])
            seen[y, x] = True
            pixels = []
            while q:
                cy, cx = q.popleft()
                pixels.append((cy, cx))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        q.append((ny, nx))
            ys = [p[0] for p in pixels]
            xs = [p[1] for p in pixels]
            out.append({"area": len(pixels), "pixels": pixels,
                        "box": (min(xs), min(ys), max(xs) + 1, max(ys) + 1)})
    return sorted(out, key=lambda p: -p["area"])


def recut(entry):
    sheet = Image.open(os.path.join("assets/AgAssets", entry["sheet"])).convert("RGB")
    x1, y1, x2, y2 = entry["box"]
    m = max(6, int(0.02 * max(sheet.size)))
    crop = sheet.crop((max(0, x1 - m), max(0, y1 - m), min(sheet.width, x2 + m), min(sheet.height, y2 + m)))

    alpha = alpha_of(crop)
    # Faint halo pixels survive the background cut as near-transparent grey specks, and they
    # widen the trim box enough to push the subject off centre. Keep confident foreground only.
    alpha = np.where(alpha < 0.5, 0.0, alpha)
    parts = islands(alpha > 0.5)
    if not parts:
        raise SystemExit(f"nothing found for {entry['name']}")
    main = parts[0]["area"]
    keep = {i for i, p in enumerate(parts) if p["area"] >= main * KEEP_SHARE}
    dropped = [p for i, p in enumerate(parts) if i not in keep]

    # Fill the mask from the island's own pixels: filling its bounding box would sweep in
    # any speck that happens to sit inside that rectangle.
    mask = np.zeros(alpha.shape, bool)
    for i in keep:
        for py, px in parts[i]["pixels"]:
            mask[py, px] = True
    # grow the kept mask a little so the feathered edge survives
    grown = mask.copy()
    for _ in range(2):
        grown[1:, :] |= grown[:-1, :]
        grown[:-1, :] |= grown[1:, :]
        grown[:, 1:] |= grown[:, :-1]
        grown[:, :-1] |= grown[:, 1:]
    alpha = np.where(grown, alpha, 0.0)

    rgba = np.dstack([np.asarray(crop).astype(np.uint8), (alpha * 255).astype(np.uint8)])
    img = Image.fromarray(rgba, "RGBA")
    box = img.getchannel("A").point(lambda v: 255 if v > 12 else 0).getbbox()
    img = img.crop(box)

    side = max(img.size)
    pad = int(side * PAD)
    canvas = Image.new("RGBA", (side + pad * 2, side + pad * 2), (0, 0, 0, 0))
    canvas.paste(img, ((canvas.width - img.width) // 2, (canvas.height - img.height) // 2))
    small = canvas.resize((SIZE, SIZE), Image.LANCZOS)

    dest_dir = os.path.join(UI, entry["group"])
    os.makedirs(dest_dir, exist_ok=True)
    # The manifest labels this box "wand", but the art in it is the CPU chip people expect.
    name = "chip" if entry["group"] == "ai" and entry["name"] == "wand" else entry["name"]
    dest = os.path.join(dest_dir, f"{name}.png")
    small.save(dest)
    stray = 100.0 * sum(p["area"] for p in dropped) / max(1, sum(p["area"] for p in parts))
    return dest, len(dropped), stray


def sweep():
    flagged = []
    for root, _, files in os.walk(UI):
        for f in sorted(files):
            if not f.endswith(".png"):
                continue
            path = os.path.join(root, f)
            alpha = np.asarray(Image.open(path).convert("RGBA"))[:, :, 3]
            parts = islands(alpha > 60)
            if len(parts) < 2:
                continue
            share = 100.0 * sum(p["area"] for p in parts[1:]) / parts[0]["area"]
            if share > 1.0:
                flagged.append((round(share, 1), os.path.relpath(path, UI)))
    for share, path in sorted(flagged, reverse=True):
        print(f"{share:6.1f}%  {path}")
    print(f"{len(flagged)} icons carry stray islands")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--sweep" in sys.argv:
        sweep()
        return
    groups = set(args or ["ai"])
    for entry in load_manifest():
        if entry["group"] not in groups:
            continue
        dest, dropped, stray = recut(entry)
        print(f"{entry['group']}/{entry['name']:14} dropped {dropped} island(s), {stray:5.1f}% of pixels -> {dest}")


if __name__ == "__main__":
    main()
