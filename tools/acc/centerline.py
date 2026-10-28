# Centreline of a flat chain mesh lying in its thinnest plane: 2 mm voxel clusters -> neighbour graph (<= 3.2 mm) ->
# longest shortest path (graph diameter) -> smoothed, resampled every 1 mm. Returns (plane axes, polyline 3D).
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import dijkstra, connected_components
from scipy.spatial import cKDTree

def plane_axes(P):
    c = P.mean(0); u, s, vt = np.linalg.svd(P - c, full_matrices=False)
    return c, vt[0], vt[1], vt[2]                       # in-plane a, b; normal n (thinnest)

def centerline(P, T, keep=None, bridge=0.004):
    """Graph = the mesh's own edges (vertices welded by position) + bridges <= `bridge` between separate pieces;
    path = graph diameter; smoothed, resampled every 1 mm. keep: vertex mask (e.g. without a pendant)."""
    key = np.round(P / 1e-5).astype(np.int64); _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
    nodes = np.zeros((inv.max() + 1, 3)); np.add.at(nodes, inv, P); nodes /= np.bincount(inv)[:, None]
    ok = np.ones(len(nodes), bool) if keep is None else np.zeros(len(nodes), bool)
    if keep is not None: ok[inv[keep]] = True
    E = inv[np.vstack([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])]
    E = E[ok[E].all(1)]
    n = len(nodes)
    g0 = coo_matrix((np.ones(len(E)), (E[:, 0], E[:, 1])), shape=(n, n))
    nc, lab = connected_components(g0, directed=False)
    tree = cKDTree(nodes); pairs = np.array(sorted(tree.query_pairs(bridge)))
    pairs = pairs[(lab[pairs[:, 0]] != lab[pairs[:, 1]]) & ok[pairs].all(1)] if len(pairs) else pairs
    allE = np.vstack([E, pairs]) if len(pairs) else E
    d = np.linalg.norm(nodes[allE[:, 0]] - nodes[allE[:, 1]], axis=1)
    g = coo_matrix((np.r_[d, d], (np.r_[allE[:, 0], allE[:, 1]], np.r_[allE[:, 1], allE[:, 0]])), shape=(n, n)).tocsr()
    nc2, lab2 = connected_components(g, directed=False)
    sub = np.where((lab2 == np.bincount(lab2[ok], minlength=nc2).argmax()) & ok)[0]
    d0 = dijkstra(g, indices=sub[0]); a = sub[np.argmax(np.where(np.isinf(d0[sub]), -1, d0[sub]))]
    da, pred = dijkstra(g, indices=a, return_predecessors=True); b = sub[np.argmax(np.where(np.isinf(da[sub]), -1, da[sub]))]
    path = [b]
    while path[-1] != a: path.append(pred[path[-1]])
    line = nodes[path[::-1]]
    seg = np.linalg.norm(np.diff(line, axis=0), axis=1); s = np.r_[0, np.cumsum(seg)]
    t = np.arange(0, s[-1], 0.001)
    out = np.c_[[np.interp(t, s, line[:, k]) for k in range(3)]].T
    k = 7; pad = np.r_[np.repeat(out[:1], k, 0), out, np.repeat(out[-1:], k, 0)]   # 15 mm moving average (strand zig-zag)
    out = np.array([pad[i:i + 2 * k + 1].mean(0) for i in range(len(out))])
    seg = np.linalg.norm(np.diff(out, axis=0), axis=1)
    return out, float(seg.sum()), nc2
