using System;
using System.Collections.Generic;

namespace DanceLab.Capture.Tests
{
    /// <summary>Детерминированный источник кадров для проверки записи без сенсора.</summary>
    public sealed class FakePoseSource : IPoseSource, IRawImageSource
    {
        public FakePoseSource(SensorInfo sensor)
        {
            Sensor = sensor;
        }

        public string TrackerId => "kinect-sdk-2";

        public SensorInfo Sensor { get; }

        public bool IsAvailable { get; set; } = true;

        public event Action<SkeletonFrame>? FrameReceived;

        public event Action<ImageFrame>? DepthReceived;

        public event Action<ImageFrame>? ColorReceived;

        public static SkeletonFrame MakeSkeleton(int i, bool withScene = true)
        {
            var joints = new List<RawJoint>
            {
                new RawJoint("Head", 0.1 * i, 1.7, 2.5, 0.9, "tracked"),
                new RawJoint("WristLeft", -0.3, 1.2 + (0.001 * i), 2.4, null, "inferred"),
                new RawJoint("AnkleRight", 0.2, 0.1, 2.6, 0.5, null),
            };
            var scene = withScene ? new SceneInfo(new double[] { 0, 0.98, 0.17, -1.2 }, new Dictionary<string, object> { { "tilt", 10.5 }, { "src", "fake" } }) : null;
            return new SkeletonFrame(33333L * i, 637000000000000000L + (333333L * i), new[] { new TrackedUser(7, joints) }, scene);
        }

        public static ImageFrame MakeImage(int i, int w, int h, string format)
        {
            var data = new byte[w * h * MemoryPreflight.BytesPerPixel(format)];
            for (var k = 0; k < data.Length; k++)
            {
                data[k] = (byte)((k + i) & 0xFF);
            }

            return new ImageFrame(33333L * i, 637000000000000000L + (333333L * i), w, h, format, data);
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void StartImages()
        {
        }

        public void StopImages()
        {
        }

        public void Dispose()
        {
        }

        public void PushSkeleton(int count, bool withScene = true)
        {
            for (var i = 0; i < count; i++)
            {
                FrameReceived?.Invoke(MakeSkeleton(i, withScene));
            }
        }

        public void PushImages(int count, int w, int h)
        {
            for (var i = 0; i < count; i++)
            {
                DepthReceived?.Invoke(MakeImage(i, w, h, "Depth16"));
                ColorReceived?.Invoke(MakeImage(i, w, h, "Bgra"));
            }
        }
    }
}
