using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>A way out of a map: standing within reach of X and pressing the door button goes to the other map.</summary>
    public sealed class MapDoor
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>What the door button says, such as "Vào nhà".</summary>
        public string Name { get; set; } = string.Empty;

        public double X { get; set; }

        /// <summary>How far from X, either side, the door can be used.</summary>
        public double Reach { get; set; }

        /// <summary>The id of the map it leads to, such as map.house.</summary>
        public string ToMap { get; set; } = string.Empty;

        /// <summary>Where the player stands on arriving.</summary>
        public double ToX { get; set; }
    }

    /// <summary>One map file: its solid boxes, where players appear and the doors out of it.</summary>
    public sealed class MapConfig : ConfigEntry
    {
        public IReadOnlyList<MapBox> Boxes { get; set; } = Array.Empty<MapBox>();

        /// <summary>Where a player appears on this map; null means the server's spawnX.</summary>
        public double? SpawnX { get; set; }

        /// <summary>The boss lives on this map. Players elsewhere neither see it nor are attacked.</summary>
        public bool Boss { get; set; }

        /// <summary>
        /// Players on the map see each other. A map that is not shared, such as the house, is a copy of
        /// its own for each player: they see only themselves and what they dropped there.
        /// </summary>
        public bool Shared { get; set; }

        public IReadOnlyList<MapDoor> Doors { get; set; } = Array.Empty<MapDoor>();
    }

    /// <summary>Where a character is: which map, as its place in <see cref="GameConfig.Maps"/>, and how far along.</summary>
    public readonly struct MapSpot
    {
        public MapSpot(int map, double x)
        {
            Map = map;
            X = x;
        }

        public int Map { get; }
        public double X { get; }
    }

    public static class MapRules
    {
        /// <summary>The door whose reach the feet are in, nearest first; null when there is none.</summary>
        public static MapDoor? DoorAt(MapConfig map, double x)
        {
            MapDoor? nearest = null;

            foreach (MapDoor door in map.Doors)
            {
                double distance = Math.Abs(x - door.X);

                if (distance <= door.Reach && (nearest == null || distance < Math.Abs(x - nearest.X)))
                {
                    nearest = door;
                }
            }

            return nearest;
        }

        /// <summary>Whether two players on <paramref name="map"/> see each other and each other's things.</summary>
        public static bool SeeEachOther(GameConfig config, int map, int viewerMap, int ownerId, int viewerId)
        {
            return map == viewerMap && (config.Maps[map].Shared || ownerId == viewerId);
        }
    }
}
