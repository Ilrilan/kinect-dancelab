using System;
using System.Collections.Generic;

namespace DanceLab.Capture
{
    /// <summary>Параметры потока кадров, как их сообщает трекер.</summary>
    public sealed class FrameSize
    {
        public FrameSize(int width, int height, double fps, string format)
        {
            Width = width;
            Height = height;
            Fps = fps;
            Format = format;
        }

        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }
        public string Format { get; }
    }

    public sealed class SensorInfo
    {
        public SensorInfo(string id, string? serial, FrameSize? depth, FrameSize? color)
        {
            Id = id;
            Serial = serial;
            Depth = depth;
            Color = color;
        }

        public string Id { get; }
        public string? Serial { get; }
        public FrameSize? Depth { get; }
        public FrameSize? Color { get; }
    }

    /// <summary>Сустав как его отдал трекер: имя трекера, метры, система координат сенсора.</summary>
    public sealed class RawJoint
    {
        public RawJoint(string name, double x, double y, double z, double? conf = null, string? state = null)
        {
            Name = name;
            X = x;
            Y = y;
            Z = z;
            Conf = conf;
            State = state;
        }

        public string Name { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double? Conf { get; }

        /// <summary>tracked, inferred, not-tracked или null, если трекер состояние не даёт.</summary>
        public string? State { get; }
    }

    public sealed class TrackedUser
    {
        public TrackedUser(int id, IReadOnlyList<RawJoint> joints)
        {
            Id = id;
            Joints = joints;
        }

        public int Id { get; }
        public IReadOnlyList<RawJoint> Joints { get; }
    }

    /// <summary>Сведения о сцене, которые трекер отдаёт сам.</summary>
    public sealed class SceneInfo
    {
        public SceneInfo(double[]? floorPlane, IReadOnlyDictionary<string, object>? other = null)
        {
            if (floorPlane != null && floorPlane.Length != 4)
            {
                throw new ArgumentException("Плоскость пола задаётся четырьмя числами a, b, c, d.", nameof(floorPlane));
            }

            FloorPlane = floorPlane;
            Other = other;
        }

        /// <summary>Плоскость пола ax+by+cz+d=0 в системе сенсора, метры, или null.</summary>
        public double[]? FloorPlane { get; }

        public IReadOnlyDictionary<string, object>? Other { get; }

        /// <summary>
        /// Высота пола в системе координат сенсора (м) из плоскости в hessian-форме (trial T17):
        /// точка пола на оси Y при x=z=0, то есть y = -d/b. Kinect FloorClipPlane (x,y,z) — нормаль,
        /// w — расстояние до плоскости (Microsoft.Kinect.xml). Нулевая плоскость (пол не измерен),
        /// b≈0 (вертикальная плоскость — проекции на Y нет) или кривая длина — null.
        /// </summary>
        public static double? FloorYFromPlane(double[]? floorPlane)
        {
            if (floorPlane == null || floorPlane.Length != 4)
            {
                return null;
            }

            var a = floorPlane[0];
            var b = floorPlane[1];
            var c = floorPlane[2];
            var d = floorPlane[3];
            if (a == 0 && b == 0 && c == 0 && d == 0)
            {
                return null;
            }

            return Math.Abs(b) < 1e-6 ? null : -d / b;
        }
    }

    /// <summary>Источник оценки пола живой сцены (trial T17).</summary>
    public enum FloorSource
    {
        /// <summary>Данных нет — «оцениваю».</summary>
        Estimating,

        /// <summary>Пол от трекера: SkeletonFrame.FloorY из FloorClipPlane (Kinect).</summary>
        Tracker,

        /// <summary>Оценка по лодыжкам, 2-й перцентиль (trial S15, fallback).</summary>
        Ankles,
    }

    public sealed class SkeletonFrame
    {
        public SkeletonFrame(long deviceTimeUs, long hostTimeUtcTicks, IReadOnlyList<TrackedUser> users, SceneInfo? scene = null, double? floorY = null)
        {
            DeviceTimeUs = deviceTimeUs;
            HostTimeUtcTicks = hostTimeUtcTicks;
            Users = users;
            Scene = scene;
            FloorY = floorY;
        }

        public long DeviceTimeUs { get; }
        public long HostTimeUtcTicks { get; }
        public IReadOnlyList<TrackedUser> Users { get; }
        public SceneInfo? Scene { get; }

        /// <summary>
        /// Высота пола в системе координат сенсора, м (trial T17): KinectPoseSource считает её из
        /// FloorClipPlane каждого BodyFrame (SceneInfo.FloorYFromPlane), Nuitrack пола не даёт — null.
        /// В записи живёт внутри scene.other: схема capture-record не допускает новых полей кадра
        /// верхнего уровня; старые записи без поля читаются как null.
        /// </summary>
        public double? FloorY { get; }

        /// <summary>
        /// Выбор пола живой сцены (trial T17/S15): валидный FloorY последнего кадра приоритетнее
        /// оценки по лодыжкам. Валидность — конечное число в разумном диапазоне (|y| меньше 3 м;
        /// NaN и бесконечность отсекаются сравнением). FloorY нет (Nuitrack, старые записи) —
        /// готовая оценка по лодыжкам, иначе «оцениваю».
        /// </summary>
        public static FloorSource PickFloorSource(double? frameFloorY, bool anklesReady)
        {
            if (frameFloorY.HasValue && Math.Abs(frameFloorY.Value) < 3.0)
            {
                return FloorSource.Tracker;
            }

            return anklesReady ? FloorSource.Ankles : FloorSource.Estimating;
        }
    }

    /// <summary>Кадр глубины или цвета как отдал трекер, без преобразования. Буфер принадлежит получателю.</summary>
    public sealed class ImageFrame
    {
        public ImageFrame(long deviceTimeUs, long hostTimeUtcTicks, int width, int height, string format, byte[] data)
        {
            DeviceTimeUs = deviceTimeUs;
            HostTimeUtcTicks = hostTimeUtcTicks;
            Width = width;
            Height = height;
            Format = format;
            Data = data;
        }

        public long DeviceTimeUs { get; }
        public long HostTimeUtcTicks { get; }
        public int Width { get; }
        public int Height { get; }
        public string Format { get; }
        public byte[] Data { get; }
    }

    public interface IPoseSource : IDisposable
    {
        /// <summary>kinect-sdk-2 или nuitrack.</summary>
        string TrackerId { get; }

        SensorInfo Sensor { get; }

        /// <summary>Сенсор отдаёт данные (для Kinect SDK это IsAvailable).</summary>
        bool IsAvailable { get; }

        void Start();

        void Stop();

        /// <summary>Вызывается на потоке трекера. Подписчик не должен блокировать.</summary>
        event Action<SkeletonFrame> FrameReceived;
    }

    public interface IRawImageSource : IDisposable
    {
        void StartImages();

        void StopImages();

        event Action<ImageFrame> DepthReceived;

        event Action<ImageFrame> ColorReceived;
    }
}
