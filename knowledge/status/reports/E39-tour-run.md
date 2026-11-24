# E39: тур ракурсов в MainWindow (T35, часть 2)

Статус: завершена
Начато: 2026-11-24, окончено: 2026-11-24

## Сделано
- MainWindow.cs: TourExitCode (строка 36), TourTsvColumns, поле ghostPanel (189),
  camTracks (218, заполняется в ApplyStartup на 775), ветка тура в ApplyStartup (829),
  StepVideoFrame (940) + обёртка UpdateVideoFrame (975), RunTourAsync (2123),
  TourStepAsync (2313), BuildTourRow (2368), SaveTourSheet (2444), CaptureElement (2523),
  ReadCanonicalSource (2539), JoinNums.
- Program.cs Main: код возврата тура приоритетнее кода app.Run, [STAThread] сохранён.

## Проверки
- dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.View.Tests (без --no-build) — 29/29.
- Пилот с видео gLO_sBM_cAll_d14_mLO5_ch02 --tour data/preview/tour-pilot --tour-times 2,4,6:
  exit=0, 9 листов, tour.tsv 82 строки (заголовок + 81), tour.log RESULT: PASS.
- Пилот брифа gBR_sFM_cAll_d04_mBR1_ch02: exit=1, «RESULT: FAIL: эталон не загрузился» —
  canonical.json этой записи отсутствует в data/derived/trial (есть только в scratch-каталогах
  _cand/_critic-e36), видео нет. Ветка track==null работает как в брифе.
- Реальная ветка «без видео» gKR_sFM_cAll_d28_mKR1_ch02 (canonical есть, видео нет):
  exit=0, 6 листов (только кадр сцены), tour.tsv 7 строк (6 + заголовок), 9 WARN,
  RESULT: PASS.
- Smoke существующего поведения: --regress media — exit=0, RESULT: PASS.
- Найденный в прогоне дефект: RepoPath рассчитан на путь к файлу (GetDirectoryName отрезал
  последний сегмент) — каталог записи не создавался; добавлено явное Directory.CreateDirectory(seqDir),
  финальный FlushLog защищён try/catch (TourExitCode=1 при ошибке записи лога).

## Отчет ПМ: отправлен (пилот 2 брифа не подтвердил по причине данных)

## E40
Задача: две точечные правки тура в tools/player_trial/MainWindow.cs.
Начато: 2026-11-24.
1) Пустой кадр сцены в листах: GhostCanvas не успевает отрисоваться до
   RenderTargetBitmap — перед CaptureElement(videoPanel/ghostPanel) в TourStepAsync
   добавлено ожидание прохода рендера диспетчера
   (await Dispatcher.InvokeAsync(..., DispatcherPriority.ApplicationIdle)).
2) Повторный запуск тура дописывал строки к старому отчёту: заголовок tour.tsv
   теперь пишется всегда (File.WriteAllText), tour.log удаляется перед началом.
Выполняется.

## Итог E40 (2026-11-24)
- MainWindow.cs: TourStepAsync — перед CaptureElement(videoPanel/ghostPanel) добавлено
  `await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);`
  (using System.Windows.Threading уже был, строка 14); RunTourAsync — заголовок
  tour.tsv пишется всегда (File.WriteAllText без условия), tour.log удаляется
  перед началом (File.Delete если существует).
- dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок.
- Прогон 1: exit=0; sheet-z1.0-t4.0.png = 736277 байт (>300 КБ, 9 кадров видео +
  9 кадров сцены); tour.tsv 10 строк; tour.log 11 строк, RESULT: PASS.
- Прогон 2 (тот же каталог): exit=0; tour.tsv 10 строк (не 19); tour.log заново
  11 строк (не дописан до 22), RESULT: PASS.
- Неточно в брифе: ожидался tour.log из одной строки RESULT: PASS — фактически
  приложение всегда пишет seq=строку, 9 строк по камерам и RESULT (11 строк на
  запуск при --tour-times 4 --tour-zooms 1). Перезапуск лога при этом работает.

## E41
Задача: точечная правка тура в MainWindow.cs — пустой кадр сцены в листах тура.
Начато: 2026-11-24.
1) TourStepAsync: ожидание диспетчера (ApplicationIdle) не гарантирует проход
   отрисовки окна — заменено на ghost.InvalidateVisual() + await Task.Delay(250).
2) После CaptureElement(ghostPanel) подсчёт непрозрачных пикселей (альфа > 0)
   кадра сцены.
3) tour.tsv: столбец sceneOpaquePx последним (TourTsvColumns + BuildTourRow);
   tour.log: «, sceneOpaquePx минимум X» в строке камеры, минимум 0 → failure
   «камера cNN: кадр сцены пуст».
Выполняется.

## Итог E41 (2026-11-24)
- MainWindow.cs: пункты 2-3 брифа как заказано — TourStepAsync: подсчёт непрозрачных
  пикселей кадра сцены (CountOpaquePixels, альфа > 0, null → 0); TourTsvColumns +
  столбец sceneOpaquePx последним (31 колонка); BuildTourRow дописывает число в
  конец строки; RunTourAsync: «, sceneOpaquePx минимум X» в строке камеры, при
  минимуме 0 — failure «камера cNN: кадр сцены пуст» (итог FAIL).
- Пункт 1 (InvalidateVisual + Task.Delay(250)) внесён, но гипотезу брифа
  НЕ подтвердил: с ожиданием 250 мс лист вышел байт в байт как у E40
  (736277), sceneOpaquePx = 0 на всех камерах — дело не в рендер-проходе.
  Настоящая причина: RenderTargetBitmap.Render(элемент-потомок) рендерит
  содержимое со сдвигом VisualOffset элемента внутри окна — ghostPanel (вторая
  колонка сетки, сдвиг ~665 px) целиком уезжала за границы снимка 618x549 и
  давала полностью прозрачный кадр; videoPanel (сдвиг ~10 px) почти целиком
  попадала, поэтому кадры видео в листах были. Исправление: CaptureElement
  рендерит через VisualBrush (Rectangle с Fill = VisualBrush(element),
  Measure/Arrange от (0,0), затем RenderTargetBitmap.Render) — содержимое
  рисуется от начала координат снимка.
- dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок (2 промежуточные
  итерации: CS8620 на tuple при инлайн-проверке sceneShot на null — подсчёт
  вынесен в CountOpaquePixels(BitmapSource?)).
- Пилот gLO_sBM_cAll_d14_mLO5_ch02 --tour data/preview/tour-pilot-fix2
  --tour-times 4 --tour-zooms 1: exit=0, RESULT: PASS, sceneOpaquePx минимум
  339282 на всех 9 камерах (= 618x549, вся панель), tour.tsv 10 строк
  (заголовок + 9), sheet-z1.0-t4.0.png = 889165 байт (было 736277, +153 КБ —
  кадры сцены с содержимым). tour.tsv: cols 31, последний столбец sceneOpaquePx,
  значения строк 2-4: 339282, 339282, 339282.
- Вне брифа (минимальная правка того же файла по факту): CaptureElement через
  VisualBrush — без неё цель брифа (непустой кадр сцены, сотни тысяч пикселей)
  недостижима, обе тайминговые версии не дали ничего.
- Спека specs/player/trial.md (строка 206) отстала от кода: контракт tour.tsv
  перечисляет 30 столбцов без sceneOpaquePx — править спеку исполнителю нельзя,
  обновить ПМ.
