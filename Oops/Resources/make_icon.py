"""Generate the oops icon: "o!" in white on a blue-to-purple tile.

The name says it — "oops!" is what you say when the layout turns out wrong.
No letters of either alphabet: at 16 px in the tray they could not be read,
and two bold shapes can.

Shapes are drawn oversampled and scaled down. The 16 px frame is set pixel by
pixel instead: scaled down, the ring's stroke lands between pixel rows, turns
grey and runs into the "!" — and the tray icon is the one people look at.

    python make_icon.py        # needs Pillow; writes icon.ico next to it
"""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).parent / "icon.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OVERSAMPLE = 8

TILE_TOP = (0x2D, 0x6C, 0xDF)
TILE_BOTTOM = (0x74, 0x38, 0xD6)
MARK = (0xFF, 0xFF, 0xFF, 0xFF)


def tile(s: int) -> Image.Image:
    """Rounded tile with the gradient, drawn at s pixels."""
    grad = Image.new("RGBA", (s, s))
    d = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / (s - 1)
        d.line([(0, y), (s, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(TILE_TOP, TILE_BOTTOM)))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=round(s * 0.22), fill=255)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(grad, mask=mask)
    return img


def mark(img: Image.Image, s: int) -> None:
    """The "o!" itself, in proportions of the tile."""
    d = ImageDraw.Draw(img)
    # The ring's outer edge and the bottom of the "!" dot share a baseline.
    ring, stroke = s * 0.19, s * 0.12
    cx, cy = s * 0.40, s * 0.56
    outer = ring + stroke / 2
    d.ellipse([cx - outer, cy - outer, cx + outer, cy + outer], outline=MARK, width=round(stroke))

    bang_x, bang_w = s * 0.74, s * 0.12
    d.rounded_rectangle([bang_x - bang_w / 2, s * 0.20, bang_x + bang_w / 2, s * 0.62],
                        radius=round(bang_w / 2), fill=MARK)
    dot = bang_w * 0.6
    d.ellipse([bang_x - dot, cy + outer - 2 * dot, bang_x + dot, cy + outer], fill=MARK)


# 16 px: "#" is white, "." is the tile.
PIXEL_16 = [
    "................",
    "................",
    "................",
    "...........##...",
    "...........##...",
    "....####...##...",
    "...######..##...",
    "..##....##.##...",
    "..##....##.##...",
    "..##....##.##...",
    "..##....##......",
    "...######..##...",
    "....####...##...",
    "................",
    "................",
    "................",
]


def make16() -> Image.Image:
    img = tile(16 * OVERSAMPLE).resize((16, 16), Image.LANCZOS)
    for y, row in enumerate(PIXEL_16):
        for x, ch in enumerate(row):
            if ch == "#":
                img.putpixel((x, y), MARK)
    return img


def make(px: int) -> Image.Image:
    if px == 16:
        return make16()
    s = px * OVERSAMPLE
    img = tile(s)
    mark(img, s)
    return img.resize((px, px), Image.LANCZOS)


def main() -> None:
    frames = [make(px) for px in SIZES]
    # Every frame is passed explicitly: letting Pillow resize one master
    # would undo the hand-set 16 px frame.
    frames[-1].save(OUT, format="ICO", sizes=[(px, px) for px in SIZES], append_images=frames[:-1])
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
