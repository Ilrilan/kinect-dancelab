# E33: окно player_trial помещается в рабочую область экрана

- Задача: T30 из `specs/player/trial.md` — заменить фиксированные Width=1320/Height=780 на подгон под `SystemParameters.WorkArea` (S31).
- Дата: 2026-11-23
- Исполнитель: implementer (flash), бриф от ПМ по результатам визуального ревью.

## Прогресс

- старт: локализован конструктор `MainWindow(Options)` (строка 311), фиксированные размеры на строках 315-316.
- правка внесена: строки 315-316 заменены на блок WorkArea (Width/Height через Math.Min, MaxWidth/MaxHeight через Math.Max, CenterScreen), комментарий S31 по тексту брифа.
- верификация пройдена, задача готова к приёмке.

## Отчёт

### Сделал

- `tools/player_trial/MainWindow.cs`: в конструкторе `MainWindow(Options)` (строка 311) удалил фиксированные `Width = 1320; Height = 780;` и вставил блок подгонки под рабочую область: комментарий S31 (строки 315-316), `var workArea = SystemParameters.WorkArea;` (317), `Width = Math.Min(1320, workArea.Width);` (318), `Height = Math.Min(780, workArea.Height);` (319), `MaxWidth = Math.Max(320, workArea.Width);` (320), `MaxHeight = Math.Max(240, workArea.Height);` (321), `WindowStartupLocation = WindowStartupLocation.CenterScreen;` (322). Текст кода — дословно из брифа. Больше ничего в файле не менял.
- `knowledge/status/reports/E33-window.md`: этот файл статуса.

### Прогнал

- `dotnet build DanceLab.sln` — сборка успешна, 0 предупреждений, 0 ошибок, EXIT=0.
- `dotnet test tests/DanceLab.View.Tests` — не пройдено 0, пройдено 21, всего 21, EXIT=0.
- `dotnet test tests/DanceLab.Core.Tests` — не пройдено 0, пройдено 38, всего 38, EXIT=0.
- `dotnet test tests/DanceLab.Capture.Tests` — не пройдено 0, пройдено 45, всего 45, EXIT=0.
- `tools/player_trial/bin/x64/Debug/net48/player_trial.exe --no-sensor --shot data/preview/trial-shot-e33-default.png --shot-after 4` (из корня репо) — EXIT=0, скриншот создан `data/preview/trial-shot-e33-default.png` (~315 КБ). `--no-build` нигде не использовался при сборке плеера (сборка отдельным шагом до этого).

### Не подтвердил / проблемы

- Фактическое размещение окна на экране (работоспособность на 150% масштабе) скриншотом `--shot` не подтверждается принципиально: он рендерит контент, а не экран, — реальную посадку окна проверяет ПМ отдельным снятием экрана, как сказано в брифе. Код-тествоых путей и принудительных ресайзов для скриншота не добавлял.
- Тест-код не менялся (правка не покрывается юнит-тестами — геометрия окна), все существующие тесты зелёные.

