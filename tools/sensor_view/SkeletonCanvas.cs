using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DanceLab.Capture;

namespace SensorView
{
    internal enum ViewKind
    {
        Front,
        Top,
    }

    /// <summary>Рисует скелет одним проходом OnRender (без элементов Shape): вид спереди (x, y) или сверху (x, z).</summary>
    internal sealed class SkeletonCanvas : FrameworkElement
    {
        private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)));
        private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2c, 0x2e, 0x36)), 1));
        private static readonly Pen FloorPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50)), 2));
        private static readonly Pen FovPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x5a, 0x66)), 1) { DashStyle = DashStyles.Dash });
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9a, 0xa0, 0xaa)));

        private static readonly Color LeftColor = Color.FromRgb(0x42, 0xa5, 0xf5);
        private static readonly Color RightColor = Color.FromRgb(0xff, 0x9a, 0x3c);
        private static readonly Color CenterColor = Color.FromRgb(0xe0, 0xe0, 0xe0);

        private readonly ViewKind kind;
        private SkeletonFrame? frame;
        private string trackerId = Topology.Kinect;
        private double[]? floor;
        private double hfovRad = 70.0 * Math.PI / 180.0;
        private string title;

        public SkeletonCanvas(ViewKind kind, string title)
        {
            this.kind = kind;
            this.title = title;
            ClipToBounds = true;
        }

        private double scalePx = 1;

        /// <summary>Рисовать полупрозрачный силуэт («призрак») под палочным скелетом.</summary>
        public bool ShowGhost { get; set; } = true;

        private string? overlay;
        private bool overlayRed;

        /// <summary>Крупная надпись по центру (обратный отсчёт, идёт запись), null убирает.</summary>
        public void SetOverlay(string? text, bool red)
        {
            if (overlay == text && overlayRed == red)
            {
                return;
            }

            overlay = text;
            overlayRed = red;
            InvalidateVisual();
        }

        public void SetData(SkeletonFrame? newFrame, string newTrackerId, double[]? newFloor, double? newHFovRad)
        {
            frame = newFrame;
            trackerId = newTrackerId;
            floor = newFloor;
            if (newHFovRad.HasValue)
            {
                hfovRad = newHFovRad.Value;
            }

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

            var sign = Topology.ScreenXSign(trackerId);
            Func<double, double, Point> map;
            double scale;

            if (kind == ViewKind.Front)
            {
                scale = h / 3.0;
                var shiftX = FirstUserRootX();
                var cx = (w / 2) - (sign * shiftX * scale);
                var cy = h * 0.58;
                map = (x, y) => new Point(cx + (sign * x * scale), cy - (y * scale));
                DrawFrontGrid(dc, w, h, cx, cy, scale);
            }
            else
            {
                scale = Math.Min((h - 40) / 5.0, w / 5.0);
                var cx = w / 2;
                map = (x, z) => new Point(cx + (sign * x * scale), h - 20 - (z * scale));
                DrawTopGrid(dc, w, h, cx, scale);
            }

            scalePx = scale;
            Text(dc, title, 8, 6, 13, LabelBrush);
            if (overlay != null)
            {
                var ft = new FormattedText(overlay, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), Math.Max(16, Math.Min(w / 14, 44)), overlayRed ? Brushes.OrangeRed : Brushes.Gold, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 3));
            }

            if (frame == null)
            {
                return;
            }

            for (var u = 0; u < frame.Users.Count; u++)
            {
                DrawUser(dc, frame.Users[u], map, w, h);
            }
        }

        private void DrawFrontGrid(DrawingContext dc, double w, double h, double cx, double cy, double scale)
        {
            for (var m = -3; m <= 3; m++)
            {
                var x = cx + (m * scale);
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
                var y = cy - (m * scale);
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }

            dc.DrawEllipse(null, new Pen(LabelBrush, 1), new Point(cx, cy), 5, 5);
            Text(dc, "сенсор (по горизонтали вид центрирован на человеке)", cx + 8, cy - 8, 11, LabelBrush);
            if (floor != null && Math.Abs(floor[1]) > 1e-6)
            {
                var y0 = -(floor[3]) / floor[1];
                var p0 = new Point(0, cy - (y0 * scale));
                var p1 = new Point(w, cy - (y0 * scale));
                dc.DrawLine(FloorPen, p0, p1);
                Text(dc, "пол", 8, p0.Y - 16, 11, FloorPen.Brush);
            }
        }

        private void DrawTopGrid(DrawingContext dc, double w, double h, double cx, double scale)
        {
            for (var m = 0; m <= 5; m++)
            {
                var y = h - 20 - (m * scale);
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
                Text(dc, m + " м", 4, y - 14, 11, LabelBrush);
            }

            for (var m = -2; m <= 2; m++)
            {
                var x = cx + (m * scale);
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            }

            var origin = new Point(cx, h - 20);
            var half = hfovRad / 2;
            dc.DrawLine(FovPen, origin, new Point(cx - (Math.Sin(half) * 5 * scale), origin.Y - (Math.Cos(half) * 5 * scale)));
            dc.DrawLine(FovPen, origin, new Point(cx + (Math.Sin(half) * 5 * scale), origin.Y - (Math.Cos(half) * 5 * scale)));
            dc.DrawEllipse(null, new Pen(LabelBrush, 1), origin, 5, 5);
            Text(dc, "сенсор, поле зрения " + (hfovRad * 180 / Math.PI).ToString("0", CultureInfo.InvariantCulture) + " град", cx + 10, origin.Y - 18, 11, LabelBrush);
        }

        private void DrawUser(DrawingContext dc, TrackedUser user, Func<double, double, Point> map, double w, double h)
        {
            if (ShowGhost)
            {
                DrawGhost(dc, user, map);
            }

            var useZ = kind == ViewKind.Top;
            Point? Pos(RawJoint? j)
            {
                if (j == null || IsMissing(j))
                {
                    return null;
                }

                return map(j.X, useZ ? j.Z : j.Y);
            }

            RawJoint? Find(string name)
            {
                for (var i = 0; i < user.Joints.Count; i++)
                {
                    if (user.Joints[i].Name == name)
                    {
                        return user.Joints[i];
                    }
                }

                return null;
            }

            foreach (var bone in Topology.Bones(trackerId))
            {
                var ja = Find(bone[0]);
                var jb = Find(bone[1]);
                var a = Pos(ja);
                var b = Pos(jb);
                if (a == null || b == null)
                {
                    continue;
                }

                var side = Topology.SideOf(bone[1]) != Side.Center ? Topology.SideOf(bone[1]) : Topology.SideOf(bone[0]);
                var weak = IsWeak(ja!) || IsWeak(jb!);
                var color = SideColor(side);
                var pen = new Pen(new SolidColorBrush(color) { Opacity = weak ? 0.45 : 1.0 }, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (weak)
                {
                    pen.DashStyle = DashStyles.Dot;
                }

                dc.DrawLine(pen, a.Value, b.Value);
            }

            for (var i = 0; i < user.Joints.Count; i++)
            {
                var j = user.Joints[i];
                var p = Pos(j);
                if (p == null)
                {
                    continue;
                }

                var rebuilt = IsRebuilt(j);
                var brush = new SolidColorBrush(rebuilt ? Colors.Gold : SideColor(Topology.SideOf(j.Name)));
                if (rebuilt)
                {
                    dc.DrawEllipse(null, new Pen(brush, 2.5), p.Value, 6, 6);
                    dc.DrawEllipse(brush, null, p.Value, 2, 2);
                }
                else if (IsWeak(j))
                {
                    dc.DrawEllipse(null, new Pen(brush, 1.5), p.Value, 4, 4);
                }
                else
                {
                    dc.DrawEllipse(brush, null, p.Value, 4, 4);
                }
            }

            var lw = Find(trackerId == Topology.Nuitrack ? "LeftHand" : "HandLeft");
            var rw = Find(trackerId == Topology.Nuitrack ? "RightHand" : "HandRight");
            var pl = Pos(lw);
            var pr = Pos(rw);
            if (pl != null)
            {
                Text(dc, "Л", pl.Value.X + 8, pl.Value.Y - 8, 16, new SolidColorBrush(LeftColor));
            }

            if (pr != null)
            {
                Text(dc, "П", pr.Value.X + 8, pr.Value.Y - 8, 16, new SolidColorBrush(RightColor));
            }
        }

        private double FirstUserRootX()
        {
            if (frame == null || frame.Users.Count == 0)
            {
                return 0;
            }

            var root = Topology.Root(trackerId);
            foreach (var j in frame.Users[0].Joints)
            {
                if (j.Name == root && !IsMissing(j))
                {
                    return j.X;
                }
            }

            return 0;
        }

        private static readonly Color GhostColor = Color.FromRgb(0x7f, 0xd3, 0xff);

        private static double Dist(RawJoint a, RawJoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

        /// <summary>Радиус капсулы кости в метрах по имени конечного сустава и длине кости (пропорции по самой кости, не по нормативу).</summary>
        private static double LimbRadius(string endJoint, double boneLength)
        {
            if (endJoint.Contains("Elbow") || endJoint.Contains("Knee"))
            {
                return 0.17 * boneLength;
            }

            if (endJoint.Contains("Wrist"))
            {
                return 0.14 * boneLength;
            }

            if (endJoint.Contains("Ankle"))
            {
                return 0.12 * boneLength;
            }

            if (endJoint == "Hand" || endJoint.EndsWith("Hand", StringComparison.Ordinal))
            {
                return 0.045;
            }

            if (endJoint.Contains("Foot"))
            {
                return 0.04;
            }

            return endJoint == "Neck" ? 0.045 : 0;
        }

        /// <summary>Полупрозрачное тело из капсул по костям, туловище по плечам и бёдрам, голова кругом. Рисуется одним слоем с общей прозрачностью, наложения капсул не темнеют.</summary>
        private void DrawGhost(DrawingContext dc, TrackedUser user, Func<double, double, Point> map)
        {
            var byName = new Dictionary<string, RawJoint>();
            foreach (var j in user.Joints)
            {
                if (!IsMissing(j))
                {
                    byName[j.Name] = j;
                }
            }

            var top = kind == ViewKind.Top;
            Point P(RawJoint j) => map(j.X, top ? j.Z : j.Y);
            var nui = trackerId == Topology.Nuitrack;
            var brush = new SolidColorBrush(GhostColor);
            byName.TryGetValue(Topology.LeftShoulder(trackerId), out var ls);
            byName.TryGetValue(Topology.RightShoulder(trackerId), out var rs);
            byName.TryGetValue(nui ? "LeftHip" : "HipLeft", out var lh);
            byName.TryGetValue(nui ? "RightHip" : "HipRight", out var rh);
            var shoulderWidth = ls != null && rs != null ? Dist(ls, rs) : 0.35;

            dc.PushOpacity(0.4);
            if (top)
            {
                if (ls != null && rs != null)
                {
                    var a = P(ls);
                    var b = P(rs);
                    var center = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    var angle = Math.Atan2(a.Y - b.Y, a.X - b.X) * 180 / Math.PI;
                    dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
                    dc.DrawEllipse(brush, null, center, ((shoulderWidth / 2) + 0.05) * scalePx, 0.12 * scalePx);
                    dc.Pop();
                }
            }
            else
            {
                foreach (var bone in Topology.Bones(trackerId))
                {
                    if (!byName.TryGetValue(bone[0], out var ja) || !byName.TryGetValue(bone[1], out var jb))
                    {
                        continue;
                    }

                    var r = LimbRadius(bone[1], Dist(ja, jb));
                    if (r <= 0)
                    {
                        continue;
                    }

                    var pen = new Pen(brush, 2 * r * scalePx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(pen, P(ja), P(jb));
                }

                if (ls != null && rs != null && lh != null && rh != null)
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(P(ls), true, true);
                        ctx.LineTo(P(rs), true, false);
                        ctx.LineTo(P(rh), true, false);
                        ctx.LineTo(P(lh), true, false);
                    }

                    g.Freeze();
                    dc.DrawGeometry(brush, new Pen(brush, 0.12 * scalePx) { LineJoin = PenLineJoin.Round }, g);
                }

                if (byName.TryGetValue("Head", out var head))
                {
                    var c = P(head);
                    var rr = 0.28 * shoulderWidth * scalePx;
                    dc.DrawEllipse(brush, null, c, rr, rr * 1.15);
                }
            }

            dc.Pop();
        }

        private static Color SideColor(Side side) => side == Side.Left ? LeftColor : side == Side.Right ? RightColor : CenterColor;

        /// <summary>Трекер не нашёл сустав (Nuitrack отдаёт нули с уверенностью 0).</summary>
        private static bool IsMissing(RawJoint j) =>
            j.State == "not-tracked" || j.Conf == 0 || (Math.Abs(j.X) < 0.01 && Math.Abs(j.Y) < 0.01 && Math.Abs(j.Z) < 0.01);

        private static bool IsRebuilt(RawJoint j) => j.State == "reconstructed" || j.State == "extrapolated";

        private static bool IsWeak(RawJoint j) => j.State == "inferred" || IsRebuilt(j) || (j.Conf.HasValue && j.Conf.Value < 0.3);

        private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x, y));
        }

        private static T Freeze<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
