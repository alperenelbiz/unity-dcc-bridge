"""Manifest and asset-convention checks for a DCC Bridge project.

Runs with nothing open: no Unity, no Blender, no Photoshop. Cheap enough to put in a
pre-commit hook, which is the point - these are the mistakes that are silent until
something is broken in-engine.
"""

from __future__ import annotations

import json
import re
from pathlib import Path

ID_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
MODEL_RE = re.compile(r"^SM_[A-Z][A-Za-z0-9]*\.fbx$")
TEXTURE_RE = re.compile(r"^T_[A-Z][A-Za-z0-9]*\.(png|tga|jpg)$")
PREFAB_RE = re.compile(r"^P_[A-Z][A-Za-z0-9]*\.prefab$")
HEX_RE = re.compile(r"^#[0-9a-fA-F]{6}$")

VALID_COLLIDERS = {"box", "mesh", "none"}


def _check_palette(project_root: Path, errors: list[str], warnings: list[str]) -> set[str]:
    spec = json.loads((project_root / "data" / "palette.json").read_text())
    columns, rows = spec["atlas"]["columns"], spec["atlas"]["rows"]
    swatches = spec["swatches"]

    if len(swatches) > columns * rows:
        errors.append(f"palette.json: {len(swatches)} swatches exceed the {columns}x{rows} grid")

    names: set[str] = set()
    for swatch in swatches:
        name = swatch["name"]
        if name in names:
            errors.append(f"palette.json: duplicate swatch '{name}'")
        names.add(name)
        if not HEX_RE.match(swatch["color"]):
            errors.append(f"swatch '{name}': colour '{swatch['color']}' is not #rrggbb")

    atlas = project_root / "Assets" / "Art" / "Textures" / "Atlas" / "T_PaletteAtlas.png"
    if not atlas.exists():
        warnings.append("palette atlas not generated yet (run `pipeline palette`)")

    return names


def _check_props(project_root: Path, errors: list[str], warnings: list[str],
                 swatch_names: set[str]) -> set[str]:
    data = json.loads((project_root / "data" / "props.json").read_text())
    blend_root = project_root / data.get("blend_root", "art-source/blender")

    names: set[str] = set()
    for prop in data["props"]:
        name = prop["name"]
        if name in names:
            errors.append(f"props.json: duplicate prop name '{name}'")
        names.add(name)
        if not re.match(r"^[a-z0-9_]+$", name):
            errors.append(f"props.json: prop name '{name}' must be lowercase_snake")
        if not (blend_root / prop["blend"]).exists():
            warnings.append(f"prop '{name}': source not yet modelled ({prop['blend']})")

        size = prop.get("size")
        if not size or len(size) != 3 or any(v <= 0 for v in size):
            errors.append(f"prop '{name}': size must be three positive metres, got {size}")

        swatch = prop.get("swatch")
        if swatch and swatch not in swatch_names:
            errors.append(f"prop '{name}': unknown swatch '{swatch}'")

        collider = prop.get("collider", "box")
        if collider not in VALID_COLLIDERS:
            errors.append(f"prop '{name}': collider '{collider}' not in {sorted(VALID_COLLIDERS)}")

    return names


def _check_asset_naming(project_root: Path, errors: list[str], warnings: list[str]) -> None:
    models = project_root / "Assets" / "Art" / "Models"
    textures = project_root / "Assets" / "Art" / "Textures"

    for path in models.glob("*.fbx"):
        if not MODEL_RE.match(path.name):
            errors.append(f"model '{path.name}' does not match SM_PascalCase.fbx")

    prefabs = project_root / "Assets" / "Art" / "Prefabs"
    for path in prefabs.glob("*.prefab") if prefabs.exists() else []:
        if not PREFAB_RE.match(path.name):
            errors.append(f"prefab '{path.name}' does not match P_PascalCase.prefab")

    for path in textures.iterdir() if textures.exists() else []:
        if path.suffix.lower() in {".png", ".tga", ".jpg"} and not TEXTURE_RE.match(path.name):
            errors.append(f"texture '{path.name}' does not match T_PascalCase.<ext>")

    # Every committed asset needs its .meta or Unity regenerates a new GUID and
    # every reference to it breaks.
    for root in (models, textures, project_root / "Assets" / "Art" / "Cards",
                 project_root / "Assets" / "Art" / "Prefabs"):
        if not root.exists():
            continue
        for path in root.iterdir():
            if path.suffix == ".meta" or path.name.startswith("."):
                continue
            if not path.with_suffix(path.suffix + ".meta").exists():
                warnings.append(f"{path.relative_to(project_root)} has no .meta yet (open Unity once to generate it)")


def run(project_root: Path) -> int:
    errors: list[str] = []
    warnings: list[str] = []

    swatch_names = _check_palette(project_root, errors, warnings)
    _check_props(project_root, errors, warnings, swatch_names)
    _check_asset_naming(project_root, errors, warnings)

    for w in warnings:
        print(f"[validate] warn  {w}")
    for e in errors:
        print(f"[validate] ERROR {e}")

    print(f"[validate] {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0
