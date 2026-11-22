"""Модель SMPL и прямая кинематика для canonical_export.py (--source smpl).

Формула проверена по модели (docs/aist-format-findings.md, раздел
«Формула размещения SMPL»): мировые суставы = FK(нулевая форма, суставы покоя
из J_regressor, вращения из smpl_poses) * smpl_scaling + smpl_trans.
Форма тела в motions не хранится, используется нулевая форма (betas = 0).

Порядок суставов подтверждён по геометрии модели (x в сторону левого плеча,
y-порядок позвоночника head>neck>spine3>spine2>spine1>pelvis, left суставы с +x):
0 pelvis, 1/2 hip_l/r, 3 spine1, 4/5 knee_l/r, 6 spine2, 7/8 ankle_l/r,
9 spine3, 10/11 foot_l/r, 12 neck, 13/14 collar_l/r, 15 head,
16/17 shoulder_l/r, 18/19 elbow_l/r, 20/21 wrist_l/r, 22/23 hand_l/r.
Родители (kintree_table): hip<-pelvis, spine1<-pelvis, knee<-hip, spine2<-spine1,
ankle<-knee, spine3<-spine2, foot<-ankle, neck<-spine3, collar<-spine3,
head<-neck, shoulder<-collar, elbow<-shoulder, wrist<-elbow, hand<-wrist.

Загрузка pickle через safe_pickle (белый список, код из файлов не исполняется).
"""
import numpy as np
from scipy.spatial.transform import Rotation

import safe_pickle

PELVIS = 0
L_HIP, R_HIP = 1, 2
SPINE1 = 3
L_KNEE, R_KNEE = 4, 5
SPINE2 = 6
L_ANKLE, R_ANKLE = 7, 8
SPINE3 = 9
L_FOOT, R_FOOT = 10, 11
NECK = 12
L_COLLAR, R_COLLAR = 13, 14
HEAD = 15
L_SHOULDER, R_SHOULDER = 16, 17
L_ELBOW, R_ELBOW = 18, 19
L_WRIST, R_WRIST = 20, 21
L_HAND, R_HAND = 22, 23


def load_model(model_path):
    """Нулевая форма модели: суставы покоя J (24, 3) в метрах (J_regressor на
    v_template) и массив родителей (корень -1)."""
    m = safe_pickle.load(model_path)
    v_template = safe_pickle.val(m["v_template"]).astype(np.float64)
    J = np.asarray(m["J_regressor"] @ v_template)          # (24, 3), метры
    parents = np.asarray(m["kintree_table"])[0].astype(np.int64)
    parents[0] = -1
    return J, parents


def joints_world(J, parents, poses, trans, scaling):
    """Прямая кинематика по кадрам (та же схема, что bench_cache.py).

    poses (N, 24, 3) axis-angle, trans (N, 3) в единицах сцены, scaling -- число.
    Возвращает мировые позиции (N, 24, 3) в единицах сцены (сантиметры).
    """
    n = poses.shape[0]
    R = Rotation.from_rotvec(poses.reshape(-1, 3)).as_matrix().reshape(n, 24, 3, 3)
    G = np.zeros((n, 24, 3, 3))
    P = np.zeros((n, 24, 3))
    for i in range(24):
        if parents[i] < 0:
            G[:, i] = R[:, i]
            P[:, i] = J[i]
        else:
            p = parents[i]
            G[:, i] = G[:, p] @ R[:, i]
            P[:, i] = P[:, p] + (G[:, p] @ (J[i] - J[p])[:, None])[..., 0]
    return P * scaling + trans[:, None, :]
