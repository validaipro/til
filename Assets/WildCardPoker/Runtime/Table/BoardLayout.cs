using UnityEngine;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Card positions for the Game 23 design board: seven cards tracing the numeral 2 and seven tracing the 3.
    /// Slot order follows the pen stroke, which is also the order the dealer turns the cards.
    /// </summary>
    public static class BoardLayout
    {
        // Card centers (pixels, relative to each numeral's origin), in stroke order.
        private static readonly Vector2[] Two =
        {
            new Vector2(-70, 180),  // top-left
            new Vector2(50, 180),   // top-right
            new Vector2(118, 72),   // shoulder
            new Vector2(10, -30),   // diagonal
            new Vector2(-90, -185), // bottom-left
            new Vector2(10, -185),  // bottom
            new Vector2(110, -185), // bottom-right
        };

        private static readonly Vector2[] Three =
        {
            new Vector2(-70, 180),  // top-left
            new Vector2(50, 180),   // top-right
            new Vector2(118, 82),   // upper bowl
            new Vector2(15, 0),     // middle
            new Vector2(118, -82),  // lower bowl
            new Vector2(50, -180),  // bottom-right
            new Vector2(-70, -180), // bottom-left
        };

        public static readonly Vector2 Center = new Vector2(0, 22);
        public const float SideOffset = 215;
        public static readonly Vector2 CardSize = new Vector2(80, 112);

        public static Vector2 Slot(int side, int slot)
        {
            var origin = Center + new Vector2(side == 2 ? -SideOffset : SideOffset, 0);
            return origin + (side == 2 ? Two : Three)[slot];
        }

        /// <summary>Where the big "2" / "3" watermark sits behind each structure.</summary>
        public static Vector2 LabelPosition(int side) =>
            Center + new Vector2(side == 2 ? -SideOffset + 15 : SideOffset + 15, 0);
    }
}
