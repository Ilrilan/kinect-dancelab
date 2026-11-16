using System;
using System.IO;
using System.Linq;
using DanceLab.Capture.Kinect;
using DanceLab.Capture.Nuitrack;
using Xunit;

namespace DanceLab.Capture.Tests
{
    /// <summary>
    /// Проверки на живом сенсоре. По умолчанию пропускаются (возвращаются сразу).
    /// Включение: переменная среды DANCELAB_SENSOR=kinect или nuitrack. Прогон короткий (3 секунды), сенсор не должен греться.
    /// </summary>
    public class SensorIntegrationTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "capture-live-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        private static bool Enabled(string name) => Environment.GetEnvironmentVariable("DANCELAB_SENSOR") == name;

        private CaptureOptions Options(Func<string>? version = null, Func<string?>? engine = null) => new CaptureOptions
        {
            Scenario = "live-test",
            RunDirectory = dir,
            DurationSeconds = 3,
            Images = true,
            CountdownSeconds = 0,
            VersionProvider = version,
            EngineProvider = engine,
        };

        [Fact]
        public void KinectDeliversSkeletonDepthAndColorFramesAtThirtyFps()
        {
            if (!Enabled("kinect"))
            {
                return;
            }

            using var source = new KinectPoseSource("live-test");
            var result = CaptureSession.Run(source, source, Options(() => source.Version));
            Assert.Equal(CaptureExitCode.Ok, result.Code);
            Assert.InRange(result.SkeletonFrames, 70, 110);
            Assert.InRange(result.DepthFrames, 70, 110);
            Assert.InRange(result.ColorFrames, 30, 110);
            using var reader = new SkeletonRecordReader(File.OpenRead(Path.Combine(dir, "skeleton.jsonl")));
            Assert.Equal("kinect-sdk-2", reader.Header.TrackerId);
            Assert.Equal(512, reader.Header.Sensor.Depth!.Width);
            var frames = reader.ReadFrames().ToList();
            var dt = frames.Zip(frames.Skip(1), (a, b) => (b.DeviceTimeUs - a.DeviceTimeUs) / 1000.0).ToList();
            Assert.InRange(dt.Average(), 30.0, 37.0);
            Assert.Contains(frames, f => f.Scene?.FloorPlane != null);
        }

        [Fact]
        public void NuitrackDeliversFramesWhenLicensed()
        {
            if (!Enabled("nuitrack"))
            {
                return;
            }

            using var source = new NuitrackPoseSource("live-test", "CNN_HPE");
            var result = CaptureSession.Run(source, source, Options(() => source.Version, () => source.Engine));
            Assert.True(source.LastError == null, source.LastError);
            Assert.Equal(CaptureExitCode.Ok, result.Code);
            Assert.True(result.DepthFrames > 30);
        }
    }
}
