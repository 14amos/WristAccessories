# sync.py: copy the plugin's WristFit.cs + BraceletPhysics.cs into the Unity bead lab, as C# 9 (Unity 2022.3's compiler):
# "namespace X;" -> "namespace X { ... }"
import re
SRC = "WORK/wristacc/src/"; DST = "WORK/pets/unity/CatTest/Assets/BeadLab/Runtime/"
import os
for f in ("WristFit.cs", "BraceletPhysics.cs", "TagSwing.cs", "LiveFit.cs", "HandWear.cs", "Seam.cs"):
    if not os.path.exists(SRC + f): print("not in the plugin any more:", f); continue      # LiveFit.cs: removed 0.9.4 (lab keeps its copy)
    s = open(SRC + f, encoding="utf-8").read()
    s, n = re.subn(r"^namespace ([\w.]+);\s*$", r"namespace \1 {", s, count=1, flags=re.M)
    if n: s = s.rstrip() + "\n}\n"
    open(DST + f, "w", encoding="utf-8").write(s); print("synced", f)
