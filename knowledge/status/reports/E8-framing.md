# E8: старт 2026-11-22 12:09

Задача: T11 «Регулятор кадра: укрупнение танцора» (S21, specs/player/trial.md).
Разрешённые файлы: tools/player_trial/MainWindow.cs, GhostCanvas.cs (+ Program.cs
для аргумента --zoom, прямо назван в брифе).

## План

1. Прочитать спеку S21/T11, MainWindow.cs, GhostCanvas.cs.
2. Слайдер «Кадр» 1.0..3.0 шаг 0.1, умолчание 1.6, подпись «x1.6».
3. Видео: ScaleTransform на MediaElement, центр — центр кадра, обрезка в videoPanel.
4. GhostCanvas.FrameZoom: масштаб сцены * FrameZoom, центр — горизонталь канвы
   на высоте центра тела; подпись панели не зумится.
5. Единое управление: один слайдер ведёт оба.
6. --zoom в Program.cs для скриншотов с зумом 1.0.
7. Верификация: build, тесты, снимки e8-default и e8-zoom100.

## Прогресс

- 12:09 прочитана спека (S21, T11), инвентарь reports.
- 12:12 прочитаны MainWindow.cs, GhostCanvas.cs, Program.cs. Из E7-contrast:
  PlayerTrial в солюшене маппится в Debug|x64 → запускать bin/x64/Debug/net48/player_trial.exe.
- Решение по центру зума призраков: масштаб s умножается на FrameZoom,
  неподвижная точка (cx, cy) — проекция центра тела (SceneCenterYm 0.9 м) при
  базовом масштабе; пол/сетка/стрелка уходят за край согласованно, подпись
  панели фиксирована. ClipToBounds у Grid по умолчанию false — выставляю явно
  на videoPanel.
- 12:20 правки внесены: GhostCanvas.FrameZoom (1.0..3.0, clamp, InvalidateVisual),
  OnRender — baseScale/cy от базового масштаба, scale = baseScale * FrameZoom;
  MainWindow — слайдер «Кадр» 1.0..3.0 шаг 0.1 умолчание 1.6 рядом со «Скорость»,
  videoZoom ScaleTransform (центр 0.5/0.5), videoPanel.ClipToBounds = true,
  OnZoomChanged ведёт видео и призраков, opts.Zoom применяется в конструкторе;
  Program.cs — --zoom 1.0..3.0 (вне диапазона игнор).
- 12:15 dotnet build DanceLab.sln: 0 предупреждений / 0 ошибок.
- 12:15 dotnet test tests/DanceLab.Core.Tests: 33/33.
- 12:16 снимки финальным exe: trial-shot-e8-default.png (1.6) и
  trial-shot-e8-zoom100.png (--zoom 1), оба exit 0. zoom100 совпадает с
  trial-shot-e7-front.png (FrameZoom 1.0 — тождество). Убрано мёртвое поле
  frameZoom (писалось, не читалось), пересобрано 0/0, тесты 33/33, снимки
  пересняты финальным exe.

## Замечания для ПМ

- При 1.6 стопни призрака и линия пола чуть ниже нижней кромки (якорь — центр
  тела по брифу); голова видна целиком. Если владельцу нужны стопни на 1.6 —
  якорь надо переносить, это правка брифа.
- --zoom не прописан в Contract specs/player/trial.md (список аргументов) —
  спеку правит ПМ.
- Живой прогон с сенсором не входил в разрешённую брифом верификацию (--no-sensor).
