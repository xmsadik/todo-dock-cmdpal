"""Regenerate the package icons in src/TodoDock/Assets.

Usage: python scripts/make-icons.py   (needs Pillow)

The mark is a teal-blue rounded square with a white checkmark.
Everything is drawn at 1024 px and downsampled, so small sizes stay smooth.
"""

from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "TodoDock" / "Assets"
BASE = 1024
TILE = (14, 116, 144, 255)  # calm teal-blue
INK = (255, 255, 255, 255)


def stroke(d: ImageDraw.ImageDraw, points: list[tuple[float, float]], width: float) -> None:
    """Polyline with round caps and joins."""
    d.line(points, fill=INK, width=int(width), joint="curve")
    r = width / 2
    for x, y in points:
        d.ellipse((x - r, y - r, x + r, y + r), fill=INK)


def icon(size: int) -> Image.Image:
    img = Image.new("RGBA", (BASE, BASE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, BASE - 1, BASE - 1), radius=int(BASE * 0.22), fill=TILE)

    w = BASE * 0.11
    stroke(d, [(BASE * 0.26, BASE * 0.53), (BASE * 0.43, BASE * 0.70), (BASE * 0.75, BASE * 0.32)], w)

    return img.resize((size, size), Image.LANCZOS)


def centered(width: int, height: int, mark: int) -> Image.Image:
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    img.alpha_composite(icon(mark), ((width - mark) // 2, (height - mark) // 2))
    return img


def main() -> None:
    squares = {
        "StoreLogo.png": 50,
        "LockScreenLogo.scale-200.png": 48,
        "Square44x44Logo.scale-200.png": 88,
        "Square44x44Logo.targetsize-24_altform-unplated.png": 24,
        "Square150x150Logo.scale-200.png": 300,
    }
    for name, size in squares.items():
        icon(size).save(ASSETS / name)

    centered(620, 300, 200).save(ASSETS / "Wide310x150Logo.scale-200.png")
    centered(1240, 600, 360).save(ASSETS / "SplashScreen.scale-200.png")
    print(f"Wrote {len(squares) + 2} icons to {ASSETS}")


if __name__ == "__main__":
    main()
