"""Headless Blender exporter for LGS-Simulator props.

Runs INSIDE Blender, never as a standalone script:

    blender --background --factory-startup \
        --python tools/blender/blender_export.py -- \
        --job data/props.json --out Assets/Art/Models [--only shelf_wall_a]

`--factory-startup` is deliberate: it ignores personal prefs and add-ons so a run on
this machine matches a run anywhere else.

The pipeline is stylized-atlas (see docs/art-pipeline-research.md §5), so there is no
bake stage. This script's job is validation and a Unity-correct FBX write.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from dataclasses import dataclass, field, asdict
from pathlib import Path

import bpy  # type: ignore[import-not-found]

# Unity-correct FBX settings. Together these land the mesh in Unity at scale
# (1,1,1) and rotation (0,0,0) instead of the 0.01 / -90deg default.
FBX_SETTINGS: dict[str, object] = {
    "use_selection": True,
    "apply_scale_options": "FBX_SCALE_ALL",
    "axis_forward": "-Z",
    "axis_up": "Y",
    "object_types": {"MESH", "EMPTY"},
    "use_mesh_modifiers": True,
    "mesh_smooth_type": "FACE",
    "bake_space_transform": True,
    "use_triangles": True,
    "use_tspace": True,
    "path_mode": "STRIP",  # never embed textures; Unity gets them from Assets/Art/Textures
    "global_scale": 1.0,
}


@dataclass
class PropResult:
    name: str
    source: str
    output: str | None = None
    triangles: int = 0
    materials: list[str] = field(default_factory=list)
    fingerprint: str = ""
    skipped: bool = False
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors


def parse_args(argv: list[str]) -> argparse.Namespace:
    # Blender passes everything after a bare "--" to the script.
    args = argv[argv.index("--") + 1 :] if "--" in argv else []
    p = argparse.ArgumentParser(prog="blender_export")
    p.add_argument("--job", required=True, help="props.json manifest")
    p.add_argument("--out", required=True, help="output dir for FBX (inside Assets/)")
    p.add_argument("--receipt", default=None, help="where to write the JSON receipt")
    p.add_argument("--only", default=None, help="export just this prop name")
    p.add_argument("--strict", action="store_true", help="treat warnings as errors")
    p.add_argument("--force", action="store_true", help="re-export even if the source is unchanged")
    return p.parse_args(args)


def reset_scene() -> None:
    """Start from an empty file so one prop's leftovers never leak into the next."""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def link_from_blend(blend_path: Path, collection_name: str) -> list[object]:
    """Append (not link) a collection from a .blend and return its mesh objects."""
    with bpy.data.libraries.load(str(blend_path), link=False) as (src, dst):
        if collection_name not in src.collections:
            available = ", ".join(src.collections) or "<none>"
            raise KeyError(f"collection '{collection_name}' not in {blend_path.name} (has: {available})")
        dst.collections = [collection_name]

    collection = bpy.data.collections[collection_name]
    bpy.context.scene.collection.children.link(collection)
    return [o for o in collection.all_objects if o.type == "MESH"]


def validate(obj, spec: dict, result: PropResult) -> None:
    """Everything that is cheap to check here and expensive to notice in Unity."""
    mesh = obj.data

    # Transforms must be applied. A non-1 scale reaching Unity means broken lightmaps,
    # broken physics and a prop that cannot be uniformly instanced.
    if not all(math.isclose(s, 1.0, abs_tol=1e-4) for s in obj.scale):
        result.errors.append(f"{obj.name}: scale {tuple(round(s, 4) for s in obj.scale)} is not applied")
    if not all(math.isclose(r, 0.0, abs_tol=1e-4) for r in obj.rotation_euler):
        result.warnings.append(f"{obj.name}: rotation is not applied")

    # Atlas pipeline: exactly one UV map, and it must stay inside the atlas.
    uv_layers = mesh.uv_layers
    if len(uv_layers) == 0:
        result.errors.append(f"{obj.name}: no UV map")
    elif len(uv_layers) > 1:
        result.warnings.append(f"{obj.name}: {len(uv_layers)} UV maps, only the active one is exported")

    atlas_material = spec.get("material", "M_PropAtlas")
    # A prop may declare extra materials for tiling detail surfaces. Anything not
    # declared is still an error - the point is that every material is deliberate.
    declared = spec.get("materials") or [atlas_material]

    slot_names = [slot.material.name for slot in obj.material_slots if slot.material]
    result.materials = slot_names

    if not slot_names:
        result.errors.append(f"{obj.name}: no material assigned")

    undeclared = sorted(set(slot_names) - set(declared))
    if undeclared:
        result.errors.append(
            f"{obj.name}: undeclared material(s) {', '.join(undeclared)}; "
            f"add them to this prop's \"materials\" in props.json")

    if uv_layers:
        # Only atlas faces must land inside [0,1]. Detail materials tile deliberately,
        # so their UVs run past the edge and clamping them would break the tiling.
        atlas_slots = {
            i for i, slot in enumerate(obj.material_slots)
            if slot.material and slot.material.name == atlas_material
        }
        uv_data = uv_layers.active.data
        out_of_bounds = 0
        for poly in mesh.polygons:
            if poly.material_index not in atlas_slots:
                continue
            for loop_index in poly.loop_indices:
                u, v = uv_data[loop_index].uv
                if not (-0.001 <= u <= 1.001 and -0.001 <= v <= 1.001):
                    out_of_bounds += 1
        if out_of_bounds:
            result.errors.append(f"{obj.name}: {out_of_bounds} atlas UV coords outside the [0,1] range")

    # Triangle budget.
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    result.triangles += tris
    budget = spec.get("tri_budget")
    if budget and tris > budget:
        result.errors.append(f"{obj.name}: {tris} tris over budget of {budget}")

    if not mesh.polygons:
        result.errors.append(f"{obj.name}: mesh has no faces")


def export_prop(spec: dict, blend_root: Path, out_dir: Path, strict: bool,
                previous: dict[str, dict], force: bool) -> PropResult:
    name = spec["name"]
    blend_path = blend_root / spec["blend"]
    result = PropResult(name=name, source=str(spec["blend"]))

    if not blend_path.exists():
        result.errors.append(f"source .blend not found: {blend_path}")
        return result

    result.fingerprint = fingerprint(blend_path, spec)
    out_path = out_dir / f"SM_{to_pascal(name)}.fbx"

    prior = previous.get(name)
    if not force and out_path.exists() and prior and prior.get("fingerprint") == result.fingerprint:
        result.output = str(out_path)
        result.skipped = True
        # Carry forward what the last real export measured; skipping does not re-measure it.
        result.materials = prior.get("materials", [])
        result.triangles = prior.get("triangles", 0)
        return result

    reset_scene()

    try:
        objects = link_from_blend(blend_path, spec.get("collection", "Export"))
    except KeyError as exc:
        result.errors.append(str(exc))
        return result

    if not objects:
        result.errors.append("export collection contains no mesh objects")
        return result

    for obj in objects:
        validate(obj, spec, result)

    if strict and result.warnings:
        result.errors.extend(f"(strict) {w}" for w in result.warnings)

    if result.errors:
        return result

    # Select exactly what we are exporting; FBX export uses the selection.
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

    out_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(out_path), **FBX_SETTINGS)

    result.output = str(out_path)
    return result


def fingerprint(blend_path: Path, spec: dict) -> str:
    """Identity of an export: the source bytes, its spec, and the export settings.

    FBX embeds a creation timestamp, so re-exporting an unchanged prop produces a
    byte-different file with identical geometry. That shows up as a dirty working tree
    and, with LFS, a brand new blob for every run. Skipping unchanged props avoids both
    and makes repeat runs effectively free.
    """
    h = hashlib.sha256()
    h.update(blend_path.read_bytes())
    h.update(json.dumps(spec, sort_keys=True).encode())
    h.update(json.dumps({k: sorted(v) if isinstance(v, set) else v for k, v in FBX_SETTINGS.items()}, sort_keys=True).encode())
    return h.hexdigest()


def load_previous(receipt_path: Path) -> dict[str, dict]:
    """Previous run's entries, keyed by prop name.

    The whole entry is kept, not just the fingerprint: a skipped export never runs
    validation, so anything measured there (material slot order, triangle count) has to
    be carried forward or the receipt silently loses it.
    """
    if not receipt_path.exists():
        return {}
    try:
        return {
            entry["name"]: entry
            for entry in json.loads(receipt_path.read_text()).get("props", [])
            if entry.get("output")
        }
    except (json.JSONDecodeError, TypeError, KeyError, AttributeError):
        # Also covers a receipt written by an older shape of this script.
        return {}


def to_pascal(snake: str) -> str:
    return "".join(part.capitalize() for part in snake.replace("-", "_").split("_") if part)


def main() -> int:
    args = parse_args(sys.argv)

    job_path = Path(args.job).resolve()
    job = json.loads(job_path.read_text())
    project_root = job_path.parent.parent
    blend_root = (project_root / job.get("blend_root", "art-source/blender")).resolve()
    out_dir = Path(args.out).resolve()

    specs = job["props"]
    if args.only:
        specs = [s for s in specs if s["name"] == args.only]
        if not specs:
            print(f"[export] no prop named '{args.only}' in {job_path.name}", file=sys.stderr)
            return 2

    receipt_path = Path(args.receipt) if args.receipt else project_root / ".pipeline-cache" / "props-receipt.json"
    previous = load_previous(receipt_path)

    results = [export_prop(spec, blend_root, out_dir, args.strict, previous, args.force) for spec in specs]

    for r in results:
        status = "skip" if r.skipped else "ok  " if r.ok else "FAIL"
        detail = "unchanged" if r.skipped else f"{r.triangles:>6} tris  {r.output or ''}"
        print(f"[export] {status} {r.name:<28} {detail}")
        for w in r.warnings:
            print(f"           warn: {w}")
        for e in r.errors:
            print(f"           error: {e}", file=sys.stderr)

    receipt_path.parent.mkdir(parents=True, exist_ok=True)
    receipt_path.write_text(json.dumps({"props": [asdict(r) for r in results]}, indent=2))

    failed = [r for r in results if not r.ok]
    skipped = sum(r.skipped for r in results)
    written = len(results) - len(failed) - skipped
    print(f"[export] {written} written, {skipped} unchanged, {len(failed)} failed -> {out_dir}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
