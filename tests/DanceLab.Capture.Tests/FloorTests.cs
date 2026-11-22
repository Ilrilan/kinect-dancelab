using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace DanceLab.Capture.Tests
{
    /// <summary>Пол живой сцены (trial T17): FloorY из плоскости Kinect, выбор источника, сериализация в записи.</summary>
    public class FloorTests
    {
        private static RunHeader Header() => new RunHeader(
            "kinect-sdk-2",
            "2.0",
            null,
            new SensorInfo("sensor-1", null, new FrameSize(512, 424, 30, "Depth16"), null),
            new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero),
            "static-front");

        private static SkeletonFrame FrameWithFloor(double? floorY, bool withScene = true)
        {
            var joints = new List<RawJoint> { new RawJoint("Head", 0, 1.7, 2.5, 0.9, "tracked") };
            var scene = withScene ? new SceneInfo(new double[] { 0, 0.98, 0.17, -1.2 }) : null;
            return new SkeletonFrame(33333L, 637000000000000000L, new[] { new TrackedUser(7, joints) }, scene, floorY);
        }

        private static string WriteOne(SkeletonFrame f)
        {
            var ms = new MemoryStream();
            using (var w = new SkeletonRecordWriter(ms, Header(), leaveOpen: true))
            {
                w.Write(f);
                w.Complete();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static JsonSchema LoadSchema()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DanceLab.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return JsonSchema.FromText(File.ReadAllText(Path.Combine(dir!.FullName, "specs", "capture", "schemas", "capture-record.schema.json")));
        }

        [Fact]
        public void FloorYFromPlaneComputesHeightOnYAxis()
        {
            // уровень: нормаль (0,1,0), пол на 1.2 м ниже нуля сенсора (система Kinect: Y вверх)
            Assert.Equal(1.2, SceneInfo.FloorYFromPlane(new double[] { 0, 1, 0, -1.2 }));
            // наклон: высота в точке x=z=0 всё равно -d/b
            Assert.Equal(1.2 / 0.9962, SceneInfo.FloorYFromPlane(new double[] { 0, 0.9962, 0.0872, -1.2 })!.Value);
            // пол выше нуля сенсора тоже валиден (отрицательное d положительным знаком)
            Assert.Equal(-1.4, SceneInfo.FloorYFromPlane(new double[] { 0, -1, 0, -1.4 }));
            Assert.Null(SceneInfo.FloorYFromPlane(null));
            Assert.Null(SceneInfo.FloorYFromPlane(new double[] { 1, 0, 0, 0.5 }));
            Assert.Null(SceneInfo.FloorYFromPlane(new double[] { 0.1, 0.2, 0.3 }));
        }

        [Fact]
        public void FloorYFromPlaneReturnsNullForUnmeasuredPlane()
        {
            // Kinect SDK: FloorClipPlane (0,0,0,0), когда пол не измерен
            Assert.Null(SceneInfo.FloorYFromPlane(new double[] { 0, 0, 0, 0 }));
        }

        [Fact]
        public void PickFloorSourcePrefersTrackerFloorOverAnkles()
        {
            Assert.Equal(FloorSource.Tracker, SkeletonFrame.PickFloorSource(1.2, anklesReady: true));
            Assert.Equal(FloorSource.Tracker, SkeletonFrame.PickFloorSource(-1.4, anklesReady: true));
            Assert.Equal(FloorSource.Tracker, SkeletonFrame.PickFloorSource(0.05, anklesReady: false));
        }

        [Fact]
        public void PickFloorSourceFallsBackToAnklesWhenFloorYInvalid()
        {
            // Nuitrack и старые записи дают null
            Assert.Equal(FloorSource.Ankles, SkeletonFrame.PickFloorSource(null, anklesReady: true));
            Assert.Equal(FloorSource.Estimating, SkeletonFrame.PickFloorSource(null, anklesReady: false));
            // вне разумного диапазона (|y| >= 3 м) и NaN — как «нет данных»
            Assert.Equal(FloorSource.Ankles, SkeletonFrame.PickFloorSource(5.0, anklesReady: true));
            Assert.Equal(FloorSource.Ankles, SkeletonFrame.PickFloorSource(double.NaN, anklesReady: true));
            Assert.Equal(FloorSource.Estimating, SkeletonFrame.PickFloorSource(double.PositiveInfinity, anklesReady: false));
        }

        [Fact]
        public void RecordWritesFloorYIntoSceneOtherAndReadsItBack()
        {
            var text = WriteOne(FrameWithFloor(1.15));
            var frameLine = text.Split('\n').First(l => l.Contains("\"frame\""));
            var json = JsonNode.Parse(frameLine)!;

            // поле верхнего уровня запрещено схемой, едет в scene.other
            Assert.Null(json["floorY"]);
            Assert.Equal(1.15, json["scene"]!["other"]!["floorY"]!.GetValue<double>());

            var result = LoadSchema().Evaluate(JsonNode.Parse(frameLine), new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.True(result.IsValid, frameLine);

            using var r = new SkeletonRecordReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
            var frames = r.ReadFrames().ToList();
            Assert.Single(frames);
            Assert.Equal(1.15, frames[0].FloorY);
        }

        [Fact]
        public void RecordWithoutFloorYKeepsSceneWithoutFloorField()
        {
            // формат старых записей: floorY нигде нет, читается как null
            var text = WriteOne(FrameWithFloor(null));
            Assert.DoesNotContain("floorY", text);

            using var r = new SkeletonRecordReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
            Assert.Null(r.ReadFrames().Single().FloorY);
        }

        [Fact]
        public void RecordWithFloorYButNoSceneStillRoundTrips()
        {
            // FloorY без Scene: писатель создаёт scene.other только с floorY, схема это допускает
            var text = WriteOne(FrameWithFloor(0.9, withScene: false));
            var frameLine = text.Split('\n').First(l => l.Contains("\"frame\""));
            Assert.NotNull(JsonNode.Parse(frameLine)!["scene"]!["other"]!["floorY"]);

            var result = LoadSchema().Evaluate(JsonNode.Parse(frameLine), new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.True(result.IsValid, frameLine);

            using var r = new SkeletonRecordReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
            Assert.Equal(0.9, r.ReadFrames().Single().FloorY);
        }

        [Fact]
        public void LegacyRecordLinesWithoutFloorYReadAsNull()
        {
            // две строки: «старая» без floorY и новая с ним — читатель различает
            var ms = new MemoryStream();
            using (var w = new SkeletonRecordWriter(ms, Header(), leaveOpen: true))
            {
                w.Write(FrameWithFloor(null));
                w.Write(FrameWithFloor(1.05));
                w.Complete();
            }

            using var r = new SkeletonRecordReader(new MemoryStream(ms.ToArray()));
            var frames = r.ReadFrames().ToList();
            Assert.Equal(2, frames.Count);
            Assert.Null(frames[0].FloorY);
            Assert.Equal(1.05, frames[1].FloorY);
            Assert.Equal(new double[] { 0, 0.98, 0.17, -1.2 }, frames[1].Scene!.FloorPlane);
        }
    }
}
