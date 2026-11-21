# T3 плеер: старт 2026-11-21

Задача: tools/player_trial — WPF-окно пробного плеера (кодом, без XAML), net48.
Бюджет: 90 минут. Статус: выполнено, ~70 мин.

## План
- [x] Прочитать specs/player/trial.md, tools/sensor_view/, src/DanceLab.Core/Canonical
- [x] Создать tools/player_trial (csproj, Program, MainWindow, GhostCanvas, PlayerSession, MultiUserReconstructor, Topology)
- [x] Зарегистрировать в DanceLab.sln (dotnet sln add)
- [x] dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок
- [x] player_trial --no-sensor --shot data/preview/trial-shot.png --shot-after 3 — png 264862 байт, выход 0

## Прогресс
- прочитаны: trial.md, sensor_view (все файлы), Canonical (Frame/Track/Canonicalizer/TrialComparer/TrialScorer/Vec3), Capture Frames.cs, Reconstruction (Types/Conversion/PoseReconstructor)
- эталон data/derived/trial/gLO_sBM_cAll_d14_mLO5_ch02.canonical.json существует (T1/T2 готовы), видео data/aist/video/10M/gLO_sBM_c05_d14_mLO5_ch02.mp4 есть
- написаны 7 файлов tools/player_trial; сборка после 3 фиксов (using System, ErrorException, double*GhostVec)
- скриншот 1: призрак и видео работают, но кнопка сенсора по умолчанию «Стоп сенсора» и статус «Загрузка...» при --no-sensor — исправлено
- скриншот 2 (финальный): статус «Идёт проба ... без сенсора», кнопка «Старт сенсора», HUD «без сенсора / Время 00:02» по часам MediaElement

## Решения по брифу
- пробные доли длины ноги (GhostLengths, константы с комментарием): бедро/голень по 0.5*L, плечо 0.35*L, предплечье 0.28*L, полу-плечи 0.18*L, полу-бёдра 0.10*L, торс 0.50*L, шея-голова 0.23*L, радиус головы 0.11*L
- экран призрака: зеркало «лицом к танцору» (экранный x = -мировой x), пол на 0.86*h, масштаб h/3.2
- калибровка: медианы длин костей по LimbLengths реконструктора за первые 5 с живого скелета, дедуп по DeviceTimeUs; до калибровки оба призрака на пробных долях от track.LegLengthM
- не вошло (нет в брифе T3): запись сессии по кнопке (spec Integration упоминает LiveRecorder) — отмечено в отчёте как вне объёма

## Не подтверждено
- сенсорный путь (Kinect/Nuitrack, калибровка, скоринг, «не вижу») — сенсора нет, проверить нельзя
