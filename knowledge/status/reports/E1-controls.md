# E1: Управление воспроизведением пробного плеера (T5+T7)

E1: старт 2026-11-22 00:38

## План (из брифа)
1. Фикс рестарта после MediaEnded: Stop() перед Position=Zero и Play().
2. Вторая строка тулбара: «▶ Плей»/«⏸ Пауза», «⟳ Рестарт», «Попытка».
3. Попытка (S14): пауза + отсчёт 3/4/5 с (дефолт 5), по нулю RestartFromZero().
4. Ракурсы (S13): комбобокс камер c01..c09 по наличию файлов, смена Source с сохранением позиции.
5. Скорость (S17): Slider 0.2..2.0 шаг 0.1, SpeedRatio + резервные часы.
6. Space/R работают, текст кнопки синхронен.

## Ход работы
- 00:38 прочитаны specs/player/trial.md (S12-S14, S17, T5, T7), tools/player_trial/MainWindow.cs.
- 00:45 все правки внесены в tools/player_trial/MainWindow.cs: фикс RestartFromZero (Stop перед Zero/Play),
  вторая строка тулбара (Плей/Пауза, Рестарт, Попытка, Отсчёт 3/4/5, Ракурс c01..c09, Скорость 0.2..2.0 + x-лейбл),
  отсчёт-оверлей поверх сцены (DispatcherTimer 1 с, повторный клик перезапускает), ракурсы с сохранением позиции
  через pendingRestore/pendingResume в MediaOpened, скорость = SpeedRatio + rate в резервных часах (StopwatchLike
  накопительный), TogglePlay после MediaEnded → RestartFromZero (та же жалоба владельца через кнопку/Space),
  UpdatePlayButton синхронизирует текст кнопки со Space во всех точках смены paused.
- 00:47 dotnet build DanceLab.sln: 0 предупреждений / 0 ошибок (первый прогон поймал CS1061 media.Duration →
  NaturalDuration, исправлено).
- 00:48 dotnet test tests/DanceLab.Core.Tests: 0 не пройдено / 32 пройдено / всего 32.
- 00:52 скриншотная приёмка: player_trial --no-sensor --shot data/preview/trial-shot-e1.png --shot-after 3 —
  exit 0, png создан; на снимке вторая строка тулбара: «⏸ Пауза», «⟳ Рестарт», «Попытка», «Отсчёт: 5»,
  «Ракурс: c05» (текущий выбран при старте), «Скорость» слайдер + «x1.0»; глифы ⏸/⟳ рендерятся.
- 00:53 готово. Итог в отчёте агенту.
