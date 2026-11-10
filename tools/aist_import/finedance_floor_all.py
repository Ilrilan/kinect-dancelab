"""Сводка по всем файлам FineDance: пол по нижней точке стопы, соглашение строки/y-вверх.

Использование: finedance_floor_all.py <smpl.pkl> <папка motion>
"""
import glob
import os
import sys

import numpy as np

sys.argv = [sys.argv[0], sys.argv[1]]  # finedance_check выполняет разбор при импорте, файлов не передаём
from finedance_check import fk, rotmats  # noqa: E402

rows = []
for p in sorted(glob.glob(os.path.join(os.environ["FD_MOTION"], "*.npy"))):
    d = np.load(p).astype(np.float64)
    P = fk(rotmats(d[:, 3:].reshape(len(d), 52, 6)[:, :22], "rows")) + d[:, None, :3]
    low = P[:, [7, 8, 10, 11], 1].min(1)
    rows.append((os.path.basename(p), len(d), low.std(), np.percentile(low, 2), np.median(low), d[:, 1].min(), d[:, 1].max()))
a = np.array([[r[2], r[3], r[4]] for r in rows])
print("файлов", len(rows))
print("std нижней точки стопы, м: p10/50/90/max", np.round(np.percentile(a[:, 0], [10, 50, 90, 100]), 3))
print("пол (2-й перцентиль), м: p10/50/90", np.round(np.percentile(a[:, 1], [10, 50, 90]), 3))
print("худшие по std:", [(r[0], round(r[2], 3)) for r in sorted(rows, key=lambda r: -r[2])[:8]])
print("диапазон trans y по файлам: min", round(min(r[5] for r in rows), 2), "max", round(max(r[6] for r in rows), 2))
