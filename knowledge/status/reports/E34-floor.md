# E34-floor (T31, specs/player/trial.md S32)

Задача: убрать «висящего» эталонного призрака и заниженную линию пола сцены.
Дата: 2026-11-23. Исполнитель: implementer (flash).

Пункты брифа:
1. Item 1: линия обуви танцора — `FrameFollower.FootRows`, `ScreenBox.FeetY` (5-й параметр конструктора), расчёт в `Update`, тест.
2. Item 2: GhostCanvas — 2a: извлечь FK в `Solve` + struct `GhostJoints`, параметр `rootShiftU` у `DrawGhost`; 2b: `groundShiftU`, `SetGroundShift`, `ComputeGroundShift` (10-й перцентиль низа стоп); 2c: перенести `RootOf` выше длинного комментария, чтобы у каждого метода был свой summary.
3. Item 3: MainWindow — `ghost.SetGroundShift(GhostCanvas.ComputeGroundShift(track))` после загрузки трека; в `UpdateVideoFrame` передавать `frame.Box.FeetY * ky` вместо `frame.Box.Bottom * ky`.
4. Item 4: статус-текст без сенсора — с учётом `mode` (в Scene — «Сцена сенсора: ...», иначе старый текст).

Прогон: dotnet build DanceLab.sln; dotnet test tests/DanceLab.View.Tests, tests/DanceLab.Core.Tests, tests/DanceLab.Capture.Tests; 4 скриншота player_trial (--no-sensor, c05/c01/c07).

## Ход работы

- Разведка: S32 и T31 из спеки прочитаны; ScreenBox конструируется только в FrameFollower.Update (других мест нет); RootOf/DrawGhost используются только внутри GhostCanvas (SetVideoBox:294, OnRender:338/343).
- Решение по 2a/2b: бриф требует `public static ComputeGroundShift`, а его псевдокод вызывает `Solve` -> `RootOf(f)`, который читает поле `lengths` (инстанс). Чтобы Solve был вызываем из статического метода с переданными `len`, RootOf и Solve делаются статическими, RootOf получает параметр `(f, len)`; вызов в SetVideoBox меняется на `RootOf(reference, lengths)` — значения те же (DrawGhost всегда передаёт `l = lengths`), рисунок при rootShiftU=0 не меняется. Отклонение от дословной сигнатуры `RootOf(f)` зафиксировано здесь.

## Item 1 — готово

- `src/DanceLab.View/FrameFollower.cs`: константа `FootRows = 30.0` (после ShoePadRows); в `Update` bottom выделен в локальную переменную, `feetY = bottom - ((ShoePadRows - FootRows) * s * z)` из тех же s и z, пятый аргумент конструктора; doc-комментарий `Update` дополнен (Bottom/FeetY).
- `src/DanceLab.View/ScreenBox.cs`: поле `FeetY` со summary, конструктор с 5-м параметром `feetY`; `default(ScreenBox)` по-прежнему невалиден.
- Тест `CenteredDancer_BoxCentredInWindow_SizeIsPaddedBboxTimesScaleZoom` расширен: `Box.FeetY == Box.Bottom - ((ShoePadRows - FootRows) * 1.0 * z)` при s=1.

## Items 2-4 — готово

- `tools/player_trial/GhostCanvas.cs`: `using System.Collections.Generic;`; поле `groundShiftU`; `SetGroundShift` и статический `ComputeGroundShift` после `SetVideoBox`; `RootOf` перенесён выше длинного комментария (теперь у `DrawGhost` свой summary), стал статическим с `(f, len)`; struct `GhostJoints` (readonly, 20 полей); `Solve(f, len, rootShiftU)` со всей FK (формулы 1:1 из DrawGhost); `DrawGhost` получает параметр `rootShiftU`, тело переведено на `j.*` (стрелка, торс, панели, позвоночник, голова, козырёк, капсулы — всё из j), дублирующий расчёт суставов удалён; в `OnRender` эталон рисуется с `-groundShiftU`, пользователь с `0`.
- `tools/player_trial/MainWindow.cs`: после загрузки трека добавлено `ghost.SetGroundShift(GhostCanvas.ComputeGroundShift(track));`; в `UpdateVideoFrame` обычный вызов `SetVideoBox` передаёт `frame.Box.FeetY * ky` вместо `frame.Box.Bottom * ky` (early-return не тронут); стартовый статус без сенсора теперь по `mode` (Scene: «Сцена сенсора: сенсор не запущен, «Старт сенсора» его запускает.», иначе прежний текст).
- Правки 2026-11-23 ~начало: сборка не прогонялась ещё.

## Отчёт (итог)

### Сделал

1. Item 1 (линия обуви танцора):
   - `src/DanceLab.View/FrameFollower.cs`: `FootRows = 30.0` (стр. 90, с требуемым комментарием); в `Update` (стр. 179-196) низ рамки выделен в `bottom`, `feetY = bottom - ((ShoePadRows - FootRows) * s * z)` из тех же `s`/`z`, передан 5-м аргументом; doc `Update` дополнен (стр. 113-124).
   - `src/DanceLab.View/ScreenBox.cs`: `public readonly double FeetY` (стр. 24) + 5-й параметр конструктора `feetY` (стр. 29); `default(ScreenBox)` невалиден, как прежде.
   - `tests/DanceLab.View.Tests/FrameFollowerTests.cs`: тест `CenteredDancer_BoxCentredInWindow_SizeIsPaddedBboxTimesScaleZoom` расширен проверкой `Box.FeetY == Box.Bottom - ((ShoePadRows - FootRows) * 1.0 * z)` (s=1, стр. ~289).
2. Item 2 (`tools/player_trial/GhostCanvas.cs`):
   - 2a: struct `GhostJoints` (стр. 466-498, 20 полей), `Solve(f, len, rootShiftU)` (стр. 500-537) — вся FK перенесена из DrawGhost дословно, `root = RootOf(f, len) + (0, (RootHeight + rootShiftU) * leg, 0)`; `DrawGhost` (стр. 546) получил параметр `rootShiftU`, тело переведено на `j.*` (стрелка от `j.Root`, `j.FwdH`; торс/панели от `j.ShL..j.HipL` с `j.FwdT`; голова/козырёк от `j.HeadC`, `j.FwdH`, `j.LeftH`; капсулы от суставов j) — формулы не менялись; `OnRender` (стр. 386-393): эталон `-groundShiftU`, пользователь `0`.
   - 2b: поле `groundShiftU` (стр. 261), `SetGroundShift` (стр. 306-312), статический `ComputeGroundShift` (стр. 317-351): единичные длины `FromLeg(1.0)`, кадр пропускается без полной валидной ноги (`BoneValid[4]&&[6]` / `[5]&&[7]`), `low` по валидным лодыжкам минус `0.12 * unit.Shin`, сортировка, элемент `Floor(0.10 * (count-1))`, 0 для null/пустого.
   - 2c: `RootOf` (стр. 457-464) перенесён выше длинного комментария — теперь у `DrawGhost` свой summary (длинный комментарий, дополнен строкой про rootShiftU).
   - Отклонение от брифа (зафиксировано выше в «Ход работы»): `RootOf`/`Solve` сделаны статическими, `RootOf` принимает `(f, len)` — иначе статический `ComputeGroundShift` не смог бы вызвать `Solve` с единичными длинами (поле `lengths` инстанса). Вызов в `SetVideoBox` — `RootOf(reference, lengths)` (стр. 298), значения идентичны прежним; рисунок при rootShiftU=0 не меняется.
3. Item 3 (`tools/player_trial/MainWindow.cs`): `ghost.SetGroundShift(GhostCanvas.ComputeGroundShift(track))` после загрузки трека (стр. 746-747); в `UpdateVideoFrame` обычный вызов `SetVideoBox` передаёт `frame.Box.FeetY * ky` (стр. 940), early-return не тронут.
4. Item 4 (`tools/player_trial/MainWindow.cs`, стр. 794-798): стартовый статус без сенсора по `mode` — в `AppMode.Scene` «Сцена сенсора: сенсор не запущен, «Старт сенсора» его запускает.», иначе прежний текст.

### Прогнал (все из C:\work\dancelab)

- `dotnet build DanceLab.sln` — сборка успешна, 0 предупреждений, 0 ошибок, exit 0.
- `dotnet test tests/DanceLab.View.Tests` — пройдено 21 / всего 21 (0 не пройдено), exit 0.
- `dotnet test tests/DanceLab.Core.Tests` — пройдено 38 / всего 38, exit 0.
- `dotnet test tests/DanceLab.Capture.Tests` — пройдено 45 / всего 45, exit 0.
- `player_trial.exe --no-sensor --shot data/preview/trial-shot-e34-c05-t4.png --shot-after 4` — exit 0, PNG создан (319 КБ).
- `... trial-shot-e34-c05-t6.png --shot-after 6` — exit 0, PNG создан (309 КБ).
- `... trial-shot-e34-c01.png --shot-after 4 --camera c01` — exit 0, PNG создан (302 КБ).
- `... trial-shot-e34-c07.png --shot-after 6 --camera c07` — exit 0, PNG создан (274 КБ).
- Скриншоты для визуальной приёмки ПМ: `data/preview/trial-shot-e34-c05-t4.png`, `trial-shot-e34-c05-t6.png`, `trial-shot-e34-c01.png`, `trial-shot-e34-c07.png`.

### Не подтвердил / проблемы

- Численное значение `ComputeGroundShift` для трека по умолчанию не приведено: его получение требует дополнительного кода (временный `Console.WriteLine` в финальном коде запрещён брифом) — по брифу число пропускается.
- Отклонение от дословной сигнатуры брифа `RootOf(f)` → `RootOf(f, len)` (и статические RootOf/Solve) — вынужденное, причина в «Ход работы»; на рисунок при rootShiftU=0 не влияет.
