# E36: пакетный импорт набора (T33)

Статус: в работе (2026-11-24)

Бюджет: 1 файл кода (tools/aist_import/canonical_export.py), 40 минут.

## План
1. Прочитать canonical_export.py, smpl_fk.py, safe_pickle.py, trial.md (Contract, «Прогон по жанрам»), trial-set.txt.
2. Добавить --batch, --source auto, export_one(), batch-summary.tsv, stdout-итог, код возврата.
3. Проверка: batch из 3 записей (smpl, smpl, coco), одиночный прогон, ошибки argparse.
4. Удалить временные артефакты, обновить docstring.

## Ход работы
- 2026-11-24: задача получена, файл статуса создан.
- Прочитаны canonical_export.py, smpl_fk.py, safe_pickle.py, trial.md (Contract
  «Прогон по жанрам»), trial-set.txt.
- Окружение: системный python без scipy; найден venv
  tools/aist_import/.venv (Python 3.13, numpy 2.5.4, scipy 1.18.1) — как в
  T1-importer. Модель SMPL на месте. Данные для проверки: motions есть для
  gLO_sBM_cAll_d13_mLO0_ch01 и gWA_sFM_cAll_d25_mWA0_ch01, для
  gWA_sFM_cAll_d25_mWA1_ch02 motions НЕТ (auto -> coco), keypoints3d/2d есть
  для всех трёх.
- Правка canonical_export.py: ExportError, export_one(seq, source, args, repo,
  verbose), _wrap_deg/_jump_stats, run_batch, argparse (группа --seq/--batch,
  --source auto), docstring.

## Итог ПМ (2026-11-24)
Исполнитель остановлен ПМ по признаку дрейфа после готовности кода (файл статуса не рос около 30 минут). Код закончен. Приёмка: ревью ПМ по диффу; второй контур собран по артефактам трёх запусков критика (двое остановлены по дрейфу): вывод одиночного режима старой и новой версии различается только путями, canonical.json и cams.json побайтово совпадают; коды возврата argparse 2 и 2, отсутствие списка 1 без стека; пакетный прогон 3/2/1 с источниками smpl и coco; независимый пересчёт скачков совпадает со сводкой (140.12, 1, 34.28, 0). Импорт набора: 60 из 60.
