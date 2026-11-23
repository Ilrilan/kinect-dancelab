using System;
using System.Collections.Generic;

namespace DanceLab.View
{
    /// <summary>
    /// Расписание тура (S34, T34): равномерные моменты прогона по записи для числового
    /// отчёта тура, без привязки к окну и камерам — чистая математика.
    /// </summary>
    public static class TourSchedule
    {
        /// <summary>
        /// Равномерные моменты тура: (i + 1) / (count + 1) * durationSec для i = 0..count-1,
        /// каждый округляется до 0.1 с и зажимается в [0.2, max(0.2, durationSec - 0.2)]
        /// (не ближе 0.2 с к краям записи). Пустой список: count <= 0, durationSec не
        /// конечное число или durationSec <= 0.
        /// </summary>
        public static IReadOnlyList<double> Times(double durationSec, int count)
        {
            if (count <= 0 || !IsFinite(durationSec) || durationSec <= 0)
            {
                return Array.Empty<double>();
            }

            var max = Math.Max(0.2, durationSec - 0.2);
            var times = new List<double>(count);
            for (var i = 0; i < count; i++)
            {
                var raw = (i + 1) / (double)(count + 1) * durationSec;
                times.Add(Math.Max(0.2, Math.Min(max, Math.Round(raw, 1))));
            }

            return times;
        }

        /// <summary>Конечное число (не NaN и не бесконечность); net48 — без double.IsFinite.</summary>
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
