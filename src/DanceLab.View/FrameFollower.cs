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

        /// <summary>
        /// Root (u,v) танцора кадра с временем ровно t (отчёт тура, S34): тот же двоичный поиск,
        /// что в TryGetBox с окном 0; false, если кадра с таким t нет.
        /// </summary>
        public bool TryGetRoot(double t, out (double U, double V) root)
        {
            root = default;
            if (rows.Length == 0)
            {
                return false;
            }

            var lo = 0;
            var hi = rows.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (rows[mid][0] < t)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            if (lo >= rows.Length || rows[lo][0] != t)
            {
                return false;
            }

            root = (rows[lo][1], rows[lo][2]);
            return true;
        }

        /// <summary>
        /// Кадр, ближайший к t по |t′ − t| (T44/S42): округление времён в файле до четырёх
        /// знаков (31.9667) и тики по таймеру (числа вида 31.966666…) делают точное
        /// совпадение редким — TryGetBox с окном 0 промахивается. Двоичный поиск позиции
        /// вставки, сравнение двух соседних кадров; при равных расстояниях берётся более
        /// ранний. false, если трек пуст или ближайший кадр дальше maxGapSec.
        /// </summary>
        public bool TryGetNearest(
                double t, out double tFrame,
                out (double UMin, double VMin, double UMax, double VMax) box,
                double maxGapSec = 0.05)
        {
            tFrame = default;
            box = default;
            var index = IndexOfNearest(t);
            if (index < 0 || Math.Abs(rows[index][0] - t) > maxGapSec)
            {
                return false;
            }

            tFrame = rows[index][0];
            box = (rows[index][3], rows[index][4], rows[index][5], rows[index][6]);
            return true;
        }

        /// <summary>
        /// Root (u,v) танцора кадра, ближайшего к t (T44/S42): тот же поиск ближайшего кадра,
        /// что в <see cref="TryGetNearest"/>; false, если трек пуст или ближайший кадр дальше
        /// maxGapSec. Точное совпадение времени (<see cref="TryGetRoot"/>) остаётся для
        /// отчёта тура (S34).
        /// </summary>
        public bool TryGetRootNearest(double t, out (double U, double V) root, double maxGapSec = 0.05)
        {
            root = default;
            var index = IndexOfNearest(t);
            if (index < 0 || Math.Abs(rows[index][0] - t) > maxGapSec)
            {
                return false;
            }

            root = (rows[index][1], rows[index][2]);
            return true;
        }

        /// <summary>Индекс кадра, ближайшего к t по |t′ − t|; −1 для пустого трека.</summary>
        private int IndexOfNearest(double t)
        {
            if (rows.Length == 0)
            {
                return -1;
            }

            var lo = 0;
            var hi = rows.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (rows[mid][0] < t)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            // lo — первый кадр с t′ >= t: ближайший из него и предыдущего
            if (lo >= rows.Length)
            {
                return rows.Length - 1;
            }

            if (lo == 0)
            {
                return 0;
            }

            return t - rows[lo - 1][0] <= rows[lo][0] - t ? lo - 1 : lo;
        }
    }

    /// <summary>
    /// Цель и пределы панорамы последнего тика <see cref="FrameFollower.Update"/> (S34, T34):
    /// числовой отчёт тура — zAutoTarget и maxPanX/maxPanY каждого тика записываются, чтобы
    /// тур мог дописать их в отчёт; на поведение следящего кадра значения не влияют.
    /// </summary>
    public readonly struct FrameTarget
    {
        /// <summary>Цель авто-зума тика до сглаживания: по рамке либо AutoZoomFallback в запасном виде.</summary>
        public readonly double ZAutoTarget;

        /// <summary>Предел панорамы тика по горизонтали, пиксели окна (окно не выходит за края контента).</summary>
        public readonly double MaxPanX;

        /// <summary>Предел панорамы тика по вертикали, пиксели окна.</summary>
        public readonly double MaxPanY;

        public FrameTarget(double zAutoTarget, double maxPanX, double maxPanY)
        {
            ZAutoTarget = zAutoTarget;
            MaxPanX = maxPanX;
            MaxPanY = maxPanY;
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
    /// Числовой отчёт тура (S34, T34): каждый Update заполняет LastTarget — цель авто-зума
    /// тика до сглаживания и пределы панорамы тика (<see cref="FrameTarget"/>), для отчёта тура.
    /// S42: цель центра панорамы — по рамке кадра, ближайшего к t (округлённые времена
    /// cams.json с тиками таймера точного совпадения не дают), не по объединённой рамке
    /// окна ±0.7 с; шаг сглаживания центра и панорамы растёт с множителем
    /// слайдера (0.2 при 1, до 0.5 при 3), чтобы на большом зуме окно успевало за танцором.
    /// </summary>
    public sealed class FrameFollower
    {
        public const double AutoZoomFallback = 1.6; // база авто-зума без cams.json (прежнее умолчание)
        public const double BboxWindowSec = 0.7;    // расширение bbox по времени (гладкость, без пампинга на махах)
        public const double ShoePadRows = 75.0;     // обувь ниже лодыжек-ключевых точек, строк источника (худший кадр c05: выпад ~66)
        public const double FootRows = 30.0;        // стопа ниже ключевой точки лодыжки, строк источника (типичная обувь; ShoePadRows остаётся запасом кадрирования)
        public const double HeadShiftFrac = 0.07;   // сдвиг центра окна вверх (запас голове), доли высоты окна
        public const double MaxTotalZoom = 9.0;     // предел итогового зума: 3.0 (авто) × 3.0 (слайдер)

        /// <summary>
        /// Числовой отчёт тура (S34, T34): цель авто-зума последнего тика Update до сглаживания
        /// и пределы панорамы этого тика; заполняется в конце каждого Update, поведение не меняет.
        /// </summary>
        public FrameTarget LastTarget { get; private set; }

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
            var hasBox = TryGetAutoFrame(t, camera, BboxWindowSec, out var box);
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

                // S42/T44: цель центра — по рамке кадра, ближайшего к t: времена в cams.json
                // округлены до 4 знаков, тики таймера точного совпадения с ними не дают;
                // ближайшего кадра нет (пустой трек / дальше maxGapSec) — остаётся центр
                // объединённой рамки
                if (TryGetNearestFrame(t, camera, out var curBox))
                {
                    var curVMin = curBox.VMin;
                    var curVMax = curBox.VMax + ShoePadRows;
                    cUTarget = (curBox.UMin + curBox.UMax) / 2.0;
                    cVTarget = (curVMin + curVMax) / 2.0 + (0.10 * 0.25 * (curVMax - curVMin));
                }
            }
            else
            {
                zAutoTarget = AutoZoomFallback;
                cUTarget = wSrc / 2.0;
                cVTarget = hSrc / 2.0;
            }

            // S42: шаг сглаживания центра и панорамы растёт с множителем слайдера (0.2 при 1,
            // до 0.5 при 3): на большом зуме окно должно догонять танцора быстрее
            var gain = Math.Min(0.5, 0.2 * Math.Max(1.0, Math.Min(3.0, sliderZoom)));
            if (double.IsNaN(smoothedCenterU))
            {
                smoothedCenterU = cUTarget; // первый валидный тик: без проползания из нуля
                smoothedCenterV = cVTarget;
            }
            else
            {
                smoothedCenterU += gain * (cUTarget - smoothedCenterU);
                smoothedCenterV += gain * (cVTarget - smoothedCenterV);
            }

            smoothedZoomAuto += 0.2 * (zAutoTarget - smoothedZoomAuto);
            var z = Math.Max(1.0, Math.Min(MaxTotalZoom, smoothedZoomAuto * sliderZoom));

            // панорама: окно центрируется на паддингованном bbox со сдвигом вверх 7% высоты окна
            var maxPanX = Math.Max(0.0, ((z * s * wSrc) - winW) / 2.0);
            var maxPanY = Math.Max(0.0, ((z * s * hSrc) - windowH) / 2.0);
            var vTarget = smoothedCenterV - (HeadShiftFrac * windowH / (s * z));
            var targetX = Math.Max(-maxPanX, Math.Min(maxPanX, -z * s * (smoothedCenterU - (wSrc / 2.0))));
            var targetY = Math.Max(-maxPanY, Math.Min(maxPanY, -z * s * (vTarget - (hSrc / 2.0))));

            smoothedPanX += gain * (targetX - smoothedPanX);
            smoothedPanY += gain * (targetY - smoothedPanY);

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

            // числовой отчёт тура (S34): только запись трёх чисел, поведение не меняет
            LastTarget = new FrameTarget(zAutoTarget, maxPanX, maxPanY);

            return (z, smoothedPanX, smoothedPanY, screenBox);
        }

        /// <summary>Расширенный по времени bbox танцора текущей камеры (окно windowSec вокруг t); false, если камеры/данных нет.</summary>
        private bool TryGetAutoFrame(double t, string? camera, double windowSec, out (double UMin, double VMin, double UMax, double VMax) box)
        {
            box = default;
            return cameras.TryGetValue(camera ?? string.Empty, out var track)
                    && track.TryGetBox(t, windowSec, out box);
        }

        /// <summary>
        /// Рамка кадра, ближайшего к t, текущей камеры (S42/T44): точного совпадения времён
        /// почти не бывает — времена в cams.json округлены до 4 знаков, тики идут по таймеру
        /// (<see cref="CamTrack.TryGetNearest"/>); false, если камеры/данных нет.
        /// </summary>
        private bool TryGetNearestFrame(double t, string? camera, out (double UMin, double VMin, double UMax, double VMax) box)
        {
            box = default;
            return cameras.TryGetValue(camera ?? string.Empty, out var track)
                    && track.TryGetNearest(t, out _, out box);
        }
    }
}
