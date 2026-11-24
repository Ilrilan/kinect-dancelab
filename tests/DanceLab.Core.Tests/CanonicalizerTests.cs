using System;
using System.Collections.Generic;
using DanceLab.Core.Canonical;
using DanceLab.Core.Reconstruction;
using Xunit;

namespace DanceLab.Core.Tests
{
    /// <summary>
    /// T40: живая поза — Canonicalizer заполняет боковой наклон (roll), оси торса (basis)
    /// и развёрнутое направление взгляда через HeadingUnwrapper (specs/player/trial.md,
    /// «Формат эталона, версия 2» S39).
    /// </summary>
    public class CanonicalizerTests
    {
        private static ReconstructedJoint J(string name, double x, double y, double z) =>
            new ReconstructedJoint(name, x, y, z, JointOrigin.Measured, 1);

        /// <summary>
        /// Стоящая фигура лицом к +Z (бок в сторону +X): бёдра и ноги на месте,
        /// середина плеч сдвинута на (dx, dy, dz) от корня (0, 0.95, 0).
        /// </summary>
        private static ReconstructedPose Figure(double dx = 0, double dy = 0.45, double dz = 0)
        {
            var mx = dx;
            var my = 0.95 + dy;
            var mz = dz;
            return new ReconstructedPose(0, new List<ReconstructedJoint>
            {
                J("SpineBase", 0, 0.95, 0),
                J("SpineMid", mx * 0.5, 0.95 + (dy * 0.5), mz * 0.5),
                J("Neck", mx, my + 0.05, mz),
                J("Head", mx, my + 0.20, mz),
                J("ShoulderLeft", mx + 0.25, my, mz),
                J("ShoulderRight", mx - 0.25, my, mz),
                J("ElbowLeft", mx + 0.25, my, mz + 0.28),
                J("ElbowRight", mx - 0.25, my - 0.28, mz),
                J("WristLeft", mx + 0.25, my, mz + 0.54),
                J("WristRight", mx - 0.25, my - 0.54, mz),
                J("HipLeft", 0.10, 0.90, 0),
                J("HipRight", -0.10, 0.90, 0),
                J("KneeLeft", 0.10, 0.50, 0),
                J("KneeRight", -0.10, 0.50, 0),
                J("AnkleLeft", 0.10, 0.10, 0),
                J("AnkleRight", -0.10, 0.10, 0),
            });
        }

        /// <summary>Поворот всех суставов позы вокруг вертикали на phi: (x, z) -> (x·cos φ + z·sin φ, −x·sin φ + z·cos φ).</summary>
        private static ReconstructedPose RotatedY(ReconstructedPose pose, double phi)
        {
            var c = Math.Cos(phi);
            var s = Math.Sin(phi);
            var joints = new List<ReconstructedJoint>();
            foreach (var j in pose.Joints)
            {
                joints.Add(new ReconstructedJoint(j.Name, (j.X * c) + (j.Z * s), j.Y, (-j.X * s) + (j.Z * c), j.Origin, j.Confidence));
            }

            return new ReconstructedPose(pose.DeviceTimeUs, joints);
        }

        private static void AssertVec(Vec3 actual, Vec3 expected, double tol, string what)
        {
            Assert.True(Math.Abs(actual.X - expected.X) <= tol, what + ".X: " + actual.X + " против " + expected.X);
            Assert.True(Math.Abs(actual.Y - expected.Y) <= tol, what + ".Y: " + actual.Y + " против " + expected.Y);
            Assert.True(Math.Abs(actual.Z - expected.Z) <= tol, what + ".Z: " + actual.Z + " против " + expected.Z);
        }

        [Fact]
        public void TorsoFrameAxesMatchMidShoulderHipLineAndCross()
        {
            // стоящий человек, наклонённый вперёд и вбок: середина плеч сдвинута на (0.1, ·, 0.25)
            var pose = Figure(dx: 0.1, dz: 0.25);
            var frame = Canonicalizer.ToCanonical(pose, null, 0.8);
            Assert.True(frame.HasTorsoBasis);

            var root = new Vec3(0, 0.95, 0);
            var midShoulder = new Vec3(0.1, 1.40, 0.25);
            var expUp = (midShoulder - root).Normalized();
            var hipLine = new Vec3(0.10 - (-0.10), 0, 0);
            var expLeft = (hipLine - (expUp * Vec3.Dot(hipLine, expUp))).Normalized();
            var expForward = Vec3.Cross(expLeft, expUp);

            var tf = frame.TorsoFrame();
            AssertVec(tf.Up, expUp, 1e-9, "Up");
            AssertVec(tf.Left, expLeft, 1e-9, "Left");
            AssertVec(tf.Forward, expForward, 1e-9, "Forward");
        }

        [Fact]
        public void SideLeanGivesRoll30DegreesAndZeroLean()
        {
            // ось торса наклонена вбок на 30°: середина плеч смещена вбок, вперёд — 0
            var a = Math.PI / 6.0;
            var r = 0.45;
            var pose = Figure(dx: r * Math.Sin(a), dy: r * Math.Cos(a));
            var frame = Canonicalizer.ToCanonical(pose, null, 0.8);
            Assert.Equal(0.0, frame.TorsoLeanRad, 6);
            Assert.True(Math.Abs(Math.Abs(frame.TorsoRollRad) - a) < 1e-6, "roll = " + frame.TorsoRollRad);
        }

        [Fact]
        public void WithoutUnwrapperHeadingIsRawAzimuth()
        {
            var phi = 0.7;
            var frame = Canonicalizer.ToCanonical(RotatedY(Figure(), phi), null, 0.8);
            Assert.Equal(Math.Atan2(Math.Sin(phi), Math.Cos(phi)), frame.HeadingRad, 12);
        }

        [Fact]
        public void WithUnwrapperHeadingSurvivesBranchFlipGlitch()
        {
            var heading = new HeadingUnwrapper();
            var poseA = RotatedY(Figure(), 0.7);
            var poseB = RotatedY(Figure(), 0.7 + Math.PI); // сбой: разворот на 180°

            var f1 = Canonicalizer.ToCanonical(poseA, null, 0.8, 0, heading);
            var f2 = Canonicalizer.ToCanonical(poseA, null, 0.8, 0, heading);
            Assert.Equal(f1.HeadingRad, f2.HeadingRad, 9);

            Canonicalizer.ToCanonical(poseB, null, 0.8, 0, heading);
            var f4 = Canonicalizer.ToCanonical(poseA, null, 0.8, 0, heading);
            Assert.True(Math.Abs(f4.HeadingRad - f2.HeadingRad) < 1e-9,
                "heading после сбоя = " + f4.HeadingRad + ", исходный = " + f2.HeadingRad);
        }
    }
}
