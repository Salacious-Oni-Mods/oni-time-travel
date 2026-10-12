using System.IO;
using HarmonyLib;
using KMod;
using UnityEngine;
using UnityEngine.Rendering;

namespace FxSandbox
{
    // Time travel: a duplicant takes a seat in the Time Machine and rides to a prehistoric twin of
    // the home asteroid, and back after a cooldown. Pick the TIME TRAVEL card on a new game; it
    // generates Terra and the twin (TimeTwin.cs). Build a Time Machine on Terra and its partner
    // appears at the same place on the twin (TimeMachine.cs); the Ride button sends a duplicant.
    //
    // The dev rigs are compiled only into a dev build (FxSandbox.csproj, FX_DEV_RIGS), and each
    // runs only under its command-line flag:
    //   --fx-probe   FxProbeRig: the ScreenEffects plumbing with an identity pass, and both
    //                variants of the warp shader drawn once with no points
    //   --fx-warp    FxWarpRig: the spacetime warp measured on screen around one Warp Emitter
    //   --fx-look    FxLookRig: the same canvas held steady with the clock running, to look at
    //   --fx-time    FxTimeRig: the time machine on its canvas, with one rider
    //   --fx-seat    FxSeatRig: the same canvas, one ride per candidate seat pose
    //   --fx-ttprehist  FxTtPrehistRig: the time-travel showcase on a real new game
    // The Warp Emitter stays a debug-only building in no build menu.
    public class Mod : UserMod2
    {
        /// <summary>The shader bundle beside FxSandbox.dll (see README.md, "The shader").</summary>
        public const string Bundle = "fxsandbox-shaders";

        /// <summary>The spacetime-warp shader (design doc §4.1).</summary>
        public const string WarpShaderName = "FxSandbox/SpacetimeWarp";

        /// <summary>The time machine's transition screen (WormholePass, 0.6.5).</summary>
        public const string WormholeShaderName = "FxSandbox/Wormhole";

        /// <summary>The low-end variant's keyword (design doc §4.1, multi_compile_local).</summary>
        public const string CheapKeyword = "FX_CHEAP";

        /// <summary>The warp shader, or null when the bundle or the shader did not load.</summary>
        public static Shader WarpShader { get; private set; }

        /// <summary>Why <see cref="WarpShader"/> is null, or the load summary when it is not.</summary>
        public static string WarpLoadReport { get; private set; } = "not loaded";

        /// <summary>The wormhole shader, or null when the bundle or the shader did not load.</summary>
        public static Shader WormholeShader { get; private set; }

        /// <summary>Why <see cref="WormholeShader"/> is null, or the load summary when it is not.</summary>
        public static string WormholeLoadReport { get; private set; } = "not loaded";

        public override void OnLoad(Harmony harmony)
        {
            // base.OnLoad applies every [HarmonyPatch] in this assembly.
            base.OnLoad(harmony);

            // OniFramework.Library 1.2: ScreenEffects and ShaderBundles, the surfaces this mod draws with.
            OniFramework.LibraryVersion.Require(1, 2, "OniTimeTravel");

#if FX_DEV_RIGS
            // BOTH HALVES ARE NEEDED: BlueprintWorld and this Install arm the WORLD; the
            // RigRegistry.Register in Patches.cs schedules the RIG.
            // canvasSaves: the probe saves its canvas and loads it back to test the re-bind.
            OniFramework.BlueprintWorld.Install(harmony);
            OniFramework.BlueprintRig.Install(harmony, "FxSandbox", "FXPROBE",
                "--fx-probe", "blueprints/fx-probe.yaml", typeof(FxProbeRig), canvasSaves: true);
            OniFramework.BlueprintRig.Install(harmony, "FxSandbox", "FXWARP",
                "--fx-warp", "blueprints/fx-warp.yaml", typeof(FxWarpRig));
            OniFramework.BlueprintRig.Install(harmony, "FxSandbox", "FXLOOK",
                "--fx-look", "blueprints/fx-warp.yaml", typeof(FxLookRig));
            OniFramework.BlueprintRig.Install(harmony, "FxSandbox", "FXTIME",
                "--fx-time", "blueprints/fx-time.yaml", typeof(FxTimeRig));
            OniFramework.BlueprintRig.Install(harmony, "FxSandbox", "FXSEAT",
                "--fx-seat", "blueprints/fx-time.yaml", typeof(FxSeatRig));
#endif

            // The Warp Emitter is debug-only and in no build menu; the strings are for its tooltip.
            WarpEmitterConfig.InstallStrings();
            TimeMachineConfig.InstallStrings();
            TimeTwin.InstallStrings();

            // The TIME TRAVEL card and the twin's worldgen hooks, in every game. The hooks act only
            // on the twin's own world file, so other clusters generate exactly as before.
            TimeTwin.Install(harmony);

            string directory = Path.GetDirectoryName(typeof(Mod).Assembly.Location);
            WarpShader = LoadShader(directory, WarpShaderName, out string warpReport);
            WarpLoadReport = warpReport;
            WormholeShader = LoadShader(directory, WormholeShaderName, out string wormholeReport);
            WormholeLoadReport = wormholeReport;
        }

        // M2's load probe. The bundle loads, the shader is in it and supported, it declares
        // FX_CHEAP in its local keyword space, and a material takes the keyword on and off once.
        // That the two variants also DRAW is FXPROBE's phase 0 (it needs a frame); this half only
        // needs the player, so it runs at load where a missing bundle is reported first.
        private static Shader LoadShader(string directory, string name, out string report)
        {
            string failure = null;
            Shader shader = OniFramework.ShaderBundles.Load(directory, Bundle, name,
                line => failure = line);
            if (shader == null)
            {
                report = failure ?? ("ShaderBundles.Load returned null for " + name);
                Debug.Log("[FxSandbox] FX-LOAD FAIL " + report);
                return null;
            }
            LocalKeyword cheap = new LocalKeyword(shader, CheapKeyword);
            if (!cheap.isValid)
            {
                report = name + " has no local keyword " + CheapKeyword;
                Debug.Log("[FxSandbox] FX-LOAD FAIL " + report);
                return null;
            }
            Material probe = new Material(shader);
            probe.EnableKeyword(cheap);
            bool on = probe.IsKeywordEnabled(cheap);
            probe.DisableKeyword(cheap);
            bool off = !probe.IsKeywordEnabled(cheap);
            Object.Destroy(probe);
            if (!on || !off)
            {
                report = CheapKeyword + " did not toggle (on " + on + ", off " + off + ")";
                Debug.Log("[FxSandbox] FX-LOAD FAIL " + report);
                return null;
            }
            report = name + " from " + Bundle + ": supported, passes "
                + shader.passCount + ", " + CheapKeyword + " toggled on and off";
            Debug.Log("[FxSandbox] FX-LOAD OK " + report);
            return shader;
        }
    }
}
