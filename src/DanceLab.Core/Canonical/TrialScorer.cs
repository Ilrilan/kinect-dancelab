using System;
using System.Collections.Generic;

namespace DanceLab.Core.Canonical
{
    /// <summary>Пробные пороги оценки (player/trial S5, S7; значения из брифа до натанцовки, меняются реестром оценок).</summary>
    public sealed class TrialParams
    {
        /// <summary>Мёртвая зона среднего отклонения, радиан: меньше — оценка максимальная.</summary>
        public double DeadZone { get; set; } = 0.35;

        /// <summary>Насыщение, радиан: среднее отклонение от него и больше даёт нулевую оценку.</summary>
        public double Saturation { get; set; } = 1.0;

        /// <summary>Порог грубой ошибки по среднему отклонению (без мёртвой зоны), радиан.</summary>
        public double ErrorThreshold { get; set; } = 0.8;

        /// <summary>Полуширина окна поиска временного сдвига, секунды.</summary>
        public double ShiftWindow { get; set; } = 0.35;

        /// <summary>Максимальная скорость изменения применённого сдвига, секунды за секунду.</summary>
        public double ShiftRate { get; set; } = 0.1;

        /// <summary>Окно бегущего процента, секунды.</summary>
        public double RunningWindow { get; set; } = 3.0;

        /// <summary>Минимальное число костей в сравнении для оценки и счётчика ошибок (T14/P0.4): ниже кадр непокрыт, как при Coverage 0.</summary>
        public int MinCoverageBones { get; set; } = 4;
    }

    /// <summary>Снимок оценки на момент Update (player/trial S5).</summary>
    public sealed class TrialScore
    {
        public TrialScore(double running01, double total01, int errors, double coverage, double shiftSec)
        {
            Running01 = running01;
            Total01 = total01;
            Errors = errors;
            Coverage = coverage;
            ShiftSec = shiftSec;
        }

        /// <summary>Бегущий процент совпадения за последние RunningWindow секунд, от 0 до 1.</summary>
        public double Running01 { get; }

        /// <summary>Накопленный итог с начала трека, от 0 до 1.</summary>
        public double Total01 { get; }

        /// <summary>Счётчик грубых ошибок-эпизодов (trial S5; T14/P0.3): одна непрерывная серия кадров со средним отклонением выше ErrorThreshold при покрытии не ниже MinCoverageBones считается одной ошибкой.</summary>
        public int Errors { get; }

        /// <summary>Покрытие последнего сравнения: доля костей, участвовавших в сравнении.</summary>
        public double Coverage { get; }

        /// <summary>Применённый временной сдвиг пользователя, секунды: эталон сэмплируется на tSec − ShiftSec.</summary>
        public double ShiftSec { get; }
    }

    /// <summary>
    /// Помоментная оценка пользователя против эталонной дорожки (player/trial S4-S9). Состояние на одного
    /// человека, Update вызывается по порядку времени по часам видео. Лучший сдвиг ищется на каждом шаге
    /// по сетке в окне ±ShiftWindow; применённый сдвиг следует за лучшим не быстрее ShiftRate. Мёртвая зона
    /// и насыщение переводят среднее отклонение в оценку от 0 до 1; грубые ошибки считаются по среднему
    /// без мёртвой зоны. Кадры, где в сравнении меньше MinCoverageBones костей (в том числе покрытие 0),
    /// считаются непокрытыми: ошибок не дают и в оценку не попадают. Ошибки дебаунсятся (T14/P0.3):
    /// +1 только при переходе в эпизод выше порога, эпизод закрывается падением среднего ниже ErrorThreshold
    /// или провалом покрытия.
    /// </summary>
    public sealed class TrialScorer
    {
        /// <summary>Число шагов сетки поиска сдвига (шаг окна 0.05 с при ShiftWindow 0.35).</summary>
        private const int ShiftSteps = 14;

        private readonly CanonicalTrack reference;
        private readonly TrialParams p;
        private readonly List<(double T, double Score)> history = new List<(double, double)>();
        private double shift;
        private double totalSum;
        private int totalN;
        private int errors;
        private bool errorEpisode;
        private double lastT = double.NaN;

        public TrialScorer(CanonicalTrack reference, TrialParams? parameters = null)
        {
            this.reference = reference ?? throw new ArgumentNullException(nameof(reference));
            p = parameters ?? new TrialParams();
        }

        public TrialScore Update(double tSec, CanonicalFrame user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var dt = double.IsNaN(lastT) || tSec <= lastT || (tSec - lastT) > 0.5 ? 0 : tSec - lastT;
            lastT = tSec;

            // поиск лучшего сдвига в окне (trial S7): минимум среднего отклонения по сетке;
            // при равенстве остаётся текущий сдвиг, чтобы не дрейфовал на статичной позе
            var best = shift;
            var bestMean = TrialComparer.Compare(reference.Sample(tSec - shift), user).MeanDeviationRad;
            for (var k = 0; k <= ShiftSteps; k++)
            {
                var candidate = -p.ShiftWindow + ((2.0 * p.ShiftWindow * k) / ShiftSteps);
                var mean = TrialComparer.Compare(reference.Sample(tSec - candidate), user).MeanDeviationRad;
                if (mean < bestMean - 1e-12)
                {
                    bestMean = mean;
                    best = candidate;
                }
            }

            if (dt > 0)
            {
                var maxStep = p.ShiftRate * dt;
                if (best > shift + maxStep)
                {
                    best = shift + maxStep;
                }

                if (best < shift - maxStep)
                {
                    best = shift - maxStep;
                }
            }

            shift = best;

            var comparison = TrialComparer.Compare(reference.Sample(tSec - shift), user);
            var effective = Math.Max(0, Math.Min(comparison.MeanDeviationRad, p.Saturation) - p.DeadZone);
            var span = Math.Max(p.Saturation - p.DeadZone, 1e-9);
            var score = 1.0 - (effective / span);

            // порог покрытия (T14/P0.4): костей в сравнении меньше минимума — кадр непокрытый,
            // как при Coverage 0: без оценки и без ошибок; Coverage наружу отдаётся фактический
            var comparedBones = (int)Math.Round(comparison.Coverage * CanonicalFrame.BoneCount);
            var covered = comparedBones >= p.MinCoverageBones;
            if (covered)
            {
                history.Add((tSec, score));
                totalSum += score;
                totalN++;
                while (history.Count > 0 && history[0].T < tSec - p.RunningWindow)
                {
                    history.RemoveAt(0);
                }
            }

            // дебаунс ошибок (T14/P0.3): +1 только на переходе в эпизод выше порога;
            // эпизод закрывается падением среднего ниже ErrorThreshold или провалом покрытия
            if (comparison.MeanDeviationRad > p.ErrorThreshold && covered)
            {
                if (!errorEpisode)
                {
                    errors++;
                    errorEpisode = true;
                }
            }
            else
            {
                errorEpisode = false;
            }

            var running = 0.0;
            foreach (var entry in history)
            {
                running += entry.Score;
            }

            return new TrialScore(
                history.Count > 0 ? running / history.Count : 0.0,
                totalN > 0 ? totalSum / totalN : 0.0,
                errors,
                comparison.Coverage,
                shift);
        }
    }
}
