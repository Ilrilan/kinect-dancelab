using System;

namespace DanceLab.Core.Canonical
{
    /// <summary>
    /// Каноническая поза на момент времени (canonical-pose S1, S7, S9; player/trial Contract).
    /// Восемь направлений костей единичной длины в системе координат торса; наклоны, heading и
    /// положение корня в долях длины ноги несут информацию для призрака и в метрику пробы не входят.
    /// </summary>
    public sealed class CanonicalFrame
    {
        public const int BoneCount = 8;

        /// <summary>Имена костей в порядке индексов всех массивов позы.</summary>
        public static readonly string[] BoneNames = { "uArmL", "uArmR", "lArmL", "lArmR", "thighL", "thighR", "shinL", "shinR" };

        public CanonicalFrame(double tSec, double[] bones, bool[] boneValid, double torsoLeanRad, double torsoTurnRad, double headTiltRad, double rootForward, double rootSide, double rootHeight, double headingRad)
        {
            if (bones == null || bones.Length != BoneCount * 3)
            {
                throw new ArgumentException("Ожидаются 24 числа: по три на восемь костей.", nameof(bones));
            }

            if (boneValid == null || boneValid.Length != BoneCount)
            {
                throw new ArgumentException("Ожидаются восемь пометок валидности костей.", nameof(boneValid));
            }

            TSec = tSec;
            Bones = (double[])bones.Clone();
            BoneValid = (bool[])boneValid.Clone();
            TorsoLeanRad = torsoLeanRad;
            TorsoTurnRad = torsoTurnRad;
            HeadTiltRad = headTiltRad;
            RootForward = rootForward;
            RootSide = rootSide;
            RootHeight = rootHeight;
            HeadingRad = headingRad;
        }

        public double TSec { get; }

        /// <summary>Направления костей в системе торса, 24 числа: по три (x — бок влево, y — вверх, z — вперёд) на кость в порядке BoneNames. Валидная кость единичная.</summary>
        public double[] Bones { get; }

        /// <summary>Кость валидна, если достоверны все суставы, участвующие в её расчёте (canonical-pose S3); невалидная кость исключается из сравнения.</summary>
        public bool[] BoneValid { get; }

        /// <summary>Наклон оси вверх торса от вертикали, радиан.</summary>
        public double TorsoLeanRad { get; }

        /// <summary>Поворот линии плеч-бёдер относительно направления взгляда (скрутка вокруг вертикали), радиан.</summary>
        public double TorsoTurnRad { get; }

        /// <summary>Наклон головы: угол направления голова-шея от оси вверх торса, радиан.</summary>
        public double HeadTiltRad { get; }

        /// <summary>Корень вперёд по направлению взгляда, доли длины ноги (S9).</summary>
        public double RootForward { get; }

        /// <summary>Корень влево от направления взгляда, доли длины ноги (S9).</summary>
        public double RootSide { get; }

        /// <summary>Корень над полом, доли длины ноги (S9).</summary>
        public double RootHeight { get; }

        /// <summary>Азимут направления взгляда от оси +Z, радиан.</summary>
        public double HeadingRad { get; }

        /// <summary>Направление кости по имени; null, если имени нет.</summary>
        public (double X, double Y, double Z)? Bone(string name)
        {
            var i = Array.IndexOf(BoneNames, name);
            return i < 0 ? ((double X, double Y, double Z)?)null : (Bones[i * 3], Bones[(i * 3) + 1], Bones[(i * 3) + 2]);
        }
    }
}
