#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Game.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>Opt-in PC UI check; uses sample forms and delayed local callbacks, never a real account.</summary>
    public sealed class AccountUiSmoke : MonoBehaviour
    {
        public static bool Enabled => Array.IndexOf(Environment.GetCommandLineArgs(), "-account-ui-smoke") >= 0;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private AccountScreens screens;
        private string output;
        private int passed;
        private bool failed;
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        private T Read<T>(string field) => (T)typeof(AccountScreens).GetField(field, Private).GetValue(screens);
        private void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++;
            File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS " + name + "\n");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Enabled) new GameObject("AccountUiSmoke").AddComponent<AccountUiSmoke>();
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            output = args[Array.IndexOf(args, "-account-ui-smoke") + 1];
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "checks.txt"), "");
            Application.logMessageReceived += OnLog;
            IEnumerator check = Check();
            while (!failed)
            {
                bool more;
                try { more = check.MoveNext(); }
                catch (Exception error) { failed = true; File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n"); break; }
                if (!more) break;
                yield return check.Current;
            }
            Application.logMessageReceived -= OnLog;
            File.AppendAllText(Path.Combine(output, "checks.txt"), $"RESULT {(failed ? "FAIL" : "PASS")}: {passed} checks\n");
            Application.Quit(failed ? 1 : 0);
        }

        private void OnLog(string condition, string trace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            failed = true;
            File.AppendAllText(Path.Combine(output, "checks.txt"), condition + "\n" + trace + "\n");
        }

        private IEnumerator Check()
        {
            var rules = GameConfig.ParseAccounts(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "config/accounts.json")));
            var scene = LoginSceneConfig.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "config/login-scene.json")));
            screens = AccountScreens.Create(rules, scene, "ws://127.0.0.1:1", (a,b,c,d) => {}, () => {}, false);
            Call(screens, "ShowLogin", new object[] { null });
            yield return new WaitForSecondsRealtime(0.3f);
            Require(Read<CanvasGroup>("inputGroup").alpha > 0.99f, "Screen fade finishes");
            RectTransform content = Read<RectTransform>("content");
            var options = content.Find("Tools/TrialOptions").gameObject;
            Button tools = content.Find("Tools/Công cụ bản thử").GetComponent<Button>();
            Require(!options.activeSelf, "Trial tools are collapsed on login");
            tools.onClick.Invoke();
            Require(options.activeSelf && options.GetComponentInChildren<InputField>() != null, "Trial tools open with server input");
            Capture("trial-tools-pc.png");
            tools.onClick.Invoke();
            Require(!options.activeSelf, "Trial tools close without leaving login");
            InputField user = GameObject.Find("Tài khoản").GetComponent<InputField>();
            InputField password = GameObject.Find("Mật khẩu").GetComponent<InputField>();
            var presentation = Read<RectTransform>("content").GetComponent<AccountPresentation>();
            EventSystem.current.SetSelectedGameObject(null);
            Call(presentation, "MoveFocus", true);
            Require(EventSystem.current.currentSelectedGameObject == password.gameObject, "Shift Tab without focus selects last field");
            Call(presentation, "MoveFocus", false);
            Require(EventSystem.current.currentSelectedGameObject == user.gameObject, "Tab wraps to first field");
            user.text = "";
            Button login = GameObject.Find("ĐĂNG NHẬP").GetComponent<Button>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(login.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(0.12f);
            Require(login.transform.localScale.x < 0.99f, "Button press feedback");
            ExecuteEvents.Execute(login.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            // A real desktop cursor can hover this button even in a hidden player. End both states
            // before checking its resting scale, and allow a few slow first frames after asset load.
            ExecuteEvents.Execute(login.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            float settleUntil = Time.unscaledTime + 1f;
            while (Mathf.Abs(login.transform.localScale.x - 1f) >= 0.005f && Time.unscaledTime < settleUntil)
                yield return null;
            Require(Mathf.Abs(login.transform.localScale.x - 1f) < 0.005f, "Button restores scale");
            ExecuteEvents.Execute(login.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Require(Read<Text>("message").text == "Nhập tài khoản và mật khẩu.", "Empty login shows validation");
            user.text = "kiemthu";
            Require(user.GetComponent<AccountFieldCaption>().Caption.gameObject.activeSelf, "Filled input retains label");
            password.text = "abcdef";
            Button eye = password.transform.Find("Eye").GetComponent<Button>();
            eye.onClick.Invoke();
            Require(password.contentType == InputField.ContentType.Standard, "Password eye reveals");
            eye.onClick.Invoke();
            Require(password.contentType == InputField.ContentType.Password, "Password eye hides");
            user.text = ""; password.text = "";
            Capture("login-pc.png");
            yield return new WaitForSecondsRealtime(0.15f);

            bool stale = false;
            Call(screens, "Run", Delay(0.2f, () => stale = true), false);
            Require(!Read<CanvasGroup>("inputGroup").interactable, "Waiting disables conflicting controls");
            Call(screens, "ShowRegister");
            yield return new WaitForSecondsRealtime(0.35f);
            Require(!stale, "Closed screen ignores old callback");
            Require(!Read<bool>("busy") && Read<CanvasGroup>("inputGroup").interactable, "New screen remains usable");
            Capture("register-pc.png");

            bool completed = false;
            Call(screens, "Run", Delay(0.05f, () =>
            {
                Call(screens, "ShowForgot");
                Call(screens, "Run", Delay(0.4f, () => completed = true), false);
            }), false);
            yield return new WaitForSecondsRealtime(0.15f);
            Require(Read<bool>("busy") && !Read<CanvasGroup>("inputGroup").interactable, "Chained request stays locked after previous completes");
            yield return new WaitForSecondsRealtime(0.45f);
            Require(completed && !Read<bool>("busy"), "Chained request unlocks on completion");

            var reply = new CodeReply { contact = "ng***@vidu.vn", lifetimeSec = 300, delivery = "operator" };
            Require(reply.Notice.Contains("người vận hành") && reply.Notice.Contains("Chưa gửi email/SMS"), "Log sender explains operator code retrieval");
            Call(screens, "ShowRegister");
            Call(screens, "Say", reply.Notice, null, (Color?)UiKit.Green);
            yield return new WaitForSecondsRealtime(0.25f);
            Capture("code-operator-pc.png");
            reply.delivery = "contact";
            Require(reply.Notice.Contains(reply.contact) && reply.Notice.StartsWith("Đã gửi"), "Contact sender confirms masked destination");
            reply.delivery = null;
            Require(!reply.Notice.Contains("Đã gửi"), "Legacy server never promises code delivery");

            Call(screens, "ShowPasswordReset");
            yield return new WaitForSecondsRealtime(0.25f);
            Require(Read<Text>("message").color == UiKit.Green && Read<Text>("message").text.Contains("Đã đổi mật khẩu"), "Password reset confirmation uses success styling");
            Capture("reset-success-pc.png");

            Call(screens, "ShowCreateCharacter", new ServerInfo { number = 1, name = "Thủy Nguyệt" });
            yield return new WaitForSecondsRealtime(0.25f);
            InputField name = Read<RectTransform>("screen").GetComponentInChildren<InputField>();
            name.text = "";
            string hint = Read<Text>("message").text;
            name.onSubmit.Invoke(name.text);
            Require(Read<Text>("message").text != hint && Read<Text>("message").color == UiKit.ErrorText, "Character name Enter runs validation");

            Call(screens, "ShowLogin", new object[] { null });
            yield return new WaitForSecondsRealtime(0.25f);
            // Capture real frames through a gust and verify the common breathing parent keeps all pieces together.
            var figure = GameObject.Find("Character").transform;
            var breathing = figure.Find("Breathing");
            Require(breathing.Find("lop7a_nguoi") != null && breathing.Find("lop7b_bay_01") != null && breathing.Find("lop7b_bay_02") != null, "Body and moving pieces share breathing parent");
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSecondsRealtime(1f);
                Capture($"animation-{i:D2}.png");
            }
            yield return new WaitForSecondsRealtime(0.2f);
            Require(File.Exists(Path.Combine(output, "animation-07.png")), "PC animation frames captured");
        }

        private static IEnumerator Delay(float seconds, Action completed)
        {
            yield return new WaitForSecondsRealtime(seconds);
            completed();
        }

        private void Capture(string name)
        {
            // Hidden Windows players have no presented backbuffer. Render the live canvases explicitly.
            var camera = new GameObject("UiCaptureCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -1000f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            camera.targetTexture = target;
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];
            var cameras = new Camera[canvases.Length];
            var distances = new float[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode; cameras[i] = canvases[i].worldCamera; distances[i] = canvases[i].planeDistance;
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = camera; canvases[i].planeDistance = 100f;
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
            RenderTexture.active = null;
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i];
            }
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = null; target.Release();
            Destroy(target); Destroy(pixels); Destroy(camera.gameObject);
        }
    }
}
#endif
