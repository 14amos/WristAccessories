#!/bin/bash
# survey.sh: render every hands outfit (or LAB_IDS) at rest, both forearms, 4 views -> lab/survey (Assets/BeadLab/Runtime/Survey.cs)
cd WORK/wristacc/lab; rm -f survey.log
timeout 5400 "$UNITY" -batchmode -projectPath "WORK/pets/unity/CatTest" -executeMethod WristAccessories.Survey.Run -logFile "WORK/wristacc/lab/survey.log"
echo "unity exit $?"; grep -a "error CS\|\[Survey\]" survey.log | head -5 | cut -c1-200
