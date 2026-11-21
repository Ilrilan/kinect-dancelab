using System;
using System.Windows;
using System.Windows.Media;
using DanceLab.Core.Canonical;

namespace PlayerTrial
{
    /// <summary>Длины костей и тулова для отрисовки призрака, метры. До калибровки — пробные доли длины ноги.</summary>
    internal struct GhostLengths
    {
        /// <summary>Пробные доли длины ноги (L = бедро + голень): это прототип, пропорции зафиксированы константами до калибровки.</summary>
        private const double ThighFrac = 0.50;
        private const double ShinFrac = 0.50;
        private const double UpperArmFrac = 0.35;
        private const double LowerArmFrac = 0.28;
        private const double HalfShoulderFrac = 0.18;
        private const double HalfHipFrac = 0.10;
        private const double TorsoFrac = 0.50;
        private const double NeckToHeadFrac = 0.23;
        private const double HeadRFrac = 0.11;

        public double UpperArm;
        public double LowerArm;
        public double Thigh;
        public double Shin;
        public double HalfShoulder;
        public double HalfHip;
        public double TorsoLen;
        public double NeckToHead;
        public double HeadR;

        public double LegLength => Thigh + Shin;

        /// <summary>Пробные доли от заданной длины ноги (до калибровки — длина ноги эталона из track.LegLengthM).</summary>
        public static GhostLengths FromLeg(double legM)
        {
            var l = new GhostLengths
            {
                Thigh = ThighFrac * legM,
                Shin = ShinFrac * legM,
                UpperArm = UpperArmFrac * legM,
                LowerArm = LowerArmFrac * legM,
                HalfShoulder = HalfShoulderFrac * legM,
                HalfHip = HalfHipFrac * legM,
                TorsoLen = TorsoFrac * legM,
                NeckToHead = NeckToHeadFrac * legM,
                HeadR = HeadRFrac * legM,
            };
            return l;
        }
    }

    /// <summary>Точка скелета призрака в мировых метрах (x — бок, y — вверх, z — вперёд).</summary>
    internal readonly struct GhostVec
    {
        public GhostVec(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public static GhostVec operator +(GhostVec a, GhostVec b) => new GhostVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static GhostVec operator -(GhostVec a, GhostVec b) => new GhostVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static GhostVec operator *(GhostVec a, double s) => new GhostVec(a.X * s, a.Y * s, a.Z * s);

        public static GhostVec operator *(double s, GhostVec a) => new GhostVec(a.X * s, a.Y * s, a.Z * s);

        public static double Dot(GhostVec a, GhostVec b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public static GhostVec Cross(GhostVec a, GhostVec b) =>
            new GhostVec((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

        public GhostVec Normalized()
        {
            var l = Length;
            return l < 1e-12 ? new GhostVec(0, 0, 0) : new GhostVec(X / l, Y / l, Z / l);
        }

        /// <summary>Составляющая, перпендикулярная оси, единичная.</summary>
        public GhostVec Orthogonalized(GhostVec axis)
        {
            var w = this - (axis * Dot(this, axis));
            return w.Normalized();
        }
    }

    /// <summary>
    /// Сцена призраков (вид спереди, x и y, как SkeletonCanvas Front в sensor_view): эталон-призрак фиксированным
    /// полупрозрачным цветом и призрак пользователя с краской кости по отклонению (мёртвая зона TrialParams не красит,
    /// дальше плавно к насыщению). Тело капсулами одним проходом OnRender, без WPF Shape на кадр.
    /// </summary>
    internal sealed class GhostCanvas : FrameworkElement
    {
        private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)));
        private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2c, 0x2e, 0x36)), 1));
        private static readonly Pen FloorPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50)), 2));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9a, 0xa0, 0xaa)));

        /// <summary>Цвет эталон-призрака (как призрак в sensor_view).</summary>
        private static readonly Color ReferenceColor = Color.FromRgb(0x7f, 0xd3, 0xff);

        /// <summary>Нейтральный цвет кости пользователя (мёртвая зона).</summary>
        private static readonly Color UserBaseColor = Color.FromRgb(0xe0, 0xe0, 0xe0);

        /// <summary>Насыщенный цвет грубого отклонения.</summary>
        private static readonly Color ErrorColor = Color.FromRgb(0xe5, 0x39, 0x35);

        /// <summary>Пробные пороги краски (TrialParams: мёртвая зона и насыщение, радиан).</summary>
        private static readonly TrialParams Paint = new TrialParams();

        private CanonicalFrame? reference;
        private CanonicalFrame? user;
        private double[]? userDeviation;
        private GhostLengths lengths = GhostLengths.FromLeg(0.8);

        /// <summary>Длины костей для прямой кинематики обоих призраков (проба: одинаковые до калибровки).</summary>
        public void SetLengths(GhostLengths newLengths)
        {
            lengths = newLengths;
        }

        public void SetReference(CanonicalFrame frame)
        {
            reference = frame;
        }

        /// <summary>Поза пользователя и отклонения костей в радианах (null — пользователя нет).</summary>
        public void SetUser(CanonicalFrame? frame, double[]? deviationRad)
        {
            user = frame;
            userDeviation = deviationRad;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            if (w < 50 || h < 50)
            {
                return;
            }

            // вид спереди как SkeletonCanvas Front: масштаб 3.2 м на высоту, пол у нижней кромки
            var scale = h / 3.2;
            var floorY = h * 0.86;
            var cx = w / 2;
            DrawGrid(dc, w, h, cx, floorY, scale);
            if (reference != null)
            {
                DrawGhost(dc, reference, ReferenceColor, null, 0.4, cx, floorY, scale);
            }

            if (user != null)
            {
                DrawGhost(dc, user, UserBaseColor, userDeviation, 0.85, cx, floorY, scale);
            }
        }

        private void DrawGrid(DrawingContext dc, double w, double h, double cx, double floorY, double scale)
        {
            for (var m = -2; m <= 2; m++)
            {
                var x = cx + (m * scale);
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            }

            for (var m = 1; floorY - (m * scale) > 0; m++)
            {
                var y = floorY - (m * scale);
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }

            dc.DrawLine(FloorPen, new Point(0, floorY), new Point(w, floorY));
            Text(dc, "пол", 6, floorY - 16, 11, FloorPen.Brush);

            // подпись панели прижата к правому верхнему углу: слева её перекрывает HUD пробы
            var title = MakeText("вид спереди (x, y), метки сетки 1 м", 13, LabelBrush);
            dc.DrawText(title, new Point(w - title.Width - 8, 6));
        }

        /// <summary>Прямая кинематика: корень от RootForward/RootSide/RootHeight с heading, торс по lean/turn, кости по направлениям канонической позы.</summary>
        private void DrawGhost(DrawingContext dc, CanonicalFrame f, Color baseColor, double[]? deviation, double opacity, double cx, double floorY, double scale)
        {
            var l = lengths;
            var leg = Math.Max(l.LegLength, 0.1);

            // мировой фрейм: heading — азимут взгляда, lean — наклон вверх-торса вперёд, turn — скрутка вокруг вертикали
            var fwdH = new GhostVec(Math.Sin(f.HeadingRad), 0, Math.Cos(f.HeadingRad));
            var leftH = new GhostVec(fwdH.Z, 0, -fwdH.X);
            var up = (new GhostVec(0, 1, 0) * Math.Cos(f.TorsoLeanRad) + fwdH * Math.Sin(f.TorsoLeanRad)).Normalized();
            var side = (leftH * Math.Cos(f.TorsoTurnRad) + fwdH * Math.Sin(f.TorsoTurnRad)).Orthogonalized(up);
            var fwdT = GhostVec.Cross(side, up);

            var root = (fwdH * (f.RootForward * leg)) + (leftH * (f.RootSide * leg)) + (new GhostVec(0, f.RootHeight * leg, 0));

            // экран: зеркало «лицом к танцору» — мировой x на экран идёт со знаком минус
            Point P(GhostVec p) => new Point(cx - (p.X * scale), floorY - (p.Y * scale));

            GhostVec Bone(int i)
            {
                var v = new GhostVec(f.Bones[i * 3], f.Bones[(i * 3) + 1], f.Bones[(i * 3) + 2]);
                return (v.X * side) + (v.Y * up) + (v.Z * fwdT);
            }

            var hipL = root + (side * l.HalfHip);
            var hipR = root - (side * l.HalfHip);
            var neck = root + (up * l.TorsoLen);
            var shL = neck + (side * l.HalfShoulder);
            var shR = neck - (side * l.HalfShoulder);
            var elbowL = shL + (Bone(0) * l.UpperArm);
            var elbowR = shR + (Bone(1) * l.UpperArm);
            var wristL = elbowL + (Bone(2) * l.LowerArm);
            var wristR = elbowR + (Bone(3) * l.LowerArm);
            var kneeL = hipL + (Bone(4) * l.Thigh);
            var kneeR = hipR + (Bone(5) * l.Thigh);
            var ankleL = kneeL + (Bone(6) * l.Shin);
            var ankleR = kneeR + (Bone(7) * l.Shin);
            var headC = neck + (up * l.NeckToHead);

            dc.PushOpacity(opacity);

            // туловище четырёхугольником плечо-бедро, позвоночник тонкой линией, голова кругом
            Color torsoColor;
            if (deviation != null)
            {
                var sum = 0.0;
                var n = 0;
                for (var i = 0; i < deviation.Length; i++)
                {
                    if (f.BoneValid[i])
                    {
                        sum += deviation[i];
                        n++;
                    }
                }

                torsoColor = n > 0 ? DeviationColor(sum / n, baseColor) : baseColor;
            }
            else
            {
                torsoColor = baseColor;
            }

            var torsoBrush = new SolidColorBrush(torsoColor);
            var torsoGeom = new StreamGeometry();
            using (var ctx = torsoGeom.Open())
            {
                ctx.BeginFigure(P(shL), true, true);
                ctx.LineTo(P(shR), true, false);
                ctx.LineTo(P(hipR), true, false);
                ctx.LineTo(P(hipL), true, false);
            }

            torsoGeom.Freeze();
            dc.DrawGeometry(torsoBrush, new Pen(torsoBrush, 0.04 * scale) { LineJoin = PenLineJoin.Round }, torsoGeom);
            dc.DrawLine(new Pen(torsoBrush, 0.02 * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, P(root), P(neck));
            dc.DrawEllipse(torsoBrush, null, P(headC), l.HeadR * scale, l.HeadR * 1.15 * scale);

            // капсулы по костям: индекс отклонения = порядок CanonicalFrame.BoneNames
            DrawBone(dc, f, deviation, 0, baseColor, P(shL), P(elbowL), 0.17 * l.UpperArm, scale);
            DrawBone(dc, f, deviation, 1, baseColor, P(shR), P(elbowR), 0.17 * l.UpperArm, scale);
            DrawBone(dc, f, deviation, 2, baseColor, P(elbowL), P(wristL), 0.14 * l.LowerArm, scale);
            DrawBone(dc, f, deviation, 3, baseColor, P(elbowR), P(wristR), 0.14 * l.LowerArm, scale);
            DrawBone(dc, f, deviation, 4, baseColor, P(hipL), P(kneeL), 0.17 * l.Thigh, scale);
            DrawBone(dc, f, deviation, 5, baseColor, P(hipR), P(kneeR), 0.17 * l.Thigh, scale);
            DrawBone(dc, f, deviation, 6, baseColor, P(kneeL), P(ankleL), 0.12 * l.Shin, scale);
            DrawBone(dc, f, deviation, 7, baseColor, P(kneeR), P(ankleR), 0.12 * l.Shin, scale);

            dc.Pop();
        }

        /// <summary>Капсула кости; невалидная кость пропускается, валидная красится по отклонению (null отклонение — базовый цвет).</summary>
        private static void DrawBone(DrawingContext dc, CanonicalFrame f, double[]? deviation, int index, Color baseColor, Point a, Point b, double radiusM, double scale)
        {
            if (!f.BoneValid[index])
            {
                return;
            }

            var color = deviation != null ? DeviationColor(deviation[index], baseColor) : baseColor;
            var pen = new Pen(new SolidColorBrush(color), 2 * radiusM * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, a, b);
        }

        /// <summary>Мёртвая зона — базовый цвет, дальше плавная краска к насыщенному (TrialParams).</summary>
        private static Color DeviationColor(double deviationRad, Color baseColor)
        {
            if (deviationRad <= Paint.DeadZone)
            {
                return baseColor;
            }

            var t = (deviationRad - Paint.DeadZone) / Math.Max(Paint.Saturation - Paint.DeadZone, 1e-9);
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromRgb(
                (byte)(baseColor.R + ((ErrorColor.R - baseColor.R) * t)),
                (byte)(baseColor.G + ((ErrorColor.G - baseColor.G) * t)),
                (byte)(baseColor.B + ((ErrorColor.B - baseColor.B) * t)));
        }

        private FormattedText MakeText(string text, double size, Brush brush)
        {
            return new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush)
        {
            dc.DrawText(MakeText(text, size, brush), new Point(x, y));
        }

        private static T Freeze<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
