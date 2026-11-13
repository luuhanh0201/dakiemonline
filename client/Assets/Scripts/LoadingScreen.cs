using DaKiem.Client.Accounts;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>
    /// The loading screen: a full-screen illustration, picked at random each time, a gold progress bar,
    /// a line saying what is being done and a playing tip. It covers entering the game, until the
    /// character, the bag and the map are ready, and every trip through a door. It fades in and out.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        private const int Order = 900;
        private const float FadeInSeconds = 0.18f;
        private const float FadeOutSeconds = 0.35f;
        private const float BarFollowPerSecond = 2.5f;

        // Shown at random; they only say what the game already does.
        private static readonly string[] Tips =
        {
            "Đứng ở cửa rồi bấm nút trên cửa (hoặc phím E) để sang map khác.",
            "Bấm Túi đồ (hoặc phím B) để mặc, tháo hay vứt đồ.",
            "Người rơm ở sân tập ngã sau một đòn và tự dựng lại sau 10 giây.",
            "Lại gần sư phụ hay người bán hàng, họ sẽ chào bạn.",
            "Ngày và đêm trong làng theo giờ thật ở Việt Nam.",
            "Đồ vứt ra đất trong nhà chỉ mình bạn thấy và nhặt được.",
        };

        private CanvasGroup group;
        private Image art;
        private AspectRatioFitter fitter;
        private Image fill;
        private Text status;
        private Text tip;
        private Sprite[] arts;
        private float shown;
        private float target;
        private float wanted;
        private float minimumUntil;
        private bool closing;

        /// <summary>True while any of it is on screen; touches and keys belong to it then.</summary>
        public bool Visible => group != null && group.alpha > 0.001f;

        /// <summary>True once it fully covers the game, so the world can be swapped behind it unseen.</summary>
        public bool Covering => group != null && group.alpha >= 0.999f && !closing;

        public static LoadingScreen Create()
        {
            RectTransform canvas = UiKit.CreateCanvas("Loading", Order);
            var screen = canvas.gameObject.AddComponent<LoadingScreen>();
            screen.Build(canvas);
            return screen;
        }

        /// <summary>Starts covering the screen with a new picture and tip.</summary>
        public void Begin(string what, float progress, float minimumSeconds)
        {
            if (!Visible || closing)
            {
                if (arts.Length > 0)
                {
                    art.sprite = arts[Random.Range(0, arts.Length)];
                    fitter.aspectRatio = art.sprite.rect.width / art.sprite.rect.height;
                }

                tip.text = Tips[Random.Range(0, Tips.Length)];
                shown = 0f;
                target = 0f;
                fill.fillAmount = 0f;
            }

            closing = false;
            wanted = 1f;
            minimumUntil = Time.unscaledTime + minimumSeconds;
            Report(what, progress);
            group.blocksRaycasts = true;
        }

        public void Report(string what, float progress)
        {
            status.text = what;
            target = Mathf.Max(target, Mathf.Clamp01(progress));
        }

        /// <summary>Everything is ready: the bar fills and the screen fades out, not before its shortest time.</summary>
        public void Finish(string what)
        {
            Report(what, 1f);
            closing = true;
        }

        /// <summary>Gone at once, as when joining fails and the account screens take over.</summary>
        public void HideNow()
        {
            closing = false;
            wanted = 0f;
            target = 0f;
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        private void Update()
        {
            float frame = Time.unscaledDeltaTime;
            shown = Mathf.MoveTowards(shown, target, frame * BarFollowPerSecond);
            fill.fillAmount = shown;

            if (closing && shown >= 0.999f && Time.unscaledTime >= minimumUntil)
            {
                wanted = 0f;
            }

            float speed = wanted > group.alpha ? 1f / FadeInSeconds : 1f / FadeOutSeconds;
            group.alpha = Mathf.MoveTowards(group.alpha, wanted, frame * speed);

            if (group.alpha <= 0f && wanted <= 0f)
            {
                closing = false;
                target = 0f;
                group.blocksRaycasts = false;
            }
        }

        private void Build(RectTransform canvas)
        {
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var arts = new System.Collections.Generic.List<Sprite>();

            for (int number = 1; ; number++)
            {
                Sprite picture = Resources.Load<Sprite>($"UI/Loading/loading_{number}");

                if (picture == null)
                {
                    break;
                }

                arts.Add(picture);
            }

            this.arts = arts.ToArray();

            // Behind the picture, for screens of another shape than it: the night blue of the game.
            Image back = UiKit.Shade(canvas, 1f);
            back.color = UiKit.Navy;

            RectTransform holder = UiKit.Rect("Art", canvas);
            UiKit.Fill(holder);
            art = holder.gameObject.AddComponent<Image>();
            art.raycastTarget = false;
            fitter = holder.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1536f / 1024f;

            if (this.arts.Length > 0)
            {
                art.sprite = this.arts[0];
            }

            RectTransform stage = UiKit.Stage(canvas);

            // A dark band along the bottom, so the bar and the words read on any picture.
            Image band = UiKit.Shade(stage, 0.62f);
            UiKit.Place(band, -400f, 690f, 2472f, 330f);

            status = UiKit.Place(UiKit.Label(stage, "Đang tải...", 32, Color.white), 336f, 706f, 1000f, 44f);

            Image barBack = UiKit.Place(UiKit.Image(stage, "bar_back", true), 386f, 758f, 900f, 46f);
            fill = UiKit.Place(UiKit.Image(stage, "bar_fill"), 398f, 768f, 876f, 26f);
            fill.preserveAspect = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            barBack.raycastTarget = false;
            fill.raycastTarget = false;

            tip = UiKit.Place(UiKit.Label(stage, string.Empty, 26, UiKit.Gold, TextAnchor.MiddleCenter, true, FontStyle.Normal), 186f, 816f, 1300f, 40f);
        }
    }
}
