# E51: капсулы-конусы и подписи поверх призрака (T46)

Статус: в работе
Исполнитель: implementer (E51)
Бриф: капсулы конечностей → ConeGeometry (ободок + зад/перед), ReferenceBounds с радиусами суставов, подписи TextOnPill после DrawGhost в OnRender. Один файл: tools/player_trial/GhostCanvas.cs. Бюджет 30 мин, без коммитов.

## Ход работы

- [x] Прочитан GhostCanvas.cs (DrawGhost, OnRender, DrawFloor, ReferenceBounds)
- [x] Прочитана спека specs/player/trial.md (S41, S43, Contract «Доведения по прогону (S40-S43)», GhostCanvas, 3-й пункт)
- [x] Добавлен ConeGeometry (после Quad, замкнутая фигура, Freeze; вырожденный случай — эллипс)
- [x] Капсулы конечностей переведены на ConeGeometry: ободок ×1.0 + зад/перед ×FrontBackWidthFrac со смещением ±off по fwdT; радиусы суставов: 0.17T, среднее(0.17T,0.12S), 0.12S; 0.17U, среднее(0.17U,0.14L), 0.14L; пиксельный радиус конца = r·scale·DepthFactor(конца); RimWidthFrac удалён (ободок теперь полнорадиусный)
- [x] ReferenceBounds: Bone принимает радиусы проксимального и дистального концов (те же числа)
- [x] DrawLabels(dc, w, cx, cy, scale) вынесен из DrawFloor, вызывается в OnRender после обоих DrawGhost
- Примечание: при правке doc-комментария случайно задет был Solve — восстановлен, регион проверен чтением
- [x] dotnet build DanceLab.sln — успешно, предупреждений 0, ошибок 0
- [x] dotnet test tests/DanceLab.View.Tests — 29/29 пройдено, 0 не пройдено (без --no-build)
- [x] dotnet test tests/DanceLab.Core.Tests — 64/64 пройдено, 0 не пройдено (без --no-build)

Статус: завершено. Плеер не запускался (снимки делает ПМ). Коммитов нет.
