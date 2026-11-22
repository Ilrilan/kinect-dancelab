# E13 — Режим подгонки позы на паузе (T15, S22)

Статус: в работе
Исполнитель: implementer E13
Разрешённые файлы: tools/player_trial/MainWindow.cs

## План
1. Прочитать specs/player/trial.md (S22, T15, гейт MinCoverageBones).
2. Прочитать tools/player_trial/MainWindow.cs (TogglePlay, UpdateLive, UpdateHud, TrialParams).
3. Реализовать: пауза = подгонка позы (мгновенный score01 по текущему кадру, сдвиг заморожен lastScore?.ShiftSec), HUD «поза: NN%» (FontSize +6), гейт Coverage*8 >= MinCoverageBones для краски и процента, скорер на паузе не обновляется.
4. dotnet build, dotnet test обоих проектов, снимок player_trial --no-sensor.

## Прогресс
- Прочитаны specs/player/trial.md (S22, T15), MainWindow.cs, TrialScorer.cs.
- Подтверждены сигнатуры: GhostCanvas.SetUser(CanonicalFrame?, double[]?), CanonicalFrame.BoneCount = 8,
  TrialComparison { BoneDeviationRad, MeanDeviationRad, Coverage }, TrialParams { DeadZone 0.35, Saturation 1.0, MinCoverageBones 4 }.
- Замечание: гуард скорера `!paused` и заморозка сдвига (`lastScore?.ShiftSec ?? 0` для сравнения) уже были в коде
  с E12; в E13 добавляются: гейт краски по MinCoverageBones, мгновенный процент позы, HUD паузы с крупной строкой.
- Правки в MainWindow.cs внесены (полный список в отчёте): using Documents, PoseLineFontSize=21, поля trialParams/livePose01/livePoseValid,
  UpdateLive (ResetUserDisplay/CoverageOk/PoseScore01 + гейт краски), UpdateHud (ветка paused с Inlines), оба места new TrialScorer(track, trialParams).
- Запущена сборка DanceLab.sln.

## Итог (2026-11-22)
- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Capture.Tests: 37/37 прошло (полный прогон, без --no-build).
- dotnet test tests/DanceLab.Core.Tests: 38/38 прошло (полный прогон, без --no-build).
- Снимок player_trial --no-sensor --shot data/preview/trial-shot-e13.png --shot-after 4: exit 0, окно не падает,
  HUD «без сенсора / Время / пол» цел, видео и эталон-призрак работают.
- Не подтверждено (нет железа): живой процент позы и гейт краски на паузе с реальным сенсором — проверено код-путём.
- Коммит не делался (по брифу).
