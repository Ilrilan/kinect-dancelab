using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DanceLab.Core.Reconstruction;
using DanceLab.View;

namespace PlayerTrial
{
    /// <summary>
    /// Сцена сенсора (specs/player/trial.md S16): скелеты всех людей палками одним проходом OnRender,
    /// без WPF Shape на кадр. Вид спереди (x, y) или сверху (x, z) — флаг в конструкторе.
    /// Кости парами суставов (корень-грудь-шея-голова, линия плеч, линия бёдер, плечо-локоть-кисть,
    /// бедро-колено-стопа, крест плечо-бедро своего борта); измерённые суставы (Measured) сплошной
    /// линией цветом человека, достроенные (Reconstructed/Extrapolated) золотым, Invalid пропускаются.
    /// Цвет человека — по id (палитра из 4), подпись id рядом с корнем. Пол — горизонтальная линия
    /// на оценке пола (вид спереди), сетка 1 м как в GhostCanvas.
    /// </summary>
    internal sealed class SceneCanvas : FrameworkElement
    {
        private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)));
        private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2c, 0x2e, 0x36)), 1));
        private static readonly Pen FloorPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50)), 2));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9a, 0xa0, 0xaa)));
        private static readonly Color Gold = Colors.Gold;

        /// <summary>Палитра цветов людей по id (4 цвета, id берётся по модулю).</summary>
        private static readonly Color[] PersonColors =
        {
            Color.FromRgb(0x64, 0xb5, 0xf6),
            Color.FromRgb(0xff, 0xb7, 0x4d),
            Color.FromRgb(0x81, 0xc7, 0x84),
            Color.FromRgb(0xba, 0x68, 0xc8),
        };

        /// <summary>Группы имён суставов у реконструкции: Kinect, Nuitrack и канонические (как в Canonicalizer.Names).</summary>
        private static readonly string[] RootNames = { "SpineBase", "Waist", "root" };
        private static readonly string[] ChestNames = { "SpineMid", "Torso", "Chest", "chest" };
        private static readonly string[] NeckNames = { "Neck", "neck" };
        private static readonly string[] HeadNames = { "Head", "head" };
        private static readonly string[] ShoulderLNames = { "ShoulderLeft", "LeftShoulder", "shoulder_l" };
        private static readonly string[] ShoulderRNames = { "ShoulderRight", "RightShoulder", "shoulder_r" };
        private static readonly string[] ElbowLNames = { "ElbowLeft", "LeftElbow", "elbow_l" };
        private static readonly string[] ElbowRNames = { "ElbowRight", "RightElbow", "elbow_r" };
        private static readonly string[] WristLNames = { "WristLeft", "LeftWrist", "wrist_l" };
        private static readonly string[] WristRNames = { "WristRight", "RightWrist", "wrist_r" };
        private static readonly string[] HipLNames = { "HipLeft", "LeftHip", "hip_l" };
        private static readonly string[] HipRNames = { "HipRight", "RightHip", "hip_r" };
        private static readonly string[] KneeLNames = { "KneeLeft", "LeftKnee", "knee_l" };
        private static readonly string[] KneeRNames = { "KneeRight", "RightKnee", "knee_r" };
        private static readonly string[] AnkleLNames = { "AnkleLeft", "LeftAnkle", "ankle_l" };
        private static readonly string[] AnkleRNames = { "AnkleRight", "RightAnkle", "ankle_r" };

        private static readonly string[][][] BonePairs =
        {
            new[] { RootNames, ChestNames },
            new[] { ChestNames, NeckNames },
            new[] { NeckNames, HeadNames },
            new[] { ShoulderLNames, ShoulderRNames },
            new[] { ShoulderLNames, ElbowLNames },
            new[] { ElbowLNames, WristLNames },
            new[] { ShoulderRNames, ElbowRNames },
            new[] { ElbowRNames, WristRNames },
            new[] { HipLNames, HipRNames },
            new[] { HipLNames, KneeLNames },
            new[] { KneeLNames, AnkleLNames },
            new[] { HipRNames, KneeRNames },
            new[] { KneeRNames, AnkleRNames },
            new[] { ShoulderLNames, HipLNames },
            new[] { ShoulderRNames, HipRNames },
        };

        private static readonly (int id, ReconstructedPose pose)[] EmptyUsers = Array.Empty<(int id, ReconstructedPose pose)>();

        private readonly bool top;
        private readonly string title;
        private IReadOnlyList<(int id, ReconstructedPose pose)> users = EmptyUsers;
        private double floorYm;
        private bool floorReady;

        /// <summary>
        /// Трекер сессии для знака экранного x (Topology.ScreenXSign, как SkeletonCanvas в sensor_view):
        /// по умолчанию Kinect — знак −1, вид от сенсора без зеркала (прежнее поведение cx − x·scale).
        /// </summary>
        public string TrackerId { get; set; } = Topology.Kinect;

        public SceneCanvas(bool topView, string title)
        {
            top = topView;
            this.title = title;
            ClipToBounds = true;
        }

        /// <summary>Оценка пола живой сцены (S15), метры; ready=false — оценки ещё нет, линия пола не рисуется.</summary>
        public void SetFloor(double floorMeters, bool ready)
        {
            floorYm = floorMeters;
            floorReady = ready;
            InvalidateVisual();
        }

        public void SetUsers(IReadOnlyList<(int id, ReconstructedPose pose)> newUsers)
        {
            users = newUsers ?? EmptyUsers;
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

            if (top)
            {
                // подпись панели прижата к правому верхнему углу: слева она пересекается с осевой меткой «5 м»
                var ft = MakeText(title, 13, LabelBrush);
                dc.DrawText(ft, new Point(w - ft.Width - 8, 6));
                DrawTop(dc, w, h);
            }
            else
            {
                Text(dc, title, 8, 6, 13, LabelBrush);
                DrawFront(dc, w, h);
            }
        }

        /// <summary>Вид спереди (x, y): масштаб и сетка как в GhostCanvas, пол — линия на оценке пола.</summary>
        private void DrawFront(DrawingContext dc, double w, double h)
        {
            var scale = h / 3.2;
            var floorPx = floorReady ? h * 0.86 - (floorYm * scale) : h * 0.86;
            var cx = w / 2;
            var sign = Topology.ScreenXSign(TrackerId);

            for (var m = -3; m <= 3; m++)
            {
                var x = cx + (m * scale);
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            }

            for (var m = 1; floorPx - (m * scale) > 0; m++)
            {
                var y = floorPx - (m * scale);
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }

            if (floorReady)
            {
                dc.DrawLine(FloorPen, new Point(0, floorPx), new Point(w, floorPx));
                Text(dc, "пол " + floorYm.ToString("0.00", CultureInfo.InvariantCulture) + " м", 6, floorPx - 16, 11, FloorPen.Brush);
            }

            foreach (var u in users)
            {
                DrawUser(dc, u.id, u.pose, (x, y) => new Point(cx + (sign * x * scale), floorPx - (y * scale)));
            }
        }

        /// <summary>Вид сверху (x, z): сенсор у нижней кромки, глубина — вверх по экрану, сетка 1 м.</summary>
        private void DrawTop(DrawingContext dc, double w, double h)
        {
            var scale = Math.Min((h - 40) / 5.0, w / 5.0);
            var cx = w / 2;
            var originY = h - 20;
            var sign = Topology.ScreenXSign(TrackerId);

            for (var m = 0; m <= 5; m++)
            {
                var y = originY - (m * scale);
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
                Text(dc, m + " м", 4, y - 14, 11, LabelBrush);
            }

            for (var m = -2; m <= 2; m++)
            {
                var x = cx + (m * scale);
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            }

            dc.DrawEllipse(null, new Pen(LabelBrush, 1), new Point(cx, originY), 5, 5);
            Text(dc, "сенсор", cx + 8, originY - 8, 11, LabelBrush);

            foreach (var u in users)
            {
                DrawUser(dc, u.id, u.pose, (x, z) => new Point(cx + (sign * x * scale), originY - (z * scale)));
            }
        }

        private void DrawUser(DrawingContext dc, int id, ReconstructedPose pose, Func<double, double, Point> map)
        {
            var color = PersonColors[((id % PersonColors.Length) + PersonColors.Length) % PersonColors.Length];
            var joints = new Dictionary<string, ReconstructedJoint>();
            foreach (var j in pose.Joints)
            {
                if (j.Origin != JointOrigin.Invalid && !joints.ContainsKey(j.Name))
                {
                    joints[j.Name] = j;
                }
            }

            ReconstructedJoint? Find(string[] names)
            {
                foreach (var n in names)
                {
                    if (joints.TryGetValue(n, out var j))
                    {
                        return j;
                    }
                }

                return null;
            }

            Point P(ReconstructedJoint j) => map(j.X, top ? j.Z : j.Y);

            foreach (var pair in BonePairs)
            {
                var a = Find(pair[0]);
                var b = Find(pair[1]);
                if (a == null || b == null)
                {
                    continue;
                }

                var rebuilt = a.Origin != JointOrigin.Measured || b.Origin != JointOrigin.Measured;
                var brush = rebuilt ? new SolidColorBrush(Gold) : new SolidColorBrush(color);
                var pen = new Pen(brush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, P(a), P(b));
            }

            // точки суставов: измеренные — цветом человека, достроенные — золотым кольцом
            foreach (var j in joints.Values)
            {
                var p = P(j);
                if (j.Origin == JointOrigin.Measured)
                {
                    dc.DrawEllipse(new SolidColorBrush(color), null, p, 3, 3);
                }
                else
                {
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Gold), 1.5), p, 4, 4);
                }
            }

            var root = Find(RootNames);
            if (root != null)
            {
                Text(dc, "id " + id.ToString(CultureInfo.InvariantCulture), P(root).X + 8, P(root).Y - 8, 13, new SolidColorBrush(color));
            }
        }

        private FormattedText MakeText(string text, double size, Brush brush)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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
