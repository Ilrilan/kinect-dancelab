# E19 — Авто-рестарт сессии Nuitrack-триала (T19, specs/player/trial.md)

- Статус: завершена (отчёт отправлен ПМ)
- Дата: 2026-11-22
- Исполнитель: implementer E19
- Изменённые файлы: tools/player_trial/MainWindow.cs (PlayerSession.cs не потребовался)

## Что сделано

1. Сторож `WatchTrialSession()`: первый вызов в `OnTick` (до ветки режима — работает и в «Сцене сенсора»).
   Условия смерти: только nuitrack (`SelectedKey` содержит «nuitrack», Kinect не трогается),
   `wantRunning && session != null && !busy`, `SkeletonFps < 0.5` непрерывно дольше 5 с при
   `Elapsed > 20 с` (грейс на загрузку нейросети nuitrack-ai). Окно смерти `trialDeadSince`.
2. Авто-рестарт `RestartTrialSessionAsync(N)`: статус «Nuitrack-триал: перезапуск сессии (N)»,
   пауза 1 с, StopAsync → StartAsync, при успехе статус «...выполнен». Счётчик `trialRestarts` за
   запуск окна; при попытке 11+ — `wantRunning=false`, StopAsync, статус «Nuitrack-триал: лимит
   перезапусков, включите сенсор вручную». Флаг `trialRestarting` исключает двойной цикл;
   повторная проверка `busy`/`wantRunning` после паузы (вмешался ручной Стоп/смена трекера).
3. Калибровка ноги и пол при авто-рестарте НЕ сбрасываются (StopAsync/StartAsync их не трогают);
   новый сброс `ResetBodyCalibration()` вызывается только в `OnTrackerChanged` (смена трекера).
4. Ручной Стоп/Старт (`OnSensorButton`) сбрасывает `trialRestarts`, лимит и окно смерти.
5. Видео, оценка и эталон-призрак цикл рестарта не трогает (media/scorer/track не затронуты).

## Верификация

- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Capture.Tests: 45/45 пройдено (без --no-build).
- dotnet test tests/DanceLab.Core.Tests: 38/38 пройдено (без --no-build).
- player_trial --no-sensor --shot data/preview/trial-shot-e19.png --shot-after 3: exit 0,
  скриншот создан, статус «без сенсора», детект не активен (wantRunning=false, session=null).

## Не подтверждено

- Живой прогон с Nuitrack (смерть сессии на 3-й минуте, реальный рестарт, лимит) — железо
  недоступно исполнителю; логика описана в отчёте для критика.
