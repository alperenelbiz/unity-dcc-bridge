# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.7.0] - 2026-09-22

Closes the hero-prop path: a prop painted in Substance now reaches a prefab in Unity.

### Added

- `dcc_sp_materials` — builds a URP material per Substance texture set from the exported
  and packed maps: base colour, normal with `_NORMALMAP`, and the mask map in **both** the
  Metallic and Occlusion slots as URP expects.

  Materials are named after the texture set, which Substance took from the mesh's own
  material names, so a slot called `M_PropLeather` finds a material called `M_PropLeather`
  with no extra mapping. The corollary: a material comes either from the palette or from
  Substance, never both — remove its entry from `data/materials.json` once a set is painted.

### Fixed

- **Prefab slots silently fell back to the atlas material** for anything this package did not
  itself generate. A material painted in Substance, or authored by hand, exists on disk but
  was invisible to the prefab builder. It now resolves by name from disk when a generated one
  is not found.
- **The atlas material kept leftovers.** Exporting an *unpainted* Substance texture set
  produces maps that claim the same material name, leaving a normal and mask map attached to
  a material meant to be flat colour — lighting the whole set wrong with nothing to show for
  it. The atlas material now clears those slots and their keywords when it is rebuilt.

### Verified end to end

A chair modelled in Blender, exported, opened in Substance, painted with a smart material,
then exported, packed and wired: the prefab carries `M_PropAtlas` and `M_PropLeather` in the
right slots, the leather material binds base colour, normal (3160 distinct values from the
paint) and mask map, and keywords `_NORMALMAP`, `_METALLICSPECGLOSSMAP`, `_OCCLUSIONMAP` are
all set.

## [0.6.0] - 2026-09-22

The Substance export path, exercised against a real project — which immediately showed the
export alone was not usable in URP.

### Added

- `dcc_sp_pack_urp` — repacks a Substance export into the layout URP's Lit shader reads:
  R metallic, G occlusion, B unused, **A smoothness**, assigned to *both* the Metallic and
  Occlusion slots with sRGB off.

  Substance writes **roughness**; URP wants **smoothness**, its inverse. Handing a Substance
  export straight to URP makes every rough surface glossy and every glossy one rough, and
  nothing errors — which is why this is a pipeline step rather than a note in the docs.
  Verified: source roughness 77 becomes smoothness 178.

- **Import rules for Substance output.** Its `_BaseColor` / `_Normal` / `_Metallic` /
  `_Roughness` / `_Height` naming is not the `_N` convention the general rule looks for, so
  normal maps were importing as colour textures. Also silent, also wrong everywhere.

### Verified

Created a project remotely from an exported chair FBX; Substance picked up both material
names from the mesh (`M_PropAtlas`, `M_PropLeather`) as its texture sets. Exported 10
textures across both sets, then packed them into two URP mask maps with the channel maths
confirmed.

### Known limitations

- The export preset is still fixed to `PBR Metallic Roughness`. Resource search for the
  installed presets returns empty through the remote API, so the preset cannot yet be
  discovered or chosen — repacking afterwards is the reliable route.

## [0.5.0] - 2026-09-22

Substance 3D Painter is now driven. All three tools are wired.

### Added

- **Substance remote scripting bridge** over `POST localhost:60041/run.json`.
- `dcc_sp_status` — API version and the open project.
- `dcc_sp_export` — export the open project's texture sets.

### Protocol notes

Adobe's documentation page is not publicly reachable, so the protocol was established by
probing a running instance. Two behaviours are worth recording because neither is obvious:

- A **single expression** returns its value as JSON; a **multi-line script** runs but always
  answers `null`. Anything needing a result has to ask for it separately.
- The interpreter's **globals persist between calls**, which is what makes that split
  workable: run the script, then read the variable it left behind.
- Script errors arrive with **HTTP 200** and an error object in the body, so failure cannot
  be detected from the status code.

### Known limitations

- `dcc_sp_export` has only been exercised with **no project open**, where it correctly
  reports so. The actual export path is untested against a real Substance project.
- The export preset is fixed to `PBR Metallic Roughness`, Substance's metallic-roughness
  default. A URP project usually wants a metallic-smoothness preset instead; this should
  become configurable once the shape of a real export is known.

## [0.4.0] - 2026-09-22

Photoshop is now driven, not just detected.

### Added

- **Photoshop ExtendScript bridge.** Runs a script inside a live Photoshop and returns what
  it evaluated to — AppleScript on macOS, the COM automation object on Windows. Both are
  Adobe-supported and need nothing beyond Photoshop itself.
- `dcc_ps_describe` — report the open document, its size and layer count.
- `dcc_ps_export_layers` — export each top-level layer as its own PNG. This is the handoff
  the pipeline is built around: Photoshop authors a template once, its layers become flat
  images, and a generator composites from them thousands of times with Photoshop closed.
  Layer visibility is restored afterwards, so the artist's document is left as found.

### Changed

- **Photoshop readiness is now "is it running", not a port check.** The previous check
  probed port 3001, which belongs to a third-party plugin proxy rather than to Photoshop —
  it reported a bridge most users do not have. Photoshop has no remote-control server of its
  own; it is scripted through the OS, so the package no longer assumes one exists.

### Known limitations

- `com.unity.pipeline` times out a command at 30s. Photoshop's **first** call in a session
  can exceed that while macOS asks for Automation permission; approve the prompt and retry.
  A document-heavy export may also need more than 30s.
- Substance is detected and selectable, but no command drives it yet.

## [0.3.2] - 2026-09-22

### Fixed

- The setup prompt kept reappearing on a project that was already configured. `configured`
  was being AND-ed with a per-machine EditorPrefs flag, so the committed project setting
  could never satisfy it on its own. It is a project fact — once someone chooses which
  tools the project uses, a teammate cloning it should not be asked again — so it now comes
  from `ProjectSettings/DccBridge.json` alone. Install paths stay per machine, and a tool
  that is enabled but missing still shows as such.

### Verified

Full Blender path exercised end to end in LGS-Simulator through a git-URL install: seven
props exported, material slot order recorded, incremental skip honoured on a second run.

## [0.3.1] - 2026-09-22

First release actually installed into a separate project. Two bugs only a live install
could surface.

### Fixed

- **The package shipped no `.meta` files, so nothing in it loaded at all.** A UPM package
  is immutable: Unity cannot write meta files into the package cache, so any asset without
  one is never imported. The assembly definition was invisible, the assembly was never
  built, and not one command registered — while `package_resolve` and the compiler both
  reported success. Meta files are now committed, as every published Unity package does.
- Version labels for GUI applications showed the bundle instead of the install. Substance
  read as `Adobe Substance 3D Painter.app` when the version is in the folder name —
  `Substance 3D Painter 2023`. Photoshop kept a stray `.app`.

### Verified in LGS-Simulator

Installed by git URL, then exercised against a running Editor: all nine commands register;
the palette atlas is **byte-identical** to the Python implementation it replaced (0 of
262,144 pixels differ, UV lookup identical); detail maps, validation, materials, prefabs
and colliders all produce correct output; and `dcc_run_all` skips the Blender steps when
Blender is disabled instead of failing.

### Known limitations

- Detail map generation takes ~16s for a 512 tile. The Voronoi pass is brute force and has
  not been optimised.
- Photoshop and Substance are detected and selectable, but no command drives them yet.

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
