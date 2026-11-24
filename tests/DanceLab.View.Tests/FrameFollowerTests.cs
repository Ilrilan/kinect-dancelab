using System;
using System.Collections.Generic;
using DanceLab.View;
using Xunit;

namespace DanceLab.View.Tests
{
    /// <summary>
    /// Тесты следящего кадра (T22, аудит P2.12): FrameFollower — чистая математика авто-кадра,
    /// перенесённая дословно из tools/player_trial/MainWindow.cs. Сценарии (а)-(е) из брифа E25.
    /// Окно 640x360 и видео 640x360 дают s = 1 — формулы зума и панорамы читаются напрямую.
    /// </summary>
    public class FrameFollowerTests
    {
        private const double Eps = 1e-9;

        /// <summary>Один тик Update с окном 640x360, видео 640x360, слайдер 1.0 (без масштаба).</summary>
        private static (double Zoom, double PanX, double PanY, ScreenBox Box) Tick(
                FrameFollower f, double t, string camera = "c01", double slider = 1.0)
        {
            return f.Update(t, camera, 640, 360, 640, 360, slider);
        }

        /// <summary>Словарь с одной камерой; кадры [t, uMin, vMin, uMax, vMax], root = центр bbox.</summary>
        private static Dictionary<string, CamTrack> SingleCam(params double[][] frames)
        {
            var rows = new double[frames.Length][];
            for (var i = 0; i < frames.Length; i++)
            {
                var f = frames[i];
                rows[i] = new[] { f[0], (f[1] + f[3]) / 2, (f[2] + f[4]) / 2, f[1], f[2], f[3], f[4] };
            }

            return new Dictionary<string, CamTrack> { ["c01"] = new CamTrack(rows) };
        }

        /// <summary>Гоняет Update до сходимости сглаживания (один и тот же t).</summary>
        private static (double Zoom, double PanX, double PanY, ScreenBox Box) Converge(
                FrameFollower f, double t, string camera = "c01", double slider = 1.0, int ticks = 400)
        {
            var last = (Zoom: 0.0, PanX: 0.0, PanY: 0.0, Box: default(ScreenBox));
            for (var i = 0; i < ticks; i++)
            {
                last = Tick(f, t, camera, slider);
            }

            return last;
        }

        /// <summary>Цель авто-зума по формуле: min(winW/bw, winH/bh), bw/bh с паддингом 25%, низ +75.</summary>
        private static double ZoomTarget(double uMin, double vMin, double uMax, double vMax)
        {
            var bw = (uMax - uMin) * 1.25;
            var bh = (vMax + FrameFollower.ShoePadRows - vMin) * 1.25;
            return Math.Max(1.0, Math.Min(3.0, Math.Min(640.0 / bw, 360.0 / bh)));
        }

        [Fact]
        public void CenteredDancer_ZoomEqualsWindowOverPaddedSize()
        {
            // (а) танцор в центре (320, 180): зум = окно / паддингованный габарит;
            // вертикаль габаритнее: bh = (260+75-100)*1.25 = 293.75 -> 360/293.75
            var f = new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 }));
            var expected = ZoomTarget(250, 100, 390, 260);

            var r = Converge(f, 1.0);

            Assert.Equal(expected, r.Zoom, 9);
            Assert.InRange(r.Zoom, 1.0, 3.0); // цель в диапазоне клампов — кламп не мешает
            // центр bbox горизонтально в центре окна: панорама X = 0
            Assert.Equal(0.0, r.PanX, 9);
        }

        [Fact]
        public void DancerOffset_PanCentersByFormula()
        {
            // (б) танцор смещён влево (центр bbox u = 220 от 320): PanX центрует,
            // знак положительный (окно уезжает вправо к контенту), величина = z*s*(W/2 - cu), s = 1
            var f = new FrameFollower(SingleCam(new[] { 1.0, 170.0, 140.0, 270.0, 240.0 }));
            var z = ZoomTarget(170, 140, 270, 240);
            var expectedPanX = z * (640.0 / 2 - 220.0); // в пределах клампа панорамы

            var r = Converge(f, 1.0);

            Assert.Equal(z, r.Zoom, 9);
            Assert.True(r.PanX > 0, "танцор левее центра — PanX должен быть положительным");
            Assert.Equal(expectedPanX, r.PanX, 6);
        }

        [Fact]
        public void Clamps_ZoomBetween1And9_PanWithinContent()
        {
            // (в) крошечный танцор: цель авто-зума упирается во внутренний кламп 3, слайдер 3 ->
            // итоговый зум в пределе MaxTotalZoom = 9 (T24: 3.0 x 3.0; прежний предел был 3)
            var tiny = new FrameFollower(SingleCam(new[] { 1.0, 319.0, 179.0, 321.0, 181.0 }));
            var rt = Converge(tiny, 1.0, slider: 3.0);
            Assert.Equal(FrameFollower.MaxTotalZoom, rt.Zoom, 9);

            // тот же крошечный танцор со слайдером 1: внутренний кламп 1..3 проходит наружу как 3
            var rt1 = Converge(new FrameFollower(SingleCam(new[] { 1.0, 319.0, 179.0, 321.0, 181.0 })), 1.0);
            Assert.Equal(3.0, rt1.Zoom, 9);

            // гигантский танцор (шире и выше окна): цель ~0.54 -> кламп снизу 1
            var huge = new FrameFollower(SingleCam(new[] { 1.0, -50.0, -50.0, 690.0, 410.0 }));
            var rh = Converge(huge, 1.0);
            Assert.Equal(1.0, rh.Zoom, 9);

            // панорама не выходит за контент при любом раскладе: |Pan| <= maxPan(z)
            foreach (var r in new[] { rt, rt1, rh })
            {
                var maxPanX = Math.Max(0.0, (r.Zoom * 640.0 - 640.0) / 2.0);
                var maxPanY = Math.Max(0.0, (r.Zoom * 360.0 - 360.0) / 2.0);
                Assert.InRange(r.PanX, -maxPanX - Eps, maxPanX + Eps);
                Assert.InRange(r.PanY, -maxPanY - Eps, maxPanY + Eps);
            }
        }

        [Fact]
        public void Smoothing_MovesTowardTargetNoFasterThan02PerTick()
        {
            // (г) два Update подряд: шаг к цели ровно 0.2 оставшейся дистанции, без перелёта
            var f = new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 }));
            var target = ZoomTarget(250, 100, 390, 260);

            var r1 = Tick(f, 1.0);
            var r2 = Tick(f, 1.0);

            // старт сглаженного зума — fallback 1.6, после тика 1: 1.6 + 0.2*(цель - 1.6)
            Assert.Equal(1.6 + 0.2 * (target - 1.6), r1.Zoom, 12);
            Assert.Equal(r1.Zoom + 0.2 * (target - r1.Zoom), r2.Zoom, 12);
            // дистанция сокращается геометрически (x0.8), перелёта нет
            Assert.Equal(Math.Abs(target - 1.6) * 0.8, Math.Abs(target - r1.Zoom), 9);
            Assert.Equal(Math.Abs(target - r1.Zoom) * 0.8, Math.Abs(target - r2.Zoom), 9);
            Assert.True((target - r1.Zoom) * (target - r2.Zoom) > 0, "зум не должен перескакивать цель");

            // панорама: та же доля 0.2 за тик от нуля к цели
            var panTarget = target * (640.0 / 2 - 320.0); // центр bbox в центре -> цель 0
            Assert.Equal(0.0, panTarget, 12);
            Assert.Equal(0.0, r1.PanX, 12);
        }

        [Fact]
        public void Smoothing_PanStepsLimited()
        {
            // (г-панорама) смещённый танцор: панорама движется к цели шагами по 0.2, не быстрее;
            // цель панорамы на каждом тике считается от текущего (ещё не сошедшегося) z
            var f = new FrameFollower(SingleCam(new[] { 1.0, 170.0, 140.0, 270.0, 240.0 }));
            var zt = ZoomTarget(170, 140, 270, 240);
            var z1 = 1.6 + 0.2 * (zt - 1.6); // сглаженный зум после тика 1
            var z2 = z1 + 0.2 * (zt - z1);   // после тика 2

            var r1 = Tick(f, 1.0);
            var r2 = Tick(f, 1.0);

            var panTarget1 = z1 * (640.0 / 2 - 220.0);
            var panTarget2 = z2 * (640.0 / 2 - 220.0);
            Assert.Equal(0.2 * panTarget1, r1.PanX, 9);
            Assert.Equal(r1.PanX + 0.2 * (panTarget2 - r1.PanX), r2.PanX, 9);
            Assert.True(Math.Abs(r1.PanX) <= 0.2 * Math.Abs(panTarget1) + Eps, "шаг 1 не быстрее 0.2 дистанции");
            Assert.True(Math.Abs(r2.PanX - r1.PanX) <= 0.2 * Math.Abs(panTarget2 - r1.PanX) + Eps,
                    "шаг 2 не быстрее 0.2 оставшейся дистанции");
        }

        [Fact]
        public void MissingCamera_FallbackZoom16_PanXZero()
        {
            // (д) камеры нет в словаре (и null): зум = fallback 1.6 сразу (сглаживание стартует с него),
            // PanX = 0. PanY по дословной формуле прежнего кода: центр = центр кадра, но окно
            // сдвинуто вверх на HeadShiftFrac (7%) -> цель панорамы Y = +0.07*windowH = 25.2.
            var f = new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 }));

            var r1 = Tick(f, 1.0, camera: "c99");
            Assert.Equal(FrameFollower.AutoZoomFallback, r1.Zoom, 12);
            Assert.Equal(0.0, r1.PanX, 12);
            Assert.Equal(0.2 * (0.07 * 360.0), r1.PanY, 12); // шаг 1 к +25.2

            var rc = Converge(f, 1.0, camera: "c99");
            Assert.Equal(FrameFollower.AutoZoomFallback, rc.Zoom, 12);
            Assert.Equal(0.0, rc.PanX, 12);
            Assert.Equal(0.07 * 360.0, rc.PanY, 9);

            var rn = Tick(new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 })), 1.0, camera: null!);
            Assert.Equal(FrameFollower.AutoZoomFallback, rn.Zoom, 12);
            Assert.Equal(0.0, rn.PanX, 12);
        }

        [Fact]
        public void BboxWindow_MotionOutside07sWindowDoesNotAffect()
        {
            // (е) кадры t=0 (танцор в центре) и t=5 (танцор улетел вправо и маленький):
            // запрос t=0.5 видит только кадр t=0 (5 вне окна ±0.7) — результат совпадает с
            // фолловером на одном кадре t=0 бит-в-бит; запрос t=5.5 — только кадр t=5
            var both = new FrameFollower(SingleCam(
                new[] { 0.0, 250.0, 100.0, 390.0, 260.0 },
                new[] { 5.0, 500.0, 200.0, 520.0, 240.0 }));
            var onlyFirst = new FrameFollower(SingleCam(new[] { 0.0, 250.0, 100.0, 390.0, 260.0 }));
            var onlySecond = new FrameFollower(SingleCam(new[] { 5.0, 500.0, 200.0, 520.0, 240.0 }));

            var a = Converge(both, 0.5);
            var b = Converge(onlyFirst, 0.5);
            Assert.Equal(b.Zoom, a.Zoom, 12);
            Assert.Equal(b.PanX, a.PanX, 12);
            Assert.Equal(b.PanY, a.PanY, 12);

            // второй запрос — свежие фолловеры (старт сглаживания из NaN, как в живом окне):
            // траектории совпадают бит-в-бит, кадр t=5 на t=5.5 один в окне ±0.7
            var both2 = new FrameFollower(SingleCam(
                new[] { 0.0, 250.0, 100.0, 390.0, 260.0 },
                new[] { 5.0, 500.0, 200.0, 520.0, 240.0 }));
            var a2 = Converge(both2, 5.5);
            var c2 = Converge(onlySecond, 5.5);
            Assert.Equal(c2.Zoom, a2.Zoom, 12);
            Assert.Equal(c2.PanX, a2.PanX, 12);
            Assert.Equal(c2.PanY, a2.PanY, 12);

            // и результат на t=5.5 реально другой, чем на t=0.5 (движение учлось)
            Assert.NotEqual(a.Zoom, a2.Zoom, 9);
            Assert.NotEqual(a.PanX, a2.PanX, 9);
        }

        [Fact]
        public void CamTrack_BoxUnionInsideWindow()
        {
            // CamTrack напрямую: объединение bbox кадров в окне, пустое окно -> false
            var track = new CamTrack(new[]
            {
                new[] { 0.0, 320.0, 180.0, 100.0, 50.0, 200.0, 150.0 },
                new[] { 1.0, 320.0, 180.0, 300.0, 100.0, 500.0, 250.0 },
                new[] { 2.0, 320.0, 180.0, 20.0, 30.0, 600.0, 400.0 },
            });

            Assert.Equal(3, track.Count);

            // окно 1.0 с на t=1.0: [0.0 .. 2.0] включительно (условие <=) — все три кадра,
            // объединение (20, 30, 600, 400)
            Assert.True(track.TryGetBox(1.0, 1.0, out var box));
            Assert.Equal(20.0, box.UMin, 12);  // от кадра t=2 (на верхней границе окна)
            Assert.Equal(30.0, box.VMin, 12);  // от кадра t=2
            Assert.Equal(600.0, box.UMax, 12);
            Assert.Equal(400.0, box.VMax, 12);

            // узкое окно 0.1 с на t=1.0 содержит один кадр t=1.0: его bbox без соседей
            Assert.True(track.TryGetBox(1.0, 0.1, out var one));
            Assert.Equal(300.0, one.UMin, 12);
            Assert.Equal(100.0, one.VMin, 12);
            Assert.Equal(500.0, one.UMax, 12);
            Assert.Equal(250.0, one.VMax, 12);

            // окно между кадрами пусто
            Assert.False(track.TryGetBox(1.5, 0.2, out var none));
            Assert.False(new CamTrack(Array.Empty<double[]>()).TryGetBox(0.0, 0.7, out _));
        }

        [Fact]
        public void Reset_RestoresFallbackZoomAndZeroesPan()
        {
            // сброс (видео не готово): сглаженный зум = fallback*слайдер, панорама 0
            var f = new FrameFollower(SingleCam(new[] { 1.0, 170.0, 140.0, 270.0, 240.0 }));
            var converged = Converge(f, 1.0);

            f.Reset(2.0);
            var after = Tick(f, 1.0);

            // после Reset сглаженный зум = 1.6*2 = 3.2; тик с целью ~1.65 -> 3.2 + 0.2*(1.65-3.2)
            var target = ZoomTarget(170, 140, 270, 240);
            var zAfterReset = 3.2 + 0.2 * (target - 3.2);
            Assert.Equal(zAfterReset, after.Zoom, 9);
            // панорама после сброса стартует с нуля: один тик = 0.2 цели от зума этого тика (2.89)
            Assert.Equal(0.2 * (zAfterReset * (640.0 / 2 - 220.0)), after.PanX, 9);
            Assert.True(Math.Abs(after.PanX) < Math.Abs(converged.PanX) - Eps);
        }

        [Fact]
        public void CenteredDancer_BoxCentredInWindow_SizeIsPaddedBboxTimesScaleZoom()
        {
            // (T24) рамка танцора в пикселях окна: s = 1 (окно = источник 640x360), z сошёлся
            // к цели авто-зума. Танцор в центре по X и PanX = 0 -> CenterX = центр окна;
            // Width = ширина bbox * s * z, Height = (высота bbox + ShoePadRows) * s * z
            var f = new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 }));
            var z = ZoomTarget(250, 100, 390, 260);

            var r = Converge(f, 1.0);

            Assert.True(r.Box.IsValid);
            Assert.Equal(320.0, r.Box.CenterX, 9);
            Assert.Equal((390.0 - 250.0) * z, r.Box.Width, 9);
            Assert.Equal((260.0 + FrameFollower.ShoePadRows - 100.0) * z, r.Box.Height, 9);
            // (E34, S32) линия обуви: FeetY = Bottom − (ShoePadRows − FootRows)·s·z, здесь s = 1 (окно = источник 640x360)
            Assert.Equal(r.Box.Bottom - ((FrameFollower.ShoePadRows - FrameFollower.FootRows) * 1.0 * z), r.Box.FeetY, 9);
        }

        [Fact]
        public void MissingCamera_BoxIsInvalid()
        {
            // (T24) камеры/данных нет: рамка = default(ScreenBox), IsValid == false
            var f = new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 }));

            var r = Tick(f, 1.0, camera: "c99");

            Assert.False(r.Box.IsValid);
        }

        [Fact]
        public void LastTarget_WithBox_MatchesIndependentFormula()
        {
            // (E37/T34, S34) LastTarget при наличии рамки: три кадра в окне ±0.7 с — объединение
            // bbox тест считает независимо, без общего хелпера ZoomTarget; s = 1 (окно = источник
            // 640x360), паддинг 1.25 и ShoePadRows; MaxPan по формуле от возвращённого зума
            var f = new FrameFollower(SingleCam(
                new[] { 0.5, 250.0, 100.0, 390.0, 260.0 },
                new[] { 0.9, 230.0, 120.0, 410.0, 280.0 },
                new[] { 1.1, 260.0, 90.0, 400.0, 270.0 }));

            var r = Tick(f, 1.0);

            // независимый расчёт: окно ±0.7 с захватывает все три кадра, объединение bbox
            var uMin = Math.Min(250.0, Math.Min(230.0, 260.0));
            var vMin = Math.Min(100.0, Math.Min(120.0, 90.0));
            var uMax = Math.Max(390.0, Math.Max(410.0, 400.0));
            var vMax = Math.Max(260.0, Math.Max(280.0, 270.0));
            var bw = (uMax - uMin) * 1.25;
            var bh = (vMax + FrameFollower.ShoePadRows - vMin) * 1.25;
            var zAutoExpected = Math.Max(1.0, Math.Min(3.0, Math.Min(640.0 / bw, 360.0 / bh)));
            var maxPanXExpected = Math.Max(0.0, ((r.Zoom * 1.0 * 640.0) - 640.0) / 2.0);
            var maxPanYExpected = Math.Max(0.0, ((r.Zoom * 1.0 * 360.0) - 360.0) / 2.0);

            Assert.Equal(zAutoExpected, f.LastTarget.ZAutoTarget, 12);
            Assert.Equal(maxPanXExpected, f.LastTarget.MaxPanX, 12);
            Assert.Equal(maxPanYExpected, f.LastTarget.MaxPanY, 12);
            // цель не в клампе снизу и зум сошёлся выше 1: пределы панорамы не вырождены в 0
            Assert.True(zAutoExpected > 1.0 && f.LastTarget.MaxPanX > 0.0 && f.LastTarget.MaxPanY > 0.0);
        }

        [Fact]
        public void LastTarget_MissingCamera_ZAutoTargetIsFallback()
        {
            // (E37/T34, S34) запасной вид (камеры без данных): ZAutoTarget = AutoZoomFallback;
            // пределы панорамы — по формуле от возвращённого зума (1.6 при слайдере 1, s = 1)
            var f = new FrameFollower(SingleCam(new[] { 1.0, 250.0, 100.0, 390.0, 260.0 }));

            var r = Tick(f, 1.0, camera: "c99");

            Assert.Equal(FrameFollower.AutoZoomFallback, f.LastTarget.ZAutoTarget, 12);
            Assert.Equal(Math.Max(0.0, ((r.Zoom * 1.0 * 640.0) - 640.0) / 2.0), f.LastTarget.MaxPanX, 12);
            Assert.Equal(Math.Max(0.0, ((r.Zoom * 1.0 * 360.0) - 360.0) / 2.0), f.LastTarget.MaxPanY, 12);
        }

        [Fact]
        public void CenterTracksCurrentFrameNotUnion()
        {
            // (S42) bbox стоит на месте в кадрах 0.0 с и 0.4 с (u 250..390), в кадре 1.0 с
            // сдвинут вправо (u 450..590): цель центра панорамы — по рамке ТЕКУЩЕГО кадра
            // (запрос с окном 0 с), не по объединённой рамке окна ±0.7 с. Окно и видео
            // 1920x1080 (s = 1): при 640x360 танцор у правого края источника, панорамного
            // запаса окна не хватает, чтобы его центрировать. Мерой будет центр ТЕКУЩЕЙ
            // рамки (u = 520), спроецированный тем же отображением окна (масштаб z от
            // центра, затем панорама); возвращаемый ScreenBox — объединённая рамка.
            var f = new FrameFollower(SingleCam(
                new[] { 0.0, 250.0, 140.0, 390.0, 200.0 },
                new[] { 0.4, 250.0, 140.0, 390.0, 200.0 },
                new[] { 1.0, 450.0, 140.0, 590.0, 200.0 }));

            (double Zoom, double PanX, double PanY, ScreenBox Box) r = default;
            for (var i = 0; i < 60; i++)
            {
                r = f.Update(1.0, "c01", 1920, 1080, 1920, 1080, 1.0);
            }

            var curCenterOnScreen = 1920.0 / 2 + (520.0 - 1920.0 / 2) * r.Zoom + r.PanX;
            Assert.True(Math.Abs(curCenterOnScreen - 960.0) <= 6.0,
                    $"центр текущей рамки на экране {curCenterOnScreen:0.###} против центра окна 960");
        }

        [Fact]
        public void LagOnZoom3AtMostTwiceLagOnZoom1()
        {
            // (S42) равномерное движение вправо 6 пикселей за кадр (кадры каждые 1/60 с,
            // bbox 140 пикселей шириной): 90 тиков с шагом 1/30 с до одного момента t = 3.0
            // при слайдере 1 и при слайдере 3. Лаг = |центр рамки ScreenBox − центр окна| /
            // ширина окна; отношение (лаг при 3) / (лаг при 1) <= 2 — большой зум не должен
            // ухудшать ведение вдвое и больше. Окно и видео 1920x1080 (s = 1): прогон движения
            // 6 px/кадр за 3 с — 1080 пикселей, в источник 640 не помещается (танцор вылетает
            // за контент, панорама упирается в кламп и лаг перестаёт зависеть от gain);
            // вертикальный размах bbox подобран так, чтобы авто-зум был внутри клампов (~2.3),
            // а не прижат к 1 или 3.
            var frames = new double[240][];
            for (var i = 0; i < 240; i++)
            {
                frames[i] = new[] { i / 60.0, 150.0 + 6 * i, 150.0, 290.0 + 6 * i, 450.0 };
            }

            var lag1 = Lag(1.0, frames);
            var lag3 = Lag(3.0, frames);
            Assert.True(lag3 <= 2.0 * lag1,
                    $"лаг при слайдере 3 ({lag3:0.###} px) должен быть не больше двух лагов при слайдере 1 ({lag1:0.###} px)");
        }

        /// <summary>Лаг ведения: 90 тиков с шагом 1/30 с по треку, |центр ScreenBox − центр окна|, пиксели окна.</summary>
        private static double Lag(double slider, double[][] frames)
        {
            var f = new FrameFollower(SingleCam(frames));
            (double Zoom, double PanX, double PanY, ScreenBox Box) r = default;
            for (var j = 1; j <= 90; j++)
            {
                r = f.Update(j / 30.0, "c01", 1920, 1080, 1920, 1080, slider);
            }

            return Math.Abs(r.Box.CenterX - 1920.0 / 2);
        }

        /// <summary>
        /// Трек 200 кадров (T44/E56): времена как в cams.json — Math.Round(i/60.0, 4);
        /// bbox 250..390 x 140..200, с кадра 100 (t = 1.6667) сдвинут вправо на 200 px
        /// (450..590); root = центр bbox.
        /// </summary>
        private static CamTrack RoundedTimesTrack()
        {
            var rows = new double[200][];
            for (var i = 0; i < 200; i++)
            {
                var shift = i >= 100 ? 200.0 : 0.0;
                rows[i] = new[]
                {
                    Math.Round(i / 60.0, 4),
                    320.0 + shift, 170.0,
                    250.0 + shift, 140.0, 390.0 + shift, 200.0,
                };
            }

            return new CamTrack(rows);
        }

        [Fact]
        public void NearestFrameFoundWithRoundedTimes()
        {
            // (T44/E56) времена кадров округлены до 4 знаков (1.6667), тики таймера дают
            // числа вида 1.666666…: TryGetBox с окном 0 промахивается, TryGetNearest
            // находит ближайший кадр. С кадра 100 bbox сдвинут вправо на 200 px — по
            // рамке видно, какой кадр нашёлся.
            var track = RoundedTimesTrack();

            // воспроизводим проблему: точного кадра 1.6733 нет
            Assert.False(track.TryGetBox(1.6733, 0.0, out _));

            // t = 1.6733 = 1.6667 + 0.0066: ближайший — кадр 100, bbox уже сдвинут
            Assert.True(track.TryGetNearest(1.6733, out var tFrame, out var box));
            Assert.Equal(1.6667, tFrame, 12);
            Assert.Equal(450.0, box.UMin, 12);
            Assert.Equal(140.0, box.VMin, 12);
            Assert.Equal(590.0, box.UMax, 12);
            Assert.Equal(200.0, box.VMax, 12);

            // t = 1.6597 = 1.6667 − 0.007: сосед 1.65 дальше (0.0097 > 0.007) — снова кадр 100
            Assert.True(track.TryGetNearest(1.6597, out tFrame, out box));
            Assert.Equal(1.6667, tFrame, 12);

            // t = 1.68: |1.68−1.6833| = 0.0033 < |1.68−1.6667| = 0.0133 — ближайший кадр 101
            Assert.True(track.TryGetNearest(1.68, out tFrame, out box));
            Assert.Equal(1.6833, tFrame, 12);

            // t = 1.72 между кадрами 1.7167 и 1.7333: ближе 1.7167 (0.0033 против 0.0133)
            Assert.True(track.TryGetNearest(1.72, out tFrame, out box));
            Assert.Equal(1.7167, tFrame, 12);

            // за пределами трека дальше maxGapSec: последний кадр 3.3167, 5.0 дальше 0.05
            Assert.False(track.TryGetNearest(5.0, out _, out _));

            // пустой трек — false
            Assert.False(new CamTrack(Array.Empty<double[]>()).TryGetNearest(1.0, out _, out _));
        }

        [Fact]
        public void CenterTracksCurrentFrameOnRealisticTicks()
        {
            // (T44/E56) реалистичные тики по треку с округлёнными временами: 90 тиков
            // tt = max(0, 2.0 − 3 + k/30) (первые 30 зажаты в нуле, последний тик t = 2.0),
            // слайдер 3 — большой зум, где S42 важнее всего (при слайдере 1 шаг сглаживания
            // 0.2/тик за 11 тиков после скачка оставил бы ~17 px источника — порог 6 px
            // недостижим). С кадра 100 (t = 1.6667) танцор сдвинут вправо на 200 px; центр
            // ТЕКУЩЕГО кадра (ближайшего к 2.0, u = 520), спроецированный тем же отображением
            // окна (масштаб z от центра, затем панорама), остаётся у центра окна (допуск 6 px);
            // при откате к объединённой рамке (центр u = 420) он ушёл бы на (520−420)·z ≈ 900 px.
            var f = new FrameFollower(new Dictionary<string, CamTrack> { ["c01"] = RoundedTimesTrack() });

            (double Zoom, double PanX, double PanY, ScreenBox Box) r = default;
            for (var k = 1; k <= 90; k++)
            {
                r = f.Update(Math.Max(0, 2.0 - 3 + k / 30.0), "c01", 1920, 1080, 1920, 1080, 3.0);
            }

            var curCenterOnScreen = 1920.0 / 2 + (520.0 - 1920.0 / 2) * r.Zoom + r.PanX;
            Assert.True(Math.Abs(curCenterOnScreen - 960.0) <= 6.0,
                    $"центр текущего кадра на экране {curCenterOnScreen:0.###} против центра окна 960");
        }

        [Fact]
        public void NearestRootMatchesFrame()
        {
            // (T44/E56) TryGetRootNearest для t = 1.6733 возвращает root кадра 1.6667
            // (root = центр bbox, сдвинутого с кадра 100: (320+200, 170) = (520, 170))
            var track = RoundedTimesTrack();

            Assert.True(track.TryGetRootNearest(1.6733, out var root));
            Assert.Equal(520.0, root.U, 12);
            Assert.Equal(170.0, root.V, 12);

            // вне трека дальше 0.05 — false
            Assert.False(track.TryGetRootNearest(5.0, out _));

            // TryGetRoot (точное совпадение, отчёт тура) не тронут: 1.6733 промах,
            // 2.0 = 120/60 находит кадр 120 как раньше
            Assert.False(track.TryGetRoot(1.6733, out _));
            Assert.True(track.TryGetRoot(2.0, out var exact));
            Assert.Equal(520.0, exact.U, 12);
        }
    }
}
