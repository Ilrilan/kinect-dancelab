# E37: библиотека View для тура (T34)

Статус: в работе (старт 2026-11-24).

План по брифу:
1. FrameFollower.cs: struct FrameTarget (ZAutoTarget, MaxPanX, MaxPanY), свойство LastTarget, заполнение в конце Update без изменения поведения.
2. Новый TourSchedule.cs: Times(durationSec, count) — равномерные моменты, округление до 0.1, зажим [0.2, max(0.2, durationSec−0.2)].
3. Тесты: FrameFollowerTests (LastTarget с рамкой и в запасном виде), новый TourScheduleTests (6 кейсов).
4. Прогон: dotnet build DanceLab.sln (0 warn / 0 err), dotnet test tests/DanceLab.View.Tests (без --no-build).

Ход:
- [старт] Файл статуса создан. Читаю код.
- Код прочитан: FrameFollower.cs, FrameFollowerTests.cs, SceneFollowerTests.cs (стиль), спека trial.md раздел Contract «Прогон по жанрам», абзац DanceLab.View. csproj: net48, xUnit 2.7 → readonly struct ок, double.IsFinite нет (использую IsNaN/IsInfinity).
- Число тестов до: 21 [Fact] (FrameFollowerTests 11, SceneFollowerTests 3, MediaQuirksTests 3, CameraRingTests 4), [Theory] 0.
- Сделано: FrameFollower.cs — struct FrameTarget (поля ZAutoTarget/MaxPanX/MaxPanY + конструктор), свойство LastTarget, заполнение в конце Update (только запись трёх чисел); новый TourSchedule.cs с Times(durationSec, count).
- Осталось: тесты (FrameFollowerTests +2, TourScheduleTests 6 кейсов), прогон build + test.
- Готово (2026-11-24). Правки: только 4 файла из брифа.
  - src/DanceLab.View/FrameFollower.cs: readonly struct FrameTarget (ZAutoTarget/MaxPanX/MaxPanY, конструктор), свойство LastTarget, запись в конце Update (поведение и возврат Update не тронуты), XML-doc класса/свойства/структуры с назначением (числовой отчёт тура, S34).
  - src/DanceLab.View/TourSchedule.cs: Times(durationSec, count) — (i+1)/(count+1)*durationSec, округление Math.Round(x,1), зажим [0.2, max(0.2, durationSec-0.2)], пустой список на count<=0 / не конечном / <=0 (IsFinite-хелпер: net48 без double.IsFinite).
  - tests/.../FrameFollowerTests.cs: +2 теста LastTarget (независимый расчёт zAuto/maxPan по объединению bbox окна ±0.7 с; fallback ZAutoTarget = AutoZoomFallback).
  - tests/.../TourScheduleTests.cs: 6 кейсов из брифа.
- Поправка по ходу: xunit 2.7 не имеет коллекционной перегрузки Assert.Equal с точностью (CS1503 int→IEqualityComparer) — убрал третий аргумент; равенство точное, корректно, т.к. Math.Round кладёт значения на сетку 0.1 (все ожидаемые значения точно представимы).
- Прогон: dotnet build DanceLab.sln — Предупреждений: 0, Ошибок: 0. dotnet test tests/DanceLab.View.Tests (без --no-build) — не пройдено 0, пройдено 29, всего 29 (было 21 [Fact], +8 новых: 2 + 6).
- Статус: задача выполнена, отчёт отправлен ПМ.
