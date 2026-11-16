using System;
using System.Collections.Generic;
using Microsoft.Kinect;

namespace DanceLab.Capture.Kinect
{
    /// <summary>
    /// Источник на Kinect SDK 2.0. Скелет: 25 суставов в метрах, система координат сенсора, состояние
    /// tracked, inferred или not-tracked, плоскость пола из BodyFrame. Глубина uint16 миллиметры,
    /// цвет в сыром формате YUY2 (без преобразования).
    /// </summary>
    public sealed class KinectPoseSource : IPoseSource, IRawImageSource
    {
        public const string Id = "kinect-sdk-2";
        private readonly KinectSensor sensor;
        private readonly string sensorId;
        private readonly object sync = new object();
        private BodyFrameReader? bodyReader;
        private DepthFrameReader? depthReader;
        private ColorFrameReader? colorReader;
        private Body[]? bodies;
        private ushort[]? depthBuffer;
        private int opened;
        private bool disposed;

        public KinectPoseSource(string sensorId)
        {
            this.sensorId = sensorId;
            var s = KinectSensor.GetDefault();
            sensor = s ?? throw new InvalidOperationException("Kinect не найден (KinectSensor.GetDefault вернул null).");
            var depth = sensor.DepthFrameSource.FrameDescription;
            var color = sensor.ColorFrameSource.FrameDescription;
            Sensor = new SensorInfo(
                sensorId,
                null,
                new FrameSize(depth.Width, depth.Height, 30, "Depth16"),
                new FrameSize(color.Width, color.Height, 30, "Yuy2"));
        }

        public string TrackerId => Id;

        public SensorInfo Sensor { get; private set; }

        public string Version => typeof(KinectSensor).Assembly.GetName().Version?.ToString() ?? "unknown";

        public event Action<SkeletonFrame>? FrameReceived;

        public event Action<ImageFrame>? DepthReceived;

        public event Action<ImageFrame>? ColorReceived;

        public void Start()
        {
            OpenSensor();
            lock (sync)
            {
                if (bodyReader != null)
                {
                    return;
                }

                bodyReader = sensor.BodyFrameSource.OpenReader();
                bodyReader.FrameArrived += OnBodyFrame;
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (bodyReader != null)
                {
                    bodyReader.FrameArrived -= OnBodyFrame;
                    bodyReader.Dispose();
                    bodyReader = null;
                }
            }

            CloseSensorIfIdle();
        }

        public void StartImages()
        {
            OpenSensor();
            lock (sync)
            {
                if (depthReader == null)
                {
                    depthReader = sensor.DepthFrameSource.OpenReader();
                    depthReader.FrameArrived += OnDepthFrame;
                }

                if (colorReader == null)
                {
                    colorReader = sensor.ColorFrameSource.OpenReader();
                    colorReader.FrameArrived += OnColorFrame;
                }
            }
        }

        public void StopImages()
        {
            lock (sync)
            {
                if (depthReader != null)
                {
                    depthReader.FrameArrived -= OnDepthFrame;
                    depthReader.Dispose();
                    depthReader = null;
                }

                if (colorReader != null)
                {
                    colorReader.FrameArrived -= OnColorFrame;
                    colorReader.Dispose();
                    colorReader = null;
                }
            }

            CloseSensorIfIdle();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
            StopImages();
        }

        private void OpenSensor()
        {
            if (System.Threading.Interlocked.Exchange(ref opened, 1) == 0)
            {
                sensor.Open();
            }
        }

        private void CloseSensorIfIdle()
        {
            lock (sync)
            {
                if (bodyReader == null && depthReader == null && colorReader == null && System.Threading.Interlocked.Exchange(ref opened, 0) == 1)
                {
                    sensor.Close();
                }
            }
        }

        private static string StateName(TrackingState s)
        {
            switch (s)
            {
                case TrackingState.Tracked:
                    return "tracked";
                case TrackingState.Inferred:
                    return "inferred";
                default:
                    return "not-tracked";
            }
        }

        private void OnBodyFrame(object? sender, BodyFrameArrivedEventArgs e)
        {
            var handler = FrameReceived;
            if (handler == null)
            {
                return;
            }

            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null)
                {
                    return;
                }

                var host = DateTime.UtcNow.Ticks;
                if (bodies == null || bodies.Length != sensor.BodyFrameSource.BodyCount)
                {
                    bodies = new Body[sensor.BodyFrameSource.BodyCount];
                }

                frame.GetAndRefreshBodyData(bodies);
                var users = new List<TrackedUser>();
                foreach (var body in bodies)
                {
                    if (body == null || !body.IsTracked)
                    {
                        continue;
                    }

                    var joints = new List<RawJoint>(body.Joints.Count);
                    foreach (var kv in body.Joints)
                    {
                        var j = kv.Value;
                        joints.Add(new RawJoint(kv.Key.ToString(), j.Position.X, j.Position.Y, j.Position.Z, null, StateName(j.TrackingState)));
                    }

                    users.Add(new TrackedUser(unchecked((int)body.TrackingId), joints));
                }

                var plane = frame.FloorClipPlane;
                double[]? floor = (plane.X == 0 && plane.Y == 0 && plane.Z == 0 && plane.W == 0) ? null : new double[] { plane.X, plane.Y, plane.Z, plane.W };
                var scene = floor == null ? null : new SceneInfo(floor);
                handler(new SkeletonFrame((long)(frame.RelativeTime.TotalMilliseconds * 1000.0), host, users, scene));
            }
        }

        private void OnDepthFrame(object? sender, DepthFrameArrivedEventArgs e)
        {
            var handler = DepthReceived;
            if (handler == null)
            {
                return;
            }

            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null)
                {
                    return;
                }

                var host = DateTime.UtcNow.Ticks;
                var w = frame.FrameDescription.Width;
                var h = frame.FrameDescription.Height;
                if (depthBuffer == null || depthBuffer.Length != w * h)
                {
                    depthBuffer = new ushort[w * h];
                }

                frame.CopyFrameDataToArray(depthBuffer);
                var data = new byte[w * h * 2];
                Buffer.BlockCopy(depthBuffer, 0, data, 0, data.Length);
                handler(new ImageFrame((long)(frame.RelativeTime.TotalMilliseconds * 1000.0), host, w, h, "Depth16", data));
            }
        }

        private void OnColorFrame(object? sender, ColorFrameArrivedEventArgs e)
        {
            var handler = ColorReceived;
            if (handler == null)
            {
                return;
            }

            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null)
                {
                    return;
                }

                var host = DateTime.UtcNow.Ticks;
                var w = frame.FrameDescription.Width;
                var h = frame.FrameDescription.Height;
                if (frame.RawColorImageFormat != ColorImageFormat.Yuy2)
                {
                    var data = new byte[w * h * 4];
                    frame.CopyConvertedFrameDataToArray(data, ColorImageFormat.Bgra);
                    handler(new ImageFrame((long)(frame.RelativeTime.TotalMilliseconds * 1000.0), host, w, h, "Bgra", data));
                    return;
                }

                var raw = new byte[w * h * 2];
                frame.CopyRawFrameDataToArray(raw);
                handler(new ImageFrame((long)(frame.RelativeTime.TotalMilliseconds * 1000.0), host, w, h, "Yuy2", raw));
            }
        }
    }
}
