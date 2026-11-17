using System;
using System.Diagnostics;
using DanceLab.Capture;
using DanceLab.Capture.Kinect;
using DanceLab.Capture.Nuitrack;
using DanceLab.Core.Reconstruction;

namespace SensorView
{
    /// <summary>Один запущенный трекер с накопленной статистикой. Обработчики вызываются на потоках трекера и ничего не блокируют.</summary>
    internal sealed class LiveSession : IDisposable
    {
        private readonly object sync = new object();
        private readonly IPoseSource pose;
        private readonly IRawImageSource images;
        private readonly Func<string?> lastError;
        private readonly Func<string> version;
        private readonly Func<string?> engine;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly PoseReconstructor reconstructor;
        private SkeletonFrame? latest;
        private SkeletonFrame? latestReconstructed;
        private ImageFrame? latestDepth;
        private bool imagesOn;

        private LiveSession(string key, IPoseSource pose, IRawImageSource images, Func<string?> lastError, Func<string> version, Func<string?> engine)
        {
            Key = key;
            this.pose = pose;
            this.images = images;
            this.lastError = lastError;
            this.version = version;
            this.engine = engine;
            reconstructor = new PoseReconstructor(pose.TrackerId);
            pose.FrameReceived += OnSkeleton;
            images.DepthReceived += OnDepth;
        }

        public string Key { get; }

        public StreamStats Skeleton { get; } = new StreamStats(90);

        public StreamStats Depth { get; } = new StreamStats(60);

        public string TrackerId => pose.TrackerId;

        public SensorInfo Sensor => pose.Sensor;

        public string Version => version();

        public string? Engine => engine();

        public string? LastError => lastError();

        public TimeSpan Elapsed => clock.Elapsed;

        public static LiveSession Create(string key)
        {
            if (key == "nuitrack-ai" || key == "nuitrack-native")
            {
                // нативный режим оставляет Skeletonization.Type из конфига Nuitrack (классический трекер), AI ставит CNN_HPE
                var n = new NuitrackPoseSource("viewer", key == "nuitrack-ai" ? "CNN_HPE" : null) { UseColorSensor = false };
                return new LiveSession(key, n, n, () => n.LastError, () => n.Version, () => n.Engine);
            }

            var k = new KinectPoseSource("viewer") { EnableFace = true };
            return new LiveSession(key, k, k, () => null, () => k.Version, () => null);
        }

        public void Start()
        {
            pose.Start();
            clock.Restart();
        }

        public void SetDepth(bool on)
        {
            if (on == imagesOn)
            {
                return;
            }

            imagesOn = on;
            if (on)
            {
                images.StartImages();
            }
            else
            {
                images.StopImages();
            }
        }

        /// <summary>true: Latest отдаёт достроенную позу (specs/core/pose-reconstruction.md), false: сырую от трекера.</summary>
        public volatile bool UseReconstruction = true;

        public LiveRecorder? Recorder { get; private set; }

        /// <summary>Начинает запись скелета в папку, источник продолжает работать.</summary>
        public void StartRecording(string directory, string scenario)
        {
            Recorder = LiveRecorder.Start(pose, directory, scenario, Version, Engine);
        }

        /// <summary>Заканчивает запись, возвращает файл и число кадров.</summary>
        public (string Path, int Frames)? StopRecording()
        {
            var r = Recorder;
            Recorder = null;
            if (r == null)
            {
                return null;
            }

            var frames = r.Stop();
            return (r.Path, frames);
        }

        public bool IsAvailable => pose.IsAvailable;

        public SkeletonFrame? Latest
        {
            get
            {
                lock (sync)
                {
                    return UseReconstruction ? latestReconstructed ?? latest : latest;
                }
            }
        }

        public ImageFrame? LatestDepth
        {
            get
            {
                lock (sync)
                {
                    return latestDepth;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                Recorder?.Dispose();
            }
            finally
            {
                Recorder = null;
            }

            pose.FrameReceived -= OnSkeleton;
            images.DepthReceived -= OnDepth;
            try
            {
                if (imagesOn)
                {
                    images.StopImages();
                }

                pose.Stop();
            }
            finally
            {
                pose.Dispose();
            }
        }

        private void OnSkeleton(SkeletonFrame f)
        {
            SkeletonFrame? rebuilt = null;
            try
            {
                rebuilt = Conversion.ToFrame(reconstructor.Process(f), f);
            }
            catch (System.Exception)
            {
                reconstructor.Reset();
            }

            lock (sync)
            {
                latest = f;
                latestReconstructed = rebuilt;
                Skeleton.Add(f.DeviceTimeUs);
            }
        }

        private void OnDepth(ImageFrame f)
        {
            lock (sync)
            {
                latestDepth = f;
                Depth.Add(f.DeviceTimeUs);
            }
        }

        /// <summary>Снимок статистики для показа, потокобезопасно.</summary>
        public (double Fps, double MeanMs, double JitterMs, double MaxMs, long Total, double DepthFps, long DepthTotal) Snapshot()
        {
            lock (sync)
            {
                return (Skeleton.Fps, Skeleton.MeanIntervalMs, Skeleton.JitterMs, Skeleton.MaxIntervalMs, Skeleton.Total, Depth.Fps, Depth.Total);
            }
        }
    }
}
