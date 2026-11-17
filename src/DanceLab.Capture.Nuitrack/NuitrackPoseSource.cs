using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using nuitrack;
using nuitrack.device;

namespace DanceLab.Capture.Nuitrack
{
    /// <summary>
    /// Источник на Nuitrack. Скелет: суставы в метрах (Nuitrack отдаёт миллиметры), система координат сенсора,
    /// уверенность из Joint.Confidence. Глубина uint16 (как отдал Nuitrack), цвет RGB24.
    /// Nuitrack отдаёт события только внутри Nuitrack.Update, поэтому у источника свой поток-насос.
    /// </summary>
    public sealed class NuitrackPoseSource : IPoseSource, IRawImageSource
    {
        public const string Id = "nuitrack";
        private readonly string sensorId;
        private readonly string? skeletonEngine;
        private readonly object sync = new object();
        private DepthSensor? depthSensor;
        private ColorSensor? colorSensor;
        private SkeletonTracker? skeletonTracker;
        private Thread? pump;
        private volatile bool running;
        private bool initialized;
        private bool wantSkeleton;
        private bool wantImages;
        private bool sceneSent;
        private SceneInfo? sceneOnce;

        /// <param name="sensorId">Имя сенсора из сцены.</param>
        /// <param name="skeletonEngine">Значение Skeletonization.Type (например CNN_HPE для AI), null оставляет конфиг как есть.</param>
        public NuitrackPoseSource(string sensorId, string? skeletonEngine = null)
        {
            this.sensorId = sensorId;
            this.skeletonEngine = skeletonEngine;
            Sensor = new SensorInfo(sensorId, null, null, null);
        }

        /// <summary>false: не создавать цветовой датчик (только для скелета, диагностика частоты). Задаётся до Start.</summary>
        public bool UseColorSensor { get; set; } = true;

        /// <summary>Дополнительные значения конфига Nuitrack (ключ и значение), применяются после выбора устройства. Для диагностики.</summary>
        public System.Collections.Generic.IDictionary<string, string> ConfigOverrides { get; } = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>Что вернул GetConfigValue для каждого переопределённого ключа (или текст ошибки).</summary>
        public System.Collections.Generic.IDictionary<string, string> ConfigResults { get; } = new System.Collections.Generic.Dictionary<string, string>();

        public string TrackerId => Id;

        public SensorInfo Sensor { get; private set; }

        /// <summary>Nuitrack инициализирован, устройство найдено и насос работает.</summary>
        public bool IsAvailable => running;

        /// <summary>Значение Skeletonization.Type, которое реально действует.</summary>
        public string? Engine { get; private set; }

        /// <summary>Версия Nuitrack, известна после Start или StartImages, до этого unknown.</summary>
        public string Version { get; private set; } = "unknown";

        public event Action<SkeletonFrame>? FrameReceived;

        public event Action<ImageFrame>? DepthReceived;

        public event Action<ImageFrame>? ColorReceived;

        public void Start()
        {
            lock (sync)
            {
                wantSkeleton = true;
                EnsureRunning();
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                wantSkeleton = false;
                StopIfIdle();
            }
        }

        public void StartImages()
        {
            lock (sync)
            {
                wantImages = true;
                EnsureRunning();
            }
        }

        public void StopImages()
        {
            lock (sync)
            {
                wantImages = false;
                StopIfIdle();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                wantSkeleton = false;
                wantImages = false;
                StopIfIdle();
            }
        }

        private void EnsureRunning()
        {
            if (running)
            {
                return;
            }

            nuitrack.Nuitrack.Init();
            initialized = true;
            Version = nuitrack.Nuitrack.GetVersion().ToString();
            var devices = nuitrack.Nuitrack.GetDeviceList();
            if (devices.Count == 0)
            {
                nuitrack.Nuitrack.Release();
                initialized = false;
                throw new InvalidOperationException("Nuitrack не нашёл устройств.");
            }

            var device = devices[0];
            nuitrack.Nuitrack.SetDevice(device);
            if (skeletonEngine != null)
            {
                nuitrack.Nuitrack.SetConfigValue("Skeletonization.Type", skeletonEngine);
            }

            foreach (var kv in ConfigOverrides)
            {
                try
                {
                    if (kv.Value != "?")
                    {
                        nuitrack.Nuitrack.SetConfigValue(kv.Key, kv.Value);
                    }

                    ConfigResults[kv.Key] = nuitrack.Nuitrack.GetConfigValue(kv.Key);
                }
                catch (System.Exception ex)
                {
                    ConfigResults[kv.Key] = "ошибка: " + ex.Message;
                }
            }

            Engine = nuitrack.Nuitrack.GetConfigValue("Skeletonization.Type");

            depthSensor = DepthSensor.Create();
            colorSensor = UseColorSensor ? ColorSensor.Create() : null;
            skeletonTracker = SkeletonTracker.Create();
            depthSensor.OnUpdateEvent += OnDepth;
            if (colorSensor != null)
            {
                colorSensor.OnUpdateEvent += OnColor;
            }
            skeletonTracker.OnSkeletonUpdateEvent += OnSkeleton;

            var dm = depthSensor.GetOutputMode();
            var cm = colorSensor?.GetOutputMode();
            string? serial = null;
            try
            {
                serial = device.GetInfo(DeviceInfoType.SERIAL_NUMBER);
            }
            catch (System.Exception)
            {
                serial = null;
            }

            Sensor = new SensorInfo(
                sensorId,
                serial,
                new FrameSize(dm.Size.Width, dm.Size.Height, dm.FPS, "Depth16"),
                cm == null ? null : new FrameSize(cm.Value.Size.Width, cm.Value.Size.Height, cm.Value.FPS, "Rgb24"));
            sceneOnce = new SceneInfo(null, new Dictionary<string, object>
            {
                { "depthHFovRad", (double)dm.HFOV },
                { "depthVFovRad", (double)dm.VFOV },
                { "depthFx", (double)dm.FX },
                { "depthFy", (double)dm.FY },
                { "depthCx", (double)dm.CX },
                { "depthCy", (double)dm.CY },
            });
            sceneSent = false;

            nuitrack.Nuitrack.Run();
            running = true;
            pump = new Thread(Pump) { IsBackground = true, Name = "nuitrack-pump" };
            pump.Start();
        }

        private void StopIfIdle()
        {
            if (wantSkeleton || wantImages || !running)
            {
                return;
            }

            running = false;
            pump?.Join();
            pump = null;
            if (depthSensor != null)
            {
                depthSensor.OnUpdateEvent -= OnDepth;
            }

            if (colorSensor != null)
            {
                colorSensor.OnUpdateEvent -= OnColor;
            }

            if (skeletonTracker != null)
            {
                skeletonTracker.OnSkeletonUpdateEvent -= OnSkeleton;
            }

            depthSensor = null;
            colorSensor = null;
            skeletonTracker = null;
            if (initialized)
            {
                nuitrack.Nuitrack.Release();
                initialized = false;
            }
        }

        private void Pump()
        {
            while (running)
            {
                try
                {
                    nuitrack.Nuitrack.Update();
                }
                catch (LicenseNotAcquiredException)
                {
                    running = false;
                    LastError = "Лицензия Nuitrack не получена (пробная лицензия требует активации в Nuitrack.exe).";
                    return;
                }
                catch (System.Exception ex)
                {
                    LastError = ex.Message;
                    Thread.Sleep(10);
                }
            }
        }

        /// <summary>Последняя ошибка потока-насоса, если он остановился или сбоил.</summary>
        public string? LastError { get; private set; }

        private void OnSkeleton(SkeletonData data)
        {
            var handler = FrameReceived;
            if (handler == null || !wantSkeleton)
            {
                return;
            }

            var host = DateTime.UtcNow.Ticks;
            var users = new List<TrackedUser>();
            foreach (var s in data.Skeletons)
            {
                var joints = new List<RawJoint>(s.Joints.Length);
                foreach (var j in s.Joints)
                {
                    joints.Add(new RawJoint(j.Type.ToString(), j.Real.X / 1000.0, j.Real.Y / 1000.0, j.Real.Z / 1000.0, j.Confidence, null));
                }

                users.Add(new TrackedUser(s.ID, joints));
            }

            SceneInfo? scene = null;
            if (!sceneSent)
            {
                scene = sceneOnce;
                sceneSent = true;
            }

            handler(new SkeletonFrame((long)data.Timestamp, host, users, scene));
        }

        private void OnDepth(DepthFrame frame)
        {
            var handler = DepthReceived;
            if (handler == null || !wantImages)
            {
                return;
            }

            var host = DateTime.UtcNow.Ticks;
            var data = new byte[frame.Cols * frame.Rows * 2];
            Marshal.Copy(frame.Data, data, 0, data.Length);
            handler(new ImageFrame((long)frame.Timestamp, host, frame.Cols, frame.Rows, "Depth16", data));
        }

        private void OnColor(ColorFrame frame)
        {
            var handler = ColorReceived;
            if (handler == null || !wantImages)
            {
                return;
            }

            var host = DateTime.UtcNow.Ticks;
            var data = new byte[frame.Cols * frame.Rows * 3];
            Marshal.Copy(frame.Data, data, 0, data.Length);
            handler(new ImageFrame((long)frame.Timestamp, host, frame.Cols, frame.Rows, "Rgb24", data));
        }
    }
}
