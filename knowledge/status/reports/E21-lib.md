# E21: Вынос общего кода player_trial/sensor_view в общую библиотеку (T21)

- Старт: 2026-11-22 15:42
- Бюджет: 100 минут
- Статус: в работе

## План

1. Прочитать specs/player/trial.md (T21) и все перечисленные файлы двух инструментов.
2. Сверить diff дубликатов: Topology, MultiUserReconstructor, GhostCanvas vs SkeletonCanvas, PlayerSession vs LiveSession, прочие дубли (StreamStats и т.п.).
3. Создать src/DanceLab.View (net48, UseWPF, ProjectReference на Capture+Core), перенести общее.
4. Переподключить оба инструмента через ProjectReference, удалить локальные копии.
5. Верификация: dotnet build DanceLab.sln, dotnet test x2, player_trial --no-sensor --shot, sensor_view --replay/запуск окна.
6. Отчёт.

## Журнал

- 15:42 создан файл статуса, начало чтения.
- 15:47 чтение завершено. Выводы diff:
  - Topology: версии разошлись. sensor_view — надмножество (Bones, Side, SideOf, FacesSensorByLabels, Head), player_trial добавляет TrackerId(key) (nuitrack-ai/native → nuitrack). Сведение: полная версия + TrackerId.
  - MultiUserReconstructor: player_trial — надмножество sensor_view (та же логика Process + кэш lastPose/lastLengths + TryGetUser). Сведение: версия player_trial.
  - GhostCanvas vs SkeletonCanvas: разные по функции (3D-сцена призраков с капсулами и камерой против 2D-канвы фронт/топ) — НЕ сводить.
  - PlayerSession vs LiveSession: разные по функции (без глубины/записи, +SkeletonFps/Source) — НЕ сводить.
  - StreamStats дубля нет — уже public в DanceLab.Capture.
- 15:48 создание src/DanceLab.View.
- 15:52 DanceLab.View.csproj + Topology.cs (сведение) + MultiUserReconstructor.cs (надмножество) созданы; массивы костей сверены с оригиналом sensor_view — идентичны.
