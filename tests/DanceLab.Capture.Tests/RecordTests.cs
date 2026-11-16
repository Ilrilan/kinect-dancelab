using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using Json.Schema;
using Xunit;

namespace DanceLab.Capture.Tests
{
    public class RecordTests
    {
        private static RunHeader Header() => new RunHeader(
            "nuitrack",
            "2026.1",
            "CNN_HPE",
            new SensorInfo("sensor-1", "ABC123", new FrameSize(512, 424, 30, "Depth16"), new FrameSize(1920, 1080, 30, "Bgra")),
            new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero),
            "static-front",
            "проба");

        private static string RepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DanceLab.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, relative);
        }

        private static JsonSchema LoadSchema() =>
            JsonSchema.FromText(File.ReadAllText(RepoFile(Path.Combine("specs", "capture", "schemas", "capture-record.schema.json"))));

        private static byte[] WriteRecord(int frames, int capacity = 4096, bool withScene = true)
        {
            var ms = new MemoryStream();
            using (var w = new SkeletonRecordWriter(ms, Header(), capacity, leaveOpen: true))
            {
                for (var i = 0; i < frames; i++)
                {
                    w.Write(FakePoseSource.MakeSkeleton(i, withScene));
                }

                w.Complete();
                Assert.Equal(frames, w.FramesWritten);
            }

            return ms.ToArray();
        }

        [Fact]
        public void RoundTripKeepsEveryField()
        {
            var bytes = WriteRecord(50);
            using var r = new SkeletonRecordReader(new MemoryStream(bytes));
            Assert.Equal("nuitrack", r.Header.TrackerId);
            Assert.Equal("CNN_HPE", r.Header.Engine);
            Assert.Equal("sensor-1", r.Header.Sensor.Id);
            Assert.Equal(512, r.Header.Sensor.Depth!.Width);
            Assert.Equal(1080, r.Header.Sensor.Color!.Height);
            Assert.Equal("static-front", r.Header.Scenario);
            Assert.Equal("проба", r.Header.Note);
            var frames = r.ReadFrames().ToList();
            Assert.Equal(50, frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                var expected = FakePoseSource.MakeSkeleton(i);
                var actual = frames[i];
                Assert.Equal(expected.DeviceTimeUs, actual.DeviceTimeUs);
                Assert.Equal(expected.HostTimeUtcTicks, actual.HostTimeUtcTicks);
                Assert.Single(actual.Users);
                Assert.Equal(7, actual.Users[0].Id);
                Assert.Equal(3, actual.Users[0].Joints.Count);
                var a = actual.Users[0].Joints;
                var e = expected.Users[0].Joints;
                for (var j = 0; j < 3; j++)
                {
                    Assert.Equal(e[j].Name, a[j].Name);
                    Assert.Equal(e[j].X, a[j].X);
                    Assert.Equal(e[j].Y, a[j].Y);
                    Assert.Equal(e[j].Z, a[j].Z);
                    Assert.Equal(e[j].Conf, a[j].Conf);
                    Assert.Equal(e[j].State, a[j].State);
                }

                Assert.Equal(expected.Scene!.FloorPlane, actual.Scene!.FloorPlane);
                Assert.Equal(10.5, actual.Scene.Other!["tilt"]);
                Assert.Equal("fake", actual.Scene.Other["src"]);
            }
        }

        [Fact]
        public void FrameNumbersAreSequentialAndFileIsJsonLines()
        {
            var text = Encoding.UTF8.GetString(WriteRecord(20));
            Assert.NotEqual((char)0xFEFF, text[0]);
            Assert.DoesNotContain("\r", text);
            var lines = text.Split('\n').Where(l => l.Length > 0).ToArray();
            Assert.Equal(21, lines.Length);
            for (var i = 1; i < lines.Length; i++)
            {
                Assert.Equal(i - 1, JsonNode.Parse(lines[i])!["n"]!.GetValue<int>());
            }
        }

        [Fact]
        public void EveryLineMatchesSchema()
        {
            var schema = LoadSchema();
            var text = Encoding.UTF8.GetString(WriteRecord(10));
            var count = 0;
            foreach (var line in text.Split('\n').Where(l => l.Length > 0))
            {
                var result = schema.Evaluate(JsonNode.Parse(line), new EvaluationOptions { OutputFormat = OutputFormat.List });
                Assert.True(result.IsValid, line);
                count++;
            }

            Assert.Equal(11, count);
        }

        [Fact]
        public void FrameWithoutSceneOmitsSceneAndStillMatchesSchema()
        {
            var schema = LoadSchema();
            var text = Encoding.UTF8.GetString(WriteRecord(3, withScene: false));
            foreach (var line in text.Split('\n').Where(l => l.Length > 0))
            {
                Assert.True(schema.Evaluate(JsonNode.Parse(line), new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid, line);
            }

            Assert.DoesNotContain("\"scene\"", text);
        }

        [Fact]
        public void SchemaRejectsFrameWithoutTimestamp()
        {
            var schema = LoadSchema();
            var bad = "{\"kind\":\"frame\",\"n\":0,\"users\":[]}";
            Assert.False(schema.Evaluate(JsonNode.Parse(bad), new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        }

        [Fact]
        public void WriterDoesNotBlockSourceAndLosesNoFrames()
        {
            var sensor = new SensorInfo("s", null, null, null);
            var source = new FakePoseSource(sensor);
            var ms = new MemoryStream();
            using var w = new SkeletonRecordWriter(ms, Header(), capacity: 20000, leaveOpen: true);
            source.FrameReceived += w.Write;
            var sw = Stopwatch.StartNew();
            source.PushSkeleton(10000);
            sw.Stop();
            w.Complete();
            Assert.Equal(10000, w.FramesWritten);
            ms.Position = 0;
            using var r = new SkeletonRecordReader(ms);
            Assert.Equal(10000, r.ReadFrames().Count());
            Assert.True(sw.ElapsedMilliseconds < 5000, "источник ждал запись: " + sw.ElapsedMilliseconds + " мс");
        }

        private sealed class SlowStream : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                Thread.Sleep(50);
                base.Write(buffer, offset, count);
            }
        }

        [Fact]
        public void OverflowStopsRecordingWithErrorInsteadOfSilentlyDroppingFrames()
        {
            var sw = Stopwatch.StartNew();
            var slow = new SlowStream();
            var w = new SkeletonRecordWriter(slow, Header(), capacity: 8, leaveOpen: true);
            for (var i = 0; i < 200; i++)
            {
                w.Write(FakePoseSource.MakeSkeleton(i));
            }

            Assert.True(sw.ElapsedMilliseconds < 2000, "Write заблокировал источник");
            Assert.Throws<CaptureBufferOverflowException>(() => w.Complete());
            w.Dispose();
        }
    }
}
