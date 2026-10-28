#!/bin/bash
# bracelet_runs.sh: every closed outfit bracelet (not watches / sleeves / loose / rings), straightened vs as measured:
# QILO renders (visual check) + every outfit arm without renders (closure columns cover_pct / open_deg / wander_mm, clips, floats)
cd WORK/wristacc/lab
while tasklist 2>/dev/null | grep -qi "^Unity.exe"; do sleep 15; done
ITEMS=$(python -c "
import json,glob
n=[]
for f in sorted(glob.glob('SPT/BepInEx/plugins/WristAccessories/acc/acc_*.json')):
  a=json.load(open(f))
  if a.get('closed') and '~' not in f and not a['name'].startswith('watch') and a['name'] not in ('rolex','skullring') and 'sleeve' not in a['name'] and not a.get('loose') and not a.get('fitR'): n.append(a['name'])
print(','.join(n))")
for st in 1 0; do
  LAB_GEOS=9b531ebc914f LAB_ITEMS=$ITEMS LAB_RENDER=1 LAB_STRAIGHT=$st LAB_OUT=WORK/wristacc/lab/fpb_qilo_s$st bash fpsurvey.sh > /dev/null 2>&1; echo "qilo straight=$st: $(grep -a 'DONE' fpsurvey.log | cut -c1-70)"
done
for st in 1 0; do
  LAB_ITEMS=$ITEMS LAB_RENDER=0 LAB_STRAIGHT=$st LAB_OUT=WORK/wristacc/lab/fpb_all_s$st bash fpsurvey.sh > /dev/null 2>&1; echo "all straight=$st: $(grep -a 'DONE\|error CS' fpsurvey.log | head -2 | cut -c1-90)"
done
