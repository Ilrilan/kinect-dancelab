# E14: SMPL-импорт эталона (T16, Вариант B)

- Задача: добавить путь `--source smpl` в tools/aist_import/canonical_export.py (FK из smpl_poses), маппинг на 16 суставов, поле source в canonical.json, сравнение smpl vs coco на эталоне gLO_sBM_cAll_d14_mLO5_ch02.
- Бюджет: 100 минут. Не коммитить.
- Статус: в работе

## Лог

- [старт] Создан файл статуса.
- Прочитаны: specs/player/trial.md (Вариант B, T16), specs/core/canonical-pose.md (S12,
  таблица суставов Вариант A), docs/aist-format-findings.md (формула FK, масштаб, пол),
  tools/aist_import/{canonical_export,smpl_check,bench_cache,safe_pickle}.py.
- Модель найдена: data/smpl/SMPL_python_v.1.1.0/smpl/models/basicmodel_neutral_lbs_10_207_0_v1.1.0.pkl.
- Порядок суставов SMPL подтверждён по геометрии модели (x в сторону левого плеча,
  y-порядок позвоночника): 0 pelvis, 1/2 hip, 3 spine1, 4/5 knee, 6 spine2, 7/8 ankle,
  9 spine3, 12 neck, 15 head, 16/17 shoulder, 18/19 elbow, 20/21 wrist.
  chest = spine2 (6) — совпадает с таблицей canonical-pose Вариант A.
- venv: numpy 2.5.4, scipy 1.18.1 — OK.
- Написан tools/aist_import/smpl_fk.py (загрузка модели + FK), canonical_export.py:
  флаг --source smpl|coco (умолчание smpl), ветка SMPL (маппинг в раскладку COCO-17
  колонок 5..16, root=pelvis, head=сустав head), поле "source" в JSON,
  sanity-принт переведён с K[d] на P[0] (значения идентичны для coco).
- Прогоны: coco → data/derived/trial_coco_cmp (443 кадра, leg 71.11),
  smpl → data/derived/trial (443 кадра, leg 67.85, scaling 86.83, кости постоянны,
  NaN нет). Дальше: сравнение smpl vs coco.
