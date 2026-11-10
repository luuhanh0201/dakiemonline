using System.Collections.Generic;
using Game.Net;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Net
{
    /// <summary>
    /// Draws an online stay: the local player where prediction says, everyone else a few ticks in
    /// the past so there are always two snapshots to blend between.
    /// </summary>
    public sealed class OnlineView
    {
        // A server correction is spread over a few frames instead of jumping; a big one jumps.
        private const float CorrectionFadePerSecond = 15f;
        private const float CorrectionSnapUnits = 2f;
        private const float LabelHeightUnits = 2.3f;

        private readonly OnlineClient client;
        private readonly CharacterView self;
        private readonly WorldView world;
        private readonly Dictionary<int, CharacterView> others = new Dictionary<int, CharacterView>();
        private readonly List<int> gone = new List<int>();
        private Vector2 previousFeet;
        private Vector2 currentFeet;
        private Vector2 correction;

        public OnlineView(OnlineClient client, CharacterView self, WorldView world)
        {
            this.client = client;
            this.self = self;
            this.world = world;
            client.Welcomed += Restart;
        }

        /// <summary>Call after each tick, before the next is predicted.</summary>
        public void AfterTick()
        {
            if (client.Predictor == null)
            {
                return;
            }

            previousFeet = currentFeet;
            currentFeet = Feet(client.Predictor.State);
        }

        /// <summary>
        /// Call after the client handled packets. If a snapshot moved the prediction since the last
        /// tick, the jump is smoothed rather than seen.
        /// </summary>
        public void SmoothCorrection()
        {
            if (client.Predictor == null)
            {
                return;
            }

            Vector2 jump = currentFeet - Feet(client.Predictor.State);

            if (jump.sqrMagnitude == 0f)
            {
                return;
            }

            // Move both ends of the tick blend with the prediction, and hide the jump in the offset that fades.
            previousFeet -= jump;
            currentFeet -= jump;
            correction = jump.magnitude > CorrectionSnapUnits ? Vector2.zero : correction + jump;
        }

        public void Show(float blend, float frameSeconds)
        {
            if (client.Predictor == null)
            {
                return;
            }

            MovementState state = client.Predictor.State;
            correction = Vector2.MoveTowards(correction, Vector2.zero, correction.magnitude * CorrectionFadePerSecond * frameSeconds + 0.001f);
            Vector2 feet = Vector2.Lerp(previousFeet, currentFeet, blend) + correction;
            world.Follow(feet, state.Facing);
            self.Show(feet, state.Facing, state.Grounded, (float)state.VelocityY, state.IsDashing, CastPhase.None, false);
            ShowOthers();
        }

        /// <summary>Names over heads, drawn from OnGUI.</summary>
        public void DrawLabels(GUIStyle style)
        {
            Camera camera = Camera.main;

            if (camera == null || client.Predictor == null)
            {
                return;
            }

            DrawLabel(camera, style, Feet(client.Predictor.State) + correction, $"P{client.PlayerId} (bạn)");

            foreach (KeyValuePair<int, RemoteTrack> pair in client.Tracks)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                TrackSample sample = pair.Value.Sample(RenderTick());
                string away = (sample.Flags & RemoteFlags.Away) != 0 ? " · mất kết nối" : string.Empty;
                DrawLabel(camera, style, new Vector2(sample.X, sample.Y), $"P{pair.Key}{away}");
            }
        }

        public void Clear()
        {
            foreach (CharacterView view in others.Values)
            {
                view.Destroy();
            }

            others.Clear();
        }

        private double RenderTick() => client.ServerClock.Tick - RemoteTrack.DefaultDelayTicks;

        private void Restart()
        {
            previousFeet = currentFeet = Feet(client.Predictor.State);
            correction = Vector2.zero;
        }

        private void ShowOthers()
        {
            double tick = RenderTick();

            foreach (KeyValuePair<int, RemoteTrack> pair in client.Tracks)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                if (!others.TryGetValue(pair.Key, out CharacterView view))
                {
                    view = new CharacterView();
                    others.Add(pair.Key, view);
                }

                TrackSample now = pair.Value.Sample(tick);
                TrackSample before = pair.Value.Sample(tick - 1d);
                float verticalSpeed = (now.Y - before.Y) * Ticks.PerSecond;
                view.Show(
                    new Vector2(now.X, now.Y),
                    now.Facing,
                    (now.Flags & RemoteFlags.Grounded) != 0,
                    verticalSpeed,
                    (now.Flags & RemoteFlags.Dashing) != 0,
                    CastPhase.None,
                    false);
            }

            gone.Clear();

            foreach (int id in others.Keys)
            {
                if (!client.Tracks.ContainsKey(id))
                {
                    gone.Add(id);
                }
            }

            foreach (int id in gone)
            {
                others[id].Destroy();
                others.Remove(id);
            }
        }

        private static void DrawLabel(Camera camera, GUIStyle style, Vector2 feet, string text)
        {
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(feet.x, feet.y + LabelHeightUnits, 0f));

            if (screen.z < 0f)
            {
                return;
            }

            var size = style.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(screen.x - size.x / 2f, Screen.height - screen.y - size.y, size.x, size.y), text, style);
        }

        private static Vector2 Feet(MovementState state) => new Vector2((float)state.X, (float)state.Y);
    }
}
