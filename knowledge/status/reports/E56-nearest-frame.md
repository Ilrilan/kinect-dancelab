# E56: ближайший кадр вместо точного совпадения времени (T44)

Статус: в работе.

- Спека: specs/player/trial.md, S42.
- Файлы: src/DanceLab.View/FrameFollower.cs, tests/DanceLab.View.Tests/FrameFollowerTests.cs.

## План

1. Прочитать FrameFollower.cs и тесты.
2. CamTrack.TryGetNearest — ближайший кадр по |t' − t|, двоичный поиск.
3. CamTrack.TryGetRootNearest — тем же поиском; TryGetRoot не трогать.
4. FrameFollower.Update — центр по ближайшему кадру через TryGetNearestFrame.
5. Тесты: NearestFrameFoundWithRoundedTimes, CenterTracksCurrentFrameOnRealisticTicks, NearestRootMatchesFrame.
6. dotnet build DanceLab.sln; dotnet test tests/DanceLab.View.Tests.

## Прогресс

- (старт) файл статуса создан.
- CamTrack: добавлены TryGetNearest, TryGetRootNearest и приватный IndexOfNearest
  (двоичный поиск позиции вставки + сравнение соседних; при равных расстояниях —
  более ранний кадр). TryGetRoot не тронут.
- FrameFollower: вызов TryGetAutoFrame(t, camera, 0.0, ...) заменён на приватный
  TryGetNearestFrame (словарь cameras + TryGetNearest); комментарии S42 обновлены.
- Проверено расчётом: t = 1.68 → ближайший кадр 101 (1.6833, 0.0033), а не кадр 100
  (0.0133) — пункт брифа «кадр 100 для обоих t» неточен; в тесте для кадра 100 взяты
  t = 1.6733 (+0.0066) и t = 1.6597 (−0.007), t = 1.68 ассертится как 1.6833.
- После скачка bbox на кадре 100 до t = 2.0 остаётся 11 тиков: при слайдере 1
  (gain 0.2) остаток сглаживания ~17 px источника (порог 6 px недостижим), поэтому
  CenterTracksCurrentFrameOnRealisticTicks идёт на слайдере 3 (gain 0.5, остаток ~0.1 px).
- Тесты добавлены: NearestFrameFoundWithRoundedTimes, CenterTracksCurrentFrameOnRealisticTicks,
  NearestRootMatchesFrame (+ хелпер RoundedTimesTrack). Сборка и прогон — далее.

## Итог (2026-11-24)

- dotnet build DanceLab.sln: успешно, 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.View.Tests: не пройдено 0, пройдено 34 (31 прежних + 3 новых).
- Строки: FrameFollower.cs — TryGetNearest (114), TryGetRootNearest (138),
  IndexOfNearest (152), вызов TryGetNearestFrame в Update (306), TryGetNearestFrame (388);
  FrameFollowerTests.cs — RoundedTimesTrack (416), NearestFrameFoundWithRoundedTimes (434),
  CenterTracksCurrentFrameOnRealisticTicks (473), NearestRootMatchesFrame (497).
- Отклонения от брифа: t = 1.68 ассертится как ближайший кадр 101 (1.6833), не кадр 100;
  CenterTracksCurrentFrameOnRealisticTicks — слайдер 3 (на слайдере 1 порог 6 px
  недостижим: после скачка 11 тиков, остаток gain 0.2 — ~17 px источника).
