using System;
using System.Collections.Generic;
using Game.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>Approved front-view artwork selected from the server's three visible equipment slots.</summary>
    public sealed class CharacterPreview
    {
        private const string DefaultArt = "Figure/front_kiem_aovai_vai";
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private Image image;

        public const float FeetFromTop = 0.97f;
        // The sword occupies the left of the crop; the midpoint between shoes is at x = 215.
        public const float FeetFromLeft = 215f / 330f;
        public const float Aspect = 330f / 412f;
        public Sprite Sprite { get; private set; }

        public void Bind(Image target)
        {
            image = target;
            image.sprite = Sprite;
        }

        public void Show(GameConfig config, Appearance appearance)
        {
            string art = $"Figure/front_{Part(config, appearance, GearSlot.Weapon)}_{Part(config, appearance, GearSlot.Armor)}_{Part(config, appearance, GearSlot.Pants)}";
            Sprite = MenuKit.Art(art);
            if (Sprite == null)
            {
                if (Warned.Add(art)) Debug.LogWarning($"Missing menu figure: {art}; using {DefaultArt}.");
                Sprite = MenuKit.Art(DefaultArt);
                if (Sprite == null) throw new InvalidOperationException("Missing default menu figure: " + DefaultArt);
            }
            if (image != null)
            {
                image.sprite = Sprite;
                image.enabled = true;
            }
        }

        public void Hide()
        {
            if (image != null) image.enabled = false;
            image = null;
        }

        private static string Part(GameConfig config, Appearance appearance, GearSlot slot)
        {
            int index = appearance[slot];
            return index >= 0 && index < config.Gear.Count && config.Gear[index].Slot == slot
                ? config.Gear[index].Art : "unknown";
        }
    }
}
