using System;

namespace DanceLab.View
{
    /// <summary>
    /// Раскладка кольца камер (S24, T24): девять кнопок c01..c09 на эллипсе внутри холста
    /// CanvasW x CanvasH, кнопка — ButtonW x ButtonH. Экранная ось Y растёт вниз, поэтому
    /// положительный синус угла — ниже на экране: c05 (фронт) оказывается внизу кольца,
    /// c01 вверху; знак -1 в угле согласован с поворотом 3D-сцены. Чистая математика без
    /// WPF-типов: холст и кнопки задаются числами.
    /// </summary>
    public static class CameraRing
    {
        public const double CanvasW = 160.0, CanvasH = 120.0, ButtonW = 30.0, ButtonH = 24.0, StepDeg = 40.0;
        public const double CenterX = CanvasW / 2, CenterY = 60.0, RadiusX = 63.5, RadiusY = 46.0;

        /// <summary>Угол кнопки на кольце, градусы: c05 внизу (фронт), c01 вверху; знак -1 согласован с поворотом 3D-сцены.</summary>
        public static double AngleDeg(int camera)
        {
            return 90 + (-1) * ((camera - 5) * StepDeg);
        }

        /// <summary>Левый верхний угол кнопки камеры 1..9 на холсте CanvasW x CanvasH; вне 1..9 — исключение.</summary>
        public static (double Left, double Top) Layout(int camera)
        {
            if (camera < 1 || camera > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(camera), camera, "камера вне 1..9");
            }

            var a = AngleDeg(camera) * Math.PI / 180.0;
            var cx = CenterX + RadiusX * Math.Cos(a); // центр кнопки на эллипсе
            var cy = CenterY + RadiusY * Math.Sin(a);
            return (cx - ButtonW / 2, cy - ButtonH / 2);
        }
    }
}
