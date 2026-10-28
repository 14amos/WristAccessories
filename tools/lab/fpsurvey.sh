#!/bin/bash
# fpsurvey.sh: sync plugin sources into the lab, run the first-person survey (Assets/BeadLab/Runtime/FpSurvey.cs) over every
# exported outfit arm (lab/fp_export.py -> lab/fpdata) -> lab/fpsurvey (report.tsv, flags.tsv, PNGs). Env: LAB_GEOS, LAB_LIMIT, LAB_ITEMS, LAB_RENDER, LAB_OUT
python -I WORK/wristacc/lab/sync.py > /dev/null
cd WORK/wristacc/lab; rm -f fpsurvey.log
timeout 7200 "$UNITY" -batchmode -projectPath "WORK/pets/unity/CatTest" -executeMethod WristAccessories.FpSurvey.Run -logFile "WORK/wristacc/lab/fpsurvey.log"
echo "unity exit $?"; grep -a "error CS\|\[FpSurvey\] DONE\|\[FpSurvey\] [A-Z0-9a-f]*:" fpsurvey.log | head -8 | cut -c1-240
