using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DanceLab.Core.Canonical;
using DanceLab.Core.Reconstruction;
using Xunit;

namespace DanceLab.Core.Tests
{
    /// <summary>Синтетические канонические позы и позы реконструкции для проверки канонизации и пробной оценки (specs/player/trial.md, T2).</summary>
    internal static class CanonSynth
    {
        public const double Leg = 0.8;      // бедро 0.4 + голень 0.4
        public const double UpperArm = 0.28;
        public const double LowerArm = 0.26;

        public static ReconstructedJoint J(string name, double x, double y, double z, JointOrigin origin = JointOrigin.Measured) =>
            new ReconstructedJoint(name, x, y, z, origin, origin == JointOrigin.Invalid ? 0 : 1);

        /// <summary>Стоящая фигура лицом к +Z: левая рука вперёд, правая вниз (зеркальная асимметрия для инварианта).</summary>
        public static ReconstructedPose Figure(long timeUs = 0, JointOrigin leftWristOrigin = JointOrigin.Measured) => new ReconstructedPose(timeUs, new List<ReconstructedJoint>
        {
            J("SpineBase", 0, 0.95, 0),
            J("SpineMid", 0, 1.25, 0),
            J("Neck", 0, 1.45, 0),
            J("Head", 0, 1.60, 0),
            J("ShoulderLeft", 0.25, 1.40, 0),
            J("ShoulderRight", -0.25, 1.40, 0),
            J("ElbowLeft", 0.25, 1.40, UpperArm),
            J("ElbowRight", -0.25, 1.40 - UpperArm, 0),
            J("WristLeft", 0.25, 1.40, UpperArm + LowerArm, leftWristOrigin),
            J("WristRight", -0.25, 1.40 - UpperArm - LowerArm, 0),
            J("HipLeft", 0.10, 0.90, 0),
            J("HipRight", -0.10, 0.90, 0),
            J("KneeLeft", 0.10, 0.50, 0),
            J("KneeRight", -0.10, 0.50, 0),
            J("AnkleLeft", 0.10, 0.10, 0),
            J("AnkleRight", -0.10, 0.10, 0),
        });

        /// <summary>Та же фигура, торс наклонён вперёд на 45°, голова наклонена вбок-вверх от шеи.</summary>
        public static ReconstructedPose FigureLean(long timeUs = 0) => new ReconstructedPose(timeUs, new List<ReconstructedJoint>
        {
            J("SpineBase", 0, 0.95, 0),
            J("SpineMid", 0, 1.25, 0.35),
            J("Neck", 0, 1.45, 0.5),
            J("Head", 0.1, 1.60, 0.5),
            J("ShoulderLeft", 0.25, 1.40, 0.45),
            J("ShoulderRight", -0.25, 1.40, 0.45),
            J("ElbowLeft", 0.25, 1.40, 0.45 + UpperArm),
            J("ElbowRight", -0.25, 1.40 - UpperArm, 0.45),
            J("WristLeft", 0.25, 1.40, 0.45 + UpperArm + LowerArm),
            J("WristRight", -0.25, 1.40 - UpperArm - LowerArm, 0.45),
            J("HipLeft", 0.10, 0.90, 0),
            J("HipRight", -0.10, 0.90, 0),
            J("KneeLeft", 0.10, 0.50, 0),
            J("KneeRight", -0.10, 0.50, 0),
            J("AnkleLeft", 0.10, 0.10, 0),
            J("AnkleRight", -0.10, 0.10, 0),
        });

        public static Dictionary<string, LimbLengths> Bones() => new Dictionary<string, LimbLengths>
        {
            ["ArmLeft"] = new LimbLengths(UpperArm, LowerArm),
            ["ArmRight"] = new LimbLengths(UpperArm, LowerArm),
            ["LegLeft"] = new LimbLengths(0.4, 0.4),
            ["LegRight"] = new LimbLengths(0.4, 0.4),
        };

        public static double[] RotX(double[] v, double a)
        {
            var c = Math.Cos(a);
            var s = Math.Sin(a);
            return new[] { v[0], (v[1] * c) - (v[2] * s), (v[1] * s) + (v[2] * c) };
        }

        /// <summary>Кадр эталона с плавным движением всех восьми костей и торса (амплитуда 0.5 рад, 0.5 Гц).</summary>
        public static CanonicalFrame Motion(double t)
        {
            var amplitude = 0.5;
            var freq = 0.5;
            var bases = new double[8][]
            {
                new[] { 0.0, 0.0, 1.0 }, new[] { 0.0, 0.0, 1.0 }, new[] { 0.0, 0.0, 1.0 }, new[] { 0.0, 0.0, 1.0 },
                new[] { 0.0, -1.0, 0.0 }, new[] { 0.0, -1.0, 0.0 }, new[] { 0.0, -1.0, 0.0 }, new[] { 0.0, -1.0, 0.0 },
            };
            var bones = new double[24];
            var valid = new bool[8];
            for (var i = 0; i < 8; i++)
            {
                var angle = amplitude * Math.Sin((2 * Math.PI * freq * t) + (0.7 * i));
                var v = RotX(bases[i], angle);
                bones[i * 3] = v[0];
                bones[(i * 3) + 1] = v[1];
                bones[(i * 3) + 2] = v[2];
                valid[i] = true;
            }

            var w = 2 * Math.PI * freq;
            return new CanonicalFrame(
                t,
                bones,
                valid,
                0.2 * Math.Sin(w * t),
                0.1 * Math.Sin((w * t) + 2.0),
                0.05 * Math.Sin(w * t),
                0.3 * Math.Sin((w * t) / 2.0),
                0.15 * Math.Sin(((w * t) / 2.0) + 1.0),
                0.9,
                0.5 * Math.Sin((w * t) / 3.0));
        }

        /// <summary>Кадр, у всех валидных костей направление (0,-1,0), повёрнутое на angle вокруг X.</summary>
        public static CanonicalFrame Tilted(double angle, int validBones = 8)
        {
            var bones = new double[24];
            var valid = new bool[8];
            var v = RotX(new[] { 0.0, -1.0, 0.0 }, angle);
            for (var i = 0; i < 8; i++)
            {
                bones[i * 3] = v[0];
                bones[(i * 3) + 1] = v[1];
                bones[(i * 3) + 2] = v[2];
                valid[i] = i < validBones;
            }

            return new CanonicalFrame(0, bones, valid, 0, 0, 0, 0, 0, 0.9, 0);
        }

        public static CanonicalTrack Track(int frames, double fps)
        {
            var list = new List<CanonicalFrame>();
            for (var k = 0; k < frames; k++)
            {
                list.Add(Motion(k / fps));
            }

            return new CanonicalTrack(fps, 0.7111, list, "synth", "synth.mp4");
        }

        /// <summary>Кадр с костями, переставленными местами (зеркальная подмена сторон).</summary>
        public static CanonicalFrame Mirror(CanonicalFrame f)
        {
            var bones = new double[24];
            var valid = new bool[8];
            int[] pairs = { 1, 0, 3, 2, 5, 4, 7, 6 };
            for (var i = 0; i < 8; i++)
            {
                var j = pairs[i];
                bones[i * 3] = f.Bones[j * 3];
                bones[(i * 3) + 1] = f.Bones[(j * 3) + 1];
                bones[(i * 3) + 2] = f.Bones[(j * 3) + 2];
                valid[i] = f.BoneValid[j];
            }

            return new CanonicalFrame(f.TSec, bones, valid, f.TorsoLeanRad, f.TorsoTurnRad, f.HeadTiltRad, f.RootForward, f.RootSide, f.RootHeight, f.HeadingRad);
        }

        public static void AssertBone(CanonicalFrame f, string name, double x, double y, double z, int precision = 9)
        {
            var b = f.Bone(name);
            Assert.True(b.HasValue, "нет кости " + name);
            Assert.Equal(x, b!.Value.X, precision);
            Assert.Equal(y, b!.Value.Y, precision);
            Assert.Equal(z, b!.Value.Z, precision);
        }
    }

    public class CanonicalTests
    {
        private static string TempFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "dancelab-t2-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        [Fact]
        public void TrialParamsHaveBriefDefaults()
        {
            var p = new TrialParams();
            Assert.Equal(0.35, p.DeadZone, 10);
            Assert.Equal(1.0, p.Saturation, 10);
            Assert.Equal(0.8, p.ErrorThreshold, 10);
            Assert.Equal(0.35, p.ShiftWindow, 10);
            Assert.Equal(0.1, p.ShiftRate, 10);
            Assert.Equal(3.0, p.RunningWindow, 10);
            Assert.Equal(4, p.MinCoverageBones);
        }

        [Fact]
        public void ToCanonicalBuildsTorsoFrameBoneDirections()
        {
            var f = Canonicalizer.ToCanonical(CanonSynth.Figure(333333), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(0.333333, f.TSec, 6);
            CanonSynth.AssertBone(f, "uArmL", 0, 0, 1);
            CanonSynth.AssertBone(f, "lArmL", 0, 0, 1);
            CanonSynth.AssertBone(f, "uArmR", 0, -1, 0);
            CanonSynth.AssertBone(f, "lArmR", 0, -1, 0);
            CanonSynth.AssertBone(f, "thighL", 0, -1, 0);
            CanonSynth.AssertBone(f, "thighR", 0, -1, 0);
            CanonSynth.AssertBone(f, "shinL", 0, -1, 0);
            CanonSynth.AssertBone(f, "shinR", 0, -1, 0);
            Assert.All(f.BoneValid, v => Assert.True(v));
            Assert.Equal(0.0, f.TorsoLeanRad, 9);
            Assert.Equal(0.0, f.TorsoTurnRad, 9);
            Assert.Equal(0.0, f.HeadingRad, 9);
            Assert.Equal(0.95 / CanonSynth.Leg, f.RootHeight, 9);
            Assert.Equal(0.0, f.RootForward, 9);
            Assert.Equal(0.0, f.RootSide, 9);
        }

        /// <summary>S15: высота корня считается от пола сцены floorYm, по умолчанию 0 — прежнее поведение.</summary>
        [Fact]
        public void ToCanonicalRootHeightIsAboveSceneFloor()
        {
            var aboveZero = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(0.95 / CanonSynth.Leg, aboveZero.RootHeight, 9);

            var aboveFloor = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg, 0.10);
            Assert.Equal(0.85 / CanonSynth.Leg, aboveFloor.RootHeight, 9);
        }

        [Fact]
        public void ToCanonicalComputesLeanAndHeadTilt()
        {
            var f = Canonicalizer.ToCanonical(CanonSynth.FigureLean(), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(Math.PI / 4.0, f.TorsoLeanRad, 6);
            Assert.InRange(f.HeadTiltRad, 0.92, 0.96);
            Assert.Equal(0.0, f.HeadingRad, 9);
            Assert.True(f.TorsoTurnRad == 0.0 || Math.Abs(f.TorsoTurnRad) < 1e-6, "наклон вперёд не должен давать скрутку");
        }

        /// <summary>T13/P0.1: «шея» для оси вверх — середина плеч (конвенция эталона canonical_export.py);
        /// сустав Neck трекера, стоящий выше середины плеч, фрейм не сдвигает.</summary>
        [Fact]
        public void NeckJointAboveShouldersDoesNotAffectTorsoFrame()
        {
            // наклонённая фигура, Neck поднят на 10 см выше середины плеч: (0, 1.50, 0.45)
            var withNeck = new List<ReconstructedJoint>();
            foreach (var j in CanonSynth.FigureLean().Joints)
            {
                withNeck.Add(j.Name == "Neck"
                    ? new ReconstructedJoint("Neck", 0.0, 1.50, 0.45, j.Origin, j.Confidence)
                    : j);
            }

            // та же фигура вовсе без сустава Neck
            var withoutNeck = new List<ReconstructedJoint>();
            foreach (var j in withNeck)
            {
                if (j.Name != "Neck")
                {
                    withoutNeck.Add(j);
                }
            }

            var a = Canonicalizer.ToCanonical(new ReconstructedPose(0, withNeck), CanonSynth.Bones(), CanonSynth.Leg);
            var b = Canonicalizer.ToCanonical(new ReconstructedPose(0, withoutNeck), CanonSynth.Bones(), CanonSynth.Leg);
            for (var i = 0; i < CanonicalFrame.BoneCount * 3; i++)
            {
                Assert.Equal(a.Bones[i], b.Bones[i], 9);
            }

            for (var i = 0; i < CanonicalFrame.BoneCount; i++)
            {
                Assert.Equal(a.BoneValid[i], b.BoneValid[i]);
            }

            Assert.Equal(a.TorsoLeanRad, b.TorsoLeanRad, 9);
            Assert.Equal(a.TorsoTurnRad, b.TorsoTurnRad, 9);
            Assert.Equal(a.HeadingRad, b.HeadingRad, 9);

            // ось вверх — от корня к середине плеч (наклон торса 45°); от Neck было бы atan2(0.45, 0.55) ≈ 0.686
            Assert.Equal(Math.PI / 4.0, a.TorsoLeanRad, 9);
        }

        /// <summary>T13/P0.1: без валидных плеч ось вверх падает на сустав Neck (прежнее поведение), не на грудь.</summary>
        [Fact]
        public void WithoutShouldersUpAxisFallsBackToNeckJoint()
        {
            // фигура без плеч: Neck (0,1.45,0.5) даёт lean 45°, грудь (0,1.25,0.35) дала бы atan2(0.35,0.30) ≈ 0.862
            var noShoulders = new List<ReconstructedJoint>();
            foreach (var j in CanonSynth.FigureLean().Joints)
            {
                if (j.Name != "ShoulderLeft" && j.Name != "ShoulderRight")
                {
                    noShoulders.Add(j);
                }
            }

            var f = Canonicalizer.ToCanonical(new ReconstructedPose(0, noShoulders), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(Math.PI / 4.0, f.TorsoLeanRad, 9);

            // плечи есть, но помечены Invalid — тоже fallback на Neck
            var invalidShoulders = new List<ReconstructedJoint>();
            foreach (var j in CanonSynth.FigureLean().Joints)
            {
                invalidShoulders.Add(j.Name == "ShoulderLeft" || j.Name == "ShoulderRight"
                    ? new ReconstructedJoint(j.Name, j.X, j.Y, j.Z, JointOrigin.Invalid, 0)
                    : j);
            }

            var f2 = Canonicalizer.ToCanonical(new ReconstructedPose(0, invalidShoulders), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(Math.PI / 4.0, f2.TorsoLeanRad, 9);
        }

        /// <summary>Инвариант trial S6: поворот в комнате и перемещение не меняют направления костей; поворот меняет heading.</summary>
        [Fact]
        public void ToCanonicalIsInvariantUnderWorldRotationAndTranslation()
        {
            var baseFrame = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);

            // поворот вокруг вертикали на 90°: (x, y, z) -> (z, y, -x)
            var rotated = new List<ReconstructedJoint>();
            foreach (var j in CanonSynth.Figure().Joints)
            {
                rotated.Add(new ReconstructedJoint(j.Name, j.Z, j.Y, -j.X, j.Origin, j.Confidence));
            }

            var rotatedFrame = Canonicalizer.ToCanonical(new ReconstructedPose(0, rotated), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(Math.PI / 2.0, rotatedFrame.HeadingRad, 9);
            for (var i = 0; i < 24; i++)
            {
                Assert.Equal(baseFrame.Bones[i], rotatedFrame.Bones[i], 9);
            }

            // сдвиг в комнате: ориентация и heading не меняются
            var moved = new List<ReconstructedJoint>();
            foreach (var j in CanonSynth.Figure().Joints)
            {
                moved.Add(new ReconstructedJoint(j.Name, j.X + 0.5, j.Y + 2.0, j.Z - 1.0, j.Origin, j.Confidence));
            }

            var movedFrame = Canonicalizer.ToCanonical(new ReconstructedPose(0, moved), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.Equal(0.0, movedFrame.HeadingRad, 9);
            for (var i = 0; i < 24; i++)
            {
                Assert.Equal(baseFrame.Bones[i], movedFrame.Bones[i], 9);
            }

            Assert.Equal((0.95 + 2.0) / CanonSynth.Leg, movedFrame.RootHeight, 9);
        }

        [Fact]
        public void ToCanonicalAcceptsNuitrackAndCanonicalJointNames()
        {
            var kinect = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);
            var rename = new Dictionary<string, string>
            {
                ["SpineBase"] = "Waist", ["SpineMid"] = "Torso", ["Neck"] = "Neck", ["Head"] = "Head",
                ["ShoulderLeft"] = "LeftShoulder", ["ShoulderRight"] = "RightShoulder",
                ["ElbowLeft"] = "LeftElbow", ["ElbowRight"] = "RightElbow",
                ["WristLeft"] = "LeftWrist", ["WristRight"] = "RightWrist",
                ["HipLeft"] = "LeftHip", ["HipRight"] = "RightHip",
                ["KneeLeft"] = "LeftKnee", ["KneeRight"] = "RightKnee",
                ["AnkleLeft"] = "LeftAnkle", ["AnkleRight"] = "RightAnkle",
            };
            var joints = new List<ReconstructedJoint>();
            foreach (var j in CanonSynth.Figure().Joints)
            {
                joints.Add(new ReconstructedJoint(rename[j.Name], j.X, j.Y, j.Z, j.Origin, j.Confidence));
            }

            var nuitrack = Canonicalizer.ToCanonical(new ReconstructedPose(0, joints), CanonSynth.Bones(), CanonSynth.Leg);
            for (var i = 0; i < 24; i++)
            {
                Assert.Equal(kinect.Bones[i], nuitrack.Bones[i], 9);
            }
        }

        /// <summary>Инвариант trial S8: сравнение позы с самой собой даёт нулевое отклонение.</summary>
        [Fact]
        public void SelfComparisonGivesZeroDeviation()
        {
            var f = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);
            var c = TrialComparer.Compare(f, f);
            Assert.Equal(0.0, c.MeanDeviationRad, 12);
            Assert.Equal(1.0, c.Coverage, 12);
            Assert.All(c.BoneDeviationRad, d => Assert.Equal(0.0, d, 12));
        }

        /// <summary>Невалидный сустав (origin Invalid) даёт Coverage меньше 1 и исключает свою кость из сравнения (trial S9).</summary>
        [Fact]
        public void InvalidWristReducesCoverage()
        {
            var reference = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);
            var user = Canonicalizer.ToCanonical(CanonSynth.Figure(leftWristOrigin: JointOrigin.Invalid), CanonSynth.Bones(), CanonSynth.Leg);
            Assert.False(user.BoneValid[2]);
            var c = TrialComparer.Compare(reference, user);
            Assert.Equal(7.0 / 8.0, c.Coverage, 12);
            Assert.Equal(0.0, c.BoneDeviationRad[2], 12);
            Assert.Equal(0.0, c.MeanDeviationRad, 12);
        }

        [Fact]
        public void EmptyPoseGivesZeroCoverage()
        {
            var reference = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);
            var empty = Canonicalizer.ToCanonical(new ReconstructedPose(0, new List<ReconstructedJoint>()), CanonSynth.Bones(), CanonSynth.Leg);
            var c = TrialComparer.Compare(reference, empty);
            Assert.Equal(0.0, c.Coverage, 12);
            Assert.Equal(0.0, c.MeanDeviationRad, 12);
        }

        [Fact]
        public void TrackSampleInterpolatesLinearly()
        {
            const string json = @"{
  ""formatVersion"": 2,
  ""sequence"": ""s"",
  ""video"": ""v.mp4"",
  ""fps"": 60,
  ""frameCount"": 2,
  ""legLengthCm"": 71.3,
  ""frames"": [
    { ""t"": 0.0,
      ""bones"": { ""uArmL"": [0,0,1], ""uArmR"": [0,0,1], ""lArmL"": [0,0,1], ""lArmR"": [0,0,1], ""thighL"": [0,1,0], ""thighR"": [0,1,0], ""shinL"": [0,1,0], ""shinR"": [0,1,0] },
      ""torso"": { ""lean"": 0.0, ""turn"": 0.0, ""roll"": 0, ""basis"": [1,0,0, 0,1,0, 0,0,1] }, ""head"": { ""tilt"": 0.0 },
      ""root"": { ""forward"": 0.0, ""side"": 0.0, ""height"": 0.9, ""heading"": 0.0 } },
    { ""t"": 0.5,
      ""bones"": { ""uArmL"": [1,0,0], ""uArmR"": [0,0,1], ""lArmL"": [0,0,1], ""lArmR"": [0,0,1], ""thighL"": [0,1,0], ""thighR"": [0,1,0], ""shinL"": [0,1,0], ""shinR"": [0,1,0] },
      ""torso"": { ""lean"": 0.5, ""turn"": 0.2, ""roll"": 0, ""basis"": [1,0,0, 0,1,0, 0,0,1] }, ""head"": { ""tilt"": 0.1 },
      ""root"": { ""forward"": 1.0, ""side"": -1.0, ""height"": 0.8, ""heading"": 1.0 } }
  ]
}";
            var path = TempFile(json);
            try
            {
                var track = CanonicalTrack.Load(path);
                Assert.Equal(60, track.Fps, 9);
                Assert.Equal(0.713, track.LegLengthM, 9);
                Assert.Equal(2, track.Frames.Count);
                Assert.Equal("s", track.Sequence);
                Assert.Equal("v.mp4", track.Video);

                var mid = track.Sample(0.25);
                Assert.Equal(0.25, mid.TSec, 9);
                Assert.Equal(0.25, mid.TorsoLeanRad, 9);
                Assert.Equal(0.1, mid.TorsoTurnRad, 9);
                Assert.Equal(0.05, mid.HeadTiltRad, 9);
                Assert.Equal(0.5, mid.RootForward, 9);
                Assert.Equal(-0.5, mid.RootSide, 9);
                Assert.Equal(0.85, mid.RootHeight, 9);
                Assert.Equal(0.5, mid.HeadingRad, 9);
                var b = mid.Bone("uArmL")!.Value;
                Assert.Equal(1.0 / Math.Sqrt(2), b.X, 9);
                Assert.Equal(0.0, b.Y, 9);
                Assert.Equal(1.0 / Math.Sqrt(2), b.Z, 9);
                Assert.True(mid.BoneValid[0]);

                // вне диапазона — зажим на крайние кадры
                Assert.Equal(0.0, track.Sample(-1.0).TorsoLeanRad, 9);
                Assert.Equal(0.5, track.Sample(10.0).TorsoLeanRad, 9);
                Assert.Equal(-1.0, track.Sample(10.0).RootSide, 9);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TrackLoadRejectsUnknownFormatVersion()
        {
            var path = TempFile("{ \"formatVersion\": 3, \"frames\": [] }");
            try
            {
                Assert.Throws<InvalidDataException>(() => CanonicalTrack.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Формат версии 1 (без torso.roll и torso.basis) загрузчиком версии 2 отклоняется.</summary>
        [Fact]
        public void TrackLoadRejectsFormatVersion1()
        {
            var path = TempFile("{ \"formatVersion\": 1, \"frames\": [] }");
            try
            {
                Assert.Throws<InvalidDataException>(() => CanonicalTrack.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Версия 2 без torso.basis в кадре — InvalidDataException.</summary>
        [Fact]
        public void TrackLoadRejectsMissingTorsoBasis()
        {
            const string json = @"{
  ""formatVersion"": 2,
  ""frames"": [
    { ""t"": 0.0, ""torso"": { ""lean"": 0.0, ""turn"": 0.0, ""roll"": 0.0 }, ""head"": { ""tilt"": 0.0 } }
  ]
}";
            var path = TempFile(json);
            try
            {
                Assert.Throws<InvalidDataException>(() => CanonicalTrack.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Версия 2 без torso.roll в кадре — InvalidDataException.</summary>
        [Fact]
        public void TrackLoadRejectsMissingTorsoRoll()
        {
            const string json = @"{
  ""formatVersion"": 2,
  ""frames"": [
    { ""t"": 0.0, ""torso"": { ""lean"": 0.0, ""turn"": 0.0, ""basis"": [1,0,0, 0,1,0, 0,0,1] }, ""head"": { ""tilt"": 0.0 } }
  ]
}";
            var path = TempFile(json);
            try
            {
                Assert.Throws<InvalidDataException>(() => CanonicalTrack.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Загруженный кадр версии 2 несёт torso.roll и torso.basis (девять чисел, значения сохраняются).</summary>
        [Fact]
        public void TrackLoadKeepsTorsoRollAndBasis()
        {
            const string json = @"{
  ""formatVersion"": 2,
  ""frames"": [
    { ""t"": 0.0, ""torso"": { ""lean"": 0.1, ""turn"": 0.2, ""roll"": 0.3, ""basis"": [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9] }, ""head"": { ""tilt"": 0.0 } }
  ]
}";
            var path = TempFile(json);
            try
            {
                var frame = CanonicalTrack.Load(path).Frames[0];
                Assert.Equal(0.3, frame.TorsoRollRad, 9);
                Assert.True(frame.HasTorsoBasis);
                Assert.NotNull(frame.TorsoBasis);
                Assert.Equal(9, frame.TorsoBasis!.Length);
                for (var i = 0; i < 9; i++)
                {
                    Assert.Equal(0.1 * (i + 1), frame.TorsoBasis[i], 9);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Интерполяция версии 2 (S39): roll линейно, basis покомпонентно, середина даёт среднее арифметическое.</summary>
        [Fact]
        public void SampleInterpolatesTorsoRollAndBasis()
        {
            var bones = new double[CanonicalFrame.BoneCount * 3];
            var valid = new bool[CanonicalFrame.BoneCount];
            var basisA = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            var basisB = new double[] { 0, 1, 0, 1, 0, 0, 0, 0, 1 };
            var track = new CanonicalTrack(60, 0.8, new List<CanonicalFrame>
            {
                new CanonicalFrame(0.0, bones, valid, 0, 0, 0, 0, 0, 0, 0, 0.0, basisA),
                new CanonicalFrame(1.0, bones, valid, 0, 0, 0, 0, 0, 0, 0, 0.4, basisB),
            });

            var mid = track.Sample(0.5);
            Assert.Equal(0.2, mid.TorsoRollRad, 9);
            Assert.True(mid.HasTorsoBasis);
            Assert.NotNull(mid.TorsoBasis);
            Assert.Equal(9, mid.TorsoBasis!.Length);
            for (var i = 0; i < 9; i++)
            {
                Assert.Equal((basisA[i] + basisB[i]) / 2.0, mid.TorsoBasis[i], 9);
            }
        }

        /// <summary>Эталон пробы от импортёра T1: загрузка, число кадров, единичность костей. Без данных — пропуск.</summary>
        [Fact]
        public void ReferenceCanonicalJsonFromImporterLoads()
        {
            var path = FindReferenceFile();
            if (path == null)
            {
                return;
            }

            var track = CanonicalTrack.Load(path);
            Assert.Equal(443, track.Frames.Count);
            // SMPL-источник с T16; COCO давал 0.710-0.713
            Assert.InRange(track.LegLengthM, 0.670, 0.690);
            Assert.InRange(track.Frames[track.Frames.Count - 1].TSec, 7.366, 7.368);
            for (var k = 0; k < track.Frames.Count; k++)
            {
                var f = track.Frames[k];
                for (var i = 0; i < CanonicalFrame.BoneCount; i++)
                {
                    if (!f.BoneValid[i])
                    {
                        continue;
                    }

                    var x = f.Bones[i * 3];
                    var y = f.Bones[(i * 3) + 1];
                    var z = f.Bones[(i * 3) + 2];
                    var length = Math.Sqrt((x * x) + (y * y) + (z * z));
                    Assert.InRange(length, 1.0 - 1e-4, 1.0 + 1e-4);
                }
            }
        }

        private static string? FindReferenceFile()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "derived", "trial", "gLO_sBM_cAll_d14_mLO5_ch02.canonical.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>Инвариант trial S8: эталон против самого себя — максимум оценки и ноль ошибок.</summary>
        [Fact]
        public void SelfScoringGivesMaxScoreAndNoErrors()
        {
            var track = CanonSynth.Track(600, 60);
            var scorer = new TrialScorer(track);
            TrialScore last = null!;
            for (var k = 0; k <= 300; k++)
            {
                var t = k / 30.0;
                last = scorer.Update(t, CanonSynth.Motion(t));
            }

            Assert.InRange(last.Total01, 0.97, 1.0);
            Assert.InRange(last.Running01, 0.97, 1.0);
            Assert.Equal(0, last.Errors);
            Assert.Equal(1.0, last.Coverage, 12);
        }

        /// <summary>Инвариант trial S7: постоянный лаг внутри окна не занижает оценку, сдвиг его находит.</summary>
        [Theory]
        [InlineData(0.2)]
        [InlineData(-0.2)]
        public void ConstantLagWithinWindowDoesNotLowerScore(double lag)
        {
            var track = CanonSynth.Track(600, 60);
            var scorer = new TrialScorer(track);
            TrialScore last = null!;
            for (var k = 0; k <= 300; k++)
            {
                var t = k / 30.0;
                last = scorer.Update(t, CanonSynth.Motion(t - lag));
            }

            Assert.InRange(last.ShiftSec, lag - 0.06, lag + 0.06);
            Assert.True(last.Total01 > 0.9, "Total01=" + last.Total01.ToString("0.000"));
            Assert.True(last.Running01 > 0.95, "Running01=" + last.Running01.ToString("0.000"));
            Assert.Equal(0, last.Errors);
        }

        /// <summary>Статичный эталон: все кости (0,-1,0).</summary>
        private static CanonicalTrack StaticTrack() =>
            new CanonicalTrack(60, 0.7111, new List<CanonicalFrame> { CanonSynth.Tilted(0.0) }, "static", null);

        [Fact]
        public void ErrorCounterCountsErrorEpisodesNotFrames()
        {
            var scorer = new TrialScorer(StaticTrack());
            var t = 0.0;
            TrialScore s = null!;
            for (var k = 0; k < 5; k++)
            {
                t += 1.0 / 30.0;
                s = scorer.Update(t, CanonSynth.Tilted(1.0));
            }

            // непрерывная серия выше порога — один эпизод, одна ошибка (T14/P0.3, раньше +1 на кадр)
            Assert.Equal(1, s.Errors);

            // ниже порога счёт не растёт и эпизод закрывается
            Assert.Equal(1, scorer.Update(t += 1.0 / 30.0, CanonSynth.Tilted(0.5)).Errors);

            // после хорошей позы новая серия выше порога — вторая ошибка
            for (var k = 0; k < 5; k++)
            {
                t += 1.0 / 30.0;
                s = scorer.Update(t, CanonSynth.Tilted(1.0));
            }

            Assert.Equal(2, s.Errors);

            // выше порога при покрытии ниже минимума: ошибку не добавляет и эпизод закрывает
            var low = scorer.Update(t += 1.0 / 30.0, CanonSynth.Tilted(1.0, 2));
            Assert.Equal(2, low.Errors);
            Assert.Equal(0.25, low.Coverage, 12);

            // после закрытия эпизода покрытием очередная плохая серия — третья ошибка
            var next = scorer.Update(t += 1.0 / 30.0, CanonSynth.Tilted(1.0));
            Assert.Equal(3, next.Errors);
        }

        /// <summary>T14/P0.3: две секунды плохой позы (60 кадров при 30 кадр/с) — одна ошибка, не 60.</summary>
        [Fact]
        public void SixtyBadFramesCountAsOneError()
        {
            var scorer = new TrialScorer(StaticTrack());
            TrialScore s = null!;
            for (var k = 1; k <= 60; k++)
            {
                s = scorer.Update(k / 30.0, CanonSynth.Tilted(1.0));
            }

            Assert.Equal(1, s.Errors);
        }

        /// <summary>T14/P0.4: 3 валидные кости ниже MinCoverageBones — кадр непокрытый: без оценки и ошибок, покрытие наружу фактическое.</summary>
        [Fact]
        public void ThreeValidBonesBelowMinCoverageGiveNoScoreOrErrors()
        {
            var scorer = new TrialScorer(StaticTrack());
            var s = scorer.Update(1.0 / 30.0, CanonSynth.Tilted(1.0, 3));
            Assert.Equal(0, s.Errors);
            Assert.Equal(0.0, s.Total01, 12);
            Assert.Equal(0.0, s.Running01, 12);
            Assert.Equal(3.0 / 8.0, s.Coverage, 12);
        }

        /// <summary>T14/P0.4: ровно 4 валидные кости — кадр покрыт: оценка пишется, ошибка считается.</summary>
        [Fact]
        public void FourValidBonesAtMinCoverageScoreAndCountErrors()
        {
            var scorer = new TrialScorer(StaticTrack());
            var bad = scorer.Update(1.0 / 30.0, CanonSynth.Tilted(1.0, 4));
            Assert.Equal(1, bad.Errors);

            var mid = scorer.Update(2.0 / 30.0, CanonSynth.Tilted(0.55, 4));
            Assert.Equal(1, mid.Errors);
            Assert.True(mid.Total01 > 0.3, "Total01=" + mid.Total01.ToString("0.000"));
            Assert.Equal(0.5, mid.Coverage, 12);
        }

        [Fact]
        public void ZeroCoverageFramesDoNotScoreOrCountErrors()
        {
            var scorer = new TrialScorer(StaticTrack());
            var s = scorer.Update(1.0 / 30.0, CanonSynth.Tilted(1.0, 0));
            Assert.Equal(0, s.Errors);
            Assert.Equal(0.0, s.Total01, 12);
            Assert.Equal(0.0, s.Running01, 12);
            Assert.Equal(0.0, s.Coverage, 12);

            // после восстановления видимости оценка появляется
            var s2 = scorer.Update(2.0 / 30.0, CanonSynth.Tilted(0.0));
            Assert.True(s2.Total01 > 0.99);
            Assert.Equal(1.0, s2.Coverage, 12);
        }

        /// <summary>Инвариант «зеркальная перестановка сторон ухудшает»: у асимметричной позы подмена L/R даёт большое отклонение.</summary>
        [Fact]
        public void MirrorSwapWorsensDeviation()
        {
            var reference = Canonicalizer.ToCanonical(CanonSynth.Figure(), CanonSynth.Bones(), CanonSynth.Leg);
            var mirrored = CanonSynth.Mirror(reference);
            var self = TrialComparer.Compare(reference, reference);
            var swap = TrialComparer.Compare(reference, mirrored);
            Assert.True(self.MeanDeviationRad < 1e-12);
            Assert.InRange(swap.MeanDeviationRad, 0.7, 0.9);
            Assert.True(swap.MeanDeviationRad > self.MeanDeviationRad * 100.0);
        }
    }
}
