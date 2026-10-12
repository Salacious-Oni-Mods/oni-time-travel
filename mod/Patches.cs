using HarmonyLib;
using OniFramework;

namespace FxSandbox
{
    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn
    {
        // Game.OnSpawn runs again for every load in the same process, and the probe loads a save
        // on purpose. RigRegistry.Register does not de-duplicate, so this guard keeps the probe to
        // one instance; that instance is DontDestroyOnLoad and carries on across the load.
        private static bool registered;
        private static bool warpRegistered;
        private static bool lookRegistered;
        private static bool timeRegistered;
        private static bool ttRegistered;
        private static bool seatRegistered;

        private static void Postfix()
        {
            if (!registered)
            {
                // The ScreenEffects plumbing check, with no shader of our own. See FxProbeRig.cs.
                registered = RigRegistry.Register<FxProbeRig>("FxSandboxProbeRig", "--fx-probe");
            }
            if (!warpRegistered)
            {
                // The spacetime warp measured on screen. See FxWarpRig.cs.
                warpRegistered = RigRegistry.Register<FxWarpRig>("FxSandboxWarpRig", "--fx-warp");
            }
            if (!lookRegistered)
            {
                // The warp to look at, nothing stepped. See FxLookRig.cs.
                lookRegistered = RigRegistry.Register<FxLookRig>("FxSandboxLookRig", "--fx-look");
            }
            if (!timeRegistered)
            {
                // The mock time machine to look at. See FxTimeRig.cs.
                timeRegistered = RigRegistry.Register<FxTimeRig>("FxSandboxTimeRig", "--fx-time");
            }
            if (!seatRegistered)
            {
                // The seat poses, one ride each. See FxSeatRig.cs.
                seatRegistered = RigRegistry.Register<FxSeatRig>("FxSandboxSeatRig", "--fx-seat");
            }
            if (!ttRegistered)
            {
                // The time-travel proof of concept on the autopilot's new game. See FxTtPrehistRig.cs.
                ttRegistered = RigRegistry.Register<FxTtPrehistRig>("FxSandboxTtPrehistRig", TimeTwin.Flag);
            }
        }
    }
}
