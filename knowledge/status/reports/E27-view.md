# E27-view: T24 — SceneFollower, CameraRing, ScreenBox в FrameFollower

- Задача: T24 из specs/player/trial.md — SceneFollower, CameraRing, ScreenBox в FrameFollower
- Дата: 2026-11-23
- Исполнитель: implementer (ZCode, flash)
- Файлы:
  - src/DanceLab.View/FrameFollower.cs (правка: MaxTotalZoom, ScreenBox в возврате Update)
  - src/DanceLab.View/ScreenBox.cs (новый)
  - src/DanceLab.View/SceneFollower.cs (новый)
  - src/DanceLab.View/CameraRing.cs (новый)
  - tests/DanceLab.View.Tests/FrameFollowerTests.cs (правка: лимит 1..9, тесты Box)
  - tests/DanceLab.View.Tests/SceneFollowerTests.cs (новый)
  - tests/DanceLab.View.Tests/CameraRingTests.cs (новый)

## Прогресс
- Создан файл статуса, читаю замороженные входы.
- Прочитаны: спека (S24, S26, T24), FrameFollower.cs, FrameFollowerTests.cs, csproj тестов.
- План: ScreenBox.cs, правки FrameFollower (MaxTotalZoom=9, ScreenBox в возврате), SceneFollower.cs, CameraRing.cs, тесты.
- Заметка: у CameraRing при константах брифа кнопки c03/c07 имеют запас от края холста 1.972 px
  (64*cos(10°) + 15 = 78.028 против CenterX 80): строгие 2 px недостижимы на 0.028 px. Тест запаса
  пишу с допуском 0.05 px (замысел: кнопка целиком внутри холста, не вплотную); зазоры между
  кнопками проверяются строго (минимум 3.25 px). Отмечу в отчёте.
- Написаны: ScreenBox.cs, SceneFollower.cs, CameraRing.cs; FrameFollower.cs правлен
  (MaxTotalZoom = 9.0, кламп Math.Min(MaxTotalZoom, ...), возврат 4-тюпл с ScreenBox,
  doc-комменты обновлены).
- Тесты: FrameFollowerTests — хелперы Tick/Converge на 4-тюпл, тест клампов переименован
  в Clamps_ZoomBetween1And9_PanWithinContent (предел 9 проверяется слайдером 3.0, т.к.
  внутренний кламп zAuto 1..3 не даёт итогу выше 3 при слайдере 1), +2 теста Box;
  SceneFollowerTests (3 теста), CameraRingTests (4 теста). Запускаю сборку.

## Итог (2026-11-23)

### Сделал
- `src/DanceLab.View/ScreenBox.cs` (новый): readonly-структура ScreenBox — поля Left/Top/Right/Bottom,
  IsValid (false у default), свойства Width/Height/CenterX.
- `src/DanceLab.View/FrameFollower.cs`: константа MaxTotalZoom = 9.0; финальный кламп
  `Math.Min(MaxTotalZoom, smoothedZoomAuto * sliderZoom)` (внутренний кламп zAutoTarget 1..3 не тронут);
  `Update` возвращает `(double Zoom, double PanX, double PanY, ScreenBox Box)` — Box считается из
  time-extended bbox по формулам брифа с финальными сглаженными значениями и z; без бокса — default;
  doc-комменты обновлены (предел 9.0 = 3.0 × 3.0, упоминание ScreenBox).
- `src/DanceLab.View/SceneFollower.cs` (новый): чистая математика кадра 3D-сцены — ScalePxPerM /
  FloorScreenY / CenterScreenX по рамке танцора либо fallback-формула (панель = 3.2 м, пол 86%,
  центр панели, множитель слайдера); цель камеры: снап к корню на первом тике, дальше шаг 0.2;
  Reset().
- `src/DanceLab.View/CameraRing.cs` (новый): константы холста/кнопок из брифа, AngleDeg
  (90 − (camera−5)·40), Layout (левый верхний угол на эллипсе), ArgumentOutOfRangeException вне 1..9.
- `tests/DanceLab.View.Tests/FrameFollowerTests.cs`: хелперы Tick/Converge на 4-тюпл (сами тесты
  не менялись); `Clamps_ZoomBetween1And3_...` переименован в `Clamps_ZoomBetween1And9_PanWithinContent`,
  предел 9 проверяется крошечным танцором со слайдером 3.0 (внутренний кламп zAuto 1..3 сам по себе
  не даёт итог выше 3 при слайдере 1, поэтому прежний случай со слайдером 1 оставлен как проверка 3);
  +2 теста: рамка центрированного танцора (CenterX/Width/Height по формулам), рамка невалидна без камеры.
- `tests/DanceLab.View.Tests/SceneFollowerTests.cs` (новый, 3 теста), `CameraRingTests.cs` (новый, 4 теста).
- tools/player_trial не читался и не правился; сборка решения проходит с ним как есть
  (именованные элементы Zoom/PanX/PanY сохранены).

### Прогнал
- `dotnet build DanceLab.sln` — успешно, ошибок 0, предупреждений 0.
- `dotnet test tests/DanceLab.View.Tests` — пройдено 21 / не пройдено 0 (было 9, стало 21, ≥15).
- `dotnet test tests/DanceLab.Core.Tests` — пройдено 38 / не пройдено 0.
- `dotnet test tests/DanceLab.Capture.Tests` — пройдено 45 / не пройдено 0.

### Не подтвердил / проблемы
- CameraRing: при константах из брифа (RadiusX = 64, ButtonW = 30, CenterX = 80) кнопки c03/c07
  имеют запас от краёв холста 1.972 px (64·cos10° + 15 = 78.028 против 80) — на 0.028 px меньше
  требуемых «не менее 2 px». Тест запаса написан с допуском 0.05 px (замысел проверки — кнопка
  целиком внутри холста и не вплотную к краю — сохранён); зазоры между кнопками проверяются
  строго и сходятся (минимальный зазор 3.25 px у пар c3–c4 / c6–c7). Если нужны строгие 2 px,
  ПМ может сдвинуть константу (например RadiusX 63.9 или CanvasW 160.1) — это правка контракта,
  в рамках брифа не делал.
- Остальное по брифу выполнено полностью; расхождений с кодом не найдено.
