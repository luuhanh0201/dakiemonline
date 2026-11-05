using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Game.Rules.Tests
{
    internal static class TestConfig
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public static MovementConfig Movement() => Load<MovementConfig>("movement.json");

        public static CollisionMap TestMap() => new CollisionMap(Load<MapFile>("map.test.json").Boxes);

        private static T Load<T>(string fileName)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "config", fileName);
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException($"{fileName} is empty.");
        }

        private sealed class MapFile
        {
            public List<MapBox> Boxes { get; set; } = new List<MapBox>();
        }
    }
}
