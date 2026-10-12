using System;
using System.Collections;
using OniFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FxSandbox
{
    /// <summary>
    /// FXSEAT (--fx-seat): the time machine's seat poses, one ride each, to pick one by eye (the
    /// rocket pilot pose looked like driving, so each candidate is tried in turn in one rig). The FXTIME
    /// canvas and dupe; the machine has no partner, so each ride is the whole sequence in one
    /// place: sit down, fade out against the white interior, fade back in seated, wait out the
    /// swirl, get up and walk off. A caption names each pose, and one screenshot per pose is
    /// taken once the dupe is seated (FxSeat-&lt;n&gt;-&lt;pose&gt;.png). The beach chair was picked,
    /// so the rig now shows only <see cref="TimeMachineSeat.Pose"/>; widen
    /// <c>shown</c> to <see cref="TimeMachineSeat.Poses"/> to compare them all again.
    /// </summary>
    public sealed class FxSeatRig : FxTimeRig
    {
        protected override string RigTag => "FXSEAT";

        protected override IEnumerator Run()
        {
            TimeMachine machine = Object.FindObjectOfType<TimeMachine>();
            if (machine == null)
            {
                Fail("the canvas placed no Time Machine");
                yield break;
            }
            RevealTestArea(FxWarpRig.HallX, FxWarpRig.HallY,
                FxWarpRig.HallX + FxWarpRig.HallW - 1, FxWarpRig.HallY + FxWarpRig.HallH - 1);
            DemoNarrator.Begin("Time machine: seat poses", "Each vanilla seated pose in the chair, one ride each.");
            SeatPose[] shown = { TimeMachineSeat.Pose };
            DemoNarrator.SetPhaseCount(shown.Length);
            DemoNarrator.StartRecording();
            DemoNarrator.FocusCameraToFit(MachineCells(), 2f);
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }
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
            float cap = Time.realtimeSinceStartup + 10f;
            while (!Spawned(dupe) && Time.realtimeSinceStartup < cap)
            {
                yield return null;
            }
            if (!Spawned(dupe))
            {
                Fail("the duplicant never finished spawning");
                yield break;
            }
            TimeMachineSeat seat = machine.GetComponent<TimeMachineSeat>();
            KBatchedAnimController k = dupe.GetComponent<KBatchedAnimController>();
            for (int i = 0; i < shown.Length; i++)
            {
                SeatPose pose = shown[i];
                TimeMachineSeat.Pose = pose;
                DemoNarrator.Phase((i + 1) + ". " + pose.Name, "Pose " + (i + 1) + " of " + shown.Length + ": " + pose.Name + ".");
                yield return Checkpoint(1.5f);
                int before = machine.Cycles;
                seat.RequestRide();
                Log("FXSEAT: pose " + seat.PoseReport);
                bool seatedBeforeStart = false, shot = false;
                float minAlpha = 1f;
                cap = Time.realtimeSinceStartup + 60f;
                while (machine.Cycles == before && Time.realtimeSinceStartup < cap)
                {
                    if (machine.Current == TimeMachine.Step.Enter)
                    {
                        seatedBeforeStart |= seat.Seated;
                        if (!shot)
                        {
                            shot = true;
                            string slug = pose.Name.ToLowerInvariant().Replace(' ', '-');
                            yield return Capture("FxSeat-" + (i + 1) + "-" + slug + ".png");
                        }
                    }
                    if (machine.Current == TimeMachine.Step.Charge || machine.Current == TimeMachine.Step.Travel)
                    {
                        minAlpha = Mathf.Min(minAlpha, machine.RiderAlpha);
                    }
                    yield return null;
                }
                if (!Require(machine.Cycles > before, pose.Name + ": the ride finished within 60 s (machine " + machine.Current + ")"))
                {
                    yield break;
                }
                Check(seatedBeforeStart, pose.Name + ": seated before the machine started");
                Check(minAlpha < 0.1f, string.Format("{0}: faded out (to {1:F2})", pose.Name, minAlpha));
                cap = Time.realtimeSinceStartup + 15f;
                while ((seat.Seated || seat.Requested) && Time.realtimeSinceStartup < cap)
                {
                    yield return null;
                }
                Check(!seat.Seated && !seat.Requested && k.TintColour.a == 255 && k.Offset.sqrMagnitude < 1e-4f,
                    pose.Name + ": got up, solid and on the floor, seat free");
                yield return Checkpoint(1.5f);
            }
            DemoNarrator.End("Pick the pose; the seat offset is tuned for it next.");
            yield return Checkpoint(2f);
        }
    }
}
