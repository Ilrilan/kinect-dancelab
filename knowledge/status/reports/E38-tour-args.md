# E38: аргументы тура и ReferenceBounds (T35, часть 1)

- Задача: specs/player/trial.md (S34), часть 1 T35 — аргументы тура в командной строке плеера и два метода GhostCanvas (ResetFrame, ReferenceBounds).
- Файлы: tools/player_trial/Program.cs, tools/player_trial/GhostCanvas.cs (только эти два).
- Статус: начата 2026-11-24.

## Ход работы

- [x] Прочитаны Program.cs, GhostCanvas.cs, раздел Contract спеки trial.md (строки 179-213)
- [x] Program.cs: Options.Tour, Options.TourTimes, Options.TourZooms, NoSensor при --tour, хелпер ParseDoubleList
- [x] GhostCanvas.cs: ResetFrame(), ReferenceBounds(), вынос UpdateCameraTarget() (вызывается в OnRender и ReferenceBounds)
- [x] dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок
- [x] dotnet test tests/DanceLab.View.Tests — 29/29 (0 не пройдено)
- [x] dotnet test tests/DanceLab.Core.Tests — 38/38 (0 не пройдено)

Задача завершена 2026-11-24, все проверки из брифа пройдены.

## Примечания

- WPF Rect не имеет статического FromLeftTopRightBottom (он есть только в UWP), габарит собран
  конструктором new Rect(minX, minY, maxX-minX, maxY-minY) по тем же лево/верх/право/низ.
- double.IsFinite недоступен в .NET Framework 4.8 — проверка !IsNaN && !IsInfinity.
- В ReferenceBounds падение с null-эталоном отсечено Rect.Empty; для локальных функций поле
  reference захвачено в локальную rf (анализ nullable не переносит проверку поля в локальные функции).

