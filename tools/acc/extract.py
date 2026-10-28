# extract.py: LOD0 meshes of the accessory candidates -> acc/<name>.json (pos, tris; raw Unity mesh space)
import UnityPy, numpy as np, json
from UnityPy.helpers.MeshHelper import MeshHandler
G = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/"
items = {"rolex": ("assets/content/items/barter/item_barter_valuable_rolex/item_barter_valuable_rolex.bundle", "item_barter_valuable_rolex_LOD0"),
         "chainlet": ("assets/content/items/barter/chain/item_chain.bundle", "chain_LOD0"),
         "chain_gold": ("assets/content/items/barter/chain/item_chain_gold.bundle", "gold_chain_LOD0"),
         "prokill": ("assets/content/items/barter/item_barter_valuable_prokill/item_barter_valuable_prokill.bundle", "item_barter_valuable_prokill_LOD0"),
         "paracord": ("assets/content/items/barter/item_barter_other_paracord/item_barter_other_paracord.bundle", "item_barter_other_paracord_LOD0"),
         "skullring": ("assets/content/items/barter/item_barter_valuable_skullring/item_barter_valuable_skullring.bundle", "item_barter_valuable_skullring_LOD0"),
         "dogtag_usec": ("assets/content/items/barter/dog_tags/item_dogtags_usec.bundle", "model_LOD0")}
for k, (p, mn) in items.items():
    env = UnityPy.load(G + p)
    for o in env.objects:
        if o.type.name == "Mesh" and o.peek_name() == mn:
            m = o.read(); h = MeshHandler(m); h.process()
            tris = np.vstack([np.array(s).reshape(-1, 3) for s in h.get_triangles()])
            json.dump(dict(pos=np.array(h.m_Vertices).tolist(), tris=tris.tolist()), open(f"WORK/tattoo/acc/{k}.json", "w"))
            print(k, len(h.m_Vertices), "verts", len(tris), "tris")
