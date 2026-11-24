using System;
using DanceLab.Core.Canonical;
using Xunit;

namespace DanceLab.Core.Tests
{
    /// <summary>Разворот направления взгляда по треку, кадр за кадром (specs/player/trial.md, S39).</summary>
    public class HeadingUnwrapperTests
    {
        [Fact]
        public void ConstantRawWithFullReliabilityReturnsRaw()
        {
            var u = new HeadingUnwrapper();
            Assert.Equal(0.3, u.Update(0.3, 1.0), 9);
            Assert.Equal(0.3, u.Update(0.3, 1.0), 9);
            Assert.Equal(0.3, u.Update(0.3, 1.0), 9);
        }

        /// <summary>Полный оборот тела даёт прирост 2π: ряд монотонно растёт и не сворачивается в ±π.</summary>
        [Fact]
        public void FullTurnUnwrapsMonotonicallyToFourHundredDegrees()
        {
            var u = new HeadingUnwrapper();
            var step = 10.0 * Math.PI / 180.0;
            var prev = 0.0;
            for (var i = 1; i <= 40; i++)
            {
                var h = u.Update(i * step, 1.0);
                Assert.True(h > prev, "кадр " + i + ": результат должен монотонно расти, " + h + " <= " + prev);
                prev = h;
            }

            Assert.Equal(400.0 * Math.PI / 180.0, prev, 9);
        }

        /// <summary>Сбойный кадр (переворот на 180° на один кадр) гасится сменой ветви и не двигает направление.</summary>
        [Fact]
        public void SingleFrameFlipGlitchDoesNotMoveHeading()
        {
            var u = new HeadingUnwrapper();
            var raws = new[] { 0.3, 0.3, 0.3 + Math.PI, 0.3, 0.3 };
            foreach (var raw in raws)
            {
                Assert.Equal(0.3, u.Update(raw, 1.0), 9);
            }
        }

        /// <summary>Низкая надёжность глушит шум: чередование 0 и π при w = 0 почти не двигает результат.</summary>
        [Fact]
        public void ZeroReliabilityMutesAlternatingNoise()
        {
            var u = new HeadingUnwrapper();
            var first = u.Update(0.0, 0.0);
            for (var i = 1; i < 10; i++)
            {
                var h = u.Update(i % 2 == 1 ? Math.PI : 0.0, 0.0);
                Assert.True(Math.Abs(h - first) <= 0.35, "кадр " + i + ": отклонение " + Math.Abs(h - first));
                Assert.True(Math.Abs(h - first) < Math.PI / 2.0, "кадр " + i + ": скачок на 180 градусов");
            }
        }

        /// <summary>При w >= ReliabilityFloor коэффициент k = 1: надёжный кадр переносится целиком.</summary>
        [Fact]
        public void GainIsOneAtOrAboveReliabilityFloor()
        {
            var u = new HeadingUnwrapper();
            u.Reset();
            u.Update(0.0, 0.5);
            Assert.Equal(0.5, u.Update(0.5, 0.5), 9);
        }

        [Fact]
        public void ResetReturnsToInitialState()
        {
            var u = new HeadingUnwrapper();
            for (var i = 1; i <= 20; i++)
            {
                u.Update(i * 0.1, 1.0);
            }

            u.Reset();
            Assert.Equal(0.7, u.Update(0.7, 0.5), 9);
        }
    }
}
