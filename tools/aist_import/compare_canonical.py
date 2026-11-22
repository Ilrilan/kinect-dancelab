"""Сравнение двух canonical.json одной последовательности (например smpl против coco).

Кадры сопоставляются по t (сек). Метрики:
- направления костей: угол между единичными векторами (градусы), по костям и суммарно;
- корень: |dforward|, |dside|, |dheight| в долях длины ноги, heading в градусах
  (разница свёрнута в -180..180);
- торс: lean, turn; голова: tilt (градусы).

Запуск: python compare_canonical.py <a.canonical.json> <b.canonical.json>
"""
import json
import sys

import numpy as np

BONES = ("uArmL", "uArmR", "lArmL", "lArmR", "thighL", "thighR", "shinL", "shinR")


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def ang_deg(u, v):
    d = np.clip(np.einsum("ijk,ijk->ij", u, v), -1.0, 1.0)
    return np.degrees(np.arccos(d))


def wrap_pi(x):
    return (x + np.pi) % (2 * np.pi) - np.pi


def main():
    a_path, b_path = sys.argv[1], sys.argv[2]
    a, b = load(a_path), load(b_path)
    fa = {f["t"]: f for f in a["frames"]}
    fb = {f["t"]: f for f in b["frames"]}
    common = sorted(set(fa) & set(fb))
    print(f"A: {a_path}")
    print(f"   source={a.get('source', '?')}, кадров {a['frameCount']}, "
          f"leg {a['legLengthCm']} см")
    print(f"B: {b_path}")
    print(f"   source={b.get('source', '?')}, кадров {b['frameCount']}, "
          f"leg {b['legLengthCm']} см")
    print(f"общих кадров по t: {len(common)} "
          f"(только в A: {len(fa) - len(common)}, только в B: {len(fb) - len(common)})")

    BA = np.array([[fa[t]["bones"][k] for k in BONES] for t in common])  # (n, 8, 3)
    BB = np.array([[fb[t]["bones"][k] for k in BONES] for t in common])
    ang = ang_deg(BA, BB)  # (n, 8)
    print("\nнаправления костей (угол между векторами A и B), радианы и градусы:")
    print(f"  {'кость':8s} {'ср,рад':>8s} {'макс,рад':>9s} {'ср,°':>7s} {'макс,°':>7s} {'p95,°':>7s}")
    for j, name in enumerate(BONES):
        print(f"  {name:8s} {np.radians(ang[:, j].mean()):8.4f} "
              f"{np.radians(ang[:, j].max()):9.4f} "
              f"{ang[:, j].mean():7.2f} {ang[:, j].max():7.2f} "
              f"{np.percentile(ang[:, j], 95):7.2f}")
    print(f"  {'итого':8s} {np.radians(ang.mean()):8.4f} {np.radians(ang.max()):9.4f} "
          f"{ang.mean():7.2f} {ang.max():7.2f} {np.percentile(ang, 95):7.2f}")

    for key, label in (("forward", "forward"), ("side", "side"), ("height", "height")):
        d = np.abs([fa[t]["root"][key] - fb[t]["root"][key] for t in common])
        print(f"root {label:8s} |dA-B|: среднее {d.mean():.4f}  макс {d.max():.4f}  (доли ноги)")

    h = np.degrees(np.abs(wrap_pi(np.array([fa[t]["root"]["heading"] for t in common])
                                 - np.array([fb[t]["root"]["heading"] for t in common]))))
    print(f"root heading |d|:  среднее {h.mean():6.2f}  макс {h.max():6.2f}  (градусы)")

    for sec, key in (("torso", "lean"), ("torso", "turn"), ("head", "tilt")):
        d = np.degrees(np.abs(np.array([fa[t][sec][key] for t in common])
                              - np.array([fb[t][sec][key] for t in common])))
        print(f"{sec} {key:5s} |d|:    среднее {d.mean():6.2f}  макс {d.max():6.2f}  (градусы)")


if __name__ == "__main__":
    main()
