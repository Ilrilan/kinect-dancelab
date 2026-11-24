# E44: импортёр, формат версии 2 (T41)

- Статус: в работе
- Начато: 2026-11-24
- Бюджет: 35 минут, 1 файл (tools/aist_import/canonical_export.py)

## План

1. Прочитать canonical_export.py и спеку trial.md (раздел Contract, «Формат эталона, версия 2 (S39)»).
2. Правка export_one: двухпроходная схема (raw-азимут/надёжность → разворот heading → запись), formatVersion 2, сводка solveErr.
3. Проверка на трёх записях (_e44-check.txt), отчёт, уборка временных файлов.

## Ход работы

- [x] Спек trial.md S39 прочитан (строки 217-244), формулы совпадают с брифом.
- [x] canonical_export.py прочитан, правки внесены: docstring v2, wrap_rad,
      двухпроходный export_one (pre -> разворот heading -> запись),
      formatVersion 2, solveErr p99/max в сводке и stdout, roll/solveErr
      строки одиночного режима.
- [x] Проверка на трёх записях, уборка.

## Итог (2026-11-24)

- Готово: export_one переведён на формат v2 — первый проход (up/left/fwd,
  bones, turn, raw-азимут, надёжность w), разворот heading по треку
  (cont/branch 120°/k=clamp(w/0.25)), второй проход (F, L, lean, roll,
  basis 3x3, forward/side в осях развёрнутого heading), formatVersion 2,
  solveErrP99Deg/maxSolveErrDeg в tsv и stdout, строки roll/solveErr в
  одиночном режиме, docstring v2. Файл один: tools/aist_import/canonical_export.py.
- Проверка: батч из 3 записей (gLO_d13, gWA_d25, gBR_d04) — все выведены,
  solveErrP99Deg 0.007 у всех, maxSolveErrDeg 0.009-0.010 (порог 0.5 — с
  запасом). formatVersion 2, torso.basis из 9 чисел, head.tilt сохранён.
- Уборка: data/derived/_e44-check.txt и data/derived/_e44-out удалены;
  набор из 60 записей не перегенерировался (задача ПМ).
- Не коммитить, не пушить — приёмка ПМ (T41 продолжается перегенерацией набора).
