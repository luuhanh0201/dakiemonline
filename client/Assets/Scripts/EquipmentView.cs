using System;
using System.Collections.Generic;
using UnityEngine;

namespace DaKiem.Client
{
    public enum GearSlot
    {
        Weapon,
        Hat,
        Armor,
        Pants,
        Cape,
    }

    /// <summary>
    /// Finds every equipment drawing by its file name, slot_item_part, so adding an item only
    /// means adding its images.
    /// </summary>
    public sealed class EquipmentCatalog
    {
        private const string Folder = "Character/Gear";

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

        // Middle of each baked canvas, in layout pixels with Y pointing down.
        private const float HatCentreX = 310f;
        private const float HatCentreY = -60f / BakedPixelsPerLayoutPixel + 70f / BakedPixelsPerLayoutPixel;
        private const float BodyCentreX = 310f;
        private const float BodyCentreY = 320f;

        // The handle sits in the fist, and the canvas middle lies this far along the blade.
        private const float GripX = 295f;
        private const float GripY = 543f;
        private const float GripToCanvasMiddle = 68f / BakedPixelsPerLayoutPixel;

        // Along the blade from the grip, in world units, for the swing's trail.
        private const float BladeInner = 0.2f;
        private const float BladeOuter = 0.62f;

        private const float CapeRootX = 262f;
        private const float CapeRootY = 268f;

        private const int CapeOrder = 10;
        private const int HatBackOrder = 14;
        private const int BodyOrder = 24;
        private const int HatOrder = 28;
        private const int WeaponOrder = 25;
        private const int FrontCapeOrder = 18;
        private const int FrontHatBackOrder = 19;
        private const int FrontPantsOrder = 21;
        private const int FrontArmorOrder = 22;
        private const int FrontHatOrder = 23;
        private const int FrontWeaponOrder = 24;

        private readonly EquipmentCatalog catalog = new EquipmentCatalog();
        private readonly Dictionary<GearSlot, int> worn = new Dictionary<GearSlot, int>();
        private readonly List<SpriteRenderer> sideParts = new List<SpriteRenderer>();
        private readonly SpriteRenderer hat;
        private readonly SpriteRenderer hatBack;
        private readonly SpriteRenderer body;
        private readonly SpriteRenderer weapon;
        private readonly SpriteRenderer bareTorso;
        private readonly Transform weaponPivot;
        private readonly LimbStrip armNear;
        private readonly LimbStrip armFar;
        private readonly LimbStrip legNear;
        private readonly LimbStrip legFar;
        private readonly SkinnedShorts hip;
        private readonly LimbStrip cape;
        private readonly FrontFigure front;
        private readonly FrontLayer frontCape;
        private readonly FrontLayer frontHatBack;
        private readonly FrontLayer frontPants;
        private readonly FrontLayer frontArmor;
        private readonly FrontLayer frontHat;
        private readonly FrontLayer frontWeapon;
        private readonly Vector2 capeRoot;
        private float capeLength;
        private Sprite bodySide;
        private Sprite bodyOpen;
        private Sprite bodyClosed;
        private readonly SpriteRenderer headImage;
        private readonly Dictionary<Texture2D, Color32[]> alphaMasks = new Dictionary<Texture2D, Color32[]>();
        private readonly List<Vector3> bladeSamples = new List<Vector3>();

        public EquipmentView(
            Transform head,
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
            headImage = head.Find("head").GetComponent<SpriteRenderer>();

            CharacterBone headBone = CharacterRig.Find(CharacterRig.Head);
            CharacterBone torsoBone = CharacterRig.Find(CharacterRig.Torso);
            CharacterBone handBone = CharacterRig.Find(CharacterRig.HandNear);

            hat = AddSidePart("gear_hat", head, CharacterRig.Local(HatCentreX - headBone.JointX, HatCentreY - headBone.JointY), HatOrder);
            hatBack = AddSidePart("gear_hat_back", head, hat.transform.localPosition, HatBackOrder);
            body = AddSidePart("gear_body", torso, CharacterRig.Local(BodyCentreX - torsoBone.JointX, BodyCentreY - torsoBone.JointY), BodyOrder);

            weaponPivot = new GameObject("gear_grip").transform;
            weaponPivot.SetParent(hand, false);
            weaponPivot.localPosition = CharacterRig.Local(GripX - handBone.JointX, GripY - handBone.JointY);
            weapon = AddSidePart("gear_weapon", weaponPivot, CharacterRig.Local(0f, -GripToCanvasMiddle), WeaponOrder);

            capeRoot = CharacterRig.Local(CapeRootX - torsoBone.JointX, CapeRootY - torsoBone.JointY);
            Sprite firstCape = catalog.Find(GearSlot.Cape, catalog.ItemsIn(GearSlot.Cape)[0], "strip");
            cape = new LimbStrip(firstCape, torso, CapeOrder, "gear_cape", 1f);

            frontCape = front.AddLayer("cape", null, FrontCapeOrder);
            frontHatBack = front.AddLayer("hat_back", null, FrontHatBackOrder);
            frontPants = front.AddLayer("pants", null, FrontPantsOrder);
            frontArmor = front.AddLayer("armor", null, FrontArmorOrder);
            frontHat = front.AddLayer("hat", null, FrontHatOrder);
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
                    BuildBladeSamples();
                    frontWeapon.Show(TextureOf(slot, item, "front"));
                    break;
                case GearSlot.Hat:
                    hat.sprite = catalog.Find(slot, item, "side");
                    hatBack.sprite = catalog.Find(slot, item, "back");
                    frontHat.Show(TextureOf(slot, item, "front"));
                    frontHatBack.Show(TextureOf(slot, item, "frontback"));
                    break;
                case GearSlot.Armor:
                    // A hard sleeve is armour: it folds at the elbow instead of curving.
                    Sprite hardSleeve = catalog.Find(slot, item, "sleevehard");
                    Sprite sleeve = hardSleeve != null ? hardSleeve : catalog.Find(slot, item, "sleeve");
                    body.sprite = catalog.Find(slot, item, "body");
                    bodySide = body.sprite;
                    bodyOpen = Resources.Load<Sprite>($"Character/Turns/armor_{item}_open");
                    bodyClosed = Resources.Load<Sprite>($"Character/Turns/armor_{item}_closed");
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
                case GearSlot.Cape:
                    Sprite strip = catalog.Find(slot, item, "strip");
                    cape.SetImage(strip);
                    capeLength = strip.texture.height / strip.pixelsPerUnit;
                    frontCape.Show(TextureOf(slot, item, "front"));
                    break;
            }
        }

        public Vector3 GripPoint => weaponPivot.position;
        public bool HasWeapon => weapon.sprite != null;
        public Vector3 BladeInnerPoint => weaponPivot.TransformPoint(Vector3.up * BladeInner);
        public Vector3 BladeOuterPoint => weaponPivot.TransformPoint(Vector3.up * BladeOuter);

        /// <summary>Turns the weapon around the fist; negative tips it forward of the forearm.</summary>
        public bool TurnWeapon(float degrees)
        {
            weaponPivot.localRotation = Quaternion.Euler(0f, 0f, degrees);
            // Preserve the authored blade direction. Clearance is solved by moving the
            // arm, never by flipping the tip through an unrelated arc around the wrist.
            return BladeClear();
        }

        public void RollWeapon(float degrees)
        {
            // Turn about the blade's long axis through the grip. The narrow middle
            // silhouette reads as a wrist roll instead of an instantaneous image flip.
            float width = Mathf.Cos(degrees * Mathf.Deg2Rad);
            if (Mathf.Abs(width) < 0.08f) width = width < 0f ? -0.08f : 0.08f;
            weapon.transform.localScale = new Vector3(width, 1f, 1f);
        }

        private bool BladeClear()
        {
            Matrix4x4 bladeToWorld = weapon.transform.localToWorldMatrix;
            return !Overlaps(headImage, bladeToWorld) && !Overlaps(hat, bladeToWorld);
        }

        private Color32[] Mask(Texture2D texture)
        {
            if (!alphaMasks.TryGetValue(texture, out var mask))
            {
                mask = texture.GetPixels32();
                alphaMasks.Add(texture, mask);
            }
            return mask;
        }

        private void BuildBladeSamples()
        {
            bladeSamples.Clear();
            Sprite sprite = weapon.sprite;
            Texture2D texture = sprite.texture;
            Color32[] pixels = Mask(texture);
            for (int y = texture.height / 4; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
                if (pixels[y * texture.width + x].a > 160)
                    bladeSamples.Add(new Vector3((x - sprite.pivot.x) / sprite.pixelsPerUnit, (y - sprite.pivot.y) / sprite.pixelsPerUnit));
        }

        private bool Overlaps(SpriteRenderer renderer, Matrix4x4 bladeToWorld)
        {
            if (renderer.sprite == null) return false;
            Sprite sprite = renderer.sprite;
            Texture2D texture = sprite.texture;
            Matrix4x4 toMask = renderer.transform.worldToLocalMatrix * bladeToWorld;
            float pixelsPerUnit = sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot;
            int width = texture.width, height = texture.height;
            Color32[] mask = Mask(texture);
            foreach (Vector3 sample in bladeSamples)
            {
                Vector3 local = toMask.MultiplyPoint3x4(sample);
                int x = Mathf.RoundToInt(local.x * pixelsPerUnit + pivot.x);
                int y = Mathf.RoundToInt(local.y * pixelsPerUnit + pivot.y);
                if (x < -1 || y < -1 || x > width || y > height) continue;
                // Include the border texels and leave one texel of clearance.
                for (int py = Mathf.Max(0, y - 1); py <= Mathf.Min(height - 1, y + 1); py++)
                for (int px = Mathf.Max(0, x - 1); px <= Mathf.Min(width - 1, x + 1); px++)
                    if (mask[py * width + px].a > 160) return true;
            }
            return false;
        }

        public void TwistBody(float amount)
        {
            Sprite selected = amount > 0.35f ? bodyOpen : amount < -0.35f ? bodyClosed : bodySide;
            body.sprite = selected != null ? selected : bodySide;
            body.transform.localScale = new Vector3(1f + 0.12f * Mathf.Abs(amount), 1f, 1f);
        }

        /// <summary>Hangs the cape from the shoulders. Angles are measured from straight down.</summary>
        public void PoseCape(float upperAngle, float lowerAngle, float ripple, float ripplePhase)
        {
            cape.Drape(capeRoot, capeLength, upperAngle, lowerAngle, ripple, ripplePhase);
        }

        public void Tint(Color color, float sideAlpha)
        {
            foreach (SpriteRenderer part in sideParts)
            {
                part.color = new Color(color.r, color.g, color.b, sideAlpha);
            }

            cape.Tint(color, sideAlpha);
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
