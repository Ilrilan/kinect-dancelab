using System;
using System.Collections.Generic;
using System.Linq;
using DanceLab.Capture;
using DanceLab.Core.Reconstruction;
using Xunit;

namespace DanceLab.Core.Tests
{
    /// <summary>Синтетическая левая рука и нога с известной геометрией: истинное положение каждого сустава известно в каждом кадре.</summary>
    internal static class Synth
    {
        public const double Upper = 0.28;
        public const double Lower = 0.26;
        public static readonly double[] Shoulder = { 0.2, 0.4, 2.5 };

        public static double[] Norm(double x, double y, double z)
        {
            var l = Math.Sqrt((x * x) + (y * y) + (z * z));
            return new[] { x / l, y / l, z / l };
        }

        /// <summary>Истинные плечо, локоть и кисть в кадре i (30 кадров в секунду): рука плавно двигается, угол сгиба далёк от пределов.</summary>
        public static (double[] S, double[] E, double[] W) Arm(int i)
        {
            var t = i / 30.0;
            var d1 = Norm(0.3, -1.0, 0.2 * Math.Sin(t));
            var d2 = Norm(0.8, 0.5 + (0.3 * Math.Sin(0.7 * t)), -0.4);
            var e = new[] { Shoulder[0] + (Upper * d1[0]), Shoulder[1] + (Upper * d1[1]), Shoulder[2] + (Upper * d1[2]) };
            var w = new[] { e[0] + (Lower * d2[0]), e[1] + (Lower * d2[1]), e[2] + (Lower * d2[2]) };
            return (Shoulder, e, w);
        }

        public static RawJoint J(string name, double[] p, string? state = "tracked", double? conf = null) => new RawJoint(name, p[0], p[1], p[2], conf, state);

        public static SkeletonFrame Frame(int i, params RawJoint[] joints) => new SkeletonFrame(i * 33333L, i * 333330L, new List<TrackedUser> { new TrackedUser(1, joints) });

        public static SkeletonFrame KinectArm(int i, string elbowState = "tracked", double[]? elbowOverride = null, bool wristMissing = false)
        {
            var (s, e, w) = Arm(i);
            return Frame(
                i,
                J("ShoulderLeft", s),
                J("ElbowLeft", elbowOverride ?? e, elbowState),
                J("WristLeft", wristMissing ? new[] { 0.0, 0.0, 0.0 } : w, wristMissing ? "not-tracked" : "tracked"));
        }

        public static double Dist(double[] a, ReconstructedJoint b) => Math.Sqrt(Math.Pow(a[0] - b.X, 2) + Math.Pow(a[1] - b.Y, 2) + Math.Pow(a[2] - b.Z, 2));

        public static double Dist(ReconstructedJoint a, ReconstructedJoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    }

    public class PoseReconstructorTests
    {
        private static PoseReconstructor Learned(out int next)
        {
            var r = new PoseReconstructor("kinect-sdk-2");
            for (var i = 0; i < 60; i++)
            {
                r.Process(Synth.KinectArm(i));
            }

            next = 60;
            return r;
        }

        [Fact]
        public void MeasuredJointsAreReturnedUnchanged()
        {
            var r = new PoseReconstructor("kinect-sdk-2");
            for (var i = 0; i < 90; i++)
            {
                var f = Synth.KinectArm(i);
                var pose = r.Process(f);
                foreach (var j in f.Users[0].Joints)
                {
                    var o = pose.Find(j.Name)!;
                    Assert.Equal(JointOrigin.Measured, o.Origin);
                    Assert.Equal(j.X, o.X);
                    Assert.Equal(j.Y, o.Y);
                    Assert.Equal(j.Z, o.Z);
                }
            }
        }

        [Fact]
        public void BoneLengthsAreLearnedFromReliableFrames()
        {
            var r = Learned(out _);
            var l = r.BoneLengths["ArmLeft"];
            Assert.InRange(l.Upper, Synth.Upper - 0.002, Synth.Upper + 0.002);
            Assert.InRange(l.Lower, Synth.Lower - 0.002, Synth.Lower + 0.002);
        }

        [Fact]
        public void LostElbowIsReconstructedCloseToTruthWhenHistoryExists()
        {
            var r = Learned(out var n);
            for (var i = n; i < n + 30; i++)
            {
                var (_, e, w) = Synth.Arm(i);
                var garbage = new[] { e[0] + 0.2, e[1] - 0.1, e[2] + 0.1 };
                var pose = r.Process(Synth.KinectArm(i, "inferred", garbage));
                var elbow = pose.Find("ElbowLeft")!;
                Assert.Equal(JointOrigin.Reconstructed, elbow.Origin);
                Assert.True(elbow.Confidence <= 0.6);
                Assert.InRange(Synth.Dist(e, elbow), 0, 0.05);
                Assert.Equal(JointOrigin.Measured, pose.Find("WristLeft")!.Origin);
                Assert.Equal(w[0], pose.Find("WristLeft")!.X);
            }
        }

        [Fact]
        public void ReconstructedElbowKeepsBoneLengths()
        {
            var r = Learned(out var n);
            var l = r.BoneLengths["ArmLeft"];
            for (var i = n; i < n + 20; i++)
            {
                var (s, _, _) = Synth.Arm(i);
                var pose = r.Process(Synth.KinectArm(i, "inferred", new[] { 0.0, 0.0, 0.0 }));
                var elbow = pose.Find("ElbowLeft")!;
                var shoulder = pose.Find("ShoulderLeft")!;
                var wrist = pose.Find("WristLeft")!;
                Assert.InRange(Synth.Dist(shoulder, elbow), l.Upper - 1e-6, l.Upper + 1e-6);
                Assert.InRange(Synth.Dist(elbow, wrist), l.Lower - 0.01, l.Lower + 0.01);
                Assert.Equal(s[0], shoulder.X);
            }
        }

        [Fact]
        public void WithoutHistoryElbowGoesBehindWristAlongSensorRay()
        {
            var r = new PoseReconstructor("kinect-sdk-2");
            r.SetBoneLengths("ArmLeft", Synth.Upper, Synth.Lower);
            var shoulder = new[] { 0.0, 0.3, 2.5 };
            var wrist = new[] { 0.5, 0.3, 2.5 };
            var pose = r.Process(Synth.Frame(0, Synth.J("ShoulderLeft", shoulder), Synth.J("ElbowLeft", new[] { 9.0, 9.0, 9.0 }, "inferred"), Synth.J("WristLeft", wrist)));
            var elbow = pose.Find("ElbowLeft")!;
            Assert.Equal(JointOrigin.Reconstructed, elbow.Origin);
            Assert.True(elbow.Z > 2.5 + 0.05, "локоть должен быть дальше от сенсора, чем плечо и кисть");
            Assert.InRange(Synth.Dist(shoulder, elbow), Synth.Upper - 1e-6, Synth.Upper + 1e-6);
        }

        [Fact]
        public void BeforeLengthsAreLearnedLostElbowIsInvalid()
        {
            var r = new PoseReconstructor("kinect-sdk-2");
            var pose = r.Process(Synth.KinectArm(0, "inferred"));
            Assert.Equal(JointOrigin.Invalid, pose.Find("ElbowLeft")!.Origin);
            Assert.Equal(JointOrigin.Measured, pose.Find("ShoulderLeft")!.Origin);
            Assert.Equal(JointOrigin.Measured, pose.Find("WristLeft")!.Origin);
        }

        [Fact]
        public void LostShoulderInvalidatesWholeLimb()
        {
            var r = Learned(out var n);
            var (_, e, w) = Synth.Arm(n);
            var pose = r.Process(Synth.Frame(n, Synth.J("ShoulderLeft", new[] { 0.0, 0.0, 0.0 }, "not-tracked"), Synth.J("ElbowLeft", e), Synth.J("WristLeft", w)));
            Assert.All(new[] { "ShoulderLeft", "ElbowLeft", "WristLeft" }, name => Assert.Equal(JointOrigin.Invalid, pose.Find(name)!.Origin));
        }

        [Fact]
        public void LostWristIsExtrapolatedAlongLastForearmDirection()
        {
            var r = Learned(out var n);
            var pose = r.Process(Synth.KinectArm(n, wristMissing: true));
            var wrist = pose.Find("WristLeft")!;
            Assert.Equal(JointOrigin.Extrapolated, wrist.Origin);
            Assert.InRange(Synth.Dist(Synth.Arm(n).W, wrist), 0, 0.05);
            Assert.InRange(wrist.Confidence, 0.01, 0.4);
        }

        [Fact]
        public void HoldIsLimitedByHoldSeconds()
        {
            var r = Learned(out var n);
            var zero = new[] { 0.0, 0.0, 0.0 };
            var origins = new List<JointOrigin>();
            for (var i = n; i < n + 20; i++)
            {
                var pose = r.Process(Synth.Frame(i, Synth.J("ShoulderLeft", Synth.Shoulder), Synth.J("ElbowLeft", zero, "not-tracked"), Synth.J("WristLeft", zero, "not-tracked")));
                origins.Add(pose.Find("WristLeft")!.Origin);
            }

            Assert.Equal(JointOrigin.Extrapolated, origins[0]);
            Assert.Equal(JointOrigin.Extrapolated, origins[5]);
            Assert.Equal(JointOrigin.Invalid, origins[19]);
        }

        [Fact]
        public void ElbowJumpFasterThanSpeedLimitIsTreatedAsOutlier()
        {
            var r = Learned(out var n);
            var (_, e, _) = Synth.Arm(n);
            var jumped = new[] { e[0] + 1.0, e[1], e[2] };
            var pose = r.Process(Synth.KinectArm(n, "tracked", jumped));
            var elbow = pose.Find("ElbowLeft")!;
            Assert.Equal(JointOrigin.Reconstructed, elbow.Origin);
            Assert.InRange(Synth.Dist(e, elbow), 0, 0.05);
        }

        [Fact]
        public void ReprocessingOwnOutputGivesSamePositions()
        {
            var first = new PoseReconstructor("kinect-sdk-2");
            var second = new PoseReconstructor("kinect-sdk-2");
            for (var i = 0; i < 90; i++)
            {
                var (_, e, _) = Synth.Arm(i);
                var lost = i >= 60;
                var pose = first.Process(Synth.KinectArm(i, lost ? "inferred" : "tracked", lost ? new[] { 0.0, 0.0, 0.0 } : null));
                var joints = pose.Joints.Select(j => new RawJoint(j.Name, j.X, j.Y, j.Z, null, j.Origin == JointOrigin.Invalid ? "not-tracked" : "tracked")).ToArray();
                var again = second.Process(Synth.Frame(i, joints));
                foreach (var j in pose.Joints)
                {
                    var a = again.Find(j.Name)!;
                    Assert.InRange(Math.Abs(a.X - j.X) + Math.Abs(a.Y - j.Y) + Math.Abs(a.Z - j.Z), 0, 1e-9);
                }

                Assert.InRange(i, 0, 90);
                Assert.True(e.Length == 3);
            }
        }

        [Fact]
        public void NuitrackZeroConfidenceElbowIsReconstructed()
        {
            var r = new PoseReconstructor("nuitrack");
            for (var i = 0; i < 60; i++)
            {
                var (s, e, w) = Synth.Arm(i);
                r.Process(Synth.Frame(i, Synth.J("LeftShoulder", s, null, 0.8), Synth.J("LeftElbow", e, null, 0.8), Synth.J("LeftWrist", w, null, 0.8)));
            }

            var (s2, e2, w2) = Synth.Arm(60);
            var pose = r.Process(Synth.Frame(60, Synth.J("LeftShoulder", s2, null, 0.8), Synth.J("LeftElbow", new[] { 0.0, 0.0, 0.0 }, null, 0.0), Synth.J("LeftWrist", w2, null, 0.8)));
            var elbow = pose.Find("LeftElbow")!;
            Assert.Equal(JointOrigin.Reconstructed, elbow.Origin);
            Assert.InRange(Synth.Dist(e2, elbow), 0, 0.05);
        }

        [Fact]
        public void NuitrackBoneLengthDeviationMakesElbowUnreliable()
        {
            var parameters = ReconstructionParameters.ForNuitrack();
            parameters.BoneDeviationM = 0.03;
            var r = new PoseReconstructor("nuitrack", parameters);
            for (var i = 0; i < 60; i++)
            {
                var (s, e, w) = Synth.Arm(i);
                r.Process(Synth.Frame(i, Synth.J("LeftShoulder", s, null, 0.8), Synth.J("LeftElbow", e, null, 0.8), Synth.J("LeftWrist", w, null, 0.8)));
            }

            var (s2, e2, w2) = Synth.Arm(60);
            var off = new[] { e2[0] + 0.08, e2[1], e2[2] };
            var pose = r.Process(Synth.Frame(60, Synth.J("LeftShoulder", s2, null, 0.8), Synth.J("LeftElbow", off, null, 0.8), Synth.J("LeftWrist", w2, null, 0.8)));
            Assert.Equal(JointOrigin.Reconstructed, pose.Find("LeftElbow")!.Origin);
        }

        [Fact]
        public void LegIsReconstructedLikeAnArm()
        {
            var r = new PoseReconstructor("kinect-sdk-2");
            var hip = new[] { 0.1, -0.1, 2.5 };
            for (var i = 0; i < 60; i++)
            {
                var t = i / 30.0;
                var knee = new[] { 0.1, -0.1 - 0.42, 2.5 + (0.1 * Math.Sin(t)) };
                var ankle = new[] { 0.1, -0.1 - 0.42 - 0.4, knee[2] - 0.05 };
                r.Process(Synth.Frame(i, Synth.J("HipLeft", hip), Synth.J("KneeLeft", knee), Synth.J("AnkleLeft", ankle)));
            }

            var kneeTrue = new[] { 0.1, -0.52, 2.5 + (0.1 * Math.Sin(2.0)) };
            var ankleTrue = new[] { 0.1, -0.92, kneeTrue[2] - 0.05 };
            var pose = r.Process(Synth.Frame(60, Synth.J("HipLeft", hip), Synth.J("KneeLeft", new[] { 5.0, 5.0, 5.0 }, "inferred"), Synth.J("AnkleLeft", ankleTrue)));
            Assert.Equal(JointOrigin.Reconstructed, pose.Find("KneeLeft")!.Origin);
            Assert.InRange(Synth.Dist(kneeTrue, pose.Find("KneeLeft")!), 0, 0.08);
        }

        [Fact]
        public void FrameWithoutUsersGivesEmptyPose()
        {
            var r = Learned(out var n);
            var pose = r.Process(new SkeletonFrame(n * 33333L, 0, new List<TrackedUser>()));
            Assert.Empty(pose.Joints);
        }
    }
}
