# E2-scene

E2: старт 2026-11-22 00:47:35

Задача: пол сцены (S15) + режим «Сцена сенсора» (S16) в player_trial, T6 из specs/player/trial.md.

## План
1. Core: Canonicalizer.ToCanonical += double floorYm = 0; тест.
2. player_trial: оценка пола (лодыжки, 2-й перцентиль, >=30 значений, раз в секунду).
3. player_trial: SceneCanvas.cs, режим Сцена сенсора, --mode probe|scene.
4. Верификация: build, tests, два снимка.

## Журнал
- 00:47 — старт, читаю specs/player/trial.md и исходники.
- 00:52 — прочитаны trial.md (S15/S16/T6/Contract), MainWindow, PlayerSession, MultiUserReconstructor, GhostCanvas, Topology, Program, Canonicalizer, Types, CanonicalTests, SkeletonCanvas (образец). Замечание: бриф говорит «33/33» тестов — проверю фактическое число до/после.
- Начата правка Core: Canonicalizer.ToCanonical += floorYm.
- 00:56 — Core готов: floorYm = 0 (дефолт), RootHeight = (rootPos.Y − floorYm)/leg, doc-комментарий S15; тест ToCanonicalRootHeightIsAboveSceneFloor добавлен.
- 01:00 — SceneCanvas.cs написан (Front/Top, палитра 4, золото для Reconstructed/Extrapolated, Invalid пропускаются, пол+сетка 1 м).
- 01:05 — MainWindow: режим-комбобокс в первой строке тулбара, панель сцены (Front/Top/статистика/«сенсор не запущен»), сбор лодыжек (мин за кадр, окно 300, ≥30, 2-й перцентиль, раз в ~1 c), floorYm в ToCanonical, строка «пол:» в HUD; Program: --mode probe|scene.
- 01:08 — фикс сборки: ReconstructedJoint — класс (не структура), убраны .Value; sceneView сделан полем.
- 01:10 — Верификация: dotnet build DanceLab.sln — 0 предупреждений / 0 ошибок; dotnet test tests/DanceLab.Core.Tests — 33/33 (32 прежних + новый тест пола). Снимки: trial-shot-e2-probe.png (--shot-after 3, exit 0: режим «Проба», видео+призрак+HUD «пол: оцениваю»); trial-shot-e2-scene.png (--mode scene, exit 0: обе панели «вид спереди (x, y)» и «вид сверху (x, z)», сетка 1 м, «сенсор не запущен», статистика снизу). git status: только разрешённые файлы.

## Итог
Выполнено всё по брифу. Бюджет ~25 минут. Коммит не делался (запрещён).

