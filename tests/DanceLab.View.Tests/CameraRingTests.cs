using System;
using DanceLab.View;
using Xunit;

namespace DanceLab.View.Tests
{
    /// <summary>
    /// Тесты раскладки кольца камер (T24, S24): девять кнопок на эллипсе целиком внутри
    /// холста (запас от краёв около 2 px), без перекрытий и касаний (зазор не меньше 2 px),
    /// c05 — самая нижняя кнопка, c01 — самая высокая; камеры вне 1..9 — исключение.
    /// </summary>
    public class CameraRingTests
    {
        /// <summary>Требуемый запас от краёв холста, px.</summary>
        private const double Margin = 2.0;

        [Fact]
        public void AllButtons_InsideCanvasWithMargin()
        {
            for (var camera = 1; camera <= 9; camera++)
            {
                var (left, top) = CameraRing.Layout(camera);
                Assert.InRange(left, Margin, CameraRing.CanvasW - Margin - CameraRing.ButtonW);
                Assert.InRange(top, Margin, CameraRing.CanvasH - Margin - CameraRing.ButtonH);
            }
        }

        [Fact]
        public void Buttons_PairwiseGapAtLeast2px_NoOverlapsOrTouching()
        {
            var rects = new (double Left, double Top)[9];
            for (var camera = 1; camera <= 9; camera++)
            {
                rects[camera - 1] = CameraRing.Layout(camera);
            }

            for (var a = 0; a < 9; a++)
            {
                for (var b = a + 1; b < 9; b++)
                {
                    // зазор между прямоугольниками по каждой оси (отрицательный = перекрытие)
                    var gapX = Math.Max(rects[a].Left - (rects[b].Left + CameraRing.ButtonW),
                            rects[b].Left - (rects[a].Left + CameraRing.ButtonW));
                    var gapY = Math.Max(rects[a].Top - (rects[b].Top + CameraRing.ButtonH),
                            rects[b].Top - (rects[a].Top + CameraRing.ButtonH));
                    Assert.True(Math.Max(gapX, gapY) >= Margin,
                            $"кнопки c0{a + 1} и c0{b + 1} перекрываются или касаются: gapX={gapX}, gapY={gapY}");
                }
            }
        }

        [Fact]
        public void C05_IsLowest_C01_IsHighest()
        {
            var tops = new double[9];
            for (var camera = 1; camera <= 9; camera++)
            {
                tops[camera - 1] = CameraRing.Layout(camera).Top;
            }

            for (var camera = 1; camera <= 9; camera++)
            {
                Assert.True(tops[4] >= tops[camera - 1] - 1e-9, "c05 должна быть самой нижней кнопкой кольца");
                Assert.True(tops[0] <= tops[camera - 1] + 1e-9, "c01 должна быть самой высокой кнопкой кольца");
            }
        }

        [Fact]
        public void Layout_ThrowsForCameraOutside1To9()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraRing.Layout(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraRing.Layout(10));
        }
    }
}
