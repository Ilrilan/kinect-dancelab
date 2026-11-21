# T2 Core: старт 2026-11-21 22:12

Статус: в работе.

## План
1. Прочитать specs/player/trial.md (Contract), specs/core/canonical-pose.md (S1-S9), существующие Types.cs, PoseReconstructor.cs, SkeletonRecord.cs, тесты.
2. Реализовать namespace DanceLab.Core.Canonical: CanonicalFrame, CanonicalTrack, TrialParams, TrialComparer, TrialScorer, Canonicalizer.
3. Юнит-тесты в tests/DanceLab.Core.Tests.
4. dotnet build DanceLab.sln; dotnet test tests/DanceLab.Core.Tests.

## Лог
- 22:12 создан файл статуса, читаю входные документы.
- 22:20 прочитаны trial.md (Contract, S1-S11), canonical-pose.md (S1-S13), Types.cs, PoseReconstructor.cs, SkeletonRecord.cs, тесты. Решение: System.Text.Json 8.0.5 (транзитивно из Capture), net48, LangVersion 10, nullable enable, TreatWarningsAsErrors. Базовая сборка sln запущена фоном.
- Решения по семантике: TrialComparison держит сырые углы костей (без мёртвой зоны) — иначе ErrorThreshold 0.8 недостижим при насыщении 1.0 и мёртвой зоне 0.35; мёртвая зона/насыщение применяются в TrialScorer. Валидность кости = все её суставы Origin != Invalid (S3). Имена суставов принимаются в трёх конвенциях: Kinect (ShoulderLeft), Nuitrack (LeftShoulder), канонические (shoulder_l).
- 22:38 написаны src/DanceLab.Core/Canonical/: Vec3.cs, CanonicalFrame.cs, CanonicalTrack.cs, TrialComparer.cs, TrialScorer.cs, Canonicalizer.cs; тесты tests/DanceLab.Core.Tests/CanonicalTests.cs (16 кейсов, лаг — теория на ±0.2). Запущены сборка и тесты.
- 22:45 первый прогон: 27/32 прошло, 5 упало. Причины: (1) ParseFrame искал lean/turn/tilt прямо в кадре, а не в подобъектах torso/head — исправлен CanonicalTrack (в реальном файле эталона они в torso/head); (2) два теста брали Track(1,60) (живой кадр движения) как «статичный» эталон — заменён на StaticTrack() из Tilted(0); (3) в тесте инвариантности ошибочно ждал heading π/2 после чистого переноса (heading там 0, перенос не вращает танцора) — ожидание исправлено. Повторный прогон: 32/32 прошло, 0 упало.
- Финальный полный прогон: dotnet build DanceLab.sln, dotnet test tests/DanceLab.Core.Tests — вывод в отчёт.

## Отчёт (2026-11-21 22:52)

### Сделал
- src/DanceLab.Core/Canonical/ (новый namespace DanceLab.Core.Canonical, стиль Types.cs/PoseReconstructor.cs, net48):
  - CanonicalFrame.cs — CanonicalFrame: 8 направлений костей по 3 числа (x бок влево, y вверх, z вперёд) + BoneNames/BoneCount, BoneValid (валидность = все суставы кости Origin != Invalid, canonical-pose S3), TorsoLeanRad, TorsoTurnRad, HeadTiltRad, RootForward/RootSide/RootHeight (доли длины ноги, S9), HeadingRad, TSec; Bone(name) для удобства.
  - CanonicalTrack.cs — Fps, LegLengthM, Frames, Sequence, Video; Load(path) canonical.json formatVersion 1 через System.Text.Json (как в Capture); Sample(tSec) — линейная интерполяция (кости перенормируются, валидность = И обеих границ; углы линейно; зажим на краях), бинарный поиск.
  - TrialComparer.cs — TrialComparison { BoneDeviationRad[8], MeanDeviationRad, Coverage } — сырые углы костей (без мёртвой зоны: иначе ErrorThreshold 0.8 недостижим при DeadZone 0.35/Saturation 1.0); Coverage = доля костей, валидных у обеих сторон.
  - TrialScorer.cs — TrialParams (дефолты по брифу), TrialScore { Running01, Total01, Errors, Coverage, ShiftSec }, TrialScorer: поиск лучшего сдвига сеткой 0.05 с в окне ±ShiftWindow на каждом Update (при равенстве остаётся текущий — нет дрейфа на статике), применённый сдвиг следует не быстрее ShiftRate; оценка = 1 − clamp(mean − DeadZone, 0, Saturation−DeadZone)/(Saturation−DeadZone); Running01 — среднее по записям за RunningWindow секунд, Total01 — по всем с начала; Errors +1 за момент с MeanDeviationRad > ErrorThreshold при Coverage > 0.5; записи с Coverage 0 не попадают в оценку и ошибки (trial S9 «не вижу»).
  - Canonicalizer.cs — ToCanonical(ReconstructedPose, boneLengths?, legLengthM): конвенции знак в знак с T1 (tools/aist_import/canonical_export.py): up = normalize(шея − корень) (fallback грудь), бок = линия бёдер к левому бедру (fallback линия плеч), вперёд = бок × вверх (правая тройка S7), координаты костей (бок, вверх, вперёд), heading = atan2(f.x, f.z). Имена суставов в трёх конвенциях: Kinect (ShoulderLeft…), Nuitrack (LeftShoulder…), канонические (shoulder_l…); chest = SpineMid/Torso/chest, neck/head напрямую. Корень: SpineBase/Waist, иначе середина бёдер; в долях длины ноги в осях heading (height над y=0; у ToCanonical нет параметра пола — поля корня в метрику не входят). legLengthM ≤ 0 → fallback на длины ног из boneLengths, иначе ArgumentException.
  - Vec3.cs — внутренний вектор.
- tests/DanceLab.Core.Tests/CanonicalTests.cs — 16 тестов (лаг — Theory на ±0.2): дефолты TrialParams; торс-фрейм и направления костей; lean π/4 и head tilt; инвариант поворота комнаты на 90° (кости равны, heading +π/2) и переноса (кости и heading равны, высота корня меняется); имена Nuitrack дают тот же фрейм; self-сравнение нулевое (S8); невалидная кисть → Coverage 7/8; пустая поза → Coverage 0; Load JSON + линейная интерполяция + зажим; отказ formatVersion 2; загрузка эталона T1 data/derived/trial/gLO_sBM_cAll_d14_mLO5_ch02.canonical.json (443 кадра, legLengthM 0.711, кости единичные 1e-4; пропуск, если файла нет); self-скоринг максимум без ошибок (S8); постоянный лаг ±0.2 внутри окна не занижает оценку и находится (S7); счётчик ошибок по порогу с защитой по покрытию; кадры с покрытием 0 не скорят и не ошибят; зеркальная подмена L/R ухудшает (0.785 рад против 0).
- Не тронуты: sensor_view, Capture, спеки; коммитов нет. git status: только новые файлы (см. выше).

### Прогнал
- dotnet build C:\work\dancelab\DanceLab.sln — «Сборка успешно завершена», Предупреждений: 0, Ошибок: 0 (9 проектов, вывод целиком в ответе агенту).
- dotnet test C:\work\dancelab\tests\DanceLab.Core.Tests — «Пройден!: не пройдено 0, пройдено 32, пропущено 0, всего 32».
- Первый прогон был 27/32 (5 упало: ParseFrame искал lean/turn/tilt не в том месте JSON, два теста использовали живой кадр как статичный эталон, ошибочное ожидание heading после переноса) — исправлено, финал 32/32.

### Не подтвердил / проблемы
- TorsoTurnRad (скрутка): формула моё решение (угол линии плеч-бёдер относительно направления взгляда в горизонтальной плоскости, atan2 по вертикальной оси). Тело шапки canonical_export.py за пределами конвенций не читал (разрешена была только шапка), точную формулу T1 не сверял; на метрику пробы и рендер по Contract (кости + heading) не влияет, но если T3/T4 захотят одинаковую скрутку у эталона и живого призрака — сверить знак/определение с canonical_export.py.
- RootForward/RootSide у ToCanonical — от сырой позиции корня в комнате (у ToCanonical нет параметра «первый кадр»/пола, она без состояния), а у импортёра T1 — относительно первого валидного кадра. На пробную метрику не влияет (сравнение только по костям); если поля корня понадобятся для шагов/прыжков, понадобится привязка к старту на уровне вызывающего кода.
- HeadTiltRad: угол направления голова−шея от оси вверх. У T1 медиана 0.577 рад с шумными ушами COCO-17; конвенция та же (угол от вверх), знак/определение совпадают по смыслу.
- head.tilt/turn/lean в Load требовательны к наличию (InvalidDataException, если нет) — совпадает с форматом, который пишет импортёр; root-поля необязательны (0 по умолчанию).

Статус: готово. Отчёт выдан 22:52.
