using System;
using System.Windows;

namespace PlayerTrial
{
    /// <summary>
    /// Чистая логика медиа-quirks MediaElement (T23, аудит P2.13), вынесенная из MainWindow для
    /// юнит-тестов. Порог «конец ролика» 200 мс: после MediaEnded Play() продолжает с конца и
    /// сразу кончается, поэтому ветка TogglePlay при Space на конце считает концом и позиции
    /// в последних 200 мс и делает рестарт с нуля. Duration может быть без TimeSpan
    /// (Automatic/Forever, в том числе пока видео не открыто) — конец в этом случае не
    /// определяется. Регрессионный сценарий живого окна: --regress media (MainWindow).
    /// </summary>
    public static class MediaQuirks
    {
        /// <summary>Порог близости к концу трека: позиции в последних 200 мс считаются «концом».</summary>
        public const int EndThresholdMs = 200;

        /// <summary>Ветка TogglePlay (Space на конце): позиция у конца в пределах 200 мс — рестарт с нуля.</summary>
        public static bool IsAtEnd(TimeSpan position, Duration duration) =>
            duration.HasTimeSpan && position >= duration.TimeSpan - TimeSpan.FromMilliseconds(EndThresholdMs);
    }
}
