using System;
using System.Collections.Generic;
using DanceLab.Capture;

namespace DanceLab.Core.Reconstruction
{
    internal readonly struct V
    {
        public V(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public static V operator +(V a, V b) => new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static V operator -(V a, V b) => new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static V operator *(V a, double s) => new V(a.X * s, a.Y * s, a.Z * s);

        public static double Dot(V a, V b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public V Normalized()
        {
            var l = Length;
            return l < 1e-12 ? new V(0, 0, 0) : new V(X / l, Y / l, Z / l);
        }
    }

    /// <summary>
    /// Достройка потерянных и ненадёжных суставов рук и ног (specs/core/pose-reconstruction.md). Причинная обработка: состояние на одного человека, кадры подаются по порядку времени.
    /// Надёжные суставы возвращаются без изменений, потерянный локоть или колено находится по длинам костей, потерянная кисть или лодыжка продолжается по последнему направлению.
    /// </summary>
    public sealed class PoseReconstructor
    {
        private const double DefaultDt = 1.0 / 30.0;
        private const int SampleWindow = 300;

        private readonly string trackerId;
        private readonly ReconstructionParameters p;
        private readonly Dictionary<string, LimbState> limbs = new Dictionary<string, LimbState>();
        private readonly Dictionary<string, (V P1, V P2, int N)> history = new Dictionary<string, (V, V, int)>();
        private readonly Dictionary<string, (V Pos, long TimeUs)> lastMeasured = new Dictionary<string, (V, long)>();
        private long prevTimeUs = long.MinValue;

        public PoseReconstructor(string trackerId, ReconstructionParameters? parameters = null)
        {
            this.trackerId = trackerId;
            p = parameters ?? (trackerId == "nuitrack" ? ReconstructionParameters.ForNuitrack() : ReconstructionParameters.ForKinect());
            foreach (var l in LimbNames.All)
            {
                limbs[l] = new LimbState();
            }
        }

        /// <summary>Выученные или заданные длины костей: ключи ArmLeft, ArmRight, LegLeft, LegRight.</summary>
        public IReadOnlyDictionary<string, LimbLengths> BoneLengths
        {
            get
            {
                var d = new Dictionary<string, LimbLengths>();
                foreach (var kv in limbs)
                {
                    if (kv.Value.Lengths.HasValue)
                    {
                        d[kv.Key] = kv.Value.Lengths.Value;
                    }
                }

                return d;
            }
        }

        /// <summary>Задать длины костей конечности из профиля (дальше они не пересчитываются).</summary>
        public void SetBoneLengths(string limb, double upper, double lower)
        {
            if (!limbs.TryGetValue(limb, out var s))
            {
                throw new ArgumentException("Неизвестная конечность: " + limb, nameof(limb));
            }

            s.Lengths = new LimbLengths(upper, lower);
            s.Preset = true;
        }

        /// <summary>Сбросить историю положений и удержание, длины костей сохраняются.</summary>
        public void Reset()
        {
            history.Clear();
            lastMeasured.Clear();
            prevTimeUs = long.MinValue;
            foreach (var s in limbs.Values)
            {
                s.ResetMotion();
            }
        }

        public ReconstructedPose Process(SkeletonFrame frame)
        {
            if (frame.Users.Count == 0)
            {
                Reset();
                return new ReconstructedPose(frame.DeviceTimeUs, new List<ReconstructedJoint>());
            }

            var user = frame.Users[0];
            var dt = prevTimeUs == long.MinValue ? 0 : (frame.DeviceTimeUs - prevTimeUs) / 1e6;
            if (dt <= 0 || dt > 0.5)
            {
                dt = 0;
            }

            var step = dt > 0 ? dt : DefaultDt;
            var raw = new Dictionary<string, RawJoint>();
            foreach (var j in user.Joints)
            {
                raw[j.Name] = j;
            }

            var output = new Dictionary<string, ReconstructedJoint>();
            var limbJoints = new HashSet<string>();
            foreach (var limbName in LimbNames.All)
            {
                var (rootName, midName, endName) = LimbNames.Joints(trackerId, limbName);
                limbJoints.Add(rootName);
                limbJoints.Add(midName);
                limbJoints.Add(endName);
                if (raw.TryGetValue(rootName, out var rootRaw) && raw.TryGetValue(midName, out var midRaw) && raw.TryGetValue(endName, out var endRaw))
                {
                    ProcessLimb(limbs[limbName], frame.DeviceTimeUs, step, rootRaw, midRaw, endRaw, output);
                }
            }

            var joints = new List<ReconstructedJoint>(user.Joints.Count);
            foreach (var j in user.Joints)
            {
                if (output.TryGetValue(j.Name, out var done))
                {
                    joints.Add(done);
                }
                else if (!IsMissing(j))
                {
                    joints.Add(new ReconstructedJoint(j.Name, j.X, j.Y, j.Z, JointOrigin.Measured, MeasuredConfidence(j)));
                }
                else
                {
                    joints.Add(new ReconstructedJoint(j.Name, j.X, j.Y, j.Z, JointOrigin.Invalid, 0));
                }

                if (!IsMissing(j))
                {
                    history.TryGetValue(j.Name, out var h);
                    history[j.Name] = (new V(j.X, j.Y, j.Z), h.N > 0 ? h.P1 : new V(j.X, j.Y, j.Z), Math.Min(h.N + 1, 3));
                }
            }

            prevTimeUs = frame.DeviceTimeUs;
            return new ReconstructedPose(frame.DeviceTimeUs, joints);
        }

        private static bool IsMissing(RawJoint j) =>
            j.State == "not-tracked" || j.Conf == 0 || (Math.Abs(j.X) < 0.01 && Math.Abs(j.Y) < 0.01 && Math.Abs(j.Z) < 0.01);

        private static double MeasuredConfidence(RawJoint j) => j.Conf.HasValue ? Math.Max(0, Math.Min(1, j.Conf.Value)) : 1.0;

        private static V Pos(RawJoint j) => new V(j.X, j.Y, j.Z);

        /// <summary>Базовая надёжность по признакам трекера (S4): пометка Kinect, уверенность Nuitrack.</summary>
        private bool BaseReliable(RawJoint j)
        {
            if (IsMissing(j))
            {
                return false;
            }

            if (trackerId == "nuitrack")
            {
                return !j.Conf.HasValue || j.Conf.Value >= p.LowConfidence;
            }

            return j.State != "inferred";
        }

        private bool SpeedOk(RawJoint j, long timeUs)
        {
            if (!lastMeasured.TryGetValue(j.Name, out var last))
            {
                return true;
            }

            var elapsed = (timeUs - last.TimeUs) / 1e6;
            if (elapsed <= 0 || elapsed > 1.0)
            {
                return true;
            }

            return (Pos(j) - last.Pos).Length / elapsed <= p.MaxSpeedMps;
        }

        private bool JitterOk(RawJoint j)
        {
            if (double.IsPositiveInfinity(p.JitterSecondDifferenceM) || !history.TryGetValue(j.Name, out var h) || h.N < 2)
            {
                return true;
            }

            var second = Pos(j) - (h.P1 * 2.0) + h.P2;
            return second.Length <= p.JitterSecondDifferenceM;
        }

        private void ProcessLimb(LimbState s, long timeUs, double step, RawJoint rootRaw, RawJoint midRaw, RawJoint endRaw, Dictionary<string, ReconstructedJoint> output)
        {
            var rootOk = !IsMissing(rootRaw);
            if (!rootOk)
            {
                s.ResetMotion();
                output[rootRaw.Name] = Invalid(rootRaw);
                output[midRaw.Name] = Invalid(midRaw);
                output[endRaw.Name] = Invalid(endRaw);
                return;
            }

            var root = Pos(rootRaw);
            var endBase = BaseReliable(endRaw) && SpeedOk(endRaw, timeUs);
            var midOk = BaseReliable(midRaw) && SpeedOk(midRaw, timeUs) && JitterOk(midRaw);
            if (midOk && s.Lengths.HasValue && !double.IsPositiveInfinity(p.BoneDeviationM))
            {
                var l = s.Lengths.Value;
                var upperDev = Math.Abs((Pos(midRaw) - root).Length - l.Upper);
                var lowerDev = endBase ? Math.Abs((Pos(endRaw) - Pos(midRaw)).Length - l.Lower) : 0;
                if (upperDev > p.BoneDeviationM || lowerDev > p.BoneDeviationM)
                {
                    midOk = false;
                }
            }

            var endOk = endBase;
            output[rootRaw.Name] = new ReconstructedJoint(rootRaw.Name, rootRaw.X, rootRaw.Y, rootRaw.Z, JointOrigin.Measured, MeasuredConfidence(rootRaw));

            if (midOk && endOk)
            {
                output[midRaw.Name] = Measured(midRaw);
                output[endRaw.Name] = Measured(endRaw);
                s.Learn(root, Pos(midRaw), Pos(endRaw), p.MinSamplesForBones);
                s.LostBoth = 0;
                s.LostEnd = 0;
                MarkMeasured(midRaw, timeUs);
                MarkMeasured(endRaw, timeUs);
                MarkMeasured(rootRaw, timeUs);
                return;
            }

            if (!midOk && endOk)
            {
                output[endRaw.Name] = Measured(endRaw);
                MarkMeasured(endRaw, timeUs);
                s.LostEnd = 0;
                s.LostBoth = 0;
                if (!s.Lengths.HasValue)
                {
                    output[midRaw.Name] = Invalid(midRaw);
                    return;
                }

                var end = Pos(endRaw);
                var mid = Elbow(s, root, end);
                output[midRaw.Name] = new ReconstructedJoint(midRaw.Name, mid.X, mid.Y, mid.Z, JointOrigin.Reconstructed, 0.5);
                s.LastForearmDir = (end - mid).Normalized();
                s.LastRoot = root;
                s.LastMid = mid;
                s.LastEnd = end;
                return;
            }

            if (midOk && !endOk)
            {
                output[midRaw.Name] = Measured(midRaw);
                MarkMeasured(midRaw, timeUs);
                s.LostBoth = 0;
                if (s.Lengths.HasValue && s.LastForearmDir.HasValue && s.LostEnd <= p.HoldSeconds)
                {
                    var end = Pos(midRaw) + (s.LastForearmDir.Value * s.Lengths.Value.Lower);
                    var conf = 0.4 * (1.0 - (s.LostEnd / Math.Max(p.HoldSeconds, 1e-9)));
                    output[endRaw.Name] = new ReconstructedJoint(endRaw.Name, end.X, end.Y, end.Z, JointOrigin.Extrapolated, Math.Max(0, conf));
                }
                else
                {
                    output[endRaw.Name] = Invalid(endRaw);
                }

                s.LostEnd += step;
                return;
            }

            // потеряны и локоть, и кисть: удержание относительно опоры
            s.LostEnd += step;
            if (s.LastRoot.HasValue && s.LastMid.HasValue && s.LastEnd.HasValue && s.LostBoth <= p.HoldSeconds)
            {
                var m = root + (s.LastMid.Value - s.LastRoot.Value);
                var e = root + (s.LastEnd.Value - s.LastRoot.Value);
                var conf = Math.Max(0, 0.4 * (1.0 - (s.LostBoth / Math.Max(p.HoldSeconds, 1e-9))));
                output[midRaw.Name] = new ReconstructedJoint(midRaw.Name, m.X, m.Y, m.Z, JointOrigin.Extrapolated, conf);
                output[endRaw.Name] = new ReconstructedJoint(endRaw.Name, e.X, e.Y, e.Z, JointOrigin.Extrapolated, conf);
            }
            else
            {
                output[midRaw.Name] = Invalid(midRaw);
                output[endRaw.Name] = Invalid(endRaw);
            }

            s.LostBoth += step;
        }

        private void MarkMeasured(RawJoint j, long timeUs) => lastMeasured[j.Name] = (Pos(j), timeUs);

        private static ReconstructedJoint Measured(RawJoint j) => new ReconstructedJoint(j.Name, j.X, j.Y, j.Z, JointOrigin.Measured, MeasuredConfidence(j));

        private static ReconstructedJoint Invalid(RawJoint j) => new ReconstructedJoint(j.Name, j.X, j.Y, j.Z, JointOrigin.Invalid, 0);

        /// <summary>
        /// Локоть (колено) по плечу и кисти: пересечение двух сфер по длинам костей. Расстояние ограничено пределом сгиба.
        /// Точка на окружности: направление из прошлого кадра, иначе за кистью по лучу сенсора (S6), иначе вниз.
        /// </summary>
        private V Elbow(LimbState s, V shoulder, V wrist)
        {
            var len = s.Lengths!.Value;
            var l1 = len.Upper;
            var l2 = len.Lower;
            var axis = wrist - shoulder;
            var d = axis.Length;
            var u = d < 1e-9 ? new V(0, -1, 0) : axis * (1.0 / d);
            var minInterior = (180.0 - p.MaxFlexionDeg) * Math.PI / 180.0;
            var dMinFlexion = Math.Sqrt(Math.Max(0, (l1 * l1) + (l2 * l2) - (2 * l1 * l2 * Math.Cos(minInterior))));
            var dMin = Math.Max(Math.Abs(l1 - l2) + 1e-3, dMinFlexion);
            var dMax = l1 + l2 - 1e-6;
            var dEff = Math.Max(dMin, Math.Min(dMax, d));
            var a = ((l1 * l1) - (l2 * l2) + (dEff * dEff)) / (2 * dEff);
            var h = Math.Sqrt(Math.Max(0, (l1 * l1) - (a * a)));
            var center = shoulder + (u * a);

            V? n = null;
            if (s.LastPerp.HasValue)
            {
                n = PerpComponent(s.LastPerp.Value, u);
            }

            if (!n.HasValue)
            {
                n = PerpComponent(shoulder.Normalized(), u);
            }

            if (!n.HasValue)
            {
                n = PerpComponent(new V(0, -1, 0), u);
            }

            if (!n.HasValue)
            {
                n = PerpComponent(new V(1, 0, 0), u) ?? new V(0, 0, 1);
            }

            s.LastPerp = n.Value;
            return center + (n.Value * h);
        }

        /// <summary>Единичная составляющая вектора, перпендикулярная оси; null, если вектор почти параллелен оси.</summary>
        private static V? PerpComponent(V v, V u)
        {
            var c = v - (u * V.Dot(v, u));
            return c.Length > 0.2 ? c.Normalized() : (V?)null;
        }

        private sealed class LimbState
        {
            private readonly List<double> upper = new List<double>();
            private readonly List<double> lower = new List<double>();

            public LimbLengths? Lengths { get; set; }

            public bool Preset { get; set; }

            public V? LastPerp { get; set; }

            public V? LastForearmDir { get; set; }

            public V? LastRoot { get; set; }

            public V? LastMid { get; set; }

            public V? LastEnd { get; set; }

            public double LostBoth { get; set; }

            public double LostEnd { get; set; }

            public void ResetMotion()
            {
                LastPerp = null;
                LastForearmDir = null;
                LastRoot = null;
                LastMid = null;
                LastEnd = null;
                LostBoth = 0;
                LostEnd = 0;
            }

            public void Learn(V root, V mid, V end, int minSamples)
            {
                LastRoot = root;
                LastMid = mid;
                LastEnd = end;
                LastForearmDir = (end - mid).Normalized();
                var axis = end - root;
                var u = axis.Normalized();
                var off = (mid - root) - (u * V.Dot(mid - root, u));
                if (off.Length > 0.02)
                {
                    LastPerp = off.Normalized();
                }

                if (Preset)
                {
                    return;
                }

                upper.Add((mid - root).Length);
                lower.Add((end - mid).Length);
                if (upper.Count > SampleWindow)
                {
                    upper.RemoveAt(0);
                    lower.RemoveAt(0);
                }

                if (upper.Count >= minSamples)
                {
                    Lengths = new LimbLengths(Median(upper), Median(lower));
                }
            }

            private static double Median(List<double> values)
            {
                var copy = new List<double>(values);
                copy.Sort();
                return copy[copy.Count / 2];
            }
        }
    }
}
