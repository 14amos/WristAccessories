# Offline tools

Everything that makes the item data in the release (`acc/`, `watchshadow/`, `outlines*.json`, `wrist_frames.json`) and the
Unity test scene the items are checked in. They read the game's own asset bundles (UnityPy) from your SPT install and write
JSON the plugin loads. Paths in the scripts are the author's working folders, written here as placeholders:

| Placeholder | Meaning |
|---|---|
| `SPT` | your SPT install (the folder with `EscapeFromTarkov.exe`) |
| `WORK/wristacc` | the working folder: `acc/`, `lab/`, `tools/`, `outlines.json`, ... |
| `WORK/tattoo/acc` | the accessory builder scripts (`tools/acc` here) |
| `WORK/pets/unity/CatTest` | a Unity 2022.3.43f1 project with `tools/unity-lab` as `Assets/BeadLab` |
| `OUT`, `BACKUPS` | output / backup folders |
| `$UNITY` | `Unity.exe` 2022.3.43f1 |

Change them to your own folders before running anything.

## Layout
- `data/` - outlines of every outfit's arm (`build_outlines.py`), wrist frames, accessory scan of all outfits
  (`scan_pieces.py`, `cluster_pieces.py`), release packaging (`make_release.py`) and the release check (`release_audit.py`).
- `acc/` - the accessory builder (`acclib.py`): bracelets laid round the arm, cut from outfits (`outfit_acc.py`; `PARTS=1`
  splits sets into single bracelets and builds bundles), watches, beads, kill sleeves.
- `lab/` - hands, gloves, shoes, tattoos and rings cut from outfits (`export_handwear.py`, `trim_cuffs.py`, `ink_export.py`,
  `arm_ink.py`, `feet_*.py`, `ring_*.py`), seam data (`seam_data.py`, `seamtone.py`), pictures (`hand_icons.py`,
  `hand_onarm.py`) and the shell scripts that run the Unity scene.
- `unity-lab/` - the Unity test scene: the plugin's own C# (synced by `lab/sync.py`) on the game's real meshes, renders for
  the item pictures (`Gallery.cs`), first-person surveys (`FpSurvey.cs`), the seam / tattoo check (`SeamTest.cs`).

Python 3 with UnityPy, numpy, scipy and Pillow. These are the author's working tools, not a polished pipeline.
