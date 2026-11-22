# E22 — Общая библиотека DanceLab.View (T21, аудит P2.11)

Старт: 2026-11-22. Бюджет: 80 минут. Предшественник E21: WIP 497594b (src/DanceLab.View/ создан,
НЕ подключён к sln и инструментам).

## План
1. Diff копий Topology / MultiUserReconstructor в player_trial и sensor_view; проверить StreamStats.
2. Свести общие копии в src/DanceLab.View (net48, UseWPF, ProjectReference Capture+Core — уже в WIP).
3. Подключить в sln и оба csproj, удалить локальные копии сведённого, поправить using.
4. GhostCanvas/SkeletonCanvas, PlayerSession/LiveSession — НЕ сводить (законно различны).
5. Верификация: build 0/0, tests 45/45 + 38/38 (без --no-build), player_trial --no-sensor --shot,
   sensor_view --replay/--shot или запуск окна 3 с.

## Журнал
- [старт] Изучаю состояние, создан этот файл.
- [5 мин] Diff-сверка готова. Topology: player_trial — урезанная копия (TrackerId, Root,
  LeftShoulder/RightShoulder, ScreenXSign), sensor_view — полная (Side enum, Bones, SideOf, Head,
  FacesSensorByLabels + те же). MultiUserReconstructor: player_trial — надмножество с lastPose/
  lastLengths/TryGetUser (нужно калибровке пробы), sensor_view — простая. WIP-версии в
  src/DanceLab.View — надмножества обоих, беру их. StreamStats: НЕ дублируется — уже общий public
  класс в src/DanceLab.Capture/StreamStats.cs, оба инструмента ссылаются на Capture. Вынос не нужен.
- [5 мин] Начинаю правки: sln + 2 csproj + удаление 4 локальных копий + using в 6 файлах
  (player_trial: PlayerSession, MainWindow, SceneCanvas; sensor_view: LiveSession, MainWindow,
  SkeletonCanvas). GhostCanvas/SkeletonCanvas/PlayerSession/LiveSession не сводятся (различны законно).
