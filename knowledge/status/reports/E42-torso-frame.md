# E42: TorsoFrame в Core (T38, часть 1)

- Статус: в работе
- Начато: 2026-11-24
- Бюджет: 3 файла (CanonicalFrame.cs правка, TorsoFrame.cs новый, TorsoFrameTests.cs новый), 25 минут

## План

1. Прочитать спеку specs/player/trial.md, раздел Contract, подраздел «Формат эталона, версия 2 (S39)».
2. Прочитать CanonicalFrame.cs, Vec3.cs, CanonicalTests.cs.
3. Создать TorsoFrame.cs (readonly struct, Left/Up/Forward).
4. Править CanonicalFrame.cs: TorsoRollRad, TorsoBasis, HasTorsoBasis, конструктор (+2 параметра), метод TorsoFrame().
5. Написать TorsoFrameTests.cs (xUnit).
6. Прогнать dotnet build DanceLab.sln и dotnet test tests/DanceLab.Core.Tests.

## Ход работы

- 2026-11-24: файл статуса создан, читаю спеку и исходники.
- Спека S39 прочитана (specs/player/trial.md, строки 217-244): формулы совпадают с брифом.
- Тестов до: 22 [Fact]/[Theory] в CanonicalTests.cs + 15 в PoseReconstructorTests.cs = 37, сходится с брифом.
- Отклонение от брифа (3 файла): Vec3 объявлен internal; `public readonly struct TorsoFrame` с полями
  `public readonly Vec3` не компилируется (CS0052 inconsistent accessibility), тесты в отдельной сборке
  без InternalsVisibleTo оси прочитать не смогут. Минимальное решение: `internal readonly struct Vec3`
  → `public readonly struct Vec3` в Vec3.cs (4-й файл, одно слово). TorsoFrame остаётся публичным,
  как требует бриф; T42 (GhostCanvas в player_trial) оси всё равно читать из другой сборки.
- Написан TorsoFrame.cs (public readonly struct, Left/Up/Forward, конструктор трёх Vec3).
- Правлен CanonicalFrame.cs: TorsoRollRad, TorsoBasis (double[]?, копия, проверка длины 9),
  HasTorsoBasis, два необязательных параметра конструктора в конце, метод TorsoFrame()
  (basis иначе углы, реортонормализация, fallback L/Y/F при вырожденном left).
- Написан TorsoFrameTests.cs: вертикальный торс по heading, наклоны вперёд/назад/вбок на 90°,
  обратный ход по углам (сетка 7x5x4), roundtrip basis (та же сетка), плюс проверка копии basis
  и ArgumentException при длине не 9.
- Первый прогон: 54/55, упал BasisRoundTripGivesSameAxes. Причина: проверочный кадр строился с
  heading 0, но basis задан в координатах (L, Y, F) конкретного heading (F = (sin h, 0, cos h));
  при h != 0 оси повёрнуты на -h. Пункт брифа «углы любые, например 0» верен только для lean/roll,
  heading в проверочном кадре обязан сохраняться. Тест исправлен (heading h, lean/roll = 0).
- Финальный прогон: dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок;
  dotnet test tests/DanceLab.Core.Tests — 55/55 зелёные (23 CanonicalTests + 15 PoseReconstructorTests
  + 11 TorsoFrameTests + 6 HeadingUnwrapperTests из параллельной задачи T39).
- Итог: методов [Fact]/[Theory] было 37, стало 45 (мои +8; прогонных кейсов мои +11).

## Статус: завершено 2026-11-24
