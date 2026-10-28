#!/bin/bash
# fpsurvey_p.sh <project copy> <log>: fpsurvey.sh on a copy of the lab project (Unity opens a project once: copies run side by side)
PROJ="$1"; LOG="$2"
timeout 7200 "$UNITY" -batchmode -projectPath "$PROJ" -executeMethod WristAccessories.FpSurvey.Run -logFile "$LOG"
echo "unity exit $?"; grep -a "error CS\|\[FpSurvey\] DONE" "$LOG" | head -4 | cut -c1-200
