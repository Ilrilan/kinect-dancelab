"""Сводка по FineDance: жанры, длительность, показатели интенсивности движения (замена сложности).

Сложности в разметке нет. Показатели считаются по прямой кинематике тела (22 сустава, нейтральный SMPL):
скорость конечностей (запястья, лодыжки), охват (диапазон положения кистей), перемещение корня, доля кадров низко над полом.
Использование: FD_ROOT=<папка finedance> finedance_summary.py <smpl.pkl> <out.csv>
"""
import csv
import glob
import json
import os
import sys

import numpy as np

out = sys.argv[2]
sys.argv = [sys.argv[0], sys.argv[1]]
from finedance_check import fk, rotmats  # noqa: E402

root = os.environ["FD_ROOT"]
FPS = 30.0
rows = []
for p in sorted(glob.glob(os.path.join(root, "motion", "*.npy"))):
    n = os.path.basename(p)[:-4]
    d = np.load(p).astype(np.float64)
    lab = json.load(open(os.path.join(root, "label_json", n + ".json"), encoding="utf-8"))
    P = fk(rotmats(d[:, 3:].reshape(len(d), 52, 6)[:, :22], "rows")) + d[:, None, :3]
    P = P - P[:, :, 1:2].min() * 0  # без изменений
    floor = np.percentile(P[:, [7, 8, 10, 11], 1].min(1), 2)
    limbs = P[:, [20, 21, 7, 8]]                       # запястья, лодыжки
    sp = np.linalg.norm(np.diff(limbs, axis=0), axis=2) * FPS   # м/с
    sp = sp[:, :]
    pel = P[:, 0]
    rel = P - pel[:, None]
    reach = np.linalg.norm(rel[:, [20, 21]], axis=2).max(1)     # вынос кисти от таза
    pel_h = pel[:, 1] - floor
    path = np.linalg.norm(np.diff(pel[:, [0, 2]], axis=0), axis=1).sum()
    rows.append(dict(id=n, song=lab["name"], style1=lab["style1"], style2=lab["style2"], sec=round(len(d) / FPS, 1),
                     speed_mean=round(float(sp.mean()), 3), speed_p95=round(float(np.percentile(sp, 95)), 3),
                     reach_mean=round(float(reach.mean()), 3), pelvis_h_min=round(float(pel_h.min()), 3),
                     low_frac=round(float((pel_h < 0.6).mean()), 3), travel_m=round(float(path), 1),
                     travel_per_min=round(float(path / (len(d) / FPS / 60)), 1)))
with open(out, "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
    w.writeheader()
    w.writerows(rows)
print("записано", len(rows), out)
