using System;
using System.Windows;
using PlayerTrial;
using Xunit;

namespace DanceLab.View.Tests
{
    /// <summary>
    /// Юнит-тесты порога конца ролика (T23, аудит P2.13): MediaQuirks.IsAtEnd — чистая логика
    /// ветки TogglePlay после MediaEnded (Play с конца сразу кончается, нужен рестарт с нуля),
    /// вынесенная из tools/player_trial/MainWindow.cs. Порог 200 мс против NaturalDuration;
    /// Duration без TimeSpan (видео ещё не открыто) — конец не определяется.
    /// </summary>
    public class MediaQuirksTests
    {
        private static readonly Duration Track = new Duration(TimeSpan.FromSeconds(7.4));

        [Fact]
        public void PositionEqualsDuration_IsAtEnd()
        {
            Assert.True(MediaQuirks.IsAtEnd(TimeSpan.FromSeconds(7.4), Track));
        }

        [Fact]
        public void PositionWithinThresholdBeforeEnd_IsAtEnd()
        {
            Assert.True(MediaQuirks.IsAtEnd(Track.TimeSpan - TimeSpan.FromMilliseconds(150), Track));
        }

        [Fact]
        public void PositionInMiddle_IsNotAtEnd()
        {
            Assert.False(MediaQuirks.IsAtEnd(TimeSpan.FromSeconds(3.7), Track));
        }
    }
}
