# T29 — отчёт критика (второй контур)

Дата: 2026-11-23. Проверка чек-листа ПМ по плееру tools/player_trial.

## Часть 1. Проверка по коду (независимое чтение файлов и grep)

1: подтверждено — FrameFollower.cs:92 `MaxTotalZoom = 9.0`; :168 `var z = Math.Max(1.0, Math.Min(MaxTotalZoom, smoothedZoomAuto * sliderZoom))`; внутренний кламп не тронут: :144 `zAutoTarget = Math.Max(1.0, Math.Min(3.0, ...))`.
2: подтверждено — FrameFollower.cs:124 сигнатура `(double Zoom, double PanX, double PanY, ScreenBox Box)`; :185-194: Left/Top/Right от UMin/VMin/UMax с `s` (:130), `z` (:168) и smoothedPanX/PanY (:177-178), Bottom от `vMaxPad = box.VMax + ShoePadRows` (:185), FeetY = `bottom - ((ShoePadRows - FootRows) * s * z)` (:188); :198 `screenBox = default` при hasBox=false. Те же z/pan окно применяет к видео (MainWindow.cs:927-930).
3: подтверждено — FrameFollower.cs:90 `public const double FootRows = 30.0`.
4: подтверждено — ScreenBox.cs:9 `public readonly struct ScreenBox`; поля Left/Top/Right/Bottom/FeetY/IsValid (:12-27); Width (:40), Height (:43), CenterX (:46); IsValid=true ставится только конструктором (:36), значит default(ScreenBox).IsValid == false.
5: подтверждено — SceneFollower.cs:47 условие `hasBox && ghostHeightM > 0.1 && boxHeightPx > 0`; :50-52 ScalePxPerM = boxHeightPx/ghostHeightM, FloorScreenY = boxBottomPx, CenterScreenX = boxCenterXpx; :57-59 fallback `panelHpx / FallbackFieldHeightM(=3.2, :18) * FrameFollower.AutoZoomFallback * sliderZoom`, `0.86 * panelHpx`, `panelWpx / 2.0`; цель камеры: снап при !hasTarget (:62-67; Reset() :28-31 сбрасывает), дальше `+= FollowerGain(=0.2, :21) * (root - target)` (:70-71).
6: подтверждено — CameraRing.cs:14-15 CanvasW 160, CanvasH 120, ButtonW 30, ButtonH 24, StepDeg 40, CenterX = CanvasW/2 (=80), CenterY 60, RadiusX 63.5, RadiusY 46; :20 `return 90 + (-1) * ((camera - 5) * StepDeg)`; :31-34 Layout возвращает (cx - ButtonW/2, cy - ButtonH/2) от точки на эллипсе.
7: подтверждено — `grep -rn 'FrameZoom|baseScale|FloorAnchorAboveFloorFrac' tools/player_trial/` → 0 вхождений (grep exit=1).
8: подтверждено — GhostCanvas.cs:37 `HeightM => Thigh + Shin + TorsoLen + NeckToHead + (2 * HeadR)`.
9: подтверждено — GhostCanvas.cs:294 сигнатура; :296-299 `lastRoot = RootOf(reference, lengths)` при reference != null; :301 `sceneFollower.Update(hasBox, heightPx, bottomPx, centerXpx, lengths.HeightM, panelW, panelH, sliderZoom, lastRoot.X, lastRoot.Z)`; :302 `framed = true`; :303 `InvalidateVisual()`.
10: подтверждено — Project: GhostCanvas.cs:404-405 `x -= targetXm; z -= targetZm;` до поворота (:406-411); DepthFactor: :418-419 до глубины (:420-423); OnRender: :358-361 `if (!framed) SetVideoBox(false, 0, 0, 0, w, h, 1.0)`; :375-379 scale с защитой NaN/Infinity/<=0 → `h / 3.2`; :381 cx; :382 `cy = sceneFrame.FloorScreenY - (SceneCenterYm * scale)`.
11: подтверждено — GhostCanvas.cs:441 `FloorLine(dc, targetXm - 4, 0, targetXm + 4, 0, FloorPen, cx, cy, scale)`.
12: подтверждено — Solve (GhostCanvas.cs:501-537) построчно совпадает с `git show HEAD:tools/player_trial/GhostCanvas.cs` (фрейм :392-395 ↔ :506-510; Bone(i) :438-442 ↔ :512-516; root: HEAD `fwdH*RootForward*leg + leftH*RootSide*leg + (0, RootHeight*leg, 0)` ↔ `RootOf(...) + (0, (RootHeight + rootShiftU)*leg, 0)` с RootOf = :458-464; hip..headC :444-457 ↔ :520-533); в DrawGhost векторы фрейма не используются напрямую (grep fwdH/leftH/side/fwdT — только RootOf :461-463 и Solve :506-535), всё через `j.*` (:549-704).
13: подтверждено — GhostCanvas.cs:319-349: :326 `GhostLengths.FromLeg(1.0)`; :330-335 пропуск кадра без `BoneValid[4] && BoneValid[6]` / `BoneValid[5] && BoneValid[7]`; :338-339 `low = min/один ankle.Y ...; lows.Add(low - (0.12 * unit.Shin))`; :347-348 сортировка и `lows[(int)Math.Floor(0.10 * (lows.Count - 1))]`; :321-324 track == null → 0; :342-345 пустой список → 0.
14: подтверждено — GhostCanvas.cs:387 эталон `..., scale, -groundShiftU)`; :392 пользователь `..., scale, 0.0)`.
15: подтверждено — GhostCanvas.cs:658-662 capK=0.25, capRx/capRy = rx/ry * 1.06, хорда `headP.Y - capK*capRy`; :686-694 купол — залитая дуга `dc.DrawGeometry(Fade(skin.Cap, 1.0), null, crown)` (без пера); :663-664 tilt 22°, `fv = (j.FwdH * cos(tilt)) - ((0,1,0) * sin(tilt))`; :665-668 база ±`0.85 * HeadR` на `+0.2 * HeadR`, кончики `fv * (CapBrimForwardFrac * HeadR)` и ±`0.55 * HeadR`; :671 заливка `Fade(skin.Brim, 1.0)`; порядок :674-684 по brimAhead; старая обводка удалена: в `git show HEAD:...GhostCanvas.cs` (:547-556) купол рисался `DrawGeometry(null, new Pen(Fade(skin.Cap...), ...), dome)` по полному эллипсу — в текущем файле этого нет.
16: подтверждено — MainWindow.cs:318-319 `Width = Math.Min(1320, workArea.Width)`, `Height = Math.Min(780, workArea.Height)`; :320-321 MaxWidth/MaxHeight от workArea; :322 `WindowStartupLocation = WindowStartupLocation.CenterScreen`.
17: подтверждено — MainWindow.cs:327 `Resources.Add(typeof(Button), CreateDarkButtonStyle())`; триггеры шаблона: :575-577 hover Opacity 0.12 на "hover", :579-581 pressed 0.24 на "hover", :583-585 disabled 0.4 на "bg" — Foreground и Background ни один не трогает; :549 `FocusVisualStyleProperty = null`.
18: подтверждено — MainWindow.cs:332-333 `Resources.Add(typeof(ComboBox)...)` и `typeof(ComboBoxItem)`; части в XAML: templateRoot :617, toggleButton :622, contentPresenter :649, PART_Popup :657, dropDownBorder :660.
19: подтверждено — MainWindow.cs:98 `angleRing` = Canvas шириной CameraRing.CanvasW, высотой CameraRing.CanvasH; кнопки :1570-1576 Width/Height = CameraRing.ButtonW/ButtonH, FontSize = 11, позиция `CameraRing.Layout(i)` (:1567); StyleRingButton при создании :1577 и в SwitchAngle :1638-1641; :1591-1592 выбранная — RingActive + RingActiveText, :43 RingActiveText = 0x10,0x11,0x14; YawRad (:1596-1600) и CameraYawSign = -1.0 (:97) совпадают с HEAD (HEAD MainWindow.cs:89, :1343-1347).
20: подтверждено — MainWindow.cs:85 countdownBox Height 40, FontSize 18, VerticalAlignment Center; :80-82 playButton/restartButton/attemptButton FontSize 20.
21: подтверждено — MainWindow.cs:166 Margin (12,0,12,0), MaxWidth 640, TextTrimming; SetStatus :730-734 ставит Text и ToolTip; `grep -n 'status\.Text' MainWindow.cs` → единственное вхождение :732 внутри SetStatus.
22: подтверждено — MainWindow.cs:1508-1532: без оценки «Трек закончен» (:1513), «без оценки» FontSize 40 (:1514-1515), подсказка «"Ещё раз" или Space: попытка заново.» (:1516-1517); с оценкой «Трек пройден» (:1521), FontSize 72 (:1522), процент (:1524), грубые (:1526), покрытие (:1527), длительность (:1528); :1531 finalCard Visible в обоих; заголовок — поле finalTitle (:143, :1513, :1521); MediaEnded вызывает безусловно: :439-445.
23: подтверждено — MainWindow.cs:1148-1153: ветка `opts.NoSensor || session == null` пишет только «без сенсора» + время, строки пола нет; пол присутствует в ветках :1160-1161 («не вижу»), :1176 (пауза), :1188 (бегущий).
24: подтверждено — MainWindow.cs:57-68: FontSize 15, FontWeight Normal, LineHeight 24, BlockLineHeight, Padding (14,10,14,12), Margin (10,0,10,10), Background 0x1b,0x1c,0x22.
25: подтверждено — MainWindow.cs:1950-1953 (StartAsync) и :793-798 (ApplyStartup, ветка --no-sensor): обе через `mode == AppMode.Scene ? "Сцена сенсора: ..." : "Идёт проба: ..."`.
26: подтверждено — MainWindow.cs:920 (ранняя ветка) и :940 (обычная) обе вызывают ghost.SetVideoBox; :938-940 передаются `frame.Box.Height * ky`, `frame.Box.FeetY * ky`, `frame.Box.CenterX * kx` при `kx = vw > 0 ? gw / vw : 1.0`, `ky = vh > 0 ? gh / vh : 1.0`; OnZoomChanged :1680-1695 — только zoomValue.Text, ghost не упоминается.
27: подтверждено — MainWindow.cs:742 `track = CanonicalTrack.Load(canonicalPath);` → :747 `ghost.SetGroundShift(GhostCanvas.ComputeGroundShift(track));`.
28: подтверждено — SceneCanvas.cs:124 правая подпись `(w - ft.Width - 12, 10)`, :130 левая `(12, 10)`; :286 пилюля `Rect(x-8, y-4, ft.Width+16, ft.Height+8), 4, 4` с альфой 0xB0 (:26); вид сверху: :193-198 метки «0 м … 5 м» на x=10; вид спереди: :162-176 метки 0..3 м 

## Часть 2. Независимые прогоны (из корня репозитория, без --no-build)

- dotnet build DanceLab.sln — 0 ошибок, 0 предупреждений, exit 0.
- dotnet test tests/DanceLab.View.Tests — пройдено 21/21, exit 0.
- dotnet test tests/DanceLab.Core.Tests — пройдено 38/38, exit 0.
- dotnet test tests/DanceLab.Capture.Tests — пройдено 45/45, exit 0.
- tools/player_trial/bin/x64/Debug/net48/player_trial.exe --no-sensor --shot data/preview/trial-shot-critic-t29.png --shot-after 4 — exit 0, PNG создан (318674 байт).
- tools/player_trial/bin/x64/Debug/net48/player_trial.exe --no-sensor --shot data/preview/trial-shot-critic-t29-end.png --shot-after 9 — exit 0, PNG создан (324813 байт).
- tools/player_trial/bin/x64/Debug/net48/player_trial.exe --no-sensor --shot data/preview/trial-shot-critic-t29-zoom3.png --shot-after 4 --zoom 3.0 — exit 0, PNG создан (399150 байт).

Полный прогон: dotnet build DanceLab.sln; dotnet test View/Core/Capture; 3 запуска player_trial со снимками — build PASS (0/0), тесты 21/21 + 38/38 + 45/45, все запуски exit 0.

Итог: 28/28 пунктов чек-листа подтверждено, расхождений с кодом нет. Вне чек-листа замечаний нет.
