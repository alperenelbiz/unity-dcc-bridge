"""Seamless tiling detail normal maps.

Generated rather than painted: a hand-painted tile shows its repeat, while periodic
noise tiles perfectly by construction and can be re-derived from parameters instead of
being an unreproducible image someone made once.

Output is OpenGL convention (+Y up), which is what Unity and URP expect. Blender also
bakes OpenGL, so nothing in this project ever needs a green-channel flip.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

OUT_DIR = Path("Assets/Art/Textures/Detail")


def _periodic_voronoi(size: int, cells: int, rng: np.random.Generator,
                      jitter: float = 0.9) -> tuple[np.ndarray, np.ndarray]:
    """Distances to the nearest and second-nearest scattered points, wrapping at the edges.

    Returns (F1, F2). Wrapping is what makes the result seamless: every distance is
    measured on a torus, so the left edge genuinely continues into the right.

    F2 - F1 is the useful quantity for leather. Raw F1 peaks at the cell *boundaries*,
    which builds raised ridges between pits. F2 - F1 instead falls to zero exactly on the
    boundaries and rises toward each cell's centre, giving domes separated by creases.
    """
    points = (np.stack(np.meshgrid(np.arange(cells), np.arange(cells), indexing="ij"), -1)
              + rng.random((cells, cells, 2)) * jitter) / cells

    coords = (np.stack(np.meshgrid(np.arange(size), np.arange(size), indexing="ij"), -1) + 0.5) / size
    flat = points.reshape(-1, 2)

    f1 = np.full((size, size), np.inf, dtype=np.float32)
    f2 = np.full((size, size), np.inf, dtype=np.float32)

    # Chunked so a large tile against many seeds does not allocate a huge array.
    for start in range(0, flat.shape[0], 48):
        seeds = flat[start : start + 48]
        delta = np.abs(coords[:, :, None, :] - seeds[None, None, :, :])
        delta = np.minimum(delta, 1.0 - delta)  # wrap
        dist = np.sqrt((delta ** 2).sum(-1))

        merged = np.concatenate([np.stack([f1, f2], -1), dist], axis=-1)
        merged.sort(axis=-1)
        f1, f2 = merged[..., 0], merged[..., 1]

    return f1, f2


def _periodic_noise(size: int, freq: int, rng: np.random.Generator) -> np.ndarray:
    """Value noise that tiles, by generating at `freq` and resampling up cyclically."""
    base = rng.random((freq, freq)).astype(np.float32)
    tiled = np.tile(base, (2, 2))
    img = Image.fromarray((tiled * 255).astype(np.uint8)).resize((size * 2, size * 2), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32)[:size, :size] / 255.0


def _height_to_normal(height: np.ndarray, strength: float) -> np.ndarray:
    """Central differences with wrapping, so the normal map tiles like its height map."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * strength

    # +Y up (OpenGL): the V axis runs opposite to the array's row order, hence the sign.
    normal = np.stack([-dx, dy, np.ones_like(height)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return ((normal * 0.5 + 0.5) * 255).astype(np.uint8)


def build_leather(size: int = 512, seed: int = 7) -> tuple[np.ndarray, np.ndarray]:
    rng = np.random.default_rng(seed)

    # Two pebble scales: large cells for the main grain, smaller ones breaking them up.
    coarse_f1, coarse_f2 = _periodic_voronoi(size, 18, rng)
    fine_f1, fine_f2 = _periodic_voronoi(size, 38, rng)
    grain = _periodic_noise(size, 128, rng)

    def domes(f1: np.ndarray, f2: np.ndarray) -> np.ndarray:
        d = f2 - f1
        return d / d.max()

    coarse = domes(coarse_f1, coarse_f2)
    fine = domes(fine_f1, fine_f2)

    # Creases are the read. Sharpen the coarse layer so boundaries stay tight rather
    # than blurring into a soft lumpy field.
    coarse = np.clip(coarse * 1.8, 0.0, 1.0)

    height = (0.72 * coarse + 0.22 * fine + 0.06 * grain).astype(np.float32)
    height = (height - height.min()) / (height.max() - height.min())

    # Flatten the tops so pebbles read as domes meeting at creases, rather than cones.
    height = np.power(height, 0.55)

    return height, _height_to_normal(height, strength=size * 0.006)


def build(project_root: Path) -> int:
    out_dir = project_root / OUT_DIR
    out_dir.mkdir(parents=True, exist_ok=True)

    height, normal = build_leather()

    Image.fromarray(normal, mode="RGB").save(out_dir / "T_LeatherNormal.png", optimize=True)
    Image.fromarray((height * 255).astype(np.uint8), mode="L").save(
        project_root / "art-source" / "blender" / "_lib" / "leather_height_reference.png", optimize=True)

    (out_dir / "detail_maps.json").write_text(json.dumps({
        "T_LeatherNormal.png": {"size": 512, "seed": 7, "convention": "OpenGL +Y", "tiles": True}
    }, indent=2) + "\n")

    print(f"[detail] T_LeatherNormal.png (512, seamless, OpenGL +Y) -> {OUT_DIR}")
    return 0
