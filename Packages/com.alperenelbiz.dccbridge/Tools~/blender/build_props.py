"""Build prop .blend sources from data/props.json.

Two kinds of prop:

  builder   an authored Python script under art-source/blender/scripts/ that
            constructs real geometry. The asset is source: reviewable in a diff,
            regenerable, and editable without reverse-engineering someone's
            modelling session.
  blockout  no builder yet, so a correctly-sized box stands in. Same structure
            the exporter validates, so the pipeline runs before anything is modelled.

    blender --background --factory-startup \
        --python tools/blender/build_props.py -- --job data/props.json [--only chair_gaming] [--force]
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import palette_uv  # noqa: E402
import propkit  # noqa: E402

DEFAULT_SIZE = (0.5, 0.5, 0.5)


def parse_args(argv: list[str]) -> argparse.Namespace:
    args = argv[argv.index("--") + 1 :] if "--" in argv else []
    p = argparse.ArgumentParser(prog="build_props")
    p.add_argument("--job", required=True)
    # Passed explicitly by Unity. Inferring it from the manifest's location breaks as soon
    # as a project keeps its data folder somewhere other than alongside Assets/.
    p.add_argument("--project-root", dest="project_root", default=None)
    p.add_argument("--only", default=None)
    p.add_argument("--force", action="store_true", help="rebuild props whose .blend exists")
    return p.parse_args(args)


def load_builder(path: Path):
    spec = importlib.util.spec_from_file_location(path.stem, path)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load builder {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not hasattr(module, "build"):
        raise AttributeError(f"{path.name} has no build(kit, prop) function")
    return module


def build_one(prop: dict, project_root: Path, blend_root: Path, defaults: dict, lookup: dict) -> str:
    name = prop["name"]
    material = prop.get("material", defaults.get("material", "M_PropAtlas"))
    kit = propkit.PropBuilder(name, lookup, material)

    builder_rel = prop.get("builder")
    if builder_rel:
        module = load_builder(project_root / "art-source" / "blender" / builder_rel)
        module.build(kit, prop)
        kind = "authored"
    else:
        size = tuple(prop.get("size", DEFAULT_SIZE))
        kit.box(size=size, location=(0, 0, size[2] / 2),
                swatch=prop.get("swatch", defaults.get("swatch", "laminate")))
        kit.finish()
        kind = "blockout"

    propkit.save(blend_root / prop["blend"])
    return kind


def main() -> int:
    args = parse_args(sys.argv)
    job_path = Path(args.job).resolve()
    job = json.loads(job_path.read_text())
    project_root = Path(args.project_root).resolve() if args.project_root else job_path.parent.parent
    blend_root = project_root / job.get("blend_root", "art-source/blender")
    defaults = job.get("defaults", {})
    lookup = palette_uv.load_lookup(project_root)

    specs = job["props"]
    if args.only:
        specs = [s for s in specs if s["name"] == args.only]
        if not specs:
            print(f"[build] no prop named '{args.only}'", file=sys.stderr)
            return 2

    built = 0
    for prop in specs:
        out_path = blend_root / prop["blend"]
        if out_path.exists() and not args.force:
            print(f"[build] skip {prop['name']} (exists)")
            continue
        try:
            kind = build_one(prop, project_root, blend_root, defaults, lookup)
        except Exception as exc:  # a bad builder must not take the whole run down
            print(f"[build] FAIL {prop['name']}: {exc}", file=sys.stderr)
            continue
        print(f"[build] {kind:<9} {prop['name']:<20} -> {out_path.relative_to(project_root)}")
        built += 1

    print(f"[build] {built} built")
    return 0


if __name__ == "__main__":
    sys.exit(main())
