using System.Collections.Generic;
using Game.Net;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Net
{
    /// <summary>
    /// Draws an online fight: the local player where prediction says, everyone else and the boss a
    /// few ticks in the past so there are always two snapshots to blend between. Hits, heals and
    /// the boss's blows come from the server's events.
    /// </summary>
    public sealed class OnlineView
    {
        // A server correction is spread over a few frames instead of jumping; a big one jumps.
        private const float CorrectionFadePerSecond = 15f;
        private const float CorrectionSnapUnits = 2f;
        private const float LabelHeightUnits = 2.3f;

        // The boss only needs one snapshot gap of delay; its red zone should show as early as possible.
        private const double BossDelayTicks = 2d;

        private static readonly IReadOnlyList<CombatEvent> NoEvents = new CombatEvent[0];

        private readonly OnlineClient client;
        private readonly GameConfig config;
        private readonly CharacterView self;
        private readonly FxView selfFx;
        private readonly BossView bossView;
        private readonly WorldView world;
        private readonly Dictionary<int, Remote> others = new Dictionary<int, Remote>();
        private readonly Dictionary<int, List<CombatEvent>> combatEvents = new Dictionary<int, List<CombatEvent>>();
        private readonly HashSet<int> hurt = new HashSet<int>();
        private readonly List<NetEvent> drained = new List<NetEvent>();
        private readonly List<ProjectileSnapshot> shots = new List<ProjectileSnapshot>();
        private readonly List<int> gone = new List<int>();
        private Vector2 previousFeet;
        private Vector2 currentFeet;
        private Vector2 correction;
        private bool bossHit;

        public OnlineView(OnlineClient client, GameConfig config, CharacterView self, FxView selfFx, BossView bossView, WorldView world)
        {
            this.client = client;
            this.config = config;
            this.self = self;
            this.selfFx = selfFx;
            this.bossView = bossView;
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
            currentFeet = Feet(client.Predictor.Movement);
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

            Vector2 jump = currentFeet - Feet(client.Predictor.Movement);

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

            SortEvents();
            ShowSelf(blend, frameSeconds);
            ShowOthers(frameSeconds);
            ShowBoss();
        }

        /// <summary>Names over heads, drawn from OnGUI.</summary>
        public void DrawLabels(GUIStyle style)
        {
            Camera camera = Camera.main;

            if (camera == null || client.Predictor == null)
            {
                return;
            }

            DrawLabel(camera, style, Feet(client.Predictor.Movement) + correction, $"P{client.PlayerId} (bạn)");

            foreach (KeyValuePair<int, RemoteTrack> pair in client.Tracks)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                TrackSample sample = pair.Value.Sample(RenderTick());
                string state = (sample.Flags & RemoteFlags.Dead) != 0 ? " · đã gục"
                    : (sample.Flags & RemoteFlags.Away) != 0 ? " · mất kết nối"
                    : string.Empty;
                DrawLabel(camera, style, new Vector2(sample.X, sample.Y), $"P{pair.Key}{state}");
            }
        }

        public void Clear()
        {
            foreach (Remote remote in others.Values)
            {
                Forget(remote);
            }

            others.Clear();
            bossView.SetVisible(false);
            self.SetVisible(true);
        }

        private double RenderTick() => client.ServerClock.Tick - RemoteTrack.DefaultDelayTicks;

        /// <summary>Removes a player who left; showing their effects for a new owner hides what was playing.</summary>
        private void Forget(Remote remote)
        {
            remote.View.Destroy();
            remote.Fx.Show(new object(), Movement(0f, 0f, 1, true), new CombatState(config.Potion), null, config.Boss, NoEvents, false, false, 0f);
        }

        private void Restart()
        {
            previousFeet = currentFeet = Feet(client.Predictor.Movement);
            correction = Vector2.zero;
        }

        /// <summary>Splits the room's new events by player, as the rules' own event types.</summary>
        private void SortEvents()
        {
            foreach (List<CombatEvent> list in combatEvents.Values)
            {
                list.Clear();
            }

            hurt.Clear();
            bossHit = false;

            foreach (NetEvent item in client.DrainEvents(drained))
            {
                if (item.Kind == RoomEventKind.Combat)
                {
                    if (!combatEvents.TryGetValue(item.PlayerId, out List<CombatEvent> list))
                    {
                        list = new List<CombatEvent>();
                        combatEvents.Add(item.PlayerId, list);
                    }

                    list.Add(item.ToCombatEvent(config));
                    bossHit |= item.CombatKind == CombatEventKind.SkillHit;
                }
                else if (item.Kind == RoomEventKind.Boss && item.BossKind == BossEventKind.PlayerHit)
                {
                    hurt.Add(item.PlayerId);
                }
            }
        }

        private IReadOnlyList<CombatEvent> EventsOf(int playerId)
        {
            return combatEvents.TryGetValue(playerId, out List<CombatEvent> list) ? list : NoEvents;
        }

        private BossState LivingBoss => client.Boss != null && !client.Boss.IsDead ? client.Boss : null;

        private void ShowSelf(float blend, float frameSeconds)
        {
            Predictor predictor = client.Predictor;
            MovementState state = predictor.Movement;
            correction = Vector2.MoveTowards(correction, Vector2.zero, correction.magnitude * CorrectionFadePerSecond * frameSeconds + 0.001f);
            Vector2 feet = Vector2.Lerp(previousFeet, currentFeet, blend) + correction;
            world.Follow(feet, state.Facing);
            selfFx.Show(client, state, predictor.Combat, LivingBoss, config.Boss, EventsOf(client.PlayerId), hurt.Contains(client.PlayerId), true, frameSeconds);
            self.SetVisible(!predictor.Resources.IsDead);
            self.Show(
                feet,
                state.Facing,
                state.Grounded,
                (float)state.VelocityY,
                state.IsDashing,
                predictor.Combat.Phase,
                selfFx.PlayerHurt,
                swing: SwingMoment.Of(predictor.Combat));
        }

        private void ShowOthers(float frameSeconds)
        {
            double tick = RenderTick();

            foreach (KeyValuePair<int, RemoteTrack> pair in client.Tracks)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                if (!others.TryGetValue(pair.Key, out Remote remote))
                {
                    remote = new Remote(new CharacterView(), new FxView(world));
                    others.Add(pair.Key, remote);
                }

                TrackSample now = pair.Value.Sample(tick);
                TrackSample before = pair.Value.Sample(tick - 1d);
                bool dead = (now.Flags & RemoteFlags.Dead) != 0;
                bool grounded = (now.Flags & RemoteFlags.Grounded) != 0;
                MovementState movement = Movement(now.X, now.Y, now.Facing, grounded);
                CombatState combat = CombatState.Showing(config, now.Casting, now.CastTicks, now.ComboStep, now.CastDirection, ShotsOf(pair.Key));

                remote.View.SetVisible(!dead);
                remote.Fx.Show(remote, movement, combat, LivingBoss, config.Boss, EventsOf(pair.Key), hurt.Contains(pair.Key), false, frameSeconds);
                remote.View.Show(
                    new Vector2(now.X, now.Y),
                    now.Facing,
                    grounded,
                    (now.Y - before.Y) * Ticks.PerSecond,
                    (now.Flags & RemoteFlags.Dashing) != 0,
                    combat.Phase,
                    remote.Fx.PlayerHurt,
                    swing: SwingMoment.Of(combat));
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
                Forget(others[id]);
                others.Remove(id);
            }
        }

        private void ShowBoss()
        {
            BossState boss = LivingBoss;
            bossView.SetVisible(boss != null);

            if (boss == null || client.BossTrack.Count == 0)
            {
                return;
            }

            float x = client.BossTrack.Sample(client.ServerClock.Tick - BossDelayTicks).X;
            bossView.Show(x, boss, bossHit);
        }

        private List<ProjectileSnapshot> ShotsOf(int playerId)
        {
            shots.Clear();

            foreach (RemoteProjectile shot in client.Projectiles)
            {
                if (shot.OwnerId == playerId)
                {
                    shots.Add(new ProjectileSnapshot(shot.Slot, shot.X, shot.Y, shot.Direction, 0d));
                }
            }

            return new List<ProjectileSnapshot>(shots);
        }

        private static MovementState Movement(float x, float y, int facing, bool grounded)
        {
            return MovementState.FromSnapshot(new MovementSnapshot(x, y, 0d, facing, grounded, 0d, 0d, 0, 0, 0, 0, 0, 0, 0));
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

        private sealed class Remote
        {
            public Remote(CharacterView view, FxView fx)
            {
                View = view;
                Fx = fx;
            }

            public CharacterView View { get; }
            public FxView Fx { get; }
        }
    }
}
