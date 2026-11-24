namespace DanceLab.Core.Canonical
{
    /// <summary>
    /// Мировые оси торса канонической позы: бок в сторону левого бедра, вверх от корня к середине плеч,
    /// вперёд; восстановлены из формата эталона версии 2 (S39).
    /// </summary>
    public readonly struct TorsoFrame
    {
        public TorsoFrame(Vec3 left, Vec3 up, Vec3 forward)
        {
            Left = left;
            Up = up;
            Forward = forward;
        }

        /// <summary>Бок в сторону левого бедра.</summary>
        public readonly Vec3 Left;

        /// <summary>Вверх от корня к середине плеч.</summary>
        public readonly Vec3 Up;

        /// <summary>Вперёд (left × up).</summary>
        public readonly Vec3 Forward;
    }
}
