#!/bin/bash
# run.sh: sync the plugin sources into the Unity bead lab, run it, print the lab lines + a contact sheet
python -I WORK/wristacc/lab/sync.py > /dev/null
cd WORK/wristacc/lab; rm -f lab.log
timeout 1500 "$UNITY" -batchmode -projectPath "WORK/pets/unity/CatTest" -executeMethod WristAccessories.BeadLab.Run -logFile "WORK/wristacc/lab/lab.log"
echo "unity exit $?"; grep -a "BeadLab\]\|error CS\|physics on\|Exception" lab.log | head -12 | cut -c1-220
python -I WORK/wristacc/lab/sheet.py ${LAB_OUT:-WORK/wristacc/lab/out} WORK/wristacc/lab/sheet.jpg 4
