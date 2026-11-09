using System.Collections.Generic;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Plays the Free Slash VFX prefabs on the sword blows. The package is not in the repository,
    /// so everything here stays quiet when it has not been imported.
    /// </summary>
    public sealed class PackageSlashFx
    {
        private const string Folder = "ThirdParty/Free Slash VFX/Prefabs/";
        private const string SlashArt = "Slash Water VFX";
        private const string ImpactArt = "Impact";
        private const int SortingOrder = 40;

        // The prefab is built for a 3D floor; this stands its arc up facing the side camera.
        private static readonly Quaternion StandUp = Quaternion.Euler(0f, 0f, 180f) * Quaternion.Euler(-90f, 0f, 0f);

        // These need a depth buffer or a floor, which a 2D side view lacks. The stretched shafts
        // shoot across the whole screen at this camera size.
        private static readonly string[] UnusableParts = { "Slash Distortion", "Puddle", "Decal", "Shafts Streched" };

        // The package glows far past white. These keep the blows to a deep indigo with pale foam.
        private const float SlashBrightness = 1.35f;
        private const float FoamBrightness = 1.6f;

        // A slightly larger pale stroke sits behind the blade, so it shows as a rim of foam.
        private const float RimSize = 1.05f;
        private const int RimOrder = 39;

        private static readonly Color DeepWater = new Color32(0x25, 0x63, 0xD0, 0xFF);
        private static readonly Color Foam = new Color32(0xDC, 0xE6, 0xFF, 0xFF);
        private static readonly Color FlyingBack = new Color32(0x1E, 0x3F, 0x7A, 0xFF);
        private static readonly Dictionary<Material, Material> Repainted = new Dictionary<Material, Material>();

        private readonly GameObject slashPrefab;
        private readonly GameObject impactPrefab;
        private readonly List<ParticleSystem> slashes = new List<ParticleSystem>();
        private readonly List<ParticleSystem> rims = new List<ParticleSystem>();
        private readonly List<ParticleSystem> impacts = new List<ParticleSystem>();

        public PackageSlashFx()
        {
            slashPrefab = Resources.Load<GameObject>(Folder + SlashArt);
            impactPrefab = Resources.Load<GameObject>(Folder + ImpactArt);
        }

        public bool Available => slashPrefab != null && impactPrefab != null;
        public bool Enabled { get; private set; }

        /// <summary>Switches between these effects and the game's own, for comparing them.</summary>
        public void Toggle()
        {
            Enabled = Available && !Enabled;
        }

        /// <param name="tilt">Degrees to lean the arc, positive toward the top of its swing.</param>
        public void PlaySlash(Vector2 place, int facing, float size, float tilt, bool rising = false)
        {
            PlaySlash(place, facing, Vector2.one * size, tilt, rising, 0.42f);
        }

        /// <summary>Overhead cut, rising return, then a wider, lower diagonal finish.</summary>
        public void PlayComboSlash(Vector2 place, int facing, int step)
        {
            switch (step)
            {
                case 1:
                    PlaySlash(place + Vector2.up * 0.08f, facing, new Vector2(0.57f, 0.67f),
                        -12f, true, 0.4f);
                    break;
                case 2:
                    PlaySlash(place + new Vector2(facing * 0.12f, -0.04f), facing,
                        new Vector2(0.88f, 0.50f), -32f, false, 0.48f);
                    break;
                default:
                    PlaySlash(place + Vector2.up * 0.08f, facing, new Vector2(0.53f, 0.66f),
                        16f, false, 0.36f);
                    break;
            }
        }

        private void PlaySlash(Vector2 place, int facing, Vector2 size, float tilt, bool rising, float life)
        {
            // Reflect vertically to reverse the stroke while keeping its crescent in front.
            // A 180-degree turn in the screen plane also moves the crescent behind the fighter.
            Quaternion reverse = rising ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
            Quaternion facingRight = Quaternion.Euler(0f, 0f, tilt) * reverse * StandUp;
            Quaternion turn = facing < 0 ? Quaternion.Euler(0f, 180f, 0f) * facingRight : facingRight;
            // The source mesh lies in local X/Z before StandUp turns it toward the camera.
            var scale = new Vector3(size.x, Mathf.Sqrt(size.x * size.y), size.y);
            Play(slashes, slashPrefab, false, place, turn, scale, life);
            Play(rims, slashPrefab, true, place, turn, scale * RimSize, life);
        }

        public void PlayImpact(Vector2 place, float size)
        {
            Play(impacts, impactPrefab, false, place, Quaternion.identity, Vector3.one * size);
        }

        private static void Play(List<ParticleSystem> pool, GameObject prefab, bool rim, Vector2 place, Quaternion turn, Vector3 size, float life = 0f)
        {
            ParticleSystem effect = null;

            foreach (ParticleSystem candidate in pool)
            {
                if (!candidate.IsAlive(true))
                {
                    effect = candidate;
                    break;
                }
            }

            if (effect == null)
            {
                effect = Create(prefab, rim);
                pool.Add(effect);
            }

            effect.transform.SetPositionAndRotation(new Vector3(place.x, place.y, 0f), turn);
            effect.transform.localScale = size;
            if (life > 0f)
            {
                var main = effect.main;
                main.startLifetime = life;
            }
            effect.Clear(true);
            effect.Play(true);
        }

        /// <summary>Works on a copy, so the package's own files stay as they were downloaded.</summary>
        private static Material Repaint(Material original, bool isBlade)
        {
            if (Repainted.TryGetValue(original, out Material painted))
            {
                return painted;
            }

            painted = new Material(original);

            if (painted.HasProperty("_Color"))
            {
                painted.SetColor("_Color", Color.white * (isBlade ? SlashBrightness : FoamBrightness));
            }

            if (painted.HasProperty("_FrontColor"))
            {
                painted.SetColor("_FrontColor", Foam);
                painted.SetColor("_BackColor", FlyingBack);
            }

            Repainted[original] = painted;
            return painted;
        }

        private static ParticleSystem Create(GameObject prefab, bool rim)
        {
            GameObject effect = Object.Instantiate(prefab);

            foreach (Light light in effect.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }

            foreach (ParticleSystem part in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool isRoot = part.transform.parent == null;

                if (System.Array.IndexOf(UnusableParts, part.name) >= 0 || (rim && !isRoot))
                {
                    part.gameObject.SetActive(false);
                    continue;
                }

                bool isBlade = isRoot && prefab.name == SlashArt;
                ParticleSystem.MainModule main = part.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startColor = isBlade && !rim ? DeepWater : Foam;

                if (prefab.name == SlashArt)
                {
                    var fade = new Gradient();
                    fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.35f, 0.06f),
                            new GradientAlphaKey(1f, 0.16f), new GradientAlphaKey(0.9f, 0.36f),
                            new GradientAlphaKey(0.4f, 0.62f), new GradientAlphaKey(0.08f, 0.84f),
                            new GradientAlphaKey(0f, 1f) });
                    var color = part.colorOverLifetime;
                    color.enabled = true;
                    color.color = fade;
                }

                var renderer = part.GetComponent<ParticleSystemRenderer>();
                renderer.sortingOrder = rim ? RimOrder : SortingOrder;
                renderer.sharedMaterial = Repaint(renderer.sharedMaterial, isBlade);
            }

            return effect.GetComponent<ParticleSystem>();
        }
    }
}
