"""Generate the oops icon: a keycap carrying both legends, Q and Й, in white.

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

# Blue to purple, the gradient the first icon had.
TILE_TOP = (0x2D, 0x6C, 0xDF)
TILE_BOTTOM = (0x74, 0x38, 0xD6)
KEY_LIP = (0x4A, 0x22, 0x9C)
INK = (0xFF, 0xFF, 0xFF)

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
    """The whole tile is the keycap: a gradient face over a darker lip."""
    lip = round(s * (0.07 if small else 0.06))
    r = round(s * 0.22)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(img).rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=KEY_LIP)

    face_h = s - lip
    grad = Image.new("RGBA", (s, face_h))
    d = ImageDraw.Draw(grad)
    for y in range(face_h):
        t = y / (face_h - 1)
        d.line([(0, y), (s, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(TILE_TOP, TILE_BOTTOM)))
    mask = Image.new("L", (s, face_h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, face_h - 1], radius=r, fill=255)
    img.paste(grad, (0, 0), mask)
    return img


def legend(img: Image.Image, text: str, cx: float, cy: float, size: float, color) -> None:
    ImageDraw.Draw(img).text((cx, cy), text, font=font(size), fill=color, anchor="mm")


def make(px: int) -> Image.Image:
    s = px * OVERSAMPLE
    if px >= 40:
        # Big enough for both glyphs to survive the downscale.
        img = keycap(s, small=False)
        legend(img, "Q", s * 0.34, s * 0.33, s * 0.38, INK)
        legend(img, "Й", s * 0.66, s * 0.61, s * 0.38, INK)
        return img.resize((px, px), Image.LANCZOS)

    img = keycap(s, small=True).resize((px, px), Image.LANCZOS)
    # Native-size glyph, nudged up off the keycap's lip.
    legend(img, "Й", px / 2, px * 0.46, px * 0.72, INK)
    return img


def main() -> None:
    frames = [make(px) for px in SIZES]
    # Every frame is passed explicitly: letting Pillow resize one master
    # would give exactly the blurred tray icon this script exists to avoid.
    frames[-1].save(OUT, format="ICO", sizes=[(px, px) for px in SIZES], append_images=frames[:-1])
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
