# E45: CanonicalTrack, формат версии 2 (T38, часть 2)

Статус: завершена
Начато: 2026-11-24

## План
- Прочитать спеку specs/player/trial.md (Contract, «Формат эталона, версия 2 (S39)»).
- CanonicalTrack.cs: FormatVersion=2, Load требует версию 2 и torso.roll+torso.basis(9), Copy переносит TorsoRollRad/TorsoBasis, Lerp интерполирует roll и basis.
- CanonicalTests.cs: фикстуры на версию 2, тесты на отказ версии 1, отсутствие roll/basis, содержимое кадра, интерполяцию.
- Прогон: dotnet build DanceLab.sln, dotnet test tests/DanceLab.Core.Tests (55 тестов до правки — проверить).

## Ход работы
- [x] Создан файл статуса.
- [x] Чтение спеки (trial.md, «Формат эталона, версия 2 (S39)», строки 217-245) и кода.
- [x] Правки CanonicalTrack.cs: FormatVersion=2, ParseFrame требует torso.roll (число) и torso.basis (массив 9), ошибки с файлом и временем кадра; Copy и Lerp переносят roll/basis (basis: у обоих — покомпонентно, у одного — имеющийся, нет — null).
- [x] Правки CanonicalTests.cs: фикстура TrackSampleInterpolatesLinearly на версию 2 (roll+basis в обоих torso); TrackLoadRejectsUnknownFormatVersion переведён на версию 3 (2 теперь поддерживается); добавлены 5 тестов: отказ версии 1, отказ без torso.basis, отказ без torso.roll, сохранение roll/basis в загруженном кадре, интерполяция roll и basis.
- [x] Сборка и тесты: dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок; dotnet test tests/DanceLab.Core.Tests — 60/60 зелёные (было 55).

## Замечания
- Во время работы параллельный исполнитель (T41) перегенерировал data/derived/trial/gLO_sBM_cAll_d14_mLO5_ch02.canonical.json до formatVersion 2 — ReferenceCanonicalJsonFromImporterLoads проходит без пропусков.
