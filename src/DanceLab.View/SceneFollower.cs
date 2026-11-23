namespace DanceLab.View
{
    /// <summary>
    /// Следящий кадр 3D-сцены с призраком (S26, T24): единый кадр обеих половин — сцена
    /// масштабируется и позиционируется по экранной рамке танцора (FrameFollower.ScreenBox).
    /// Есть рамка (танцор виден): пикселей на метр = высота рамки / рост призрака, линия
    /// пола = низ рамки, экранная ось корня = центр рамки по X. Нет данных о габарите
    /// танцора (нет cams.json) — прежний широкий вид: высота панели покрывает
    /// FallbackFieldHeightM метров, пол на 86% высоты панели, корень по центру панели.
    /// Цель камеры (в мире, м) сглаживается, как зум/центр у FrameFollower: первый вызов
    /// после конструктора или Reset — снап к корню призрака, дальше шаг 0.2 оставшейся
    /// дистанции (FollowerGain). Чистая математика без WPF-типов: панель и рамка задаются
    /// числами.
    /// </summary>
    public sealed class SceneFollower
    {
        /// <summary>Прежний базовый вид сцены: высота панели покрывает 3.2 м.</summary>
        public const double FallbackFieldHeightM = 3.2;

        /// <summary>Сглаживание цели камеры, как у FrameFollower: шаг 0.2 оставшейся дистанции.</summary>
        public const double FollowerGain = 0.2;

        private double targetXm;
        private double targetZm;
        private bool hasTarget;

        /// <summary>Сброс цели камеры: следующий вызов Update снапится к корню призрака.</summary>
        public void Reset()
        {
            hasTarget = false;
        }

        /// <summary>
        /// Один тик кадра сцены: hasBox/boxHeightPx/boxBottomPx/boxCenterXpx — экранная рамка
        /// танцора (ScreenBox из FrameFollower), ghostHeightM — рост призрака (м),
        /// panelWpx/panelHpx — панель сцены (пиксели), sliderZoom — множитель слайдера «Кадр»,
        /// rootXm/rootZm — корень призрака в мире (м). Возвращает масштаб, линию пола,
        /// экранную ось корня и сглаженную цель камеры.
        /// </summary>
        public Frame Update(bool hasBox, double boxHeightPx, double boxBottomPx, double boxCenterXpx,
                double ghostHeightM, double panelWpx, double panelHpx, double sliderZoom,
                double rootXm, double rootZm)
        {
            double scalePxPerM;
            double floorScreenY;
            double centerScreenX;
            if (hasBox && ghostHeightM > 0.1 && boxHeightPx > 0)
            {
                // рамка танцора: призрак растягивается на высоту рамки, пол — на линии ног
                scalePxPerM = boxHeightPx / ghostHeightM;
                floorScreenY = boxBottomPx;
                centerScreenX = boxCenterXpx;
            }
            else
            {
                // fallback: прежний широкий вид — высота панели покрывает FallbackFieldHeightM м
                scalePxPerM = panelHpx / FallbackFieldHeightM * FrameFollower.AutoZoomFallback * sliderZoom;
                floorScreenY = 0.86 * panelHpx;
                centerScreenX = panelWpx / 2.0;
            }

            if (!hasTarget)
            {
                targetXm = rootXm; // первый тик: без проползания из нуля, как центр у FrameFollower
                targetZm = rootZm;
                hasTarget = true;
            }
            else
            {
                targetXm += FollowerGain * (rootXm - targetXm);
                targetZm += FollowerGain * (rootZm - targetZm);
            }

            return new Frame(scalePxPerM, floorScreenY, centerScreenX, targetXm, targetZm);
        }

        /// <summary>Кадр сцены: числа для отрисовки и цель камеры в мире.</summary>
        public readonly struct Frame
        {
            /// <summary>Пикселей на метр сцены.</summary>
            public readonly double ScalePxPerM;

            /// <summary>Экранная линия пола под корнем эталона.</summary>
            public readonly double FloorScreenY;

            /// <summary>Экранная точка, в которую проецируется корень эталона.</summary>
            public readonly double CenterScreenX;

            /// <summary>Цель камеры в мире по X, м.</summary>
            public readonly double TargetXm;

            /// <summary>Цель камеры в мире по Z, м.</summary>
            public readonly double TargetZm;

            public Frame(double scalePxPerM, double floorScreenY, double centerScreenX,
                    double targetXm, double targetZm)
            {
                ScalePxPerM = scalePxPerM;
                FloorScreenY = floorScreenY;
                CenterScreenX = centerScreenX;
                TargetXm = targetXm;
                TargetZm = targetZm;
            }
        }
    }
}
