using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using DanceLab.Core.Canonical;
using DanceLab.View;

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

        /// <summary>Рост фигуры призрака в метрах.</summary>
        public double HeightM => Thigh + Shin + TorsoLen + NeckToHead + (2 * HeadR);

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
    /// (мёртвая зона TrialParams не красит, дальше плавно к насыщению). Тело капсулами-конусами одним проходом
    /// OnRender (S43), радиус конца умножается на f (глубина). Пол и сетка — линии пола 4x4 м в той же проекции.
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

        /// <summary>Подложка подписей сцены (S41): цвет фона #0A0B0E с непрозрачностью 0xB0 (приём из SceneCanvas).</summary>
        private static readonly Brush LabelPillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x0A, 0x0B, 0x0E)));

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

            /// <summary>Цвет тонкого ободка базовой капсулы: у эталона — цвет зада (E53), у пользователя — светлый, поверх краска отклонений.</summary>
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

        /// <summary>Разметка эталона: непрозрачные слои — перед тёплый янтарный, зад глубокий сине-графитовый; ободок = цвет зада, чтобы серпики слоёв на сгибах сливались с задом (E7, E53).</summary>
        private static readonly GhostSkin ReferenceSkin = new GhostSkin(
            Color.FromRgb(0xFF, 0xC8, 0x5C), Color.FromRgb(0x22, 0x34, 0x4A), Color.FromRgb(0x22, 0x34, 0x4A),
            Color.FromRgb(0x50, 0x93, 0xBF), Color.FromRgb(0x71, 0xB5, 0xDD), 1.0, 1.0, 1.0, 1.0);

        /// <summary>Разметка пользователя: та же схема, слои 0.75 поверх краски отклонений, панели торса 0.75/0.7 (E7).</summary>
        private static readonly GhostSkin UserSkin = new GhostSkin(
            Color.FromRgb(0xFF, 0xB7, 0x4D), Color.FromRgb(0x37, 0x47, 0x4F), Color.FromRgb(0xE0, 0xE0, 0xE0),
            Color.FromRgb(0x96, 0x9A, 0xA4), Color.FromRgb(0xB2, 0xB6, 0xC0), 0.75, 0.75, 0.75, 0.7);

        /// <summary>Смещение слоёв переда/зада капсул в 3D: радиус кости * 0.75 — при виде в лоб/спину один слой почти полностью перекрывает другой (S20, E7).</summary>
        private const double FrontBackOffsetFrac = 0.75;

        /// <summary>Толщина слоёв переда/зада: ~85% прежней базовой капсулы — ядро поверхности (S20, E7).</summary>
        private const double FrontBackWidthFrac = 0.85;

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

        /// <summary>Цель камеры сцены в мире, м (S26): из SceneFollower при framed, иначе (0, 0); точка (targetXm, 0, targetZm) проецируется в (cx, FloorScreenY).</summary>
        private double targetXm, targetZm;

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

        /// <summary>Следящий кадр сцены (S26, T24): масштаб, линия пола и цель камеры из рамки танцора на видео.</summary>
        private readonly SceneFollower sceneFollower = new SceneFollower();

        /// <summary>Последний кадр сцены от SceneFollower.</summary>
        private SceneFollower.Frame sceneFrame;

        /// <summary>Кадр сцены ведётся SceneFollower (рамка видео пришла хотя бы раз).</summary>
        private bool framed;

        /// <summary>Последний известный корень эталона в мире (цель камеры), старт (0, 0, 0).</summary>
        private GhostVec lastRoot;

        /// <summary>Поправка высоты корня эталона, доли длины ноги (S32).</summary>
        private double groundShiftU;

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

        /// <summary>
        /// Кадр сцены из рамки танцора на видео (S26): hasBox — есть ли рамка (ScreenBox.IsValid);
        /// heightPx, bottomPx, centerXpx — рамка танцора в пикселях панели видео; panelW, panelH — размер
        /// этой панели; sliderZoom — множитель слайдера «Кадр» (нужен только запасному широкому виду).
        /// Призрак получает тот же размер на экране, что танцор, стоит на той же линии ног, камера следует
        /// за корнем эталона.
        /// </summary>
        public void SetVideoBox(bool hasBox, double heightPx, double bottomPx, double centerXpx, double panelW, double panelH, double sliderZoom)
        {
            if (reference != null)
            {
                lastRoot = RootOf(reference, lengths);
            }

            sceneFrame = sceneFollower.Update(hasBox, heightPx, bottomPx, centerXpx, lengths.HeightM, panelW, panelH, sliderZoom, lastRoot.X, lastRoot.Z);
            framed = true;
            InvalidateVisual();
        }

        /// <summary>Задать поправку высоты корня эталона (S32), доли длины ноги.</summary>
        public void SetGroundShift(double legUnits)
        {
            groundShiftU = legUnits;
            InvalidateVisual();
        }

        /// <summary>Сброс кадра сцены для нового прогона сглаживания: тур (S34) пересоздаёт следящий кадр и прокручивает сглаживание заново.</summary>
        public void ResetFrame()
        {
            framed = false;
            sceneFollower.Reset();
        }

        /// <summary>
        /// Габарит эталон-призрака в пикселях панели сцены по той же проекции, что OnRender (S34, tour.tsv ghost*):
        /// голова с куполом бейсболки (радиус 1.06) и козырьком, концы костей, расширенные на радиусы
        /// конусов в суставах (S43). Эталона нет — Rect.Empty.
        /// </summary>
        public Rect ReferenceBounds()
        {
            if (reference == null)
            {
                return Rect.Empty;
            }

            var rf = reference;
            UpdateCameraTarget();

            // масштаб и центр — те же формулы кадра сцены, что в OnRender (S26), с той же защитой масштаба
            var scale = sceneFrame.ScalePxPerM;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                scale = ActualHeight / 3.2;
            }

            var cx = sceneFrame.CenterScreenX;
            var cy = sceneFrame.FloorScreenY - (SceneCenterYm * scale);

            var l = lengths;
            var j = Solve(rf, l, -groundShiftU);
            Point P(GhostVec p) => Project(p.X, p.Y, p.Z, cx, cy, scale);
            double F(GhostVec p) => DepthFactor(p.X, p.Z);

            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            void Take(Point p)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            // голова: центр, расширенный на купольные радиусы (купол 1.06 накрывает эллипс головы 1.0)
            var fHead = F(j.HeadC);
            var headP = P(j.HeadC);
            var rx = l.HeadR * scale * fHead * 1.06;
            var ry = l.HeadR * 1.15 * scale * fHead * 1.06;
            Take(new Point(headP.X - rx, headP.Y - ry));
            Take(new Point(headP.X + rx, headP.Y + ry));

            // козырёк бейсболки: та же геометрия, что в DrawGhost (S20, S27)
            var tilt = 22 * Math.PI / 180;
            var fv = (j.FwdH * Math.Cos(tilt)) - (new GhostVec(0, 1, 0) * Math.Sin(tilt));
            var brimBaseL = j.HeadC - (j.LeftH * (0.85 * l.HeadR)) + (new GhostVec(0, 1, 0) * (0.2 * l.HeadR));
            var brimBaseR = j.HeadC + (j.LeftH * (0.85 * l.HeadR)) + (new GhostVec(0, 1, 0) * (0.2 * l.HeadR));
            var brimTipL = brimBaseL + (fv * (CapBrimForwardFrac * l.HeadR)) + (j.LeftH * (0.55 * l.HeadR));
            var brimTipR = brimBaseR + (fv * (CapBrimForwardFrac * l.HeadR)) - (j.LeftH * (0.55 * l.HeadR));
            Take(P(brimBaseL));
            Take(P(brimBaseR));
            Take(P(brimTipL));
            Take(P(brimTipR));

            // кость: оба конца валидной кости, расширенные на радиус конуса сустава в пикселях
            // (те же радиусы проксимального и дистального концов, что Capsule, S43)
            void Bone(int index, GhostVec a, GhostVec b, double radiusA, double radiusB)
            {
                if (!rf.BoneValid[index])
                {
                    return;
                }

                var pa = P(a);
                var ra = radiusA * scale * F(a);
                Take(new Point(pa.X - ra, pa.Y - ra));
                Take(new Point(pa.X + ra, pa.Y + ra));
                var pb = P(b);
                var rb = radiusB * scale * F(b);
                Take(new Point(pb.X - rb, pb.Y - rb));
                Take(new Point(pb.X + rb, pb.Y + rb));
            }

            var rHip = 0.17 * l.Thigh;
            var rKnee = 0.5 * ((0.17 * l.Thigh) + (0.12 * l.Shin));
            var rAnkle = 0.12 * l.Shin;
            var rSh = 0.17 * l.UpperArm;
            var rElbow = 0.5 * ((0.17 * l.UpperArm) + (0.14 * l.LowerArm));
            var rWrist = 0.14 * l.LowerArm;
            Bone(0, j.ShL, j.ElbowL, rSh, rElbow);
            Bone(1, j.ShR, j.ElbowR, rSh, rElbow);
            Bone(2, j.ElbowL, j.WristL, rElbow, rWrist);
            Bone(3, j.ElbowR, j.WristR, rElbow, rWrist);
            Bone(4, j.HipL, j.KneeL, rHip, rKnee);
            Bone(5, j.HipR, j.KneeR, rHip, rKnee);
            Bone(6, j.KneeL, j.AnkleL, rKnee, rAnkle);
            Bone(7, j.KneeR, j.AnkleR, rKnee, rAnkle);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Экранная точка корня эталон-призрака: проекция мировой точки (Root.X, Root.Y, Root.Z) из
        /// Solve(reference, lengths, −groundShiftU) той же проекцией, что ReferenceBounds — те же
        /// масштаб, центр и цель камеры. Эталона нет — null.
        /// </summary>
        private Point? ReferenceRootPoint()
        {
            if (reference == null)
            {
                return null;
            }

            var rf = reference;
            UpdateCameraTarget();

            // масштаб и центр — те же формулы кадра сцены, что в OnRender и ReferenceBounds (S26)
            var scale = sceneFrame.ScalePxPerM;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                scale = ActualHeight / 3.2;
            }

            var cx = sceneFrame.CenterScreenX;
            var cy = sceneFrame.FloorScreenY - (SceneCenterYm * scale);

            var j = Solve(rf, lengths, -groundShiftU);
            return Project(j.Root.X, j.Root.Y, j.Root.Z, cx, cy, scale);
        }

        /// <summary>
        /// Экранная X-координата проекции корня эталон-призрака для отчёта тура (S34, tour.tsv ghostRootDx):
        /// точка мира (Root.X, Root.Y, Root.Z) из Solve(reference, lengths, −groundShiftU) той же проекцией,
        /// что ReferenceBounds (те же масштаб, центр и цель камеры). Эталона нет — NaN.
        /// </summary>
        public double ReferenceRootX()
        {
            var p = ReferenceRootPoint();
            return p.HasValue ? p.Value.X : double.NaN;
        }

        /// <summary>
        /// Экранная Y-координата проекции корня эталон-призрака для отчёта тура (S34): та же точка мира
        /// и та же проекция, что ReferenceRootX. Эталона нет — NaN.
        /// </summary>
        public double ReferenceRootY()
        {
            var p = ReferenceRootPoint();
            return p.HasValue ? p.Value.Y : double.NaN;
        }

        /// <summary>
        /// Насколько стопы прямой кинематики эталона висят над полом (S32), доли длины ноги: для каждого кадра трека
        /// берётся низ нижней стопы (лодыжка минус радиус капсулы голени 0.12 * Shin) при единичной длине ноги,
        /// результат — 10-й перцентиль по треку (самые нижние кадры: танцор стоит на полу). Отрицательное значение
        /// означает, что стопы уходят под пол. 0, если в треке нет кадров с валидными ногами.
        /// </summary>
        public static double ComputeGroundShift(CanonicalTrack track)
        {
            if (track == null)
            {
                return 0;
            }

            var unit = GhostLengths.FromLeg(1.0);
            var lows = new List<double>();
            foreach (var f in track.Frames)
            {
                var legL = f.BoneValid[4] && f.BoneValid[6];
                var legR = f.BoneValid[5] && f.BoneValid[7];
                if (!legL && !legR)
                {
                    continue;
                }

                var j = Solve(f, unit, 0.0);
                var low = legL && legR ? Math.Min(j.AnkleL.Y, j.AnkleR.Y) : legL ? j.AnkleL.Y : j.AnkleR.Y;
                lows.Add(low - (0.12 * unit.Shin));
            }

            if (lows.Count == 0)
            {
                return 0;
            }

            lows.Sort();
            return lows[(int)Math.Floor(0.10 * (lows.Count - 1))];
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;

            // до первого тика с рамкой (S26) запасной широкий вид тоже ведёт SceneFollower:
            // рендер всегда идёт по одним формулам кадра
            if (!framed)
            {
                SetVideoBox(false, 0, 0, 0, w, h, 1.0);
            }

            // цель камеры (S26): следование за корнем эталона из SceneFollower
            UpdateCameraTarget();

            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            if (w < 50 || h < 50)
            {
                return;
            }

            // масштаб и центр от SceneFollower (S26): рамка танцора на видео, до неё — запасной
            // широкий вид; точка (targetXm, 0, targetZm) проецируется в (cx, FloorScreenY)
            var scale = sceneFrame.ScalePxPerM;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                scale = h / 3.2;
            }

            var cx = sceneFrame.CenterScreenX;
            var cy = sceneFrame.FloorScreenY - (SceneCenterYm * scale);

            DrawFloor(dc, w, h, cx, cy, scale);
            if (reference != null)
            {
                DrawGhost(dc, reference, ReferenceColor, ReferenceSkin, null, 1.0, cx, cy, scale, -groundShiftU);
            }

            if (user != null)
            {
                DrawGhost(dc, user, UserBaseColor, UserSkin, userDeviation, 0.85, cx, cy, scale, 0.0);
            }

            // подписи на подложках поверх призраков (S41): призрак проходит под ними
            DrawLabels(dc, w, cx, cy, scale);
        }

        /// <summary>Цель камеры сцены из последнего кадра SceneFollower (S26) — общая для OnRender и ReferenceBounds.</summary>
        private void UpdateCameraTarget()
        {
            targetXm = sceneFrame.TargetXm;
            targetZm = sceneFrame.TargetZm;
        }

        /// <summary>
        /// Проекция точки мира (S18) относительно цели камеры (S26): сначала вычитается (targetXm, targetZm),
        /// затем поворот мира на -(yaw + фронт сцены) вокруг вертикали матрицей 3x3, камера на D м:
        /// f = D / (D + pz''), экран x' = cx + px''·f·s, y' = cy - (py - центр)·f·s.
        /// Без зеркала: вид призрака совпадает с ракурсом видео (сверено с keypoints2d по всем камерам).
        /// </summary>
        private Point Project(double x, double y, double z, double cx, double cy, double scale)
        {
            x -= targetXm;
            z -= targetZm;
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
            x -= targetXm;
            z -= targetZm;
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

            // зелёная линия пола поперёк взгляда камеры (S40): направление экранной горизонтали в мире
            // H = (cos θ, 0, −sin θ), θ = yaw + фронт сцены; от target − 4·H до target + 4·H линия
            // перпендикулярна взгляду, лежит на одной глубине (f = 1 на обоих концах), на экране
            // горизонтальна и не уходит за камеру; сетка вокруг мирового начала не двигается
            var th = cameraYawRad + StageFrontOffsetRad;
            var hx = Math.Cos(th);
            var hz = -Math.Sin(th);
            FloorLine(dc, targetXm - (4 * hx), targetZm - (4 * hz), targetXm + (4 * hx), targetZm + (4 * hz), FloorPen, cx, cy, scale);
        }

        /// <summary>
        /// Подписи сцены на подложках (S41), рисуются после призраков, чтобы те проходили под ними:
        /// «пол» у левого конца линии пола и заголовок панели у правого верхнего угла.
        /// </summary>
        private void DrawLabels(DrawingContext dc, double w, double cx, double cy, double scale)
        {
            // подпись «пол» прижата к левому краю панели на подложке (S40, S41, E53): линия пола
            // длиной 8 м уходит за край, левый конец вне панели; линия на экране горизонтальна
            // (на обоих концах f = 1), поэтому высота подписи — Y проекции точки цели;
            // текст на x = 12, чтобы подложка (левое поле 8) начиналась в 4 px от края и не обрезалась
            var floorY = Project(targetXm, 0, targetZm, cx, cy, scale).Y;
            TextOnPill(dc, MakeText("пол", 11, FloorPen.Brush), 12, floorY - 16);

            // подпись панели прижата к правому верхнему углу: слева её перекрывает HUD пробы;
            // позиция текста сохранена, подложка с полями 8 и 4 рисуется вокруг него
            var deg = (cameraYawRad * 180.0 / Math.PI).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            var title = MakeText("сцена 3D (азимут " + deg + "°), сетка пола 1 м", 13, LabelBrush);
            TextOnPill(dc, title, w - title.Width - 8, 6);
        }

        /// <summary>Линия на полу от (x1, 0, z1) до (x2, 0, z2) в проекции сцены.</summary>
        private void FloorLine(DrawingContext dc, double x1, double z1, double x2, double z2, Pen pen, double cx, double cy, double scale)
        {
            dc.DrawLine(pen, Project(x1, 0, z1, cx, cy, scale), Project(x2, 0, z2, cx, cy, scale));
        }

        /// <summary>Горизонтальный корень позы в мире: heading-фрейм на RootForward/RootSide (высота добавляется отдельно в Solve).</summary>
        private static GhostVec RootOf(CanonicalFrame f, GhostLengths len)
        {
            var leg = Math.Max(len.LegLength, 0.1);
            var fwdH = new GhostVec(Math.Sin(f.HeadingRad), 0, Math.Cos(f.HeadingRad));
            var leftH = new GhostVec(fwdH.Z, 0, -fwdH.X);
            return (fwdH * (f.RootForward * leg)) + (leftH * (f.RootSide * leg));
        }

        /// <summary>Суставы фигуры призрака в метрах (S20, S32): мировой фрейм и вся цепочка от корня до стоп.</summary>
        private readonly struct GhostJoints
        {
            public GhostJoints(GhostVec fwdH, GhostVec leftH, GhostVec up, GhostVec side, GhostVec fwdT, GhostVec root,
                    GhostVec hipL, GhostVec hipR, GhostVec neck, GhostVec shL, GhostVec shR,
                    GhostVec elbowL, GhostVec elbowR, GhostVec wristL, GhostVec wristR,
                    GhostVec kneeL, GhostVec kneeR, GhostVec ankleL, GhostVec ankleR, GhostVec headC)
            {
                FwdH = fwdH;
                LeftH = leftH;
                Up = up;
                Side = side;
                FwdT = fwdT;
                Root = root;
                HipL = hipL;
                HipR = hipR;
                Neck = neck;
                ShL = shL;
                ShR = shR;
                ElbowL = elbowL;
                ElbowR = elbowR;
                WristL = wristL;
                WristR = wristR;
                KneeL = kneeL;
                KneeR = kneeR;
                AnkleL = ankleL;
                AnkleR = ankleR;
                HeadC = headC;
            }

            public readonly GhostVec FwdH, LeftH, Up, Side, FwdT, Root, HipL, HipR, Neck, ShL, ShR,
                ElbowL, ElbowR, WristL, WristR, KneeL, KneeR, AnkleL, AnkleR, HeadC;
        }

        /// <summary>
        /// Прямая кинематика позы: оси торса берутся из CanonicalFrame.TorsoFrame() (формат версии 2, S39),
        /// кости восстанавливаются в системе таза leftT·x + up·y + fwdPelvis·z, в которой они записаны;
        /// бёдра откладываются по оси таза leftT, плечи по линии плеч side. rootShiftU — поправка высоты
        /// корня в долях длины ноги (0 у пользователя).
        /// </summary>
        private static GhostJoints Solve(CanonicalFrame f, GhostLengths len, double rootShiftU)
        {
            var leg = Math.Max(len.LegLength, 0.1);

            // мировой фрейм: heading — азимут взгляда; горизонтальные оси — для бейсболки и стрелки (E7)
            var fwdH = new GhostVec(Math.Sin(f.HeadingRad), 0, Math.Cos(f.HeadingRad));
            var leftH = new GhostVec(fwdH.Z, 0, -fwdH.X);

            // оси торса из CanonicalFrame.TorsoFrame() (S39): leftT — тазовая ось (линия бёдер),
            // fwdPelvis — передняя ось системы таза, в которой записаны кости
            var tf = f.TorsoFrame();
            var up = new GhostVec(tf.Up.X, tf.Up.Y, tf.Up.Z);
            var leftT = new GhostVec(tf.Left.X, tf.Left.Y, tf.Left.Z);
            var fwdPelvis = new GhostVec(tf.Forward.X, tf.Forward.Y, tf.Forward.Z);

            // линия плеч: скрутка turn вокруг оси вверх торса от тазовой оси; fwdT — передняя ось груди,
            // остаётся для разметки переда/зада капсул, панелей торса и бейсболки
            var turn = f.TorsoTurnRad;
            var sideRaw = (leftT * Math.Cos(turn)) + (fwdPelvis * Math.Sin(turn));
            var side = sideRaw.Orthogonalized(up);
            var fwdT = GhostVec.Cross(side, up);

            GhostVec Bone(int i)
            {
                var v = new GhostVec(f.Bones[i * 3], f.Bones[(i * 3) + 1], f.Bones[(i * 3) + 2]);
                return (v.X * leftT) + (v.Y * up) + (v.Z * fwdPelvis);
            }

            var root = RootOf(f, len) + (new GhostVec(0, (f.RootHeight + rootShiftU) * leg, 0));

            var hipL = root + (leftT * len.HalfHip);
            var hipR = root - (leftT * len.HalfHip);
            var neck = root + (up * len.TorsoLen);
            var shL = neck + (side * len.HalfShoulder);
            var shR = neck - (side * len.HalfShoulder);
            var elbowL = shL + (Bone(0) * len.UpperArm);
            var elbowR = shR + (Bone(1) * len.UpperArm);
            var wristL = elbowL + (Bone(2) * len.LowerArm);
            var wristR = elbowR + (Bone(3) * len.LowerArm);
            var kneeL = hipL + (Bone(4) * len.Thigh);
            var kneeR = hipR + (Bone(5) * len.Thigh);
            var ankleL = kneeL + (Bone(6) * len.Shin);
            var ankleR = kneeR + (Bone(7) * len.Shin);
            var headC = neck + (up * len.NeckToHead);

            return new GhostJoints(fwdH, leftH, up, side, fwdT, root, hipL, hipR, neck, shL, shR,
                    elbowL, elbowR, wristL, wristR, kneeL, kneeR, ankleL, ankleR, headC);
        }

        /// <summary>
        /// Прямая кинематика: корень от RootForward/RootSide/RootHeight с heading, торс по lean/turn, кости по направлениям
        /// канонической позы. Разметка переда/зада (S20): конечности — конусы с закруглёнными концами тремя слоями
        /// (ободок + перед/зад со смещением в 3D по fwdT до проекции, ближний слой поверх дальнего), панели торса
        /// ±fwdT*0.08*leg, бейсболка с козырьком вперёд по heading, стрелка направления на полу от проекции корня
        /// вперёд по heading.
        /// rootShiftU — сдвиг корня по вертикали в долях длины ноги (S32: эталон опускается на пол, у пользователя 0).
        /// </summary>
        private void DrawGhost(DrawingContext dc, CanonicalFrame f, Color baseColor, GhostSkin skin, double[]? deviation, double opacity, double cx, double cy, double scale, double rootShiftU)
        {
            var l = lengths;
            var j = Solve(f, l, rootShiftU);
            var leg = Math.Max(l.LegLength, 0.1);

            // экран: 3D-проекция сцены (S18), зеркало внутри Project; F — перспективный множитель для радиусов
            Point P(GhostVec p) => Project(p.X, p.Y, p.Z, cx, cy, scale);
            double F(GhostVec p) => DepthFactor(p.X, p.Z);
            void Line(GhostVec a, GhostVec b, double widthPx, Color color, double alpha)
            {
                var brush = Fade(color, alpha);
                dc.DrawLine(new Pen(brush, widthPx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, P(a), P(b));
            }

            // капсула конечности — конус с закруглёнными концами (S43): пиксельный радиус каждого конца =
            // радиус сустава в метрах × scale × глубина конца; три слоя одной формы (S20, E7): тонкий
            // ободок базы (у эталона — цвет зада, у пользователя — краска отклонений), поверх зад и перед ядром ~85%
            // со смещением концов в 3D по fwdT до проекции; ближний к камере слой рисуется последним —
            // сзади виден графит, спереди янтарь, в профиль слои сходятся на ребро ободка
            void Capsule(int index, GhostVec a, GhostVec b, double radiusA, double radiusB)
            {
                if (!f.BoneValid[index])
                {
                    return;
                }

                void Cone(GhostVec from, GhostVec to, double raPx, double rbPx, Color color, double alpha)
                {
                    dc.DrawGeometry(Fade(color, alpha), null, ConeGeometry(P(from), P(to), raPx, rbPx));
                }

                var raPx = radiusA * scale * F(a);
                var rbPx = radiusB * scale * F(b);
                var paint = deviation != null ? DeviationColor(deviation[index], baseColor) : skin.Rim;
                Cone(a, b, raPx, rbPx, paint, 1.0);
                var offA = j.FwdT * (radiusA * FrontBackOffsetFrac);
                var offB = j.FwdT * (radiusB * FrontBackOffsetFrac);
                var layerRa = raPx * FrontBackWidthFrac;
                var layerRb = rbPx * FrontBackWidthFrac;
                if (F(a + offA) >= F(a - offA))
                {
                    Cone(a - offA, b - offB, layerRa, layerRb, skin.Back, skin.BackOpacity);
                    Cone(a + offA, b + offB, layerRa, layerRb, skin.Front, skin.FrontOpacity);
                }
                else
                {
                    Cone(a + offA, b + offB, layerRa, layerRb, skin.Front, skin.FrontOpacity);
                    Cone(a - offA, b - offB, layerRa, layerRb, skin.Back, skin.BackOpacity);
                }
            }

            // стрелка направления на полу (S20): от проекции корня на пол вперёд по heading, до полупрозрачной фигуры
            var rootFloor = new GhostVec(j.Root.X, 0, j.Root.Z);
            var arrowLen = ArrowLengthFrac * leg;
            var arrowTip = rootFloor + (j.FwdH * arrowLen);
            var arrowWidth = Math.Max(2.0 * F(rootFloor), 1.5);
            Line(rootFloor, arrowTip, arrowWidth, baseColor, ArrowOpacity);
            var barbLen = arrowLen * 0.3;
            Line(arrowTip, arrowTip + (RotateY(j.FwdH, ArrowBarbAngleRad) * (-barbLen)), arrowWidth * 0.8, baseColor, ArrowOpacity);
            Line(arrowTip, arrowTip + (RotateY(j.FwdH, -ArrowBarbAngleRad) * (-barbLen)), arrowWidth * 0.8, baseColor, ArrowOpacity);

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
            var fNeck = F(j.Neck); // толщина торса и позвоночника по глубине шеи
            dc.DrawGeometry(torsoBrush, new Pen(torsoBrush, 0.04 * scale * fNeck) { LineJoin = PenLineJoin.Round }, Quad(P(j.ShL), P(j.ShR), P(j.HipR), P(j.HipL)));

            // панели торса: те же слои перед/зад, что у капсул; ближний к камере поверх
            var panelOff = j.FwdT * (TorsoPanelOffsetFrac * leg);
            var panelPenWidth = 0.02 * scale * fNeck;
            var frontNearer = F(j.Neck + panelOff) >= F(j.Neck - panelOff);
            void Panel(GhostVec off, Color color, double alpha)
            {
                var brush = Fade(color, alpha);
                dc.DrawGeometry(brush, new Pen(brush, panelPenWidth) { LineJoin = PenLineJoin.Round }, Quad(P(j.ShL + off), P(j.ShR + off), P(j.HipR + off), P(j.HipL + off)));
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

            dc.DrawLine(new Pen(torsoBrush, 0.02 * scale * fNeck) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, P(j.Root), P(j.Neck));

            // голова и бейсболка (S20, S27): купол — верх эллипса головы над хордой, увеличен на 6%,
            // сплошная заливка Cap — виден спереди, сзади и сбоку; козырёк наклонён вниз на 22° и
            // торчит вперёд по heading — маркер переда фигуры. Порядок: сзади козырёк за головой —
            // рисуется до эллипса, спереди — после эллипса и до купола
            var fHead = F(j.HeadC);
            var headP = P(j.HeadC);
            var rx = l.HeadR * scale * fHead;
            var ry = l.HeadR * 1.15 * scale * fHead;
            var capK = 0.25; // хорда купола на 0.25*ry выше центра эллипса головы
            var capRx = rx * 1.06;
            var capRy = ry * 1.06;
            var capHalfW = capRx * Math.Sqrt(1 - (capK * capK));
            var capChordY = headP.Y - (capK * capRy);
            var tilt = 22 * Math.PI / 180; // козырёк наклонён вниз (S27)
            var fv = (j.FwdH * Math.Cos(tilt)) - (new GhostVec(0, 1, 0) * Math.Sin(tilt));
            var brimBaseL = j.HeadC - (j.LeftH * (0.85 * l.HeadR)) + (new GhostVec(0, 1, 0) * (0.2 * l.HeadR));
            var brimBaseR = j.HeadC + (j.LeftH * (0.85 * l.HeadR)) + (new GhostVec(0, 1, 0) * (0.2 * l.HeadR));
            var brimTipL = brimBaseL + (fv * (CapBrimForwardFrac * l.HeadR)) + (j.LeftH * (0.55 * l.HeadR));
            var brimTipR = brimBaseR + (fv * (CapBrimForwardFrac * l.HeadR)) - (j.LeftH * (0.55 * l.HeadR));
            void Brim()
            {
                dc.DrawGeometry(Fade(skin.Brim, 1.0), null, Quad(P(brimBaseL), P(brimBaseR), P(brimTipR), P(brimTipL)));
            }

            var brimAhead = F(j.HeadC + (fv * (CapBrimForwardFrac * l.HeadR * 0.5))) > fHead;
            if (!brimAhead)
            {
                Brim();
            }

            dc.DrawEllipse(torsoBrush, null, headP, rx, ry);
            if (brimAhead)
            {
                Brim();
            }

            var crown = new StreamGeometry();
            using (var ctx = crown.Open())
            {
                ctx.BeginFigure(new Point(headP.X - capHalfW, capChordY), true, true);
                ctx.ArcTo(new Point(headP.X + capHalfW, capChordY), new Size(capRx, capRy), 0, false, SweepDirection.Clockwise, true, false);
            }

            crown.Freeze();
            dc.DrawGeometry(Fade(skin.Cap, 1.0), null, crown);

            // капсулы по костям: индекс отклонения = порядок CanonicalFrame.BoneNames; радиусы суставов
            // (S43): соседние сегменты делят радиус в общем суставе (колено и локоть — среднее соседних)
            var rHip = 0.17 * l.Thigh;
            var rKnee = 0.5 * ((0.17 * l.Thigh) + (0.12 * l.Shin));
            var rAnkle = 0.12 * l.Shin;
            var rSh = 0.17 * l.UpperArm;
            var rElbow = 0.5 * ((0.17 * l.UpperArm) + (0.14 * l.LowerArm));
            var rWrist = 0.14 * l.LowerArm;
            Capsule(0, j.ShL, j.ElbowL, rSh, rElbow);
            Capsule(1, j.ShR, j.ElbowR, rSh, rElbow);
            Capsule(2, j.ElbowL, j.WristL, rElbow, rWrist);
            Capsule(3, j.ElbowR, j.WristR, rElbow, rWrist);
            Capsule(4, j.HipL, j.KneeL, rHip, rKnee);
            Capsule(5, j.HipR, j.KneeR, rHip, rKnee);
            Capsule(6, j.KneeL, j.AnkleL, rKnee, rAnkle);
            Capsule(7, j.KneeR, j.AnkleR, rKnee, rAnkle);

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

        /// <summary>
        /// Конус с закруглёнными концами (S43): замкнутая фигура по двум экранным точкам и пиксельным
        /// радиусам концов (ra у точки a, rb у точки b; rb может равняться ra). Боковые стороны — внешние
        /// касательные окружностей концов (угол alpha = Asin((ra − rb)/d) от перпендикуляра к оси), торцы —
        /// дуги радиусов rb и ra. Вырожденный случай (концы совпали) — эллипс радиуса max(ra, rb) вокруг a.
        /// </summary>
        private static StreamGeometry ConeGeometry(Point a, Point b, double ra, double rb)
        {
            var g = new StreamGeometry();
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var d = Math.Sqrt((dx * dx) + (dy * dy));
            using (var ctx = g.Open())
            {
                if (d < 1e-6)
                {
                    var r = Math.Max(ra, rb);
                    ctx.BeginFigure(new Point(a.X - r, a.Y), true, false);
                    ctx.ArcTo(new Point(a.X + r, a.Y), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                    ctx.ArcTo(new Point(a.X - r, a.Y), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                }
                else
                {
                    var ux = dx / d;
                    var uy = dy / d;
                    var nx = -uy;
                    var ny = ux;
                    var alpha = Math.Asin(Math.Max(-1.0, Math.Min(1.0, (ra - rb) / d)));
                    var ca = Math.Cos(alpha);
                    var sa = Math.Sin(alpha);
                    var wx = (nx * ca) + (ux * sa); // w = n·cos(alpha) + u·sin(alpha)
                    var wy = (ny * ca) + (uy * sa);
                    var t1a = new Point(a.X + (ra * wx), a.Y + (ra * wy));
                    var t1b = new Point(b.X + (rb * wx), b.Y + (rb * wy));
                    var t2b = new Point(b.X - (rb * wx), b.Y - (rb * wy));
                    var t2a = new Point(a.X - (ra * wx), a.Y - (ra * wy));
                    ctx.BeginFigure(t1a, true, false);
                    ctx.LineTo(t1b, true, false);
                    ctx.ArcTo(t2b, new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, false);
                    ctx.LineTo(t2a, true, false);
                    ctx.ArcTo(t1a, new Size(ra, ra), 0, false, SweepDirection.Counterclockwise, true, false);
                }
            }

            g.Freeze();
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

        /// <summary>
        /// Подпись на пилюле-подложке (S41): скруглённый прямоугольник цвета фона с непрозрачностью 0xB0,
        /// радиус 4, поля 8 по горизонтали и 4 по вертикали (приём из SceneCanvas); поверх — текст
        /// FormattedText в заданной точке (x, y — позиция текста, как без подложки).
        /// </summary>
        private void TextOnPill(DrawingContext dc, FormattedText text, double x, double y)
        {
            dc.DrawRoundedRectangle(LabelPillBrush, null, new Rect(x - 8, y - 4, text.Width + 16, text.Height + 8), 4, 4);
            dc.DrawText(text, new Point(x, y));
        }

        private static T Freeze<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
