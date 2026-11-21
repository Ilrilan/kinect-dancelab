using System;
using System.Collections.Generic;
using DanceLab.Core.Reconstruction;

namespace DanceLab.Core.Canonical
{
    /// <summary>
    /// Перевод достроенной позы трекера в канонический вид (canonical-pose S1, S3, S7, S9, S10; player/trial Contract).
    /// Конвенции совпадают с tools/aist_import/canonical_export.py (эталонный путь пробы) знак в знак:
    /// вверх = normalize(шея − корень) (при потере шеи — грудь), бок = линия бёдер в сторону левого бедра,
    /// ортогонализованная по вверх, вперёд = бок × вверх (правая тройка); координаты костей (бок, вверх, вперёд);
    /// heading = atan2(вперёд.x, вперёд.z). Имена суставов — как у реконструкции: Kinect (ShoulderLeft),
    /// Nuitrack (LeftShoulder) и канонические (shoulder_l); chest/neck/head приходят от трекеров напрямую
    /// (SpineMid/Torso, Neck, Head). Перемещение корня — в долях длины ноги (S9), в осях текущего heading
    /// (вперёд — по взгляду, side — влево от взгляда); в метрику пробы корень не входит.
    /// Высота корня считается над полом сцены floorYm (S15: оценка пола живой сцены по лодыжкам),
    /// не над нулём сенсора; floorYm по умолчанию 0 — прежнее поведение (высота над y=0 сенсора).
    /// </summary>
    public static class Canonicalizer
    {
        private const int ShoulderL = 0;
        private const int ShoulderR = 1;
        private const int ElbowL = 2;
        private const int ElbowR = 3;
        private const int WristL = 4;
        private const int WristR = 5;
        private const int HipL = 6;
        private const int HipR = 7;
        private const int KneeL = 8;
        private const int KneeR = 9;
        private const int AnkleL = 10;
        private const int AnkleR = 11;

        /// <summary>Имена суставов у реконструкции: Kinect, Nuitrack и канонические.</summary>
        private static readonly string[][] Names =
        {
            new[] { "ShoulderLeft", "LeftShoulder", "shoulder_l" },
            new[] { "ShoulderRight", "RightShoulder", "shoulder_r" },
            new[] { "ElbowLeft", "LeftElbow", "elbow_l" },
            new[] { "ElbowRight", "RightElbow", "elbow_r" },
            new[] { "WristLeft", "LeftWrist", "wrist_l" },
            new[] { "WristRight", "RightWrist", "wrist_r" },
            new[] { "HipLeft", "LeftHip", "hip_l" },
            new[] { "HipRight", "RightHip", "hip_r" },
            new[] { "KneeLeft", "LeftKnee", "knee_l" },
            new[] { "KneeRight", "RightKnee", "knee_r" },
            new[] { "AnkleLeft", "LeftAnkle", "ankle_l" },
            new[] { "AnkleRight", "RightAnkle", "ankle_r" },
        };

        public static CanonicalFrame ToCanonical(ReconstructedPose pose, IReadOnlyDictionary<string, LimbLengths>? boneLengths, double legLengthM, double floorYm = 0)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            var leg = LegLength(legLengthM, boneLengths);
            var j = new ReconstructedJoint?[12];
            for (var i = 0; i < 12; i++)
            {
                j[i] = FindAny(pose, Names[i]);
            }

            var neck = FindAny(pose, "Neck", "neck");
            var chest = FindAny(pose, "SpineMid", "Torso", "Chest", "chest");
            var head = FindAny(pose, "Head", "head");

            // корень: SpineBase/Waist напрямую, иначе середина бёдер
            var rootJoint = FindAny(pose, "SpineBase", "Waist", "root");
            Vec3 rootPos = default;
            var rootOk = false;
            if (Usable(rootJoint))
            {
                rootPos = P(rootJoint!);
                rootOk = true;
            }
            else if (Usable(j[HipL]) && Usable(j[HipR]))
            {
                rootPos = (P(j[HipL]!) + P(j[HipR]!)) * 0.5;
                rootOk = true;
            }

            // ось вверх торса (S7): от корня к шее, при потере шеи — к груди
            var up = new Vec3(0, 1, 0);
            var upOk = false;
            if (rootOk)
            {
                Vec3 source = default;
                if (Usable(neck))
                {
                    source = P(neck!);
                }
                else if (Usable(chest))
                {
                    source = P(chest!);
                }

                var d = source - rootPos;
                if (d.Length > 1e-9)
                {
                    up = d.Normalized();
                    upOk = true;
                }
            }

            // боковая ось (S7): линия бёдер в сторону левого бедра; при потере бёдер — линия плеч
            var side = new Vec3(1, 0, 0);
            if (upOk && Usable(j[HipL]) && Usable(j[HipR]))
            {
                side = Orthogonalized(P(j[HipL]!) - P(j[HipR]!), up);
            }
            else if (upOk && Usable(j[ShoulderL]) && Usable(j[ShoulderR]))
            {
                side = Orthogonalized(P(j[ShoulderL]!) - P(j[ShoulderR]!), up);
            }
            else if (upOk)
            {
                side = Orthogonalized(side, up);
            }

            var forward = upOk ? Vec3.Cross(side, up) : new Vec3(0, 0, 1);
            if (upOk && side.Length < 1e-9)
            {
                // вырожденный случай (линия бёдер вдоль оси вверх): фрейм сохраняем определённым
                side = Orthogonalized(new Vec3(0, 0, 1), up);
                forward = Vec3.Cross(side, up);
            }

            var bones = new double[CanonicalFrame.BoneCount * 3];
            var valid = new bool[CanonicalFrame.BoneCount];
            EmitBone(bones, valid, 0, j[ShoulderL], j[ElbowL], side, up, forward, upOk);
            EmitBone(bones, valid, 1, j[ShoulderR], j[ElbowR], side, up, forward, upOk);
            EmitBone(bones, valid, 2, j[ElbowL], j[WristL], side, up, forward, upOk);
            EmitBone(bones, valid, 3, j[ElbowR], j[WristR], side, up, forward, upOk);
            EmitBone(bones, valid, 4, j[HipL], j[KneeL], side, up, forward, upOk);
            EmitBone(bones, valid, 5, j[HipR], j[KneeR], side, up, forward, upOk);
            EmitBone(bones, valid, 6, j[KneeL], j[AnkleL], side, up, forward, upOk);
            EmitBone(bones, valid, 7, j[KneeR], j[AnkleR], side, up, forward, upOk);

            var forwardH = new Vec3(forward.X, 0, forward.Z);
            var heading = 0.0;
            if (forwardH.Length > 1e-9)
            {
                forwardH = forwardH.Normalized();
                heading = Math.Atan2(forwardH.X, forwardH.Z);
            }
            else
            {
                forwardH = new Vec3(0, 0, 1);
            }

            // наклон торса от вертикали, вперёд — плюс (canonical_export.py:150-151)
            var lean = 0.0;
            if (upOk)
            {
                lean = Math.Atan2(Vec3.Dot(up, forwardH), Vec3.Dot(up, new Vec3(0, 1, 0)));
            }

            // скрутка торса: линия плеч относительно бёдерно-заданной системы (canonical_export.py:143-146)
            var turn = 0.0;
            if (upOk && Usable(j[ShoulderL]) && Usable(j[ShoulderR]))
            {
                var sl = Orthogonalized(P(j[ShoulderL]!) - P(j[ShoulderR]!), up);
                turn = Math.Atan2(Vec3.Dot(sl, forward), Vec3.Dot(sl, side));
            }

            var headTilt = 0.0;
            if (upOk && Usable(neck) && Usable(head))
            {
                var headDir = P(head!) - P(neck!);
                if (headDir.Length > 1e-9)
                {
                    headTilt = Vec3.Angle(headDir, up);
                }
            }

            double rootForward = 0, rootSide = 0, rootHeight = 0;
            if (rootOk)
            {
                var leftOfView = new Vec3(forwardH.Z, 0, -forwardH.X);
                rootForward = Vec3.Dot(rootPos, forwardH) / leg;
                rootSide = Vec3.Dot(rootPos, leftOfView) / leg;
                rootHeight = (rootPos.Y - floorYm) / leg; // S15: над полом сцены, не над нулём сенсора
            }

            return new CanonicalFrame(pose.DeviceTimeUs / 1e6, bones, valid, lean, turn, headTilt, rootForward, rootSide, rootHeight, heading);
        }

        private static double LegLength(double legLengthM, IReadOnlyDictionary<string, LimbLengths>? boneLengths)
        {
            if (legLengthM > 0 && !double.IsNaN(legLengthM) && !double.IsInfinity(legLengthM))
            {
                return legLengthM;
            }

            if (boneLengths != null && boneLengths.TryGetValue("LegLeft", out var l) && boneLengths.TryGetValue("LegRight", out var r))
            {
                var v = 0.5 * ((l.Upper + l.Lower) + (r.Upper + r.Lower));
                if (v > 0)
                {
                    return v;
                }
            }

            throw new ArgumentException("Длина ноги должна быть положительной (или должны быть заданы длины ног в boneLengths).", nameof(legLengthM));
        }

        private static ReconstructedJoint? FindAny(ReconstructedPose pose, params string[] names)
        {
            foreach (var name in names)
            {
                var joint = pose.Find(name);
                if (joint != null)
                {
                    return joint;
                }
            }

            return null;
        }

        /// <summary>Сустав достоверен, если он есть и не помечен Invalid (canonical-pose S3).</summary>
        private static bool Usable(ReconstructedJoint? joint) => joint != null && joint.Origin != JointOrigin.Invalid;

        private static Vec3 P(ReconstructedJoint joint) => new Vec3(joint.X, joint.Y, joint.Z);

        /// <summary>Составляющая вектора, перпендикулярная оси вверх, единичная.</summary>
        private static Vec3 Orthogonalized(Vec3 v, Vec3 up)
        {
            var w = v - (up * Vec3.Dot(v, up));
            return w.Normalized();
        }

        /// <summary>Направление кости в координатах торса; невалидные суставы или разваленный фрейм дают невалидную кость.</summary>
        private static void EmitBone(double[] bones, bool[] valid, int index, ReconstructedJoint? from, ReconstructedJoint? to, Vec3 side, Vec3 up, Vec3 forward, bool frameOk)
        {
            if (!frameOk || !Usable(from) || !Usable(to))
            {
                return;
            }

            var d = P(to!) - P(from!);
            if (d.Length < 1e-9)
            {
                return;
            }

            var n = d.Normalized();
            bones[index * 3] = Vec3.Dot(n, side);
            bones[(index * 3) + 1] = Vec3.Dot(n, up);
            bones[(index * 3) + 2] = Vec3.Dot(n, forward);
            valid[index] = true;
        }
    }
}
