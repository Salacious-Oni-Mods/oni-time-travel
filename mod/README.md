# Time Travel

A duplicant takes a seat in the Time Machine, rides to a prehistoric twin of the home asteroid, and
rides back after a cooldown. Needs the OniFramework.Library mod 1.2 or newer; does not need the
custom SimDLL.

## Playing it

1. Start a new game and pick the **TIME TRAVEL** card. It generates two asteroids: Terra, and
   Prehistoric Terra, the same Terra long ago.
2. Build a **Time Machine** (Equipment menu) on Terra. Its partner is built for you at the same
   place on the prehistoric twin, in a cleared pocket, and the area around it is revealed.
3. Select the machine and press **Time Travel**. A duplicant walks to the seat, rides, and arrives
   in the partner machine on the twin. Press **Time Travel** on the twin's machine to ride back.
4. After a ride both machines recharge for 5 cycles on the sim clock. The recharge is saved and
   survives a reload; the button says how long is left.

## The time machine

`FxTimeMachine` is a 4x4 building (`TimeMachine.cs`). A dupe sits in its chair (`TimeMachineSeat`,
beach-chair pose), the machine charges, hops the dupe to its partner machine on another world
(vanilla's teleporter order, `WarpPortal.DelayedWarp`, without the portal's state machine), holds the
arrival, and then cools down for 5 cycles on the sim clock (`ReadyAt`, saved). The pair is found again
by place after every load (`TimeMachine.Pair`); a machine built on any other asteroid has no partner
and its rider arrives back in it.

A ride draws a white pulse, the warp swirl, a dark shade, screen shake and blue bolts: blue copies
of DLC3's stress-shock beam made at load from the player's own DLC3 files (`BoltRecolour.cs`), so
the mod ships none of Klei's art. The bolts strike nearby walls like a comet's tile damage (hardness VERY FIRM and up
is immune, and so is the floor under the machine) and, one in ten, a nearby dupe for 5 HP (never
below 10 HP, never the rider); the machine radiates 380 rads in a 6-cell radius while it charges,
hops and holds. Bolts need DLC3 (the Bionic Booster Pack): without it the bolt effects and their
damage are disabled. Radiation needs Spaced Out. Without either, the ride works without that part.

The partner world is the PREHISTORIC TWIN (`TimeTwin.cs`, `worldgen/`): a second Terra generated
with home at new-game time from home's own yaml and seeds, then converted cell by cell before the
settle, keeping each cell's phase, stability, mass and temperature, so the shape matches home.
Every biome, the start included, becomes DLC4's Garden, Raptor or Wetlands (elements, zone
backgrounds, critters and plants); the magma core, the surface crust and space stay as home's.

## The Warp Emitter

`FxWarpEmitter` is a dev-only 1x1 building (`DebugOnly`, Klei's `dev_generator_kanim`, in no build
menu), modelled on Klei's `DevGenerator`. Each one puts a warp 8 cells above its cell centre;
`WarpPass` owns the one ScreenEffects handle and feeds it up to 8 live emitters, nearest the camera
first. With no emitters the pass has no points and is unbound. The emitter saves nothing.

## The shader

`FxSandbox/SpacetimeWarp` and `FxSandbox/Wormhole` are built into the bundle `fxsandbox-shaders`
with the project's Unity shader build, which places it in `fx-sandbox/bin/Release/` beside
`FxSandbox.dll`. The bundle is a build output and is not committed. At load, `Mod.cs` reads it with
`ShaderBundles.Load`, checks the `FX_CHEAP` keyword exists and toggles, and logs
`[FxSandbox] FX-LOAD OK` or `FX-LOAD FAIL <why>`.

## Dev rigs

The default build is the playable mod and references only OniFramework.Library. The rigs derive
from the framework's `RigHarness` (in OniFramework) and are compiled only into a dev build:

    dotnet build -c Release -p:FxDevRigs=true -p:OniFrameworkProject=<path to OniFramework.csproj>

Each runs only under its command-line flag:

| Flag | Rig | What it checks |
|---|---|---|
| `--fx-probe` | `FxProbeRig` (FXPROBE) | the ScreenEffects plumbing with an identity pass (Klei's `PostFX/Desaturate`): the pass goes live and is drawn every frame, the picture is unchanged, disabling it from inside its own callback is safe (H1), it re-binds after loading a save (H2), and Dispose removes it. Milestone M1. Since M2 (0.1.0) it first blits a flat colour through both variants of the warp shader (full and `FX_CHEAP`) with no points, which must come back unchanged. M3's FXWARP replaces it |
| `--fx-warp` | `FxWarpRig` (FXWARP) | the spacetime warp on screen, canvas `blueprints/fx-warp.yaml`, one Warp Emitter, clock paused: strength 0 is an identity with the pass live; at 0.25/0.5/1 the change stays inside R + 1 cell, nothing changes outside, and it grows with strength; the running ripple moves only inside the disc; the same near the right screen edge plus a clamp-streak check; frame-time delta OFF/ON <= 0.5 ms (full variant; cheap logged) and no per-frame allocation. Writes `FxWarp-*.png` to the rig screenshots folder for a visual checklist (design doc §6.4). Milestone M3 |
| `--fx-look` | `FxLookRig` | the FXWARP canvas held steady with the clock running, to look at |
| `--fx-time` | `FxTimeRig` (FXTIME) | the time machine on canvas `blueprints/fx-time.yaml`, one rider, no partner: the ride's steps in order, the white pulse, the overlays' depth order, shade, shake and bolts; writes `FxTime-ride-travel.png` at the hop |
| `--fx-seat` | `FxSeatRig` (FXSEAT) | the same canvas, one ride per candidate seat pose (`TimeMachineSeat.Poses`), to pick one; the beach chair won |
| `--fx-ttprehist` | `FxTtPrehistRig` (TTPREHIST) | the time-travel showcase on a NEW GAME (autopilot, cluster `FxTimeTwinCluster`): the prehistoric twin from home's seeds with every biome converted (T1), same shape as home (T2 sites, T3 cells), the home machine building and linking its partner, the ride and hop to the twin, the arrival seat, the ride's bolts and radiation, the 5-cycle cooldown surviving a save and reload, and the ride home |

FXPROBE and FXWARP have not run live yet.
