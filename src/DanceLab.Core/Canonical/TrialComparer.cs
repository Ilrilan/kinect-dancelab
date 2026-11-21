using System;

namespace DanceLab.Core.Canonical
{
    /// <summary>Результат сравнения двух канонических поз (player/trial Contract).</summary>
    public sealed class TrialComparison
    {
        public TrialComparison(double[] boneDeviationRad, double meanDeviationRad, double coverage)
        {
            BoneDeviationRad = boneDeviationRad;
            MeanDeviationRad = meanDeviationRad;
            Coverage = coverage;
        }

        /// <summary>Углы между костями эталона и пользователя в радианах, в порядке CanonicalFrame.BoneNames; для костей вне сравнения 0.</summary>
        public double[] BoneDeviationRad { get; }

        /// <summary>Средний угол по костям, участвовавшим в сравнении, без мёртвой зоны; 0, если сравнивать нечего.</summary>
        public double MeanDeviationRad { get; }

        /// <summary>Доля костей, участвовавших в сравнении: валидных и у эталона, и у пользователя (trial S9).</summary>
        public double Coverage { get; }
    }

    /// <summary>Помоментное сравнение двух канонических поз: углы между одноимёнными костями (trial S4, S6).</summary>
    public static class TrialComparer
    {
        public static TrialComparison Compare(CanonicalFrame reference, CanonicalFrame user)
        {
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var deviations = new double[CanonicalFrame.BoneCount];
            var sum = 0.0;
            var compared = 0;
            for (var i = 0; i < CanonicalFrame.BoneCount; i++)
            {
                if (!reference.BoneValid[i] || !user.BoneValid[i])
                {
                    continue;
                }

                var a = new Vec3(reference.Bones[i * 3], reference.Bones[(i * 3) + 1], reference.Bones[(i * 3) + 2]);
                var b = new Vec3(user.Bones[i * 3], user.Bones[(i * 3) + 1], user.Bones[(i * 3) + 2]);
                var angle = Vec3.Angle(a, b);
                deviations[i] = angle;
                sum += angle;
                compared++;
            }

            return new TrialComparison(deviations, compared > 0 ? sum / compared : 0.0, compared / (double)CanonicalFrame.BoneCount);
        }
    }
}
