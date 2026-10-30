# wristscrap.py [--apply] <hw json>...: islands of a tattoo item that are only on the forearm bone and look like the source outfit's
# bracelet / cuff (mean skin share < 0.9 and mean saturation >= 0.5: gold beads, brown leather) - the brown patches at the wrist seam
# (user shot 2026-10-30, Day Off). --apply drops them (backup .bak-wristscrap).
import json,base64,numpy as np,sys,shutil
sys.path.insert(0,"WORK/wristacc/lab"); import hand_icons_lib as HI
exec(open("WORK/wristacc/lab/top_watch/isl_fn.py").read())
arr=lambda s,t,k: np.frombuffer(base64.b64decode(s),t).reshape(-1,k)
b64=lambda a,t: base64.b64encode(np.ascontiguousarray(a,dtype=t).tobytes()).decode()
def skin(px):
    x=np.clip(px,0,1); mx=x.max(-1); mn=x.min(-1); d=mx-mn+1e-9; s=np.where(mx>0,d/(mx+1e-9),0); v=mx; r,g,b=x[...,0],x[...,1],x[...,2]
    h=np.where(mx==r,((g-b)/d)%6,np.where(mx==g,(b-r)/d+2,(r-g)/d+4))*60
    return ((h<50)|(h>340))&(s>0.12)&(s<0.7)&(v>0.22), s
W=np.array([[1/3,1/3,1/3],[.6,.2,.2],[.2,.6,.2],[.2,.2,.6]])
apply="--apply" in sys.argv
for f in [a for a in sys.argv[1:] if a!="--apply"]:
    j=json.load(open(f)); P=arr(j["pos"],"<f4",3); UV=arr(j["uv"],"<f4",2); BI=arr(j["bi"],"<i4",4); BW=arr(j["bw"],"<f4",4); top=BI[np.arange(len(BI)),BW.argmax(1)]
    fa=[i for i,b in enumerate(j["bones"]) if "Forearm" in b]; gone=0; tot=0; subs=[]
    for s in j["subs"]:
        T=arr(s["tris"],"<i4",3); tot+=len(T); img=HI.tex(j["bundle"],s["mat"])
        if img is None: subs.append(s); continue
        hh,ww=img.shape[:2]; uv=np.einsum("kj,tjc->tkc",W,UV[T])%1.0; px=img[((1-uv[...,1])*(hh-1)).astype(int),(uv[...,0]*(ww-1)).astype(int)]
        sk,sa=skin(px); isl=islands(T,len(P)); keep=np.ones(len(T),bool)
        for k in np.unique(isl):
            m=isl==k
            if np.isin(top[T[m]],fa).all() and sk[m].mean()<0.9 and sa[m].mean()>=0.5: keep[m]=False
        gone+=int((~keep).sum()); s2=dict(s); s2["tris"]=b64(T[keep].ravel(),"<i4"); subs.append(s2)
    print(f.split("/")[-1], j.get("note","")[:50], "scrap triangles", gone, "of", tot)
    if apply and gone:
        shutil.copy(f, f+".bak-wristscrap"); j["subs"]=subs; json.dump(j,open(f,"w"))
