"""DCC Bridge generators and validators.

Lives inside the Unity package but always operates on a *consuming project*, so the
project root is passed in rather than inferred from this file's location:

    uv run --project <pkg>/Tools~/pipeline python cli.py <command> --project-root <unity project>

Unity calls this through ToolRunner; the arguments are the same by hand.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

TOOLS_ROOT = Path(__file__).resolve().parent.parent   # .../Tools~
BLENDER_SCRIPTS = TOOLS_ROOT / "blender"


def find_blender(explicit: str | None) -> Path:
    """Blender's location is passed in by Unity, which knows what the user configured.

    The fallbacks only cover running this script by hand. A Steam install is included
    because `which blender` and mdfind both miss it.
    """
    if explicit and Path(explicit).exists():
        return Path(explicit)

    override = os.environ.get("BLENDER_PATH")
    if override and Path(override).exists():
        return Path(override)

    candidates = [
        Path.home() / "Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender",
        Path("/Applications/Blender.app/Contents/MacOS/Blender"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate

    found = shutil.which("blender")
    if found:
        return Path(found)

    raise FileNotFoundError(
        "Blender not found. Enable and locate it in Project Settings > DCC Bridge, "
        "or set BLENDER_PATH.")


def run_blender(args: argparse.Namespace, script: str, extra: list[str]) -> int:
    blender = find_blender(args.blender)
    command = [
        str(blender), "--background", "--factory-startup",
        "--python", str(BLENDER_SCRIPTS / script), "--",
        "--job", str(Path(args.project_root) / "data" / "props.json"),
        *extra,
    ]

    proc = subprocess.run(command, capture_output=True, text=True)

    # Blender is noisy; only the scripts' own tagged lines are worth surfacing.
    for line in proc.stdout.splitlines():
        if line.startswith(("[export]", "[build]")) or line.strip().startswith(("warn:", "error:")):
            print(line)

    if proc.returncode != 0 and proc.stderr.strip():
        print(proc.stderr.strip()[-2000:], file=sys.stderr)

    return proc.returncode


def cmd_palette(args: argparse.Namespace) -> int:
    import palette

    return palette.build(Path(args.project_root))


def cmd_detail(args: argparse.Namespace) -> int:
    import detail_maps

    return detail_maps.build(Path(args.project_root))


def cmd_validate(args: argparse.Namespace) -> int:
    import validate

    return validate.run(Path(args.project_root))


def cmd_build_props(args: argparse.Namespace) -> int:
    extra = []
    if args.only:
        extra += ["--only", args.only]
    if args.force:
        extra += ["--force"]
    return run_blender(args, "build_props.py", extra)


def cmd_export_props(args: argparse.Namespace) -> int:
    extra = ["--out", str(Path(args.project_root) / "Assets" / "Art" / "Models")]
    if args.only:
        extra += ["--only", args.only]
    if args.force:
        extra += ["--force"]
    if args.strict:
        extra += ["--strict"]
    return run_blender(args, "blender_export.py", extra)


def main() -> int:
    parser = argparse.ArgumentParser(prog="dccbridge", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project-root", required=True, help="the Unity project to operate on")
    parser.add_argument("--blender", default=None, help="absolute path to the Blender executable")

    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("palette", help="generate the palette atlas and its UV lookup").set_defaults(func=cmd_palette)
    sub.add_parser("detail", help="generate seamless tiling detail maps").set_defaults(func=cmd_detail)
    sub.add_parser("validate", help="check manifests and asset naming").set_defaults(func=cmd_validate)

    p = sub.add_parser("build-props", help="build prop .blend sources from data/props.json")
    p.add_argument("--only", default=None)
    p.add_argument("--force", action="store_true")
    p.set_defaults(func=cmd_build_props)

    p = sub.add_parser("export-props", help="export prop FBX into Assets/Art/Models")
    p.add_argument("--only", default=None)
    p.add_argument("--force", action="store_true")
    p.add_argument("--strict", action="store_true")
    p.set_defaults(func=cmd_export_props)

    args = parser.parse_args()
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
