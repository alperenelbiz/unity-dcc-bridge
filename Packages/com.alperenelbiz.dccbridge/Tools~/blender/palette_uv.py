"""Snap UV islands onto named palette swatches.

Imported by scripts running inside Blender. In the atlas pipeline a UV island does not
need to be unwrapped well - it needs to sit on the right colour cell. That makes
"texturing" a prop a matter of naming a swatch, which is scriptable.

The UV lookup is generated alongside the atlas image by `pipeline palette`, so the two
can never disagree about where a colour lives.
"""

from __future__ import annotations

import json
from pathlib import Path

import bmesh  # type: ignore[import-not-found]
import bpy  # type: ignore[import-not-found]

LOOKUP_RELATIVE = Path("art-source/blender/_lib/palette_uv.json")


def load_lookup(project_root: Path) -> dict[str, dict]:
    path = project_root / LOOKUP_RELATIVE
    if not path.exists():
        raise FileNotFoundError(f"{path} missing - run `pipeline palette` first")
    return json.loads(path.read_text())["swatches"]


def assign_swatch(obj, swatch_name: str, lookup: dict[str, dict], material_slot: int | None = None) -> int:
    """Collapse every UV of `obj` onto one swatch's centre.

    Returns the number of loops moved. Pass `material_slot` to affect only the faces
    using that slot, which is how a prop gets more than one colour.
    """
    if swatch_name not in lookup:
        raise KeyError(f"unknown swatch '{swatch_name}' - known: {', '.join(sorted(lookup))}")

    u, v = lookup[swatch_name]["u"], lookup[swatch_name]["v"]
    mesh = obj.data

    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")

    uv_layer = mesh.uv_layers.active
    moved = 0

    for poly in mesh.polygons:
        if material_slot is not None and poly.material_index != material_slot:
            continue
        for loop_index in poly.loop_indices:
            uv_layer.data[loop_index].uv = (u, v)
            moved += 1

    return moved


def assign_swatch_to_selection(obj, swatch_name: str, lookup: dict[str, dict]) -> int:
    """Same, but only for faces currently selected in Edit Mode.

    This is the interactive path: select the faces you want a colour on, run it, repeat.
    """
    if swatch_name not in lookup:
        raise KeyError(f"unknown swatch '{swatch_name}'")

    u, v = lookup[swatch_name]["u"], lookup[swatch_name]["v"]

    was_edit = obj.mode == "EDIT"
    if not was_edit:
        bpy.ops.object.mode_set(mode="EDIT")

    bm = bmesh.from_edit_mesh(obj.data)
    uv_layer = bm.loops.layers.uv.verify()
    moved = 0

    for face in bm.faces:
        if not face.select:
            continue
        for loop in face.loops:
            loop[uv_layer].uv = (u, v)
            moved += 1

    bmesh.update_edit_mesh(obj.data)
    if not was_edit:
        bpy.ops.object.mode_set(mode="OBJECT")

    return moved
