using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>Fits the form into the safe area; the artwork uses a separate, uncropped character layout.</summary>
    [DefaultExecutionOrder(-20)]
    public sealed class AccountPresentation : MonoBehaviour
    {
        public RectTransform Stage;
        public CanvasGroup Group;
        public float Duration = 0.18f;
        private float opened;
        private readonly Vector3[] corners = new Vector3[4];

        private void OnEnable() => opened = Time.unscaledTime;

        private void LateUpdate() => RefreshLayout();

        internal void RefreshLayout()
        {
            if (Stage == null) return;
            var canvas = GetComponentInParent<Canvas>();
            var host = (RectTransform)transform;
            Rect safe = Application.isPlaying ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);
            if (Application.isPlaying && Screen.width > 0 && Screen.height > 0)
            {
                host.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
                host.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
                host.offsetMin = host.offsetMax = Vector2.zero;
            }
            float scale = Mathf.Min(1f, host.rect.width / UiKit.StageWidth, host.rect.height / UiKit.StageHeight);
            float progress = Application.isPlaying ? Mathf.Clamp01((Time.unscaledTime - opened) / Mathf.Max(0.01f, Duration)) : 1f;
            float ease = 1f - Mathf.Pow(1f - progress, 3f);
            Stage.localScale = Vector3.one * scale;
            Stage.anchoredPosition = new Vector2(150f * scale, -12f * (1f - ease));
            Group.alpha = ease;

            // Keep the selected field above the native keyboard, without shrinking its text again.
            if (Application.isPlaying && TouchScreenKeyboard.visible && EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                var field = selected == null ? null : selected.GetComponent<InputField>();
                if (field != null && field.isFocused && field.transform.IsChildOf(Stage))
                {
                    ((RectTransform)field.transform).GetWorldCorners(corners);
                    Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    float bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y;
                    float keyboardTop = TouchScreenKeyboard.area.yMax;
                    if (keyboardTop > 0f)
                        Stage.anchoredPosition += Vector2.up * Mathf.Max(0f, keyboardTop + 16f - bottom) / canvas.scaleFactor;
                }
            }
            if (Application.isPlaying) HandleTab();
        }

        private void HandleTab()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame || EventSystem.current == null || !Group.interactable) return;
            MoveFocus(keyboard.shiftKey.isPressed);
        }

        private void MoveFocus(bool backwards)
        {
            if (EventSystem.current == null || !Group.interactable) return;
            var fields = System.Array.FindAll(Stage.GetComponentsInChildren<InputField>(), field => field.IsInteractable());
            if (fields.Length == 0) return;
            int current = System.Array.FindIndex(fields, field => field.gameObject == EventSystem.current.currentSelectedGameObject);
            int next = current < 0 ? (backwards ? fields.Length - 1 : 0)
                : (current + (backwards ? -1 : 1) + fields.Length) % fields.Length;
            fields[next].Select(); fields[next].ActivateInputField();
        }
    }

    /// <summary>Small unscaled feedback, with no overshoot that could cover nearby controls.</summary>
    public sealed class AccountButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private bool pressed;
        private bool hovered;
        private Selectable control;
        private Vector3 rest;
        private void Awake() { control = GetComponent<Selectable>(); rest = transform.localScale; }
        private void OnDisable() { pressed = hovered = false; transform.localScale = rest; }
        private void Update()
        {
            bool active = control != null && control.IsInteractable();
            float target = !active ? 1f : pressed ? 0.975f : hovered ? 1.012f : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, rest * target, 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
            if (!active) pressed = hovered = false;
        }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) pressed = true; }
        public void OnPointerUp(PointerEventData e) => pressed = false;
        public void OnPointerEnter(PointerEventData e) => hovered = true;
        public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
    }

    /// <summary>
    /// Keeps status text readable over the scenery, without a blank plate when there is no message. A long
    /// message first gets a smaller size, down to <see cref="Smallest"/>; if it still does not fit, the
    /// plate grows upward, its bottom edge staying put, so the words never run out of it.
    /// </summary>
    public sealed class AccountMessageBackdrop : MonoBehaviour
    {
        private const float PadX = 16f;
        private const float PadY = 6f;

        public Text Label;
        public Image Plate;
        public int Largest = 30;
        public int Smallest = 20;
        private float baseHeight = -1f;
        private float bottom;
        private string fitted;

        private void LateUpdate()
        {
            if (Label == null || Plate == null) return;
            Plate.enabled = !string.IsNullOrWhiteSpace(Label.text);
            if (Label.text == fitted) return;
            fitted = Label.text;

            var plate = (RectTransform)Plate.transform;
            if (baseHeight < 0f)
            {
                baseHeight = plate.sizeDelta.y;
                bottom = plate.anchoredPosition.y - baseHeight * (1f - plate.pivot.y);
            }

            Label.resizeTextForBestFit = false;
            Label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Label.verticalOverflow = VerticalWrapMode.Overflow;
            float width = plate.sizeDelta.x - 2f * PadX;
            float room = baseHeight - 2f * PadY;
            Label.fontSize = Largest;

            while (Label.fontSize > Smallest && Needed(width) > room)
            {
                Label.fontSize--;
            }

            float height = Mathf.Max(baseHeight, Needed(width) + 2f * PadY);
            plate.sizeDelta = new Vector2(plate.sizeDelta.x, height);
            plate.anchoredPosition = new Vector2(plate.anchoredPosition.x, bottom + height * (1f - plate.pivot.y));
            Label.rectTransform.offsetMin = new Vector2(PadX, PadY);
            Label.rectTransform.offsetMax = new Vector2(-PadX, -PadY);
        }

        /// <summary>The height the text takes at the current size when wrapped to the given width.</summary>
        private float Needed(float width)
        {
            TextGenerationSettings settings = Label.GetGenerationSettings(new Vector2(width, 0f));
            return Label.cachedTextGeneratorForLayout.GetPreferredHeight(Label.text, settings) / Label.pixelsPerUnit;
        }
    }

    /// <summary>A small persistent caption after typing, so a filled field still has a name.</summary>
    public sealed class AccountFieldCaption : MonoBehaviour
    {
        public InputField Field;
        public Text Caption;
        public void Refresh(string value)
        {
            bool filled = !string.IsNullOrEmpty(value);
            Caption.gameObject.SetActive(filled);
            RectTransform text = Field.textComponent.rectTransform;
            text.offsetMin = new Vector2(text.offsetMin.x, filled ? 2f : 6f);
            text.offsetMax = new Vector2(text.offsetMax.x, filled ? -27f : -6f);
        }
    }
}
