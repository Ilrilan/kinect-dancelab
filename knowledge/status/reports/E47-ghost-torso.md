# E47: GhostCanvas по TorsoFrame (T42, часть 1)

Статус: завершена

## Брифт (кратко)

- GhostCanvas.cs: Solve строит торс по осям TorsoFrame из CanonicalFrame (формат v2, S39)
- Проверка: dotnet build DanceLab.sln, dotnet test tests/DanceLab.Core.Tests, dotnet test tests/DanceLab.View.Tests

## Ход работы

- 2026-11-24: создан файл статуса, читаю GhostCanvas.cs, CanonicalFrame.cs, TorsoFrame.cs, Vec3.cs.
- 2026-11-24: Solve переписан по TorsoFrame() (оси из f.TorsoFrame(), кости в системе таза leftT/up/fwdPelvis,
  бёдра по leftT, плечи по side = (leftT*cos(turn) + fwdPelvis*sin(turn)).Orthogonalized(up), fwdT = side x up,
  FwdH/LeftH без изменений); XML-комментарий над Solve переписан. Запущена сборка DanceLab.sln.
- 2026-11-24: dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок.
- 2026-11-24: dotnet test tests/DanceLab.Core.Tests — 60/60 пройдено, 0 не пройдено.
- 2026-11-24: dotnet test tests/DanceLab.View.Tests — 29/29 пройдено, 0 не пройдено. Задача завершена.

## Итог

Правки только в C:\work\dancelab\tools\player_trial\GhostCanvas.cs (метод Solve и его XML-комментарий).
DrawGhost, ReferenceBounds, ResetFrame, ComputeGroundShift, порядок слоёв — без изменений.
Коммиты не делались (по брифу).

