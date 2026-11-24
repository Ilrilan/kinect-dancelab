# E52a: подложка HUD и ускорение тура (T47)

Статус: завершена (2026-11-24), отчёт ПМу отправлен

## Сделано (код)
- MainWindow.cs: hud (TextBlock) очищен от Background/Padding/Margin/выравнивания; новое поле
  hudPlate (Border: #0A0B0E альфа 0xB0, CornerRadius 4, Padding 8,4,8,4, Margin 14,14,8,8,
  Child = hud); в ghostPanel добавляется hudPlate вместо hud (S41).
- MainWindow.cs: TourStepAsync разделён на SeekTourMomentAsync (перемотка + 600 мс, один раз
  на момент) и TourZoomStepAsync (зум, сглаживание, снимки, строка отчёта — на каждый зум).
- MainWindow.cs RunTourAsync: удаление старого tour.log в начало метода (до первых записей);
  Directory.CreateDirectory(seqDir) после проверки track == null; при незагрузившемся эталоне
  строка «<seq>: RESULT: FAIL: эталон не загрузился» в tour-set-errors.log каталога тура.
- Program.cs RunTourSet: tour-set.log перезаписывается пустой строкой в начале запуска.

## Проверки
- dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок (первая сборка дала CS0236:
  инициализатор поля не может ссылаться на поле экземпляра hud; Child назначен в конструкторе).
- dotnet test tests/DanceLab.View.Tests — пройдено 29, не пройдено 0.
- Тур с замером (--tour data/preview/tour-speed --tour-times 2,4,6,8,10): exit=0, 71 с
  (было ~240 с); 15 листов, tour.tsv 136 строк, tour.log «RESULT: PASS».
- Smoke (--no-sensor --shot .../smoke.png --shot-after 3): exit=0, smoke.png 304194 байт;
  снимок удалён, каталог tour-speed оставлен.
- --tour-set 1 запись: exit=0, tour-set.log = «RESULT: PASS» (журнал начат заново).
- --tour-set 2 записи (вторая несуществующая gNO_sXX_cAll_d00_mNO0_ch99): exit=1,
  tour-set.log = «gNO_...: FAIL» + «RESULT: FAIL» (только строки этого запуска);
  tour-set-errors.log = «gNO_...: RESULT: FAIL: эталон не загрузился»; каталог записи
  не создан.
- Уборка: временный список _e52-set.txt и tour-set-errors.log удалены.

## Изменённые файлы
- tools/player_trial/MainWindow.cs
- tools/player_trial/Program.cs
- knowledge/status/reports/E52-tour-window.md (этот файл)


## E54: столбцы curRootDx и ghostRootDx в tour.tsv (конец T47)

Статус: завершена (2026-11-24, исполнитель E54)

Сделано:
- MainWindow.cs: TourTsvColumns после sceneOpaquePx добавлены curRootDx, ghostRootDx (33 столбца);
  BuildTourRow считает curRootDx = (rootU − videoW/2)·s·z + panX (s = min(winW/videoW, winH/videoH),
  z — итоговый зум шага step.Zoom) по кадру cams.json с временем ровно t; нет кадра или видео —
  пусто. ghostRootDx = ghost.ReferenceRootX() − ghostPanel.ActualWidth/2; NaN — пусто.
- MainWindow.cs: MinSceneOpaquePx читала sceneOpaquePx как последнее поле строки — после добавления
  двух столбцов в конец сломалась бы (минимум всегда 0, тур всегда FAIL); теперь ищет столбец
  по имени в TourTsvColumns и парсит по индексу.
- src/DanceLab.View/FrameFollower.cs: CamTrack.TryGetRoot(t, out (double U, double V) root) —
  тот же двоичный поиск, что TryGetBox с окном 0, кадр с временем ровно t; false, если кадра нет.
  Других правок в файле нет.

Проверки:
- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.View.Tests (без --no-build): пройдено 31, не пройдено 0.
- Тур из bin/x64/Debug/net48: ./player_trial.exe --seq gLO_sBM_cAll_d14_mLO5_ch02
  --tour data/preview/tour-root --tour-times 4 --tour-zooms 1 → exit=0, tour.log «RESULT: PASS»,
  sceneOpaquePx минимум 339282. tour.tsv: заголовок 33 поля, последние три — sceneOpaquePx,
  curRootDx, ghostRootDx; 10 строк (заголовок + 9 камер).
- Значения (камера: curRootDx / ghostRootDx): c01 30.127/7.066, c02 33.844/−1.321,
  c03 12.476/−3.291, c04 −35.563/−6.534, c05 −35.689/−10.673, c06 −1.855/3.181,
  c07 16.912/7.146, c08 18.532/18.307, c09 29.8/12.075 — все в ожидаемых пределах (−60..60 px).

Изменённые файлы:
- tools/player_trial/MainWindow.cs
- src/DanceLab.View/FrameFollower.cs (только CamTrack.TryGetRoot)
- knowledge/status/reports/E52-tour-window.md (этот файл)
- данные тура data/preview/tour-root/ (вне репозитория)


## E55: cur* в BuildTourRow — зум step.Zoom вместо множителя слайдера

Статус: в работе (исполнитель E55, 2026-11-24)

Сделано:
- MainWindow.cs, BuildTourRow (строки ~2468-2471): в четырёх формулах curLeft/curTop/
  curRight/curBottom множитель `z` заменён на `step.Zoom` (итоговый зум шага, как в
  FrameFollower и как в соседнем вычислении curRootDx). Больше ничего не менялось;
  XML-комментарий над методом слайдер для cur* не упоминал — не правился.

Проверки:
- dotnet build DanceLab.sln — 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.View.Tests (без --no-build) — пройдено 31, не пройдено 0.
- Тур из bin/x64/Debug/net48 (--tour data/preview/tour-cur --tour-times 4 --tour-zooms 1,3):
  exit=0. tour.tsv, камера c05 (zoom / z / curLeft / curTop / curRight / curBottom):
  - zoom 1: 1 / 2.424 / 213.11 / 107.2 / 397.892 / 459.611
  - zoom 3: 3 / 7.271 / 47.385 / -303.008 / 601.731 / 754.226
  Отклонения cur* от центра окна (617.667×548.707) при zoom 3 в ~2.6-3.4 раза больше,
  чем при zoom 1 — ожидание «примерно втрое» подтверждено (не ровно 3, т.к. PanX/PanY
  шагов различаются и прибавляются после масштабирования).




