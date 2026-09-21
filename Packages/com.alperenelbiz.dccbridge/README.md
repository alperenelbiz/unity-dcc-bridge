# DCC Bridge

Builds game-ready assets in Unity — palette atlases, seamless detail maps, validation, and
material, prefab and collider assembly — and drives Blender, Photoshop and Substance 3D
Painter when you have them.

**Every tool is optional.** Not everyone on a team can run Blender or owns Photoshop, so
the pipeline adapts: you pick what you have, and only those features appear.

## Requirements

- Unity 6000.0 or newer
- `com.unity.pipeline` (installed automatically as a dependency)
- Optional: Blender 4.2+, Photoshop, Substance 3D Painter

**Nothing outside Unity is required.** With no DCC tool installed you still get palette
atlases, detail maps, validation, materials, prefabs and colliders. Each tool you add
unlocks more.

### How the tools differ

Blender runs **headless** — driven in batch with no window, so it can do bulk work.

Photoshop and Substance do **not**. Photoshop is scripted through the OS against a running
application, so it has to be open. Substance listens on port 60041, but only when launched
with `--enable-remote-scripting`. That makes them the place to author one of a kind, not to produce a
thousand: use them to design a template or paint a hero prop, and let the headless
generators mass-produce from what they export.

## Install

Package Manager → **Add package from git URL**:

```
https://github.com/alperenelbiz/unity-dcc-bridge.git?path=/Packages/com.alperenelbiz.dccbridge#v0.4.0
```

Pin the tag. Without `#v0.4.0` you track the default branch and every fetch may bring
breaking changes.

To upgrade, change the tag and let the Package Manager re-resolve:

```
...#v0.4.0
```

## First run

A setup window opens the first time the package loads. Turn on the tools you can actually
run; each is auto-detected, and you can point at a custom path.

Reachable later at **Project Settings → DCC Bridge**.

Which tools the project expects is written to `ProjectSettings/DccBridge.json` and is meant
to be committed. Install paths are stored per machine and never shared — committing a
teammate's Blender path would break the project for everyone else.

## Use

From the Editor: **Tools → DCC Bridge**. Menu entries for a tool you do not have are
greyed out rather than failing when clicked.

From the CLI, against a *running* Editor:

```bash
unity request dcc_status
unity request dcc_palette
unity request dcc_run_all
```

`dcc_run_all` skips steps whose tool is unavailable instead of failing, so a contributor
with only Unity still gets everything that does not need Blender.

## Project layout it expects

```
data/
  palette.json        swatch definitions — the atlas is generated from this, not painted
  props.json          prop manifest: size, swatch, collider, triangle budget, builder
  materials.json      optional: detail materials carrying a real tiling texture
art-source/
  blender/props/      one .blend per prop
  blender/scripts/    optional prop builder scripts (geometry as reviewable source)
  blender/_lib/       generated UV lookup and palette reference
Assets/Art/
  Models/             generated FBX          SM_PascalCase.fbx
  Prefabs/            generated prefabs      P_PascalCase.prefab
  Materials/          generated materials    M_PascalCase.mat
  Textures/Atlas/     palette atlas          point filtered, no mips
  Textures/Detail/    tiling detail maps     repeat, linear, mipped
```

Import **Samples → Basic Setup** for a working `palette.json`, `props.json` and an example
builder script.

## Why the palette is data, not a painted image

Blender needs to know exactly where each swatch sits in order to snap UV islands onto it.
Generating the texture and the UV lookup from one source means they can never disagree, and
recolouring a whole project becomes a JSON edit.

## Licence

MIT. See [LICENSE.md](LICENSE.md).
