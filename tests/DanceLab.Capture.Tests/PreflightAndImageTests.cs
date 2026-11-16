using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DanceLab.Capture.Tests
{
    public class PreflightAndImageTests
    {
        [Theory]
        [InlineData("Depth16", 2)]
        [InlineData("Yuy2", 2)]
        [InlineData("Rgb24", 3)]
        [InlineData("Bgra", 4)]
        public void BytesPerPixel(string format, int expected) => Assert.Equal(expected, MemoryPreflight.BytesPerPixel(format));

        [Fact]
        public void UnknownFormatIsRejected() => Assert.Throws<ArgumentException>(() => MemoryPreflight.BytesPerPixel("weird"));

        [Fact]
        public void EstimateForTenSecondsOfKinectDepthAndColor()
        {
            var depth = MemoryPreflight.FrameBytes(new FrameSize(512, 424, 30, "Depth16"));
            var color = MemoryPreflight.FrameBytes(new FrameSize(1920, 1080, 30, "Bgra"));
            Assert.Equal(512L * 424 * 2, depth);
            Assert.Equal(1920L * 1080 * 4, color);
            var need = MemoryPreflight.EstimateBytes(300, depth, 300, color);
            Assert.InRange(need, 2_500_000_000L, 2_800_000_000L);
        }

        [Fact]
        public void CheckFailsWhenFreeMemoryBelowNeedTimesFactorAndReportsShortfall()
        {
            var r = MemoryPreflight.Check(needBytes: 1000, freeBytes: 1400);
            Assert.False(r.Ok);
            Assert.Equal(100, r.ToFreeBytes);
            var ok = MemoryPreflight.Check(1000, 1500);
            Assert.True(ok.Ok);
            Assert.Equal(0, ok.ToFreeBytes);
        }

        [Fact]
        public void FreePhysicalMemoryIsPositive() => Assert.True(MemoryPreflight.GetFreePhysicalBytes() > 0);

        [Fact]
        public void RecorderIgnoresFramesBeyondCapacityAndSavesFiles()
        {
            var rec = new ImageRunRecorder(depthCapacity: 5, colorCapacity: 3);
            for (var i = 0; i < 8; i++)
            {
                rec.AddDepth(FakePoseSource.MakeImage(i, 4, 2, "Depth16"));
                rec.AddColor(FakePoseSource.MakeImage(i, 4, 2, "Bgra"));
            }

            Assert.Equal(5, rec.DepthCount);
            Assert.Equal(3, rec.ColorCount);
            Assert.True(rec.IsFull);

            var dir = Path.Combine(Path.GetTempPath(), "capture-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var header = new RunHeader("kinect-sdk-2", "2.0", null, new SensorInfo("s", null, null, null), DateTimeOffset.UtcNow, "depthcolor");
                rec.Save(dir, header);
                Assert.Equal(5 * 4 * 2 * 2, new FileInfo(Path.Combine(dir, "depth.bin")).Length);
                Assert.Equal(3 * 4 * 2 * 4, new FileInfo(Path.Combine(dir, "color.bin")).Length);
                Assert.Equal(5, File.ReadAllLines(Path.Combine(dir, "depth.times.jsonl")).Length);
                Assert.Equal(3, File.ReadAllLines(Path.Combine(dir, "color.times.jsonl")).Length);
                var run = File.ReadAllText(Path.Combine(dir, "run.json"));
                Assert.Contains("\"frames\"", run);
                Assert.Contains("\"count\": 5", run);
                var first = File.ReadAllBytes(Path.Combine(dir, "depth.bin")).Take(16).ToArray();
                Assert.Equal(FakePoseSource.MakeImage(0, 4, 2, "Depth16").Data, first);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}
