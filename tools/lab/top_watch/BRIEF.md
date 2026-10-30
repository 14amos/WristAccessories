# Labelling baked watches on third-person tops (Escape from Tarkov, offline, read-only on the game)

Goal: for each job (a third-person TOP outfit mesh, LOD0), list the mesh ISLANDS (index-connected triangle groups) that make up
the outfit's own WRIST WATCH, so the mod can cut exactly those triangles out. Everything else must stay.

Folder (all scripts here; work only in this folder): WORK/wristacc/lab/top_watch
Python: WORK/pets/venv/Scripts/python.exe -I   (cd into the folder first, in the same bash command; prefix OPENBLAS_NUM_THREADS=1 - the game is running and memory is tight)

Tools:
- `bash chk.sh <job> "<ids comma list>" [L|R]` -> chk_<job>s.png: row 1 = the wrist WITHOUT the listed islands (what stays),
  row 2 = ONLY the listed islands (what gets cut), 4 views round the arm. Use "" for no ids.
- `WORK/pets/venv/Scripts/python.exe -I label.py <path> <L|R> lab_<job>.png ""` -> full 4-row image (1680x1680):
  row 1 textured, row 2 islands in random colours with their id numbers, rows 3/4 as chk. It also PRINTS every island near
  the wrist as id:triangles. Job paths: jobs.json[job]["path"]. Crop / zoom images with PIL (venv python) when labels overlap,
  e.g. a crop of row 2 scaled x2.
Facts learned on 3 done tops: watch islands are usually one contiguous run of ids (e.g. 26..57, 42..76 + 86, 95..117 + 131);
the dial / bezel / glass are often a separate id after the run. NOT watch: sleeve, cuff, velcro / sleeve straps, skin, glove
or hand pieces, patches - even when they are next to the watch. Watch parts: strap (both halves, loops, holes), buckle, case,
bezel, dial, glass, crown/buttons.
Procedure per job: label.py -> read row 2 ids + the printed list -> guess the set -> chk.sh -> look at both rows: row 1 must
show NO watch bit left and NO hole in the sleeve/skin; row 2 must show ONLY watch. Fix and repeat until clean. If there is no
watch on the left wrist (label side L), try R. If there is no watch at all, ids = [].
Output: write labels_<yourname>.json: {"<job>": {"side": "L", "ids": [..], "note": "clean" or what is doubtful}} - update the
file after every job (so work is not lost). Final message: one line per job: job, side, id count, clean/doubtful.
Do not edit any other file; do not touch the game folders.
