using System;
using System.Collections.Generic;

namespace DanceLab.Capture
{
    /// <summary>Частота и разброс интервалов потока кадров по скользящему окну временных меток устройства.</summary>
    public sealed class StreamStats
    {
        private readonly int window;
        private readonly Queue<long> times = new Queue<long>();

        public StreamStats(int window = 90)
        {
            if (window < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(window), "Окно должно быть не меньше 3 кадров.");
            }

            this.window = window;
        }

        /// <summary>Сколько кадров принято за всё время.</summary>
        public long Total { get; private set; }

        public void Add(long deviceTimeUs)
        {
            times.Enqueue(deviceTimeUs);
            Total++;
            while (times.Count > window)
            {
                times.Dequeue();
            }
        }

        public void Reset()
        {
            times.Clear();
            Total = 0;
        }

        /// <summary>Кадров в секунду по окну, 0 если кадров меньше двух.</summary>
        public double Fps
        {
            get
            {
                var mean = MeanIntervalMs;
                return mean > 0 ? 1000.0 / mean : 0;
            }
        }

        public double MeanIntervalMs
        {
            get
            {
                var d = Intervals();
                return d.Count == 0 ? 0 : Mean(d);
            }
        }

        /// <summary>Стандартное отклонение интервалов между кадрами, мс (джиттер).</summary>
        public double JitterMs
        {
            get
            {
                var d = Intervals();
                if (d.Count < 2)
                {
                    return 0;
                }

                var m = Mean(d);
                var s = 0.0;
                foreach (var v in d)
                {
                    s += (v - m) * (v - m);
                }

                return Math.Sqrt(s / d.Count);
            }
        }

        public double MaxIntervalMs
        {
            get
            {
                var max = 0.0;
                foreach (var v in Intervals())
                {
                    max = Math.Max(max, v);
                }

                return max;
            }
        }

        private List<double> Intervals()
        {
            var result = new List<double>(times.Count);
            long? prev = null;
            foreach (var t in times)
            {
                if (prev.HasValue)
                {
                    result.Add((t - prev.Value) / 1000.0);
                }

                prev = t;
            }

            return result;
        }

        private static double Mean(List<double> d)
        {
            var s = 0.0;
            foreach (var v in d)
            {
                s += v;
            }

            return s / d.Count;
        }
    }

    /// <summary>Положение сенсора относительно пола по плоскости ax+by+cz+d=0 в системе сенсора.</summary>
    public readonly struct FloorGeometry
    {
        public FloorGeometry(double heightM, double pitchDeg, double rollDeg)
        {
            HeightM = heightM;
            PitchDeg = pitchDeg;
            RollDeg = rollDeg;
        }

        /// <summary>Высота сенсора над полом, метры.</summary>
        public double HeightM { get; }

        /// <summary>Наклон оптической оси вверх или вниз, градусы (поворот вокруг оси x).</summary>
        public double PitchDeg { get; }

        /// <summary>Крен вокруг оптической оси, градусы.</summary>
        public double RollDeg { get; }

        /// <summary>Разбирает плоскость пола. Возвращает null, если плоскость вырождена (нулевая нормаль).</summary>
        public static FloorGeometry? FromPlane(double[] plane)
        {
            if (plane == null || plane.Length != 4)
            {
                throw new ArgumentException("Плоскость задаётся четырьмя числами a, b, c, d.", nameof(plane));
            }

            var a = plane[0];
            var b = plane[1];
            var c = plane[2];
            var d = plane[3];
            var n = Math.Sqrt((a * a) + (b * b) + (c * c));
            if (n < 1e-9)
            {
                return null;
            }

            const double toDeg = 180.0 / Math.PI;
            return new FloorGeometry(Math.Abs(d) / n, Math.Atan2(c, b) * toDeg, Math.Atan2(a, b) * toDeg);
        }

        /// <summary>Высота точки над полом, метры (со знаком).</summary>
        public static double HeightAbove(double[] plane, double x, double y, double z)
        {
            var n = Math.Sqrt((plane[0] * plane[0]) + (plane[1] * plane[1]) + (plane[2] * plane[2]));
            return ((plane[0] * x) + (plane[1] * y) + (plane[2] * z) + plane[3]) / n;
        }
    }
}
