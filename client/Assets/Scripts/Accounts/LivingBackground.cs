using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>The numbers of config/login-scene.json. Distances are in pixels of the 1672 x 941 frame.</summary>
    [Serializable]
    public sealed class LoginSceneConfig
    {
        public string id;
        public string status;
        public float cloudDrift = 30f;
        public float cloudPeriodSec = 26f;
        public float starTwinkleSpeed = 0.35f;
        public float starBrighten = 0.35f;
        public float moonGlowPeriodSec = 5f;
        public float moonGlowPulse = 0.07f;
        public float grassSway = 0.0012f;
        public float grassSpeed = 0.55f;
        public float grassWaves = 9f;
        public float blossomSway = 0.0025f;
        public float blossomSpeed = 0.3f;
        public float blossomWaves = 1.5f;
        public float gustEveryMinSec = 6f;
        public float gustEveryMaxSec = 12f;
        public float gustStrength = 1.3f;
        public float gustRiseSec = 1.2f;
        public float gustFallSec = 2.6f;
        public float breathScale = 0.006f;
        public float breathPeriodSec = 3.2f;
        public float hairSwayDeg = 2f;
        public float scarfSwayDeg = 2.5f;
        public float sashSwayDeg = 2.5f;
        public float partPeriodSec = 2.8f;
        public int petalCount = 7;
        public float petalSpeedMin = 45f;
        public float petalSpeedMax = 90f;
        public float petalFall = 35f;
        public float petalSpinDeg = 120f;
        public float petalSize = 26f;

        public static LoginSceneConfig Parse(string json)
        {
            return string.IsNullOrEmpty(json) ? new LoginSceneConfig() : JsonUtility.FromJson<LoginSceneConfig>(json);
        }
    }

    /// <summary>
    /// The moving night scene behind the account screens, rebuilt from the layers in
    /// Resources/UI/Background (docs/PROMPT-NEN-DONG.md): clouds drift, stars twinkle, grass and
    /// blossom sway, the swordsman breathes while his hair and scarf move, petals blow across, and
    /// every few seconds a gust makes it all stronger for a moment. Missing layers fall back to the
    /// flat picture.
    /// </summary>
    public sealed class LivingBackground : MonoBehaviour
    {
        private const string Folder = "UI/Background/";
        private static readonly Vector2 Moon = new Vector2(1456f, 264f);
        private static readonly Vector2 Feet = new Vector2(255f, 732f);
        private static readonly Vector2 BodyCentre = new Vector2(262f, 470f);

        private readonly List<Swaying> swaying = new List<Swaying>();
        private readonly List<Petal> petals = new List<Petal>();
        private LoginSceneConfig config;
        private RectTransform canvas;
        private RectTransform root;
        private readonly List<Drifting> clouds = new List<Drifting>();
        private RectTransform body;
        private RectTransform character;
        private RectTransform panel;
        private Sprite hairSprite;
        private readonly Vector3[] panelCorners = new Vector3[4];
        // Includes the sword and the swept hair/scarf, with padding for every animation extremum.
        private static readonly Rect CharacterEnvelope = Rect.MinMaxRect(20f, 240f, 505f, 756f);
        private Image moonGlow;
        private GrassTipMotion grass;
        private Material blossom;
        private Sprite[] petalSprites = Array.Empty<Sprite>();
        private float gust = 1f;
        private float nextGust;
        private float gustStarted = -100f;

        private sealed class Swaying
        {
            public RectTransform Rect;
            public float Degrees;
            public float Period;
            public float Phase;
        }

        private sealed class Drifting
        {
            public RectTransform Rect;
            public float HomeX;

            /// <summary>-1 cut off on the left, 1 on the right, 0 whole: which way it may drift.</summary>
            public int Side;
            public float Period;
            public float Phase;
        }

        /// <summary>Each cloud of lop2 on its own, where it sat, drifting slowly.</summary>
        private void Clouds()
        {
            var manifest = Resources.Load<TextAsset>(Folder + "lop2_may");

            if (manifest == null)
            {
                return;
            }

            foreach (CutPiece piece in JsonUtility.FromJson<CutManifest>(manifest.text).pieces)
            {
                Sprite sprite = Load(piece.name);

                if (sprite == null)
                {
                    continue;
                }

                var image = UiKit.Rect(piece.name, root).gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.raycastTarget = false;
                var rect = (RectTransform)image.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(piece.w, piece.h);
                rect.anchoredPosition = new Vector2(piece.x, -piece.y);
                clouds.Add(new Drifting
                {
                    Rect = rect,
                    HomeX = piece.x,
                    Side = piece.x <= 2f ? -1 : piece.x + piece.w >= UiKit.StageWidth - 3f ? 1 : 0,
                    Period = config.cloudPeriodSec * UnityEngine.Random.Range(0.8f, 1.25f),
                    Phase = UnityEngine.Random.value,
                });
            }
        }

        private sealed class Petal
        {
            public RectTransform Rect;
            public float Speed;
            public float Spin;
            public float Phase;
        }

        public static LivingBackground Create(LoginSceneConfig config, int order)
        {
            RectTransform canvas = UiKit.CreateCanvas("LivingBackground", order);
            var background = canvas.gameObject.AddComponent<LivingBackground>();
            background.config = config ?? new LoginSceneConfig();
            background.canvas = canvas;
            background.Build();
            return background;
        }

        public void Show(bool visible)
        {
            canvas.gameObject.SetActive(visible);
        }

        public void SetPanel(RectTransform value) => panel = value;

        private void OnDestroy()
        {
            if (hairSprite != null)
            {
                if (Application.isPlaying) Destroy(hairSprite);
                else DestroyImmediate(hairSprite);
            }
        }

        private void Build()
        {
            UiKit.Shade(canvas, 1f).raycastTarget = false;
            root = UiKit.Stage(canvas);

            if (Load("lop1_troi") == null || Load("lop7a_nguoi") == null)
            {
                Layer("nen_goc");
                return;
            }

            Image sky = Layer("lop1_troi");
            sky.material = Material("Shaders/UiTwinkle");
            sky.material.SetFloat("_Speed", config.starTwinkleSpeed);
            sky.material.SetFloat("_Amount", config.starBrighten);
            sky.material.SetVector("_Moon", new Vector4(Moon.x / UiKit.StageWidth, 1f - Moon.y / UiKit.StageHeight, 0.16f, 0f));

            moonGlow = UiKit.Place(UiKit.Rect("MoonGlow", root).gameObject.AddComponent<Image>(), Moon.x - 240f, Moon.y - 240f, 480f, 480f);
            moonGlow.sprite = Glow();
            moonGlow.raycastTarget = false;

            Clouds();
            Layer("lop3_den");
            Layer("lop5_than");

            // The canopy moves inside its own picture, so the parts the frame cuts off stay at the edge.
            Image canopy = Layer("lop6_hoa");
            blossom = Material("Shaders/UiSway");
            blossom.SetFloat("_Amp", config.blossomSway);
            blossom.SetFloat("_Speed", config.blossomSpeed);
            blossom.SetFloat("_Freq", config.blossomWaves);
            blossom.SetFloat("_ProbeOn", 0f);
            blossom.SetFloat("_Along", 1f);
            blossom.SetFloat("_EdgeFade", 0.03f);
            canopy.material = blossom;

            Image ground = Layer("lop4_co");
            grass = ground.gameObject.AddComponent<GrassTipMotion>();
            grass.Amplitude = config.grassSway * UiKit.StageWidth;
            grass.Speed = config.grassSpeed;

            character = FigureRoot("Character", root);
            body = FigureRoot("Breathing", character);
            Pieces("lop7b_bay", BodyCentre, 0f, config.partPeriodSec, PartDegrees, PartRoot);
            Layer("lop7a_nguoi", body);

            if (Load("lop9_co_truoc") != null)
            {
                // A different artwork needs its own authored tip regions. Never warp unknown foreground.
                Layer("lop9_co_truoc");
            }

            BuildPetals();
            // Decorative foreground must not paint over or appear to cut the character's feet or sword.
            character.SetAsLastSibling();
            nextGust = Time.time + config.gustEveryMinSec;
        }

        private void LateUpdate()
        {
            Cover();

            if (body == null)
            {
                return;
            }

            Animate(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        private void Animate(float t, float deltaTime)
        {
            UpdateGust(t);

            foreach (Drifting cloud in clouds)
            {
                // A cloud the frame cuts off only drifts outward, so its cut side never comes into view.
                float wave = Mathf.Sin((t / cloud.Period + cloud.Phase) * Mathf.PI * 2f);
                float offset = cloud.Side == 0 ? wave : cloud.Side * (0.5f + 0.5f * wave);
                cloud.Rect.anchoredPosition = new Vector2(cloud.HomeX + offset * config.cloudDrift, cloud.Rect.anchoredPosition.y);
            }

            float glow = 0.55f + config.moonGlowPulse * Mathf.Sin(t * Mathf.PI * 2f / config.moonGlowPeriodSec);
            moonGlow.color = new Color(0.85f, 0.9f, 1f, glow);

            grass.Animate(t, gust);
            blossom.SetFloat("_Gust", gust);

            foreach (Swaying part in swaying)
            {
                float wave = Mathf.Sin((t / part.Period + part.Phase) * Mathf.PI * 2f) * 0.7f
                           + Mathf.Sin((t / part.Period * 1.7f + part.Phase * 2f) * Mathf.PI * 2f) * 0.3f;

                // Wind blows to the right, so a gust leans everything that way a little; at most
                // gustStrength times the calm swing, so a part never pulls away from the body.
                part.Rect.localRotation = Quaternion.Euler(0f, 0f, -(wave + (gust - 1f)) * part.Degrees);
            }

            float breath = 1f + config.breathScale * Mathf.Sin(t * Mathf.PI * 2f / config.breathPeriodSec);
            body.localScale = new Vector3(1f, breath, 1f);

            MovePetals(deltaTime, t);
        }

        /// <summary>Scales the frame so the scene covers the whole screen, cutting the sides or top as needed.</summary>
        private void Cover()
        {
            Vector2 size = canvas.rect.size;
            float scale = Mathf.Max(size.x / UiKit.StageWidth, size.y / UiKit.StageHeight);
            root.localScale = new Vector3(scale, scale, 1f);
            root.anchoredPosition = Vector2.zero;
            if (character == null) return;

            // Scenery can cover/crop; the complete figure fits the visible strip before the form.
            // Keep its authored foot position whenever possible, so it stays on the rock.
            Rect safe = new Rect(-size.x / 2f + 24f, -size.y / 2f + 24f, size.x - 48f, size.y - 48f);
            Canvas owner = canvas.GetComponent<Canvas>();
            if (Application.isPlaying && Screen.width > 0 && Screen.height > 0)
            {
                Rect area = Screen.safeArea;
                safe.xMin = Mathf.Max(safe.xMin, area.xMin / Screen.width * size.x - size.x / 2f + 24f);
                safe.xMax = Mathf.Min(safe.xMax, area.xMax / Screen.width * size.x - size.x / 2f - 24f);
                safe.yMin = Mathf.Max(safe.yMin, area.yMin / Screen.height * size.y - size.y / 2f + 24f);
                safe.yMax = Mathf.Min(safe.yMax, area.yMax / Screen.height * size.y - size.y / 2f - 24f);
            }
            if (panel != null && panel.gameObject.activeInHierarchy)
            {
                panel.GetWorldCorners(panelCorners);
                Canvas formCanvas = panel.GetComponentInParent<Canvas>();
                Camera formCamera = formCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : formCanvas.worldCamera;
                Camera backgroundCamera = owner.renderMode == RenderMode.ScreenSpaceOverlay ? null : owner.worldCamera;
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(formCamera, panelCorners[0]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screenPoint, backgroundCamera, out Vector2 edge);
                safe.xMax = Mathf.Min(safe.xMax, edge.x - 20f);
            }
            float fit = Mathf.Max(0.05f, Mathf.Min(scale, safe.width / CharacterEnvelope.width, safe.height / CharacterEnvelope.height));
            Vector2 foot = new Vector2((Feet.x - UiKit.StageWidth / 2f) * scale, (UiKit.StageHeight / 2f - Feet.y) * scale);
            Vector2 authoredFoot = foot;
            foot.x = Mathf.Clamp(foot.x, safe.xMin + (Feet.x - CharacterEnvelope.xMin) * fit, safe.xMax - (CharacterEnvelope.xMax - Feet.x) * fit);
            foot.y = Mathf.Clamp(foot.y, safe.yMin + (CharacterEnvelope.yMax - Feet.y) * fit, safe.yMax - (Feet.y - CharacterEnvelope.yMin) * fit);
            // Pan the overscanned scenery with the figure, preserving the rock underneath its feet.
            Vector2 overflow = new Vector2(Mathf.Max(0f, UiKit.StageWidth * scale - size.x), Mathf.Max(0f, UiKit.StageHeight * scale - size.y)) * 0.5f;
            root.anchoredPosition = new Vector2(Mathf.Clamp(foot.x - authoredFoot.x, -overflow.x, overflow.x), Mathf.Clamp(foot.y - authoredFoot.y, -overflow.y, overflow.y));
            character.localScale = Vector3.one * (fit / scale);
            character.anchoredPosition = new Vector2((foot.x - root.anchoredPosition.x) / scale + UiKit.StageWidth / 2f, (foot.y - root.anchoredPosition.y) / scale - UiKit.StageHeight / 2f);
        }

        private static RectTransform FigureRoot(string name, RectTransform parent)
        {
            RectTransform figure = UiKit.Rect(name, parent);
            figure.anchorMin = figure.anchorMax = new Vector2(0f, 1f);
            figure.sizeDelta = new Vector2(UiKit.StageWidth, UiKit.StageHeight);
            figure.pivot = new Vector2(Feet.x / UiKit.StageWidth, 1f - Feet.y / UiKit.StageHeight);
            figure.anchoredPosition = new Vector2(Feet.x, -Feet.y);
            return figure;
        }

        private void UpdateGust(float t)
        {
            if (t >= nextGust)
            {
                gustStarted = t;
                nextGust = t + config.gustRiseSec + config.gustFallSec + UnityEngine.Random.Range(config.gustEveryMinSec, config.gustEveryMaxSec);
            }

            float since = t - gustStarted;
            float strength = since < config.gustRiseSec
                ? Mathf.SmoothStep(0f, 1f, since / config.gustRiseSec)
                : 1f - Mathf.SmoothStep(0f, 1f, (since - config.gustRiseSec) / config.gustFallSec);
            gust = 1f + (config.gustStrength - 1f) * Mathf.Clamp01(strength);
        }

        private void BuildPetals()
        {
            var sprites = new List<Sprite>();

            for (int i = 1; i <= 12; i++)
            {
                Sprite sprite = Load($"lop8_canh_{i:D2}");

                if (sprite != null)
                {
                    sprites.Add(sprite);
                }
            }

            petalSprites = sprites.ToArray();

            if (petalSprites.Length == 0)
            {
                return;
            }

            for (int i = 0; i < config.petalCount; i++)
            {
                var image = UiKit.Rect("Petal", root).gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                var petal = new Petal { Rect = (RectTransform)image.transform };
                petal.Rect.anchorMin = petal.Rect.anchorMax = new Vector2(0f, 1f);
                Respawn(petal, image, scatter: true);
                petals.Add(petal);
            }
        }

        private void Respawn(Petal petal, Image image, bool scatter)
        {
            image.sprite = petalSprites[UnityEngine.Random.Range(0, petalSprites.Length)];
            float size = config.petalSize * UnityEngine.Random.Range(0.7f, 1.3f);
            petal.Rect.sizeDelta = new Vector2(size, size);
            petal.Speed = UnityEngine.Random.Range(config.petalSpeedMin, config.petalSpeedMax);
            petal.Spin = UnityEngine.Random.Range(-config.petalSpinDeg, config.petalSpinDeg);
            petal.Phase = UnityEngine.Random.value * 10f;

            // From the blossom, or anywhere at the start so the sky is not empty for the first seconds.
            Vector2 at = scatter
                ? new Vector2(UnityEngine.Random.Range(0f, UiKit.StageWidth), UnityEngine.Random.Range(0f, UiKit.StageHeight * 0.8f))
                : new Vector2(UnityEngine.Random.Range(-40f, 420f), UnityEngine.Random.Range(0f, 420f));
            petal.Rect.anchoredPosition = new Vector2(at.x, -at.y);
        }

        private void MovePetals(float dt, float t)
        {
            foreach (Petal petal in petals)
            {
                Vector2 p = petal.Rect.anchoredPosition;
                float drift = Mathf.Sin(t * 1.3f + petal.Phase) * 22f;
                p += new Vector2(petal.Speed * gust, -(config.petalFall + drift)) * dt;
                petal.Rect.anchoredPosition = p;
                petal.Rect.localRotation = Quaternion.Euler(0f, 0f, petal.Rect.localEulerAngles.z + petal.Spin * dt * gust);

                if (p.x > UiKit.StageWidth + 60f || -p.y > UiKit.StageHeight + 60f)
                {
                    Respawn(petal, petal.Rect.GetComponent<Image>(), scatter: false);
                }
            }
        }

        // Pieces of lop7b in reading order: the ponytail, the scarf, the sash. Each turns about where it
        // leaves the body, measured on the art (the back of the head, the neck, the hip).
        private static readonly Vector2[] PartRoots = { new Vector2(238f, 340f), new Vector2(336f, 478f), new Vector2(305f, 566f) };

        private float PartDegrees(string name)
        {
            return name.EndsWith("_01") ? config.hairSwayDeg : name.EndsWith("_02") ? config.scarfSwayDeg : config.sashSwayDeg;
        }

        private static Vector2? PartRoot(string name)
        {
            int index = name.EndsWith("_01") ? 0 : name.EndsWith("_02") ? 1 : name.EndsWith("_03") ? 2 : -1;
            return index < 0 ? (Vector2?)null : PartRoots[index];
        }

        /// <summary>
        /// Puts back the cut-out pieces of a layer where they sat, each turning about the point of its
        /// box nearest to what it hangs from (the trunk for blossom, the body for hair and scarf).
        /// </summary>
        private void Pieces(string layer, Vector2 hangsFrom, float degrees, float period, Func<string, float> degreesOf = null, Func<string, Vector2?> rootOf = null)
        {
            var manifest = Resources.Load<TextAsset>(Folder + layer);

            if (manifest == null)
            {
                return;
            }

            CutManifest cut = JsonUtility.FromJson<CutManifest>(manifest.text);

            foreach (CutPiece piece in cut.pieces)
            {
                Sprite sprite = Load(piece.name);

                if (sprite == null)
                {
                    continue;
                }

                bool hair = piece.name == "lop7b_bay_01";
                float pieceWidth = piece.w;
                if (hair)
                {
                    // The export's last 16 columns contain a detached scarf fragment, beyond the hair.
                    // Use a native sprite sub-rectangle; the ponytail's entire silhouette stays intact.
                    Rect source = sprite.rect;
                    source.width *= 184f / 200f;
                    hairSprite = sprite = Sprite.Create(sprite.texture, source, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    pieceWidth = 184f;
                }

                Vector2 from = rootOf?.Invoke(piece.name) ?? hangsFrom;
                var pivot = new Vector2(Mathf.Clamp(from.x, piece.x, piece.x + piece.w), Mathf.Clamp(from.y, piece.y, piece.y + piece.h));
                var image = UiKit.Rect(piece.name, body).gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.raycastTarget = false;
                var rect = (RectTransform)image.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(pieceWidth, piece.h);
                rect.pivot = new Vector2((pivot.x - piece.x) / pieceWidth, 1f - (pivot.y - piece.y) / piece.h);
                // Seat the root beneath the yellow tie at (214, 358), not above the head.
                rect.anchoredPosition = new Vector2(pivot.x, -pivot.y) + (hair ? new Vector2(-24f, -18f) : Vector2.zero);
                swaying.Add(new Swaying
                {
                    Rect = rect,
                    Degrees = degreesOf?.Invoke(piece.name) ?? degrees,
                    Period = period * UnityEngine.Random.Range(0.85f, 1.15f),
                    Phase = UnityEngine.Random.value,
                });
            }
        }

        [Serializable]
        private sealed class CutManifest
        {
            public CutPiece[] pieces = Array.Empty<CutPiece>();
        }

        [Serializable]
        private sealed class CutPiece
        {
            public string name;
            public float x;
            public float y;
            public float w;
            public float h;
        }

        /// <summary>A full-frame layer, stretched over the whole 1672 x 941 frame.</summary>
        private Image Layer(string name, RectTransform parent = null)
        {
            var image = UiKit.Rect(name, parent != null ? parent : root).gameObject.AddComponent<Image>();
            image.sprite = Load(name);
            image.raycastTarget = false;
            var rect = (RectTransform)image.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(UiKit.StageWidth, UiKit.StageHeight);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(UiKit.StageWidth / 2f, -UiKit.StageHeight / 2f);
            return image;
        }

        private static Sprite Load(string name) => Resources.Load<Sprite>(Folder + name);

        private static Material Material(string shader)
        {
            var found = Resources.Load<Shader>(shader);
            return found == null ? null : new Material(found);
        }

        /// <summary>A soft round glow, white in the middle and clear at the edge.</summary>
        private static Sprite Glow()
        {
            const int Size = 128;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = new Vector2(x + 0.5f - Size / 2f, y + 0.5f - Size / 2f).magnitude / (Size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * 0.6f));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }
    }
}
