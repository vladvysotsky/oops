"""Generate oops icon — 'A|Я' letter swap on a blue gradient."""
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

OUT = Path(__file__).parent / "icon.ico"


def make(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded-rect background gradient (blue → purple).
    r = max(2, size // 6)
    for y in range(size):
        t = y / max(1, size - 1)
        # interpolate blue (0x2D6CDF) to purple (0x7438D6)
        col = (
            int(0x2D + (0x74 - 0x2D) * t),
            int(0x6C + (0x38 - 0x6C) * t),
            int(0xDF + (0xD6 - 0xDF) * t),
            255,
        )
        d.line([(0, y), (size, y)], fill=col)

    # Mask to rounded corners (cheap: redraw transparent corners).
    corner = Image.new("L", (size, size), 0)
    cd = ImageDraw.Draw(corner)
    cd.rounded_rectangle([(0, 0), (size, size)], radius=r, fill=255)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img, mask=corner)

    # Letters: "A" and "Я" with a diagonal slash separator.
    d2 = ImageDraw.Draw(out)
    font_size = int(size * 0.58)
    font = None
    for name in (
        "DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    ):
        try:
            font = ImageFont.truetype(name, font_size)
            break
        except OSError:
            continue
    if font is None:
        font = ImageFont.load_default()

    # "A" on the left, "Я" on the right.
    cx = size / 2
    cy = size / 2
    pad = size * 0.05
    # Left letter
    d2.text(
        (cx - pad, cy),
        "A",
        font=font,
        anchor="rm",
        fill=(255, 255, 255, 255),
    )
    # Right letter (Cyrillic)
    d2.text(
        (cx + pad, cy),
        "Я",
        font=font,
        anchor="lm",
        fill=(255, 255, 255, 255),
    )
    # Diagonal separator/swap accent.
    sw = max(2, size // 24)
    d2.line(
        [(cx - sw, cy - size * 0.32), (cx + sw, cy + size * 0.32)],
        fill=(255, 255, 255, 230),
        width=sw,
    )
    return out


def main() -> None:
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    base = make(256)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    base.save(OUT, format="ICO", sizes=sizes)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
