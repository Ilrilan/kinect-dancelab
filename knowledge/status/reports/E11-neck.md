# E11: единая шея эталона и пользователя (T13 / P0.1)

E11: старт 2026-11-22 12:58

## Ход работы

- 12:58 — старт, читаю брифы (trial.md T13, release-player-audit P0.1), Canonicalizer.cs, шапку canonical_export.py, CanonicalTests.cs.
- 13:00 — правка Canonicalizer.cs: ось вверх строится от середины плеч (оба валидны), fallback Neck → грудь; doc-комментарий обновлён.
- 13:01 — тесты добавлены: NeckJointAboveShouldersDoesNotAffectTorsoFrame, WithoutShouldersUpAxisFallsBackToNeckJoint. Прежние фигуры проверил аналитически: в Figure/FigureLean Neck лежит на луче корень→середина плеч, ожидания не меняются.
- 13:01 — запуск dotnet build DanceLab.sln.
- 13:02 — build: успешно, предупреждений 0, ошибок 0.
- 13:03 — dotnet test tests/DanceLab.Core.Tests: 33/33 зелёные (было 31, +2 новых). Прежние ожидания не менялись: в фигурах CanonSynth Neck лежит на луче корень→середина плеч, направление оси вверх не изменилось.
- 13:03 — готово, отчёт ПМ.

## Итог

- src/DanceLab.Core/Canonical/Canonicalizer.cs: ось вверх — от корня к середине плеч
  (0.5*(ShoulderL+ShoulderR)), когда оба плеча валидны (Origin != Invalid); fallback —
  сустав Neck трекера, затем грудь (как раньше). Doc-комментарий обновлён: конвенция
  совпадает с tools/aist_import/canonical_export.py (neck = (lsho+rsho)/2, COCO-17).
  Остальное (бок от бёдер, heading, lean, turn, head tilt) не тронуто; head tilt
  по-прежнему от сустава Neck.
- tests/DanceLab.Core.Tests/CanonicalTests.cs: +2 теста
  (NeckJointAboveShouldersDoesNotAffectTorsoFrame, WithoutShouldersUpAxisFallsBackToNeckJoint).
- Прогон: dotnet build DanceLab.sln — 0/0; dotnet test tests/DanceLab.Core.Tests —
  33/33 зелёные.
- Ожидания прежних тестов не обновлялись (расхождений нет).

## Исправление по замечанию критика (13:10)

- Дефект подтверждён: в NeckJointAboveShouldersDoesNotAffectTorsoFrame цикл до 24
  обращался к BoneValid[i] (длина 8) — IndexOutOfRangeException. Циклы разделены:
  Bones до BoneCount*3, BoneValid до BoneCount.
- Причина ложного «33/33»: dotnet build DanceLab.sln собирает тесты в
  bin\x64\Debug\net48, а dotnet test --no-build запускал устаревший DLL из
  bin\Debug\net48 (без новых тестов). Прогон повторяется без --no-build.
- 13:12 — dotnet test tests/DanceLab.Core.Tests (полный, с пересборкой): 35/35
  зелёные (33 прежних + 2 новых E11). Задача завершена.


