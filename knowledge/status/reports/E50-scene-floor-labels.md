# E50: линия пола поперёк взгляда и подложки (T45)

Статус: завершено (правки внесены, прогон зелёный; не коммитить — приёмка ПМ)
Файлы: tools/player_trial/GhostCanvas.cs (единственный правимый)
Прогон: dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок; dotnet test tests/DanceLab.View.Tests — 29/29; dotnet test tests/DanceLab.Core.Tests — 64/64

## Ход работы

- [x] 1. Прочитан спека specs/player/trial.md (S40, S41, Contract «Доведения по прогону», GhostCanvas) и GhostCanvas.cs; приём пилюли из SceneCanvas.cs (DrawRoundedRectangle, поля 8/4, радиус 4)
- [x] 2. Линия пола DrawFloor поперёк взгляда камеры: th = cameraYawRad + StageFrontOffsetRad, H = (cos th, 0, −sin th), от target − 4·H до target + 4·H; подпись «пол» у проекции левого конца (x + 6, y − 16); сетка 4x4 не тронута
- [x] 3. Подложка: LabelPillBrush #0A0B0E с альфой 0xB0, метод TextOnPill (DrawRoundedRectangle радиус 4, поля 8/4, текст FormattedText как раньше); применён к заголовку «сцена 3D (азимут …)» (позиция правого верхнего угла текста сохранена) и к «пол»
- [x] 4. ReferenceRootX()/ReferenceRootY() + приватный ReferenceRootPoint (Solve(reference, lengths, −groundShiftU), та же проекция, что ReferenceBounds; null → NaN), XML-комментарии на русском
- [x] 5. Прогон: сборка 0/0, View.Tests 29/29, Core.Tests 64/64 (первая попытка сборки упала по чужой правке MainWindow.cs CS0236, со второй — зелёная)

## Примечания

- Пилюля рисуется DrawRoundedRectangle(brush, null, rect, 4, 4) — та же фигура, что RectangleGeometry с RadiusX = RadiusY = 4, и тот же приём, что в SceneCanvas.Caption (спека: «приём из SceneCanvas»).
- Хелпер Text(...) в GhostCanvas остался без вызовов после перевода «пол» на подложку — заменён на TextOnPill.
- Подложка заголовка у правого края выступает за панель на 8 px и обрезается ClipToBounds — позиция самого текста сохранена точно.

## E53: подпись «пол» к левому краю и цвет ободка эталона (T45, конец)

Статус: завершено (правки внесены, прогон зелёный; не коммитить — приёмка ПМ)
Файлы: tools/player_trial/GhostCanvas.cs (единственный правимый)
Прогон: dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок; dotnet test tests/DanceLab.View.Tests — 31/31; dotnet test tests/DanceLab.Core.Tests — 64/64

## Ход работы

- [x] 1. Прочитаны DrawFloor (строки 592-612), DrawLabels (618-633), TextOnPill (1030-1034), ReferenceSkin/UserSkin (184-191), Capsule/Cone в DrawGhost (767-797)
- [x] 2. Правка 1: подпись «пол» на подложке, текст в (12, floorY − 16), floorY = Project(targetXm, 0, targetZm).Y; x = 12, чтобы подложка (поле 8 слева) началась в 4 px от края — брифовая точка x = 6 дала бы подложку с −2 px (обрезка); комментарий в DrawLabels обновлён
- [x] 3. Правка 2: rim ReferenceSkin = Color.FromRgb(0x22, 0x34, 0x4A) (тот же, что back); UserSkin не тронут (rim 0xE0E0E0); уточнены комментарии: summary ReferenceSkin, doc Rim-свойства, строка схемы слоёв в DrawGhost (E53)
- [x] 4. Прогон: сборка 0/0 с первой попытки, View.Tests 31/31, Core.Tests 64/64 (оба без --no-build)

## Примечания

- Отступ подложки «пол»: TextOnPill рисует пилюлю от x − 8, поэтому «подложка в 4 px от края» = текст на x = 12; брифовая точка (6, …) в буквальном виде обрезала бы подложку на 2 px. Отклонение 6 px позиции текста — на усмотрение ПМ при приёмке.
- Самопроверка геометрии: концы линии пола относительно цели дают px'' = ±4, pz'' = 0 → f = 1 на концах, Y линии совпадает с Y проекции точки цели (правка упростила код: убраны hx/hz из DrawLabels).
