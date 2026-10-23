"""Generate the oops icon: one keycap carrying both legends, Q and Й.

That is how the program converts — positionally: the key that gives Q in the
English layout gives Й in the Russian one. Both legends fit from 40 px up; the
tray sizes (16-32 px) cannot hold two letters, so they show the keycap with Й
alone. Shapes are drawn oversampled and scaled down, but at small sizes the
letter is rendered at the target size itself: a downscaled glyph turns into a
blur, a hinted one stays sharp.

    python make_icon.py        # needs Pillow; writes icon.ico next to it
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).parent / "icon.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OVERSAMPLE = 8

TILE_TOP = (0x3B, 0x7B, 0xF0)
TILE_BOTTOM = (0x24, 0x52, 0xC8)
KEY_FACE = (0xFF, 0xFF, 0xFF)
KEY_LIP = (0xC4, 0xD3, 0xEE)
INK = (0x1A, 0x1D, 0x23)
ACCENT = (0x2D, 0x6C, 0xDF)

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


def keycap(s: int, small: bool) -> Image.Image:
    """Blue tile with a white keycap, drawn at s pixels."""
    tile = Image.new("RGBA", (s, s))
    d = ImageDraw.Draw(tile)
    for y in range(s):
        t = y / (s - 1)
        d.line([(0, y), (s, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(TILE_TOP, TILE_BOTTOM)))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=round(s * 0.22), fill=255)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(tile, mask=mask)

    # Small sizes give the keycap more of the tile: two pixels of margin are
    # all a 16 px icon can spare.
    m = s * (0.12 if small else 0.17)
    lip = s * (0.09 if small else 0.075)
    r = round(s * 0.12)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([m, m, s - m, s - m], radius=r, fill=KEY_LIP)
    d.rounded_rectangle([m, m, s - m, s - m - lip], radius=r, fill=KEY_FACE)
    return img


def legend(img: Image.Image, text: str, cx: float, cy: float, size: float, color) -> None:
    ImageDraw.Draw(img).text((cx, cy), text, font=font(size), fill=color, anchor="mm")


def make(px: int) -> Image.Image:
    s = px * OVERSAMPLE
    if px >= 40:
        # Big enough for both glyphs to survive the downscale.
        img = keycap(s, small=False)
        legend(img, "Q", s * 0.385, s * 0.375, s * 0.30, INK)
        legend(img, "Й", s * 0.615, s * 0.585, s * 0.30, ACCENT)
        return img.resize((px, px), Image.LANCZOS)

    img = keycap(s, small=True).resize((px, px), Image.LANCZOS)
    # Native-size glyph, nudged up off the keycap's lip.
    legend(img, "Й", px / 2, px * 0.47, px * 0.62, ACCENT)
    return img


def main() -> None:
    frames = [make(px) for px in SIZES]
    # Every frame is passed explicitly: letting Pillow resize one master
    # would give exactly the blurred tray icon this script exists to avoid.
    frames[-1].save(OUT, format="ICO", sizes=[(px, px) for px in SIZES], append_images=frames[:-1])
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
