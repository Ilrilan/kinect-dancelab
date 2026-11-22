---
name: dev-environment-pitfalls
description: "Ловушки окружения сборки DanceLab: dotnet test --no-build берёт устаревший binDebug вместо bindDebug; player_trial.exe снимать только из bin/x64/Debug/net48"
metadata:
  node_type: memory
  type: project
  modified: 2026-11-22
---

Решение DanceLab.sln маппирует проекты в bin/x64/Debug/net48, но dotnet test --no-build может запустить устаревший DLL из bin/Debug/net48.

**Why:** дважды ловили: E11 отчитался «33/33» от старого бинарника (новые тесты не исполнялись), E7 снял скриншоты старым exe. Счёт тестов или снимок без проверки пути бинарника — не доказательство.

**How to apply:** в брифах исполнителям и при приёмке: полный dotnet test без --no-build — тогда DLL пересобирается из текущих исходников и путь (bin/Debug у проектного запуска) не важен; путь критичен только при --no-build и при запуске exe: player_trial запускать только из bin/x64/Debug/net48 (уточнение по вердикту критика E12, 2026-11-22). См. [[general-working-rules]].
