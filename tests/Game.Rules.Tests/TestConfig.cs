using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Rules.Tests
{
    internal static class TestConfig
    {
        public static Dictionary<string, string> Files()
        {
            return GameConfig.FileNames.ToDictionary(
                name => name,
                name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", name)));
        }

        public static GameConfig Load() => GameConfig.Parse(Files());

        public static MovementConfig Movement() => Load().Movement;

        public static CollisionMap TestMap() => new CollisionMap(Load().MapBoxes);
    }
}
