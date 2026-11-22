# E18 — Статус калибровки длин ног в HUD (T18, specs/player/trial.md, аудит P1.7)

- Статус: в работе
- Начато: 2026-11-22
- Разрешённый файл: tools/player_trial/MainWindow.cs
- Бюджет: 30 минут

## План

1. Прочитать specs/player/trial.md (T18) и MainWindow.cs (CollectCalibration, UpdateHud).
2. Добавить строку HUD «нога: калибрую N с» / «нога: X.XX м, готово» в обе ветки с живым пользователем.
3. Сборка + тесты + скриншот --no-sensor.

## Журнал

- [старт] файл создан, читаю спеку и код.

## Журнал (продолжение)

- [5 мин] Спека T18 и MainWindow.cs прочитаны (CollectCalibration:810, UpdateHud, FloorHudLine:1028 как образец, перезапуск calibStart:855). Понял ветки HUD: «без сенсора» (903), «не вижу» (911), пауза-Inlines (921-926), бегущие оценки (930).
- [7 мин] Код внесён: хелпер LegHudLine(inv, seen) после UpdateHud + appending строки в ветки «не вижу», пауза, оценки. Ветка «без сенсора» без строки. Перезапуск калибровки (сброс calibStart в CollectCalibration:855) автоматически даёт счёт с нуля.
- [8 мин] Начинаю верификацию: dotnet build DanceLab.sln.

## Верификация

- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Capture.Tests (без --no-build): 45/45 пройдено.
- dotnet test tests/DanceLab.Core.Tests (без --no-build): 38/38 пройдено.
- player_trial --no-sensor --shot data/preview/trial-shot-e18.png --shot-after 3: exit 0, снимок создан; HUD «без сенсора / Время / пол: оцениваю» без строки калибровки (ожидаемо: сенсора нет).

## Код-путь (для критика, построчно)

tools/player_trial/MainWindow.cs, едиственный изменённый файл:

1. LegHudLine(inv, seen) (после UpdateHud, ~строка 936):
   - guard `opts.NoSensor || session == null || !seen` -> null: без сенсора или без видимого пользователя строки нет.
   - `!calibrated`: calibStart не задан -> null (пользователь ещё не обработан); иначе elapsed = целые секунды от calibStart, кламп [0..5], возврат «нога: калибрую N с».
   - `calibrated`: «нога: X.XX м, готово» (userLegM, формат 0.00).
2. UpdateHud:
   - ветка «без сенсора» (opts.NoSensor || session == null): не изменена, строка не добавляется.
   - ветка «не вижу» (!seen || score == null): legLine добавляется только при seen==true (пользователь виден, score ещё не готов); при !seen null — строки нет.
   - ветка паузы (Inlines, S22): legLine добавляется последним Run после floorLine (seen здесь всегда true).
   - ветка бегущих оценок: legLine конкатенируется после floorLine.
3. Перезапуск калибровки: CollectCalibration при вырожденном окне (thigh<=0 || shin<=0) сбрасывает calibStart = UtcNow и очищает списки; calibrated остаётся false, поэтому LegHudLine продолжает «калибрую», счёт от нового calibStart — дополнительный код не нужен.
4. Обновление HUD каждые 6 тиков (~200 мс) — счётчик N идёт 0,1,2,3,4,5 и сменяется «готово».
5. seen = lastUserSeen свежее 1 с (ставится в UpdateLive при comparison.Coverage > 0) — сигнал «пользователь виден»; живой скелет гарантирует calibStart (CollectCalibration вызывается в UpdateLive до расчёта coverage).

## Статус: завершено, отчёт отправлен
