using System;
using System.Collections.Generic;

namespace DanceLab.Core.Reconstruction
{
    /// <summary>Происхождение сустава в достроенной позе (specs/core/pose-reconstruction.md, S5).</summary>
    public enum JointOrigin
    {
        /// <summary>Измерен трекером и признан надёжным.</summary>
        Measured,

        /// <summary>Достроен по опоре (плечо и кисть известны, локоть найден по длинам костей).</summary>
        Reconstructed,

        /// <summary>Удержан или продолжен без опоры не дольше времени удержания.</summary>
        Extrapolated,

        /// <summary>Нет данных.</summary>
        Invalid,
    }

    public sealed class ReconstructedJoint
    {
        public ReconstructedJoint(string name, double x, double y, double z, JointOrigin origin, double confidence)
        {
            Name = name;
            X = x;
            Y = y;
            Z = z;
            Origin = origin;
            Confidence = confidence;
        }

        public string Name { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public JointOrigin Origin { get; }

        /// <summary>От 0 до 1. Measured: уверенность трекера или 1, Reconstructed: не больше 0,6, Extrapolated: от 0,4 до 0, Invalid: 0.</summary>
        public double Confidence { get; }
    }

    public sealed class ReconstructedPose
    {
        public ReconstructedPose(long deviceTimeUs, IReadOnlyList<ReconstructedJoint> joints)
        {
            DeviceTimeUs = deviceTimeUs;
            Joints = joints;
        }

        public long DeviceTimeUs { get; }
        public IReadOnlyList<ReconstructedJoint> Joints { get; }

        public ReconstructedJoint? Find(string name)
        {
            for (var i = 0; i < Joints.Count; i++)
            {
                if (Joints[i].Name == name)
                {
                    return Joints[i];
                }
            }

            return null;
        }
    }

    /// <summary>Пороги достройки. Начальные значения из реестра оценок E74-E80 (specs/estimates.md), задаются отдельно для каждого трекера (S10).</summary>
    public sealed class ReconstructionParameters
    {
        /// <summary>Порог второй разности положения локтя или колена, метры (E74). Бесконечность отключает проверку.</summary>
        public double JitterSecondDifferenceM { get; set; } = double.PositiveInfinity;

        /// <summary>Предел скорости сустава для проверки непрерывности, м/с (E75).</summary>
        public double MaxSpeedMps { get; set; } = 6.0;

        /// <summary>Предел сгиба в локте и колене, градусы (E76).</summary>
        public double MaxFlexionDeg { get; set; } = 150.0;

        /// <summary>Порог низкой уверенности Nuitrack (E77). Ниже порога сустав ненадёжен.</summary>
        public double LowConfidence { get; set; }

        /// <summary>Порог отклонения длины кости от выученной, метры (E78). Бесконечность отключает проверку.</summary>
        public double BoneDeviationM { get; set; } = double.PositiveInfinity;

        /// <summary>Время удержания последнего значения при потере, секунды (E80).</summary>
        public double HoldSeconds { get; set; } = 0.3;

        /// <summary>Сколько кадров с полностью надёжной конечностью нужно, чтобы выучить длины костей.</summary>
        public int MinSamplesForBones { get; set; } = 30;

        public static ReconstructionParameters ForKinect() => new ReconstructionParameters
        {
            JitterSecondDifferenceM = 0.015,
        };

        public static ReconstructionParameters ForNuitrack() => new ReconstructionParameters
        {
            LowConfidence = 0.3,
            BoneDeviationM = 0.03,
        };
    }

    /// <summary>Выученные длины костей конечности, метры.</summary>
    public readonly struct LimbLengths
    {
        public LimbLengths(double upper, double lower)
        {
            Upper = upper;
            Lower = lower;
        }

        /// <summary>Плечо или бедро.</summary>
        public double Upper { get; }

        /// <summary>Предплечье или голень.</summary>
        public double Lower { get; }
    }

    internal static class LimbNames
    {
        public const string ArmLeft = "ArmLeft";
        public const string ArmRight = "ArmRight";
        public const string LegLeft = "LegLeft";
        public const string LegRight = "LegRight";

        public static readonly string[] All = { ArmLeft, ArmRight, LegLeft, LegRight };

        /// <summary>Имена суставов конечности у трекера: опора, середина (локоть, колено), конец (кисть, лодыжка).</summary>
        public static (string Root, string Mid, string End) Joints(string trackerId, string limb)
        {
            var nuitrack = trackerId == "nuitrack";
            switch (limb)
            {
                case ArmLeft:
                    return nuitrack ? ("LeftShoulder", "LeftElbow", "LeftWrist") : ("ShoulderLeft", "ElbowLeft", "WristLeft");
                case ArmRight:
                    return nuitrack ? ("RightShoulder", "RightElbow", "RightWrist") : ("ShoulderRight", "ElbowRight", "WristRight");
                case LegLeft:
                    return nuitrack ? ("LeftHip", "LeftKnee", "LeftAnkle") : ("HipLeft", "KneeLeft", "AnkleLeft");
                case LegRight:
                    return nuitrack ? ("RightHip", "RightKnee", "RightAnkle") : ("HipRight", "KneeRight", "AnkleRight");
                default:
                    throw new ArgumentException("Неизвестная конечность: " + limb, nameof(limb));
            }
        }
    }
}
