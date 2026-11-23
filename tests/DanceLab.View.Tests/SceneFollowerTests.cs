using DanceLab.View;
using Xunit;

namespace DanceLab.View.Tests
{
    /// <summary>
    /// Тесты следящего кадра 3D-сцены (T24, S26): SceneFollower повторяет рамку танцора
    /// (масштаб = высота рамки / рост призрака, пол = низ рамки, ось корня = центр рамки)
    /// или прежний широкий вид без данных; цель камеры снапится к корню на первом тике
    /// и дальше движется шагами ровно 0.2 оставшейся дистанции; Reset снапит снова.
    /// </summary>
    public class SceneFollowerTests
    {
        [Fact]
        public void WithBox_ScaleFloorCenterFollowBoxFormulas()
        {
            var f = new SceneFollower();

            var fr = f.Update(hasBox: true, boxHeightPx: 250.0, boxBottomPx: 340.0, boxCenterXpx: 300.0,
                    ghostHeightM: 1.78, panelWpx: 640, panelHpx: 360, sliderZoom: 1.0,
                    rootXm: 0.5, rootZm: 2.0);

            Assert.Equal(250.0 / 1.78, fr.ScalePxPerM, 12);
            Assert.Equal(340.0, fr.FloorScreenY, 12);
            Assert.Equal(300.0, fr.CenterScreenX, 12);
            // первый вызов после конструктора: цель камеры снапится к корню призрака
            Assert.Equal(0.5, fr.TargetXm, 12);
            Assert.Equal(2.0, fr.TargetZm, 12);
        }

        [Fact]
        public void WithoutBox_FallbackWideViewFollowsFormula()
        {
            var f = new SceneFollower();

            var fr1 = f.Update(hasBox: false, boxHeightPx: 0, boxBottomPx: 0, boxCenterXpx: 0,
                    ghostHeightM: 0, panelWpx: 640, panelHpx: 360, sliderZoom: 1.0,
                    rootXm: 3.0, rootZm: -1.0);
            var fr2 = f.Update(hasBox: false, boxHeightPx: 0, boxBottomPx: 0, boxCenterXpx: 0,
                    ghostHeightM: 0, panelWpx: 640, panelHpx: 360, sliderZoom: 2.0,
                    rootXm: 3.0, rootZm: -1.0);

            var expectedBase = 360.0 / SceneFollower.FallbackFieldHeightM * FrameFollower.AutoZoomFallback;
            Assert.Equal(expectedBase * 1.0, fr1.ScalePxPerM, 12);
            Assert.Equal(expectedBase * 2.0, fr2.ScalePxPerM, 12);
            Assert.Equal(0.86 * 360.0, fr1.FloorScreenY, 12);
            Assert.Equal(0.86 * 360.0, fr2.FloorScreenY, 12);
            Assert.Equal(320.0, fr1.CenterScreenX, 12);
            Assert.Equal(320.0, fr2.CenterScreenX, 12);
        }

        [Fact]
        public void TargetSmoothing_FirstCallSnaps_StepIs02_ResetSnapsAgain()
        {
            var f = new SceneFollower();
            var fr1 = f.Update(true, 250.0, 340.0, 300.0, 1.78, 640, 360, 1.0, 1.0, 0.0);
            Assert.Equal(1.0, fr1.TargetXm, 12);
            Assert.Equal(0.0, fr1.TargetZm, 12);

            // второй тик: цель движется ровно на 0.2 разницы между корнем и целью
            var fr2 = f.Update(true, 250.0, 340.0, 300.0, 1.78, 640, 360, 1.0, 2.0, 1.0);
            Assert.Equal(1.0 + 0.2 * (2.0 - 1.0), fr2.TargetXm, 12);
            Assert.Equal(0.0 + 0.2 * (1.0 - 0.0), fr2.TargetZm, 12);

            // Reset: следующий тик снова снапится к корню
            f.Reset();
            var fr3 = f.Update(true, 250.0, 340.0, 300.0, 1.78, 640, 360, 1.0, -3.0, 4.0);
            Assert.Equal(-3.0, fr3.TargetXm, 12);
            Assert.Equal(4.0, fr3.TargetZm, 12);
        }
    }
}
