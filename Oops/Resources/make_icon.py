"""Generate the oops icon: a keycap carrying both legends, Q and Й.

That is how the program converts — positionally: the key that gives Q in the
English layout gives Й in the Russian one. Both legends fit from 40 px up; the
tray sizes (16-32 px) cannot hold two letters, so they show the keycap with Й
alone. Й is cut through the keycap, so the tile's gradient shows in it.
Shapes are drawn oversampled and scaled down, but at small sizes the
letter is rendered at the target size itself: a downscaled glyph turns into a
blur, a hinted one stays sharp.

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
    # Small sizes give the keycap more of the tile: two pixels of margin are
    # all a 16 px icon can spare.
    m = s * (0.12 if small else 0.17)
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


def make(px: int) -> Image.Image:
    s = px * OVERSAMPLE
    if px >= 40:
        # Big enough for both glyphs to survive the downscale.
        cap = keycap(s, small=False)
        cut_out(cap, glyph_mask(s, "Й", s * 0.615, s * 0.585, s * 0.30))
        img = tile(s)
        img.alpha_composite(cap)
        ImageDraw.Draw(img).text((s * 0.385, s * 0.375), "Q", font=font(s * 0.30), fill=INK, anchor="mm")
        return img.resize((px, px), Image.LANCZOS)

    # Shapes oversampled, the glyph at native size and nudged up off the lip.
    cap = keycap(s, small=True).resize((px, px), Image.LANCZOS)
    cut_out(cap, glyph_mask(px, "Й", px / 2, px * 0.47, px * 0.62))
    img = tile(s).resize((px, px), Image.LANCZOS)
    img.alpha_composite(cap)
    return img


def main() -> None:
    frames = [make(px) for px in SIZES]
    # Every frame is passed explicitly: letting Pillow resize one master
    # would give exactly the blurred tray icon this script exists to avoid.
    frames[-1].save(OUT, format="ICO", sizes=[(px, px) for px in SIZES], append_images=frames[:-1])
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
