"""Проверка связи smpl_trans/smpl_scaling с точками и смещения лодыжка-подошва.

Загрузка pickle через белый список: numpy, scipy.sparse, OrderedDict; объекты chumpy
заменяются заглушками, которые только запоминают состояние. Код из файлов не исполняется.
"""
import io
import pickle
import sys
from collections import OrderedDict

import numpy as np
import scipy.sparse as sp
from scipy.spatial.transform import Rotation


class ChStub:
    def __setstate__(self, state):
        self.state = state

    def value(self):
        s = self.state
        if isinstance(s, dict) and "x" in s:
            return np.asarray(s["x"])
        return s


ALLOWED = {
    ("numpy._core.multiarray", "_reconstruct"), ("numpy.core.multiarray", "_reconstruct"),
    ("numpy._core.multiarray", "scalar"), ("numpy.core.multiarray", "scalar"),
    ("numpy", "ndarray"), ("numpy", "dtype"),
    ("collections", "OrderedDict"),
}


class Safe(pickle.Unpickler):
    def find_class(self, module, name):
        if (module, name) in ALLOWED:
            return super().find_class(module, name)
        if (module, name) in (("copy_reg", "_reconstructor"), ("copyreg", "_reconstructor")):
            import copyreg
            return copyreg._reconstructor
        if module == "__builtin__" and name in ("object", "set", "frozenset", "dict", "list", "tuple"):
            return {"object": object, "set": set, "frozenset": frozenset, "dict": dict, "list": list, "tuple": tuple}[name]
        if module.startswith("scipy.sparse") and name in ("csc_matrix", "csr_matrix", "coo_matrix"):
            return getattr(sp, name)
        if module.startswith("chumpy"):
            return ChStub
        raise pickle.UnpicklingError(f"blocked: {module}.{name}")


def load(path, encoding="latin1"):
    with open(path, "rb") as f:
        return Safe(io.BytesIO(f.read()), encoding=encoding).load()


def val(x):
    if isinstance(x, ChStub):
        return x.value()
    if sp.issparse(x):
        return x
    return np.asarray(x)


model_path, motion_path, kp_path = sys.argv[1:4]
m = load(model_path)
print("model keys:", sorted(m.keys()))
for k in sorted(m.keys()):
    v = m[k]
    if isinstance(v, ChStub):
        a = v.value()
        print(f"  {k}: chumpy -> {getattr(a, 'shape', type(a))}")
    elif sp.issparse(v):
        print(f"  {k}: sparse {v.shape}")
    elif isinstance(v, np.ndarray):
        print(f"  {k}: ndarray {v.shape} {v.dtype}")
    else:
        print(f"  {k}: {type(v).__name__}")

v_template = val(m["v_template"]).astype(np.float64)
J_reg = m["J_regressor"]
kintree = np.asarray(m["kintree_table"])
parents = kintree[0].astype(np.int64)
parents[0] = -1
J = np.asarray(J_reg @ v_template)
print("rest joints:", J.shape, " pelvis J0 =", np.round(J[0], 4))
print("rest-pose model height (y range of vertices, m):", round(float(v_template[:, 1].max() - v_template[:, 1].min()), 3))
sole_y = float(v_template[:, 1].min())
for jid, nm in ((7, "L_ankle"), (8, "R_ankle"), (10, "L_foot"), (11, "R_foot")):
    print(f"  {nm}: y={J[jid,1]:.4f}  ankle-to-lowest-vertex (m)={J[jid,1]-sole_y:.4f}")

mo = load(motion_path)
poses = mo["smpl_poses"].reshape(-1, 24, 3).astype(np.float64)
scale = float(mo["smpl_scaling"][0])
trans = mo["smpl_trans"].astype(np.float64)
kp = load(kp_path)
Ko = kp["keypoints3d_optim"]
Kr = kp["keypoints3d"]

R = Rotation.from_rotvec(poses.reshape(-1, 3)).as_matrix().reshape(-1, 24, 3, 3)
T = poses.shape[0]
G = np.zeros((T, 24, 3, 3))
P = np.zeros((T, 24, 3))
for i in range(24):
    if parents[i] < 0:
        G[:, i] = R[:, i]
        P[:, i] = J[i]
    else:
        p = parents[i]
        G[:, i] = G[:, p] @ R[:, i]
        P[:, i] = P[:, p] + (G[:, p] @ (J[i] - J[p])[:, None])[..., 0]

pairs = [("lsho", 16, 5), ("rsho", 17, 6), ("lelb", 18, 7), ("relb", 19, 8), ("lwri", 20, 9), ("rwri", 21, 10),
         ("lhip", 1, 11), ("rhip", 2, 12), ("lkne", 4, 13), ("rkne", 5, 14), ("lank", 7, 15), ("rank", 8, 16)]


def report(label, joints):
    d = []
    for nm, si, ci in pairs:
        d.append(np.linalg.norm(joints[:, si] - Ko[:, ci], axis=1).mean())
    print(f"{label:55s} mean dist to keypoints3d_optim (cm): {np.mean(d):7.2f}  per joint max {np.max(d):7.2f}")
    return np.mean(d)


cands = {
    "A: P*scale + trans": P * scale + trans[:, None],
    "B: (P + trans)*scale": (P + trans[:, None]) * scale,
    "C: P*scale + trans - J0*scale": P * scale + (trans - J[0] * scale)[:, None],
    "D: P*scale (без trans)": P * scale,
}
best = None
for k, v in cands.items():
    r = report(k, v)
    if best is None or r < best[0]:
        best = (r, k)
print("best:", best)

# лучшая из вариантов: разброс остатка по кадрам и средняя разница положения таза
Jw = cands[best[1]]
res = Jw - 0
pelvis_w = Jw[:, 0]
hip_mid = (Ko[:, 11] + Ko[:, 12]) / 2
print("pelvis(SMPL J0) - hip midpoint(kp): mean", np.round((pelvis_w - hip_mid).mean(axis=0), 2), " std", np.round((pelvis_w - hip_mid).std(axis=0), 2))
print("mean per-joint distance (cm) for best candidate:")
for nm, si, ci in pairs:
    print(f"  {nm}: {np.linalg.norm(Jw[:, si] - Ko[:, ci], axis=1).mean():6.2f}")

# смещение лодыжка-подошва в единицах сцены
print("ankle-to-sole offset in scene units (cm), foot vertices:",
      round(float(((J[7, 1] + J[8, 1]) / 2 - sole_y) * scale), 2))
