def islands(T, nv):
    par = np.arange(nv)
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for a, b, cc in T: ra, rb, rc = f(a), f(b), f(cc); par[rb] = ra; par[f(rc)] = ra
    return np.array([f(a) for a in T[:, 0]])
