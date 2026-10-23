"""Generate the oops icon: a keycap carrying both legends, Q and Й.

That is how the program converts — positionally: the key that gives Q in the
English layout gives Й in the Russian one. Both legends are on every size,
the 16 px tray icon included — the author's requirement. Й is cut through the
keycap, so the tile's gradient shows in it. Shapes are drawn oversampled and
scaled down, but at small sizes the letters are rendered at the target size
itself: a downscaled glyph turns into a blur, a hinted one stays sharp.

    python make_icon.py        # needs Pillow; writes icon.ico next to it
"""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

OUT = Path(__file__).parent / "icon.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OVERSAMPLE = 8

# Blue to purple, the gradient the first icon had.
TILE_TOP = (0x2D, 0x6C, 0xDF)
TILE_BOTTOM = (0x74, 0x38, 0xD6)
KEY_FACE = (0xFF, 0xFF, 0xFF)
KEY_LIP = (0xD0, 0xC8, 0xF0)
INK = (0x1A, 0x1D, 0x23)

FONTS = (
    "segoeuib.ttf",                                        # Windows
    "C:/Windows/Fonts/segoeuib.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "DejaVuSans-Bold.ttf",
)


def font(size: float) -> ImageFont.FreeTypeFont:
    for name in FONTS:
        try:
            return ImageFont.truetype(name, max(1, round(size)))
        except OSError:
            continue
    raise SystemExit("no bold font found; install DejaVu Sans or run on Windows")


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


def keycap(s: int, small: bool) -> Image.Image:
    """White keycap with a lip, on a transparent layer, drawn at s pixels."""
    # Small sizes give the keycap more of the tile: two letters on a
    # 16 px icon need every pixel.
    m = s * (0.10 if small else 0.17)
    lip = s * (0.09 if small else 0.075)
    r = round(s * 0.12)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([m, m, s - m, s - m], radius=r, fill=KEY_LIP)
    d.rounded_rectangle([m, m, s - m, s - m - lip], radius=r, fill=KEY_FACE)
    return img


def glyph_mask(s: int, text: str, cx: float, cy: float, size: float) -> Image.Image:
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).text((cx, cy), text, font=font(size), fill=255, anchor="mm")
    return mask


def cut_out(layer: Image.Image, mask: Image.Image) -> None:
    """Punch the glyph through the keycap, so the tile shows in the letter."""
    alpha = layer.getchannel("A")
    layer.putalpha(ImageChops.subtract(alpha, mask))


# The 16 px tray icon is drawn pixel by pixel. No font survives being set
# six pixels tall: bold strokes merge (Й read as М), regular ones blur.
PIXEL_Q = [
    ".###.",
    "#...#",
    "#...#",
    "#...#",
    "#.#.#",
    "#..#.",
    ".##.#",
]
PIXEL_J = [
    ".###.",
    ".....",
    "#...#",
    "#..##",
    "#.#.#",
    "##..#",
    "#...#",
]


def pixels(rows, x0: int, y0: int):
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == "#":
                yield x0 + x, y0 + y


def make16() -> Image.Image:
    px = 16
    img = tile(px * OVERSAMPLE).resize((px, px), Image.LANCZOS)
    cap = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    d = ImageDraw.Draw(cap)
    d.rectangle([1, 1, 14, 13], fill=KEY_LIP)
    d.rectangle([1, 1, 14, 12], fill=KEY_FACE)
    for corner in [(1, 1), (14, 1), (1, 13), (14, 13)]:
        cap.putpixel(corner, (0, 0, 0, 0))
    for xy in pixels(PIXEL_J, 8, 5):
        cap.putpixel(xy, (0, 0, 0, 0))
    img.alpha_composite(cap)
    for xy in pixels(PIXEL_Q, 2, 2):
        img.putpixel(xy, INK + (255,))
    return img


def make(px: int) -> Image.Image:
    if px == 16:
        return make16()
    s = px * OVERSAMPLE
    if px >= 32:
        # Big enough for both glyphs to survive the downscale.
        cap = keycap(s, small=False)
        cut_out(cap, glyph_mask(s, "Й", s * 0.615, s * 0.585, s * 0.30))
        img = tile(s)
        img.alpha_composite(cap)
        ImageDraw.Draw(img).text((s * 0.385, s * 0.375), "Q", font=font(s * 0.30), fill=INK, anchor="mm")
        return img.resize((px, px), Image.LANCZOS)

    # Shapes oversampled, the glyphs at native size: a downscaled glyph blurs,
    # a hinted one stays sharp. Both letters stay even at 16 px, by the
    # author's decision — the pair IS the icon.
    cap = keycap(s, small=True).resize((px, px), Image.LANCZOS)
    cut_out(cap, glyph_mask(px, "Й", px * 0.68, px * 0.58, px * 0.46))
    img = tile(s).resize((px, px), Image.LANCZOS)
    img.alpha_composite(cap)
    ImageDraw.Draw(img).text((px * 0.33, px * 0.36), "Q", font=font(px * 0.44), fill=INK, anchor="mm")
    return img


def main() -> None:
    frames = [make(px) for px in SIZES]
    # Every frame is passed explicitly: letting Pillow resize one master
    # would give exactly the blurred tray icon this script exists to avoid.
    frames[-1].save(OUT, format="ICO", sizes=[(px, px) for px in SIZES], append_images=frames[:-1])
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
