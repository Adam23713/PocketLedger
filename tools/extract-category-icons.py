#!/usr/bin/env python3

from collections import deque
from pathlib import Path
import sys

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
PUBLIC_SOURCE = ROOT / "src/PocketLedger/wwwroot/images/category-icons/category-icons-source.png"
DEVELOPMENT_SOURCE = ROOT / "tools/development-assets/category-icons-source.png"
OUTPUT_ROOT = ROOT / "src/PocketLedger/wwwroot/images/category-icons"

COLUMN_CENTERS = {
    "left": [190, 264, 336, 407, 472],
    "middle": [679, 750, 823, 895, 968],
    "right": [1180, 1252, 1324, 1396, 1467],
}

GROUPS = {
    "food": ("left", 0),
    "travel": ("left", 1),
    "phone": ("left", 8),
    "fuel": ("left", 10),
    "coffee": ("left", 11),
    "shopping": ("left", 12),
    "parking": ("left", 13),
    "cinema": ("left", 14),
    "car-service": ("middle", 0),
    "bills": ("middle", 1),
    "transportation": ("middle", 2),
    "health": ("middle", 3),
    "insurance": ("middle", 4),
    "gift": ("middle", 7),
    "entertainment": ("middle", 8),
    "accommodation": ("middle", 9),
    "internet": ("middle", 10),
    "streaming": ("middle", 11),
    "other-expense": ("middle", 14),
    "bank": ("right", 6),
    "salary": ("right", 9),
    "interest": ("right", 11),
    "investment": ("right", 12),
    "cashback": ("right", 13),
    "other-income": ("right", 14),
}

FIRST_ROW_CENTER_Y = 96
ROW_HEIGHT = 58
CELL_HALF_WIDTH = 29
CELL_HALF_HEIGHT = 26
CANVAS_SIZE = 256
ICON_SIZE = 216


def source_path() -> Path:
    if DEVELOPMENT_SOURCE.exists():
        return DEVELOPMENT_SOURCE
    if PUBLIC_SOURCE.exists():
        return PUBLIC_SOURCE
    raise FileNotFoundError("Category icon source sheet was not found.")


def is_background(pixel: tuple[int, int, int, int]) -> bool:
    red, green, blue, _ = pixel
    return min(red, green, blue) >= 238 and max(red, green, blue) - min(red, green, blue) <= 14


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
    visible_alpha = icon.getchannel("A").point(lambda alpha: 255 if alpha >= 48 else 0)
    bounds = visible_alpha.getbbox()
    if bounds is None:
        raise ValueError("The extracted icon is empty.")

    trimmed = icon.crop(bounds)
    scale = min(ICON_SIZE / trimmed.width, ICON_SIZE / trimmed.height)
    resized_size = (max(1, round(trimmed.width * scale)), max(1, round(trimmed.height * scale)))
    trimmed = trimmed.resize(resized_size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (255, 255, 255, 0))
    position = ((CANVAS_SIZE - trimmed.width) // 2, (CANVAS_SIZE - trimmed.height) // 2)
    canvas.alpha_composite(trimmed, position)
    return canvas


def main() -> int:
    source = Image.open(source_path()).convert("RGBA")
    if source.size != (1536, 1024):
        raise ValueError(f"Unexpected source dimensions: {source.size}")

    generated = 0
    for group, (column, row) in GROUPS.items():
        center_y = FIRST_ROW_CENTER_Y + row * ROW_HEIGHT
        output_directory = OUTPUT_ROOT / group
        output_directory.mkdir(parents=True, exist_ok=True)

        for index, center_x in enumerate(COLUMN_CENTERS[column], start=1):
            crop_box = (
                center_x - CELL_HALF_WIDTH,
                center_y - CELL_HALF_HEIGHT,
                center_x + CELL_HALF_WIDTH,
                center_y + CELL_HALF_HEIGHT,
            )
            icon = normalize_icon(remove_connected_background(source.crop(crop_box)))
            icon.save(output_directory / f"{group}-{index}.png", optimize=True)
            generated += 1

    print(f"Generated {generated} category icons in {OUTPUT_ROOT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
