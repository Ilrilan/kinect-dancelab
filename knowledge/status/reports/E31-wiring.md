# E31-wiring — T28: сцена получает рамку танцора из окна (specs/player/trial.md S26/T28)

Дата: 2026-11-23
Исполнитель: implementer (flash), бриф от ПМ.

Задачи брифа:
1. MainWindow.UpdateVideoFrame кормит сцену: в нормальном пути после четырёх
   transform-присваиваний добавить пересчёт рамки в координаты панели сцены и
   вызов ghost.SetVideoBox(...); в ветке раннего возврата (видео не готово,
   где вызывается follower.Reset) добавить ghost.SetVideoBox(false, ...).
2. OnZoomChanged: убрать строку ghost.FrameZoom = v; поправить комментарий
   (множитель слайдера доходит до обеих половин через тик). Текст метки оставить.
3. GhostCanvas: удалить свойство FrameZoom, поле frameZoom и константу
   FloorAnchorAboveFloorFrac с док-комментарием; в OnRender при framed == false
   сначала SetVideoBox(false, 0, 0, 0, w, h, 1.0), затем всегда формулы framed
   (scale = sceneFrame.ScalePxPerM, cx = sceneFrame.CenterScreenX,
   cy = sceneFrame.FloorScreenY - SceneCenterYm * scale; guard: scale не
   конечен или <= 0 → h / 3.2); удалить старые baseScale/anchorYm/anchorScreenY;
   сохранить SceneCenterYm, CameraDistanceM, StageFrontOffsetRad; обновить
   комментарии класса/метода без упоминаний FrameZoom и «зум 100».

Верификация: dotnet build DanceLab.sln; dotnet test tests/DanceLab.View.Tests,
tests/DanceLab.Core.Tests, tests/DanceLab.Capture.Tests; 6 скриншотов exe
(--no-sensor, код выхода 0 у каждого).

---
Прогресс:
- [2026-11-23] Старт: статус-файл создан, читаю frozen inputs.
- [2026-11-23] Item 1 готов: MainWindow.UpdateVideoFrame — в раннем возврате добавлен
  ghost.SetVideoBox(false, ...); в нормальном пути после четырёх transform-присваиваний
  добавлен пересчёт рамки (kx/ky по ActualWidth панелей) и ghost.SetVideoBox(...).
- [2026-11-23] Item 2 готов: OnZoomChanged — строка ghost.FrameZoom = v удалена,
  док-комментарий переписан (множитель доходит до обеих половин через тик), текст метки нетронут.
- [2026-11-23] Item 3 готов: GhostCanvas — удалены поле frameZoom, свойство FrameZoom,
  константа FloorAnchorAboveFloorFrac с док-комментарием; OnRender: при !framed сначала
  SetVideoBox(false, 0, 0, 0, w, h, 1.0), далее всегда формулы SceneFollower с guard на scale
  (IsNaN/IsInfinity/<=0 → h/3.2; double.IsFinite на net48 нет, стиль как в старом коде);
  старые baseScale/anchorYm/anchorScreenY удалены; SceneCenterYm/CameraDistanceM/
  StageFrontOffsetRad сохранены. grep по FrameZoom/frameZoom/зум 100/baseScale в
  tools/player_trial — 0 вхождений.
- [2026-11-23] Запускаю dotnet build DanceLab.sln.
- [2026-11-23] Build: успех, 0 предупреждений, 0 ошибок, exit 0.
- [2026-11-23] Тесты: View.Tests 21/21 (exit 0), Core.Tests 38/38 (exit 0),
  Capture.Tests 45/45 (exit 0).
- [2026-11-23] Запускаю 6 скриншотов player_trial (--no-sensor), код выхода каждого должен быть 0.
- [2026-11-23] Скриншоты: все 6 команд отработали с exit 0, файлы в data/preview/
  (trial-shot-e31-{c05,zoom2,zoom3,c01,c07,c09}.png). Вердикт по картинкам — за ПМ.

---

## Отчёт

### Сделал

1. **Item 1** — `tools/player_trial/MainWindow.cs`, `UpdateVideoFrame` (метод с ~762):
   - ранний возврат (видео не готово): после сброса `videoPan` добавлен
     `ghost.SetVideoBox(false, 0, 0, 0, ghost.ActualWidth, ghost.ActualHeight, sliderZoom);`
     (строка 770);
   - нормальный путь: после `videoZoom.ScaleX/ScaleY`, `videoPan.X/Y` добавлен блок
     «кадр сцены (S26)» — vw/vh/gw/gh, kx/ky и
     `ghost.SetVideoBox(frame.Box.IsValid, frame.Box.Height * ky, frame.Box.Bottom * ky, frame.Box.CenterX * kx, gw, gh, sliderZoom);`
     (строки 782–789).
2. **Item 2** — `MainWindow.cs`, `OnZoomChanged` (~1524–1546): строка
   `ghost.FrameZoom = v;` удалена, док-комментарий переписан (множитель слайдера
   доходит до обеих половин через тик — UpdateVideoFrame применяет его к видео и
   с тем же значением кормит сцену, S26); текст метки `zoomValue` не тронут.
3. **Item 3** — `tools/player_trial/GhostCanvas.cs`:
   - удалены поле `frameZoom`, свойство `FrameZoom` с док-комментарием и константа
     `FloorAnchorAboveFloorFrac` с док-комментарием (все упоминания — grep 0);
   - `OnRender` (строки 302–333): при `!framed` сначала
     `SetVideoBox(false, 0, 0, 0, w, h, 1.0)` (запасной широкий вид тоже от
     SceneFollower), затем цель камеры всегда из `sceneFrame`; формулы кадра всегда
     framed: `scale = sceneFrame.ScalePxPerM` с guard (IsNaN/IsInfinity/<=0 →
     `h / 3.2`; `double.IsFinite` на net48 отсутствует, guard в стиле прежнего кода),
     `cx = sceneFrame.CenterScreenX`, `cy = sceneFrame.FloorScreenY - SceneCenterYm * scale`;
     старые `baseScale`/`anchorYm`/`anchorScreenY` и их комментарии удалены;
   - `SceneCenterYm`, `CameraDistanceM`, `StageFrontOffsetRad` сохранены;
   - комментарии класса и метода не упоминают `FrameZoom`/«зум 100».
   Заделы SceneFollower (`sceneFollower`, `sceneFrame`, `framed`, `lastRoot`) не менялись.

### Прогнал (все из C:\work\dancelab)

- `dotnet build DanceLab.sln` — успех, 0 предупреждений, 0 ошибок, exit 0.
- `dotnet test tests/DanceLab.View.Tests` — пройдено 21/21 (0 не пройдено, 0 пропущено), exit 0.
- `dotnet test tests/DanceLab.Core.Tests` — пройдено 38/38, exit 0.
- `dotnet test tests/DanceLab.Capture.Tests` — пройдено 45/45, exit 0.
- 6 запусков player_trial.exe (`--no-sensor --shot-after 4`), все exit 0, PNG созданы:
  trial-shot-e31-c05.png, -zoom2.png (--zoom 2.0), -zoom3.png (--zoom 3.0),
  -c01.png, -c07.png, -c09.png (в `data/preview/`).

### Не подтвердил / проблемы

- Не подтвердил ничего из contradicts-категории: все пункты брифа легли на код без отклонений.
  Единственная адаптация: guard масштаба записан через `IsNaN/IsInfinity/<=0` вместо
  `!double.IsFinite` — API `double.IsFinite` недоступен в net48, семантика та же.
- Визуальную корректность кадров (совпадение размеров/линии ног, поведение на x2/x3)
  не проверял — снимки сделаны, вердикт за ПМ (по брифу описание не требуется).

