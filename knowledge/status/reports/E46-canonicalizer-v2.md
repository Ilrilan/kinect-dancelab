# E46: Canonicalizer, формат версии 2 (T40)

Статус: завершена (2026-11-24)

План:
- [x] HeadingUnwrapper.cs: свойства Current, LastRaw
- [x] Canonicalizer.cs: lean/roll/basis/F/L, передача в CanonicalFrame, XML-комментарий
- [x] Тесты tests/DanceLab.Core.Tests/CanonicalizerTests.cs
- [x] dotnet build DanceLab.sln (0 warn / 0 err)
- [x] dotnet test tests/DanceLab.Core.Tests (число до/после)

Прогресс:
- прочитаны спека (S39, формат версии 2), Canonicalizer/CanonicalFrame/HeadingUnwrapper/Vec3/TorsoFrame, тесты CanonicalTests и PoseReconstructorTests.
- HeadingUnwrapper.cs: добавлены свойства Current и LastRaw (обновление в Update, сброс в Reset).
- Canonicalizer.cs: сигнатура с HeadingUnwrapper? heading = null; развёрнутый heading (fh/w/raw, raw=LastRaw при w<=1e-9, при upOk==false unwrapper не подкармливается), F=(sin h,0,cos h), L=Y×F, lean/roll по S39, basis девять чисел (null при upOk==false), корень в осях F/L, передача roll/basis в CanonicalFrame, XML-комментарий дополнен.
- Создан tests/DanceLab.Core.Tests/CanonicalizerTests.cs: 4 теста (оси торса 1e-9, roll 30°/lean 0, heading без unwrapper = raw 1e-12, heading с unwrapper держится через сбой 180°).
- Базовый прогон до правок: 55 тестов, 53 зелёные, 2 красные (CanonicalTests: TrackSampleInterpolatesLinearly и ReferenceCanonicalJsonFromImporterLoads — тогда была в полёте чужая правка T38, CanonicalTrack.Load уже требовал версию 2, фикстуры ещё версии 1).
- dotnet build DanceLab.sln: 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.Core.Tests: 64/64 зелёные (мои 4 прошли; 5 новых тестов T38 в CanonicalTests и исправленные им фикстуры закрыли прежние 2 красных).
- Точечный прогон CanonicalizerTests: 4/4 зелёные.
