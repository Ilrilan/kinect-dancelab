# E24: Удалить дубликаты DanceLab.View (остаток T21, specs/player/trial.md)

E24: старт 2026-11-22 (исполнитель E24, бюджет 25 мин)

## План
1. Проверить, включены ли дубликаты в компиляцию (csproj net48 non-SDK — маска или явные списки).
2. Удалить 4 файла: tools/player_trial/Topology.cs, tools/player_trial/MultiUserReconstructor.cs, tools/sensor_view/Topology.cs, tools/sensor_view/MultiUserReconstructor.cs; поправить csproj при явном перечислении.
3. StreamStats: если копии в обоих инструментах — вынести в DanceLab.View; если в одном — не трогать.
4. Верификация: dotnet build DanceLab.sln (0/0), dotnet test обоих тестовых проектов без --no-build, player_trial --no-sensor --shot, ls.

## Статус
- [старт] Читаю csproj и состояние файлов.
- [факт] csproj обоих инструментов и библиотеки — SDK-style (маска `**/*.cs`), файлы явно не перечислены: править csproj не нужно. Сборка 0/0 при дубликатах потому, что неймспейсы разные: локальные `PlayerTrial`/`SensorView` (internal) против библиотечного `DanceLab.View` (public). Резолвится ЛОКАЛЬНАЯ копия (свой неймспейс приоритетнее using), библиотека пока подключена впрок.
- [факт] Локальные копии — функциональные подмножества/эквиваленты библиотеки: player_trial/Topology = sensor_view/Topology минус Bones/SideOf/Side/Head/FacesSensorByLabels; sensor_view/Topology = библиотечная минус TrackerId; MultiUserReconstructor в player_trial уже содержит lastPose/TryGetUser как в библиотеке. Все вызываемые инструменты членами (TrackerId, Kinect, Nuitrack, Root, LeftShoulder, RightShoulder, ScreenXSign, Bones, SideOf, Side, FacesSensorByLabels; MultiUserReconstructor: ctor, Process, TryGetUser, Reset) в библиотеке есть — удаление безопасно.
- [факт] StreamStats: копий в инструментах НЕТ, единый src/DanceLab.Capture/StreamStats.cs, оба инструмента берут через ProjectReference на DanceLab.Capture. Шаг 3 брифа — действий не требует.
- [действие] git rm четырёх файлов — удалены, staged (коммита нет). csproj не правился (не нужен).
- [верификация] dotnet build DanceLab.sln — 0 ошибок, 0 предупреждений (2.2 с). Инструменты перешли на DanceLab.View.
- [верификация] dotnet test DanceLab.Capture.Tests — 45/45 (8 s); DanceLab.Core.Tests — 38/38 (0.27 s), оба без --no-build.
- [верификация] player_trial --no-sensor --shot data/preview/trial-shot-e24.png --shot-after 3 — exit 0. Скриншот проверен визуально: призрак-скелет на 3D-сетке, HUD (режим/статус/время/пол), кольцо сенсоров c01–c09 — на месте.
- [верификация] ls: четыре файла отсутствуют в дереве; git status — D по всем четырём (staged), untracked только файл отчёта.
- [итог] Задача выполнена. csproj не требовал правки (SDK-style, включение по маске). StreamStats действий не требовал (уже един в DanceLab.Capture). Коммит не делается (по брифу — приёмка ПМ).

## Итоги верификации
- dotnet build DanceLab.sln: 0 ошибок / 0 предупреждений (2.2 с)
- dotnet test tests/DanceLab.Capture.Tests: 45 пройдено / 0 не пройдено (8 s)
- dotnet test tests/DanceLab.Core.Tests: 38 пройдено / 0 не пройдено (0.27 s)
- player_trial --no-sensor --shot data/preview/trial-shot-e24.png --shot-after 3: exit 0, скриншот с призраком/HUD/кольцом
- Файлы удалены: tools/player_trial/Topology.cs, tools/player_trial/MultiUserReconstructor.cs, tools/sensor_view/Topology.cs, tools/sensor_view/MultiUserReconstructor.cs
