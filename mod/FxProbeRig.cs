using System.Collections;
using OniFramework;
using UnityEngine;
using UnityEngine.Rendering;

namespace FxSandbox
{
    /// <summary>
    /// FXPROBE (--fx-probe): the plumbing of OniFramework.ScreenEffects, with no shader of our own.
    ///
    /// M1 of the spacetime-warp plan (oni/docs/spacetime-warp-effect.md §8). The pass is Klei's own
    /// <c>Klei/PostFX/Desaturate</c>, which compiles to a pure copy (its amount is unused in the
    /// compiled fragment, design doc §2.1), so a live pass must leave the picture unchanged. That
    /// lets the rig check the binding without a bundle: M2 brings the warp shader, and M3's FXWARP
    /// rig replaces this one.
    ///
    /// WHAT IT CHECKS, and why each half is there:
    /// 1. Registered with <c>DrawWithoutPoints</c>, the pass goes live (<c>LivePasses == 1</c>,
    ///    <c>IsLive</c>), Klei calls it every frame (<c>PassesDrawn</c> and the Update callback
    ///    advance), and the picture is unchanged against a capture with nothing registered. The
    ///    identity alone could pass with nothing bound, so it is never read without the counters.
    /// 2. H1: a pass disabled from INSIDE its own Update callback (inside Klei's foreach) leaves
    ///    the list on the next LateUpdate without throwing, and comes back when re-enabled.
    /// 3. H2: the canvas is saved and loaded back in this process. Klei's list dies with the old
    ///    CameraController; the same handle must be live again on the new one, and drawn.
    /// 4. Dispose takes it out (<c>LivePasses == 0</c>).
    /// 0. (M2) Before any of that: the warp shader that Mod.OnLoad loaded draws in BOTH variants. With
    ///    no points it is an identity, so a flat colour blitted through it must come back unchanged;
    ///    a variant the player could not run comes back black or magenta instead.
    ///
    /// The second-load path is the least proven part: no rig before this one has loaded a save
    /// from inside its own Run(). The harness object is DontDestroyOnLoad, so the coroutine should
    /// carry on; if it does not, the run has no verdict and rig-wait.sh says so (exit 2).
    /// </summary>
    public sealed class FxProbeRig : RigHarness
    {
        private const string PassId = "FxSandbox.Probe";
        private const string SaveName = "fx-probe.sav";
        // Mean absolute difference, per channel, on the 0..255 scale. The design's identity bar.
        private const float IdentityMeanDiff = 0.5f;
        // A real-time safeguard on the reload, not the plan: the wait ends as soon as the new world
        // is up, and a load that has not finished in this long is a broken run.
        private const float ReloadCapSeconds = 120f;

        private ScreenEffectHandle handle;
        private Material material;
        private int updates;
        private bool disableFromInsideUpdate;
        private Texture2D shot;

        protected override string ModTag => "FxSandbox";

        protected override string RigTag => "FXPROBE";

        protected override string BlueprintFile => "blueprints/fx-probe.yaml";

        protected override bool IsPresentation => false;

        // Nothing here depends on the simulation having ticked: every check is on the render path.
        protected override bool VerdictSurvivesAStoppedClock => true;

        protected override IEnumerator Run()
        {
            Log("PHASE 0 -- setup");
            Shader shader = Shader.Find("Klei/PostFX/Desaturate");
            string shaderName = "Klei/PostFX/Desaturate";
            if (shader == null || !shader.isSupported)
            {
                // Unity's own copy shader is always in a player build; it is an identity too.
                shader = Shader.Find("Hidden/BlitCopy");
                shaderName = "Hidden/BlitCopy";
            }
            if (shader == null || !shader.isSupported)
            {
                Inconclusive("no identity shader: neither Klei/PostFX/Desaturate nor Hidden/BlitCopy "
                    + "was found and supported, so there is nothing to bind");
                yield break;
            }
            material = new Material(shader);
            Log("identity pass is " + shaderName);
            Log("PHASE 0b -- the warp shader's two variants draw (M2)");
            if (Check(Mod.WarpShader != null, "the warp shader loaded at OnLoad (" + Mod.WarpLoadReport + ")"))
            {
                Check(VariantIsIdentity(false, out string full),
                    "the full variant with no points draws the source unchanged (" + full + ")");
                Check(VariantIsIdentity(true, out string cheap),
                    "the " + Mod.CheapKeyword + " variant with no points draws the source unchanged (" + cheap + ")");
            }
            Check(ScreenEffects.LivePasses == 0, "nothing is live before the probe registers (LivePasses "
                + ScreenEffects.LivePasses + ")");

            yield return Checkpoint(1f);
            yield return Capture();
            Texture2D off = shot;

            Log("PHASE 1 -- live pass, counters and identity");
            int revision = ScreenEffects.Revision;
            handle = ScreenEffects.Register(new ScreenEffectSpec
            {
                Id = PassId,
                Material = material,
                DrawWithoutPoints = true,
                Update = OnPassUpdate,
            });
            if (!Check(handle != null, "Register returned a handle"))
            {
                Destroy(off);
                yield break;
            }
            Check(ScreenEffects.Revision == revision + 1, "Revision bumped once on Register");
            Check(ScreenEffects.Register(new ScreenEffectSpec { Id = PassId, Material = material }) == null,
                "a duplicate Id is refused");
            yield return Frames(5);
            Check(ScreenEffects.LivePasses == 1 && handle.IsLive,
                "the pass is live (LivePasses " + ScreenEffects.LivePasses + ", IsLive " + handle.IsLive + ")");

            long drawn = ScreenEffects.PassesDrawn;
            int updated = updates;
            yield return Frames(30);
            long drawnDelta = ScreenEffects.PassesDrawn - drawn;
            Check(drawnDelta >= 20, "PassesDrawn advanced " + drawnDelta + " over 30 frames (want >= 20)");
            Check(updates - updated == drawnDelta,
                "the Update callback ran once per draw (" + (updates - updated) + " for " + drawnDelta + ")");

            yield return Capture();
            float diff = MeanDiff(off, shot);
            Check(diff < IdentityMeanDiff, "the live identity pass leaves the picture unchanged (mean diff "
                + diff.ToString("F3") + "/255, want < " + IdentityMeanDiff + ")");
            Destroy(shot);
            Destroy(off);

            Log("PHASE 2 -- H1: disabled from inside its own Update, inside Klei's foreach");
            disableFromInsideUpdate = true;
            yield return Frames(5);
            Check(!handle.Enabled && ScreenEffects.LivePasses == 0 && !handle.IsLive,
                "disabling from inside Update unbinds on the next LateUpdate without throwing (Enabled "
                + handle.Enabled + ", LivePasses " + ScreenEffects.LivePasses + ")");
            handle.Enabled = true;
            yield return Frames(5);
            Check(ScreenEffects.LivePasses == 1 && handle.IsLive, "re-enabling binds it again");

            Log("PHASE 3 -- H2: save the canvas, load it back, the same handle re-binds");
            CameraController before = CameraController.Instance;
            string path = SaveLoader.Instance.Save(RigSaves.PathFor(SaveName));
            Log("saved " + path + "; loading it back");
            LoadScreen.DoLoad(path);
            float deadline = Time.realtimeSinceStartup + ReloadCapSeconds;
            while (!NewWorldIsUp(before))
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Fail("the reload did not bring up a new CameraController and grid within "
                        + ReloadCapSeconds + " s");
                    yield break;
                }
                yield return null;
            }
            yield return Checkpoint(2f);
            Check(ScreenEffects.LivePasses == 1 && handle.IsLive,
                "after the load the same handle is live on the new camera (LivePasses "
                + ScreenEffects.LivePasses + ", IsLive " + handle.IsLive + ")");
            drawn = ScreenEffects.PassesDrawn;
            yield return Frames(30);
            drawnDelta = ScreenEffects.PassesDrawn - drawn;
            Check(drawnDelta >= 20, "after the load PassesDrawn advanced " + drawnDelta + " over 30 frames");

            Log("PHASE 4 -- Dispose");
            revision = ScreenEffects.Revision;
            handle.Dispose();
            yield return Frames(5);
            Check(ScreenEffects.LivePasses == 0 && !handle.IsLive && ScreenEffects.Revision == revision + 1,
                "Dispose unbinds the pass (LivePasses " + ScreenEffects.LivePasses + ")");
            Check(!ScreenEffects.TryGet(PassId, out _), "a disposed Id is no longer registered");
        }

        private void OnPassUpdate(ScreenEffectFrame frame)
        {
            updates++;
            if (disableFromInsideUpdate)
            {
                disableFromInsideUpdate = false;
                frame.Handle.Enabled = false;
            }
        }

        private static bool NewWorldIsUp(CameraController before)
        {
            CameraController now = CameraController.Instance;
            return now != null && !ReferenceEquals(now, before) && Game.Instance != null && Grid.CellCount > 0;
        }

        // One blit of a flat colour through the warp shader with zero points, read back off the GPU.
        // _FxPointCount is a global that ScreenEffects writes only while a pass is live; nothing is
        // live in phase 0, so setting it here cannot fight the framework.
        private static bool VariantIsIdentity(bool cheapVariant, out string detail)
        {
            const int Size = 8;
            const int Tolerance = 2;
            Color32 colour = new Color32(64, 128, 192, 255);
            Texture2D source = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            Color32[] fill = new Color32[Size * Size];
            for (int i = 0; i < fill.Length; i++)
            {
                fill[i] = colour;
            }
            source.SetPixels32(fill);
            source.Apply();
            RenderTexture target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            Material warp = new Material(Mod.WarpShader);
            LocalKeyword cheap = new LocalKeyword(Mod.WarpShader, Mod.CheapKeyword);
            warp.SetKeyword(cheap, cheapVariant);
            Shader.SetGlobalFloat("_FxPointCount", 0f);
            Shader.SetGlobalFloat("_FxAspect", 1f);
            Graphics.Blit(source, target, warp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D back = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            back.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            back.Apply();
            RenderTexture.active = previous;
            Color32 got = back.GetPixel(Size / 2, Size / 2);
            RenderTexture.ReleaseTemporary(target);
            Destroy(warp);
            Destroy(source);
            Destroy(back);
            detail = "got " + got.r + "," + got.g + "," + got.b + " for " + colour.r + "," + colour.g + "," + colour.b;
            return System.Math.Abs(got.r - colour.r) <= Tolerance && System.Math.Abs(got.g - colour.g) <= Tolerance
                && System.Math.Abs(got.b - colour.b) <= Tolerance;
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        // The finished backbuffer, after the GPU has drawn the frame; the diff runs in-process so
        // nothing waits on a file.
        private IEnumerator Capture()
        {
            yield return new WaitForEndOfFrame();
            shot = ScreenCapture.CaptureScreenshotAsTexture();
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
    }
}
