using System;

namespace DanceLab.Core.Canonical
{
    /// <summary>Трёхмерный вектор для построения канонической позы.</summary>
    public readonly struct Vec3
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public static double Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

        public Vec3 Normalized()
        {
            var l = Length;
            return l < 1e-12 ? new Vec3(0, 0, 0) : new Vec3(X / l, Y / l, Z / l);
        }

        /// <summary>Угол между векторами в радианах; для нулевого вектора даёт 0.</summary>
        public static double Angle(Vec3 a, Vec3 b) => Math.Atan2(Cross(a, b).Length, Dot(a, b));
    }
}
