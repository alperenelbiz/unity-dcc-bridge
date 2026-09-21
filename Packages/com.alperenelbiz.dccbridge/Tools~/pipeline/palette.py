"""Palette atlas generation.

The atlas is generated from `data/palette.json`, not hand-painted, because Blender needs
to know exactly where each swatch sits in order to snap UV islands onto it. Defining the
palette as data means the image and the UV lookup can never drift apart, and recolouring
the whole game is a JSON edit.

Photoshop's job here is deciding the colours, not storing them.

Outputs:
  Assets/Art/Textures/Atlas/T_PaletteAtlas.png   the texture Unity imports
  art-source/blender/_lib/palette_uv.json        swatch -> UV centre, for Blender
"""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw

ATLAS_PATH = Path("Assets/Art/Textures/Atlas/T_PaletteAtlas.png")
UV_PATH = Path("art-source/blender/_lib/palette_uv.json")
SHEET_PATH = Path("art-source/blender/_lib/palette_reference.png")


def hex_to_rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


def swatch_uv(index: int, columns: int, rows: int) -> tuple[float, float]:
    """UV centre of a swatch cell.

    Pillow draws row 0 at the top; Blender's V axis runs bottom-up. The flip below is
    the only place that conversion happens, so a mismatch shows up here and nowhere else.
    """
    col, row = index % columns, index // columns
    u = (col + 0.5) / columns
    v = 1.0 - (row + 0.5) / rows
    return round(u, 6), round(v, 6)


def build(project_root: Path) -> int:
    spec = json.loads((project_root / "data" / "palette.json").read_text())
    size = spec["atlas"]["size"]
    columns, rows = spec["atlas"]["columns"], spec["atlas"]["rows"]
    swatches = spec["swatches"]

    if len(swatches) > columns * rows:
        print(f"[palette] ERROR {len(swatches)} swatches will not fit a {columns}x{rows} grid")
        return 1

    cell = size // columns
    # Magenta base: any UV that misses a defined swatch is then obvious in-engine
    # rather than quietly picking up a neighbouring colour.
    atlas = Image.new("RGB", (size, size), (255, 0, 255))
    draw = ImageDraw.Draw(atlas)

    uv_lookup: dict[str, dict] = {}

    for index, swatch in enumerate(swatches):
        col, row = index % columns, index // columns
        x0, y0 = col * cell, row * cell
        draw.rectangle([x0, y0, x0 + cell - 1, y0 + cell - 1], fill=hex_to_rgb(swatch["color"]))

        u, v = swatch_uv(index, columns, rows)
        uv_lookup[swatch["name"]] = {"u": u, "v": v, "color": swatch["color"], "index": index}

    atlas_path = project_root / ATLAS_PATH
    atlas_path.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(atlas_path, "PNG", optimize=True)

    uv_path = project_root / UV_PATH
    uv_path.parent.mkdir(parents=True, exist_ok=True)
    uv_path.write_text(json.dumps({"atlas": spec["atlas"], "swatches": uv_lookup}, indent=2) + "\n")

    _write_reference_sheet(project_root, spec, cell)

    print(f"[palette] {len(swatches)} swatches -> {ATLAS_PATH}")
    print(f"[palette] UV lookup -> {UV_PATH}")
    return 0


def _write_reference_sheet(project_root: Path, spec: dict, cell: int) -> None:
    """A labelled version for humans. Never imported by Unity - it lives in art-source."""
    columns = spec["atlas"]["columns"]
    swatches = spec["swatches"]
    rows_used = (len(swatches) + columns - 1) // columns

    pad, label_h = 12, 26
    tile = cell + pad
    sheet = Image.new("RGB", (columns * tile + pad, rows_used * (tile + label_h) + pad), (24, 24, 26))
    draw = ImageDraw.Draw(sheet)

    for index, swatch in enumerate(swatches):
        col, row = index % columns, index // columns
        x = pad + col * tile
        y = pad + row * (tile + label_h)
        draw.rectangle([x, y, x + cell, y + cell], fill=hex_to_rgb(swatch["color"]))
        draw.text((x, y + cell + 4), swatch["name"], fill=(200, 200, 205))
        draw.text((x, y + cell + 15), swatch["color"], fill=(130, 130, 136))

    out = project_root / SHEET_PATH
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, "PNG", optimize=True)
