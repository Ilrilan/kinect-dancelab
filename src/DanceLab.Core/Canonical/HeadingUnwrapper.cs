using System;

namespace DanceLab.Core.Canonical
{
    /// <summary>
    /// Разворот направления взгляда по треку, кадр за кадром (specs/player/trial.md, S39).
    /// Ведёт непрерывный ряд по сырым азимутам, ветвь из {0, π} против скачков на 180°
    /// и коэффициент надёжности, глушащий шум сырого азимута при близости переда к вертикали.
    /// </summary>
    public sealed class HeadingUnwrapper
    {
        /// <summary>Порог смены ветви: |wrap(cand − h)| больше него — ветвь переворачивается и кандидат пересчитывается.</summary>
        public const double SwitchRad = 120.0 * Math.PI / 180.0;

        /// <summary>Полная надёжность: w ниже него сглаживание усиливается по формуле k = clamp(w / 0.25, 0.1, 1).</summary>
        public const double ReliabilityFloor = 0.25;

        /// <summary>Минимальный коэффициент подмешивания: даже при w = 0 кадр сдвигает направление не быстрее него.</summary>
        public const double MinGain = 0.1;

        private double? _unwrapped;   // h — предыдущее развёрнутое направление
        private double? _cont;        // cont — непрерывный ряд по сырым азимутам
        private double? _prevRaw;     // raw предыдущего кадра для приращения cont
        private double _branch;       // b — ветвь из {0, π}

        /// <summary>Последнее развёрнутое направление; null до первого Update, сбрасывается в Reset.</summary>
        public double? Current { get; private set; }

        /// <summary>Сырой азимут предыдущего кадра; null до первого Update, сбрасывается в Reset.</summary>
        public double? LastRaw { get; private set; }

        /// <summary>
        /// Принимает сырой азимут кадра и надёжность 0..1 (NaN считается нулём), возвращает развёрнутое
        /// направление: не свёрнутое в ±π, без скачков на 180°, при низкой надёжности — почти неподвижное.
        /// </summary>
        public double Update(double rawHeadingRad, double reliability)
        {
            var w = double.IsNaN(reliability) ? 0.0 : Clamp(reliability, 0.0, 1.0);

            if (_cont == null)
            {
                _cont = rawHeadingRad;
            }
            else
            {
                _cont += Wrap(rawHeadingRad - _prevRaw!.Value);
            }

            _prevRaw = rawHeadingRad;
            LastRaw = rawHeadingRad;

            var cand = _cont.Value + _branch;
            if (_unwrapped != null && Math.Abs(Wrap(cand - _unwrapped.Value)) > SwitchRad)
            {
                _branch = Math.PI - _branch;
                cand = _cont.Value + _branch;
            }

            double result;
            if (_unwrapped == null)
            {
                result = cand;
            }
            else
            {
                var k = Clamp(w / ReliabilityFloor, MinGain, 1.0);
                result = _unwrapped.Value + (k * Wrap(cand - _unwrapped.Value));
            }

            _unwrapped = result;
            Current = result;
            return result;
        }

        /// <summary>Возвращает в начальное состояние: следующий Update первым кадром даёт сам сырой азимут.</summary>
        public void Reset()
        {
            _unwrapped = null;
            _cont = null;
            _prevRaw = null;
            _branch = 0.0;
            Current = null;
            LastRaw = null;
        }

        /// <summary>Свёртка угла в интервал (−π, π]: (a + π) % 2π, при результате ≤ 0 добавить 2π, минус π.</summary>
        private static double Wrap(double a)
        {
            var x = (a + Math.PI) % (2.0 * Math.PI);
            if (x <= 0.0)
            {
                x += 2.0 * Math.PI;
            }

            return x - Math.PI;
        }

        /// <summary>Зажим значения в диапазон: Math.Clamp в net48 недоступен.</summary>
        private static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);
    }
}
