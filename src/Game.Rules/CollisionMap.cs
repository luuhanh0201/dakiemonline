using System;
using System.Collections.Generic;

namespace Game.Rules
{
    public sealed class MapBox
    {
        public string Id { get; set; } = string.Empty;
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        /// <summary>One-way boxes only stop a body landing from above.</summary>
        public bool OneWay { get; set; }
    }

    public sealed class CollisionMap
    {
        private const double Tolerance = 1e-9;

        private readonly IReadOnlyList<MapBox> boxes;

        public CollisionMap(IReadOnlyList<MapBox> boxes)
        {
            this.boxes = boxes;
        }

        /// <summary>Returns the X the body reaches, stopping at the first solid side in its path.</summary>
        public double SweepX(double x, double y, double deltaX, double width, double height)
        {
            double halfWidth = width / 2d;
            double target = x + deltaX;

            foreach (MapBox box in boxes)
            {
                if (box.OneWay || y >= box.MaxY - Tolerance || y + height <= box.MinY + Tolerance)
                {
                    continue;
                }

                if (deltaX > 0d && box.MinX >= x + halfWidth - Tolerance)
                {
                    target = Math.Min(target, box.MinX - halfWidth);
                }
                else if (deltaX < 0d && box.MaxX <= x - halfWidth + Tolerance)
                {
                    target = Math.Max(target, box.MaxX + halfWidth);
                }
            }

            return target;
        }

        /// <summary>Returns the height of the highest surface crossed while falling, or null.</summary>
        public double? FindLanding(double x, double fromY, double toY, double width)
        {
            double? landing = null;

            foreach (MapBox box in boxes)
            {
                if (OverlapsX(box, x, width)
                    && box.MaxY <= fromY + Tolerance
                    && box.MaxY >= toY - Tolerance
                    && (landing == null || box.MaxY > landing))
                {
                    landing = box.MaxY;
                }
            }

            return landing;
        }

        /// <summary>Returns the underside of the lowest solid box hit while rising, or null.</summary>
        public double? FindCeiling(double x, double fromTop, double toTop, double width)
        {
            double? ceiling = null;

            foreach (MapBox box in boxes)
            {
                if (!box.OneWay
                    && OverlapsX(box, x, width)
                    && box.MinY >= fromTop - Tolerance
                    && box.MinY < toTop
                    && (ceiling == null || box.MinY < ceiling))
                {
                    ceiling = box.MinY;
                }
            }

            return ceiling;
        }

        private static bool OverlapsX(MapBox box, double x, double width)
        {
            double halfWidth = width / 2d;
            return x - halfWidth < box.MaxX - Tolerance && x + halfWidth > box.MinX + Tolerance;
        }
    }
}
