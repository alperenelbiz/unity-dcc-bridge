"""Shape helpers for authored props.

A prop builder script describes geometry as code: parts, each pinned to a palette
swatch, joined into one mesh at the end. Authoring this way means an asset is source
rather than a binary blob — reviewable in a diff, regenerable, and editable later
without reverse-engineering someone's modelling session.

Colour comes from UVs, not materials: each part's UVs collapse onto its swatch cell
before the join, so the finished prop is many colours on a single shared material.
"""

from __future__ import annotations

import bpy  # type: ignore[import-not-found]
import bmesh  # type: ignore[import-not-found]
from mathutils import Matrix  # type: ignore[import-not-found]

import palette_uv

EXPORT_COLLECTION = "Export"


class PropBuilder:
    """Accumulates parts, then welds them into one export-ready object."""

    def __init__(self, name: str, lookup: dict, material_name: str = "M_PropAtlas"):
        self.name = name
        self.lookup = lookup
        self.material_name = material_name
        self.parts: list = []

        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.collection = bpy.data.collections.new(EXPORT_COLLECTION)
        bpy.context.scene.collection.children.link(self.collection)

    # ---- primitives -------------------------------------------------------

    def box(self, size, location=(0, 0, 0), swatch="laminate", bevel=0.0, name=None,
            material=None, tile=None):
        """Axis-aligned box. `location` is its centre, Z up, metres.

        Pass `material` + `tile` to give the part a detail material instead of a flat
        swatch: its UVs stay a real cube projection at `tile` metres per repeat, so a
        tiling normal map reads at a believable scale.
        """
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
        obj = bpy.context.active_object
        obj.name = name or f"{self.name}_part{len(self.parts)}"
        obj.scale = size
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

        if bevel > 0:
            self._bevel(obj, bevel)

        return self._register(obj, swatch, material, tile)

    def cylinder(self, radius, depth, location=(0, 0, 0), swatch="metal_dark",
                 vertices=8, rotation=(0, 0, 0), name=None, material=None, tile=None):
        """Low-vertex cylinder. 8 sides reads as round at shop distances and costs little."""
        bpy.ops.mesh.primitive_cylinder_add(
            radius=radius, depth=depth, vertices=vertices,
            location=location, rotation=rotation)
        obj = bpy.context.active_object
        obj.name = name or f"{self.name}_part{len(self.parts)}"
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        return self._register(obj, swatch, material, tile)

    # ---- assembly ---------------------------------------------------------

    def _bevel(self, obj, width: float) -> None:
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        bmesh.ops.bevel(
            mesh, geom=list(mesh.edges) + list(mesh.verts),
            offset=width, segments=1, affect="EDGES", profile=0.5, clamp_overlap=True)
        mesh.to_mesh(obj.data)
        mesh.free()

    def _register(self, obj, swatch: str, material: str | None = None, tile: float | None = None):
        detail = material is not None and material != self.material_name

        # Detail parts keep a real cube projection so their texture tiles; atlas parts
        # collapse onto a single swatch cell. Doing this per part before the join is
        # what lets one mesh carry several colours and more than one material.
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.cube_project(cube_size=tile if detail and tile else 1.0)
        bpy.ops.object.mode_set(mode="OBJECT")

        if not detail:
            palette_uv.assign_swatch(obj, swatch, self.lookup)

        # Assign the part's own material before the join. Blender remaps material
        # indices during a join, so per-part assignment is what keeps the slots correct.
        name = material or self.material_name
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        obj.data.materials.clear()
        obj.data.materials.append(mat)

        self.parts.append(obj)
        return obj

    def finish(self, pivot_at_base: bool = True):
        """Join every part, seat the pivot, assign the shared material, collect it."""
        if not self.parts:
            raise RuntimeError(f"prop '{self.name}' has no parts")

        bpy.ops.object.select_all(action="DESELECT")
        for part in self.parts:
            part.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]

        if len(self.parts) > 1:
            bpy.ops.object.join()

        obj = bpy.context.active_object
        obj.name = self.name
        obj.data.name = f"{self.name}_mesh"

        if pivot_at_base:
            # Put the origin at the centre of the prop's footprint, on the floor.
            #
            # Two separate things have to happen, and doing only the second is a trap:
            # a join leaves the object's origin wherever the *first* part's origin was,
            # so the object carries a leftover location. Baking that into the mesh first
            # is what makes the pivot actually land at world zero; otherwise the prefab
            # arrives in Unity with a non-zero root transform and rotates about a leg.
            obj.data.transform(obj.matrix_world)
            obj.matrix_world = Matrix.Identity(4)

            coords = [v.co for v in obj.data.vertices]
            xs = [c.x for c in coords]
            ys = [c.y for c in coords]
            zs = [c.z for c in coords]
            obj.data.transform(Matrix.Translation((
                -(min(xs) + max(xs)) / 2.0,
                -(min(ys) + max(ys)) / 2.0,
                -min(zs),
            )))

        for coll in list(obj.users_collection):
            coll.objects.unlink(obj)
        self.collection.objects.link(obj)

        return obj


def save(path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(path))
