# E43: HeadingUnwrapper (T39)

## Статус
- Завершена 2026-11-24. Сборка и тесты зелёные.

## Прогресс
- Спека прочитана: specs/player/trial.md, строки ~229 (S39, разворот направления взгляда). Формула совпадает с брифом.
- Vec3.cs и CanonicalTests.cs прочитаны (стиль: XML-комментарии на русском, xUnit, Assert.Equal с точностью).
- Конфигурация: net48, LangVersion 10.0, Nullable enable; Math.Clamp в net48 недоступен и в базе не используется — приватный Clamp-хелпер.
- Создан src/DanceLab.Core/Canonical/HeadingUnwrapper.cs: public sealed class, константы SwitchRad (120°), ReliabilityFloor (0.25), MinGain (0.1), Update, Reset, приватные Wrap и Clamp.
- Создан tests/DanceLab.Core.Tests/HeadingUnwrapperTests.cs: 6 тестов по брифу.
- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Core.Tests: до 38 тестов (все зелёные), после 44 (все зелёные, +6 новых).
- Коммит не делался (по брифу), git не трогался.
