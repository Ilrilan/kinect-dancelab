# E12 — Дебаунс счётчика грубых ошибок и порог покрытия скоринга

- Задача: T14 из specs/player/trial.md; P0.3+P0.4 из knowledge/zettel/release-player-audit.md
- Исполнитель: E12 (implementer), бюджет 40 мин
- Файлы: src/DanceLab.Core/Canonical/TrialScorer.cs, tests/DanceLab.Core.Tests/CanonicalTests.cs
- Статус: в работе

## План
1. Прочитать спеку (T14), аудит (P0.3, P0.4), TrialScorer.cs, CanonicalTests.cs.
2. P0.3: Errors растёт на 1 только при переходе в состояние ошибки (mean > ErrorThreshold && Coverage > MinCoverage), не на каждом кадре.
3. P0.4: TrialParams.MinCoverageBones = 4 (int); оценка и ошибки только при валидных костях >= MinCoverageBones; Coverage отдаётся фактический.
4. Обновить doc-комментарии (Errors = эпизоды).
5. Тесты: обновить существующий тест счётчика; добавить (а) 60 плохих кадров = 1 ошибка; (б) 3 кости — нет оценки/ошибок; (в) 4 кости — считается.
6. dotnet build DanceLab.sln; dotnet test tests/DanceLab.Core.Tests (полный, без --no-build).

## Лог
- [старт] читаю входные документы.
- Прочитаны: specs/player/trial.md (T14), release-player-audit.md (P0.3, P0.4), TrialScorer.cs, CanonicalTests.cs, dev-environment-pitfalls.md.
- Проверено использование TrialScorer/TrialParams вне Core (tools/player_trial) — новый параметр с умолчанием ничего не ломает.
- Правка TrialScorer.cs: параметр MinCoverageBones=4, гейт покрытия (число сравнённых костей >= MinCoverageBones), дебаунс ошибок по эпизодам (поле errorEpisode), doc-комментарии.
- Правка CanonicalTests.cs: дефолт MinCoverageBones в TrialParamsHaveBriefDefaults; ErrorCounterTriggersAboveThresholdAndGuardsByCoverage переписан на эпизоды (1 -> хорошо -> 2 -> низкое покрытие закрывает эпизод -> 3); новые SixtyBadFramesCountAsOneError, ThreeValidBonesBelowMinCoverageGiveNoScoreOrErrors, FourValidBonesAtMinCoverageScoreAndCountErrors.
- Запускаю dotnet build DanceLab.sln, затем полный dotnet test tests/DanceLab.Core.Tests без --no-build.

## Результат
- dotnet build DanceLab.sln: успешно, 0 предупреждений / 0 ошибок.
- dotnet test tests/DanceLab.Core.Tests (полный прогон, DLL пересобрана в самом прогоне): пройдено 38, не пройдено 0, всего 38.
- Фильтрованный прогон четырёх новых/обновлённых тестов по именам: пройдено 4 / 4.
- Статус: выполнено, отчёт отправлен ПМ. Не коммитил (по брифу).
