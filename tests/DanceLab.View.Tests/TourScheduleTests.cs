using System;
using DanceLab.View;
using Xunit;

namespace DanceLab.View.Tests
{
    /// <summary>
    /// Тесты расписания тура (E37/T34, S34): TourSchedule.Times — равномерные моменты
    /// (i + 1) / (count + 1) * durationSec, округление до 0.1 с, зажим не ближе 0.2 с
    /// к краям записи, пустой список на некорректных входах.
    /// </summary>
    public class TourScheduleTests
    {
        [Fact]
        public void Duration12_Count5_EvenPoints()
        {
            Assert.Equal(new[] { 2.0, 4.0, 6.0, 8.0, 10.0 }, TourSchedule.Times(12, 5));
        }

        [Fact]
        public void Duration3_Count5_AllInsideClamp()
        {
            // (i + 1)/6 * 3 = 0.5..2.5 — внутри [0.2, 2.8], зажим не мешает
            Assert.Equal(new[] { 0.5, 1.0, 1.5, 2.0, 2.5 }, TourSchedule.Times(3, 5));
        }

        [Fact]
        public void Duration03_Count3_AllClampedToMinimum()
        {
            // сырые точки 0.075/0.15/0.225 ниже нижнего зажима: max(0.2, 0.3-0.2) = 0.2
            Assert.Equal(new[] { 0.2, 0.2, 0.2 }, TourSchedule.Times(0.3, 3));
        }

        [Fact]
        public void Count0_Empty()
        {
            Assert.Empty(TourSchedule.Times(12, 0));
        }

        [Fact]
        public void NanDuration_Empty()
        {
            Assert.Empty(TourSchedule.Times(double.NaN, 3));
        }

        [Fact]
        public void Duration7_Count1_MiddlePoint()
        {
            Assert.Equal(new[] { 3.5 }, TourSchedule.Times(7, 1));
        }
    }
}
