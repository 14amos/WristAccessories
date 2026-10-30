# chk.sh <job index> <hide list> : renders kept/removed rows, small
i=$1; p=$(python -I -c "import json;print(json.load(open('jobs.json'))[$i]['path'])")
WORK/pets/venv/Scripts/python.exe -I label.py "$p" ${3:-L} chk_$i.png "$2" > /dev/null 2>&1
WORK/pets/venv/Scripts/python.exe -I -c "
from PIL import Image;im=Image.open('chk_$i.png');w,h=im.size;im.crop((0,h//2,w,h)).resize((w*2//3,h//3)).save('chk_${i}s.png')"
