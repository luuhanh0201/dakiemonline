using DaKiem.Client.Net;
using Game.Net;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Touching things in the world (docs/KE-HOACH-CHAM-DOI-TUONG.md): one touch on a dummy or the boss
    /// selects it to look at, two pin it and fight it on their own until it falls; a touch on an NPC walks
    /// up to them and opens their dialog. The fighting itself is decided by <see cref="AutoFight"/>.
    /// </summary>
    public sealed class TargetControl
    {
        private const float DummyTouchHalfWidth = 0.45f;
        private const float NpcTouchHalfWidth = 0.5f;
        private const float NpcTouchHeight = 1.8f;
        private const float BossTouchHalfWidth = 1f;
        private const float BossTouchHeight = 2.5f;
        private const float TouchBelowFeet = 0.2f;
        private const float TouchAboveHead = 0.3f;

        /// <summary>How close to an NPC the walk stops and the dialog opens.</summary>
        private const double NpcTalkReach = 1d;

        private const string DummyName = "Người rơm";
        private const string BossName = "Boss";
        private const int RingOrder = -6;

        private static readonly Color SelectedRing = new Color32(0xF2, 0xC7, 0x6E, 0xE0);
        private static readonly Color PinnedRing = new Color32(0xD9, 0x4F, 0x4F, 0xE0);

        private readonly GameConfig config;
        private readonly HudView hud;
        private readonly SpriteRenderer ring;
        private Kind kind;
        private int index = -1;
        private int map = -1;
        private bool pinned;

        public TargetControl(GameConfig config, HudView hud)
        {
            this.config = config;
            this.hud = hud;
            var holder = new GameObject("Target ring");
            ring = holder.AddComponent<SpriteRenderer>();
            ring.sprite = RingSprite();
            ring.sortingOrder = RingOrder;
            ring.enabled = false;
        }

        private enum Kind
        {
            None,
            Dummy,
            Boss,
            Npc,
        }

        /// <summary>A touch on the world: picks what lies under it, or lets go of everything on empty ground.</summary>
        public void Touch(Vector2 screen, bool twice, OnlineClient online)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            Vector3 world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -camera.transform.position.z));
            MapConfig here = config.Maps[online.Map];
            map = online.Map;

            for (int npc = 0; npc < here.Npcs.Count; npc++)
            {
                if (Inside(world, here.Npcs[npc].X, NpcTouchHalfWidth, NpcTouchHeight))
                {
                    Pick(Kind.Npc, npc, true);
                    return;
                }
            }

            TrainingConfig yard = here.Training;

            for (int dummy = 0; yard != null && dummy < yard.Dummies.Count; dummy++)
            {
                if (Inside(world, yard.Dummies[dummy].X, Mathf.Max((float)yard.Width / 2f, DummyTouchHalfWidth), (float)yard.Height))
                {
                    Pick(Kind.Dummy, dummy, twice);
                    return;
                }
            }

            if (online.BossOnMap && online.Boss != null && !online.Boss.IsDead && Inside(world, online.Boss.X, BossTouchHalfWidth, BossTouchHeight))
            {
                Pick(Kind.Boss, 0, twice);
                return;
            }

            Clear();
        }

        /// <summary>Lets go of the target, as when the joystick is pushed: the selection stays, the fighting stops.</summary>
        public void StopAuto()
        {
            if (kind == Kind.Npc)
            {
                Clear();
            }

            pinned = false;
        }

        public void Clear()
        {
            kind = Kind.None;
            index = -1;
            pinned = false;
        }

        /// <summary>Forgets everything and hides the ring, the target's health and any NPC's words, as on leaving.</summary>
        public void Reset()
        {
            Clear();
            ring.enabled = false;
            hud.HideTarget();
            hud.HideDialog();
        }

        /// <summary>
        /// Decides this tick's walk and skill while pinned or walking up to an NPC; false when the player is in
        /// charge. Opens the NPC's dialog on arrival. The chosen skill fights in place of the plain attack
        /// only when it comes back quickly (<see cref="AutoFight.CanStandIn"/>).
        /// </summary>
        public bool Steer(OnlineClient online, SkillSlot? chosenSlot, out AutoStep step)
        {
            step = default;
            Predictor predictor = online.Predictor;

            if (online.Map != map)
            {
                Clear();
                map = online.Map;
            }

            if (predictor == null || kind == Kind.None || (!pinned && kind != Kind.Npc))
            {
                return false;
            }

            double self = predictor.Movement.X;

            if (kind == Kind.Npc)
            {
                MapNpc npc = config.Maps[map].Npcs[index];
                double away = npc.X - self;

                if (System.Math.Abs(away) <= NpcTalkReach)
                {
                    hud.ShowDialog(npc.Name, npc.Greeting);
                    Clear();
                    return false;
                }

                step = new AutoStep(away > 0d ? 1d : -1d, null);
                return true;
            }

            if (!TargetStanding(online, out double targetX))
            {
                pinned = false;
                return false;
            }

            SkillConfig plain = config.Skill(SkillSlot.Attack);
            SkillConfig chosen = chosenSlot == null ? null : config.Skill(chosenSlot.Value);
            chosen = chosen != null && AutoFight.CanStandIn(chosen, config.AutoFight) ? chosen : null;
            bool ready = chosen != null && Combat.IsSkillReady(predictor.Combat, predictor.Resources, chosen);
            step = AutoFight.Decide(self, predictor.Movement.Facing, targetX, plain, chosen, ready, config.AutoFight);
            return true;
        }

        /// <summary>When a skill is pressed by hand with a target selected, which way to lean to face it; 0 if already facing it.</summary>
        public double FaceTarget(OnlineClient online)
        {
            Predictor predictor = online.Predictor;

            if (predictor == null || (kind != Kind.Dummy && kind != Kind.Boss) || !TargetStanding(online, out double targetX))
            {
                return 0d;
            }

            int side = targetX >= predictor.Movement.X ? 1 : -1;
            return side == predictor.Movement.Facing ? 0d : side * AutoFight.TurnTilt;
        }

        /// <summary>Puts the ring under the target and its name and health on the HUD.</summary>
        public void Show(OnlineClient online)
        {
            bool shown = (kind == Kind.Dummy || kind == Kind.Boss) && online.Map == map;
            ring.enabled = shown;

            if (!shown)
            {
                hud.HideTarget();
                return;
            }

            double x;
            float share;
            string name;

            if (kind == Kind.Dummy)
            {
                TrainingConfig yard = config.Maps[map].Training;
                x = yard.Dummies[index].X;
                RemoteDummy state = index < online.Dummies.Count ? online.Dummies[index] : new RemoteDummy(0, false, 0);
                int hp = state.Fallen ? 0 : Mathf.Max(0, yard.MaxHp - state.Hits * yard.DamagePerHit);
                share = (float)hp / yard.MaxHp;
                name = $"{DummyName} · {hp}/{yard.MaxHp}";
                ring.transform.localScale = new Vector3((float)yard.Width * 1.6f, (float)yard.Width * 0.45f, 1f);
            }
            else
            {
                BossState boss = online.Boss;
                x = boss == null ? 0d : boss.X;
                int hp = boss == null ? 0 : boss.Hp;
                share = (float)hp / config.Boss.MaxHp;
                name = $"{BossName} · {hp}/{config.Boss.MaxHp}";
                ring.transform.localScale = new Vector3(2.2f, 0.6f, 1f);
            }

            ring.transform.position = new Vector3((float)x, 0.02f, 0f);
            ring.color = pinned ? PinnedRing : SelectedRing;
            hud.ShowTarget(name, share, pinned);
        }

        private void Pick(Kind picked, int place, bool pin)
        {
            kind = picked;
            index = place;
            pinned = pin;
        }

        private bool TargetStanding(OnlineClient online, out double x)
        {
            x = 0d;

            if (kind == Kind.Dummy)
            {
                x = config.Maps[map].Training.Dummies[index].X;
                return index >= online.Dummies.Count || !online.Dummies[index].Fallen;
            }

            if (kind == Kind.Boss && online.BossOnMap && online.Boss != null)
            {
                x = online.Boss.X;
                return !online.Boss.IsDead;
            }

            return false;
        }

        private static bool Inside(Vector3 world, double x, float halfWidth, float height)
        {
            return Mathf.Abs(world.x - (float)x) <= halfWidth
                && world.y >= -TouchBelowFeet
                && world.y <= height + TouchAboveHead;
        }

        /// <summary>A flat ring one unit across, tinted by the renderer.</summary>
        private static Sprite RingSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    float edge = Mathf.Clamp01((1f - d) * size / 2f) * Mathf.Clamp01((d - 0.72f) * size / 2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
