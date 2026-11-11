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

        /// <summary>The real files with one piece of text in one of them replaced.</summary>
        public static GameConfig LoadWith(string file, string find, string replace)
        {
            Dictionary<string, string> files = Files();

            if (!files[file].Contains(find))
            {
                throw new ArgumentException($"{file} does not contain {find}", nameof(find));
            }

            files[file] = files[file].Replace(find, replace);
            return GameConfig.Parse(files);
        }

        public static MovementConfig Movement() => Load().Movement;

        public static CollisionMap TestMap() => new CollisionMap(Load().MapBoxes);
    }
}
