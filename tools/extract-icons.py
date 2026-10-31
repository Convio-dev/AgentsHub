"""Cut the sticker sheets in assets/AgAssets into individual transparent PNGs.

Segment mode labels foreground regions, folds nested pieces into their host, reattaches
small fragments to the sticker they belong to, then crops each sticker with a feathered
alpha channel. Grid mode slices the pixel-art sheets on their cell grid and drops the
caption bands, which must not end up inside a sprite.

Pillow only. The sheets are about a megapixel each, so a plain BFS is fast enough and the
repo stays free of a numpy dependency.
"""
import json
from collections import deque
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "assets" / "AgAssets"
OUT = ROOT / "assets" / "icons"
REVIEW = OUT / "review"

TOL = 26
PAD = 4
ATTACH_GAP = 40

# stem, group, mode, keeper area, names in reading order
SHEETS = [
    ("icons-tools-1", "tools", "segment", 5000,
     ["terminal", "document", "braces", "folder", "folders",
      "git", "github", "wifi-off", "bolt", "gear"]),
    ("icons-tools", "ai", "segment", 5000,
     ["openai-knot", "asterisk", "sparkle", "robot",
      "brain", "chat", "wand", "chip"]),
    ("icons-1", "mascot", "segment", 50000,
     ["cat-confused", "cat-terminal", "cat-folder", "cat-database"]),
    ("icons-2", "collage", "segment", 3000, []),
    ("Icons-3", "collage", "segment", 3000, []),
    ("caty", "pixel-mascot", "segment", 10000, []),
    ("2d-cat", "sprite-cat", "grid", 0, []),
    ("2d-animals", "sprite-animals", "grid", 0, []),
]


def background(rgb):
    w, h = rgb.size
    px = rgb.load()
    picks = [px[x, y] for y in (2, h - 3) for x in (2, w - 3)]
    picks.sort(key=sum)
    return picks[len(picks) // 2]


def foreground(rgb):
    """Binary mask of anything far enough from the sheet's own background colour."""
    w, h = rgb.size
    bg = background(rgb)
    diff = ImageChops.difference(rgb, Image.new("RGB", (w, h), bg))
    red, green, blue = diff.split()
    spread = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    return spread.point(lambda v: 255 if v > TOL else 0)


def label(mask):
    """8-connected components over the foreground mask."""
    w, h = mask.size
    data = mask.load()
    seen = bytearray(w * h)
    parts = []
    for start in range(w * h):
        y, x = divmod(start, w)
        if not data[x, y] or seen[start]:
            continue
        seen[start] = 1
        queue = deque([(x, y)])
        pixels = []
        min_x, min_y, max_x, max_y = x, y, x, y
        while queue:
            cx, cy = queue.popleft()
            pixels.append(cy * w + cx)
            if cx < min_x:
                min_x = cx
            if cx > max_x:
                max_x = cx
            if cy < min_y:
                min_y = cy
            if cy > max_y:
                max_y = cy
            for dy in (-1, 0, 1):
                ny = cy + dy
                if ny < 0 or ny >= h:
                    continue
                for dx in (-1, 0, 1):
                    nx = cx + dx
                    if 0 <= nx < w and data[nx, ny] and not seen[ny * w + nx]:
                        seen[ny * w + nx] = 1
                        queue.append((nx, ny))
        if len(pixels) >= 60:
            parts.append({"pixels": pixels,
                          "box": (min_x, min_y, max_x + 1, max_y + 1),
                          "area": len(pixels)})
    return parts


def contains(inner, outer, slack=8):
    return (inner[0] >= outer[0] - slack and inner[1] >= outer[1] - slack
            and inner[2] <= outer[2] + slack and inner[3] <= outer[3] + slack)


def gap(a, b):
    return max(max(a[0] - b[2], b[0] - a[2], 0), max(a[1] - b[3], b[1] - a[3], 0))


def merge_into(host, guest):
    host["pixels"].extend(guest["pixels"])
    host["area"] += guest["area"]
    host["box"] = (min(host["box"][0], guest["box"][0]),
                   min(host["box"][1], guest["box"][1]),
                   max(host["box"][2], guest["box"][2]),
                   max(host["box"][3], guest["box"][3]))


def group(parts, keep):
    """Fold nested pieces inward, then attach small fragments to the nearest keeper."""
    changed = True
    while changed:
        changed = False
        for i in range(len(parts)):
            for j in range(i + 1, len(parts)):
                a, b = parts[i], parts[j]
                if contains(b["box"], a["box"]):
                    merge_into(a, b)
                elif contains(a["box"], b["box"]):
                    merge_into(b, a)
                else:
                    continue
                parts.pop(j)
                changed = True
                break
            if changed:
                break

    keepers = [p for p in parts if p["area"] >= keep]
    for scrap in [p for p in parts if p["area"] < keep]:
        if not keepers:
            break
        nearest = min(keepers, key=lambda k: gap(scrap["box"], k["box"]))
        # Close enough means it is a detached part of that sticker, not sheet decoration.
        if gap(scrap["box"], nearest["box"]) <= ATTACH_GAP:
            merge_into(nearest, scrap)
    return keepers


def reading_order(parts, height):
    row_height = max(height // 5, 1)
    return sorted(parts, key=lambda p: ((p["box"][1] + p["box"][3]) // 2 // row_height,
                                        p["box"][0]))


def crop(rgb, part, feather):
    w, h = rgb.size
    x0, y0, x1, y1 = part["box"]
    x0, y0 = max(0, x0 - PAD), max(0, y0 - PAD)
    x1, y1 = min(w, x1 + PAD), min(h, y1 + PAD)
    alpha = Image.new("L", (x1 - x0, y1 - y0), 0)
    put = alpha.load()
    for index in part["pixels"]:
        y, x = divmod(index, w)
        put[x - x0, y - y0] = 255
    if feather:
        # Softens the JPEG fringe so the cut edge does not read as a dark halo.
        alpha = alpha.filter(ImageFilter.GaussianBlur(0.7)).point(
            lambda v: 0 if v < 20 else v)
    tile = rgb.crop((x0, y0, x1, y1)).convert("RGBA")
    tile.putalpha(alpha)
    return tile, (x0, y0, x1, y1)


def runs(values, floor, min_gap, min_size):
    spans, start = [], None
    for index, value in enumerate(values):
        if value > floor and start is None:
            start = index
        elif value <= floor and start is not None:
            spans.append([start, index])
            start = None
    if start is not None:
        spans.append([start, len(values)])
    merged = []
    for span in spans:
        if merged and span[0] - merged[-1][1] <= min_gap:
            merged[-1][1] = span[1]
        else:
            merged.append(span)
    return [(a, b) for a, b in merged if b - a >= min_size]


def grid_cells(mask):
    """Pixel art: horizontal bands first, then cells inside each band."""
    w, h = mask.size
    # Median filter first: JPEG ringing scatters single pixels of noise across the flat
    # background, and any column touching one of them stops reading as an empty gap.
    mask = mask.filter(ImageFilter.MedianFilter(3))
    rows = list(mask.convert("L").resize((1, h), Image.BOX).tobytes())
    cells = []
    for top, bottom in runs(rows, 6, 4, 60):
        band = mask.crop((0, top, w, bottom))
        cols = list(band.convert("L").resize((w, 1), Image.BOX).tobytes())
        spans = runs(cols, 6, 6, 30)
        if len(spans) < 2:
            # Frames touching each other read as one span; fall back to equal quarters so a
            # cycle still yields its cells instead of a single strip.
            step = w / 4
            spans = [(int(i * step), int((i + 1) * step)) for i in range(4)]
        for left, right in spans:
            cell = band.crop((left, 0, right, bottom - top))
            box = cell.getbbox()
            if box is None:
                continue
            x0, y0, x1, y1 = box
            if (x1 - x0) * (y1 - y0) < 400:
                continue
            cells.append((left + x0, top + y0, left + x1, top + y1))
    return cells


def run_sheet(stem, group_name, mode, keep, names, manifest, report):
    path = next((p for p in SRC.iterdir() if p.stem.lower() == stem.lower()), None)
    if path is None:
        print("missing sheet:", stem)
        return None

    rgb = Image.open(path).convert("RGB")
    mask = foreground(rgb)
    folder = OUT / group_name
    folder.mkdir(parents=True, exist_ok=True)
    tiles = []

    if mode == "grid":
        for box in grid_cells(mask):
            tile = rgb.crop(box).convert("RGBA")
            tile.putalpha(mask.crop(box))
            tiles.append((tile, box))
    else:
        for part in reading_order(group(label(mask), keep), rgb.size[1]):
            tile, box = crop(rgb, part, feather=True)
            tiles.append((tile, box))

    for index, (tile, box) in enumerate(tiles, start=1):
        name = names[index - 1] if index - 1 < len(names) else f"{stem.lower()}-{index:02d}"
        target = folder / f"{name}.png"
        tile.save(target)
        manifest.append({"name": name, "group": group_name, "sheet": path.name,
                         "box": list(box), "size": list(tile.size),
                         "file": str(target.relative_to(ROOT)).replace("\\", "/")})

    report.append((path.name, mode, len(tiles), max((t[0].size[0] for t in tiles), default=0)))
    return tiles


def montage(entries, out_path, title):
    """Every crop with its index and name, so a bad cut is visible before it ships."""
    cols, cell, label = 6, 150, 26
    rows = max((len(entries) + cols - 1) // cols, 1)
    canvas = Image.new("RGB", (cols * cell, rows * (cell + label) + 34), (18, 18, 20))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    draw.text((8, 8), f"{title} ({len(entries)})", fill=(150, 200, 255), font=font)
    for index, (name, image) in enumerate(entries):
        x, y = (index % cols) * cell, 34 + (index // cols) * (cell + label)
        thumb = image.copy()
        thumb.thumbnail((cell - 16, cell - 16), Image.LANCZOS)
        canvas.paste(thumb, (x + (cell - thumb.size[0]) // 2,
                             y + (cell - thumb.size[1]) // 2), thumb)
        draw.text((x + 6, y + cell + 4), f"{index + 1}. {name}", fill=(190, 190, 200), font=font)
    canvas.save(out_path)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    REVIEW.mkdir(parents=True, exist_ok=True)
    manifest, report, grouped = [], [], {}
    for stem, group_name, mode, keep, names in SHEETS:
        tiles = run_sheet(stem, group_name, mode, keep, names, manifest, report)
        if tiles:
            grouped[group_name] = tiles

    (OUT / "manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")

    print(f"{'sheet':18}{'mode':9}{'pieces':>7}{'widest':>8}")
    for name, mode, count, widest in report:
        print(f"{name:18}{mode:9}{count:7}{widest:8}")

    for group_name, tiles in grouped.items():
        entries = [(m["name"], tile) for m, (tile, _box) in
                   zip([m for m in manifest if m["group"] == group_name], tiles)]
        montage(entries, REVIEW / f"{group_name}.png", group_name)
    print(f"icons={len(manifest)}")


main()
