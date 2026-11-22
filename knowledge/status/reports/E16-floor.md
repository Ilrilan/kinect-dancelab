# E16 — Пол живой сцены из FloorClipPlane Kinect

- Статус: в работе
- Начато: 2026-11-22
- Бюджет: 60 минут
- Задача: T17 из specs/player/trial.md (аудит P1.6)

## План

1. Прочитать: specs/player/trial.md (T17, S15), Frames.cs, KinectPoseSource, MainWindow.cs (пол), тесты Capture.
2. Capture: SkeletonFrame.FloorY (double?), KinectPoseSource заполняет из FloorClipPlane, Nuitrack — null.
3. player_trial: приоритет FloorY (валидность |FloorY|<3, сглаживание 0.3), HUD «(kinect)»/«(лодыжки)»/«оцениваю»; сериализация FloorY в skeleton.jsonl.
4. Тесты: сериализация с полем/без, выбор источника пола.
5. Верификация: dotnet build, dotnet test x2, player_trial --no-sensor --shot.

## Прогресс

- [2026-11-22] Старт: читаю входные файлы.
- Прочитал trial.md (T17/S15), Frames.cs, KinectPoseSource (FloorClipPlane уже едет в SceneInfo.FloorPlane), SkeletonRecord, MainWindow (пол по лодыжкам), тесты, схему capture-record.schema.json.
- Два отклонения от брифа (зафиксированы для отчёта):
  1. «y-компонента FloorClipPlane = высота пола» — нет: SDK-доки (hessian-форма): (x,y,z) — нормаль, w — расстояние; высота пола на оси Y при x=z=0 это -w/y. Формула FloorYFromPlane = -d/b, план (0,1,0,-1.2) → 1.2.
  2. Поле верхнего уровня «floorY» в skeleton.jsonl запрещено схемой (additionalProperties: false, кадр и scene закрыты), specs/ исполнителю править нельзя, иначе падает тест EveryLineMatchesSchema. Решение: в записи floorY едет внутри scene.other (схема «other» открыта), читатель поднимает его обратно в SkeletonFrame.FloorY; старые записи без поля дают null.
- Ещё находка: MultiUserReconstructor.Process (player_trial, вне разрешённых файлов) строит новые SkeletonFrame и теряет FloorY. Обход в MainWindow.cs (разрешён): подписка на сырой Source.FrameReceived.
- [2026-11-22] Правки: Frames.cs (FloorY, FloorSource, PickFloorSource, FloorYFromPlane), KinectPoseSource (заполнение FloorY), SkeletonRecord (писатель/читатель), MainWindow (источник пола, HUD), FloorTests.cs.
