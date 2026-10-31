"""Contact sheets for a folder of loose icons, numbered so they can be referred to by index."""
import os
import sys

from PIL import Image, ImageDraw

SRC = sys.argv[1] if len(sys.argv) > 1 else "assets/AgAssets/NEW"
CELL, PAD, COLS = 200, 8, 6
LABEL_H = 22


def files_in_order(path):
    names = [n for n in os.listdir(path) if n.lower().endswith((".png", ".jpg", ".jpeg", ".webp"))]

    def key(n):
        stem = os.path.splitext(n)[0]
        digits = "".join(ch for ch in stem if ch.isdigit())
        return (int(digits) if digits else 0, n)

    return sorted(names, key=key)


def sheet(items, out_path, start_index):
    rows = (len(items) + COLS - 1) // COLS
    w = COLS * (CELL + PAD) + PAD
    h = rows * (CELL + LABEL_H + PAD) + PAD
    canvas = Image.new("RGB", (w, h), (8, 8, 8))
    draw = ImageDraw.Draw(canvas)
    for i, (name, im) in enumerate(items):
        col, row = i % COLS, i // COLS
        x = PAD + col * (CELL + PAD)
        y = PAD + row * (CELL + LABEL_H + PAD)
        thumb = im.copy()
        thumb.thumbnail((CELL, CELL), Image.LANCZOS)
        # checker so transparent art is visible
        plate = Image.new("RGB", (CELL, CELL), (17, 17, 17))
        px, py = x + (CELL - thumb.width) // 2, y + (CELL - thumb.height) // 2
        if thumb.mode == "RGBA":
            plate.paste(thumb, ((CELL - thumb.width) // 2, (CELL - thumb.height) // 2), thumb)
        else:
            plate.paste(thumb, ((CELL - thumb.width) // 2, (CELL - thumb.height) // 2))
        canvas.paste(plate, (x, y))
        label = f"{start_index + i}. {name.split('_')[1] if '_' in name else name}"
        draw.text((x + 2, y + CELL + 4), label, fill=(230, 230, 230))
        draw.text((x + 2, y + CELL + 12), f"{im.width}x{im.height}", fill=(120, 120, 120))
    canvas.save(out_path)
    print(out_path, canvas.size)


def main():
    names = files_in_order(SRC)
    loaded = []
    for n in names:
        im = Image.open(os.path.join(SRC, n))
        loaded.append((n, im.convert("RGBA")))
    half = 24
    sheet(loaded[:half], os.path.join(SRC, "review-1.png"), 1)
    sheet(loaded[half:], os.path.join(SRC, "review-2.png"), half + 1)
    for i, (n, im) in enumerate(loaded, 1):
        print(f"{i:2} {n:30} {im.width}x{im.height} alpha={im.getpixel((0, 0))[3]}")


if __name__ == "__main__":
    main()
