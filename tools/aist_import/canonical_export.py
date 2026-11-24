"""Экспорт канонической позы AIST++ в <seq>.canonical.json (контракт specs/player/trial.md).

--source smpl (умолчание для --seq, canonical-pose S12, основной путь): прямая
кинематика SMPL по smpl_poses из data/aist/extracted/motions/<seq>.pkl. Модель
(нулевая форма) в data/smpl/.../basicmodel_neutral_lbs_10_207_0_v1.1.0.pkl;
формула FK и проверка -- docs/aist-format-findings.md (расхождение с точками
около 3 см). Маппинг суставов (порядок подтверждён по модели, см. smpl_fk.py;
canonical-pose Вариант A):
  root  = pelvis (0)          chest = spine2 (6)   -- в JSON не входит
  neck  = середина плеч SMPL (16, 17) -- конвенция T13, как у живой позы
  head  = head (15)
  плечо/локоть/запястье = 16/17, 18/19, 20/21
  бедро/колено/лодыжка = 1/2, 4/5, 7/8

--source coco (прежний путь пробы, без изменений): сглаженные точки COCO-17 из
data/aist/extracted/keypoints3d/<seq>.pkl, ключ keypoints3d_optim (N, 17, 3),
сантиметры, ось y вверх, x в сторону левого плеча, лицо по +z
(docs/aist-format-findings.md).

--source auto (умолчание для --batch): smpl, если существует
motions/<seq>.pkl в --src-smpl, иначе coco. Явные smpl и coco работают как
раньше (в том числе прежнее сообщение об отсутствии файла движений).

Построение из COCO-17 (по Contract в trial.md):
  root  = середина бёдер (lhip, rhip)
  neck  = середина плеч (lsho, rsho)  -- приближение пробы
  chest = середина root-neck (в JSON не входит, нужны были для контракта C#)
  head  = середина ушей (lear, rear), если недоступно -- нос

Система торса (правая тройка, приближение canonical-pose S7):
  up  = normalize(neck - root)
  left = линия бёдер (lhip - rhip), ортогонализованная по up, единичная
  forward = cross(left, up)          (left x up = forward, как x cross y = z)
Направления костей -- единичные векторы в координатах (left, up, forward).
Перемещение корня -- в долях длины ноги (canonical-pose S9), относительно первого
валидного кадра, оси текущего heading: forward = по направлению взгляда,
side = влево от взгляда, height = над полом.
Пол: 2-й перцентиль высоты низшей лодыжки минус смещение лодыжка-подошва
(6.4 см; 0.0705 * smpl_scaling, scaling 90-95 -> 6.2-6.6, docs/aist-format-findings.md).
Если оценка пола отличается от глобальной медианы (71.6) больше чем на 10 см --
глобальная медиана минус то же смещение (правило из findings).

Кадры, где нужные точки NaN, пропускаются с подсчётом в stdout.

Рядом с canonical.json пишется <seq>.cams.json (следящий кадр, trial S21/T12):
по каждой из камер c01..c09 (ось 0 keypoints2d, docs/aist-format-findings.md,
ключ (9, N, 17, 3)) кадры {"t", "root":[u,v], "bbox":[uMin,vMin,uMax,vMax]}:
root -- центр бёдер (COCO 11/12), bbox -- по валидным точкам кадра
(score >= 0.3); кадры с невалидными бёдрами пропускаются; t -- из timestamps
pkl (микросекунды), в JSON переводится в секунды (так же, как t в canonical.json).
Формат: {"formatVersion":1, "fps":60, "frameCount":N,
"cameras":{"c01":{"frames":[{"t":..,"root":[u,v],"bbox":[..]},...]},...}}.

Режимы запуска:
  python canonical_export.py --seq <имя cAll> [--source smpl|coco]
      [--src ...] [--src-smpl ...] [--smpl-model ...] [--src2d ...] [--out ...]
    одиночная запись, прежний контрольный вывод и санити дословно.
  python canonical_export.py --batch <файл> [--source smpl|coco|auto] [остальные как выше]
    список последовательностей: первый токен строки -- имя с cAll, пустые строки
    и строки с # пропускаются; неудачная запись не останавливает остальные.
    Пишет <out>/batch-summary.tsv (заголовок + строка на запись), столбцы:
      seq, source, frames, skipped, durationSec, legLenCm, floorFlag,
      minRootHeight, maxDHeadingDeg, headingJumpFrames, maxDBoneDeg,
      boneJumpFrames
    durationSec = frames/60 (0.01); legLenCm как в canonical.json; floorFlag = 1,
    если пол взят глобальный (ветка «подозрителен»); minRootHeight -- минимум
    root.height (4 знака). Скачки -- между соседними выведенными кадрами:
    maxDHeadingDeg -- максимум |Δheading| в градусах (разница свёрнута в
    -180..180), headingJumpFrames -- число пар с |Δheading| > 45°,
    maxDBoneDeg -- максимум угла между направлениями одной кости по восьми
    костям, boneJumpFrames -- число пар, где хотя бы одна кость повернулась
    больше 60°. В stdout строка на запись и итог. Код возврата: 0 -- все записи
    выведены, 1 -- хоть одна не удалась (список в stdout).
Порядок COCO-17: nose, leye, reye, lear, rear, lsho, rsho, lelb, relb,
lwri, rwri, lhip, rhip, lkne, rkne, lank, rank.
В JSON пишется поле "source" ("smpl" или "coco"); бандл фиксирует источник
(canonical-pose S12). C#-загрузчик незнакомые поля пропускает.
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import safe_pickle
import smpl_fk

# при пайпе stdout в Windows может получить кодировку без символов вроде ‖;
# замещаем некодируемые символы, а не роняем прогон после записи JSON
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass

NOSE, LEYE, REYE, LEAR, REAR = 0, 1, 2, 3, 4
LSHO, RSHO, LELB, RELB, LWRI, RWRI = 5, 6, 7, 8, 9, 10
LHIP, RHIP, LKNE, RKNE, LANK, RANK = 11, 12, 13, 14, 15, 16

# (имя в JSON, из сустава, в сустав)
BONES = [
    ("uArmL", LSHO, LELB), ("uArmR", RSHO, RELB),
    ("lArmL", LELB, LWRI), ("lArmR", RELB, RWRI),
    ("thighL", LHIP, LKNE), ("thighR", RHIP, RKNE),
    ("shinL", LKNE, LANK), ("shinR", RKNE, RANK),
]
CORE_POINTS = [LSHO, RSHO, LELB, RELB, LWRI, RWRI, LHIP, RHIP, LKNE, RKNE, LANK, RANK]
# SMPL-суставы -> колонки 5..16 раскладки COCO-17 (lsho..rank), см. smpl_fk.py
SMPL_TO_COCO = (smpl_fk.L_SHOULDER, smpl_fk.R_SHOULDER,
                smpl_fk.L_ELBOW, smpl_fk.R_ELBOW,
                smpl_fk.L_WRIST, smpl_fk.R_WRIST,
                smpl_fk.L_HIP, smpl_fk.R_HIP,
                smpl_fk.L_KNEE, smpl_fk.R_KNEE,
                smpl_fk.L_ANKLE, smpl_fk.R_ANKLE)
DEFAULT_SMPL_MODEL = os.path.join("data", "smpl", "SMPL_python_v.1.1.0", "smpl", "models",
                                  "basicmodel_neutral_lbs_10_207_0_v1.1.0.pkl")
FPS = 60.0                  # findings: 720 кадров на 12 с
CAM_SCORE_MIN = 0.3         # следящий кадр (S21/T12): бёдра с меньшим score пропускаются
FLOOR_P2_GLOBAL = 71.6      # глобальная медиана ank_p2, см (findings)
SOLE_OFFSET_CM = 6.4        # лодыжка-подошва, середина 6.2-6.6 (findings)
WORLD_UP = np.array([0.0, 1.0, 0.0])
# пороги скачков сводки (trial.md, Contract «Прогон по жанрам»)
HEADING_JUMP_DEG = 45.0     # |Δheading| между соседними кадрами, выше -- скачок
BONE_JUMP_DEG = 60.0        # поворот кости между соседними кадрами, выше -- скачок


class ExportError(Exception):
    """Ошибка построения одной записи (нет файла, неожиданные данные)."""


def unit(v):
    n = np.linalg.norm(v)
    return v / n


def frame_coords(v, left, up, fwd):
    """Мировой вектор -> координаты (left, up, forward)."""
    return np.array([np.dot(v, left), np.dot(v, up), np.dot(v, fwd)])


def wrap_deg(d):
    """Угол в градусах, свёрнутый в -180..180."""
    return (d + 180.0) % 360.0 - 180.0


def jump_stats(frames):
    """Скачки между соседними выведенными кадрами (по порядку списка frames).

    Возвращает (maxDHeadingDeg, headingJumpFrames, maxDBoneDeg, boneJumpFrames):
    максимум |Δheading| в градусах и число пар с |Δheading| > HEADING_JUMP_DEG;
    максимум угла между направлениями одной кости по восьми костям и число пар,
    где хотя бы одна кость повернулась больше BONE_JUMP_DEG.
    """
    max_dh = 0.0
    head_jumps = 0
    max_dbone = 0.0
    bone_jumps = 0
    for i in range(1, len(frames)):
        prev, cur = frames[i - 1], frames[i]
        dh = abs(wrap_deg(np.degrees(cur["root"]["heading"] - prev["root"]["heading"])))
        if dh > max_dh:
            max_dh = dh
        if dh > HEADING_JUMP_DEG:
            head_jumps += 1
        any_bone = False
        for name, _, _ in BONES:
            a, b = prev["bones"][name], cur["bones"][name]
            dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
            ang = np.degrees(np.arccos(max(-1.0, min(1.0, dot))))
            if ang > max_dbone:
                max_dbone = float(ang)
            if ang > BONE_JUMP_DEG:
                any_bone = True
        if any_bone:
            bone_jumps += 1
    return max_dh, head_jumps, max_dbone, bone_jumps


def export_cams(seq, kp2d_path, out_dir):
    """<seq>.cams.json для следящего кадра (trial S21/T12): по камерам c01..c09 кадры
    {"t": сек, "root": [u,v] (центр бёдер COCO 11/12), "bbox": [uMin,vMin,uMax,vMax] --
    по валидным (score >= CAM_SCORE_MIN, конечные) ключевым точкам COCO-17 кадра},
    t в секундах из timestamps (микросекунды). Кадры с невалидными бёдрами (нет root)
    пропускаются. Возвращает (путь, кадров всего, список пропущенных по камерам)."""
    data = safe_pickle.load(kp2d_path)
    if "keypoints2d" not in data or "timestamps" not in data:
        raise ExportError("нет ключей keypoints2d/timestamps: " + kp2d_path)
    K = np.asarray(data["keypoints2d"], dtype=np.float64)
    ts = np.asarray(data["timestamps"], dtype=np.float64).ravel()
    if K.ndim != 4 or K.shape[0] < 9:
        raise ExportError(f"ключ keypoints2d неожиданной формы {K.shape} (нужна (9, N, 17, 3)): " + kp2d_path)

    n_frames = min(K.shape[1], len(ts))
    cams = {}
    skipped = []
    for c in range(9):
        frames = []
        n_skip = 0
        for i in range(n_frames):
            l = K[c, i, LHIP]
            r = K[c, i, RHIP]
            if not (np.isfinite(l[:2]).all() and np.isfinite(r[:2]).all()) or \
                    min(l[2], r[2]) < CAM_SCORE_MIN:
                n_skip += 1
                continue
            pts = K[c, i]  # (17, 3): x, y, score
            ok = np.isfinite(pts[:, :2]).all(axis=1) & (pts[:, 2] >= CAM_SCORE_MIN)
            if not ok.any():
                n_skip += 1
                continue
            xy = pts[ok, :2]
            bbox = [float(xy[:, 0].min()), float(xy[:, 1].min()),
                    float(xy[:, 0].max()), float(xy[:, 1].max())]
            frames.append({
                "t": round(float(ts[i] / 1e6), 4),
                "root": [round((float(l[0]) + float(r[0])) / 2.0, 1),
                         round((float(l[1]) + float(r[1])) / 2.0, 1)],
                "bbox": [round(v, 1) for v in bbox],
            })
        cams[f"c{c + 1:02d}"] = {"frames": frames}
        skipped.append(n_skip)

    result = {
        "formatVersion": 1,
        "fps": int(FPS),
        "frameCount": n_frames,
        "cameras": cams,
    }
    out_path = os.path.join(out_dir, seq + ".cams.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    return out_path, n_frames, skipped


def export_one(seq, source, args, repo, verbose=True):
    """Строит и пишет <seq>.canonical.json и <seq>.cams.json для одной записи.

    source "auto": smpl, если существует motions/<seq>.pkl в --src-smpl, иначе coco.
    verbose=True печатает прежний контрольный вывод одиночного --seq (дословно).
    Ошибки -- ExportError (одиночный режим превращает её в sys.exit с тем же
    текстом; в пакетном режиме запись помечается неудавшейся, остальные идут).
    Возвращает строку сводки (столбцы batch-summary.tsv) словарём.
    """
    src = args.src if os.path.isabs(args.src) else os.path.join(repo, args.src)
    src_smpl = args.src_smpl if os.path.isabs(args.src_smpl) else os.path.join(repo, args.src_smpl)
    smpl_model = args.smpl_model if os.path.isabs(args.smpl_model) else os.path.join(repo, args.smpl_model)
    src2d = args.src2d if os.path.isabs(args.src2d) else os.path.join(repo, args.src2d)
    out_dir = args.out if os.path.isabs(args.out) else os.path.join(repo, args.out)

    if source == "auto":
        source = "smpl" if os.path.isfile(os.path.join(src_smpl, seq + ".pkl")) else "coco"

    if source == "smpl":
        motion_path = os.path.join(src_smpl, seq + ".pkl")
        if not os.path.isfile(motion_path):
            raise ExportError(f"нет файла движений SMPL: {motion_path} "
                              f"(для этой последовательности можно запустить с --source coco)")
        if not os.path.isfile(smpl_model):
            raise ExportError(f"нет модели SMPL: {smpl_model}")
        mo = safe_pickle.load(motion_path)
        poses = np.asarray(mo["smpl_poses"], dtype=np.float64).reshape(-1, 24, 3)
        trans = np.asarray(mo["smpl_trans"], dtype=np.float64)
        scaling = float(np.asarray(mo["smpl_scaling"], dtype=np.float64).ravel()[0])
        total = len(poses)

        # ---- отбор кадров: NaN во вращениях/перемещении ----
        ok = np.isfinite(poses.reshape(total, -1)).all(axis=1) & np.isfinite(trans).all(axis=1)
        idx = np.flatnonzero(ok)
        n_nan = total - len(idx)

        J, parents = smpl_fk.load_model(smpl_model)
        W = smpl_fk.joints_world(J, parents, poses[idx], trans[idx], scaling)  # (n, 24, 3), см
        P = np.zeros((len(idx), 17, 3))
        P[:, 5:17] = W[:, SMPL_TO_COCO]       # lsho..rank из суставов SMPL
        H = W[:, smpl_fk.HEAD]                # голова -- сустав head
        root = W[:, smpl_fk.PELVIS]           # корень -- сустав pelvis (S12/Вариант A)
    else:
        pkl_path = os.path.join(src, seq + ".pkl")
        if not os.path.isfile(pkl_path):
            raise ExportError(f"нет входного файла: {pkl_path}")

        data = safe_pickle.load(pkl_path)
        K = np.asarray(data["keypoints3d_optim"], dtype=np.float64)
        total = len(K)

        # ---- отбор кадров: ядро скелета + голова (уши, иначе нос) ----
        core_ok = np.isfinite(K[:, CORE_POINTS]).all(axis=(1, 2))
        ears_ok = np.isfinite(K[:, LEAR]).all(axis=1) & np.isfinite(K[:, REAR]).all(axis=1)
        nose_ok = np.isfinite(K[:, NOSE]).all(axis=1)
        head_pts = np.full((len(K), 3), np.nan)
        head_pts[ears_ok] = (K[ears_ok, LEAR] + K[ears_ok, REAR]) / 2.0
        head_pts[~ears_ok & nose_ok] = K[~ears_ok & nose_ok, NOSE]
        head_ok = np.isfinite(head_pts).all(axis=1)
        keep = core_ok & head_ok
        idx = np.flatnonzero(keep)
        n_nan = total - len(idx)

        P = K[keep]
        H = head_pts[keep]

        root = (P[:, LHIP] + P[:, RHIP]) / 2.0
    neck = (P[:, LSHO] + P[:, RSHO]) / 2.0

    # ---- длины костей (мировые, см); rotation сохраняет длину ----
    lengths = {}
    for name, a, b in BONES:
        lengths[name] = np.linalg.norm(P[:, b] - P[:, a], axis=1)

    leg_per_side = np.linalg.norm(P[:, LKNE] - P[:, LHIP], axis=1) + \
        np.linalg.norm(P[:, LANK] - P[:, LKNE], axis=1), \
        np.linalg.norm(P[:, RKNE] - P[:, RHIP], axis=1) + \
        np.linalg.norm(P[:, RANK] - P[:, RKNE], axis=1)
    leg_all = np.concatenate(leg_per_side)
    leg_len = float(np.median(leg_all))

    # ---- пол (findings: правило оценки) ----
    ank_min = np.minimum(P[:, LANK, 1], P[:, RANK, 1])
    floor_p2 = float(np.percentile(ank_min, 2))
    floor_global = abs(floor_p2 - FLOOR_P2_GLOBAL) > 10.0
    if floor_global:
        floor_y = FLOOR_P2_GLOBAL - SOLE_OFFSET_CM
        floor_flag = "  [ВНИМАНИЕ: пол по данным подозрителен, взят глобальный]"
    else:
        floor_y = floor_p2 - SOLE_OFFSET_CM
        floor_flag = ""

    root0 = root[0]
    bones_out_dev = 0.0
    frames = []
    for i in range(len(P)):
        up = unit(neck[i] - root[i])
        hl = P[i, LHIP] - P[i, RHIP]
        hl_o = hl - up * np.dot(hl, up)
        left = unit(hl_o)
        fwd = np.cross(left, up)

        bones = {}
        for name, a, b in BONES:
            v = unit(P[i, b] - P[i, a])
            bones_out_dev = max(bones_out_dev, abs(np.linalg.norm(v) - 1.0))
            bones[name] = frame_coords(v, left, up, fwd)

        sl = P[i, LSHO] - P[i, RSHO]
        sl_o = sl - up * np.dot(sl, up)
        # поворот плечевой линии относительно бёдерно-заданной системы торса
        turn = float(np.arctan2(np.dot(sl_o, fwd), np.dot(sl_o, left)))

        fwd_h = fwd - WORLD_UP * np.dot(fwd, WORLD_UP)
        fwd_h = unit(fwd_h)
        # наклон торса от вертикали в сагиттальной плоскости (вперёд -- плюс)
        lean = float(np.arctan2(np.dot(up, fwd_h), float(np.dot(up, WORLD_UP))))
        heading = float(np.arctan2(fwd_h[0], fwd_h[2]))
        left_h = np.cross(WORLD_UP, fwd_h)  # y cross z = x, влево

        hd = unit(H[i] - neck[i])
        tilt = float(np.arctan2(np.dot(hd, fwd_h), np.dot(hd, up)))

        dr = root[i] - root0
        frames.append({
            "t": round(idx[i] / FPS, 4),
            "bones": {k: [round(float(x), 4) for x in bones[k]] for k in
                      ("uArmL", "uArmR", "lArmL", "lArmR", "thighL", "thighR", "shinL", "shinR")},
            "torso": {"lean": round(lean, 4), "turn": round(turn, 4)},
            "head": {"tilt": round(tilt, 4)},
            "root": {
                "forward": round(float(np.dot(dr, fwd_h)) / leg_len, 4),
                "side": round(float(np.dot(dr, left_h)) / leg_len, 4),
                "height": round(float(root[i, 1] - floor_y) / leg_len, 4),
                "heading": round(heading, 4),
            },
        })

    result = {
        "formatVersion": 1,
        "source": source,
        "sequence": seq,
        "video": seq.replace("cAll", "c05") + ".mp4",
        "fps": 60,
        "frameCount": len(frames),
        "legLengthCm": round(leg_len, 1),
        "frames": frames,
    }
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, seq + ".canonical.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)

    # ---- <seq>.cams.json: следящий кадр (S21/T12), если keypoints2d на месте ----
    kp2d_path = os.path.join(src2d, seq + ".pkl")
    if os.path.isfile(kp2d_path):
        cams_path, cams_frames, cams_skipped = export_cams(seq, kp2d_path, out_dir)
        if verbose:
            print(f"cams.json: {cams_path}")
            print(f"  камер: {len(cams_skipped)}, кадров: {cams_frames}, "
                  f"пропущено по камерам c01..c09: {cams_skipped}")
    else:
        if verbose:
            print(f"cams.json не записан: нет {kp2d_path}")

    # ---- сводка (столбцы batch-summary.tsv) ----
    max_dh, head_jumps, max_dbone, bone_jumps = jump_stats(frames)
    summary = {
        "seq": seq,
        "source": source,
        "frames": len(frames),
        "skipped": n_nan,
        "durationSec": round(len(frames) / FPS, 2),
        "legLenCm": round(leg_len, 1),
        "floorFlag": 1 if floor_global else 0,
        "minRootHeight": round(min(fr["root"]["height"] for fr in frames), 4),
        "maxDHeadingDeg": round(max_dh, 2),
        "headingJumpFrames": head_jumps,
        "maxDBoneDeg": round(max_dbone, 2),
        "boneJumpFrames": bone_jumps,
    }
    if not verbose:
        return summary

    # ---- контрольные числа ----
    print(f"последовательность: {seq}")
    print(f"источник: {source}"
          + (f", smpl_scaling {scaling:.2f}" if source == "smpl" else ""))
    skip_why = "NaN" if source == "smpl" else "NaN/голова"
    print(f"кадров всего: {total}, пропущено ({skip_why}): {n_nan}, выведено: {len(frames)}")
    if floor_flag:
        print(f"пол: p2 лодыжек {floor_p2:.1f} см -> floor_y {floor_y:.1f} см{floor_flag}")
    else:
        print(f"пол: p2 лодыжек {floor_p2:.1f} см -> floor_y {floor_y:.1f} см")
    print(f"медиана длины ноги (бедро+голень, оба борта): {leg_len:.2f} см")
    print("длины костей, см (мин..макс):")
    for name in ("uArmL", "uArmR", "lArmL", "lArmR", "thighL", "thighR", "shinL", "shinR"):
        v = lengths[name]
        print(f"  {name:7s} {v.min():6.2f} .. {v.max():6.2f}  медиана {np.median(v):6.2f}")
    print(f"единичность bones: макс |‖v‖-1| = {bones_out_dev:.2e}")

    # ---- санити ----
    sho_hip = float(np.mean((neck[:, 1] - root[:, 1])))
    d = idx[0]
    print("--- санити ---")
    print(f"плечи выше бёдер в среднем на {sho_hip:.2f} см (ось y вверх): "
          + ("ок" if sho_hip > 0 else "ПРОВАЛ"))
    print(f"кадр {d}: lhip.x - rhip.x = {P[0, LHIP, 0] - P[0, RHIP, 0]:+.2f}, "
          f"lsho.x - rsho.x = {P[0, LSHO, 0] - P[0, RSHO, 0]:+.2f} "
          f"(x в сторону левого плеча: ожидался плюс)")
    for pair in (("uArmL", "uArmR"), ("lArmL", "lArmR"), ("thighL", "thighR"), ("shinL", "shinR")):
        ml, mr = np.median(lengths[pair[0]]), np.median(lengths[pair[1]])
        print(f"  медианы {pair[0]}={ml:.2f} vs {pair[1]}={mr:.2f} см "
              f"(асимметрия {abs(ml - mr) / max(ml, mr) * 100:.1f}%)")
    print(f"heading первого кадра: {frames[0]['root']['heading']:+.3f} рад "
          f"(лицо по +z: ожидалось около 0)")
    print(f"выход: {out_path}")
    return summary


def run_batch(args, repo):
    """Режим --batch: список последовательностей -> JSON-файлы + batch-summary.tsv.

    Формат списка: первый токен строки -- имя с cAll, пустые строки и строки с #
    пропускаются. Неудачная запись не останавливает остальные; в stdout строка на
    запись и итог. Возвращает код возврата: 0 -- все записи выведены, 1 -- хоть
    одна не удалась (список в stdout).
    """
    batch_path = args.batch if os.path.isabs(args.batch) else os.path.join(repo, args.batch)
    if not os.path.isfile(batch_path):
        sys.exit(f"нет файла списка: {batch_path}")
    seqs = []
    with open(batch_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            seqs.append(line.split()[0])
    if not seqs:
        print(f"в {batch_path} нет ни одной записи")
        return 1

    out_dir = args.out if os.path.isabs(args.out) else os.path.join(repo, args.out)
    os.makedirs(out_dir, exist_ok=True)
    columns = ("seq", "source", "frames", "skipped", "durationSec", "legLenCm",
               "floorFlag", "minRootHeight", "maxDHeadingDeg", "headingJumpFrames",
               "maxDBoneDeg", "boneJumpFrames")
    rows = []
    failed = []
    for seq in seqs:
        try:
            r = export_one(seq, args.source, args, repo, verbose=False)
        except Exception as e:  # одна неудачная запись не останавливает остальные
            failed.append(seq)
            print(f"НЕ УДАЛАСЬ {seq}: {e}")
            continue
        rows.append(r)
        print(f"{seq}: источник {r['source']}, кадров {r['frames']}, "
              f"пропущено {r['skipped']}, {r['durationSec']:.2f} с, "
              f"floorFlag {r['floorFlag']}, "
              f"maxDHeadingDeg {r['maxDHeadingDeg']:.2f}, "
              f"headingJumpFrames {r['headingJumpFrames']}, "
              f"maxDBoneDeg {r['maxDBoneDeg']:.2f}, "
              f"boneJumpFrames {r['boneJumpFrames']}")

    tsv_path = os.path.join(out_dir, "batch-summary.tsv")
    with open(tsv_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(columns) + "\n")
        for r in rows:
            f.write("\t".join([
                r["seq"], r["source"], str(r["frames"]), str(r["skipped"]),
                f"{r['durationSec']:.2f}", f"{r['legLenCm']:.1f}", str(r["floorFlag"]),
                f"{r['minRootHeight']:.4f}", f"{r['maxDHeadingDeg']:.2f}",
                str(r["headingJumpFrames"]), f"{r['maxDBoneDeg']:.2f}",
                str(r["boneJumpFrames"]),
            ]) + "\n")
    print(f"сводка: {tsv_path}")
    print(f"итог: записей {len(seqs)}, выведено {len(rows)}, не удалось {len(failed)}"
          + (": " + ", ".join(failed) if failed else ""))
    return 1 if failed else 0


def main():
    ap = argparse.ArgumentParser(description="AIST++ SMPL FK / keypoints3d_optim -> canonical.json (+ .cams.json)")
    group = ap.add_mutually_exclusive_group(required=True)
    group.add_argument("--seq", help="имя последовательности с cAll (одиночный режим)")
    group.add_argument("--batch", help="файл со списком последовательностей: первый токен строки -- "
                        "имя с cAll, пустые строки и строки с # пропускаются")
    ap.add_argument("--source", choices=("smpl", "coco", "auto"), default=None,
                    help="источник эталона: smpl = FK по smpl_poses (canonical-pose S12, "
                         "основной путь), coco = keypoints3d_optim (прежний путь пробы), "
                         "auto = smpl, если есть motions/<seq>.pkl, иначе coco "
                         "(умолчание: smpl для --seq, auto для --batch)")
    ap.add_argument("--src", default="data/aist/extracted/keypoints3d",
                    help="каталог keypoints3d (для --source coco)")
    ap.add_argument("--src-smpl", default="data/aist/extracted/motions",
                    help="каталог motions pkl (для --source smpl)")
    ap.add_argument("--smpl-model", default=DEFAULT_SMPL_MODEL,
                    help="файл модели SMPL (нулевая форма)")
    ap.add_argument("--src2d", default="data/aist/extracted/keypoints2d",
                    help="каталог keypoints2d для <seq>.cams.json (следящий кадр, S21/T12)")
    ap.add_argument("--out", default="data/derived/trial")
    args = ap.parse_args()
    # умолчание источника зависит от режима: smpl для одиночного, auto для пакетного
    if args.source is None:
        args.source = "auto" if args.batch else "smpl"

    repo = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

    if args.batch:
        sys.exit(run_batch(args, repo))

    try:
        export_one(args.seq, args.source, args, repo, verbose=True)
    except ExportError as e:
        sys.exit(str(e))


if __name__ == "__main__":
    main()
