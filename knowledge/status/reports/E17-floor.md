# E17 — завершение T17 «Пол из FloorClipPlane»

Статус: в работе
Начато: 2026-11-22
Бюджет: 30 минут. Запрещено коммитить.

## План

1. Прочитать WIP 167f672 и отчёт E16-floor.md, составить список хвостов.
2. Проверить по брифу T17: FloorY в SkeletonFrame, Kinect/Nuitrack заполнение,
   сериализация skeleton.jsonl, MainWindow (приоритет, сглаживание 0.3, HUD),
   FloorTests.
3. Доделать хвосты минимально.
4. Верификация: dotnet build, dotnet test (Capture, Core), player_trial --no-sensor --shot.

## Журнал

- [старт] Создан файл статуса.
- Прочитал WIP 167f672 и E16-floor.md. Хвосты по брифу T17:
  1. FloorY nullable + Kinect из FloorClipPlane + Nuitrack null — БЫЛ в WIP (Frames.cs, KinectPoseSource.cs:331, NuitrackPoseSource.cs:291 без floorY). Формула -d/b (E16: отклонение от брифа, hessian-форма) — подтверждена тестами.
  2. Сериализация floorY в scene.other (схема закрывает верхний уровень) + чтение, старые записи null — БЫЛ в WIP (SkeletonRecord.cs, читатель через ToObject→GetDouble, `is double` работает).
  3. MainWindow: приоритет FloorY, сглаживание 0.3, HUD (kinect)/(лодыжки)/оцениваю, подписка на сырой Source.FrameReceived (обход MultiUserReconstructor, теряющего FloorY) — БЫЛ в WIP.
  4. FloorTests: 9 тестов (плоскость, приоритет, сериализация с полем/без/без сцены, legacy-строки) — БЫЛ в WIP.
- Известные ограничения вне разрешённых файлов (не трогаю): Conversion.ToFrame (src/DanceLab.Core) и MultiUserReconstructor (player_trial, sensor_view) не переносят FloorY — на T17 не влияет (пол берётся с сырого источника), pose_eval/sensor_view вне задачи T17.
- Остаток работы: верификация (build, тесты x2, --no-sensor --shot).
- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Capture.Tests: 45/45 прошли, 0 упали.
- dotnet test tests/DanceLab.Core.Tests: 37/38, падает ReferenceCanonicalJsonFromImporterLoads — LegLengthM локального data/derived/trial/gLO_sBM_cAll_d14_mLO5_ch02.canonical.json = 0.679 вне диапазона теста 0.710–0.713. Причина: локальный эталон перегенерирован T16-импортёром (SMPL, вариант B), диапазон в тесте от старого COCO-варианта (T1). WIP 167f672 и правки E17 файлы DanceLab.Core не трогают; tests/DanceLab.Core.Tests вне разрешённых файлов E17 — не правлю, фиксирую для ПМ.
- Запускаю player_trial --no-sensor --shot.
- НАХОДКА: первый прогон `dotnet run --project tools/player_trial --no-build` снял кадр из УСТАРЕВШЕГО бина (UI эпохи T3, bin/Debug от 21.11 22:29). Причина: sln собирается в платформу x64 (выход bin/x64/Debug), а dotnet run без сборки берёт bin/Debug (AnyCPU) — sln-сборка бин player_trial не обновляет. Прогон повторён БЕЗ --no-build: dotnet run собрал проект, UI актуальный.
- player_trial --no-sensor --shot data/preview/trial-shot-e17.png --shot-after 3: exit 0, HUD «без сенсора / Время: 00:02 / пол: оцениваю» — критерий брифа выполнен.
- Итог: хвосты кода не найдены — все пункты брифа T17 были в WIP 167f672 и подтверждены (код-ревью + тесты + прогон). Правки кода E17 не потребовались.
- Микро-задача от ПМ (разрешение расширено на один тест): CanonicalTests.ReferenceCanonicalJsonFromImporterLoads — диапазон LegLengthM 0.710-0.713 (COCO) меняется на 0.670-0.690 с комментарием про SMPL-источник с T16. Локальный эталон 0.679 входит в новый диапазон. Правка ограничена строкой 490 + комментарий.
- Правка внесена, дифф проверен: только строка InRange + комментарий (2 строки). dotnet test tests/DanceLab.Core.Tests без --no-build: 38/38, 0 не пройдено (TSec-ассёрт 7.366-7.368 тоже проходит). Готово.
- Известное вне задачи: tests/DanceLab.Core.Tests 37/38 (падение от T16-данных, см. выше); Conversion.ToFrame и MultiUserReconstructor (sensor_view, pose_eval) не переносят FloorY — на T17 не влияет.
