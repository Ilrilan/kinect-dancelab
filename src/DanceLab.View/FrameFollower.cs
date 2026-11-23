using System;
using System.Collections.Generic;

namespace DanceLab.View
{
    /// <summary>
    /// Кадры одной камеры из <seq>.cams.json (следящий кадр, S21/T12): строки
    /// [t, rootU, rootV, uMin, vMin, uMax, vMax] — root (u,v) и bbox танцора в пикселях
    /// кадра, t в секундах, отсортированы по t (так пишет импортёр).
    /// </summary>
    public sealed class CamTrack
    {
        private readonly double[][] rows; // [t, rootU, rootV, uMin, vMin, uMax, vMax]

        public CamTrack(double[][] rows)
        {
            this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        /// <summary>Число кадров трека.</summary>
        public int Count => rows.Length;

        /// <summary>
        /// Расширенный по времени bbox танцора: объединение bbox кадров трека в окне
        /// t ± windowSec (гладкость без пампинга на махах); false, если данных нет.
        /// </summary>
        public bool TryGetBox(double t, double windowSec, out (double UMin, double VMin, double UMax, double VMax) box)
        {
            box = default;
            if (rows.Length == 0)
            {
                return false;
            }

            var lo = 0;
            var hi = rows.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (rows[mid][0] < t - windowSec)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            var uMin = double.MaxValue;
            var vMin = double.MaxValue;
            var uMax = double.MinValue;
            var vMax = double.MinValue;
            for (var i = lo; i < rows.Length && rows[i][0] <= t + windowSec; i++)
            {
                uMin = Math.Min(uMin, rows[i][3]);
                vMin = Math.Min(vMin, rows[i][4]);
                uMax = Math.Max(uMax, rows[i][5]);
                vMax = Math.Max(vMax, rows[i][6]);
            }

            if (uMin > uMax)
            {
                return false; // в окне не оказалось ни одного кадра (берём fallback)
            }

            box = (uMin, vMin, uMax, vMax);
            return true;
        }
    }

    /// <summary>
    /// Следящий кадр (S21, T12): авто-зум и панорама видео по bbox танцора из cams.json
    /// текущей камеры. zAuto = clamp(min(winW/(bw+pad), winH/(bh+pad)), 1, 3), pad = 25% габарита:
    /// вертикальный паддинг асимметричный (верхней кромке 40%, нижней 60%), низ bbox расширен
    /// на ShoePadRows строк (обувь ниже лодыжек-ключевых точек), окно панели в пикселях
    /// источника = win/(s); итог z = zAuto * слайдер (сглаженные 0.2/тик), предел
    /// MaxTotalZoom = 9.0 (3.0 авто × 3.0 слайдер). Окно центрируется
    /// на сглаженном центре паддингованного bbox со сдвигом вверх 7% высоты окна (запас голове):
    /// Tx = -z*s*(cu - W/2), Ty = -z*s*(cv_цель - H/2). Клампы панорамы: окно не выходит за края
    /// контента. Нет камеры/данных — fallback: база zAuto = 1.6, центр = центр кадра.
    /// Чистая математика без WPF-типов (T22, аудит P2.12): окно и видео задаются числами,
    /// состояние сглаживания внутри; формулы перенесены дословно из tools/player_trial/MainWindow.cs.
    /// </summary>
    public sealed class FrameFollower
    {
        public const double AutoZoomFallback = 1.6; // база авто-зума без cams.json (прежнее умолчание)
        public const double BboxWindowSec = 0.7;    // расширение bbox по времени (гладкость, без пампинга на махах)
        public const double ShoePadRows = 75.0;     // обувь ниже лодыжек-ключевых точек, строк источника (худший кадр c05: выпад ~66)
        public const double FootRows = 30.0;        // стопа ниже ключевой точки лодыжки, строк источника (типичная обувь; ShoePadRows остаётся запасом кадрирования)
        public const double HeadShiftFrac = 0.07;   // сдвиг центра окна вверх (запас голове), доли высоты окна
        public const double MaxTotalZoom = 9.0;     // предел итогового зума: 3.0 (авто) × 3.0 (слайдер)

        private readonly Dictionary<string, CamTrack> cameras;
        private double smoothedZoomAuto = AutoZoomFallback;
        private double smoothedCenterU = double.NaN;
        private double smoothedCenterV = double.NaN;
        private double smoothedPanX;
        private double smoothedPanY;

        public FrameFollower(Dictionary<string, CamTrack> cameras)
        {
            this.cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
        }

        /// <summary>Сброс сглаживания (видео не готово): зум = fallback*слайдер, панорама 0.</summary>
        public void Reset(double sliderZoom)
        {
            smoothedPanX = 0;
            smoothedPanY = 0;
            smoothedZoomAuto = AutoZoomFallback * sliderZoom;
        }

        /// <summary>
        /// Один тик следящего кадра: t — текущий момент (с), camera — ключ камеры cams.json,
        /// windowW/windowH — окно панели (ширина контента и высота панели, пиксели элемента),
        /// videoW/videoH — размер источника, sliderZoom — множитель слайдера (округлён и зажат
        /// 1..3 вызывающим). Возвращает итоговый зум (единый по X/Y), панораму в пикселях окна
        /// и рамку танцора ScreenBox в пикселях окна тем же отображением, которое окно применяет
        /// к видео (масштаб z от центра элемента, затем панорама): Bottom — низ bbox с запасом
        /// ShoePadRows, FeetY — линия обуви танцора (S32), Bottom − (ShoePadRows − FootRows)·s·z;
        /// без данных — default.
        /// </summary>
        public (double Zoom, double PanX, double PanY, ScreenBox Box) Update(
                double t, string? camera, double windowW, double windowH,
                double videoW, double videoH, double sliderZoom)
        {
            var wSrc = videoW;
            var hSrc = videoH;
            var s = Math.Min(windowW / wSrc, windowH / hSrc); // контент в пикселях элемента
            var winW = windowW; // панель ширины контента (медиа заполняет окно по ширине)

            // авто-кадр: расширенный bbox, цель зума по габариту танцора с запасом pad = 25%
            var hasBox = TryGetAutoFrame(t, camera, out var box);
            double zAutoTarget;
            double cUTarget;
            double cVTarget;
            if (hasBox)
            {
                var vMin = box.VMin;
                var vMax = box.VMax + ShoePadRows; // bbox по лодыжкам: обувь ниже, расширяем низ
                var bw = (box.UMax - box.UMin) * 1.25;                    // паддинг 25%: по половине на борт
                var bh = (vMax - vMin) * 1.25;                            // сверху 40% паддинга, снизу 60%
                zAutoTarget = Math.Max(1.0, Math.Min(3.0, Math.Min((winW / s) / bw, (windowH / s) / bh)));
                cUTarget = (box.UMin + box.UMax) / 2.0;
                // центр паддингованного бокса: [vMin-0.4p, vMax+0.6p] -> центр = центр bbox + 0.1*p
                cVTarget = (vMin + vMax) / 2.0 + (0.10 * 0.25 * (vMax - vMin));
            }
            else
            {
                zAutoTarget = AutoZoomFallback;
                cUTarget = wSrc / 2.0;
                cVTarget = hSrc / 2.0;
            }

            if (double.IsNaN(smoothedCenterU))
            {
                smoothedCenterU = cUTarget; // первый валидный тик: без проползания из нуля
                smoothedCenterV = cVTarget;
            }
            else
            {
                smoothedCenterU += 0.2 * (cUTarget - smoothedCenterU);
                smoothedCenterV += 0.2 * (cVTarget - smoothedCenterV);
            }

            smoothedZoomAuto += 0.2 * (zAutoTarget - smoothedZoomAuto);
            var z = Math.Max(1.0, Math.Min(MaxTotalZoom, smoothedZoomAuto * sliderZoom));

            // панорама: окно центрируется на паддингованном bbox со сдвигом вверх 7% высоты окна
            var maxPanX = Math.Max(0.0, ((z * s * wSrc) - winW) / 2.0);
            var maxPanY = Math.Max(0.0, ((z * s * hSrc) - windowH) / 2.0);
            var vTarget = smoothedCenterV - (HeadShiftFrac * windowH / (s * z));
            var targetX = Math.Max(-maxPanX, Math.Min(maxPanX, -z * s * (smoothedCenterU - (wSrc / 2.0))));
            var targetY = Math.Max(-maxPanY, Math.Min(maxPanY, -z * s * (vTarget - (hSrc / 2.0))));

            smoothedPanX += 0.2 * (targetX - smoothedPanX);
            smoothedPanY += 0.2 * (targetY - smoothedPanY);

            // рамка танцора в пикселях окна: то же отображение, которое окно применяет к видео
            // (масштаб z от центра элемента, затем сдвиг на панораму; контент центрирован)
            ScreenBox screenBox;
            if (hasBox)
            {
                var vMaxPad = box.VMax + ShoePadRows;
                var bottom = windowH / 2.0 + (vMaxPad - videoH / 2.0) * s * z + smoothedPanY;
                // линия обуви танцора (S32): из того же s и z, низ рамки минус запас кадрирования сверх обуви
                var feetY = bottom - ((ShoePadRows - FootRows) * s * z);
                screenBox = new ScreenBox(
                        windowW / 2.0 + (box.UMin - videoW / 2.0) * s * z + smoothedPanX,
                        windowH / 2.0 + (box.VMin - videoH / 2.0) * s * z + smoothedPanY,
                        windowW / 2.0 + (box.UMax - videoW / 2.0) * s * z + smoothedPanX,
                        bottom,
                        feetY);
            }
            else
            {
                screenBox = default; // камеры/данных нет — рамки нет
            }

            return (z, smoothedPanX, smoothedPanY, screenBox);
        }

        /// <summary>Расширенный по времени bbox танцора текущей камеры; false, если камеры/данных нет.</summary>
        private bool TryGetAutoFrame(double t, string? camera, out (double UMin, double VMin, double UMax, double VMax) box)
        {
            box = default;
            return cameras.TryGetValue(camera ?? string.Empty, out var track)
                    && track.TryGetBox(t, BboxWindowSec, out box);
        }
    }
}
