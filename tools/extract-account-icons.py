#!/usr/bin/env python3

from collections import deque
from pathlib import Path
import sys

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
PUBLIC_SOURCE = ROOT / "src/PocketLedger/wwwroot/images/account-icons/account-icons-source.png"
DEVELOPMENT_SOURCE = ROOT / "tools/development-assets/account-icons-source.png"
OUTPUT_ROOT = ROOT / "src/PocketLedger/wwwroot/images/account-icons"

ACCOUNT_TYPES = {
    "cash": 239,
    "bank-account": 402,
    "savings": 566,
    "credit-card": 730,
    "other": 894,
}

ICON_CENTERS_X = [459, 682, 910, 1138, 1364]
CELL_HALF_WIDTH = 96
CELL_HALF_HEIGHT = 72
CANVAS_SIZE = 256
ICON_SIZE = 220


def source_path() -> Path:
    if DEVELOPMENT_SOURCE.exists():
        return DEVELOPMENT_SOURCE
    if PUBLIC_SOURCE.exists():
        return PUBLIC_SOURCE
    raise FileNotFoundError("Account icon source sheet was not found.")


def is_background(pixel: tuple[int, int, int, int]) -> bool:
    red, green, blue, _ = pixel
    return min(red, green, blue) >= 238 and max(red, green, blue) - min(red, green, blue) <= 12


def remove_connected_background(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    width, height = rgba.size
    queue: deque[tuple[int, int]] = deque()
    visited: set[tuple[int, int]] = set()

    for x in range(width):
        queue.extend(((x, 0), (x, height - 1)))
    for y in range(height):
        queue.extend(((0, y), (width - 1, y)))

    while queue:
        x, y = queue.popleft()
        if (x, y) in visited or not is_background(pixels[x, y]):
            continue

        visited.add((x, y))
        red, green, blue, _ = pixels[x, y]
        whiteness = min(red, green, blue)
        alpha = max(0, min(255, (255 - whiteness) * 15))
        pixels[x, y] = (red, green, blue, alpha)

        if x > 0:
            queue.append((x - 1, y))
        if x + 1 < width:
            queue.append((x + 1, y))
        if y > 0:
            queue.append((x, y - 1))
        if y + 1 < height:
            queue.append((x, y + 1))

    return rgba


def normalize_icon(icon: Image.Image) -> Image.Image:
    alpha = icon.getchannel("A")
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError("The extracted icon is empty.")

    trimmed = icon.crop(bounds)
    trimmed.thumbnail((ICON_SIZE, ICON_SIZE), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (255, 255, 255, 0))
    position = ((CANVAS_SIZE - trimmed.width) // 2, (CANVAS_SIZE - trimmed.height) // 2)
    canvas.alpha_composite(trimmed, position)
    return canvas


def main() -> int:
    source = Image.open(source_path()).convert("RGBA")
    if source.size != (1536, 1024):
        raise ValueError(f"Unexpected source dimensions: {source.size}")

    generated = 0
    for account_type, center_y in ACCOUNT_TYPES.items():
        output_directory = OUTPUT_ROOT / account_type
        output_directory.mkdir(parents=True, exist_ok=True)

        for index, center_x in enumerate(ICON_CENTERS_X, start=1):
            crop_box = (
                center_x - CELL_HALF_WIDTH,
                center_y - CELL_HALF_HEIGHT,
                center_x + CELL_HALF_WIDTH,
                center_y + CELL_HALF_HEIGHT,
            )
            icon = normalize_icon(remove_connected_background(source.crop(crop_box)))
            icon.save(output_directory / f"{account_type}-{index}.png", optimize=True)
            generated += 1

    print(f"Generated {generated} account icons in {OUTPUT_ROOT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
