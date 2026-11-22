# E26: Медиа-quirks MediaElement — регрессионные сценарии (T23, аудит P2.13)

Статус: готово (отчёт ПМ 2026-11-22)
Бюджет: 45 минут. Начало: 2026-11-22

## План

1. Изучить tools/player_trial (аргументы, --shot-механика, UIA-примеры E10)
2. --regress media: сценарий в живом окне (пауза/плей/скорость/рестарт/MediaEnded+Space)
3. Утверждения в окне: позиция < 1 с после рестарта и после MediaEnded+Space; скорость x2 по двум замерам
4. IsAtEnd(position, duration) — статический метод + тест в View.Tests (3 случая)
5. Верификация: dotnet build 0/0, dotnet test x3, прогон --no-sensor --regress media

## Лог
- [факты] MainWindow (tools/player_trial): ветка конца ролика в TogglePlay — порог 200 мс против
  media.NaturalDuration.TimeSpan; RestartFromZero использует Stop перед Position=0; MediaEnded ставит
  paused=true (финал-оверлей только с lastScore). UIA в E4/E10 был интерактивным из живой сессии —
  для --regress сценарий делается в самом окне: клавиши RaiseEvent(PreviewKeyDown), слайдер программно.
- [код] MediaQuirks.cs новый (IsAtEnd, EndThresholdMs=200); MainWindow.cs: ветка TogglePlay на IsAtEnd,
  SavePng выделен из ShotIfRequested, RunMediaRegression (шаги по брифу, лог построчно, ассерты
  PASS/FAIL в лог+статус, скриншот+закрытие), SendKey (RaiseEvent PreviewKeyDown), хелперы
  WaitFor/Pos/Dur/RepoPath; Program.cs: Options.Regress, разбор --regress media; View.Tests: csproj
  +UseWPF и ProjectReference на player_trial, MediaQuirksTests (конец/почти конец/середина).
- [отклонение от брифа, мелкое] старт сценария = дождаться MediaOpened, затем SendKey(R) с нуля —
  иначе конец трека может наступить посреди замеров скорости; в логе это «шаг=старт(R)».
- [сборка] dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- [тесты] dotnet test --no-build: Core 38/38, Capture 45/45, View 9/9 (6 FrameFollower + 3 новых MediaQuirks).
- [прогон 1] player_trial --no-sensor --regress media: exit 0; лог data/preview/trial-regress-media.log —
  все ассерты PASS (позиция после рестартов 0.271/0.286/0.275 с < 1 с; пауза/плей Space;
  SpeedRatio 2.0 на лету и обратно 1.0, ratio замеров 2.02; MediaEnded на 7.00 с; RESULT: PASS);
  скриншот data/preview/trial-regress-media.png (1282x742, пауза, Время 00:01, скорость x1.0).
- [прогон 2] повторный запуск: exit 0, RESULT: PASS (0.284/0.290 с, ratio 2.02) — стабильно.
- [статус] готово, отчёт ПМ. Примечание: чекбокс T23 в specs/player/trial.md и --regress в его Contract
  не правлю (specs вне разрешённых файлов) — отметить при приёмке.
