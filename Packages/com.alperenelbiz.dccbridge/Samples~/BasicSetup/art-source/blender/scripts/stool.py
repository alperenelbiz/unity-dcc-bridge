"""Example prop builder: a three-legged stool.

A prop with a `builder` entry in props.json is constructed by code rather than standing in
as a blockout box. Authoring this way keeps the asset reviewable in a diff and regenerable,
instead of a binary .blend whose construction is lost.

Colour comes from UVs: each part is pinned to a palette swatch and the parts are joined
into one mesh on one material, so a multi-coloured prop still costs one draw call.
"""

import math

SEAT_HEIGHT = 0.56
SEAT_RADIUS = 0.17
SEAT_THICKNESS = 0.05
LEG = 0.035


def build(kit, prop):
    seat_swatch = prop.get("swatch", "wood_light")

    # Three legs read as a stool and are cheaper than four.
    for index in range(3):
        angle = math.radians(90 + index * 120)
        radius = SEAT_RADIUS * 0.66
        kit.box(
            size=(LEG, LEG, SEAT_HEIGHT),
            location=(math.cos(angle) * radius, math.sin(angle) * radius, SEAT_HEIGHT / 2),
            swatch="metal_dark",
            name=f"leg_{index}",
        )

    kit.cylinder(
        radius=SEAT_RADIUS,
        depth=SEAT_THICKNESS,
        location=(0, 0, SEAT_HEIGHT + SEAT_THICKNESS / 2),
        swatch=seat_swatch,
        vertices=12,
        name="seat",
    )

    return kit.finish()
