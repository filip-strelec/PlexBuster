using System.Collections.Generic;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Picks the smallest room that holds a number of tapes and places its shelves.
    /// Room space: the door is centred on the front wall at the origin and the room extends along +Z.
    /// Shelves are listed in reading order: the back wall left to right (what you see on entering),
    /// clockwise round the walls, then the island rows snaking back from the entrance.
    /// </summary>
    public class RoomLayout
    {
        public struct Shelf
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public int Rows;
            public int Capacity => Rows * Columns;
        }

        public const int Columns = 8;
        public const int WallRows = 6;
        public const int IslandRows = 5;   // islands are lower so you can see across the room
        public const float DoorWidth = StoreShell.DoorWidth;
        public const float DoorHeight = StoreShell.DoorHeight;

        static readonly float UnitWidth = ShelfUnit.WidthFor(Columns) + 0.02f;
        const float ShelfDepth = ShelfUnit.Depth;
        const float WallGap = 0.01f;
        const float CornerMargin = 0.3f;
        const float DoorMargin = 0.2f;
        const float Aisle = 1.6f;
        const float FrontClearance = 2.2f;
        const float IslandGap = 0.02f;
        const float IslandDepth = 2 * ShelfDepth + IslandGap;
        const float MinSize = 5f;
        const float MaxSize = 40f;
        const float SizeStep = 0.5f;
        const float MaxAspect = 2f;

        public float Width { get; private set; }
        public float Depth { get; private set; }
        public List<Shelf> Shelves { get; } = new();

        /// <summary>Where the player stands after walking in: just inside the door, facing the back wall.</summary>
        public static Pose Entry => new(new Vector3(0, 0, 0.9f), Quaternion.identity);

        public static RoomLayout For(int itemCount)
        {
            RoomLayout best = null;
            for (var w = MinSize; w <= MaxSize; w += SizeStep)
            for (var d = MinSize; d <= MaxSize; d += SizeStep)
            {
                if (w / d > MaxAspect || d / w > MaxAspect) continue;
                if (best != null && w * d >= best.Width * best.Depth) continue;
                if (CapacityOf(w, d) >= itemCount) best = new RoomLayout { Width = w, Depth = d };
            }

            // More tapes than the biggest room holds: build the biggest and fill what fits.
            best ??= new RoomLayout { Width = MaxSize, Depth = MaxSize };
            best.PlaceShelves(itemCount);
            return best;
        }

        static int CapacityOf(float w, float d)
        {
            var wallUnits = BackCount(w) + 2 * SideCount(d) + 2 * FrontCount(w);
            var (rows, perFace) = Islands(w, d);
            return wallUnits * Columns * WallRows + rows * 2 * perFace * Columns * IslandRows;
        }

        static int Fit(float length) => Mathf.Max(0, Mathf.FloorToInt(length / UnitWidth));
        static int BackCount(float w) => Fit(w - 2 * CornerMargin);
        static int SideCount(float d) => Fit(d - 2 * CornerMargin);
        static int FrontCount(float w) => Fit((w - DoorWidth) / 2 - CornerMargin - DoorMargin);

        static (int rows, int perFace) Islands(float w, float d)
        {
            var perFace = Fit(w - 2 * (ShelfDepth + Aisle));
            var zone = IslandZoneEnd(d) - FrontClearance;
            var rows = perFace > 0 && zone >= IslandDepth ? Mathf.FloorToInt((zone + Aisle) / (IslandDepth + Aisle)) : 0;
            return (rows, perFace);
        }

        static float IslandZoneEnd(float d) => d - ShelfDepth - Aisle;

        void PlaceShelves(int itemCount)
        {
            var all = new List<Shelf>();
            var halfWidth = Width / 2;
            var doorSide = DoorWidth / 2 + DoorMargin;
            var frontCentre = (doorSide + halfWidth - CornerMargin) / 2;
            var wallInset = ShelfDepth + WallGap;
            var backCount = BackCount(Width);

            // Walls, clockwise from the back-left corner. A unit's local +X is the viewer's left.
            AddRun(all, backCount, new Vector3(0, 0, Depth - wallInset), Vector3.right, 180, WallRows);
            AddRun(all, SideCount(Depth), new Vector3(halfWidth - wallInset, 0, Depth / 2), Vector3.back, -90, WallRows);
            AddRun(all, FrontCount(Width), new Vector3(frontCentre, 0, wallInset), Vector3.left, 0, WallRows);
            AddRun(all, FrontCount(Width), new Vector3(-frontCentre, 0, wallInset), Vector3.left, 0, WallRows);
            AddRun(all, SideCount(Depth), new Vector3(-halfWidth + wallInset, 0, Depth / 2), Vector3.forward, 90, WallRows);

            // Island rows: double-sided, snaking from the entrance towards the back.
            var (rows, perFace) = Islands(Width, Depth);
            var zoneLength = IslandZoneEnd(Depth) - FrontClearance;
            var used = rows * IslandDepth + Mathf.Max(0, rows - 1) * Aisle;
            for (var r = 0; r < rows; r++)
            {
                var z = FrontClearance + (zoneLength - used) / 2 + IslandDepth / 2 + r * (IslandDepth + Aisle);
                AddRun(all, perFace, new Vector3(0, 0, z - IslandGap / 2 - ShelfDepth), Vector3.right, 180, IslandRows);
                AddRun(all, perFace, new Vector3(0, 0, z + IslandGap / 2 + ShelfDepth), Vector3.left, 0, IslandRows);
            }

            // Keep only the shelves the items need.
            var capacity = 0;
            foreach (var shelf in all)
            {
                if (capacity >= itemCount) break;
                Shelves.Add(shelf);
                capacity += shelf.Capacity;
            }

            // A small collection fits on the back wall: centre it there instead of leaving it in a corner.
            if (Shelves.Count > 0 && Shelves.Count < backCount)
            {
                var count = Shelves.Count;
                Shelves.Clear();
                AddRun(Shelves, count, new Vector3(0, 0, Depth - wallInset), Vector3.right, 180, WallRows);
            }
        }

        /// <summary>Adds <paramref name="count"/> units centred on <paramref name="centre"/>, in reading order along <paramref name="direction"/>.</summary>
        static void AddRun(List<Shelf> list, int count, Vector3 centre, Vector3 direction, float yaw, int rows)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            var first = centre - direction * ((count - 1) * 0.5f * UnitWidth);
            for (var i = 0; i < count; i++)
                list.Add(new Shelf { Position = first + direction * (i * UnitWidth), Rotation = rotation, Rows = rows });
        }
    }
}
