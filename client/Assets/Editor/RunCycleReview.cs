using System;
using System.IO;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    public static class RunCycleReview
    {
        public static void Run()
        {
            Review(4f, 0.24f, "run");
        }

        public static void Walk()
        {
            Review(2f, 0.56f, "walk");
        }

        private static void Review(float travelSpeed, float stance, string label)
        {
            var view = new CharacterView();
            Transform root = GameObject.Find("Character").transform;
            Transform near = root.Find("Rig/hip/foot_near");
            Transform far = root.Find("Rig/hip/foot_far");
            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 0.65f;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.2f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var cell = new RenderTexture(220, 280, 24);
            camera.targetTexture = cell;
            var sheet = new Texture2D(220 * 8, 280 * 3, TextureFormat.RGB24, false);
            int contacts = 0;
            float worstSlide = 0f;
            var phaseField = typeof(CharacterView).GetField("runCycle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (int fps in new[] { 30, 60, 120 })
            {
                Vector3 oldNear = Vector3.zero, oldFar = Vector3.zero;
                bool nearWasDown = false, farWasDown = false;
                float x = 0f;
                for (int frame = 0; frame < fps * 4; frame++)
                {
                    float speed = frame < fps * 3 ? travelSpeed : 0f;
                    x += speed / fps;
                    view.Show(new Vector2(x, 0f), 1, true, 0f, false, CastPhase.None, false, 1f / fps);
                    if (frame > fps && frame < fps * 3)
                    {
                        float phase = (float)phaseField.GetValue(view);
                        CheckContact(near.position, phase < stance, ref oldNear, ref nearWasDown, ref contacts, ref worstSlide);
                        CheckContact(far.position, Mathf.Repeat(phase + 0.5f, 1f) < stance, ref oldFar, ref farWasDown, ref contacts, ref worstSlide);
                    }
                    foreach (Transform joint in root.GetComponentsInChildren<Transform>(true))
                        if (float.IsNaN(joint.position.x) || float.IsNaN(joint.position.y))
                            throw new Exception("Non-finite joint: " + joint.name);
                    // A full real-time cycle, including channel easing, at 60 Hz.
                    int sample = frame - fps;
                    if (fps == 60 && sample >= 0 && sample < 24)
                    {
                        camera.transform.position = new Vector3(x, 0.55f, -10f);
                        // Unity can display stale sprite geometry on the first manual render.
                        if (sample == 0) camera.Render();
                        camera.Render();
                        RenderTexture.active = cell;
                        sheet.ReadPixels(new Rect(0, 0, 220, 280), sample % 8 * 220, (2 - sample / 8) * 280);
                    }
                }
            }
            if (contacts < 10 || worstSlide > 0.003f)
                throw new Exception($"Foot support failed: {contacts} samples, slide {worstSlide}");
            RenderTexture.active = null;
            sheet.Apply();
            File.WriteAllBytes($"Logs/{label}-continuous.png", sheet.EncodeToPNG());
            Debug.Log($"{label} review passed at 30/60/120 Hz: {contacts} support samples, max slide {worstSlide:F6} units/frame.");
        }

        private static void CheckContact(Vector3 foot, bool down, ref Vector3 previous, ref bool wasDown, ref int count, ref float worst)
        {
            if (down && Mathf.Abs(foot.y - 73f / CharacterRigScale.LayoutPixelsPerUnit) > 0.001f)
                throw new Exception("Planted ankle height changed: " + foot.y);
            if (down && wasDown)
            {
                count++;
                worst = Mathf.Max(worst, Mathf.Abs(foot.x - previous.x));
            }
            previous = foot;
            wasDown = down;
        }
    }
}
