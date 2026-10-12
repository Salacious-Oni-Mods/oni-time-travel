using KSerialization;
using TUNING;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// The Time Machine: a 4x4 building wearing <c>fx_time_machine_kanim</c> (drawn from the concept
    /// sheet by art/time-machine/make.py) with a Warp Emitter on its glass.
    ///
    /// It plays the sheet's cycle (idle, enter, charge, travel, return, exit, cooldown), drives the
    /// warp from it and carries a rider. Alone (FXTIME) the rider rides and steps out of the same
    /// machine. With a <see cref="TimeMachine.Partner"/> on another world (TTPREHIST, the
    /// time-travel proof of concept) the rider leaves through this machine and arrives at the
    /// partner, which is the whole of "time travel" in the saved game: a world hop, never a rewind.
    /// Built Anywhere, with no ports, from the Equipment menu. On a TIME TRAVEL game the partner is
    /// built for the player on the twin, at the same place (<see cref="TimeMachine.Pair"/>).
    /// </summary>
    public sealed class TimeMachineConfig : IBuildingConfig
    {
        public const string Id = "FxTimeMachine";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 4, 4, "fx_time_machine_kanim", 100, 3f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER0, MATERIALS.ALL_METALS, 2400f, BuildLocationRule.Anywhere,
                decor: BUILDINGS.DECOR.NONE, noise: NOISE_POLLUTION.NONE);
            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "HollowMetal";
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            WarpEmitter warp = go.AddOrGet<WarpEmitter>();
            // The glass centre: the sheet's dome is 2.0 cells above the floor, and a 4-wide
            // building draws 0.5 cell right of its origin cell (BuildingDef.GetVisualizerOffset).
            warp.CentreOffset = TimeMachine.GlassCentre;
            warp.RadiusCells = 4f;
            warp.Strength = 0f;
            warp.Emitting = false;
            go.AddOrGet<TimeMachine>();
            go.AddOrGet<TimeMachineSeat>();
            // RIDE GAMEPLAY: radiation while the machine charges, hops and holds,
            // a vanilla Constant emitter as RadiationLightConfig / HEPEngineConfig set theirs up.
            // Spaced Out only; without it there is no radiation grid and the machine has none.
            if (DlcManager.FeatureRadiationEnabled())
            {
                RadiationEmitter rad = go.AddComponent<RadiationEmitter>();
                rad.emitType = RadiationEmitter.RadiationEmitterType.Constant;
                rad.emitRads = TimeMachine.RadiationRads;
                rad.emitRadiusX = TimeMachine.RadiationRadius;
                rad.emitRadiusY = TimeMachine.RadiationRadius;
                rad.emissionOffset = new Vector3(TimeMachine.GlassCentre.x, TimeMachine.GlassCentre.y, 0f);
            }
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }

        internal static void InstallStrings()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Time Machine");
            Strings.Add(prefix + "DESC", "Where the dinosaur is, is where the bed will be.");
            Strings.Add(prefix + "EFFECT", "On a TIME TRAVEL asteroid, sends a duplicant to its partner machine on the "
                + "prehistoric twin, at the same place long ago, and back. Its partner is built with it. "
                + "One ride every 5 cycles, at either end. Select it and press Time Travel to send a duplicant.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("Equipment"), Id);
        }
    }

    /// <summary>
    /// Steps the sheet's cycle on the SIM clock (GameClock: stops when paused, follows game speed,
    /// and is the same clock on every machine of a multiplayer game) and sets the warp's strength
    /// from the step.
    ///
    /// <b>THE RIDE</b>. The rider climbs into the chair in the glass first; the
    /// cycle starts only once they are seated (<see cref="TimeMachineSeat"/>). Charge: the swirl
    /// rises, the screen darkens and shakes, bolts arc from the glass to the walls, floor and
    /// ceiling, and the rider fades out of the chair. Travel: a short white pulse under the
    /// swirl, the rider gone; with a partner the rider is moved there at its end (<see cref="Hop"/>),
    /// still faded out, so the ride plays as one cutscene in order on either rig.
    /// Hold: at whichever machine they arrive at, the same pulse, then the rider fades back in,
    /// seated, while the swirl keeps going. Return and Exit: the swirl winds down with the rider
    /// still seated; only when it has finished do they get out of the chair and leave.
    ///
    /// The look: a short white under the swirl, the swirl on top, predominant and lasting, plus a
    /// dark tint, screen shake and bolts that hit real nearby solid cells. One
    /// timeline drives them all (<see cref="Animate"/>). Every overlay is its own controller with
    /// an explicit depth, front to back: bolts, the swirl (the portal symbol, hidden on the
    /// machine itself), the white, the dark shade.
    ///
    /// <b>THE LENGTH</b> (about 15 seconds, a cutscene): Enter 0.99, Charge
    /// 4.62, Travel 3.0, Hold 3.5, Return 1.98, Exit 0.99 = 15.08 s (was 10.05, and only 7.55 s
    /// on a partnered ride, whose hop came at the start of Travel). The kanim steps got more
    /// frames in make.py rather than a slower play speed, so the swirl turns as fast as before;
    /// the charge is a build-up: the rider stays seated for its first 45 %, and the warp, shake
    /// and bolt rate climb on t squared.
    ///
    /// <b>THE COOLDOWN</b> (once every 5 cycles, on the sim clock, saved, immune to reload). One timestamp, <see cref="ReadyAt"/>, in GameClock seconds, written on BOTH
    /// machines at the moment of departure, so either end refuses a ride until it passes. It is
    /// the WarpPortal pattern (its <c>rechargeProgress</c> is serialized sim time) made a
    /// timestamp: no per-tick update, frozen while paused, and a reload of the same save brings
    /// back the same number. While it has not passed the machine idles on its amber screen.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class TimeMachine : KMonoBehaviour
    {
        public enum Step { Idle, Enter, Charge, Travel, Hold, Return, Exit, Cooldown }

        /// <summary>The glass centre from the origin cell, in cells (see TimeMachineConfig).</summary>
        public static readonly Vector2 GlassCentre = new Vector2(0.5f, 1.5f);

        // Anim lengths are the kanim's frame counts at 33 ms (make.py); the loops are held.
        public const float EnterSeconds = 30 * 0.033f;
        public const float ChargeSeconds = 140 * 0.033f;
        public float TravelSeconds = 3f;

        /// <summary>The arrival: the swirl held while the rider fades back in.</summary>
        public float HoldSeconds = 3.5f;
        public const float ReturnSeconds = 60 * 0.033f;
        public const float ExitSeconds = 30 * 0.033f;

        /// <summary>The cooldown ANIM step after a cycle, in sim seconds.</summary>
        public float CooldownSeconds = 3f;

        /// <summary>
        /// The ride gate: sim seconds from one departure to the next ride. 0 for a machine with no
        /// partner (the FXTIME look rig); <see cref="Pair"/> sets <see cref="FiveCycles"/>.
        /// </summary>
        public float RideCooldownSeconds;

        public const float FiveCycles = 5 * 600f;

        /// <summary>GameClock time before which this machine refuses a ride. Saved.</summary>
        [Serialize]
        public float ReadyAt;

        /// <summary>
        /// The machine at the other end, on another world, or null for a ride that returns here.
        /// Not saved: <see cref="Pair"/> finds it again by place after every load.
        /// </summary>
        public TimeMachine Partner;

        /// <summary>
        /// The warp at full travel. Kept under the lens fold (strength ~0.34 with the shader's
        /// defaults, where the Einstein radius passes the core softening): below it the lens
        /// never folds, so there is no shadow disc and no mirrored copy of the machine in its
        /// glass (a black dot in the glass read badly in the ride).
        /// </summary>
        public float PeakStrength = 0.3f;

        public Step Current { get; private set; } = Step.Idle;

        /// <summary>Completed cycles since spawn; the rig counts these.</summary>
        public int Cycles { get; private set; }

        /// <summary>Riders sent to the partner since spawn.</summary>
        public int Hops { get; private set; }

        /// <summary>The dupe in the chair this cycle, or null. Set by TimeMachineSeat.</summary>
        public WorkerBase Rider;

        /// <summary>True while a hop is moving the rider: the seat must not reset them.</summary>
        public bool Hopping { get; private set; }

        /// <summary>The white interior's level, 0..<see cref="WhitePeak"/>, for the rig.</summary>
        public float FlashLevel { get; private set; }

        /// <summary>The rider's opacity this frame, 1 seated and solid, 0 gone; for the rig.</summary>
        public float RiderAlpha { get; private set; } = 1f;

        /// <summary>The dark shade's opacity, 0..1, for the rig.</summary>
        public float ShadeLevel { get; private set; }

        /// <summary>The screen shake asked for this frame, in cells, for the rig.</summary>
        public float ShakeLevel { get; private set; }

        /// <summary>How far this machine's wormhole is open, 0..1 (WormholePass draws it on the active world).</summary>
        public float WormholeLevel { get; private set; }

        /// <summary>The wormhole's white flare as it closes, 0..1.</summary>
        public float WormholeFlare { get; private set; }

        /// <summary>The streaks' clock: this machine's sim seconds.</summary>
        public float WormholeClock => fxClock;

        /// <summary>Bolts fired since spawn, for the rig.</summary>
        public int BoltsFired { get; private set; }

        /// <summary>False when the bolt kanims are missing (they ship with DLC3, Bionic Booster Pack).</summary>
        public bool BoltsAvailable => beamFile != null && impactFile != null;

        /// <summary>The nearby solid cells bolts can hit (exposed, in line of sight), for the rig.</summary>
        public int BoltTargets => targets.Count;

        /// <summary>Bolts that struck a cell soft enough to take damage, and the damage they dealt
        /// (in cell health, 1 = one cell broken); for the rig.</summary>
        public int WallHits { get; private set; }
        public float WallDamage { get; private set; }

        /// <summary>Bolts that struck a cell too hard to damage (hardness VERY FIRM and up, or a bunker).</summary>
        public int ImmuneHits { get; private set; }

        /// <summary>Bolts that struck a dupe, and the hit points they took; for the rig.</summary>
        public int DupeHits { get; private set; }
        public float DupeDamage { get; private set; }

        /// <summary>True while the radiation emitter is on; false without Spaced Out.</summary>
        public bool Radiating { get; private set; }

        /// <summary>False without Spaced Out: no radiation grid, so the machine has no emitter.</summary>
        public bool RadiationAvailable => radiation != null;

        /// <summary>Sim seconds the emitter has been on since spawn, for the rig.</summary>
        public float RadiatedSeconds { get; private set; }

        /// <summary>
        /// True when the overlays sit in the ride's depth order, front to back: bolts, swirl, white,
        /// shade (front is the more negative z). For the rig.
        /// </summary>
        public bool OverlaysOrdered =>
            BoltZ < SwirlZ && SwirlZ < WhiteZ && WhiteZ < ShadeZ
            && Mathf.Approximately(swirl.transform.GetPosition().z, SwirlZ)
            && Mathf.Approximately(flash.transform.GetPosition().z, WhiteZ)
            && Mathf.Approximately(shade.transform.GetPosition().z, ShadeZ);

        private const string Kanim = "fx_time_machine_kanim";
        // The bolts are blue copies of vanilla's stress-shock beam and impact (art/time-machine/bolt.py;
        // vanilla's own stays untouched). They fire only when the vanilla beam itself is loaded,
        // i.e. with DLC3; without DLC3 the bolt effect is simply disabled.
        private const string VanillaBeamKanim = "bionic_dupe_stress_beam_fx_kanim";
        private const string BeamKanim = "fx_time_machine_bolt_kanim";
        private const string ImpactKanim = "fx_time_machine_bolt_impact_kanim";
        private static readonly KAnimHashedString PortalSymbol = "fx_time_machine_portal";
        private static readonly string[] NotSwirl =
        {
            "fx_time_machine_body", "fx_time_machine_lamp", "fx_time_machine_screen", "fx_time_machine_tip",
            "fx_time_machine_flash", "fx_time_machine_shade",
        };

        // The glass's white pulse (owner: "make the white less length"): it rises over the last 0.4 s
        // of the travel, holds through the wormhole's full tunnel, and clears in 0.45 s as the tunnel
        // closes into the glass (0.6.5: it rose at the end of the CHARGE, which since 0.6.4's hop at
        // the end of travel left the white 3 s before the cut).
        private const float WhiteRise = 0.4f, WhiteFull = 0.3f, WhiteClear = 0.45f;

        /// <summary>
        /// The white's top level. At full, and with Bloom, it washed out the swirl drawn over it
        /// (so the Bloom is dropped AND the white is capped at ~70 %).
        /// </summary>
        public const float WhitePeak = 0.7f;

        // THE WORMHOLE (0.6.5: a transition screen in place of the whiteout, ~2.5 s around the
        // cut). The iris opens from the glass over the travel's last second; the full
        // tunnel holds for 1 s after the cut (the hop at the end of travel); then it flares white and
        // closes into the glass in 0.8 s. The departing machine, which the camera leaves within a
        // frame or two of the hop, closes sooner, so a player who stays on its world sees it end
        // inside the 0.99 s exit.
        private const float WormholeOpenSeconds = 1f, WormholeHoldSeconds = 1f, WormholeCloseSeconds = 0.8f;
        private const float DepartHoldSeconds = 0.5f;

        // The arrival: the rider stays faded until the tunnel has closed and the white has cleared,
        // then fades in over 2 s.
        private const float FadeInDelay = WormholeHoldSeconds + WhiteFull + WhiteClear, FadeInSeconds = 2f;
        private const float SwirlFadeSeconds = 1f;

        private const float ShadePeak = 0.55f;

        // The shade art is a 64 px square, 101 px a cell; it is stretched to cover the view.
        private const float ShadeCellsWide = 160f, ShadeCellsHigh = 100f, ShadeArtCells = 64f / 101f;

        private const float BoltLife = 0.2f, BoltReach = 8f;

        // RIDE GAMEPLAY. A bolt hits like a
        // comet's tile damage (Comet.DamageTiles): BoltDamage / element.strength, windows x5,
        // bunkers immune. Cells of hardness VERY FIRM and up (GameUtil.AppendHardnessString's band,
        // >= 50) never take damage. One bolt in ten goes to a dupe near the glass instead, for
        // DupeDamage (2 s of vanilla's stress beam, STRESS.SHOCKER.DAMAGE_RATE 2.5/s), never the
        // rider and never one at or below DupeSafeHp, so a bolt cannot kill.
        private const float BoltDamage = 0.15f, WindowMultiplier = 5f;
        private const byte ImmuneHardness = 50;
        private const float DupeChance = 0.1f, DupeReach = 4f, DupeDamageHp = 5f, DupeSafeHp = 10f;
        // 380, not the design's 600, since the 15 s ride: the emitter is on for
        // 11.12 s instead of 7.08, and 380 x 11.12 / 600 keeps a ride's dose at the source <= 7.1.
        internal const float RadiationRads = 380f;
        internal const short RadiationRadius = 6;
        private const string DamageSource = "Time machine";
        private const int BoltSlots = 2;

        private static float ShadeZ => Grid.GetLayerZ(Grid.SceneLayer.FXFront);
        private static float WhiteZ => Grid.GetLayerZ(Grid.SceneLayer.FXFront2);
        private static float SwirlZ => WhiteZ - 0.01f;
        private static float BoltZ => WhiteZ - 0.02f;

        public bool Ready => GameClock.Instance == null || GameClock.Instance.GetTime() >= ReadyAt;

        public float SecondsUntilReady =>
            GameClock.Instance == null ? 0f : Mathf.Max(0f, ReadyAt - GameClock.Instance.GetTime());

        private float stepTime;
        private float lastClock;
        private bool idleShowsCooldown;
        private bool departing;
        private KBatchedAnimController anim;
        private KBatchedAnimController flash;
        private KBatchedAnimController swirl;
        private KBatchedAnimController shade;
        private WarpEmitter warp;

        private KAnimFile beamFile, impactFile;
        private readonly KBatchedAnimController[] beams = new KBatchedAnimController[BoltSlots];
        private readonly KBatchedAnimController[] impacts = new KBatchedAnimController[BoltSlots];
        private readonly float[] boltEnds = new float[BoltSlots];
        private readonly System.Collections.Generic.List<BoltTarget>[] targetsBySide =
        {
            new System.Collections.Generic.List<BoltTarget>(), new System.Collections.Generic.List<BoltTarget>(),
            new System.Collections.Generic.List<BoltTarget>(), new System.Collections.Generic.List<BoltTarget>(),
        };
        private readonly System.Collections.Generic.List<BoltTarget> targets = new System.Collections.Generic.List<BoltTarget>();
        private RadiationEmitter radiation;

        /// <summary>A cell a bolt can strike, and the point on its face the bolt lands on.</summary>
        private struct BoltTarget
        {
            public int Cell;
            public Vector3 Hit;
        }
        private readonly System.Random boltRandom = new System.Random(7);
        private int nextSide;
        private float fxClock, nextBoltAt;
        private static bool boltsMissingLogged;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            anim = GetComponent<KBatchedAnimController>();
            warp = GetComponent<WarpEmitter>();
            lastClock = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
            // The overlays, each at its own depth (see OverlaysOrdered). The white and the swirl
            // sit on the machine; the shade is centred on the glass and stretched over the view.
            flash = Overlay("TimeMachineWhite", Kanim, transform.GetPosition(), WhiteZ);
            flash.Offset = anim.Offset;
            flash.Play("flash", KAnim.PlayMode.Paused);
            flash.gameObject.SetActive(false);
            swirl = Overlay("TimeMachineSwirl", Kanim, transform.GetPosition(), SwirlZ);
            swirl.Offset = anim.Offset;
            foreach (string symbol in NotSwirl)
            {
                swirl.SetSymbolVisiblity(symbol, false);
            }
            anim.SetSymbolVisiblity(PortalSymbol, false);
            Vector2 centre = warp.WarpCentre;
            shade = Overlay("TimeMachineShade", Kanim, new Vector3(centre.x, centre.y, 0f), ShadeZ);
            shade.animWidth = ShadeCellsWide / ShadeArtCells;
            shade.animHeight = ShadeCellsHigh / ShadeArtCells;
            shade.TintColour = new Color32(0, 0, 0, 0);
            shade.Play("shade", KAnim.PlayMode.Paused);
            shade.gameObject.SetActive(false);
            // The bolts: our blue copies of vanilla's stress-shock beam (StressShockChore.MakeBeam).
            if (Assets.TryGetAnim(VanillaBeamKanim, out _))
            {
                Assets.TryGetAnim(BeamKanim, out beamFile);
                Assets.TryGetAnim(ImpactKanim, out impactFile);
            }
            if (!BoltsAvailable && !boltsMissingLogged)
            {
                boltsMissingLogged = true;
                Debug.Log("[FxSandbox] time machine: no bolts, " + VanillaBeamKanim + " (DLC3) or our " + BeamKanim
                    + " / " + ImpactKanim + " is not loaded");
            }
            // A SimComponent starts active; the emitter is on only through a ride (Enter).
            radiation = GetComponent<RadiationEmitter>();
            radiation?.SetEmitting(false);
            Enter(Step.Idle);
            // Next frame, so that on a load the machine at the other end has spawned too.
            GameScheduler.Instance.ScheduleNextFrame("TimeMachine.Pair", _ => Pair());
        }

        /// <summary>
        /// On a TIME TRAVEL game, links this machine with the one at the same place on the other
        /// world (<see cref="TimeTwin.TwinCellOf"/> works either way round). When the player builds
        /// one on home and the twin has none there, it is built on the twin first, in a cleared
        /// pocket on a solid floor, from the same material (<see cref="BuildAt"/>), and the area
        /// around it is revealed. Nothing happens on any other cluster; a machine there rides back
        /// to itself. Runs on every spawn, so a loaded game pairs again.
        /// </summary>
        internal void Pair()
        {
            if (this == null || Partner != null || !TimeTwin.FindWorlds(out WorldContainer home, out WorldContainer twin))
            {
                return;
            }
            int world = this.GetMyWorldId();
            bool atHome = world == home.id;
            if (!atHome && world != twin.id)
            {
                return;
            }
            int cell = Grid.PosToCell(this);
            int other = atHome ? TimeTwin.TwinCellOf(cell, home, twin) : TimeTwin.TwinCellOf(cell, twin, home);
            if (!Grid.IsValidCell(other))
            {
                return;
            }
            GameObject there = Grid.Objects[other, (int)ObjectLayer.Building];
            TimeMachine partner = there != null ? there.GetComponent<TimeMachine>() : null;
            if (partner == null && atHome)
            {
                partner = BuildAt(other, GetComponent<PrimaryElement>().Element.tag);
                if (partner != null)
                {
                    twin.SetDiscovered(reveal_surface: false);
                    Grid.CellToXY(other, out int x, out int y);
                    GridVisibility.Reveal(x + 1, y + 2, 8, 8f);
                    Debug.Log("[FxSandbox] time machine: built the partner on the twin at cell " + other);
                }
            }
            if (partner == null || partner.Partner != null && partner.Partner != this)
            {
                return;
            }
            Partner = partner;
            partner.Partner = this;
            RideCooldownSeconds = partner.RideCooldownSeconds = FiveCycles;
        }

        /// <summary>
        /// Builds a finished machine at <paramref name="origin"/>: a pocket for the 4x4 machine and a
        /// cell either side, on a solid floor row. On the twin this is rock (the past has no base).
        /// </summary>
        internal static TimeMachine BuildAt(int origin, Tag material)
        {
            Grid.CellToXY(origin, out int ox, out int oy);
            for (int x = ox - 1; x <= ox + 4; x++)
            {
                int floor = Grid.XYToCell(x, oy - 1);
                if (!Grid.Solid[floor])
                {
                    SimMessages.ReplaceElement(floor, SimHashes.Granite, CellEventLogger.Instance.DebugTool, 1000f, 293f);
                }
                for (int y = oy; y <= oy + 4; y++)
                {
                    int c = Grid.XYToCell(x, y);
                    if (Grid.Solid[c] || Grid.Element[c].IsLiquid)
                    {
                        SimMessages.ReplaceElement(c, SimHashes.Oxygen, CellEventLogger.Instance.DebugTool, 1f, 293f);
                    }
                }
            }
            BuildingDef def = Assets.GetBuildingDef(TimeMachineConfig.Id);
            GameObject go = def.Build(origin, Orientation.Neutral, null, new[] { material }, 293f,
                playsound: false, timeBuilt: GameClock.Instance.GetTime());
            return go != null ? go.GetComponent<TimeMachine>() : null;
        }

        protected override void OnCleanUp()
        {
            SetAlpha(Rider, 1f);
            WormholePass.Remove(this);
            base.OnCleanUp();
        }

        /// <summary>
        /// A controller of its own at depth <paramref name="z"/>, child of the machine. Built the
        /// way StressShockChore.MakeBeam builds its beam (inactive, anims swapped, then switched on),
        /// with the position set BEFORE it is switched on: a batch takes its depth when the
        /// controller registers, and a moved controller's batch only follows the move if it batches
        /// alone. Always visible, so the stretched shade is never culled with its centre.
        /// </summary>
        private KBatchedAnimController Overlay(string name, string kanim, Vector3 at, float z)
        {
            return Overlay(name, Assets.GetAnim(kanim), at, z, transform);
        }

        private static KBatchedAnimController Overlay(string name, KAnimFile file, Vector3 at, float z, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            KBatchedAnimController k = go.AddComponent<KBatchedAnimController>();
            k.visibilityType = KAnimControllerBase.VisibilityType.Always;
            // Always-visible controllers step on Time.unscaledDeltaTime (KBatchedAnimUpdater) unless
            // told otherwise; the machine's timeline steps on Time.deltaTime. Under the recorder's
            // lockstep a frame is 1/60 s of game time but ~4x that of real time, so the swirl's
            // 4.62 s charge ran out ~1.2 s in and froze on its last frame (1224 take, 0:08-0:11).
            k.forceUseGameTime = true;
            k.SwapAnims(new[] { file });
            go.transform.SetPosition(new Vector3(at.x, at.y, z));
            go.SetActive(true);
            return k;
        }

        /// <summary>Called by the seat once the rider is sitting in the chair.</summary>
        public void StartCycle()
        {
            if (Current == Step.Idle && Ready)
            {
                Enter(Step.Enter);
            }
        }

        private void Enter(Step step)
        {
            // The exit straight after a partnered travel is the departing end of the wormhole.
            departing = step == Step.Exit && Current == Step.Travel;
            Current = step;
            stepTime = 0f;
            idleShowsCooldown = step == Step.Idle && !Ready;
            bool loop = step == Step.Idle || step == Step.Travel || step == Step.Hold || step == Step.Return
                || step == Step.Cooldown;
            anim.Play(AnimName(step), loop ? KAnim.PlayMode.Loop : KAnim.PlayMode.Once);
            // The swirl overlay plays the same anim, so its portal frames stay in step with the machine.
            // On exit it keeps the full swirl turning and Animate fades it out: "exit" stepped the
            // portal down through its fade levels, which read as a cut.
            bool fadeOut = step == Step.Exit;
            swirl.Play(AnimName(fadeOut ? Step.Travel : step), loop || fadeOut ? KAnim.PlayMode.Loop : KAnim.PlayMode.Once);
            bool radiate = step == Step.Charge || step == Step.Travel || step == Step.Hold;
            if (radiation != null && radiate != Radiating)
            {
                radiation.SetEmitting(radiate);
            }
            Radiating = radiation != null && radiate;
            if (step == Step.Charge)
            {
                FindBoltTargets();
                nextBoltAt = fxClock;
            }
            if (step == Step.Travel)
            {
                Depart();
            }
            if (step == Step.Exit)
            {
                // Return's last frame can stop short of t = 1; the rider sits solid from here.
                SetAlpha(Rider, 1f);
            }
        }

        private string AnimName(Step step)
        {
            switch (step)
            {
                case Step.Enter: return "enter";
                case Step.Charge: return "charge";
                case Step.Travel:
                case Step.Hold:
                // The swirl keeps going while the rider gets up: "return" wound it
                // down to a still glass for 3 s with a seated dupe in it.
                case Step.Return: return "travel";
                case Step.Exit: return "exit";
                case Step.Cooldown: return "cooldown";
                default: return Ready ? "idle" : "cooldown";
            }
        }

        private float Length(Step step)
        {
            switch (step)
            {
                case Step.Enter: return EnterSeconds;
                case Step.Charge: return ChargeSeconds;
                case Step.Travel: return TravelSeconds;
                case Step.Hold: return HoldSeconds;
                case Step.Return: return ReturnSeconds;
                case Step.Exit: return ExitSeconds;
                case Step.Cooldown: return CooldownSeconds;
                default: return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// The warp follows the swirl: up with the charge, held at full through travel, hold and
        /// return (owner: the swirl "more predominant and stay for longer"), down on exit.
        /// </summary>
        private float StrengthFor(Step step, float t)
        {
            switch (step)
            {
                case Step.Charge: return PeakStrength * 0.6f * Mathf.Clamp01(t) * Mathf.Clamp01(t);
                case Step.Travel:
                case Step.Hold:
                case Step.Return: return PeakStrength;
                case Step.Exit: return PeakStrength * 0.7f * (1f - Mathf.Clamp01(t));
                default: return 0f;
            }
        }

        /// <summary>
        /// Entering travel: the cooldown starts on both ends. The departure is the one moment the
        /// cooldown is written; the hop itself waits for the end of travel.
        /// </summary>
        private void Depart()
        {
            if (RideCooldownSeconds > 0f && GameClock.Instance != null)
            {
                ReadyAt = GameClock.Instance.GetTime() + RideCooldownSeconds;
                if (Partner != null)
                {
                    Partner.ReadyAt = ReadyAt;
                }
            }
        }

        /// <summary>
        /// The end of travel. With a partner and a rider, the rider goes there and this end goes dark
        /// (Exit, with no rider left to make solid); alone, the rider arrives back here (Hold).
        /// </summary>
        private void EndTravel()
        {
            if (Partner == null || Rider == null)
            {
                Enter(Partner != null ? Step.Exit : Step.Hold);
                return;
            }
            WorkerBase rider = Rider;
            Rider = null;
            Enter(Step.Exit);
            StartCoroutine(Hop(rider));
        }

        /// <summary>
        /// The world hop, in vanilla's teleporter order (WarpPortal.DelayedWarp): at the end of
        /// the frame, end the ride (chore and work, <see cref="TimeMachineSeat.EndRide"/>: vanilla
        /// stops only the work and leaves its chore current), let the receiving end take them (position and a
        /// compulsory chore, WarpReceiver.ReceiveWarpedDuplicant), and only then move them between
        /// the two worlds' lists with <c>ClusterManager.MigrateMinion</c>. The portal's own state
        /// machine is not used: it is single-use and picks its own target world.
        /// </summary>
        private System.Collections.IEnumerator Hop(WorkerBase rider)
        {
            yield return SequenceUtil.WaitForEndOfFrame;
            if (rider == null || Partner == null)
            {
                yield break;
            }
            int from = rider.GetMyWorldId(), to = Partner.GetMyWorldId();
            // EndRide ends this seat's chore and the work with it; Hopping keeps the seat from making
            // the faded-out rider solid or lifting them out of the chair offset on the way.
            Hopping = true;
            GetComponent<TimeMachineSeat>().EndRide(rider);
            Hopping = false;
            Rider = null;
            Partner.Arrive(rider);
            ClusterManager.Instance.MigrateMinion(rider.GetComponent<MinionIdentity>(), to, from);
            Hops++;
        }

        /// <summary>
        /// The partner's half of a hop: the rider is in this machine's chair, faded out, under the
        /// white pulse, and a compulsory seat chore keeps them there until the swirl ends.
        /// </summary>
        public void Arrive(WorkerBase rider)
        {
            rider.transform.SetPosition(Grid.CellToPos(Grid.PosToCell(this), CellAlignment.Bottom, Grid.SceneLayer.Move));
            Rider = rider;
            SetAlpha(rider, 0f);
            FlashLevel = 1f;
            GetComponent<TimeMachineSeat>().Receive(rider);
            // This end never charged, and targets were only found at the start of a charge, so the
            // arriving machine fired no bolts through its hold (the twin's count was 0).
            FindBoltTargets();
            nextBoltAt = fxClock;
            Enter(Step.Hold);
        }

        private void Update()
        {
            // The frame's scaled time, 0 while paused and faster at speeds 2 and 3, like the kanims.
            // GameClock only moves in Sim33ms ticks, so a timeline read off it stepped every other
            // frame of a 60 fps film (odd frames were exact duplicates).
            float now = GameClock.Instance != null ? GameClock.Instance.GetTime() : lastClock;
            float dt = Time.deltaTime;
            stepTime += dt;
            fxClock += dt;
            lastClock = now;
            if (Radiating)
            {
                RadiatedSeconds += dt;
            }
            float length = Length(Current);
            if (stepTime >= length)
            {
                switch (Current)
                {
                    case Step.Enter: Enter(Step.Charge); break;
                    case Step.Charge: Enter(Step.Travel); break;
                    case Step.Travel: EndTravel(); break;
                    case Step.Hold: Enter(Step.Return); break;
                    case Step.Return: Enter(Step.Exit); break;
                    case Step.Exit: Enter(Step.Cooldown); break;
                    case Step.Cooldown: Cycles++; Enter(Step.Idle); break;
                }
                length = Length(Current);
            }
            if (Current == Step.Idle && idleShowsCooldown == Ready)
            {
                // The cooldown passed, or a partner's departure started one: swap idle's screen.
                Enter(Step.Idle);
            }
            float t = Mathf.Clamp01(stepTime / length);
            float strength = StrengthFor(Current, t);
            warp.Strength = strength;
            warp.Emitting = strength > 0f;
            Animate(t, stepTime);
            WormholePass.Offer(this);
            Shake(t, stepTime, dt > 0f);
            Bolts(t);
        }

        private void LateUpdate()
        {
            WormholePass.Push();
        }

        /// <summary>
        /// The ride's one timeline for the white pulse, the dark shade and the rider's fade, from
        /// the step, its progress <paramref name="t"/> and its seconds <paramref name="s"/>.
        /// </summary>
        private void Animate(float t, float s)
        {
            float level = 0f, alpha = 1f, dark = 0f, open = 0f, flare = 0f;
            switch (Current)
            {
                case Step.Charge:
                    // The swirl rises and the screen darkens as the rider fades from the chair.
                    alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.45f) / 0.55f));
                    dark = ShadePeak * Mathf.SmoothStep(0f, 1f, t);
                    break;
                case Step.Travel:
                    // The wormhole opens over the last second and the glass whitens under it for the hop.
                    level = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - (TravelSeconds - WhiteRise)) / WhiteRise));
                    open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - (TravelSeconds - WormholeOpenSeconds)) / WormholeOpenSeconds));
                    alpha = 0f;
                    dark = ShadePeak;
                    break;
                case Step.Hold:
                    // The arrival: the full tunnel, its flare and close into the white glass, then the
                    // rider fades back in, seated, under the swirl.
                    level = Pulse(s - WormholeHoldSeconds);
                    Closing(s, WormholeHoldSeconds, WormholeCloseSeconds, out open, out flare);
                    alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - FadeInDelay) / FadeInSeconds));
                    dark = ShadePeak;
                    break;
                case Step.Return:
                    dark = ShadePeak * (1f - 0.6f * t);
                    break;
                case Step.Exit:
                    dark = ShadePeak * 0.4f * (1f - t);
                    if (departing)
                    {
                        level = Pulse(s - DepartHoldSeconds);
                        Closing(s, DepartHoldSeconds, ExitSeconds - DepartHoldSeconds, out open, out flare);
                    }
                    break;
            }
            WormholeLevel = open;
            WormholeFlare = flare;
            // Full through the charge and the hop. The arrival fades it once the white has cleared,
            // so it is gone ~2.75 s into the hold (held longer, it lasted too long); the departing
            // end fades over its exit. At tint alpha 0 the portal symbol draws white, not clear
            // (0.6.12: the swirl held at alpha 0 whitened the glass for the whole cooldown), so it
            // is hidden there instead.
            float swirlLevel = Current == Step.Charge || Current == Step.Travel ? 1f
                : Current == Step.Hold ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - FadeInDelay) / SwirlFadeSeconds))
                : Current == Step.Exit && departing ? 1f - Mathf.SmoothStep(0f, 1f, t)
                : 0f;
            byte swirlAlpha = (byte)Mathf.RoundToInt(255f * swirlLevel);
            bool swirlShown = swirlAlpha > 0;
            if (swirl.GetSymbolVisiblity(PortalSymbol) != swirlShown)
            {
                swirl.SetSymbolVisiblity(PortalSymbol, swirlShown);
            }
            if (swirlShown && swirl.TintColour.a != swirlAlpha)
            {
                swirl.TintColour = new Color32(255, 255, 255, swirlAlpha);
            }
            if (Rider != null && (Current == Step.Charge || Current == Step.Travel || Current == Step.Hold || Current == Step.Return))
            {
                SetAlpha(Rider, alpha);
            }
            RiderAlpha = Rider != null ? alpha : 1f;
            level *= WhitePeak;
            FlashLevel = level;
            bool on = level > 0.01f;
            if (flash.gameObject.activeSelf != on)
            {
                flash.gameObject.SetActive(on);
                if (on)
                {
                    flash.Play("flash", KAnim.PlayMode.Paused);
                }
            }
            if (on)
            {
                flash.SetPositionPercent(level);
            }
            ShadeLevel = dark;
            bool shaded = dark > 0.005f;
            if (shade.gameObject.activeSelf != shaded)
            {
                shade.gameObject.SetActive(shaded);
                if (shaded)
                {
                    shade.Play("shade", KAnim.PlayMode.Paused);
                }
            }
            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(dark) * 255f);
            if (shaded && shade.TintColour.a != a)
            {
                shade.TintColour = new Color32(0, 0, 0, a);
            }
        }

        /// <summary>
        /// The wormhole's end, <paramref name="hold"/> s into the step: the iris closes over
        /// <paramref name="close"/> s, and the white flare peaks a fifth of the way in and is gone with it.
        /// </summary>
        private static void Closing(float s, float hold, float close, out float open, out float flare)
        {
            float u = Mathf.Clamp01((s - hold) / close);
            open = 1f - Mathf.SmoothStep(0f, 1f, u);
            flare = s < hold ? 0f : Mathf.SmoothStep(0f, 1f, u / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.2f) / 0.8f));
        }

        /// <summary>The glass's white: full until 0.3 s after <paramref name="s"/> = 0, then clear in 0.45 s.</summary>
        private static float Pulse(float s)
        {
            return s < WhiteFull ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - WhiteFull) / WhiteClear));
        }

        /// <summary>
        /// The screen shake, in cells: building through the charge, a hard jolt for the first 0.4 s
        /// of the hop and of the arrival, a low rumble after, gone by the exit. Only on the world the
        /// player is looking at, and never while the game is paused.
        /// </summary>
        private void Shake(float t, float s, bool running)
        {
            float amp = 0f;
            switch (Current)
            {
                case Step.Charge: amp = 0.15f * t * t; break;
                case Step.Travel:
                case Step.Hold: amp = s < 0.4f ? 0.25f : 0.1f; break;
                case Step.Return: amp = 0.1f * (1f - t); break;
            }
            ShakeLevel = amp;
            if (amp > 0f && running && ClusterManager.Instance != null
                && this.GetMyWorldId() == ClusterManager.Instance.activeWorldId)
            {
                ScreenShake.Request(amp);
            }
        }

        /// <summary>
        /// The bolts' targets, found once per ride at the start of the charge: every solid cell within
        /// 8 cells of the glass that has an open neighbour and that the glass can see, sorted by side
        /// (floor, ceiling, left, right) so the bolts take turns hitting each. The hit point is the
        /// cell's centre moved half a cell toward the glass, onto its face.
        /// </summary>
        private void FindBoltTargets()
        {
            targets.Clear();
            foreach (var side in targetsBySide)
            {
                side.Clear();
            }
            Vector2 centre = warp.WarpCentre;
            int origin = Grid.PosToCell(new Vector3(centre.x, centre.y, 0f));
            if (!Grid.IsValidCell(origin))
            {
                return;
            }
            Grid.CellToXY(origin, out int ox, out int oy);
            int reach = Mathf.CeilToInt(BoltReach);
            for (int y = oy - reach; y <= oy + reach; y++)
            {
                for (int x = ox - reach; x <= ox + reach; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !Grid.Solid[cell] || (x - ox) * (x - ox) + (y - oy) * (y - oy) > BoltReach * BoltReach)
                    {
                        continue;
                    }
                    bool exposed = false;
                    foreach (int n in new[] { Grid.CellAbove(cell), Grid.CellBelow(cell), Grid.CellLeft(cell), Grid.CellRight(cell) })
                    {
                        exposed |= Grid.IsValidCell(n) && !Grid.Solid[n];
                    }
                    if (!exposed || !Grid.TestLineOfSight(ox, oy, x, y, c => Grid.Solid[c], blocking_tile_visible: true))
                    {
                        continue;
                    }
                    Vector3 c3 = Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2);
                    Vector3 toward = new Vector3(centre.x - c3.x, centre.y - c3.y, 0f).normalized;
                    Vector3 hit = c3 + toward * 0.5f;
                    hit.z = BoltZ;
                    int dx = x - ox, dy = y - oy;
                    int sideIndex = Mathf.Abs(dy) >= Mathf.Abs(dx) ? (dy < 0 ? 0 : 1) : (dx < 0 ? 2 : 3);
                    BoltTarget target = new BoltTarget { Cell = cell, Hit = hit };
                    targetsBySide[sideIndex].Add(target);
                    targets.Add(target);
                }
            }
        }

        /// <summary>
        /// Fires and retires bolts on the ride's cadence: every 0.5 s through the charge, every 0.2 s
        /// at the hop and the arrival (at most two at once), every 0.6 s on the return, none on the
        /// exit. Each lives 0.2 s. The beam is aimed the way StressShockChore.AimBeam aims its own.
        /// </summary>
        private void Bolts(float t)
        {
            for (int i = 0; i < BoltSlots; i++)
            {
                if (beams[i] != null && beams[i].gameObject.activeSelf && fxClock >= boltEnds[i])
                {
                    beams[i].gameObject.SetActive(false);
                    impacts[i].gameObject.SetActive(false);
                }
            }
            float cadence;
            switch (Current)
            {
                // The charge's bolts come faster as it builds (0.8 s apart to 0.25 s).
                case Step.Charge: cadence = Mathf.Lerp(0.8f, 0.25f, t); break;
                case Step.Travel:
                case Step.Hold: cadence = 0.3f; break;
                case Step.Return: cadence = 0.6f; break;
                default: return;
            }
            if (!BoltsAvailable || targets.Count == 0 || fxClock < nextBoltAt)
            {
                return;
            }
            int slot = System.Array.FindIndex(beams, k => k == null || !k.gameObject.activeSelf);
            if (slot < 0)
            {
                return;
            }
            nextBoltAt = fxClock + cadence;
            Vector2 centre = warp.WarpCentre;
            // A cell an earlier bolt broke is no longer a target.
            foreach (var list in targetsBySide)
            {
                list.RemoveAll(bt => !Grid.Solid[bt.Cell]);
            }
            targets.RemoveAll(bt => !Grid.Solid[bt.Cell]);
            if (targets.Count == 0)
            {
                return;
            }
            System.Collections.Generic.List<BoltTarget> side = null;
            for (int tries = 0; tries < targetsBySide.Length && (side == null || side.Count == 0); tries++)
            {
                side = targetsBySide[nextSide];
                nextSide = (nextSide + 1) % targetsBySide.Length;
            }
            BoltTarget struck = side[boltRandom.Next(side.Count)];
            Vector3 target = struck.Hit;
            // Draw the dice every bolt, so the wall sequence does not depend on who stands nearby.
            bool toDupe = boltRandom.NextDouble() < DupeChance;
            Health victim = toDupe ? FindDupeTarget(centre) : null;
            if (victim != null)
            {
                target = victim.transform.GetPosition() + new Vector3(0f, 0.75f, 0f);
                target.z = BoltZ;
                victim.Damage(DupeDamageHp);
                DupeHits++;
                Debug.Log(string.Format("[FxSandbox] time machine bolt hit {0} (world {1}, {2})", victim.gameObject.GetProperName(),
                    this.GetMyWorldId(), Current));
                DupeDamage += DupeDamageHp;
            }
            else
            {
                DamageCell(struck.Cell, Grid.PosToCell(new Vector3(centre.x, centre.y, 0f)));
            }
            Vector3 from = new Vector3(centre.x, centre.y, BoltZ);
            if (beams[slot] == null)
            {
                beams[slot] = Overlay("TimeMachineBolt", beamFile, from, BoltZ, transform);
                impacts[slot] = Overlay("TimeMachineBoltImpact", impactFile, target, BoltZ, transform);
            }
            else
            {
                beams[slot].transform.SetPosition(from);
                impacts[slot].transform.SetPosition(target);
                beams[slot].gameObject.SetActive(true);
                impacts[slot].gameObject.SetActive(true);
            }
            KBatchedAnimController beam = beams[slot];
            Vector3 v = Vector3.Normalize(new Vector3(target.x - from.x, target.y - from.y, 0f));
            beam.Rotation = MathUtil.AngleSigned(Vector3.up, v, Vector3.forward) + 90f;
            float dist = Vector2.Distance(new Vector2(from.x, from.y), new Vector2(target.x, target.y));
            string beamAnim = dist > 3f ? "beam3" : dist > 2f ? "beam2" : "beam1";
            beam.Play(beamAnim, KAnim.PlayMode.Loop);
            beam.animWidth = dist > 3f ? dist / 3f : dist > 2f ? dist / 2f : dist;
            impacts[slot].Play("stress_beam_impact_fx", KAnim.PlayMode.Loop);
            boltEnds[slot] = fxClock + BoltLife;
            BoltsFired++;
        }

        /// <summary>
        /// A bolt's tile damage: Comet.DamageTiles's own rule, copied because that method is a
        /// private instance method that reads and writes a live comet (STRUCTURAL). A built tile
        /// (a SimCellOccupier that does not replace the cell's element) takes the BuildingHP damage
        /// event, so it is repairable; a natural cell takes WorldDamage and breaks at 1.0.
        /// Hardness is read from the element that takes the hit (the tile's own for a built one).
        /// </summary>
        private void DamageCell(int cell, int fromCell)
        {
            GameObject tile = Grid.Objects[cell, (int)ObjectLayer.FoundationTile];
            float multiplier = 1f;
            bool built = false;
            if (tile != null)
            {
                KPrefabID id = tile.GetComponent<KPrefabID>();
                if (id.HasTag(GameTags.Window))
                {
                    multiplier = WindowMultiplier;
                }
                else if (id.HasTag(GameTags.Bunker))
                {
                    multiplier = 0f;
                }
                SimCellOccupier occupier = tile.GetComponent<SimCellOccupier>();
                built = occupier != null && !occupier.doReplaceElement;
            }
            Element element = built ? tile.GetComponent<PrimaryElement>().Element : Grid.Element[cell];
            if (multiplier == 0f || element.strength == 0f || element.hardness >= ImmuneHardness || UnderFootprint(cell))
            {
                ImmuneHits++;
                return;
            }
            float damage = BoltDamage * multiplier / element.strength;
            WallHits++;
            WallDamage += damage;
            if (built)
            {
                BuildingHP hp = tile.GetComponent<BuildingHP>();
                tile.BoxingTrigger((int)GameHashes.DoBuildingDamage, new BuildingHP.DamageSourceInfo
                {
                    damage = Mathf.RoundToInt(damage * hp.MaxHitPoints),
                    source = DamageSource,
                    popString = DamageSource,
                });
            }
            else
            {
                WorldDamage.Instance.ApplyDamage(cell, damage, fromCell, DamageSource, DamageSource);
            }
        }

        /// <summary>
        /// True for a cell directly under the machine's footprint: its foundation, and the floor of
        /// the cell the rider works from. Bolts still strike it but never break it; when a bolt
        /// broke it in an FXTIME run, the rider lost his floor mid-ride, the seat's chore ended and
        /// he stood unfaded beside the machine through travel (and, no longer the rider, took a bolt).
        /// </summary>
        private bool UnderFootprint(int cell)
        {
            Building building = GetComponent<Building>();
            return building != null && building.PlacementCellsContainCell(Grid.CellAbove(cell));
        }

        /// <summary>
        /// A dupe a bolt may strike instead of a wall: on this world, within <see cref="DupeReach"/>
        /// cells of the glass and in its line of sight, not the rider, and above
        /// <see cref="DupeSafeHp"/>. The nearest one, or null.
        /// </summary>
        private Health FindDupeTarget(Vector2 centre)
        {
            int origin = Grid.PosToCell(new Vector3(centre.x, centre.y, 0f));
            Grid.CellToXY(origin, out int ox, out int oy);
            int world = this.GetMyWorldId();
            Health best = null;
            float bestDist = float.MaxValue;
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.GetMyWorldId() != world || (Rider != null && dupe.gameObject == Rider.gameObject))
                {
                    continue;
                }
                Health health = dupe.GetComponent<Health>();
                if (health == null || health.IsDefeated() || health.hitPoints <= DupeSafeHp)
                {
                    continue;
                }
                Vector3 p = dupe.transform.GetPosition();
                float dist = Vector2.Distance(centre, new Vector2(p.x, p.y + 0.75f));
                Grid.CellToXY(Grid.PosToCell(p), out int x, out int y);
                if (dist > DupeReach || dist >= bestDist
                    || !Grid.TestLineOfSight(ox, oy, x, y, c => Grid.Solid[c], blocking_tile_visible: false))
                {
                    continue;
                }
                best = health;
                bestDist = dist;
            }
            return best;
        }

        /// <summary>
        /// Fades a dupe through its anim's tint alpha, the way Klei's own build animate-in fades a
        /// building in (KleiPermitBuildingAnimateIn). The tint's colour stays white.
        /// </summary>
        internal static void SetAlpha(WorkerBase rider, float alpha)
        {
            if (rider == null)
            {
                return;
            }
            KBatchedAnimController k = rider.GetComponent<KBatchedAnimController>();
            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
            if (k.TintColour.a != a)
            {
                k.TintColour = new Color32(255, 255, 255, a);
            }
        }
    }

    /// <summary>
    /// World-camera shake. Vanilla has none for the world (UIShake moves UI rects only; meteors do
    /// not shake), so this nudges the world cameras, children of CameraController, by a Perlin
    /// offset in LateUpdate, after the controller has moved its own transform. The UI camera is
    /// left alone. Anything that wants a shake calls <see cref="Request"/> every frame it wants
    /// one; the strongest request of the frame wins and the offset is taken back when none come.
    /// </summary>
    public sealed class ScreenShake : MonoBehaviour
    {
        private static ScreenShake instance;
        private float request;
        private Vector3 applied;

        /// <summary>Shake the world view this frame by up to <paramref name="cells"/> cells.</summary>
        public static void Request(float cells)
        {
            if (CameraController.Instance == null)
            {
                return;
            }
            if (instance == null)
            {
                instance = CameraController.Instance.gameObject.AddOrGet<ScreenShake>();
            }
            instance.request = Mathf.Max(instance.request, cells);
        }

        private void LateUpdate()
        {
            float amp = request;
            request = 0f;
            Vector3 offset = Vector3.zero;
            if (amp > 0f)
            {
                float time = Time.unscaledTime * 18f;
                offset = new Vector3((Mathf.PerlinNoise(time, 0.37f) - 0.5f) * 2f * amp,
                    (Mathf.PerlinNoise(0.71f, time) - 0.5f) * 2f * amp, 0f);
            }
            Vector3 delta = offset - applied;
            if (delta == Vector3.zero)
            {
                return;
            }
            CameraController c = CameraController.Instance;
            foreach (Camera cam in new[] { c.baseCamera, c.overlayCamera, c.overlayNoDepthCamera, c.lightBufferCamera,
                c.simOverlayCamera, c.infraredCamera })
            {
                if (cam != null)
                {
                    cam.transform.localPosition += delta;
                }
            }
            applied = offset;
        }
    }

    /// <summary>
    /// A vanilla seated dupe anim the chair can wear: its kanim and its sit-down, sit and get-up
    /// anims. Names missing from the kanim are found by their _pre/_loop/_pst suffix.
    /// </summary>
    public sealed class SeatPose
    {
        public string Name, Kanim, Pre, Loop, Pst;

        public SeatPose(string name, string kanim, string pre = "working_pre", string loop = "working_loop",
            string pst = "working_pst")
        {
            Name = name;
            Kanim = kanim;
            Pre = pre;
            Loop = loop;
            Pst = pst;
        }
    }

    /// <summary>
    /// The chair. The dupe side wears a vanilla seated anim (<see cref="Pose"/>, one of
    /// <see cref="Poses"/>; FXSEAT shows each in turn for the owner to pick), drawn on
    /// BuildingFront, lifted by <see cref="SeatOffset"/> from the origin cell into the sheet's
    /// chair and facing the way the chair faces.
    ///
    /// A RIDE is a WorkChore any dupe on this world takes: they walk in and sit down, and only
    /// then does the machine's cycle start. A hop ends the work with no get-up anim (the rider
    /// has faded out). An ARRIVAL is vanilla's WarpReceiver pattern: a compulsory chore on the
    /// arriving dupe's own ChoreProvider, already seated (working_loop only). Either work finishes
    /// once the machine is past its exit, i.e. when the swirl has finished; then the dupe plays
    /// working_pst, gets out of the chair and leaves.
    /// </summary>
    public sealed class TimeMachineSeat : Workable
    {
        /// <summary>
        /// The seated dupe's offset from the origin cell's bottom centre, in cells. The sheet's
        /// chair cushion is 1.35 cells above the floor and 0.27 right of the origin cell's centre;
        /// the pilot anim already sits its dupe at a chair height of its own, so this is the
        /// difference. Tuned on the seat screenshots for the beach chair pose (the hips sat right of
        /// the seat and a touch high, so 0.3 left and 0.1 down).
        /// </summary>
        public static Vector2 SeatOffset = new Vector2(-0.05f, 0.75f);

        /// <summary>The sheet's chair faces right (its back is on the left).</summary>
        public static bool FaceLeft = false;

        /// <summary>The candidates (the rocket pilot pose looked like driving).</summary>
        public static readonly SeatPose[] Poses =
        {
            new SeatPose("Sick Bay patient", "anim_interacts_treatment_chair_sick_kanim"),
            new SeatPose("Sauna bench", "anim_interacts_sauna_kanim"),
            new SeatPose("Mess table", "anim_eat_table_kanim"),
            new SeatPose("Beach chair", "anim_interacts_beach_chair_kanim"),
            new SeatPose("Rocket pilot", "anim_interacts_rocket_control_station_kanim"),
        };

        /// <summary>The pose the next ride or arrival uses: the beach chair, picked from the seat rig.</summary>
        public static SeatPose Pose = Poses[3];

        /// <summary>What the last <see cref="Wear"/> resolved, for the rig's log.</summary>
        public string PoseReport { get; private set; } = "";

        private Chore chore;
        private TimeMachine machine;
        private bool arriving;
        private float sitSeconds = 1f;
        private float sitTime;

        /// <summary>True from the request until the ride ends.</summary>
        public bool Requested => chore != null;

        /// <summary>The arrival chore while it waits for its dupe, for the rig's diagnosis.</summary>
        public Chore PendingChore => chore;

        /// <summary>True while a dupe sits in this chair (seated or fading), for the rig.</summary>
        public bool Seated { get; private set; }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            workingPstFailed = null;
            workLayer = Grid.SceneLayer.BuildingFront;
            showProgressBar = false;
            synchronizeAnims = false;
            SetWorkTime(float.PositiveInfinity);
        }

        /// <summary>
        /// Puts <see cref="Pose"/> on the chair: its kanim as the override, its anims as the work
        /// and get-up anims, and the sit-down's own length (read the way WarpReceiver reads its
        /// printing anims). An arrival is already seated, so it skips the sit-down.
        /// </summary>
        private void Wear(bool seated)
        {
            KAnimFile file = Assets.GetAnim(Pose.Kanim);
            overrideAnims = new[] { file };
            KAnimFileData data = file.GetData();
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < data.animCount; i++)
            {
                names.Add(data.GetAnim(i).name);
            }
            string pre = Find(names, Pose.Pre, "_pre"), loop = Find(names, Pose.Loop, "_loop"), pst = Find(names, Pose.Pst, "_pst");
            sitSeconds = 0f;
            for (int i = 0; i < data.animCount; i++)
            {
                if (data.GetAnim(i).name == pre)
                {
                    sitSeconds = data.GetAnim(i).totalTime;
                }
            }
            workAnims = seated || pre == null ? new HashedString[] { loop } : new HashedString[] { pre, loop };
            workingPstComplete = pst != null ? new HashedString[] { pst } : null;
            PoseReport = string.Format("{0} ({1}): pre {2} ({3:F2} s), loop {4}, pst {5}; anims: {6}", Pose.Name, Pose.Kanim,
                pre ?? "none", sitSeconds, loop ?? "none", pst ?? "none", string.Join(", ", names.ToArray()));
        }

        private static string Find(System.Collections.Generic.List<string> names, string wanted, string suffix)
        {
            if (names.Contains(wanted))
            {
                return wanted;
            }
            return names.Find(n => n.EndsWith(suffix));
        }

        private static readonly EventSystem.IntraObjectHandler<TimeMachineSeat> OnRefreshUserMenuDelegate =
            new EventSystem.IntraObjectHandler<TimeMachineSeat>((seat, data) => seat.OnRefreshUserMenu());

        protected override void OnSpawn()
        {
            base.OnSpawn();
            machine = GetComponent<TimeMachine>();
            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenuDelegate);
        }

        /// <summary>
        /// The player's way in: Time Travel asks for a rider, as <see cref="RequestRide"/> does;
        /// while that rider is still on the way it cancels the ride. Greyed out, with the reason,
        /// while the machine is busy or cooling down.
        /// </summary>
        private void OnRefreshUserMenu()
        {
            if (chore != null && !arriving && machine.Rider == null)
            {
                Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo("action_cancel",
                    "Cancel Time Travel", CancelRide, tooltipText: "The duplicant on the way to the seat stays home."));
                return;
            }
            string tip = machine.Partner != null
                ? "A duplicant takes the seat and rides to the partner machine on the other world."
                : "A duplicant takes the seat and rides. With no partner machine on another world, they arrive back here.";
            bool ready = chore == null && machine.Current == TimeMachine.Step.Idle && machine.Ready;
            if (!machine.Ready)
            {
                tip = string.Format("Recharging: ready in {0:F1} cycles.", machine.SecondsUntilReady / 600f);
            }
            else if (!ready)
            {
                tip = "A ride is under way.";
            }
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo("action_direction_both",
                "Time Travel", RequestRide, tooltipText: tip, is_interactable: ready));
        }

        private void CancelRide()
        {
            chore?.Cancel("time travel cancelled");
            chore = null;
            Game.Instance.userMenu.Refresh(gameObject);
        }

        /// <summary>
        /// Ask for a rider: any dupe on this machine's world takes the chore, walks in and rides.
        /// Refused while the machine is busy or cooling down.
        /// </summary>
        public void RequestRide()
        {
            if (chore != null || machine.Current != TimeMachine.Step.Idle || !machine.Ready)
            {
                return;
            }
            arriving = false;
            Wear(seated: false);
            chore = new WorkChore<TimeMachineSeat>(Db.Get().ChoreTypes.Migrate, this, only_when_operational: false,
                on_complete: _ => chore = null, on_end: _ => chore = null,
                priority_class: PriorityScreen.PriorityClass.high);
            Game.Instance.userMenu.Refresh(gameObject);
        }

        /// <summary>
        /// The hop's end of a ride: cancels the ride chore, which stops the work on its way out
        /// (ToggleWork's exit). Stopping only the work (WorkerBase.StopWork, WarpPortal.DelayedWarp's
        /// call) leaves the chore current with nothing to work on, and the arrival can never take
        /// over from it: both are Migrate chores, and ChoreConsumer.ChooseChore replaces a current
        /// chore only with a higher interrupt priority (seen at both ends of a TTPREHIST ride).
        /// </summary>
        public void EndRide(WorkerBase rider)
        {
            chore?.Cancel("time machine hop");
            if (rider.GetWorkable() == this)
            {
                rider.StopWork();
            }
        }

        /// <summary>The arrival: this dupe, and only this dupe, sits here until the swirl ends.</summary>
        public void Receive(WorkerBase dupe)
        {
            chore?.Cancel("time machine arrival");
            arriving = true;
            Wear(seated: true);
            Sit(dupe, 1f);
            chore = new WorkChore<TimeMachineSeat>(Db.Get().ChoreTypes.Migrate, this, dupe.GetComponent<ChoreProvider>(),
                run_until_complete: true, on_complete: _ => chore = null, on_end: _ => chore = null,
                allow_in_red_alert: true, ignore_schedule_block: true, only_when_operational: false,
                is_preemptable: false, allow_prioritization: false,
                priority_class: PriorityScreen.PriorityClass.compulsory);
        }

        protected override void OnStartWork(WorkerBase worker)
        {
            base.OnStartWork(worker);
            machine.Rider = worker;
            Seated = true;
            sitTime = 0f;
            worker.GetComponent<Facing>().SetFacing(FaceLeft);
            Sit(worker, arriving ? 1f : 0f);
        }

        protected override bool OnWorkTick(WorkerBase worker, float dt)
        {
            if (!arriving && machine.Current == TimeMachine.Step.Idle && machine.Rider == worker)
            {
                // Climbing in: lifted into the chair over the sit-down anim; the cycle starts once seated.
                sitTime += dt;
                float t = sitSeconds > 0f ? Mathf.Clamp01(sitTime / sitSeconds) : 1f;
                Sit(worker, t);
                if (t >= 1f)
                {
                    machine.StartCycle();
                }
                return false;
            }
            // Done once the hold is over: the get-up anim plays and the dupe walks off under the
            // swirl, which loops through Return (no still, seated wait after it).
            return machine.Current == TimeMachine.Step.Return || machine.Current == TimeMachine.Step.Exit
                || machine.Current == TimeMachine.Step.Cooldown || machine.Current == TimeMachine.Step.Idle;
        }

        protected override void OnStopWork(WorkerBase worker)
        {
            Seated = false;
            if (!machine.Hopping)
            {
                // Never leave a dupe faded or floating, whatever ended the work.
                TimeMachine.SetAlpha(worker, 1f);
                Sit(worker, 0f);
            }
            machine.Rider = null;
            arriving = false;
            base.OnStopWork(worker);
        }

        /// <summary>Lifts the dupe's anim from the floor (0) into the chair (1).</summary>
        internal static void Sit(WorkerBase dupe, float t)
        {
            KBatchedAnimController k = dupe.GetComponent<KBatchedAnimController>();
            Vector3 offset = new Vector3(SeatOffset.x * t, SeatOffset.y * t, 0f);
            // The Offset setter re-registers the batch, so it is written only when it moves.
            if ((k.Offset - offset).sqrMagnitude >= 1e-5f)
            {
                k.Offset = offset;
            }
        }
    }
}
