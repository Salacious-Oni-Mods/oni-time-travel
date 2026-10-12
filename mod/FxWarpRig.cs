using System.Collections;
using System.Collections.Generic;
using System.IO;
using OniFramework;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// FXWARP (--fx-warp): the spacetime-warp screen pass, measured on the screen (design doc §6.4).
    ///
    /// The canvas (blueprints/fx-warp.yaml) is straight high-contrast lines around one Warp Emitter,
    /// so a lens shows. The clock is paused for the whole run; the render path still runs, and the
    /// sim cannot move anything between two captures.
    ///
    /// PHASES
    /// 0. Setup: shader present (else INCONCLUSIVE), frame the hall, freeze the ripple.
    /// 1. Identity: strength 0 against the emitter off, mean diff &lt; 0.5/255, with the pass live.
    /// 2. Strengths 0.25, 0.5, 1: the change stays inside R + 1 cell (catches a Y flip, a wrong
    ///    aspect or a wrong radius scale), nothing changes outside, and it grows with strength.
    /// 3. Motion: with the ripple running, two captures differ inside the disc and not outside.
    /// 4. Screen edge: the same with the warp near the right edge, plus a streak check.
    /// 5. Frame time: OFF/ON/OFF/ON medians, delta &lt;= 0.5 ms (full variant gated, cheap logged),
    ///    and no per-frame allocation.
    ///
    /// Every diff runs in-process on the captured textures; the PNGs are for the owner's checklist.
    /// </summary>
    public sealed class FxWarpRig : RigHarness
    {
        // Mean absolute difference per channel, 0..255 scale: the identity bar (§6.4).
        private const float IdentityMeanDiff = 0.5f;
        // A pixel counts as "changed" above this (max channel, 0..255).
        private const int ChangedThreshold = 8;
        private const float ConfinedFraction = 0.99f;
        private const int Window = 300;
        private const float DeltaGateMs = 0.5f;
        private const float StabilityMs = 0.3f;
        private const long AllocationGateBytes = 64 * 1024;
        // The hall the warp sits in, canvas = world cells (fx-warp.yaml).
        internal const int HallX = 12, HallY = 6, HallW = 24, HallH = 18;

        private WarpEmitter emitter;
        private Texture2D shot;
        private bool pausedByUs;

        protected override string ModTag => "FxSandbox";

        protected override string RigTag => "FXWARP";

        protected override string BlueprintFile => "blueprints/fx-warp.yaml";

        protected override bool IsPresentation => false;

        protected override bool ShowsGameUi => false;

        // The game is paused for the whole measurement; render still runs, sim noise is removed.
        protected override bool VerdictSurvivesAStoppedClock => true;

        protected override IEnumerator Run()
        {
            Log("FXWARP: PHASE 0 -- setup");
            // The canvas, emitter included, is built by the harness before Run, so the emitter is
            // looked up here; gating IsReady on it deadlocked (the build waits for IsReady).
            if (WarpPass.First == null)
            {
                Fail("the canvas placed no Warp Emitter (FxWarpEmitter at 23,6)");
                yield break;
            }
            if (WarpPass.Material == null || WarpPass.Handle == null)
            {
                Inconclusive("shader unavailable: " + Mod.WarpLoadReport);
                yield break;
            }
            emitter = WarpPass.First;
            if (SpeedControlScreen.Instance != null && !SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Pause(false);
                pausedByUs = true;
            }
            DemoNarrator.FocusCameraToFit(HallCells(), 2f);
            emitter.Speed = 0f;
            emitter.Phase = 0.25f;
            yield return Frames(30);
            Log("warp centre " + emitter.WarpCentre + ", radius " + emitter.RadiusCells + " cells");

            bool savedCheap = ScreenEffects.CheapMode;
            ScreenEffects.CheapMode = false;

            // ---------------------------------------------------------------- 1
            Log("FXWARP: PHASE 1 -- identity (control)");
            emitter.Emitting = false;
            yield return Frames(5);
            yield return Capture("FxWarp-off.png");
            Texture2D off = shot;

            float colourShift = emitter.ColourShift;
            float shimmer = emitter.Shimmer;
            Color tint = emitter.Tint;
            emitter.Strength = 0f;
            emitter.ColourShift = 0f;
            emitter.Shimmer = 0f;
            emitter.Tint = new Color(tint.r, tint.g, tint.b, 0f);
            emitter.Emitting = true;
            yield return Frames(5);
            yield return Capture("FxWarp-s000.png");
            float idDiff = MeanDiff(off, shot);
            Check(idDiff < IdentityMeanDiff, "strength 0 is an identity (mean diff " + idDiff.ToString("F3")
                + "/255, want < " + IdentityMeanDiff + ")");
            Check(ScreenEffects.LivePasses == 1 && WarpPass.Handle.IsLive,
                "the pass was live for the identity capture (LivePasses " + ScreenEffects.LivePasses + ")");
            Destroy(shot);

            // ---------------------------------------------------------------- 2
            Log("FXWARP: PHASE 2 -- three strengths, ripple frozen at phase 0.25");
            emitter.ColourShift = colourShift;
            emitter.Shimmer = shimmer;
            emitter.Tint = tint;
            float[] strengths = { 0.25f, 0.5f, 1f };
            string[] names = { "FxWarp-s025.png", "FxWarp-s050.png", "FxWarp-s100.png" };
            int[] inside = new int[strengths.Length];
            for (int s = 0; s < strengths.Length; s++)
            {
                emitter.Strength = strengths[s];
                yield return Frames(5);
                yield return Capture(names[s]);
                Disc disc = DiscNow();
                Stats st = Compare(off, shot, disc);
                inside[s] = st.ChangedInside;
                CheckConfinedAndOutside(st, "strength " + strengths[s]);
                Destroy(shot);
            }
            Check(inside[0] < inside[1] && inside[1] < inside[2], "the changed pixels inside the radius rise "
                + "strictly with strength (" + inside[0] + " < " + inside[1] + " < " + inside[2] + ")");

            // ---------------------------------------------------------------- 3
            Log("FXWARP: PHASE 3 -- motion");
            emitter.Strength = 1f;
            emitter.Speed = 1f;
            yield return Frames(5);
            yield return Capture(null);
            Texture2D a = shot;
            yield return Frames(30);
            yield return Capture(null);
            {
                Disc disc = DiscNow();
                Stats st = Compare(a, shot, disc);
                float moved = disc.PixelCount > 0 ? (float)st.ChangedInside / disc.PixelCount : 0f;
                Check(moved > 0.01f, "the running ripple moves the picture inside the disc ("
                    + (moved * 100f).ToString("F2") + "% of its pixels changed in 30 frames, want > 1%)");
                Check(st.OutsideMean < IdentityMeanDiff, "nothing moves outside R + 1 (mean diff "
                    + st.OutsideMean.ToString("F3") + "/255)");
            }
            Destroy(a);
            Destroy(shot);
            emitter.Speed = -1f;
            yield return Frames(30);
            yield return Capture("FxWarp-reverse.png");
            Destroy(shot);
            Log("reverse speed captured (FxWarp-reverse.png): a human check that the rings fall inward");
            emitter.Speed = 0f;
            emitter.Phase = 0.25f;

            // ---------------------------------------------------------------- 4
            Log("FXWARP: PHASE 4 -- screen edge");
            Camera cam = CameraController.Instance.overlayCamera;
            float ortho = cam.orthographicSize;
            float worldWidth = 2f * ortho * cam.aspect;
            Vector2 centre = emitter.WarpCentre;
            CameraController.Instance.SnapTo(new Vector3(centre.x - 0.45f * worldWidth, centre.y, 0f), ortho);
            emitter.Emitting = false;
            yield return Frames(10);
            yield return Capture("FxWarp-edge-off.png");
            Texture2D edgeOff = shot;
            emitter.Emitting = true;
            yield return Frames(5);
            yield return Capture("FxWarp-edge.png");
            {
                Disc disc = DiscNow();
                float at = disc.CentreX / shot.width;
                Check(at > 0.85f, "the warp centre sits near the right edge (" + at.ToString("F3")
                    + " of the width, want > 0.85; lower means the camera was clamped)");
                Stats st = Compare(edgeOff, shot, disc);
                CheckConfinedAndOutside(st, "at the edge");
                int streaks = StreakRows(edgeOff, shot, disc, out int rows);
                Check(streaks <= Mathf.Max(1, rows / 50), "no clamp streak in the right-most 4 columns ("
                    + streaks + " of " + rows + " disc rows are one stretched colour only when warped)");
            }
            Destroy(edgeOff);
            Destroy(shot);

            // ---------------------------------------------------------------- 5
            Log("FXWARP: PHASE 5 -- frame time");
            DemoNarrator.FocusCameraToFit(HallCells(), 2f);
            emitter.Strength = 1f;
            yield return Frames(30);
            float[] full = new float[4];
            long[] alloc = new long[4];
            bool[] collected = new bool[4];
            yield return Windows(full, alloc, collected);
            float offDelta = Mathf.Abs(full[0] - full[2]);
            float delta = (full[1] + full[3]) / 2f - (full[0] + full[2]) / 2f;
            Log("full variant medians ms: OFF " + full[0].ToString("F3") + ", ON " + full[1].ToString("F3")
                + ", OFF " + full[2].ToString("F3") + ", ON " + full[3].ToString("F3")
                + "; delta " + delta.ToString("F3") + " ms");
            if (offDelta > StabilityMs)
            {
                Inconclusive("machine noise: the two OFF medians differ by " + offDelta.ToString("F3")
                    + " ms (> " + StabilityMs + ")");
            }
            else
            {
                Check(delta <= DeltaGateMs, "the full variant costs <= " + DeltaGateMs + " ms a frame (delta "
                    + delta.ToString("F3") + " ms)");
            }
            // The game allocates every frame with or without the pass (an earlier run saw the
            // heap rise ~1 MB per window and had no OFF number to hold it against), so the gate is
            // what an ON window allocates beyond the OFF window before it. A window a collection
            // fell in measures nothing, so that pair is dropped rather than read.
            Log("heap rise B: OFF " + alloc[0] + ", ON " + alloc[1] + ", OFF " + alloc[2] + ", ON " + alloc[3]
                + "; collections in window: " + collected[0] + "/" + collected[1] + "/" + collected[2] + "/" + collected[3]);
            int pairs = 0;
            long worst = long.MinValue;
            for (int on = 1; on < 4; on += 2)
            {
                if (collected[on - 1] || collected[on])
                {
                    continue;
                }
                pairs++;
                worst = System.Math.Max(worst, alloc[on] - alloc[on - 1]);
            }
            if (pairs == 0)
            {
                Inconclusive("allocation unmeasured: a collection fell in both ON/OFF pairs");
            }
            else
            {
                Check(worst <= AllocationGateBytes, "the pass allocates nothing per frame (ON beyond OFF "
                    + worst + " B over " + Window + " frames in the worse of " + pairs + " clean pair(s), gate "
                    + AllocationGateBytes + ")");
            }
            LogGpuTime();

            ScreenEffects.CheapMode = true;
            float[] cheap = new float[4];
            yield return Windows(cheap, new long[4], new bool[4]);
            float cheapDelta = (cheap[1] + cheap[3]) / 2f - (cheap[0] + cheap[2]) / 2f;
            Log("cheap variant medians ms: OFF " + cheap[0].ToString("F3") + ", ON " + cheap[1].ToString("F3")
                + ", OFF " + cheap[2].ToString("F3") + ", ON " + cheap[3].ToString("F3")
                + "; delta " + cheapDelta.ToString("F3") + " ms (logged, not gated)");

            ScreenEffects.CheapMode = savedCheap;
            Destroy(off);
            if (pausedByUs && SpeedControlScreen.Instance != null)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }
        }

        // ------------------------------------------------------------------ measurement

        private struct Disc
        {
            public float CentreX;
            public float CentreY;
            public float Radius;     // R, pixels
            public float Outer;      // R + 1 cell, pixels
            public int PixelCount;   // pixels within R, on screen
        }

        private struct Stats
        {
            public int Changed;
            public int ChangedInside;
            public float OutsideMean;
        }

        // The warp centre and radius in screenshot pixels, projected the way the framework does
        // (overlay camera, WorldToViewportPoint; radius in viewport heights) -- the same transform
        // the framework's own rig diagnostics use.
        private Disc DiscNow()
        {
            Camera cam = CameraController.Instance.overlayCamera;
            int w = Screen.width;
            int h = Screen.height;
            Vector2 c = emitter.WarpCentre;
            Vector3 v = cam.WorldToViewportPoint(new Vector3(c.x, c.y, 0f));
            float perCell = Grid.CellSizeInMeters / (2f * cam.orthographicSize) * h;
            Disc d = new Disc
            {
                CentreX = v.x * w,
                CentreY = v.y * h,
                Radius = emitter.RadiusCells * perCell,
                Outer = (emitter.RadiusCells + 1f) * perCell,
            };
            int count = 0;
            int x0 = Mathf.Max(0, (int)(d.CentreX - d.Radius)), x1 = Mathf.Min(w - 1, (int)(d.CentreX + d.Radius));
            int y0 = Mathf.Max(0, (int)(d.CentreY - d.Radius)), y1 = Mathf.Min(h - 1, (int)(d.CentreY + d.Radius));
            float r2 = d.Radius * d.Radius;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - d.CentreX, dy = y + 0.5f - d.CentreY;
                    if (dx * dx + dy * dy <= r2)
                    {
                        count++;
                    }
                }
            }
            d.PixelCount = count;
            return d;
        }

        private static Stats Compare(Texture2D x, Texture2D y, Disc disc)
        {
            Stats st = new Stats { OutsideMean = float.MaxValue };
            if (x == null || y == null || x.width != y.width || x.height != y.height)
            {
                return st;
            }
            Color32[] p = x.GetPixels32();
            Color32[] q = y.GetPixels32();
            int w = x.width;
            float in2 = disc.Radius * disc.Radius;
            float out2 = disc.Outer * disc.Outer;
            double outsideSum = 0;
            long outsidePixels = 0;
            for (int i = 0; i < p.Length; i++)
            {
                int dr = System.Math.Abs(p[i].r - q[i].r);
                int dg = System.Math.Abs(p[i].g - q[i].g);
                int db = System.Math.Abs(p[i].b - q[i].b);
                float dx = i % w + 0.5f - disc.CentreX, dy = i / w + 0.5f - disc.CentreY;
                float r2 = dx * dx + dy * dy;
                if (System.Math.Max(dr, System.Math.Max(dg, db)) > ChangedThreshold)
                {
                    st.Changed++;
                    if (r2 <= out2)
                    {
                        st.ChangedInside++;
                    }
                }
                if (r2 > out2)
                {
                    outsideSum += dr + dg + db;
                    outsidePixels++;
                }
            }
            st.OutsideMean = outsidePixels > 0 ? (float)(outsideSum / (3.0 * outsidePixels)) : 0f;
            return st;
        }

        private void CheckConfinedAndOutside(Stats st, string label)
        {
            float confined = st.Changed > 0 ? (float)st.ChangedInside / st.Changed : 0f;
            Check(st.Changed > 0 && confined >= ConfinedFraction, label + ": the change is confined to R + 1 cell ("
                + st.ChangedInside + " of " + st.Changed + " changed pixels, " + (confined * 100f).ToString("F2")
                + "%, want >= " + (ConfinedFraction * 100f) + "%)");
            Check(st.OutsideMean < IdentityMeanDiff, label + ": nothing changes outside R + 1 (mean diff "
                + st.OutsideMean.ToString("F3") + "/255, want < " + IdentityMeanDiff + ")");
        }

        // A row of the disc's span whose right-most 4 pixels are one colour when warped but were not
        // before: the stretched edge texel a missing saturate (or a wrong wrap mode) would draw.
        private static int StreakRows(Texture2D before, Texture2D after, Disc disc, out int rows)
        {
            rows = 0;
            int streaks = 0;
            int w = after.width;
            int y0 = Mathf.Max(0, (int)(disc.CentreY - disc.Radius));
            int y1 = Mathf.Min(after.height - 1, (int)(disc.CentreY + disc.Radius));
            Color32[] p = before.GetPixels32();
            Color32[] q = after.GetPixels32();
            for (int y = y0; y <= y1; y++)
            {
                rows++;
                int row = y * w;
                if (Flat(q, row + w - 4) && !Flat(p, row + w - 4))
                {
                    streaks++;
                }
            }
            return streaks;
        }

        private static bool Flat(Color32[] px, int start)
        {
            for (int i = 1; i < 4; i++)
            {
                if (px[start + i].r != px[start].r || px[start + i].g != px[start].g || px[start + i].b != px[start].b)
                {
                    return false;
                }
            }
            return true;
        }

        private static float MeanDiff(Texture2D x, Texture2D y)
        {
            if (x == null || y == null || x.width != y.width || x.height != y.height)
            {
                return float.MaxValue;
            }
            Color32[] p = x.GetPixels32();
            Color32[] q = y.GetPixels32();
            double sum = 0;
            for (int i = 0; i < p.Length; i++)
            {
                sum += System.Math.Abs(p[i].r - q[i].r) + System.Math.Abs(p[i].g - q[i].g)
                    + System.Math.Abs(p[i].b - q[i].b);
            }
            return (float)(sum / (3.0 * p.Length));
        }

        // OFF, ON, OFF, ON, Window frames each: the median unscaled frame time (ms) of each, the
        // managed-heap rise across each (the same source RigHarness meters with), and whether a
        // gen-0 collection fell inside it (then its rise is meaningless).
        private IEnumerator Windows(float[] medians, long[] alloc, bool[] collected)
        {
            float[] samples = new float[Window];
            for (int wdw = 0; wdw < 4; wdw++)
            {
                bool on = wdw % 2 == 1;
                emitter.Emitting = on;
                yield return Frames(10);
                int gcs = System.GC.CollectionCount(0);
                long heap = System.GC.GetTotalMemory(false);
                for (int i = 0; i < Window; i++)
                {
                    yield return null;
                    samples[i] = Time.unscaledDeltaTime * 1000f;
                }
                alloc[wdw] = System.GC.GetTotalMemory(false) - heap;
                collected[wdw] = System.GC.CollectionCount(0) != gcs;
                System.Array.Sort(samples);
                medians[wdw] = samples[Window / 2];
            }
            emitter.Emitting = true;
        }

        private void LogGpuTime()
        {
            FrameTimingManager.CaptureFrameTimings();
            FrameTiming[] t = new FrameTiming[1];
            uint n = FrameTimingManager.GetLatestTimings(1, t);
            Log(n > 0 && t[0].gpuFrameTime > 0 ? "gpu=" + t[0].gpuFrameTime.ToString("F3") + " ms" : "gpu=unavailable");
        }

        // ------------------------------------------------------------------ plumbing

        internal static IEnumerable<int> HallCells()
        {
            List<int> cells = new List<int>(HallW * HallH);
            for (int y = HallY; y < HallY + HallH; y++)
            {
                for (int x = HallX; x < HallX + HallW; x++)
                {
                    cells.Add(Grid.XYToCell(x, y));
                }
            }
            return cells;
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        // The finished backbuffer after the GPU has drawn the frame (ScreenCapture after
        // WaitForEndOfFrame); a PNG copy when named, written through RigSaves so the folder exists.
        private IEnumerator Capture(string png)
        {
            yield return new WaitForEndOfFrame();
            shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (png != null && shot != null)
            {
                try
                {
                    File.WriteAllBytes(RigSaves.ScreenshotPathFor(png), shot.EncodeToPNG());
                }
                catch (System.Exception e)
                {
                    Log("could not write " + png + ": " + e.Message);
                }
            }
        }
    }
}
