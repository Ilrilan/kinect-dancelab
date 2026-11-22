# E25 — Тестируемость UI-логики (T22, аудит P2.12)

Статус: в работе
Начато: 2026-11-22
Бюджет: 70 минут

## План
1. Прочитать контекст (trial.md T22, MainWindow.cs, DanceLab.View, Core.Tests)
2. FrameFollower.cs в src/DanceLab.View (чистая математика, без WPF)
3. MainWindow делегирует FrameFollower, CamTrack в View
4. tests/DanceLab.View.Tests (xunit, net48), 6 тестов (а)-(е)
5. Верификация: build 0/0, test всех проектов, player_trial --no-sensor --shot

## Журнал
- [старт] Читаю контекст.
- [+5мин] Контекст собран: UpdateVideoFrame/TryGetAutoFrame/LoadCamRoot MainWindow.cs:619-806,
  константы 186-197. PlayerTrial.csproj УЖЕ ссылается на DanceLab.View — правка csproj не нужна.
  План: CamTrack (обёртка double[][] строк) + TryGetBox в View; FrameFollower c Update(...)→(Zoom,PanX,PanY);
  LoadCamRoot возвращает Dictionary<string,CamTrack>; MainWindow хранит follower.
  Замечено для отчёта: в fallback панорама Y по формуле = +0.07*windowH (не 0) — бриф говорит «панорама 0»,
  сохраняю дословно прежнее поведение, проверю тестом.
- [+8мин] Готово: src/DanceLab.View/FrameFollower.cs (CamTrack + FrameFollower, формулы дословно);
  MainWindow.cs: поля констант/сглаживания удалены → follower, LoadCamRoot возвращает
  Dictionary<string,CamTrack>, UpdateVideoFrame делегирует, TryGetAutoFrame удалён.
  PlayerTrial.csproj уже ссылался на View (правка csproj не потребовалась).
- [след.] Тест-проект tests/DanceLab.View.Tests + добавление в sln.
- [+12мин] Готово: tests/DanceLab.View.Tests (csproj + FrameFollowerTests.cs, 9 тестов: (а)-(е)
  из брифа + CamTrack.TryGetBox + Reset); проект добавлен в DanceLab.sln (x64 конфиги, папка tests).
- [след.] dotnet build DanceLab.sln — жду результат.
- [+18мин] dotnet build DanceLab.sln: 0 предупреждений / 0 ошибок.
- [+22мин] Тесты: View.Tests сначала 6/9 — 3 падения из-за ошибок в самих ожиданиях тестов
  (цель панорамы от незашаженного z; окно 0.7 с не накрывает соседние кадры; повторный запрос
  на том же фолловере несёт состояние сглаживания — сделал свежие инстансы). Исправлено,
  View.Tests 9/9. Capture.Tests 45/45, Core.Tests 38/38.
- [след.] Финальный прогон build+test всех трёх, затем player_trial --no-sensor --shot.
- [+25мин] Финальный build sln: 0/0. Запускаю player_trial --no-sensor --shot
  data/preview/trial-shot-e25.png --shot-after 3 (долгая команда, старт 25-я минута бюджета).
- [+28мин] player_trial: exit 0, скриншот data/preview/trial-shot-e25.png создан.
  Сравнение с trial-shot-e24.png: авто-кадр/масштаб/центровка видео идентичны (фон — штора,
  стена, пол — в том же масштабе, танцор по центру), призрак/сцена/сетка пола/зелёная линия
  на месте, кольцо камер c01-c09 с активной c05, «Кадр x1.0 × авто». Различие — поза танцора
  в видео: соседний кадр при --shot-after 3, не логика кадра.

## Итог
- Статус: готово.
- dotnet build DanceLab.sln: 0 предупреждений / 0 ошибок.
- dotnet test: Capture 45/45, Core 38/38, View (новый) 9/9.
- player_trial --no-sensor --shot: exit 0, кадр по поведению идентичен e24.
- Примечание для ПМ: пункт брифа (д) «камера без данных → fallback 1.6, панорама 0»:
  PanX = 0 подтверждён; PanY по дословной формуле прежнего кода = +HeadShiftFrac*windowH
  (сдвиг окна вверх на 7% высоты) — не 0. Поведение прежнего кода сохранено, тест
  MissingCamera_FallbackZoom16_PanXZero фиксирует формулу.
