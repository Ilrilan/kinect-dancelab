# E30 — T25: GhostCanvas — кадрирование сцены по видео (SceneFollower) + бейсболка

Задача: T25 из `specs/player/trial.md`. Исполнитель: implementer (flash).
Дата: 2026-11-23.

Файлы: только `tools/player_trial/GhostCanvas.cs`. `MainWindow.cs` не трогаем
(его переподключит следующая задача).

Пункты:
1. `GhostLengths.HeightM` = Thigh + Shin + TorsoLen + NeckToHead + 2 * HeadR.
2. `RootOf(CanonicalFrame)` — формула корня из DrawGhost, используется в DrawGhost.
3. Framing-вход: `sceneFollower`, `sceneFrame`, `framed`, `lastRoot`; метод
   `SetVideoBox(...)`.
4. Проекция с камерой-таргетом: `targetXm/targetZm`, Project/DepthFactor,
   OnRender scale/cx/cy из sceneFrame, DrawFloor через таргет.
5. Кепка (S27): корона StreamGeometry 106%, козырёк с наклоном 22°, порядок
   отрисовки по `brimAhead`, убрать старые dome-арку и Brim/halfBrim.

Проверки: dotnet build DanceLab.sln; dotnet test tests/DanceLab.View.Tests;
dotnet test tests/DanceLab.Core.Tests; три скриншота --no-sensor
(trial-shot-e30-c05/c01/c07.png), exit code 0.

Статус: завершено.

Прогресс 2026-11-23:
- Item 1: HeightM добавлен (Thigh+Shin+TorsoLen+NeckToHead+2*HeadR).
- Item 2: RootOf() добавлен, DrawGhost использует его + (0, RootHeight*leg, 0).
- Item 3: поля sceneFollower/sceneFrame/framed/lastRoot, метод SetVideoBox с доком из брифа;
  using DanceLab.View добавлен (ссылка на DanceLab.View уже была в PlayerTrial.csproj).
- Item 4: targetXm/targetZm ставятся в начале OnRender; Project/DepthFactor вычитают таргет;
  framed-ветка scale/cx/cy (finite>0); DrawFloor — зелёная линия targetXm±4, сетка не тронута.
- Item 5: купол — заливка StreamGeometry (хорда k=0.25, эллипс 106%), козырёк с наклоном 22°
  (fv, базы 0.85*HeadR + 0.2 вверх, кончики 1.6*HeadR вперёд ± 0.55*HeadR), порядок по brimAhead;
  старые dome-арка, плоский Brim-квад и halfBrim удалены.

Проверки:
- dotnet build DanceLab.sln — попытка 1 упала на чужой правке MainWindow.cs:321
  (CreateDarkButtonStyle, исполнитель той задачи правил файл в этот момент); попытка 2 через
  45 с — успех, 0 ошибок, 0 предупреждений.
- dotnet test tests/DanceLab.View.Tests — пройдено 21/21, exit 0.
- dotnet test tests/DanceLab.Core.Tests — пройдено 38/38, exit 0.
- Скриншоты: первый запуск c05 упал с необработанным исключением в конструкторе MainWindow
  (MainWindow.cs:496, Style/TargetName — чужая незавершённая правка того же файла); повтор
  через 60 с — exit 0. Итог: c05 exit 0, c01 exit 0, c07 exit 0; файлы
  data/preview/trial-shot-e30-{c05,c01,c07}.png созданы.
- Framing-путь (SetVideoBox) в скриншотах не активен: MainWindow подключит его следующая
  задача; скриншоты проходят по legacy-ветке (таргет (0,0), прежние scale/cx/cy) и новой кепке.

## Сделал

- Item 1: `tools/player_trial/GhostCanvas.cs` — `GhostLengths.HeightM` (строка 36).
- Item 2: `RootOf(CanonicalFrame)` (строки 451-457), `DrawGhost` строит
  `root = RootOf(f) + (0, RootHeight*leg, 0)` (строка 473).
- Item 3: поля `sceneFollower`/`sceneFrame`/`framed`/`lastRoot` (строки 272-281),
  публичный `SetVideoBox(...)` (строки 310-327) с доком из брифа; `using DanceLab.View`
  добавлен (ссылка на DanceLab.View уже была в PlayerTrial.csproj, csproj не трогался).
- Item 4: `targetXm, targetZm` (строка 236), установка в начале `OnRender`
  (строки 329-339), framed-ветка scale/cx/cy (строки 351-356), `Project` и
  `DepthFactor` вычитают таргет (строки 387-402), `DrawFloor` — зелёная линия
  `targetXm-4..targetXm+4` (строка 430), сетка вокруг мирового начала не тронута.
- Item 5: блок «голова и бейсболка (S20, S27)» (строки 594-639): заливной купол
  StreamGeometry (эллипс 106%, хорда k=0.25), козырёк с наклоном 22° и порядком
  отрисовки по `brimAhead`; арка-dome, плоский Brim и halfBrim удалены.

## Прогнал

- dotnet build DanceLab.sln: попытка 1 — 1 ошибка (чужая правка MainWindow.cs:321,
  CS0103 CreateDarkButtonStyle); попытка 2 — успешно, 0 ошибок, 0 предупреждений.
- dotnet test tests/DanceLab.View.Tests: 21/21 пройдено, exit 0.
- dotnet test tests/DanceLab.Core.Tests: 38/38 пройдено, exit 0.
- player_trial.exe --no-sensor --shot ... c05: первый запуск — краш в MainWindow.ctor
  (чужая правка), повтор — exit 0; c01 — exit 0; c07 — exit 0.

## Не подтвердил / проблемы

- Framing-путь SetVideoBox на скриншотах не задействован: по брифу MainWindow
  переподключается следующей задачей, поэтому скриншоты проверяют legacy-ветку
  (таргет (0,0), прежний масштаб) и новую кепку. Сам метод написан по контракту
  SceneFollower.Update и компилируется.
- Два сбоя во время проверки — не в моём файле: ошибка сборки MainWindow.cs:321 и
  краш MainWindow.ctor:496 (незавершённая правка параллельного исполнителя);
  оба ушли после его правок, повторы успешны.

