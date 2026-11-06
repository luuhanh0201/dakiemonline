using System;
using System.Collections.Generic;
using UnityEngine;

namespace DaKiem.Client
{
    public enum GearSlot
    {
        Weapon,
        Armor,
        Pants,
    }

    /// <summary>
    /// Finds every equipment drawing by its file name, slot_item_part, so adding an item only
    /// means adding its images.
    /// </summary>
    public sealed class EquipmentCatalog
    {
        private const string Folder = "Character/BoldGear";

        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<GearSlot, List<string>> items = new Dictionary<GearSlot, List<string>>();

        public EquipmentCatalog()
        {
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                items[slot] = new List<string>();
            }

            foreach (Sprite sprite in Resources.LoadAll<Sprite>(Folder))
            {
                sprites[sprite.name] = sprite;
                string[] nameParts = sprite.name.Split('_');

                if (nameParts.Length == 3 && Enum.TryParse(nameParts[0], true, out GearSlot slot) && !items[slot].Contains(nameParts[1]))
                {
                    items[slot].Add(nameParts[1]);
                }
            }

            foreach (List<string> list in items.Values)
            {
                list.Sort(StringComparer.Ordinal);
            }
        }

        public IReadOnlyList<string> ItemsIn(GearSlot slot) => items[slot];

        /// <summary>Returns null when this item has no drawing for that part.</summary>
        public Sprite Find(GearSlot slot, string item, string part)
        {
            sprites.TryGetValue($"{slot.ToString().ToLowerInvariant()}_{item}_{part}", out Sprite sprite);
            return sprite;
        }
    }

    /// <summary>
    /// Shows the worn items. Side drawings hang on the skeleton or replace the drawing of a limb;
    /// front drawings are layers over the front figure. Positions match tools/bake-gear.ps1.
    /// </summary>
    public sealed class EquipmentView
    {
        public const int FrontCanvasWidth = 240;
        public const int FrontCanvasHeight = 340;

        private const float BakedPixelsPerLayoutPixel = CharacterRigScale.BakeScale;

        // Middle of the armour canvas, in layout pixels with Y pointing down.
        private const float BodyCentreX = 310f;
        private const float BodyCentreY = 492f;

        // The handle sits in the fist, and the canvas middle lies this far along the blade.
        private const float GripX = 295f;
        private const float GripY = 543f;
        private const float GripToCanvasMiddle = 59f / BakedPixelsPerLayoutPixel;

        // The swing's trail starts this share of the way along the blade from the grip.
        private const float BladeInnerShare = 0.55f;
        private const byte SolidAlpha = 160;

        // The second fist sits this far down the handle from the first, in world units.
        private const float SecondHandDrop = 0.1f;

        private const int BodyOrder = 24;
        private const int WeaponOrder = 26;
        private const int FrontPantsOrder = 21;
        private const int FrontArmorOrder = 22;
        private const int FrontWeaponOrder = 24;

        private readonly EquipmentCatalog catalog = new EquipmentCatalog();
        private readonly Dictionary<GearSlot, int> worn = new Dictionary<GearSlot, int>();
        private readonly List<SpriteRenderer> sideParts = new List<SpriteRenderer>();
        private readonly SpriteRenderer body;
        private readonly SpriteRenderer weapon;
        private readonly SpriteRenderer bareTorso;
        private readonly Transform weaponPivot;
        private readonly LimbStrip armNear;
        private readonly LimbStrip armFar;
        private readonly LimbStrip legNear;
        private readonly LimbStrip legFar;
        private readonly SkinnedShorts hip;
        private readonly FrontFigure front;
        private readonly FrontLayer frontPants;
        private readonly FrontLayer frontArmor;
        private readonly FrontLayer frontWeapon;
        private float bladeReach;

        public EquipmentView(
            Transform torso,
            Transform hand,
            SpriteRenderer bareTorso,
            LimbStrip armNear,
            LimbStrip armFar,
            LimbStrip legNear,
            LimbStrip legFar,
            SkinnedShorts hip,
            FrontFigure front)
        {
            this.bareTorso = bareTorso;
            this.armNear = armNear;
            this.armFar = armFar;
            this.legNear = legNear;
            this.legFar = legFar;
            this.hip = hip;
            this.front = front;

            CharacterBone torsoBone = CharacterRig.Find(CharacterRig.Torso);
            CharacterBone handBone = CharacterRig.Find(CharacterRig.HandNear);

            body = AddSidePart("gear_body", torso, CharacterRig.Local(BodyCentreX - torsoBone.JointX, BodyCentreY - torsoBone.JointY), BodyOrder);

            weaponPivot = new GameObject("gear_grip").transform;
            weaponPivot.SetParent(hand, false);
            weaponPivot.localPosition = CharacterRig.Local(GripX - handBone.JointX, GripY - handBone.JointY);
            weapon = AddSidePart("gear_weapon", weaponPivot, CharacterRig.Local(0f, -GripToCanvasMiddle), WeaponOrder);

            frontPants = front.AddLayer("pants", null, FrontPantsOrder);
            frontArmor = front.AddLayer("armor", null, FrontArmorOrder);
            frontWeapon = front.AddLayer("weapon", null, FrontWeaponOrder);

            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                int initial = 0;
                if (slot == GearSlot.Weapon)
                {
                    IReadOnlyList<string> weapons = catalog.ItemsIn(slot);
                    for (int index = 0; index < weapons.Count; index++)
                        if (weapons[index] == "kiem") { initial = index; break; }
                }
                Wear(slot, initial);
            }
        }

        /// <summary>Puts on the next item in a slot, and back to the first after the last.</summary>
        public void WearNext(GearSlot slot)
        {
            Wear(slot, worn[slot] + 1);
        }

        public void Wear(GearSlot slot, int index)
        {
            IReadOnlyList<string> items = catalog.ItemsIn(slot);
            int chosen = index % items.Count;
            string item = items[chosen];
            worn[slot] = chosen;

            switch (slot)
            {
                case GearSlot.Weapon:
                    weapon.sprite = catalog.Find(slot, item, "side");
                    bladeReach = FindBladeReach(weapon.sprite);
                    frontWeapon.Show(TextureOf(slot, item, "front"));
                    break;
                case GearSlot.Armor:
                    // A hard sleeve is armour: it folds at the elbow instead of curving.
                    Sprite hardSleeve = catalog.Find(slot, item, "sleevehard");
                    Sprite sleeve = hardSleeve != null ? hardSleeve : catalog.Find(slot, item, "sleeve");
                    body.sprite = catalog.Find(slot, item, "body");
                    bareTorso.enabled = false;
                    armNear.SetImage(sleeve);
                    armFar.SetImage(sleeve);
                    armNear.SetStiff(hardSleeve != null);
                    armFar.SetStiff(hardSleeve != null);
                    frontArmor.Show(TextureOf(slot, item, "front"));
                    break;
                case GearSlot.Pants:
                    Sprite leg = catalog.Find(slot, item, "leg");
                    hip.SetImage(catalog.Find(slot, item, "hip").texture);
                    legNear.SetImage(leg);
                    legFar.SetImage(leg);
                    frontPants.Show(TextureOf(slot, item, "front"));
                    break;
            }
        }

        public Vector3 GripPoint => weaponPivot.position;
        public Vector3 SecondHandPoint => weaponPivot.TransformPoint(Vector3.down * SecondHandDrop);
        public bool HasWeapon => weapon.sprite != null;
        public Vector3 BladeInnerPoint => weaponPivot.TransformPoint(Vector3.up * bladeReach * BladeInnerShare);
        public Vector3 BladeOuterPoint => weaponPivot.TransformPoint(Vector3.up * bladeReach);

        /// <summary>Turns the weapon around the fist; negative tips it forward of the forearm.</summary>
        public bool TurnWeapon(float degrees)
        {
            weaponPivot.localRotation = Quaternion.Euler(0f, 0f, degrees);
            // A head this big fills the arc of every swing, so the blade passes behind it.
            return true;
        }

        public void RollWeapon(float degrees)
        {
            // Turn about the blade's long axis through the grip. The narrow middle
            // silhouette reads as a wrist roll instead of an instantaneous image flip.
            float width = Mathf.Cos(degrees * Mathf.Deg2Rad);
            if (Mathf.Abs(width) < 0.08f) width = width < 0f ? -0.08f : 0.08f;
            // The swords are drawn bowing left, and the bowed side is the cutting edge. Mirrored,
            // the edge leads a right-facing swing.
            weapon.transform.localScale = new Vector3(-width, 1f, 1f);
        }

        public void TwistBody(float amount)
        {
            body.transform.localScale = new Vector3(1f + 0.12f * Mathf.Abs(amount), 1f, 1f);
        }

        public void Tint(Color color, float sideAlpha)
        {
            foreach (SpriteRenderer part in sideParts)
            {
                part.color = new Color(color.r, color.g, color.b, sideAlpha);
            }
        }

        /// <summary>How far the tip of a weapon's drawing lies from the fist, in world units.</summary>
        private float FindBladeReach(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            Color32[] pixels = texture.GetPixels32();

            for (int y = texture.height - 1; y >= 0; y--)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a > SolidAlpha)
                    {
                        return weapon.transform.localPosition.y + (y - sprite.pivot.y) / sprite.pixelsPerUnit;
                    }
                }
            }

            return 0f;
        }

        private Texture TextureOf(GearSlot slot, string item, string part)
        {
            Sprite sprite = catalog.Find(slot, item, part);
            return sprite == null ? null : sprite.texture;
        }

        private SpriteRenderer AddSidePart(string name, Transform parent, Vector3 position, int order)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;

            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = bareTorso.sharedMaterial;
            renderer.sortingOrder = order;
            sideParts.Add(renderer);
            return renderer;
        }
    }
}
