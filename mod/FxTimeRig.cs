using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OniFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FxSandbox
{
    /// <summary>
    /// FXTIME (--fx-time): the mock time machine to LOOK at, on its own canvas (fx-time.yaml, the
    /// FXWARP stripes with the machine on the hall floor at 23,6).
    ///
    /// The owner's look pass for the machine: the camera framed on it, the clock running, and
    /// <see cref="Cycles"/> full cycles (idle, enter, charge, travel, return, exit, cooldown) with
    /// a short idle between. Its checks only prove the hold showed what it claims: the kanim
    /// loaded with all seven anims, every step was visited in order, the warp was on during
    /// travel and off while idle, and the cycles finished.
    ///
    /// Phase 2 puts one dupe in it: the dupe takes the ride chore, walks in on vanilla's teleporter
    /// anims, vanishes for travel, is back on return, and walks out alive. One screenshot per step
    /// (FxTime-ride-*.png) answers what the checks cannot: whether the teleporter's dupe anim sits
    /// on this machine's seat.
    /// </summary>
    public class FxTimeRig : RigHarness
    {
        private const int Cycles = 3;
        private const float IdleBetween = 2f;
        protected const int MachineX = 23, MachineY = 6;

        // On the hall floor between the west pillar (x 17, solid Granite) and the machine (x 22..25).
        // The first live run spawned the dupe at x 17, inside the pillar, and it never moved.
        protected const int DupeX = 19;

        private static readonly string[] Anims = { "idle", "enter", "charge", "travel", "return", "exit", "cooldown" };

        protected override string ModTag => "FxSandbox";

        protected override string RigTag => "FXTIME";

        protected override string BlueprintFile => "blueprints/fx-time.yaml";

        protected override IEnumerator Run()
        {
            Log("FXTIME: PHASE 0 -- setup");
            TimeMachine machine = Object.FindObjectOfType<TimeMachine>();
            if (machine == null)
            {
                Fail("the canvas placed no Time Machine (FxTimeMachine at " + MachineX + "," + MachineY + ")");
                yield break;
            }
            KBatchedAnimController anim = machine.GetComponent<KBatchedAnimController>();
            List<string> missing = new List<string>();
            foreach (string a in Anims)
            {
                if (!anim.HasAnimation(a))
                {
                    missing.Add(a);
                }
            }
            Check(missing.Count == 0, "fx_time_machine_kanim carries all seven anims"
                + (missing.Count == 0 ? "" : " (missing: " + string.Join(", ", missing.ToArray()) + ")"));
            if (WarpPass.Material == null || WarpPass.Handle == null)
            {
                Inconclusive("shader unavailable: " + Mod.WarpLoadReport);
                yield break;
            }
            WarpEmitter warp = machine.GetComponent<WarpEmitter>();
            RevealTestArea(FxWarpRig.HallX, FxWarpRig.HallY,
                FxWarpRig.HallX + FxWarpRig.HallW - 1, FxWarpRig.HallY + FxWarpRig.HallH - 1);
            DemoNarrator.Begin("Time machine: the ride", "The swirl, the white pulse, the shade, the shake and the bolts.");
            DemoNarrator.SetPhaseCount(2);
            DemoNarrator.StartRecording();
            DemoNarrator.FocusCameraToFit(MachineCells(), 2f);
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }

            Check(machine.Current == TimeMachine.Step.Idle && !warp.Emitting, "the machine spawned idle with the warp off");

            Log("FXTIME: PHASE 1 -- " + Cycles + " cycles");
            DemoNarrator.Phase("Empty cycles", Cycles + " cycles with nobody in the chair.");
            List<TimeMachine.Step> seen = new List<TimeMachine.Step>();
            bool warpInTravel = true, warpWhileIdle = false;
            for (int c = 0; c < Cycles; c++)
            {
                float until = Time.realtimeSinceStartup + IdleBetween;
                while (Time.realtimeSinceStartup < until)
                {
                    warpWhileIdle |= warp.Emitting;
                    yield return null;
                }
                int before = machine.Cycles;
                machine.StartCycle();
                float cap = Time.realtimeSinceStartup + 60f;
                while (machine.Cycles == before && Time.realtimeSinceStartup < cap)
                {
                    if (seen.Count == 0 || seen[seen.Count - 1] != machine.Current)
                    {
                        seen.Add(machine.Current);
                        Log("FXTIME: cycle " + (c + 1) + " -> " + machine.Current);
                    }
                    if (machine.Current == TimeMachine.Step.Travel)
                    {
                        warpInTravel &= warp.Emitting && warp.Strength > 0.9f * machine.PeakStrength;
                    }
                    yield return null;
                }
                if (machine.Cycles == before)
                {
                    Fail("cycle " + (c + 1) + " did not finish within 60 s (stuck in " + machine.Current + ")");
                    yield break;
                }
            }

            TimeMachine.Step[] order =
            {
                TimeMachine.Step.Enter, TimeMachine.Step.Charge, TimeMachine.Step.Travel, TimeMachine.Step.Hold,
                TimeMachine.Step.Return, TimeMachine.Step.Exit, TimeMachine.Step.Cooldown,
            };
            bool inOrder = true;
            int i = 0;
            foreach (TimeMachine.Step s in seen)
            {
                if (s == TimeMachine.Step.Idle)
                {
                    continue;
                }
                inOrder &= s == order[i % order.Length];
                i++;
            }
            Check(inOrder && i == Cycles * order.Length, "every cycle stepped enter, charge, travel, hold, return, exit, cooldown in order ("
                + i + " steps seen)");
            Check(warpInTravel, "the warp was on at full strength (PeakStrength " + machine.PeakStrength + ") through every travel");
            Check(!warpWhileIdle, "the warp stayed off while the machine was idle");
            Check(machine.Cycles == Cycles, "the machine finished " + Cycles + " cycles (" + machine.Cycles + ")");

            Log("FXTIME: PHASE 2 -- one dupe rides");
            DemoNarrator.Phase("The ride", "A duplicant sits down, fades out at the hop, fades back in and walks off.");
            GameObject dupe;
            try
            {
                dupe = new MinionStartingStats(is_starter_minion: false)
                    .Deliver(Grid.CellToPosCBC(Grid.XYToCell(DupeX, MachineY), Grid.SceneLayer.Move));
            }
            catch (Exception e)
            {
                Fail("could not spawn a duplicant: " + e.Message);
                yield break;
            }
            float spawnCap = Time.realtimeSinceStartup + 10f;
            while (!Spawned(dupe) && Time.realtimeSinceStartup < spawnCap)
            {
                yield return null;
            }
            if (!Spawned(dupe))
            {
                Fail("the duplicant never finished spawning");
                yield break;
            }
            TimeMachineSeat seat = machine.GetComponent<TimeMachineSeat>();
            int ridesBefore = machine.Cycles;
            seat.RequestRide();
            Check(seat.Requested, "the ride chore was created");

            KBatchedAnimController dupeAnim = dupe.GetComponent<KBatchedAnimController>();
            WorkerBase rider = null;
            bool hiddenInTravel = true, sawTravel = false, ordered = true;
            float maxShade = 0f, maxShake = 0f, maxWhite = 0f, whiteSeconds = 0f;
            int boltsBefore = machine.BoltsFired;
            float maxWormhole = 0f, maxFlare = 0f;
            int pushedBefore = WormholePass.FramesPushed;
            TimeMachine.Step shotOf = TimeMachine.Step.Idle;
            float rideCap = Time.realtimeSinceStartup + 90f;
            while (machine.Cycles == ridesBefore && Time.realtimeSinceStartup < rideCap)
            {
                if (machine.Rider != null)
                {
                    rider = machine.Rider;
                }
                if (machine.Current == TimeMachine.Step.Travel)
                {
                    sawTravel = true;
                    hiddenInTravel &= dupeAnim.TintColour.a == 0;
                }
                maxShade = Mathf.Max(maxShade, machine.ShadeLevel);
                maxShake = Mathf.Max(maxShake, machine.ShakeLevel);
                maxWhite = Mathf.Max(maxWhite, machine.FlashLevel);
                maxWormhole = Mathf.Max(maxWormhole, machine.WormholeLevel);
                maxFlare = Mathf.Max(maxFlare, machine.WormholeFlare);
                whiteSeconds += machine.FlashLevel > 0.5f * TimeMachine.WhitePeak ? Time.deltaTime : 0f;
                ordered &= machine.OverlaysOrdered;
                if (machine.Current != shotOf && machine.Current != TimeMachine.Step.Idle)
                {
                    shotOf = machine.Current;
                    // Mid-step, so the anim is past its first frame. The travel shot waits for the
                    // wormhole's iris to be half open (0.6.5); the hold shot at 0.4 s is the full tunnel.
                    float mid = Time.realtimeSinceStartup + 0.4f;
                    float irisCap = Time.realtimeSinceStartup + 10f;
                    while (shotOf == TimeMachine.Step.Travel
                        ? machine.Current == TimeMachine.Step.Travel && machine.WormholeLevel < 0.5f && Time.realtimeSinceStartup < irisCap
                        : Time.realtimeSinceStartup < mid)
                    {
                        yield return null;
                    }
                    yield return Capture("FxTime-ride-" + shotOf.ToString().ToLowerInvariant() + ".png");
                    Log("FXTIME: ride " + shotOf + " captured");
                    continue;
                }
                yield return null;
            }
            if (machine.Cycles == ridesBefore)
            {
                Fail("the ride did not finish within 90 s (machine " + machine.Current + ", rider "
                    + (rider != null ? rider.name : "none") + ")");
                yield break;
            }
            float leaveCap = Time.realtimeSinceStartup + 3f;
            while (seat.Requested && Time.realtimeSinceStartup < leaveCap)
            {
                yield return null;
            }
            Check(rider != null && rider.gameObject == dupe, "the spawned dupe took the ride");
            Check(sawTravel && hiddenInTravel, "the dupe was faded out through travel");
            Check(maxWhite > 0.95f * TimeMachine.WhitePeak && whiteSeconds < 3f, string.Format(
                "the white pulsed to its cap and briefly (peak {0:F2} of {1:F2}, above half for {2:F1} s over the hop and the hold; gate 3 s)",
                maxWhite, TimeMachine.WhitePeak, whiteSeconds));
            int pushed = WormholePass.FramesPushed - pushedBefore;
            Check(Mod.WormholeShader != null && maxWormhole > 0.99f && maxFlare > 0.9f && pushed > 0
                && machine.WormholeLevel <= 0f && WormholePass.Level <= 0f, string.Format(
                "the wormhole opened fully, flared and closed (open up to {0:F2}, flare up to {1:F2}, {2} frames drawn, now {3:F2}; {4})",
                maxWormhole, maxFlare, pushed, WormholePass.Level, Mod.WormholeLoadReport));
            Check(ordered, "front to back the overlays stayed bolts, swirl, white, shade");
            Check(maxShade > 0.5f, string.Format("the screen darkened (shade up to {0:F2})", maxShade));
            Check(maxShake >= 0.25f, string.Format("the screen shook (up to {0:F2} cells)", maxShake));
            if (machine.BoltsAvailable)
            {
                Check(machine.BoltTargets > 0 && machine.BoltsFired > boltsBefore, string.Format(
                    "bolts hit nearby solid cells ({0} fired at {1} targets)", machine.BoltsFired - boltsBefore, machine.BoltTargets));
                // The bolts are recoloured at load from the player's DLC3 kanims (BoltRecolour). Their
                // textures go next to the screenshots, to diff offline against bolt.py's copies.
                Check(BoltRecolour.Made == 2, string.Format("both bolt kanims were made at load from DLC3's ({0} of 2; {1})",
                    BoltRecolour.Made, BoltRecolour.ReadPath));
                foreach (KeyValuePair<string, Texture2D> made in BoltRecolour.Textures)
                {
                    string png = "FxTime-runtime-" + made.Key + ".png";
                    try
                    {
                        File.WriteAllBytes(RigSaves.ScreenshotPathFor(png), made.Value.EncodeToPNG());
                    }
                    catch (Exception e)
                    {
                        Log("could not write " + png + ": " + e.Message);
                    }
                }
            }
            else
            {
                Log("FXTIME: no bolt check, the stress beam kanims are not loaded (DLC3)");
            }
            Check(dupeAnim.IsVisible(), "the dupe is visible again after the ride");
            Check(!seat.Requested && machine.Rider == null, "the ride chore ended and the seat is free");
            Check(!dupe.HasTag(GameTags.Dead) && !dupe.HasTag(GameTags.Dying), "the dupe is alive after the ride");
            DemoNarrator.End("The ride for the owner's look pass.");
            yield return Checkpoint(2f);
        }

        protected static bool Spawned(GameObject go)
        {
            MinionIdentity id = go != null ? go.GetComponent<MinionIdentity>() : null;
            return id != null && id.isSpawned;
        }

        protected IEnumerator Capture(string png)
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot == null)
            {
                yield break;
            }
            try
            {
                File.WriteAllBytes(RigSaves.ScreenshotPathFor(png), shot.EncodeToPNG());
            }
            catch (Exception e)
            {
                Log("could not write " + png + ": " + e.Message);
            }
            Object.Destroy(shot);
        }

        protected static IEnumerable<int> MachineCells()
        {
            List<int> cells = new List<int>();
            for (int y = MachineY - 1; y < MachineY + 8; y++)
            {
                for (int x = MachineX - 5; x < MachineX + 7; x++)
                {
                    cells.Add(Grid.XYToCell(x, y));
                }
            }
            return cells;
        }
    }
}
