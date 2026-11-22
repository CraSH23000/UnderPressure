# Character Factory

Blender scripts that build the characters from code, so any proportion is a one-line change and a re-export.
Unity ignores this folder (its name ends in `~`).

- `cf.py`: shape helpers (spheres, smooth limbs, lofted torsos, fur clumps, anchors).
- `yeti.py`, `skeleton.py`, `chihuahua.py`, `sharc.py` and `slime.py`: each in S / M / L. `chihuahua.py` puts its own head on `human.py`'s body (the human itself is parked).
- `export.py`: writes `Yeti_S.fbx`, `Yeti_M.fbx` and `Yeti_L.fbx`.
- `render.py`: studio preview renders.

**Re-export into the game** (from this folder):

    blender -b -P export.py -- ../Resources/Characters Yeti Skeleton Chihuahua Sharc Slime

You can also run it with plain Python, using the `bpy` module (`pip install bpy`).

**Part contract the game relies on.** Names must match; Blender's -Y is the front.
- `Body` (mesh), with `Head` under it.
- `Eye_L` and `Eye_R` under `Head` (dot eyes: the game blinks and nudges them).
- `Anchor_Hat` (empty, top of head).
- `Arm_L` and `Arm_R`, pivoting at the shoulder.
- `Hand_L` and `Hand_R`.
- `Leg_L` and `Leg_R`, pivoting at the hip, with `Shoe_L` and `Shoe_R` under them.

**Material names.**
- `Tint` and `TintShade` take the player's Spray Booth color.
- `Skin`, `Dark`, `Bone` and `Pupil` are fixed colors.
- `Glow` is a pale glowing color (the skeleton's eyes).
- `Slime` becomes see-through goo in the player's color. It also marks the character as a blob: no legs, cosmetics inside, a trail.
- Any other material name keeps its modelled color.
