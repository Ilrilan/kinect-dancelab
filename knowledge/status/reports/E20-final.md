# E20 — Финальный экран итогов трека (T20, аудит P1.9)

Статус: завершена
Дата: 2026-11-22
Исполнитель: E20 (implementer)
Разрешённый файл: tools/player_trial/MainWindow.cs (других изменений нет)

## Что сделано
- Оверлей финала: Border finalCard по центру ghostPanel (тёмная полупрозрачная карточка
  #b81b1c22, скругление, паддинг), внутри StackPanel: «Трек пройден» (26pt), итог NN%
  (72pt bold, Total01), «Грубые ошибки: N», «Покрытие: NN%» (среднее за трек),
  «Длительность: mm:ss» (ClockSeconds на момент MediaEnded), кнопки «Ещё раз» и «Закрыть».
- Появление: media.MediaEnded -> finalDurationSec = ClockSeconds(); ShowFinalOverlay();
  оверлей показывается только при lastScore != null (--no-sensor / нет человека — нет).
- Скрытие: StartAttempt (вход «Ещё раз» и кнопки «Попытка»/отсчёт) и RestartFromZero
  (R, Space на конце трека, конец отсчёта) вызывают HideFinalOverlay; «Закрыть» — просто
  HideFinalOverlay. Смена ракурса оверлей СОХРАНЯЕТ (решение ПМ): SwitchAngle не тронут.
- Среднее покрытие: TrialScore.Coverage — покрытие последнего сравнения, поэтому в
  MainWindow копятся coverageSum/coverageCount (рядом с lastScore = scorer.Update),
  сброс в RestartFromZero вместе с lastScore; при count==0 fallback на score.Coverage.
- Запись и сенсор оверлей не трогает (чисто UI-элемент в ghostPanel, topmost по порядку
  добавления; StartAttempt прячет карточку до показа countdownText — наложений нет).

## Верификация
- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Capture.Tests (без --no-build): 45/45 пройдено, 0 не пройдено.
- dotnet test tests/DanceLab.Core.Tests (без --no-build): 38/38 пройдено, 0 не пройдено.
- Снимок 1: player_trial --no-sensor --shot data/preview/trial-shot-e20-end.png --shot-after 9
  (exit 0): окно дожило до конца трека 7.4 с (HUD «Время: 00:07», кнопка «Плей» — пауза
  после MediaEnded), оверлея нет (lastScore null — корректно), падения нет.
- Снимок 2: player_trial --tracker kinect --shot data/preview/trial-shot-e20-end-kinect.png
  --shot-after 9 (exit 0): сенсор стартовал («Стоп сенсора», HUD «не вижу»), конец трека,
  оверлея нет (lastScore null), падения нет.
- Сам оверлей на снимке не проверен: без живого танца lastScore остаётся null (по брифу
  DEBUG-ветку не заводить). Проверен код-путём построчно (в отчёте E20 для критика).

## Не подтверждено / на критика и живой прогон
- Визуальная приёмка оверлея (композиция карточки, читаемость 72pt процента) — только
  по коду, снимок возможен при живом прогоне владельца с танцем.
- Поведение «смена ракурса сохраняет оверлей» — по коду (SwitchAngle не тронут, карточка
  живёт в ghostPanel независимо от перезагрузки MediaElement), живым не проверено.
