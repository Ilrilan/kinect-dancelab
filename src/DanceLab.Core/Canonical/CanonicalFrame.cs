using System;
using System.Collections.Generic;

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

        public CanonicalFrame(double tSec, double[] bones, bool[] boneValid, double torsoLeanRad, double torsoTurnRad, double headTiltRad, double rootForward, double rootSide, double rootHeight, double headingRad, double torsoRollRad = 0, IReadOnlyList<double>? torsoBasis = null)
        {
            if (bones == null || bones.Length != BoneCount * 3)
            {
                throw new ArgumentException("Ожидаются 24 числа: по три на восемь костей.", nameof(bones));
            }

            if (boneValid == null || boneValid.Length != BoneCount)
            {
                throw new ArgumentException("Ожидаются восемь пометок валидности костей.", nameof(boneValid));
            }

            if (torsoBasis != null && torsoBasis.Count != 9)
            {
                throw new ArgumentException("Ожидаются девять чисел basis: построчно left, up, fwd.", nameof(torsoBasis));
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
            TorsoRollRad = torsoRollRad;
            if (torsoBasis != null)
            {
                var basisCopy = new double[9];
                for (var i = 0; i < 9; i++)
                {
                    basisCopy[i] = torsoBasis[i];
                }

                TorsoBasis = basisCopy;
            }
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

        /// <summary>Боковой наклон торса, радианы (S39): atan2(−up·L, up·U1), где U1 = Y·cos(lean) + F·sin(lean).</summary>
        public double TorsoRollRad { get; }

        /// <summary>Оси торса в координатах (L, Y, F): построчно left, up, fwd — девять чисел; null, если не заданы (S39).</summary>
        public double[]? TorsoBasis { get; }

        /// <summary>Задан ли torso.basis: тогда оси торса берутся из него, а формулы по углам не применяются.</summary>
        public bool HasTorsoBasis => TorsoBasis != null;

        /// <summary>Направление кости по имени; null, если имени нет.</summary>
        public (double X, double Y, double Z)? Bone(string name)
        {
            var i = Array.IndexOf(BoneNames, name);
            return i < 0 ? ((double X, double Y, double Z)?)null : (Bones[i * 3], Bones[(i * 3) + 1], Bones[(i * 3) + 2]);
        }

        /// <summary>
        /// Мировые оси торса (S39): при заданном torso.basis строки basis умножаются на (L, Y, F),
        /// иначе оси строятся по lean/roll вокруг вертикали и heading; тройка реортонормализуется.
        /// </summary>
        public TorsoFrame TorsoFrame()
        {
            var fwdH = new Vec3(Math.Sin(HeadingRad), 0, Math.Cos(HeadingRad));
            var y = new Vec3(0, 1, 0);
            var l = Vec3.Cross(y, fwdH);

            Vec3 left, up;
            if (TorsoBasis != null)
            {
                var b = TorsoBasis;
                left = (l * b[0]) + (y * b[1]) + (fwdH * b[2]);
                up = (l * b[3]) + (y * b[4]) + (fwdH * b[5]);
            }
            else
            {
                var u1 = (y * Math.Cos(TorsoLeanRad)) + (fwdH * Math.Sin(TorsoLeanRad));
                up = (u1 * Math.Cos(TorsoRollRad)) - (l * Math.Sin(TorsoRollRad));
                left = (l * Math.Cos(TorsoRollRad)) + (u1 * Math.Sin(TorsoRollRad));
            }

            left = left.Normalized();
            if (left.Length < 1e-9)
            {
                return new TorsoFrame(l, y, fwdH);
            }

            up = (up - (left * Vec3.Dot(up, left))).Normalized();
            var fwd = Vec3.Cross(left, up);
            return new TorsoFrame(left, up, fwd);
        }
    }
}
