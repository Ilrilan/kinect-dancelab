namespace DanceLab.View
{
    /// <summary>
    /// Рамка танцора в пикселях окна видео-панели (T24, S26): расширенный bbox источника,
    /// спроецированный на экран тем же отображением, которое окно применяет к видео
    /// (масштаб z от центра элемента, затем сдвиг на панораму; контент центрирован).
    /// default(ScreenBox) невалиден (IsValid == false) — камеры/данных нет.
    /// </summary>
    public readonly struct ScreenBox
    {
        /// <summary>Левая кромка рамки, пиксели окна панели.</summary>
        public readonly double Left;

        /// <summary>Верхняя кромка рамки, пиксели окна панели.</summary>
        public readonly double Top;

        /// <summary>Правая кромка рамки, пиксели окна панели.</summary>
        public readonly double Right;

        /// <summary>Нижняя кромка рамки (низ bbox с ShoePadRows), пиксели окна панели.</summary>
        public readonly double Bottom;

        /// <summary>Рамка есть; false у default(ScreenBox).</summary>
        public readonly bool IsValid;

        public ScreenBox(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            IsValid = true;
        }

        /// <summary>Ширина рамки, пиксели.</summary>
        public double Width => Right - Left;

        /// <summary>Высота рамки, пиксели.</summary>
        public double Height => Bottom - Top;

        /// <summary>Горизонтальный центр рамки, пиксели.</summary>
        public double CenterX => (Left + Right) / 2;
    }
}
