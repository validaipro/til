using UnityEngine;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Positions for the Diamond Turn-over board: two rows of five, centered on the felt.
    /// Row 1 sits above row 2; columns are numbered 1-5 left to right.
    /// </summary>
    public static class BoardLayout
    {
        public static readonly Vector2 CardSize = new Vector2(80, 112);
        public const float ColumnSpacing = 96;
        public const float Row1Y = 105;
        public const float Row2Y = -25;

        /// <summary>Card center for <paramref name="row"/> (1 or 2) and <paramref name="column"/> (0..4).</summary>
        public static Vector2 Slot(int row, int column) =>
            new Vector2((column - 2) * ColumnSpacing, row == 1 ? Row1Y : Row2Y);

        public static Vector2 RowLabel(int row) => new Vector2(-2.5f * ColumnSpacing - 70, row == 1 ? Row1Y : Row2Y);

        public static Vector2 ColumnLabel(int column) => new Vector2((column - 2) * ColumnSpacing, Row1Y + CardSize.y / 2 + 18);
    }
}
