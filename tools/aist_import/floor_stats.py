import glob
import io
import os
import pickle
import sys
from collections import defaultdict

import numpy as np

ALLOWED = {
    ("numpy._core.multiarray", "_reconstruct"), ("numpy.core.multiarray", "_reconstruct"),
    ("numpy._core.multiarray", "scalar"), ("numpy.core.multiarray", "scalar"),
    ("numpy", "ndarray"), ("numpy", "dtype"),
}


class Safe(pickle.Unpickler):
    def find_class(self, module, name):
        if (module, name) in ALLOWED:
            return super().find_class(module, name)
        raise pickle.UnpicklingError(f"blocked: {module}.{name}")


kp_dir, mapping_path, out_csv = sys.argv[1], sys.argv[2], sys.argv[3]
setting = {}
with open(mapping_path, encoding="utf8") as f:
    for line in f:
        parts = line.split()
        if len(parts) == 2:
            setting[parts[0]] = parts[1]

LANK, RANK, LHIP, RHIP, NOSE = 15, 16, 11, 12, 0
rows = []
for p in sorted(glob.glob(os.path.join(kp_dir, "*.pkl"))):
    name = os.path.basename(p)[:-4]
    with open(p, "rb") as f:
        d = Safe(io.BytesIO(f.read())).load()
    K = d["keypoints3d_optim"]
    ank_y = np.minimum(K[:, LANK, 1], K[:, RANK, 1])
    hip_y = (K[:, LHIP, 1] + K[:, RHIP, 1]) / 2
    nose_y = K[:, NOSE, 1]
    leg = (np.linalg.norm(K[:, LHIP] - K[:, 13], axis=1) + np.linalg.norm(K[:, 13] - K[:, LANK], axis=1)).mean()
    rows.append(
        (name, setting.get(name, "?"), len(K), float(ank_y.min()), float(np.percentile(ank_y, 2)),
         float(np.percentile(ank_y, 50)), float(hip_y.min()), float(hip_y.max()), float(nose_y.min()), float(leg))
    )

with open(out_csv, "w", encoding="utf8") as f:
    f.write("name,setting,frames,ank_min,ank_p2,ank_p50,hip_min,hip_max,nose_min,leg\n")
    for r in rows:
        f.write(",".join(str(round(x, 2)) if isinstance(x, float) else str(x) for x in r) + "\n")

A = np.array([[r[3], r[4], r[5], r[6], r[7], r[8], r[9]] for r in rows])
print("sequences:", len(rows))
for i, lab in enumerate(["ank_min", "ank_p2", "ank_p50", "hip_min", "hip_max", "nose_min", "leg"]):
    c = A[:, i]
    print(f"{lab:8s} min {c.min():8.1f}  p5 {np.percentile(c,5):8.1f}  median {np.median(c):8.1f}  p95 {np.percentile(c,95):8.1f}  max {c.max():8.1f}")

by_set = defaultdict(list)
for r in rows:
    by_set[r[1]].append(r[3])
print("\nank_min per camera setting (count / mean / std / min / max):")
for k in sorted(by_set, key=lambda s: (len(s), s)):
    v = np.array(by_set[k])
    print(f"  {k:12s} n={len(v):4d} mean={v.mean():7.1f} std={v.std():5.1f} min={v.min():7.1f} max={v.max():7.1f}")

by_g = defaultdict(list)
for r in rows:
    by_g[r[0][1:3]].append(r[3])
print("\nank_min per genre:")
for k in sorted(by_g):
    v = np.array(by_g[k])
    print(f"  {k} n={len(v):4d} mean={v.mean():7.1f} std={v.std():5.1f} min={v.min():7.1f} max={v.max():7.1f}")
