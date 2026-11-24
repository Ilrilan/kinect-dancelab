# E48: окно, разворот направления живой позы и --tour-set

- Задача: два независимых пункта в tools/player_trial по specs/player/trial.md (S39).
- Пункт 1: HeadingUnwrapper в UpdateLive (5-й аргумент ToCanonical), Reset в RestartFromZero и ResetUserDisplay.
- Пункт 2: --tour-set <файл> в Program.cs, тур по списку записей за один процесс, лог data/preview/tour/tour-set.log.
- Бюджет: 2 файла кода (MainWindow.cs, Program.cs), 35 минут.

## Ход работы

- [x] Прочитаны MainWindow.cs, Program.cs, HeadingUnwrapper.cs, Canonicalizer.cs, спека specs/player/trial.md
- [x] Пункт 1: поле userHeading, 5-й аргумент ToCanonical в UpdateLive, Reset в RestartFromZero и ResetUserDisplay, второй конструктор MainWindow(Options, string)
- [x] Пункт 2: Options.TourSet + WithSeq, умолчание тура data/preview/tour, RunTourSet в Program.cs (схема: одно app.Run(), окна по очереди Show/Closed, Shutdown после последнего; повторный app.Run(window) в WPF невозможен)
- [x] Прогоны: build 0/0; View.Tests 29/29; Core.Tests 64/64; тур 2 записей exit=0 (два sheet-z1.0-t4.0.png, tour.log PASS); тур с gNO_sXX… exit=1 (tour-set.log: FAIL-строка + RESULT: FAIL); smoke no-sensor exit=0, smoke.png 303460 байт
- [x] Временный список data/preview/_e48-set.txt удалён
