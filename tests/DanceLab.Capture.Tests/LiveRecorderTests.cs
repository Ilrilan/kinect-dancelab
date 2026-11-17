using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DanceLab.Capture.Tests
{
    public class LiveRecorderTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "live-recorder-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        private static SensorInfo Sensor() => new SensorInfo("s1", "SN", new FrameSize(8, 4, 30, "Depth16"), null);

        [Fact]
        public void RecordsOnlyBetweenStartAndStop()
        {
            var src = new FakePoseSource(Sensor());
            src.PushSkeleton(3);
            var rec = LiveRecorder.Start(src, dir, "live", "t1", "engine-x");
            src.PushSkeleton(5);
            var n = rec.Stop();
            src.PushSkeleton(4);
            Assert.Equal(5, n);
            using var reader = new SkeletonRecordReader(File.OpenRead(Path.Combine(dir, "skeleton.jsonl")));
            Assert.Equal("live", reader.Header.Scenario);
            Assert.Equal("engine-x", reader.Header.Engine);
            Assert.Equal(5, reader.ReadFrames().Count());
        }

        [Fact]
        public void StopTwiceIsHarmless()
        {
            var src = new FakePoseSource(Sensor());
            var rec = LiveRecorder.Start(src, dir, "live", "t1", null);
            src.PushSkeleton(2);
            Assert.Equal(2, rec.Stop());
            Assert.Equal(2, rec.Stop());
            rec.Dispose();
        }

        [Fact]
        public void SourceKeepsRunningAfterRecordingStops()
        {
            var src = new FakePoseSource(Sensor());
            var seen = 0;
            src.FrameReceived += _ => seen++;
            var rec = LiveRecorder.Start(src, dir, "live", "t1", null);
            src.PushSkeleton(2);
            rec.Stop();
            src.PushSkeleton(3);
            Assert.Equal(5, seen);
            Assert.True(src.IsAvailable);
        }
    }
}
