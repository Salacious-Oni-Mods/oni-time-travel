using System.Collections;
using OniFramework;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// FXLOOK (--fx-look): the spacetime warp to LOOK at, on the FXWARP canvas, with nothing stepped.
    ///
    /// FXWARP measures the pass, and its own steps (a frozen phase, strength jumps, a reversed
    /// speed, camera snaps, 300-frame ON/OFF windows) read on screen as the effect jumping. This
    /// rig is the owner's look pass (M4): one emitter at its default look (strength 1, speed 1),
    /// the camera framed once, the game clock running, and a realtime hold long enough to watch
    /// the rings and the shimmer loop. Its checks only prove the hold showed what it claims: the
    /// emitter was live the whole time, its phase advanced at the set speed, and the clock ran.
    /// </summary>
    public class FxLookRig : RigHarness
    {
        // Long enough to see several full ring cycles and the 8-phase shimmer circle more than once.
        private const float HoldSeconds = 48f;

        protected override string ModTag => "FxSandbox";

        protected override string RigTag => "FXLOOK";

        protected override string BlueprintFile => "blueprints/fx-warp.yaml";

        protected override IEnumerator Run()
        {
            Log("FXLOOK: PHASE 0 -- setup");
            WarpEmitter emitter = WarpPass.First;
            if (emitter == null)
            {
                Fail("the canvas placed no Warp Emitter (FxWarpEmitter at 23,6)");
                yield break;
            }
            if (WarpPass.Material == null || WarpPass.Handle == null)
            {
                Inconclusive("shader unavailable: " + Mod.WarpLoadReport);
                yield break;
            }
            emitter.Strength = 1f;
            emitter.Speed = 1f;
            emitter.Emitting = true;
            RevealTestArea(FxWarpRig.HallX, FxWarpRig.HallY,
                FxWarpRig.HallX + FxWarpRig.HallW - 1, FxWarpRig.HallY + FxWarpRig.HallH - 1);
            DemoNarrator.FocusCameraToFit(FxWarpRig.HallCells(), 2f);
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }

            Log("FXLOOK: PHASE 1 -- hold " + HoldSeconds + " s, nothing stepped");
            float phase0 = emitter.Phase;
            float clock0 = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
            bool dropped = false;
            float until = Time.realtimeSinceStartup + HoldSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                dropped |= WarpPass.First != emitter || !emitter.Emitting;
                yield return null;
            }
            float phaseRun = emitter.Phase - phase0;
            float clockRun = GameClock.Instance != null ? GameClock.Instance.GetTime() - clock0 : 0f;

            Check(!dropped, "the emitter stayed live for the whole hold");
            Check(Mathf.Abs(phaseRun - HoldSeconds) < 2f, "the phase advanced at speed 1 (" + phaseRun.ToString("F1")
                + " over a " + HoldSeconds + " s hold)");
            Check(clockRun > 0f, "the game clock ran during the hold (" + clockRun.ToString("F1") + " s)");
        }
    }
}
