using System;
using Xunit;

namespace DanceLab.Capture.Tests
{
    public class StreamStatsTests
    {
        [Fact]
        public void SteadyThirtyFpsGivesThirtyAndZeroJitter()
        {
            var s = new StreamStats();
            for (var i = 0; i < 100; i++)
            {
                s.Add(i * 33333L);
            }

            Assert.InRange(s.Fps, 29.9, 30.1);
            Assert.InRange(s.JitterMs, 0, 0.001);
            Assert.InRange(s.MaxIntervalMs, 33.3, 33.4);
            Assert.Equal(100, s.Total);
        }

        [Fact]
        public void AlternatingIntervalsGiveJitterAndMax()
        {
            var s = new StreamStats(window: 20);
            long t = 0;
            for (var i = 0; i < 40; i++)
            {
                t += i % 2 == 0 ? 33000 : 67000;
                s.Add(t);
            }

            Assert.InRange(s.MeanIntervalMs, 49.0, 51.0);
            Assert.InRange(s.JitterMs, 16.0, 18.0);
            Assert.InRange(s.MaxIntervalMs, 66.9, 67.1);
        }

        [Fact]
        public void FewFramesGiveZeros()
        {
            var s = new StreamStats();
            Assert.Equal(0, s.Fps);
            s.Add(0);
            Assert.Equal(0, s.Fps);
            Assert.Equal(0, s.JitterMs);
        }

        [Fact]
        public void ResetClears()
        {
            var s = new StreamStats();
            s.Add(0);
            s.Add(33000);
            s.Reset();
            Assert.Equal(0, s.Total);
            Assert.Equal(0, s.Fps);
        }

        [Fact]
        public void TooSmallWindowIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StreamStats(2));
        }

        [Fact]
        public void FloorGeometryFromMeasuredKinectPlane()
        {
            var g = FloorGeometry.FromPlane(new[] { 0.03, 0.98, 0.19, -0.615 })!.Value;
            Assert.InRange(g.HeightM, 0.59, 0.62);
            Assert.InRange(g.PitchDeg, 10.0, 12.0);
            Assert.InRange(g.RollDeg, 1.5, 2.0);
        }

        [Fact]
        public void LevelSensorHasZeroTilt()
        {
            var g = FloorGeometry.FromPlane(new[] { 0.0, 1.0, 0.0, -1.5 })!.Value;
            Assert.Equal(1.5, g.HeightM, 6);
            Assert.Equal(0, g.PitchDeg, 6);
            Assert.Equal(0, g.RollDeg, 6);
        }

        [Fact]
        public void DegeneratePlaneReturnsNull()
        {
            Assert.Null(FloorGeometry.FromPlane(new[] { 0.0, 0.0, 0.0, 1.0 }));
        }

        [Fact]
        public void PointHeightAboveFloor()
        {
            var plane = new[] { 0.0, 1.0, 0.0, 1.5 };
            Assert.Equal(1.7, FloorGeometry.HeightAbove(plane, 0.3, 0.2, 2.0), 6);
        }
    }
}
