using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using DaKiem.Client.Accounts;
using Game.Rules;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client.Editor
{
    /// <summary>Renders the actual account UI with sample data, without using a real account or server.</summary>
    public static class LoginScreenReview
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Dictionary<string, Rect> AlphaBounds = new Dictionary<string, Rect>();
        private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-loginReviewOut") + 1];
            Directory.CreateDirectory(output);
            UiEdgeReview.Audit(output);
            File.WriteAllText(Path.Combine(output, "character-check.txt"), "");
            File.WriteAllText(Path.Combine(output, "frame-check.txt"), "");
            // Every screen at every common shape: the mock-up, phones held sideways, 4:3 tablets, 32:9,
            // a maximised browser window, laptops, 16:10 and a small window.
            string[] screens = { "login", "register", "forgot", "link", "servers", "character", "reset", "code", "guest", "linked" };
            foreach (string size in Sizes.Keys)
                foreach (string screen in screens)
                    Capture(output, size.Length == 0 ? screen : screen + "-" + size);
            Capture(output, "character-filled");
        }

        private static readonly Dictionary<string, Vector2Int> Sizes = new Dictionary<string, Vector2Int>
        {
            [""] = new Vector2Int(1672, 941),
            ["phone"] = new Vector2Int(852, 393),
            ["tablet"] = new Vector2Int(1200, 900),
            ["ultrawide"] = new Vector2Int(2560, 720),
            ["browser"] = new Vector2Int(1920, 950),
            ["laptop"] = new Vector2Int(1366, 768),
            ["16x10"] = new Vector2Int(1280, 800),
            ["small"] = new Vector2Int(800, 600),
        };

        private static void Capture(string output, string page)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int dash = page.IndexOf('-');
            string suffix = dash < 0 ? "" : page.Substring(dash + 1);
            Vector2Int size = Sizes.TryGetValue(suffix, out Vector2Int found) ? found : Sizes[""];
            int width = size.x;
            int height = size.y;
            bool inGame = page.StartsWith("guest") || page.StartsWith("linked");
            var rules = GameConfig.ParseAccounts(File.ReadAllText(Path.Combine(Application.dataPath, "../../config/accounts.json")));
            var scene = LoginSceneConfig.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "../../config/login-scene.json")));
            UnityEngine.Random.InitState(1206);
            var screens = AccountScreens.Create(rules, scene, "ws://127.0.0.1:5080", (a, b, c, d) => { }, () => { }, false);
            Set(screens, "account", new AccountInfo { username = "khach_123456", kind = "guest" });
            var server = new ServerInfo { number = 1, name = "Thủy Nguyệt", status = "good", isNew = true };
            switch (page.Split('-')[0])
            {
                case "login":
                    Call(screens, "ShowLogin", new object[] { null });
                    foreach (var field in UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None))
                        if (field.gameObject.name == "Tài khoản") field.text = "";
                    break;
                case "register": Call(screens, "ShowRegister"); break;
                case "reset": Call(screens, "ShowPasswordReset"); break;
                case "code":
                    Call(screens, "ShowRegister");
                    Call(screens, "Say", new CodeReply { delivery = "operator", lifetimeSec = 600 }.Notice, null, (Color?)new Color32(0xAD, 0xF2, 0xCF, 0xFF));
                    break;
                case "forgot": Call(screens, "ShowForgot"); break;
                case "link": Call(screens, "ShowLink", false); break;
                case "character": Call(screens, "ShowCreateCharacter", server); break;
                case "guest": screens.ShowGuestReminderInGame(); break;
                case "linked": Call(screens, "Done", "Đã liên kết. Từ nay đăng nhập bằng tài khoản <color=#F2C76E>kiemkhach_99</color>.", true); break;
                case "servers":
                    Call(screens, "ShowServers", new object[] { null });
                    Set(screens, "servers", new[] { server }); Set(screens, "chosenServer", server);
                    var grid = GameObject.Find("Servers").GetComponent<RectTransform>();
                    for (int i = grid.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(grid.GetChild(i).gameObject);
                    Call(screens, "FillServers", grid);
                    ((Button)screens.GetType().GetField("enterButton", Private).GetValue(screens)).interactable = true;
                    break;
            }
            if (page.EndsWith("filled"))
                foreach (InputField field in screens.GetComponentsInChildren<InputField>()) field.text = "KiếmKhách";
            if (page == "character-filled")
                foreach (InputField field in UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None)) field.text = "KiếmKhách";
            var camera = new GameObject("AuditCamera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = height / 2f;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 100;
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = Mathf.Min(width / 1672f, height / 941f);
                canvas.scaleFactor = scaler.scaleFactor;
            }
            Canvas.ForceUpdateCanvases();
            foreach (var presentation in UnityEngine.Object.FindObjectsByType<AccountPresentation>(FindObjectsSortMode.None))
                Call(presentation, "LateUpdate");
            Canvas.ForceUpdateCanvases();
            var bg = UnityEngine.Object.FindFirstObjectByType<LivingBackground>(FindObjectsInactive.Include);
            if (!inGame) Call(bg, "LateUpdate");
            // In the game the scenery is hidden and the dimmed game shows instead.
            if (!inGame) CheckCharacter(output, page, bg, camera, width, height);
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour.GetType().Name == "OneLine") Call(behaviour, "LateUpdate");
            foreach (var backdrop in UnityEngine.Object.FindObjectsByType<AccountMessageBackdrop>(FindObjectsSortMode.None))
                Call(backdrop, "LateUpdate");
            CheckFrame(output, page, bg, camera, width, height);
            foreach (InputField field in UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None))
            {
                var hint = field.placeholder as Text;
                if (hint != null && hint.preferredWidth > hint.rectTransform.rect.width + 1f)
                    throw new InvalidOperationException($"Input hint overflows: {page}: {hint.text}");
            }
            Canvas.ForceUpdateCanvases();
            camera.Render(); camera.Render();
            Text status = (Text)screens.GetType().GetField("message", Private).GetValue(screens);
            if (status != null && !string.IsNullOrEmpty(status.text))
            {
                TextGenerationSettings settings = status.GetGenerationSettings(status.rectTransform.rect.size);
                status.cachedTextGenerator.Populate(status.text, settings);
                settings.fontSize = status.cachedTextGenerator.fontSizeUsedForBestFit;
                settings.resizeTextForBestFit = false;
                float textHeight = status.cachedTextGeneratorForLayout.GetPreferredHeight(status.text, settings) / status.pixelsPerUnit;
                if (textHeight > status.rectTransform.rect.height + 1f)
                    throw new InvalidOperationException($"Status text clipped: {page}, {textHeight} > {status.rectTransform.rect.height}");
                var corners = new Vector3[4];
                status.rectTransform.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = camera.WorldToScreenPoint(corner);
                    if (point.x < 0 || point.x > width || point.y < 0 || point.y > height)
                        throw new InvalidOperationException($"Status outside screen: {page}: {point}");
                }
            }
            RenderTexture.active = target;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0); shot.Apply();
            File.WriteAllBytes(Path.Combine(output, page + ".png"), shot.EncodeToPNG());
            if (page == "login") CaptureWind(output, bg, camera, target);
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(shot);
            camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target);
            Debug.Log("LOGIN_REVIEW_CAPTURE " + page);
        }

        private static void CheckCharacter(string output, string page, LivingBackground bg, Camera camera, int width, int height)
        {
            var figure = (RectTransform)typeof(LivingBackground).GetField("character", Private).GetValue(bg);
            var panel = (RectTransform)typeof(LivingBackground).GetField("panel", Private).GetValue(bg);
            Vector3[] panelCorners = new Vector3[4];
            panel.GetWorldCorners(panelCorners);
            float panelLeft = camera.WorldToScreenPoint(panelCorners[0]).x;
            float clearance = float.MaxValue;
            var hair = figure.Find("Breathing/lop7b_bay_01") as RectTransform;
            var breathing = figure.Find("Breathing") as RectTransform;
            // Forty seconds covers breathing and repeated gusts. Test actual alpha extents, not the transparent full-frame image box.
            for (int sample = 0; sample <= 1200; sample++)
            {
                Call(bg, "Animate", sample / 30f, 1f / 30f);
                // The hair pivot must stay under the tie through breathing and every wind extremum.
                Vector3 tie = breathing.TransformPoint(new Vector3(214f - 255f, 732f - 358f));
                if (Vector3.Distance(hair.position, tie) > 0.01f)
                    throw new InvalidOperationException($"Hair root separates from tie: {page}, t={sample / 30f}");
                foreach (Image image in figure.GetComponentsInChildren<Image>())
                {
                    Rect alpha = BoundsOf(image.sprite);
                    Rect local = image.rectTransform.rect;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float x = corner % 2 == 0 ? alpha.xMin : alpha.xMax;
                        float y = corner < 2 ? alpha.yMin : alpha.yMax;
                        Vector3 world = image.rectTransform.TransformPoint(new Vector3(local.xMin + x * local.width, local.yMin + y * local.height));
                        Vector3 point = camera.WorldToScreenPoint(world);
                        float gap = Mathf.Min(point.x, width - point.x, point.y, height - point.y, panelLeft - point.x);
                        clearance = Mathf.Min(clearance, gap);
                        if (gap < 2f) throw new InvalidOperationException($"Character clipped: {page}, {image.name}, t={sample / 30f}, point={point}, panel={panelLeft}");
                    }
                }
            }
            File.AppendAllText(Path.Combine(output, "character-check.txt"), $"PASS {page}: 1201 frames, complete body/hair/scarf/sash/sword; minimum clearance {clearance:F1}px\n");
        }

        /// <summary>
        /// Everything on the screen stays on the screen, and anything that touches the frame sits wholly in its
        /// navy area and off the gold bottom corners. Things placed fully outside the frame, such as VÀO GAME
        /// below the server list, are fine. The logo, the title plaque and the X belong on the beam by design.
        /// </summary>
        private static void CheckFrame(string output, string page, LivingBackground bg, Camera camera, int width, int height)
        {
            var panel = (RectTransform)typeof(LivingBackground).GetField("panel", Private).GetValue(bg);
            var stage = (RectTransform)panel.parent;
            Rect frame = StageRect(stage, panel);
            Rect inner = Rect.MinMaxRect(frame.xMin + Kit("FrameSide"), frame.yMin + Kit("FrameTop"), frame.xMax - Kit("FrameSide"), frame.yMax - Kit("FrameBottom"));
            Rect leftCorner = Rect.MinMaxRect(frame.xMin, frame.yMax - Kit("FrameCornerHeight"), frame.xMin + Kit("FrameCornerWidth"), frame.yMax);
            Rect rightCorner = Rect.MinMaxRect(frame.xMax - Kit("FrameCornerWidth"), frame.yMax - Kit("FrameCornerHeight"), frame.xMax, frame.yMax);
            var parts = new List<RectTransform>();
            foreach (RectTransform child in stage)
            {
                if (child.name == "Servers") foreach (RectTransform card in child) parts.Add(card);
                else if (child.name != "logo" && child.name != "panel" && child.name != "title" && child.name != "close") parts.Add(child);
            }

            int checkedParts = 0;
            var corners = new Vector3[4];
            var placed = new List<KeyValuePair<string, Rect>>();
            foreach (RectTransform part in parts)
            {
                if (!part.gameObject.activeInHierarchy) continue;
                var graphic = part.GetComponent<Graphic>();
                if (graphic != null && !graphic.enabled) continue;
                checkedParts++;
                Rect box = StageRect(stage, part);

                // The tab strip is a backing behind its two buttons; nothing else may sit on top of another part.
                if (part.name != "Tabs")
                {
                    Rect core = Rect.MinMaxRect(box.xMin + 1f, box.yMin + 1f, box.xMax - 1f, box.yMax - 1f);
                    foreach (var other in placed)
                        if (core.Overlaps(other.Value))
                            throw new InvalidOperationException($"Overlaps {other.Key}: {page}: {part.name} {box}");
                    placed.Add(new KeyValuePair<string, Rect>(part.name, box));
                }
                string where = $"{page}: {part.name} {box}";
                if (box.Overlaps(frame))
                {
                    const float slack = 0.5f;
                    if (box.xMin < inner.xMin - slack || box.xMax > inner.xMax + slack || box.yMin < inner.yMin - slack || box.yMax > inner.yMax + slack)
                        throw new InvalidOperationException($"Crosses the frame border: {where}, navy area {inner}");
                    if (box.Overlaps(leftCorner) || box.Overlaps(rightCorner))
                        throw new InvalidOperationException($"Covers a gold corner: {where}");
                }
                part.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = camera.WorldToScreenPoint(corner);
                    if (point.x < -0.5f || point.x > width + 0.5f || point.y < -0.5f || point.y > height + 0.5f)
                        throw new InvalidOperationException($"Off the screen at {width}x{height}: {where}");
                }
            }
            File.AppendAllText(Path.Combine(output, "frame-check.txt"), $"PASS {page} ({width}x{height}): {checkedParts} parts inside the frame or clear of it, all on screen\n");
        }

        /// <summary>A layout constant of the client's internal UiKit, such as the frame's measured border.</summary>
        private static float Kit(string name)
        {
            Type kit = typeof(AccountScreens).Assembly.GetType("DaKiem.Client.Accounts.UiKit");
            return (float)kit.GetField(name, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
        }

        /// <summary>A rectangle in mock-up coordinates: top-left origin, y down, as UiKit.Place takes them.</summary>
        private static Rect StageRect(RectTransform stage, RectTransform part)
        {
            var corners = new Vector3[4];
            part.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector3 local = stage.InverseTransformPoint(corner);
                var point = new Vector2(local.x + Kit("StageWidth") / 2f, Kit("StageHeight") / 2f - local.y);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect BoundsOf(Sprite sprite)
        {
            string path = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(path)) path = AssetDatabase.GetAssetPath(sprite.texture);
            string key = path + sprite.rect;
            if (AlphaBounds.TryGetValue(key, out Rect bounds)) return bounds;
            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(path));
            var pixels = texture.GetPixels32();
            int offsetX = Mathf.RoundToInt(sprite.rect.x / sprite.texture.width * texture.width);
            int offsetY = Mathf.RoundToInt(sprite.rect.y / sprite.texture.height * texture.height);
            int width = Mathf.RoundToInt(sprite.rect.width / sprite.texture.width * texture.width);
            int height = Mathf.RoundToInt(sprite.rect.height / sprite.texture.height * texture.height);
            int left = width, right = 0, bottom = height, top = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    // Ignore faint isolated export residue; the character's antialiased silhouette is > 12.5% alpha.
                    if (pixels[(y + offsetY) * texture.width + x + offsetX].a > 32)
                    { left = Mathf.Min(left, x); right = Mathf.Max(right, x + 1); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y + 1); }
            bounds = Rect.MinMaxRect((float)left / width, (float)bottom / height, (float)right / width, (float)top / height);
            UnityEngine.Object.DestroyImmediate(texture);
            AlphaBounds[key] = bounds;
            return bounds;
        }

        private static void CaptureWind(string output, LivingBackground bg, Camera camera, RenderTexture target)
        {
            var sheet = new Texture2D(1200, 620, TextureFormat.RGB24, false);
            for (int i = 0; i < 4; i++)
            {
                Call(bg, "Animate", 6.4f + i * 0.45f, 0f);
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                sheet.ReadPixels(new Rect(20, 941 - 540, 300, 310), i * 300, 310);
                sheet.ReadPixels(new Rect(0, 0, 300, 310), i * 300, 0);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(output, "hair-and-rock-sequence.png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);

            // Render the terrain alone to ensure moving grass cannot distort adjacent rocks or flowers.
            var root = (RectTransform)typeof(LivingBackground).GetField("root", Private).GetValue(bg);
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.gameObject != bg.gameObject) canvas.gameObject.SetActive(false);
            foreach (Transform child in root) child.gameObject.SetActive(child.name == "lop4_co");
            var grass = root.Find("lop4_co").GetComponent<GrassTipMotion>();
            Color32[][] frames = new Color32[2][];
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            for (int i = 0; i < 2; i++)
            {
                grass.Animate(i * 0.7f, 1.3f);
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                frames[i] = pixels.GetPixels32();
                File.WriteAllBytes(Path.Combine(output, $"terrain-{i}.png"), pixels.EncodeToPNG());
            }
            int changed = 0;
            for (int y = 0; y < target.height; y++)
                for (int x = 0; x < target.width; x++)
                {
                    int at = y * target.width + x;
                    if (frames[0][at].Equals(frames[1][at])) continue;
                    changed++;
                    // Whole rock shelves, foreground flowers, and the solid lower hillside are static.
                    int top = target.height - 1 - y;
                    if (!GrassTipMotion.IsTipRegion(new Vector2(x, top), 4f))
                        throw new InvalidOperationException($"Grass deforms neighbouring image at ({x},{top})");
                    bool protectedRock = x >= 180 && x <= 400 && top >= 720 && top <= 840;
                    bool protectedFlowers = x >= 150 && x <= 220 && top >= 795 && top <= 855;
                    bool protectedGround = top >= 916;
                    if (protectedRock || protectedFlowers || protectedGround)
                        throw new InvalidOperationException($"Terrain moves outside grass tips at ({x},{top})");
                }
            if (changed == 0) throw new InvalidOperationException("Grass animation is not visible");
            File.WriteAllText(Path.Combine(output, "wind-check.txt"), $"PASS: {changed} changed pixels confined to grass tip patches; all neighbouring imagery and protected rocks, flowers and lower terrain pixel-identical.\n");
            UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
