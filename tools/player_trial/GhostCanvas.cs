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

        public static GhostVec operator -(GhostVec a) => new GhostVec(-a.X, -a.Y, -a.Z);

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
    /// Сцена призраков 3D (S18): мир поворачивается вокруг вертикали на -CameraYawRad матрицей 3x3, затем камера
    /// на D метров на высоте центра сцены с перспективным делением f = D / (D + pz''); графический движок не используется.
    /// Эталон-призрак фиксированным полупрозрачным цветом и призрак пользователя с краской кости по отклонению
    /// (мёртвая зона TrialParams не красит, дальше плавно к насыщению). Тело капсулами одним проходом OnRender,
    /// радиус капсулы умножается на f (глубина). Пол и сетка — линии пола 4x4 м в той же проекции.
    /// Разметка переда/зада (S20, E7): капсула рисуется тремя слоями — тонкий нейтральный ободок базы, поверх
    /// переднее и заднее ядро, смещённые в 3D по forward торса до проекции (в профиль слои сходятся на ребро);
    /// эталон — непрозрачные янтарь/графит, пользователь — 0.75 поверх краски отклонений; торс — те же передняя
    /// и задняя панели; на голове бейсболка с козырьком вперёд, под призраком стрелка направления на полу.
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

        /// <summary>Цвета разметки переда/зада и бейсболки одного призрака (S20, E7).</summary>
        private readonly struct GhostSkin
        {
            public GhostSkin(Color front, Color back, Color rim, Color cap, Color brim,
                double frontOpacity, double backOpacity, double panelFrontOpacity, double panelBackOpacity)
            {
                Front = front;
                Back = back;
                Rim = rim;
                Cap = cap;
                Brim = brim;
                FrontOpacity = frontOpacity;
                BackOpacity = backOpacity;
                PanelFrontOpacity = panelFrontOpacity;
                PanelBackOpacity = panelBackOpacity;
            }

            /// <summary>Тёплый оттенок переднего слоя капсул и передней панели торса.</summary>
            public Color Front { get; }

            /// <summary>Холодный/тёмный оттенок заднего слоя и задней панели.</summary>
            public Color Back { get; }

            /// <summary>Нейтральный цвет тонкого ободка базовой капсулы (у пользователя поверх — краска отклонений).</summary>
            public Color Rim { get; }

            /// <summary>Цвет купола бейсболки (чуть темнее базового цвета призрака).</summary>
            public Color Cap { get; }

            /// <summary>Цвет козырька (чуть светлее купола).</summary>
            public Color Brim { get; }

            /// <summary>Непрозрачность переднего слоя капсул: у эталона непрозрачно, у пользователя поверх краски отклонений.</summary>
            public double FrontOpacity { get; }

            /// <summary>Непрозрачность заднего слоя капсул.</summary>
            public double BackOpacity { get; }

            /// <summary>Непрозрачность передней панели торса.</summary>
            public double PanelFrontOpacity { get; }

            /// <summary>Непрозрачность задней панели торса.</summary>
            public double PanelBackOpacity { get; }
        }

        /// <summary>Разметка эталона: непрозрачные слои — перед тёплый янтарный, зад глубокий сине-графитовый; ободок нейтральный (E7).</summary>
        private static readonly GhostSkin ReferenceSkin = new GhostSkin(
            Color.FromRgb(0xFF, 0xC8, 0x5C), Color.FromRgb(0x22, 0x34, 0x4A), Color.FromRgb(0x8E, 0x99, 0xA3),
            Color.FromRgb(0x50, 0x93, 0xBF), Color.FromRgb(0x71, 0xB5, 0xDD), 1.0, 1.0, 1.0, 1.0);

        /// <summary>Разметка пользователя: та же схема, слои 0.75 поверх краски отклонений, панели торса 0.75/0.7 (E7).</summary>
        private static readonly GhostSkin UserSkin = new GhostSkin(
            Color.FromRgb(0xFF, 0xB7, 0x4D), Color.FromRgb(0x37, 0x47, 0x4F), Color.FromRgb(0xE0, 0xE0, 0xE0),
            Color.FromRgb(0x96, 0x9A, 0xA4), Color.FromRgb(0xB2, 0xB6, 0xC0), 0.75, 0.75, 0.75, 0.7);

        /// <summary>Смещение слоёв переда/зада капсул в 3D: радиус кости * 0.75 — при виде в лоб/спину один слой почти полностью перекрывает другой (S20, E7).</summary>
        private const double FrontBackOffsetFrac = 0.75;

        /// <summary>Толщина слоёв переда/зада: ~85% прежней базовой капсулы — ядро поверхности (S20, E7).</summary>
        private const double FrontBackWidthFrac = 0.85;

        /// <summary>Толщина нейтрального ободка базовой капсулы: ~30% прежней базовой (E7).</summary>
        private const double RimWidthFrac = 0.3;

        /// <summary>Смещение панелей торса: доли длины ноги (0.08 * рост в долях, S20).</summary>
        private const double TorsoPanelOffsetFrac = 0.08;

        /// <summary>Вынос козырька бейсболки вперёд: HeadR * 1.6 (S20).</summary>
        private const double CapBrimForwardFrac = 1.6;

        /// <summary>Длина стрелки направления на полу: доли длины ноги (S20).</summary>
        private const double ArrowLengthFrac = 0.35;

        /// <summary>Непрозрачность стрелки на полу.</summary>
        private const double ArrowOpacity = 0.55;

        /// <summary>Угол косок наконечника стрелки от оси, радиан (~26°).</summary>
        private const double ArrowBarbAngleRad = 0.45;

        /// <summary>Расстояние камеры сцены, м (S18).</summary>
        private const double CameraDistanceM = 4.0;

        /// <summary>Высота центра сцены: камера смотрит на эту высоту, м.</summary>
        private const double SceneCenterYm = 0.9;

        /// <summary>
        /// Азимут фронта сцены в мировых осях: мир призрака = оси датасета (heading абсолютный), а фронт,
        /// откуда смотрит фронтальная камера c05, — азимут ~180° (позиция c05 из setting7_1). Смещение
        /// подобрано сравнением проекции скелета с keypoints2d: без него все ракурсы перевёрнуты спереди/сзади.
        /// </summary>
        private const double StageFrontOffsetRad = Math.PI;

        private double cameraYawRad;

        /// <summary>Азимут камеры сцены (S18): радиан относительно фронта сцены; 0 — фронтальный вид (c05).</summary>
        public double CameraYawRad
        {
            get => cameraYawRad;
            set
            {
                cameraYawRad = value;
                InvalidateVisual();
            }
        }

        private CanonicalFrame? reference;
        private CanonicalFrame? user;
        private double[]? userDeviation;
        private GhostLengths lengths = GhostLengths.FromLeg(0.8);

        public GhostCanvas()
        {
            ClipToBounds = true;
        }

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

            // масштаб и центр как в прежнем виде спереди: пол на 0.86h при yaw 0, фигура ~1.7 м на 3.2 м высоты
            var scale = h / 3.2;
            var cy = (h * 0.86) - (SceneCenterYm * scale);
            var cx = w / 2;
            DrawFloor(dc, w, h, cx, cy, scale);
            if (reference != null)
            {
                DrawGhost(dc, reference, ReferenceColor, ReferenceSkin, null, 1.0, cx, cy, scale);
            }

            if (user != null)
            {
                DrawGhost(dc, user, UserBaseColor, UserSkin, userDeviation, 0.85, cx, cy, scale);
            }
        }

        /// <summary>
        /// Проекция точки мира (S18): поворот мира на -(yaw + фронт сцены) вокруг вертикали матрицей 3x3,
        /// затем камера на D м: f = D / (D + pz''), экран x' = cx + px''·f·s, y' = cy - (py - центр)·f·s.
        /// Без зеркала: вид призрака совпадает с ракурсом видео (сверено с keypoints2d по всем камерам).
        /// </summary>
        private Point Project(double x, double y, double z, double cx, double cy, double scale)
        {
            var th = cameraYawRad + StageFrontOffsetRad;
            var c = Math.Cos(th);
            var s = Math.Sin(th);
            var px = (x * c) - (z * s);    // поворот мира на -тета
            var pz = -((x * s) + (z * c)); // глубина после поворота: вперёд от камеры — минус
            var f = CameraDistanceM / (CameraDistanceM + pz);
            return new Point(cx + (px * f * scale), cy - ((y - SceneCenterYm) * f * scale));
        }

        /// <summary>Перспективный множитель f той же проекции по (x, z) — для радиуса капсулы и толщины линий.</summary>
        private double DepthFactor(double x, double z)
        {
            var th = cameraYawRad + StageFrontOffsetRad;
            var c = Math.Cos(th);
            var s = Math.Sin(th);
            return CameraDistanceM / (CameraDistanceM - ((x * s) + (z * c)));
        }

        /// <summary>Пол и сетка в 3D-проекции (S18): линии пола 4x4 м с шагом 1 м, повёрнутые yaw, с перспективой.</summary>
        private void DrawFloor(DrawingContext dc, double w, double h, double cx, double cy, double scale)
        {
            for (var m = -2; m <= 2; m++)
            {
                if (m != 0)
                {
                    FloorLine(dc, m, -2, m, 2, GridPen, cx, cy, scale); // вдоль z на x = m
                }

                FloorLine(dc, -2, m, 2, m, GridPen, cx, cy, scale);     // вдоль x на z = m
            }

            // зелёная линия пола — ось x через центр сцены: при любом yaw f = 1, на высоте прежней линии пола
            FloorLine(dc, -2, 0, 2, 0, FloorPen, cx, cy, scale);
            var floorScreenY = cy + (SceneCenterYm * scale);
            Text(dc, "пол", 6, floorScreenY - 16, 11, FloorPen.Brush);

            // подпись панели прижата к правому верхнему углу: слева её перекрывает HUD пробы
            var deg = (cameraYawRad * 180.0 / Math.PI).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            var title = MakeText("сцена 3D (азимут " + deg + "°), сетка пола 1 м", 13, LabelBrush);
            dc.DrawText(title, new Point(w - title.Width - 8, 6));
        }

        /// <summary>Линия на полу от (x1, 0, z1) до (x2, 0, z2) в проекции сцены.</summary>
        private void FloorLine(DrawingContext dc, double x1, double z1, double x2, double z2, Pen pen, double cx, double cy, double scale)
        {
            dc.DrawLine(pen, Project(x1, 0, z1, cx, cy, scale), Project(x2, 0, z2, cx, cy, scale));
        }

        /// <summary>
        /// Прямая кинематика: корень от RootForward/RootSide/RootHeight с heading, торс по lean/turn, кости по направлениям
        /// канонической позы. Разметка переда/зада (S20): капсулы тремя слоями (база + перед/зад со смещением в 3D по
        /// fwdT до проекции, ближний слой поверх дальнего), панели торса ±fwdT*0.08*leg, бейсболка с козырьком вперёд
        /// по heading, стрелка направления на полу от проекции корня вперёд по heading.
        /// </summary>
        private void DrawGhost(DrawingContext dc, CanonicalFrame f, Color baseColor, GhostSkin skin, double[]? deviation, double opacity, double cx, double cy, double scale)
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

            // экран: 3D-проекция сцены (S18), зеркало внутри Project; F — перспективный множитель для радиусов
            Point P(GhostVec p) => Project(p.X, p.Y, p.Z, cx, cy, scale);
            double F(GhostVec p) => DepthFactor(p.X, p.Z);
            void Line(GhostVec a, GhostVec b, double widthPx, Color color, double alpha)
            {
                var brush = Fade(color, alpha);
                dc.DrawLine(new Pen(brush, widthPx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, P(a), P(b));
            }

            // капсула кости тремя слоями (S20, E7): тонкий нейтральный ободок базы (у пользователя — краска
            // отклонений), поверх зад и перед ядром ~85% прежней базовой со смещением концов в 3D по fwdT до
            // проекции; ближний к камере слой рисуется последним — сзади виден графит, спереди янтарь, в
            // профиль слои сходятся на ребро ободка
            void Capsule(int index, GhostVec a, GhostVec b, double radiusM)
            {
                if (!f.BoneValid[index])
                {
                    return;
                }

                var width = 2 * radiusM * scale * 0.5 * (F(a) + F(b));
                var paint = deviation != null ? DeviationColor(deviation[index], baseColor) : skin.Rim;
                Line(a, b, width * RimWidthFrac, paint, 1.0);
                var off = fwdT * (radiusM * FrontBackOffsetFrac);
                var layerWidth = width * FrontBackWidthFrac;
                if (F(a + off) >= F(a - off))
                {
                    Line(a - off, b - off, layerWidth, skin.Back, skin.BackOpacity);
                    Line(a + off, b + off, layerWidth, skin.Front, skin.FrontOpacity);
                }
                else
                {
                    Line(a + off, b + off, layerWidth, skin.Front, skin.FrontOpacity);
                    Line(a - off, b - off, layerWidth, skin.Back, skin.BackOpacity);
                }
            }

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

            // стрелка направления на полу (S20): от проекции корня на пол вперёд по heading, до полупрозрачной фигуры
            var rootFloor = new GhostVec(root.X, 0, root.Z);
            var arrowLen = ArrowLengthFrac * leg;
            var arrowTip = rootFloor + (fwdH * arrowLen);
            var arrowWidth = Math.Max(2.0 * F(rootFloor), 1.5);
            Line(rootFloor, arrowTip, arrowWidth, baseColor, ArrowOpacity);
            var barbLen = arrowLen * 0.3;
            Line(arrowTip, arrowTip + (RotateY(fwdH, ArrowBarbAngleRad) * (-barbLen)), arrowWidth * 0.8, baseColor, ArrowOpacity);
            Line(arrowTip, arrowTip + (RotateY(fwdH, -ArrowBarbAngleRad) * (-barbLen)), arrowWidth * 0.8, baseColor, ArrowOpacity);

            dc.PushOpacity(opacity);

            // туловище: базовый четырёхугольник с краской отклонений + панели переда/зада ±fwdT (S20)
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
            var fNeck = F(neck); // толщина торса и позвоночника по глубине шеи
            dc.DrawGeometry(torsoBrush, new Pen(torsoBrush, 0.04 * scale * fNeck) { LineJoin = PenLineJoin.Round }, Quad(P(shL), P(shR), P(hipR), P(hipL)));

            // панели торса: те же слои перед/зад, что у капсул; ближний к камере поверх
            var panelOff = fwdT * (TorsoPanelOffsetFrac * leg);
            var panelPenWidth = 0.02 * scale * fNeck;
            var frontNearer = F(neck + panelOff) >= F(neck - panelOff);
            void Panel(GhostVec off, Color color, double alpha)
            {
                var brush = Fade(color, alpha);
                dc.DrawGeometry(brush, new Pen(brush, panelPenWidth) { LineJoin = PenLineJoin.Round }, Quad(P(shL + off), P(shR + off), P(hipR + off), P(hipL + off)));
            }

            if (frontNearer)
            {
                Panel(-panelOff, skin.Back, skin.PanelBackOpacity);
                Panel(panelOff, skin.Front, skin.PanelFrontOpacity);
            }
            else
            {
                Panel(panelOff, skin.Front, skin.PanelFrontOpacity);
                Panel(-panelOff, skin.Back, skin.PanelBackOpacity);
            }

            dc.DrawLine(new Pen(torsoBrush, 0.02 * scale * fNeck) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, P(root), P(neck));

            // голова и бейсболка (S20): купол — дуга по верхней половине эллипса головы, козырёк — плоский
            // четырёхугольник от кромки купола вперёд по heading на уровне середины головы; сзади козырёк
            // за головой — рисуется до эллипса, спереди — после
            var fHead = F(headC);
            var headP = P(headC);
            var rx = l.HeadR * scale * fHead;
            var ry = l.HeadR * 1.15 * scale * fHead;
            var halfBrim = 0.95 * l.HeadR;
            var brimBaseL = headC - (leftH * halfBrim);
            var brimBaseR = headC + (leftH * halfBrim);
            var brimFwd = fwdH * (CapBrimForwardFrac * l.HeadR);
            void Brim()
            {
                dc.DrawGeometry(Fade(skin.Brim, 1.0), null, Quad(P(brimBaseL), P(brimBaseR), P(brimBaseR + brimFwd), P(brimBaseL + brimFwd)));
            }

            var brimAhead = F(headC + (brimFwd * 0.5)) > fHead;
            if (!brimAhead)
            {
                Brim();
            }

            dc.DrawEllipse(torsoBrush, null, headP, rx, ry);
            if (brimAhead)
            {
                Brim();
            }

            var dome = new StreamGeometry();
            using (var ctx = dome.Open())
            {
                ctx.BeginFigure(new Point(headP.X - rx, headP.Y), false, false);
                ctx.ArcTo(new Point(headP.X + rx, headP.Y), new Size(rx, ry), 0, false, SweepDirection.Clockwise, true, false);
            }

            dome.Freeze();
            dc.DrawGeometry(null, new Pen(Fade(skin.Cap, 1.0), Math.Max(rx * 0.18, 1.5)) { LineJoin = PenLineJoin.Round }, dome);

            // капсулы по костям: индекс отклонения = порядок CanonicalFrame.BoneNames
            Capsule(0, shL, elbowL, 0.17 * l.UpperArm);
            Capsule(1, shR, elbowR, 0.17 * l.UpperArm);
            Capsule(2, elbowL, wristL, 0.14 * l.LowerArm);
            Capsule(3, elbowR, wristR, 0.14 * l.LowerArm);
            Capsule(4, hipL, kneeL, 0.17 * l.Thigh);
            Capsule(5, hipR, kneeR, 0.17 * l.Thigh);
            Capsule(6, kneeL, ankleL, 0.12 * l.Shin);
            Capsule(7, kneeR, ankleR, 0.12 * l.Shin);

            dc.Pop();
        }

        /// <summary>Четырёхугольник по четырём экранным точкам (торс, панели, козырёк).</summary>
        private static StreamGeometry Quad(Point a, Point b, Point c, Point d)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(a, true, true);
                ctx.LineTo(b, true, false);
                ctx.LineTo(c, true, false);
                ctx.LineTo(d, true, false);
            }

            return g;
        }

        /// <summary>Кисть цвета с альфой: полупрозрачные слои разметки поверх базы (S20).</summary>
        private static SolidColorBrush Fade(Color color, double alpha)
        {
            var a = Math.Max(0, Math.Min(1, alpha));
            return new SolidColorBrush(a >= 0.999 ? color : Color.FromArgb((byte)Math.Round(255 * a), color.R, color.G, color.B));
        }

        /// <summary>Поворот горизонтального вектора вокруг вертикали (коски стрелки на полу).</summary>
        private static GhostVec RotateY(GhostVec v, double angleRad)
        {
            var c = Math.Cos(angleRad);
            var s = Math.Sin(angleRad);
            return new GhostVec((v.X * c) + (v.Z * s), 0, (-(v.X * s)) + (v.Z * c));
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
