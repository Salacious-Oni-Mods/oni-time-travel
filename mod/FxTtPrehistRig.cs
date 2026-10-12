using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Klei;
using OniFramework;
using ProcGen;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FxSandbox
{
    /// <summary>
    /// TTPREHIST (--fx-ttprehist): the time-travel proof of concept as a narrated showcase, on a
    /// REAL new game made by the framework's autopilot from a fixed seed (allowed for this arm only;
    /// same-seed topology cannot be shown on a canvas):
    /// <c>--framework-newgame FxTimeTravel --framework-newgame-seed &lt;n&gt; --fx-ttprehist --mod1-record</c>.
    ///
    /// The checks, in run order (the proof-of-concept scope's acceptance list, with the shape gate):
    /// <list type="bullet">
    /// <item>T1 both worlds exist; the twin has prehistoric zones and DLC4 life; home has neither.</item>
    /// <item>T2 overworld sites: at least 90% of home's sites have a twin site within 2 cells.</item>
    /// <item>T3 cell shape, the acceptance gate: the solid/non-solid mask agrees on at least 97% of
    /// the world (border excluded), liquids overlap at least 80% (Jaccard), and both figures are
    /// logged; screenshots of the same region on both worlds.</item>
    /// <item>T4 the ride: the dupe shrinks under the flash, hops (now on the twin), arrives alive,
    /// and both machines hold the same 5-cycle ReadyAt; a second ride is refused.</item>
    /// <item>T5 save and reload: ReadyAt comes back unchanged and the dupe is still on the twin.</item>
    /// <item>T6 the time-lapse cut (GameClock.SetTime to 20 s before ReadyAt, Klei's own clock
    /// advance), still refused, then ready on the real clock, and the reversed ride home.</item>
    /// </list>
    /// </summary>
    public sealed class FxTtPrehistRig : RigHarness
    {
        protected override string ModTag => "FxSandbox";

        protected override string RigTag => "TTPREHIST";

        /// <summary>A real colony, made by the autopilot rather than loaded from a save (O4).</summary>
        protected override bool UsesColonySave => true;

        private const string SaveName = "ttprehist.sav";
        private const int Border = 3;
        private const float ShapeSolidGate = 0.97f, ShapeLiquidGate = 0.80f, SiteGate = 0.90f;
        private const float LapseLeadSeconds = 20f;

        private static readonly string[] PrehistoricLife =
        {
            "Raptor", "Stego", "Chameleon", "PrehistoricPacu", "Mosquito", "Dinofern", "DewDripperPlant",
            "FlyTrapPlant", "KelpPlant", "GardenFoodPlant", "GardenForagePlant", "GardenForagePlantPlanted",
            "GardenDecorPlant", "ButterflyPlant", "VineMother",
        };

        private static readonly HashSet<SubWorld.ZoneType> PrehistoricZones = new HashSet<SubWorld.ZoneType>
        {
            SubWorld.ZoneType.PrehistoricGarden, SubWorld.ZoneType.PrehistoricRaptor, SubWorld.ZoneType.PrehistoricWetlands,
        };

        /// <summary>Game frames per second of a recorded take (lockstep, below).</summary>
        private const int RecordFps = 60;

        protected override IEnumerator Run()
        {
            if (RigRegistry.AnyFlag("--mod1-record"))
            {
                // Lockstep, as Backrooms' recorded bench take: every frame advances 1/RecordFps s of
                // game time, and the real rate is capped under RecordFps so the recorder's real-time
                // schedule (--mod1-record-fps 60) takes every frame. The film is then a uniform 60 fps
                // of game time however slow the capture makes the real frame. Unity statics, so they
                // hold through the T5 reload; the rig's real-clock caps are safeguards with slack.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = RecordFps - 10;
                Time.captureFramerate = RecordFps;
                Log("TTPREHIST: lockstep at " + RecordFps + " fps of game time, real rate capped at " + (RecordFps - 10));
            }
            DemoNarrator.Begin("Time travel: the prehistoric route",
                "FxSandbox proof of concept. The same asteroid, long ago, from the same seed.");
            DemoNarrator.SetPhaseCount(6);
            // From the first frame: the side-by-side of the two worlds is the point of the film.
            DemoNarrator.StartRecording();

            // ------------------------------------------------------------ T1 the worlds
            DemoNarrator.Phase("Two worlds, one seed", "Terra now, and Terra long ago, generated together.");
            if (!Require(TimeTwin.FindWorlds(out WorldContainer home, out WorldContainer twin),
                "the game holds both FxTimeTerra and FxTimeTwinTerra")) yield break;
            Log("worldgen report:\n" + TimeTwin.Report);
            foreach (KeyValuePair<string, double> kv in TimeTwin.WorldgenSeconds)
            {
                Log(string.Format("worldgen {0}: {1:F1} s (GenerateOffline + RenderOffline)", kv.Key, kv.Value));
            }
            Check(TimeTwin.Report.ToString().Contains("twin seeds"), "the twin was generated from home's seeds");
            Check(TimeTwin.CellsConverted > 0, string.Format("the twin's biomes were converted ({0} of {1} cells in them, {2:P1})",
                TimeTwin.CellsConverted, TimeTwin.CellsInConvertedZones,
                TimeTwin.CellsInConvertedZones > 0 ? (float)TimeTwin.CellsConverted / TimeTwin.CellsInConvertedZones : 0f));
            int twinZones = CountZones(twin, PrehistoricZones), homeZones = CountZones(home, PrehistoricZones);
            Check(twinZones > 0 && homeZones == 0, "prehistoric zones on the twin only (twin " + twinZones
                + " overworld cells, home " + homeZones + ")");
            // Owner ruling "Every biome + the start": every twin zone but the core, the crust and space.
            int twinBiomes = CountZones(twin, null);
            Check(twinZones == twinBiomes, "every biome of the twin is prehistoric, the start included ("
                + twinZones + " of " + twinBiomes + " overworld cells; core, surface and space kept)");
            DemoNarrator.Stat("Prehistoric zones on the twin", twinZones + " / " + twinBiomes);

            // ------------------------------------------------------------ T2 + T3 the shape
            DemoNarrator.Phase("Same shape", "Where the dinosaur is, is where your bed will be.");
            float sites = SiteAgreement(home, twin);
            Check(sites >= SiteGate, string.Format("overworld sites: {0:P1} of home's have a twin site within 2 cells (gate {1:P0})",
                sites, SiteGate));
            ShapeAgreement(home, twin, out float solid, out float liquid, out float convertedSolid, out int cells);
            Log(string.Format("SHAPE solid {0:P2}, liquid {1:P2} (Jaccard), converted cells solid {2:P2}, over {3} cells",
                solid, liquid, convertedSolid, cells));
            DemoNarrator.Stat("Solid shape agreement", solid.ToString("P1"));
            DemoNarrator.Stat("Liquid agreement", liquid.ToString("P1"));
            Check(solid >= ShapeSolidGate, string.Format("the twin's solid shape matches home on {0:P2} of cells (gate {1:P0})",
                solid, ShapeSolidGate));
            Check(liquid >= ShapeLiquidGate, string.Format("the twin's liquids overlap home's {0:P2} (gate {1:P0})",
                liquid, ShapeLiquidGate));

            Telepad pod = Components.Telepads.Count > 0 ? Components.Telepads.Items[0] : null;
            if (!Require(pod != null && pod.GetMyWorldId() == home.id, "home has the printing pod")) yield break;
            int homeOrigin = Grid.OffsetCell(Grid.PosToCell(pod), 4, 0);
            int twinOrigin = TimeTwin.TwinCellOf(homeOrigin, home, twin);
            twin.SetDiscovered(reveal_surface: false);
            yield return ShowBoth(home, twin, homeOrigin, twinOrigin, 22, "base");
            yield return ShowWholeWorlds(home, twin);
            // Worldgen critters and plants spawn on reveal (WorldGenSpawner), so count them only now.
            yield return Checkpoint(2f);
            int twinLife = CountLife(twin.id), homeLife = CountLife(home.id);
            Check(twinLife > 0 && homeLife == 0, "DLC4 critters and plants live on the twin only (twin "
                + twinLife + ", home " + homeLife + ")");
            DemoNarrator.Stat("Prehistoric life on the twin", twinLife.ToString());

            // ------------------------------------------------------------ the machines
            DemoNarrator.Phase("The machine", "A pod by the printing pod, and its echo in the past.");
            // The player's path: only the home machine is placed; it builds its own partner on the
            // twin at the same place and links the pair (TimeMachine.Pair, the frame after it spawns).
            TimeMachine homeMachine = Place(homeOrigin, "home");
            if (!Require(homeMachine != null, "the home time machine was built")) yield break;
            float pairCap = Time.unscaledTime + 10f;
            while (homeMachine.Partner == null && Time.unscaledTime < pairCap)
            {
                yield return null;
            }
            TimeMachine twinMachine = homeMachine.Partner;
            if (!Require(twinMachine != null && twinMachine.Partner == homeMachine
                && twinMachine.GetMyWorldId() == twin.id && Grid.PosToCell(twinMachine) == twinOrigin,
                "the home machine built its partner on the twin at the same place and linked it")) yield break;
            Check(homeMachine.RideCooldownSeconds == TimeMachine.FiveCycles && twinMachine.RideCooldownSeconds == TimeMachine.FiveCycles,
                "the linked pair has the 5-cycle cooldown");
            RevealTestAreaAround(twinOrigin, 8);
            ClusterManager.Instance.SetActiveWorld(home.id);
            DemoNarrator.FocusCameraToFit(Around(homeOrigin, 7), 2f);
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }
            yield return Checkpoint(3f);

            // ------------------------------------------------------------ T4 the ride
            DemoNarrator.Phase("The ride", "The dupe sits in the chair, then fades out as the glass turns white.");
            homeMachine.GetComponent<TimeMachineSeat>().RequestRide();
            Check(homeMachine.GetComponent<TimeMachineSeat>().Requested, "the ride chore was created");
            WorkerBase rider = null;
            float minAlpha = 1f, maxFlash = 0f;
            bool seatedAtStart = false, seatShot = false;
            TimeMachineSeat homeSeat = homeMachine.GetComponent<TimeMachineSeat>();
            float cap = Time.unscaledTime + 120f;
            int pushedBefore = WormholePass.FramesPushed;
            while (homeMachine.Hops == 0 && Time.unscaledTime < cap)
            {
                rider = homeMachine.Rider ?? rider;
                if (homeMachine.Current == TimeMachine.Step.Enter && !seatedAtStart)
                {
                    seatedAtStart = homeSeat.Seated;
                }
                if (homeMachine.Current == TimeMachine.Step.Enter && !seatShot)
                {
                    seatShot = true;
                    yield return Capture("TtPrehist-seated.png", 0f);
                }
                if (homeMachine.Current == TimeMachine.Step.Charge)
                {
                    minAlpha = Mathf.Min(minAlpha, homeMachine.RiderAlpha);
                }
                // Since 0.6.5 the glass whitens at the END of travel, for the hop.
                maxFlash = Mathf.Max(maxFlash, homeMachine.FlashLevel);
                yield return null;
            }
            if (!Require(homeMachine.Hops == 1 && rider != null, "the dupe rode and hopped within 120 s (machine "
                + homeMachine.Current + ")")) yield break;
            Check(seatedAtStart, "the dupe was sitting in the chair before the machine started");
            Check(minAlpha < 0.1f && maxFlash > 0.9f * TimeMachine.WhitePeak, string.Format("the dupe faded out of the chair (to {0:F2}) as the glass turned white (up to {1:F2})",
                minAlpha, maxFlash));
            string riderName = rider.GetProperName();
            ClusterManager.Instance.SetActiveWorld(twin.id);
            DemoNarrator.FocusCameraToFit(Around(twinOrigin, 7), 2f);
            CheckWormholeAtCut(homeMachine, twinMachine, "past", pushedBefore);
            yield return new WaitForEndOfFrame();
            CheckWormholeDrawnAfterCut(twinMachine, "past");
            yield return Capture("TtPrehist-arrival.png", 0.3f);
            yield return WatchArrival(twinMachine, rider, "twin");
            CheckWormholeClosed(twinMachine, "past");
            Check(rider.GetMyWorldId() == twin.id, "the dupe is on the prehistoric twin (world " + rider.GetMyWorldId() + ")");
            Check(Alive(rider.gameObject), riderName + " arrived alive");
            RideGameplay(homeMachine, twinMachine);
            float now = GameClock.Instance.GetTime();
            float readyAt = homeMachine.ReadyAt;
            Check(Mathf.Abs(readyAt - twinMachine.ReadyAt) < 0.01f && Mathf.Abs(readyAt - now - TimeMachine.FiveCycles) < 30f,
                string.Format("both machines hold one cooldown, {0:F0} s from now (5 cycles = {1:F0} s)",
                    readyAt - now, TimeMachine.FiveCycles));
            twinMachine.GetComponent<TimeMachineSeat>().RequestRide();
            Check(!twinMachine.GetComponent<TimeMachineSeat>().Requested, "a ride back is refused while the cooldown runs");
            DemoNarrator.Note(riderName + " is in the past. The machine needs 5 cycles before it can bring them back.");
            yield return Checkpoint(4f);

            // ------------------------------------------------------------ T5 save and reload
            DemoNarrator.Phase("Save and reload", "The cooldown is saved with the game; reloading does not reset it.");
            CameraController before = CameraController.Instance;
            string path = SaveLoader.Instance.Save(RigSaves.PathFor(SaveName));
            Log("saved " + path + "; loading it back");
            LoadScreen.DoLoad(path);
            float deadline = Time.unscaledTime + 180f;
            while (!NewWorldIsUp(before))
            {
                if (Time.unscaledTime > deadline)
                {
                    Fail("the reload did not finish within 180 s");
                    yield break;
                }
                yield return null;
            }
            yield return Checkpoint(3f);
            // The reload builds a new scene with its own HUD; presentation mode hid only the first
            // (an earlier TTPREHIST take filmed the tutorial list and resources from here on).
            DemoRig.HideVanillaHud();
            DemoRig.PushNotificationsDown(NotificationPushdownPixels);
            Log("presentation mode re-applied after the reload");
            if (!Require(TimeTwin.FindWorlds(out home, out twin), "both worlds are back after the reload")) yield break;
            homeMachine = MachineOn(home.id);
            twinMachine = MachineOn(twin.id);
            if (!Require(homeMachine != null && twinMachine != null, "both machines are back after the reload")) yield break;
            // Partner is not saved; each machine finds the other again by place the frame after it spawns.
            if (!Require(homeMachine.Partner == twinMachine && twinMachine.Partner == homeMachine,
                "the machines linked again by themselves after the reload")) yield break;
            Check(Mathf.Abs(homeMachine.ReadyAt - readyAt) < 0.01f && Mathf.Abs(twinMachine.ReadyAt - readyAt) < 0.01f,
                string.Format("ReadyAt survived the reload ({0:F1} / {1:F1}, saved {2:F1})",
                    homeMachine.ReadyAt, twinMachine.ReadyAt, readyAt));
            MinionIdentity back = Components.LiveMinionIdentities.Items.FirstOrDefault(m => m.GetProperName() == riderName);
            if (!Require(back != null && back.GetMyWorldId() == twin.id, riderName + " is still on the twin after the reload")) yield break;
            rider = back.GetComponent<WorkerBase>();
            if (SaveGame.Instance != null)
            {
                // The time-lapse jumps 5 cycles; one autosave in the middle of the film is not wanted.
                SaveGame.Instance.AutoSaveCycleInterval = 0;
            }
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }

            // ------------------------------------------------------------ T6 time-lapse and return
            float left = twinMachine.SecondsUntilReady;
            DemoNarrator.Phase("Five cycles later", string.Format("Time-lapse: {0:F1} cycles of cooldown remain.", left / 600f));
            ClusterManager.Instance.SetActiveWorld(twin.id);
            DemoNarrator.FocusCameraToFit(Around(twinOrigin, 7), 2f);
            yield return Checkpoint(3f);
            GameClock.Instance.SetTime(readyAt - LapseLeadSeconds);
            Check(!twinMachine.Ready, string.Format("after the cut the machine is still cooling ({0:F1} s left)",
                twinMachine.SecondsUntilReady));
            cap = Time.unscaledTime + 120f;
            while (!twinMachine.Ready && Time.unscaledTime < cap)
            {
                yield return null;
            }
            if (!Require(twinMachine.Ready, "the cooldown ended on the sim clock")) yield break;
            DemoNarrator.Phase("Home", "The ride back: fade out in the past, fade in at home.");
            twinMachine.GetComponent<TimeMachineSeat>().RequestRide();
            Check(twinMachine.GetComponent<TimeMachineSeat>().Requested, "the ride home is accepted once the cooldown is over");
            cap = Time.unscaledTime + 120f;
            pushedBefore = WormholePass.FramesPushed;
            while (twinMachine.Hops == 0 && Time.unscaledTime < cap)
            {
                yield return null;
            }
            if (!Require(twinMachine.Hops == 1, "the dupe rode home within 120 s (machine " + twinMachine.Current + ")")) yield break;
            ClusterManager.Instance.SetActiveWorld(home.id);
            DemoNarrator.FocusCameraToFit(Around(homeOrigin, 7), 2f);
            CheckWormholeAtCut(twinMachine, homeMachine, "home", pushedBefore);
            yield return new WaitForEndOfFrame();
            CheckWormholeDrawnAfterCut(homeMachine, "home");
            yield return Capture("TtPrehist-home.png", 0.3f);
            yield return WatchArrival(homeMachine, rider, "home");
            CheckWormholeClosed(homeMachine, "home");
            Check(rider.GetMyWorldId() == home.id, riderName + " is home (world " + rider.GetMyWorldId() + ")");
            Check(Alive(rider.gameObject), riderName + " came home alive");
            KBatchedAnimController k = rider.GetComponent<KBatchedAnimController>();
            Check(k.IsVisible() && k.TintColour.a == 255 && k.Offset.sqrMagnitude < 1e-4f,
                "the dupe is out of the chair, solid and on the floor");
            Check(!homeMachine.Ready, "the machine is cooling again after the ride home");
            DemoNarrator.End("Time travel is a world hop: the same seed, a prehistoric past, and a cooldown the save keeps.");
            yield return Checkpoint(4f);
        }

        // ---------------------------------------------------------------- measurements

        /// <summary>Overworld cells of <paramref name="w"/> in <paramref name="zones"/>; null = every zone
        /// the twin conversion covers (all but <see cref="TimeTwin.KeptZones"/>).</summary>
        private static int CountZones(WorldContainer w, HashSet<SubWorld.ZoneType> zones)
        {
            int n = 0;
            foreach (WorldDetailSave.OverworldCell oc in SaveLoader.Instance.clusterDetailSave.overworldCells)
            {
                bool counted = zones != null ? zones.Contains(oc.zoneType) : !TimeTwin.KeptZones.Contains(oc.zoneType);
                if (counted && Contains(w, oc.poly.Centroid()))
                {
                    n++;
                }
            }
            return n;
        }

        private static int CountLife(int worldId)
        {
            HashSet<Tag> life = new HashSet<Tag>(PrehistoricLife.Select(s => new Tag(s)));
            int n = 0;
            foreach (KPrefabID id in Object.FindObjectsOfType<KPrefabID>())
            {
                if (life.Contains(id.PrefabTag) && id.gameObject.GetMyWorldId() == worldId)
                {
                    n++;
                }
            }
            return n;
        }

        private static bool Contains(WorldContainer w, Vector2 p) =>
            p.x >= w.WorldOffset.x && p.x < w.WorldOffset.x + w.WorldSize.x
            && p.y >= w.WorldOffset.y && p.y < w.WorldOffset.y + w.WorldSize.y;

        /// <summary>T2: the fraction of home's overworld sites with a twin site within 2 cells, offset applied.</summary>
        private static float SiteAgreement(WorldContainer home, WorldContainer twin)
        {
            List<Vector2> h = new List<Vector2>(), t = new List<Vector2>();
            Vector2 delta = new Vector2(twin.WorldOffset.x - home.WorldOffset.x, twin.WorldOffset.y - home.WorldOffset.y);
            foreach (WorldDetailSave.OverworldCell oc in SaveLoader.Instance.clusterDetailSave.overworldCells)
            {
                Vector2 c = oc.poly.Centroid();
                if (Contains(home, c))
                {
                    h.Add(c + delta);
                }
                else if (Contains(twin, c))
                {
                    t.Add(c);
                }
            }
            if (h.Count == 0)
            {
                return 0f;
            }
            int hit = h.Count(p => t.Any(q => (q - p).sqrMagnitude <= 4f));
            Debug.Log(string.Format("[FxSandbox] TTPREHIST sites: home {0}, twin {1}, matched {2}", h.Count, t.Count, hit));
            return (float)hit / h.Count;
        }

        /// <summary>
        /// T3: home and twin compared cell for cell, the border band excluded. Solid: the fraction
        /// of cells whose solid/non-solid state agrees. Liquid: cells liquid on both over cells
        /// liquid on either. Converted: the solid agreement over the cells whose element differs
        /// between the two worlds (the conversion's cells, plus whatever the settle moved), which
        /// is what the conversion itself costs in shape.
        /// </summary>
        private static void ShapeAgreement(WorldContainer home, WorldContainer twin, out float solid, out float liquid,
            out float convertedSolid, out int total)
        {
            int agree = 0, liqBoth = 0, liqEither = 0, conv = 0, convAgree = 0;
            total = 0;
            for (int y = Border; y < home.WorldSize.y - Border; y++)
            {
                for (int x = Border; x < home.WorldSize.x - Border; x++)
                {
                    int a = Grid.XYToCell(home.WorldOffset.x + x, home.WorldOffset.y + y);
                    int b = Grid.XYToCell(twin.WorldOffset.x + x, twin.WorldOffset.y + y);
                    Element ea = Grid.Element[a], eb = Grid.Element[b];
                    if (ea.id == SimHashes.Unobtanium || eb.id == SimHashes.Unobtanium)
                    {
                        continue;
                    }
                    total++;
                    bool same = Grid.Solid[a] == Grid.Solid[b];
                    agree += same ? 1 : 0;
                    if (ea.IsLiquid || eb.IsLiquid)
                    {
                        liqEither++;
                        liqBoth += ea.IsLiquid && eb.IsLiquid ? 1 : 0;
                    }
                    if (ea.id != eb.id)
                    {
                        conv++;
                        convAgree += same ? 1 : 0;
                    }
                }
            }
            solid = total > 0 ? (float)agree / total : 0f;
            liquid = liqEither > 0 ? (float)liqBoth / liqEither : 1f;
            convertedSolid = conv > 0 ? (float)convAgree / conv : 1f;
        }

        // ---------------------------------------------------------------- staging

        private TimeMachine Place(int origin, string label)
        {
            // On home the pocket is the base room, mostly air already.
            TimeMachine machine = TimeMachine.BuildAt(origin, SimHashes.Steel.CreateTag());
            Log("placed the " + label + " machine at cell " + origin + (machine != null ? "" : " -- FAILED"));
            return machine;
        }

        private static TimeMachine MachineOn(int worldId) =>
            Object.FindObjectsOfType<TimeMachine>().FirstOrDefault(m => m.GetMyWorldId() == worldId);

        private static bool Alive(GameObject go) =>
            go != null && !go.HasTag(GameTags.Dead) && !go.HasTag(GameTags.Dying);

        private static bool NewWorldIsUp(CameraController before)
        {
            CameraController now = CameraController.Instance;
            return now != null && !ReferenceEquals(now, before) && Game.Instance != null && Grid.CellCount > 0;
        }

        private static IEnumerable<int> Around(int cell, int r)
        {
            Grid.CellToXY(cell, out int cx, out int cy);
            List<int> cells = new List<int>();
            for (int y = cy - r / 2; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    int c = Grid.XYToCell(x, y);
                    if (Grid.IsValidCell(c))
                    {
                        cells.Add(c);
                    }
                }
            }
            return cells;
        }

        /// <summary>The same region on both worlds, one screenshot each, for the owner's comparison.</summary>
        private IEnumerator ShowBoth(WorldContainer home, WorldContainer twin, int homeCell, int twinCell, int r, string what)
        {
            RevealTestAreaAround(homeCell, r + 4);
            RevealTestAreaAround(twinCell, r + 4);
            ClusterManager.Instance.SetActiveWorld(home.id);
            DemoNarrator.FocusCameraToFit(Around(homeCell, r), 1f);
            DemoNarrator.Note("Now: home, around the printing pod.");
            yield return Capture("TtPrehist-" + what + "-home.png", 1.5f);
            ClusterManager.Instance.SetActiveWorld(twin.id);
            DemoNarrator.FocusCameraToFit(Around(twinCell, r), 1f);
            DemoNarrator.Note("Then: the same place on the prehistoric twin.");
            yield return Capture("TtPrehist-" + what + "-twin.png", 1.5f);
        }

        /// <summary>Each world whole, revealed, for the side-by-side in the record.</summary>
        /// <summary>
        /// An arrival: the rider stays in the chair while the machine returns and winds its swirl
        /// down, gets up only once the swirl has finished, and the seat is free again afterwards.
        /// </summary>
        private IEnumerator WatchArrival(TimeMachine machine, WorkerBase rider, string where)
        {
            TimeMachineSeat seat = machine.GetComponent<TimeMachineSeat>();
            bool leftEarly = false, sawSeated = false, diagnosed = false;
            float start = Time.unscaledTime;
            float cap = start + 20f;
            while (machine.Current != TimeMachine.Step.Idle && Time.unscaledTime < cap)
            {
                if (!sawSeated && !diagnosed && Time.unscaledTime > start + 1f)
                {
                    diagnosed = true;
                    Log("arrival not seated after 1 s: " + WhyNot(rider, seat.PendingChore));
                }
                // The rider stays seated through the hold and gets up from Return on, under the swirl.
                bool swirling = machine.Current == TimeMachine.Step.Hold;
                if (swirling && seat.Seated)
                {
                    sawSeated = true;
                }
                if (swirling && sawSeated && !seat.Seated)
                {
                    leftEarly = true;
                }
                yield return null;
            }
            Check(sawSeated && !leftEarly, "the dupe arrived in the " + where + " chair and stayed seated through the hold");
            cap = Time.unscaledTime + 15f;
            while ((seat.Seated || seat.Requested) && Time.unscaledTime < cap)
            {
                yield return null;
            }
            Check(!seat.Seated && !seat.Requested && machine.Rider == null, "then got out of the " + where + " chair and left it free");
        }

        /// <summary>
        /// The ride's gameplay (fx/TIME-MACHINE-DESIGN.md "RIDE GAMEPLAY"), read off both machines
        /// after the first ride: every bolt struck a wall (damaged or immune) or a dupe, and the
        /// radiation was on for the charge, the hop and the hold (1.58 + 2.5 + 3 s, the departing
        /// machine's charge and hop plus the arriving machine's hold). Each half is skipped, and
        /// says so, without the DLC it needs (bolts DLC3, radiation Spaced Out).
        /// </summary>
        /// <summary>
        /// The wormhole across the camera cut (0.6.5): the frame after the hop, both ends hold the full
        /// tunnel, so the cut lands inside it whichever world the camera shows, and the pass drew it
        /// while it opened on the departing world.
        /// </summary>
        private void CheckWormholeAtCut(TimeMachine from, TimeMachine to, string where, int pushedBefore)
        {
            int pushed = WormholePass.FramesPushed - pushedBefore;
            Log(string.Format("WORMHOLE at the cut to {0}: departing {1:F2} ({2}), arriving {3:F2} ({4}), {5} frames drawn while it opened; {6}",
                where, from.WormholeLevel, from.Current, to.WormholeLevel, to.Current, pushed, Mod.WormholeLoadReport));
            Check(Mod.WormholeShader != null && from.WormholeLevel > 0.99f && to.WormholeLevel > 0.99f && pushed > 0,
                string.Format("the wormhole was fully open on both worlds across the cut to {0} ({1:F2} / {2:F2}, {3} frames drawn)",
                    where, from.WormholeLevel, to.WormholeLevel, pushed));
        }

        /// <summary>
        /// The cut's own frame, after it rendered: the pass drew the ARRIVING machine's glass. The
        /// 23:28 take drew the departing one, off-screen, for that frame (the choice was made in
        /// Update, before this coroutine moved the camera), and the world-switch hitch held the
        /// empty frame on screen for 0.3 s and 0.6 s.
        /// </summary>
        private void CheckWormholeDrawnAfterCut(TimeMachine to, string where)
        {
            TimeMachine drawn = WormholePass.Drawn;
            Check(drawn == to, string.Format("the cut's own frame drew the arriving machine's wormhole ({0}: drawn {1} on world {2}, level {3:F2})",
                where, drawn == null ? "none" : "a machine", drawn == null ? -1 : drawn.GetMyWorldId(), WormholePass.Level));
        }

        private void CheckWormholeClosed(TimeMachine machine, string where)
        {
            Check(machine.WormholeLevel <= 0f && machine.WormholeFlare <= 0f && WormholePass.Level <= 0f,
                string.Format("the wormhole closed into the glass before the dupe got up ({0}: open {1:F2}, flare {2:F2}, drawn {3:F2})",
                    where, machine.WormholeLevel, machine.WormholeFlare, WormholePass.Level));
        }

        private void RideGameplay(TimeMachine home, TimeMachine twin)
        {
            int fired = home.BoltsFired + twin.BoltsFired;
            int walls = home.WallHits + twin.WallHits, immune = home.ImmuneHits + twin.ImmuneHits;
            int dupes = home.DupeHits + twin.DupeHits;
            string bolts = string.Format("{0} bolts: {1} soft-wall hits ({2:F2} cell health), {3} immune, {4} dupe hits ({5:F0} HP)",
                fired, walls, home.WallDamage + twin.WallDamage, immune, dupes, home.DupeDamage + twin.DupeDamage);
            Log("RIDE GAMEPLAY " + bolts + string.Format(" [home {0} bolts over charge and travel, twin {1} over the hold]",
                home.BoltsFired, twin.BoltsFired));
            if (home.BoltsAvailable)
            {
                Check(fired > 0 && walls + immune + dupes == fired, "every bolt struck a wall or a dupe (" + bolts + ")");
                Check(twin.BoltsFired > 0 && twin.BoltTargets > 0, string.Format(
                    "the arriving machine fired bolts through its hold too ({0} at {1} targets)", twin.BoltsFired, twin.BoltTargets));
            }
            else
            {
                Log("bolt gameplay skipped: no DLC3 bolt kanims");
            }
            float radiated = home.RadiatedSeconds + twin.RadiatedSeconds;
            if (home.RadiationAvailable)
            {
                Check(radiated > 10f && radiated < 12.5f, string.Format(
                    "radiation was on for the charge, travel and hold only ({0:F1} s, expected ~11.1)", radiated));
            }
            else
            {
                Log("radiation skipped: no Spaced Out");
            }
        }

        /// <summary>
        /// Why a dupe is not doing a chore, from its own last precondition snapshot (what the
        /// game's chore debugger reads): its current chore, and the chore's failed precondition.
        /// </summary>
        private static string WhyNot(WorkerBase dupe, Chore chore)
        {
            if (dupe == null)
            {
                return "no dupe";
            }
            ChoreDriver driver = dupe.GetComponent<ChoreDriver>();
            string text = "current chore " + (driver != null && driver.GetCurrentChore() != null
                ? driver.GetCurrentChore().choreType.Id : "none") + ", world " + dupe.GetMyWorldId()
                + ", pending " + (chore == null ? "none" : chore.choreType.Id + (chore.IsValid() ? "" : " (invalid)"));
            ChoreConsumer consumer = dupe.GetComponent<ChoreConsumer>();
            if (consumer == null || chore == null)
            {
                return text;
            }
            ChoreConsumer.PreconditionSnapshot snap = consumer.GetLastPreconditionSnapshot();
            foreach (Chore.Precondition.Context c in snap.failedContexts)
            {
                if (c.chore == chore)
                {
                    return text + ", FAILED " + c.chore.GetPreconditions()[c.failedPreconditionId].condition.id;
                }
            }
            foreach (Chore.Precondition.Context c in snap.succeededContexts)
            {
                if (c.chore == chore)
                {
                    return text + ", succeeded (not yet chosen)";
                }
            }
            return text + ", not in the dupe's last snapshot";
        }

        private IEnumerator ShowWholeWorlds(WorldContainer home, WorldContainer twin)
        {
            foreach (WorldContainer w in new[] { home, twin })
            {
                RevealTestArea(w.WorldOffset.x, w.WorldOffset.y, w.WorldOffset.x + w.WorldSize.x - 1,
                    w.WorldOffset.y + w.WorldSize.y - 1);
                ClusterManager.Instance.SetActiveWorld(w.id);
                List<int> corners = new List<int>
                {
                    Grid.XYToCell(w.WorldOffset.x + Border, w.WorldOffset.y + Border),
                    Grid.XYToCell(w.WorldOffset.x + w.WorldSize.x - Border, w.WorldOffset.y + w.WorldSize.y - Border),
                };
                DemoNarrator.FocusCameraToFit(corners, 1f);
                yield return Capture("TtPrehist-world-" + (w == home ? "home" : "twin") + ".png", 2f);
            }
        }

        private IEnumerator Capture(string png, float settleSeconds)
        {
            yield return Checkpoint(settleSeconds);
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot == null)
            {
                yield break;
            }
            try
            {
                File.WriteAllBytes(RigSaves.ScreenshotPathFor(png), shot.EncodeToPNG());
                Log("captured " + png);
            }
            catch (Exception e)
            {
                Log("could not write " + png + ": " + e.Message);
            }
            Object.Destroy(shot);
        }
    }
}
