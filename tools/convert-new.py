"""Turn loose 1024x1024 JPEG stickers into trimmed transparent PNGs for the UI.

The sheet extractor handles collages; these files are already one icon each, so the
only work is background removal, trim, and downscale.
"""
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw

SRC = "assets/AgAssets/NEW"
OUT = "ui/src/assets/icons"
SIZE = 128
SOFT_LO, SOFT_HI = 16, 44  # channel distance band that becomes a feathered alpha edge

# index -> (group, name). 46 and 47 are duplicate contact sheets, skipped on purpose.
MAP = {
    1: ("engines", "deepseek"),
    2: ("engines", "qwen"),
    3: ("engines", "vyce"),
    4: ("engines", "opencode"),
    5: ("engines", "freebuff"),
    6: ("sections", "appearance"),
    7: ("tools", "generic"),
    8: ("engines", "cline"),
    9: ("sections", "general"),
    10: ("engines", "antigravity"),
    11: ("engines", "claude"),
    12: ("sections", "integrations"),
    13: ("sections", "devtools"),
    14: ("states", "running"),
    15: ("sections", "logs"),
    16: ("sections", "lan"),
    17: ("sections", "about"),
    18: ("states", "offline"),
    19: ("actions", "refresh"),
    20: ("states", "ok"),
    21: ("states", "warn"),
    22: ("actions", "add"),
    23: ("actions", "search"),
    24: ("actions", "copy"),
    25: ("actions", "download"),
    26: ("actions", "external"),
    27: ("actions", "key"),
    28: ("actions", "lock"),
    29: ("actions", "pencil"),
    30: ("actions", "trash"),
    31: ("objects", "workspace"),
    32: ("objects", "session"),
    33: ("objects", "port"),
    34: ("objects", "proxy"),
    35: ("objects", "webhook"),
    36: ("objects", "discord"),
    37: ("objects", "browser"),
    38: ("objects", "package"),
    39: ("objects", "screenshot"),
    40: ("objects", "quota"),
    41: ("objects", "model"),
    42: ("objects", "mcp"),
    43: ("objects", "file-code"),
    44: ("objects", "file-json"),
    45: ("objects", "file-text"),
}


def index_of(name):
    m = re.search(r"photo_(\d+)", name)
    if m:
        return int(m.group(1))
    digits = "".join(ch for ch in os.path.splitext(name)[0] if ch.isdigit())
    return int(digits) if digits else 0


def source_files():
    found = {}
    for n in os.listdir(SRC):
        if n.lower().endswith((".jpg", ".jpeg", ".png")) and not n.startswith("review"):
            found.setdefault(index_of(n), os.path.join(SRC, n))
    return found


def background_color(arr):
    corners = []
    for ys in (slice(0, 24), slice(-24, None)):
        for xs in (slice(0, 24), slice(-24, None)):
            corners.append(arr[ys, xs].reshape(-1, 3))
    return np.median(np.concatenate(corners, axis=0), axis=0)


def cut(path):
    im = Image.open(path).convert("RGB")
    arr = np.asarray(im).astype(np.int16)
    bg = background_color(arr)
    dist = np.abs(arr - bg).max(axis=2).astype(np.float32)
    alpha = np.clip((dist - SOFT_LO) / (SOFT_HI - SOFT_LO), 0.0, 1.0)

    rgba = np.dstack([arr.astype(np.uint8), (alpha * 255).astype(np.uint8)])
    out = Image.fromarray(rgba, "RGBA")

    box = out.getchannel("A").point(lambda v: 255 if v > 12 else 0).getbbox()
    if not box:
        raise SystemExit(f"nothing left after cut: {path}")
    out = out.crop(box)

    pad = int(max(out.size) * 0.04)
    canvas = Image.new("RGBA", (out.width + pad * 2, out.height + pad * 2), (0, 0, 0, 0))
    canvas.paste(out, (pad, pad))

    scale = SIZE / max(canvas.size)
    return canvas.resize((max(1, round(canvas.width * scale)), max(1, round(canvas.height * scale))),
                         Image.LANCZOS)


def contact_sheet(items, path):
    cell, cols = 140, 8
    rows = (len(items) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 18)), (10, 10, 10))
    draw = ImageDraw.Draw(sheet)
    for i, (label, im) in enumerate(items):
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        sheet.paste(im.convert("RGB"), (x + (cell - im.width) // 2, y + 8))
        draw.text((x + 3, y + cell - 4), label, fill=(235, 235, 235))
    sheet.save(path)
    print("sheet:", path, sheet.size)


def main():
    files = source_files()
    made = []
    for idx, (group, name) in sorted(MAP.items()):
        src = files.get(idx)
        if not src:
            print(f"missing index {idx} ({name})")
            continue
        dest_dir = os.path.join(OUT, group)
        os.makedirs(dest_dir, exist_ok=True)
        im = cut(src)
        dest = os.path.join(dest_dir, f"{name}.png")
        im.save(dest)
        made.append((f"{group}/{name}", im))
        print(f"{idx:2} {os.path.basename(src):34} -> {dest:52} {im.width}x{im.height}")
    print(f"{len(made)} icons written")
    if made:
        contact_sheet(made, "assets/icons/review/new.png")


if __name__ == "__main__":
    sys.exit(main())
