using System;
using System.Collections.Generic;
using System.Diagnostics;
using DanceLab.Capture;
using DanceLab.Capture.Kinect;
using DanceLab.Capture.Nuitrack;
using DanceLab.Core.Reconstruction;

namespace PlayerTrial
{
    /// <summary>
    /// Урезанная копия tools/sensor_view/LiveSession.cs для пробы: без глубины и записи.
    /// Один запущенный трекер; обработчики приходят на потоках трекера и ничего не блокируют.
    /// Latest отдаёт последний кадр (с достройкой), рядом достроенная поза и длины костей каждого человека.
    /// </summary>
    internal sealed class PlayerSession : IDisposable
    {
        private readonly object sync = new object();
        private readonly IPoseSource pose;
        private readonly Func<string?> lastError;
        private readonly Func<string> version;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly MultiUserReconstructor reconstructor;
        private SkeletonFrame? latest;

        private PlayerSession(string key, IPoseSource pose, Func<string?> lastError, Func<string> version)
        {
            Key = key;
            TrackerId = Topology.TrackerId(key);
            this.pose = pose;
            this.lastError = lastError;
            this.version = version;
            reconstructor = new MultiUserReconstructor(TrackerId);
            pose.FrameReceived += OnSkeleton;
        }

        public string Key { get; }

        /// <summary>Живой источник кадров: нужен LiveRecorder для записи сессии.</summary>
        public IPoseSource Source => pose;

        public string TrackerId { get; }

        public string Version => version();

        public string? LastError => lastError();

        public TimeSpan Elapsed => clock.Elapsed;

        public double SkeletonFps
        {
            get
            {
                lock (sync)
                {
                    return skeleton.Fps;
                }
            }
        }

        private readonly StreamStats skeleton = new StreamStats(90);

        public static PlayerSession Create(string key)
        {
            if (key == "nuitrack-ai" || key == "nuitrack-native")
            {
                // нативный режим оставляет Skeletonization.Type из конфига Nuitrack, AI ставит CNN_HPE
                var n = new NuitrackPoseSource("viewer", key == "nuitrack-ai" ? "CNN_HPE" : null) { UseColorSensor = false };
                return new PlayerSession(key, n, () => n.LastError, () => n.Version);
            }

            var k = new KinectPoseSource("viewer");
            return new PlayerSession(key, k, () => null, () => k.Version);
        }

        public void Start()
        {
            pose.Start();
            clock.Restart();
        }

        /// <summary>Последний кадр (достроенный), null пока кадров нет.</summary>
        public SkeletonFrame? Latest
        {
            get
            {
                lock (sync)
                {
                    return latest;
                }
            }
        }

        /// <summary>Достроенная поза и снимок длин костей человека из последнего кадра.</summary>
        public bool TryGetUser(int id, out ReconstructedPose pose, out IReadOnlyDictionary<string, LimbLengths> lengths)
        {
            lock (sync)
            {
                return reconstructor.TryGetUser(id, out pose, out lengths);
            }
        }

        public void Dispose()
        {
            pose.FrameReceived -= OnSkeleton;
            try
            {
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
                rebuilt = reconstructor.Process(f);
            }
            catch (Exception)
            {
                reconstructor.Reset();
            }

            lock (sync)
            {
                latest = rebuilt ?? f;
                skeleton.Add(f.DeviceTimeUs);
            }
        }
    }
}
