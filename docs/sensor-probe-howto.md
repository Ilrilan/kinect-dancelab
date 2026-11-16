# Запись замеров с сенсора: как запускать

Перед началом.

- Kinect подключён к USB 3.0 родным блоком питания.
- В Параметры, Система, Звук, Ввод, Набор микрофонов (Xbox NUI Sensor): Улучшение звука выключено (см. docs/kinect-v2-dropouts.md). Обновления Windows могут включить эффект снова.
- Закрыты Kinect Studio, Kinect Configuration Verifier и Nuitrack Viewer: один сенсор нельзя открывать ими и зондом одновременно.
- Для Nuitrack пробная лицензия активирована (окно Nuitrack.exe из меню Пуск). Пробный сеанс работает три минуты, самый длинный прогон набора 30 секунд.
- Свободно не менее 6 ГБ оперативной памяти для записей глубины и цвета (предпроверка сама откажется, если памяти мало).

Сборка.

```
dotnet build DanceLab.sln
dotnet test tests/DanceLab.Capture.Tests/DanceLab.Capture.Tests.csproj
node --test tools/capture_report/report.test.ts
```

Набор записей по порядку, на каждый трекер отдельно (по одной инструкции на экране, ждёт Enter, обратный отсчёт 5 секунд перед записью):

```
node tools/sensor_probe/session.ts --tracker kinect
node tools/sensor_probe/session.ts --tracker nuitrack
```

Отдельные записи: параметр --only 5,6 (номера из списка в tools/sensor_probe/session.ts). Записи лежат в data/captures/<дата-время>_<трекер>_<сценарий>/ (вне git).

Прямой запуск без сценария.

```
tools/sensor_probe/bin/x64/Debug/net48/sensor_probe.exe --tracker kinect --scenario my-test --mode skeleton --duration 10
```

Режим depthcolor пишет ещё глубину и цвет (около 1,5-3 ГБ на прогон). Nuitrack по умолчанию запускается в режиме AI (--nuitrack-engine CNN_HPE), для родного трекинга укажите --nuitrack-engine config.

Сводка по записи.

```
node tools/capture_report/report.ts data/captures/<папка>/skeleton.jsonl
```

Показывает частоту кадров, долю кадров с человеком, шум суставов, стабильность длин костей, доли состояний и уверенность, плоскость пола, смены сторон плеч (переворот) и повторяемость движения рук (период и высота пика по 6 повторам).

Коды выхода sensor_probe: 0 успех, 1 ошибка аргументов, 2 мало памяти, 3 нет сенсора или он не отдаёт данные, 4 переполнение очереди записи.
