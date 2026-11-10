"""Проверка соглашения FineDance: 6D вращения (строки или столбцы), ось вверх, положение пола.

Скелет SMPL нейтральный (24 сустава). Первые 22 сустава SMPL-H совпадают с телом SMPL, кисти не используются.
Использование: finedance_check.py <smpl.pkl> <motion.npy>...
"""
import sys

import numpy as np

from safe_pickle import load, val

m = load(sys.argv[1])
v_t = val(m["v_template"]).astype(np.float64)
J = np.asarray(m["J_regressor"] @ v_t)
parents = np.asarray(m["kintree_table"])[0].astype(np.int64)
parents[0] = -1


def gs(a, b):
    x = a / np.linalg.norm(a, axis=-1, keepdims=True)
    b = b - (x * b).sum(-1, keepdims=True) * x
    y = b / np.linalg.norm(b, axis=-1, keepdims=True)
    return x, y, np.cross(x, y)


def rotmats(d6, mode):
    a, b = d6[..., :3], d6[..., 3:]
    x, y, z = gs(a, b)
    if mode == "cols":      # a, b = первые два столбца
        return np.stack([x, y, z], axis=-1)
    return np.stack([x, y, z], axis=-2)   # a, b = первые две строки


def fk(R):
    T = R.shape[0]
    G = np.zeros((T, 22, 3, 3))
    P = np.zeros((T, 22, 3))
    for i in range(22):
        if parents[i] < 0:
            G[:, i] = R[:, i]
            P[:, i] = J[i]
        else:
            p = parents[i]
            G[:, i] = G[:, p] @ R[:, i]
            P[:, i] = P[:, p] + (G[:, p] @ (J[i] - J[p])[:, None])[..., 0]
    return P


for path in sys.argv[2:]:  # noqa
    d = np.load(path).astype(np.float64)
    trans, rot = d[:, :3], d[:, 3:].reshape(len(d), 52, 6)
    print(path, d.shape, "trans min", np.round(trans.min(0), 3), "max", np.round(trans.max(0), 3), "first", np.round(trans[0], 3))
    for mode in ("cols", "rows"):
        P = fk(rotmats(rot[:, :22], mode)) + trans[:, None]
        feet = P[:, [7, 8, 10, 11]]
        for ax, nm in ((1, "y"), (2, "z")):
            low = feet[:, :, ax].min(1)
            hi = P[:, 15, ax] - low   # голова над самой низкой точкой стопы
            print(f"  {mode} вверх={nm}: нижняя точка стопы std {low.std():.3f} m, диапазон {low.min():.3f}..{low.max():.3f}; голова над стопой среднее {hi.mean():.2f} std {hi.std():.3f}")
