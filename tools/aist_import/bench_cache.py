"""Замер: сколько стоит вычисление производного кеша для одной последовательности."""
import io
import pickle
import sys
import time

import numpy as np
import scipy.sparse as sp
from scipy.spatial.transform import Rotation


class ChStub:
    def __setstate__(self, state):
        self.state = state

    def value(self):
        s = self.state
        return np.asarray(s["x"]) if isinstance(s, dict) and "x" in s else s


ALLOWED = {
    ("numpy._core.multiarray", "_reconstruct"), ("numpy.core.multiarray", "_reconstruct"),
    ("numpy._core.multiarray", "scalar"), ("numpy.core.multiarray", "scalar"),
    ("numpy", "ndarray"), ("numpy", "dtype"), ("collections", "OrderedDict"),
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


def load(path):
    with open(path, "rb") as f:
        return Safe(io.BytesIO(f.read()), encoding="latin1").load()


model_path, motion_path = sys.argv[1:3]
m = load(model_path)
mo = load(motion_path)
v_template = np.asarray(m["v_template"], dtype=np.float64)
shapedirs = m["shapedirs"].value() if isinstance(m["shapedirs"], ChStub) else m["shapedirs"]
weights = np.asarray(m["weights"], dtype=np.float64)
J_reg = m["J_regressor"]
parents = np.asarray(m["kintree_table"])[0].astype(np.int64)
parents[0] = -1
poses = mo["smpl_poses"].reshape(-1, 24, 3).astype(np.float64)
T = poses.shape[0]
print(f"кадров: {T} ({T / 60:.1f} с при 60 fps)")

# 1. подстановка формы тела пользователя: пересчёт покоя по новым betas (один раз на профиль)
betas = np.zeros(10)
betas[:3] = [1.5, -0.8, 0.4]
t0 = time.perf_counter()
v_shaped = v_template + np.einsum("vcb,b->vc", np.asarray(shapedirs)[:, :, :10], betas)
J = np.asarray(J_reg @ v_shaped)
t1 = time.perf_counter()
print(f"подстановка формы (на профиль, один раз): {1000 * (t1 - t0):.1f} мс")

# 2. прямая кинематика по кадрам для суставов с новыми длинами костей
t0 = time.perf_counter()
R = Rotation.from_rotvec(poses.reshape(-1, 3)).as_matrix().reshape(T, 24, 3, 3)
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
t1 = time.perf_counter()
print(f"прямая кинематика 24 суставов, {T} кадров: {1000 * (t1 - t0):.1f} мс")

# 3. переприземление: высота пола по низшей лодыжке (суставы), масштаб перемещения по отношению ног
t0 = time.perf_counter()
ankle_y = np.minimum(P[:, 7, 1], P[:, 8, 1])
floor = np.percentile(ankle_y, 2)
leg_ratio = 1.1
trans = mo["smpl_trans"].astype(np.float64)
trans_scaled = trans * leg_ratio
t1 = time.perf_counter()
print(f"переприземление корня и масштаб перемещения: {1000 * (t1 - t0):.2f} мс")

# 4. для сравнения: полный скиннинг меша 6890 вершин на каждый кадр
t0 = time.perf_counter()
A = np.zeros((T, 24, 4, 4))
A[:, :, :3, :3] = G
A[:, :, :3, 3] = P - (G @ J[None, :, :, None])[..., 0]
A[:, :, 3, 3] = 1
Tm = np.einsum("vj,tjab->tvab", weights, A)
vh = np.concatenate([v_shaped, np.ones((6890, 1))], axis=1)
verts = np.einsum("tvab,vb->tva", Tm, vh)[..., :3]
t1 = time.perf_counter()
print(f"полный скиннинг меша 6890 вершин, {T} кадров (без поз-зависимых поправок): {1000 * (t1 - t0):.0f} мс")
print("размер результата для кеша: суставы", P.nbytes // 1024, "КБ; меш по кадрам", verts.nbytes // (1024 * 1024), "МБ")
