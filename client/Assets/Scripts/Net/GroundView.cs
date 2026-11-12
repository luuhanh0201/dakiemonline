using System.Collections.Generic;
using DaKiem.Client.Accounts;
using Game.Net;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Net
{
    /// <summary>
    /// Items lying on the ground, drawn with their bag icons bobbing a little above the feet line,
    /// and which one the player stands close enough to pick up.
    /// </summary>
    public sealed class GroundView
    {
        // Above the ground and the boss's body, under every character.
        private const int Order = 2;
        private const float SizeUnits = 0.55f;
        private const float LiftUnits = 0.3f;
        private const float BobUnits = 0.06f;

        private readonly GameConfig config;
        private readonly Dictionary<int, SpriteRenderer> shown = new Dictionary<int, SpriteRenderer>();
        private readonly List<int> gone = new List<int>();
        private readonly HashSet<int> present = new HashSet<int>();

        public GroundView(GameConfig config)
        {
            this.config = config;
        }

        public void Show(IReadOnlyList<GroundItem> ground)
        {
            present.Clear();

            foreach (GroundItem item in ground)
            {
                if (item.Item < 0 || item.Item >= config.Gear.Count)
                {
                    continue;
                }

                present.Add(item.Id);

                if (!shown.TryGetValue(item.Id, out SpriteRenderer renderer))
                {
                    renderer = new GameObject($"Ground {item.Id}").AddComponent<SpriteRenderer>();
                    GearItemConfig gear = config.Gear[item.Item];
                    renderer.sprite = UiKit.Art($"Items/{gear.Slot.ToString().ToLowerInvariant()}_{gear.Art}");
                    renderer.sortingOrder = Order;
                    float size = renderer.sprite == null ? 1f : Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y);
                    renderer.transform.localScale = Vector3.one * (SizeUnits / size);
                    shown.Add(item.Id, renderer);
                }

                // Each item bobs on its own beat, so a pile does not move as one block.
                float bob = Mathf.Sin(Time.time * 2.6f + item.Id) * BobUnits;
                renderer.transform.position = new Vector3(item.X, item.Y + LiftUnits + bob, 0f);
            }

            gone.Clear();

            foreach (int id in shown.Keys)
            {
                if (!present.Contains(id))
                {
                    gone.Add(id);
                }
            }

            foreach (int id in gone)
            {
                Object.Destroy(shown[id].gameObject);
                shown.Remove(id);
            }
        }

        /// <summary>The nearest item the feet reach, by the same rule the server checks; null when none.</summary>
        public GroundItem? Reachable(IReadOnlyList<GroundItem> ground, double feetX, double feetY)
        {
            GroundItem? best = null;
            double bestDistance = double.MaxValue;

            foreach (GroundItem item in ground)
            {
                double distance = System.Math.Abs(item.X - feetX);

                if (item.Item >= 0 && item.Item < config.Gear.Count
                    && InventoryRules.InReach(config.Bag, feetX, feetY, item.X, item.Y)
                    && distance < bestDistance)
                {
                    best = item;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public void Clear()
        {
            foreach (SpriteRenderer renderer in shown.Values)
            {
                Object.Destroy(renderer.gameObject);
            }

            shown.Clear();
        }
    }
}
