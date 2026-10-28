#!/bin/bash
# gallery.sh: sync plugin sources into the lab, render every accessory (or LAB_ACCS) on the user's arm, contact sheet
python -I WORK/wristacc/lab/sync.py > /dev/null
cd WORK/wristacc/lab; rm -f gallery.log
timeout 2400 "$UNITY" -batchmode -projectPath "WORK/pets/unity/CatTest" -executeMethod WristAccessories.Gallery.Run -logFile "WORK/wristacc/lab/gallery.log"
echo "unity exit $?"; grep -a "error CS\|\[Gallery\] [A-Z]\|Exception" gallery.log | grep -v "^\[Gallery\] [a-z_~0-9]*:" | head -8 | cut -c1-200
cat ${LAB_OUT:-WORK/wristacc/lab/gallery}/report.txt 2>/dev/null
