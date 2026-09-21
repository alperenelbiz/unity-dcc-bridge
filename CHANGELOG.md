# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-09-22

Removes the external Python requirement and completes the Unity-side assembly. A project
with **no DCC tools installed at all** now has a working pipeline.

### Changed — breaking

- **Python (uv) is no longer a tool.** Its only real job was writing PNGs; numpy already
  ships inside Blender and the rest was plain Python. The generators are C# now, so nothing
  outside Unity has to be installed to use them. Projects that enabled Python in
  `ProjectSettings/DccBridge.json` will simply stop listing it — no migration needed, and
  the enum values of the remaining tools are unchanged.
- `Tools~/pipeline` is gone. The Python that remains runs inside Blender's own interpreter,
  where `bpy` leaves no alternative.

### Added

- **Material assembly.** The atlas material is built from the palette. Detail materials —
  the ones carrying a real tiling texture rather than a flat swatch — are declared per
  project in the optional `data/materials.json`, so no material name is baked into the package.
- **Prefab assembly.** One placeable prefab per prop: mesh, materials in the slot order the
  exporter recorded, and a collider sized from mesh bounds. An FBX cannot carry a collider,
  so anything downstream should reference the prefab.
- **Import rules** applied by path convention: point-filtered unmipped palette atlas,
  repeating linear detail maps, and models that keep the scale and axes the exporter baked.
- Commands `dcc_materials` and `dcc_prefabs`, and both added to `dcc_run_all`.

### Known limitations

- Photoshop and Substance are detected and selectable, but no command drives them yet.
- The package has not yet been installed into a separate project end to end; every claim
  above is from compilation and code review, not from a live run.

## [0.2.0] - 2026-09-21

Adds Substance 3D Painter as a selectable tool, and teaches the capability system the
difference between a tool being installed and being reachable.

### Added

- **Substance 3D Painter** in the tool list, detected across Adobe and Steam installs on
  macOS and Windows.
- **Connection state for socket-driven tools.** Photoshop and Substance are not launched as
  executables; they are driven over a local port (3001 and 60041). Both stay silent unless
  started with remote control enabled, so "installed" and "usable" are now separate states.
  The UI reports `not connected` with the specific fix — for Substance, relaunch with
  `--enable-remote-scripting`.

### Fixed

- Substance detection missed real installs. Adobe names its folder
  `Adobe Substance 3D Painter <year>` while Steam uses `Substance 3D Painter <year>`, and
  some installs carry no year at all. Found by probing an actual machine rather than
  trusting the documented path.

### Known limitations

- Substance is detected and selectable, but no command drives it yet. Same for Photoshop.
  Both are groundwork for the hero-prop path: Blender exports low and high poly, Substance
  paints, Unity receives a metallic-smoothness set.
- Material, prefab and collider assembly is still not in the package.

## [0.1.0] - 2026-09-21

First release. Establishes the capability model and the generator pipeline.

### Added

- **Tool capability system.** Blender, Photoshop and Python are each optional. A setup
  window opens on first run, tools are auto-detected per platform, and every feature that
  needs a missing tool is hidden from the menu and reports clearly when invoked.
  Which tools a project expects is committed; where they are installed is not.
- **Palette atlas generation** from `data/palette.json`, producing both the texture and the
  UV lookup Blender snaps islands onto, so the image and the coordinates cannot drift apart.
- **Seamless detail normal maps** from periodic Voronoi measured on a torus, tiling by
  construction rather than by hand-fixing seams. Output is OpenGL +Y, matching Unity.
- **Blender prop authoring and export.** Props are described as code with `propkit`, or fall
  back to a sized blockout. Export validates applied scale, UV bounds per material, declared
  materials and triangle budget, and refuses to write an FBX that would arrive broken.
- **Incremental export** fingerprinted on source bytes plus settings, so unchanged props are
  skipped and FBX timestamps stop dirtying the working tree on every run.
- **CLI commands** registered through `com.unity.pipeline`, so an agent or CI job can drive a
  *running* Editor: `dcc_status`, `dcc_palette`, `dcc_detail`, `dcc_validate`,
  `dcc_build_props`, `dcc_export_props`, `dcc_run_all`.

### Known limitations

- Material, prefab and collider assembly is not in this release; it is the focus of 0.2.0.
- Photoshop is detected and selectable but no command drives it yet.
- `com.unity.pipeline` is an experimental Unity package. Its API may change between Unity
  versions, which would require a matching release here.
