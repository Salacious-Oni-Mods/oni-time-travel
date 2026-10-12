using System.Collections.Generic;
using OniFramework;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// The time machine's transition screen (fx/TIME-MACHINE-DESIGN.md, 0.6.5): one
    /// <see cref="ScreenEffectHandle"/> drawing FxSandbox/Wormhole around the glass of the machine the
    /// player is looking at. It replaces the whiteout at the hop: the iris opens over the travel's last
    /// second, the tunnel fills the view across the camera cut, then flares white and closes into the
    /// glass of the machine at the other end.
    ///
    /// Built like <see cref="WarpPass"/>. Every machine with a wormhole offers itself from Update; the
    /// first machine's LateUpdate picks the widest one on the active world and pushes it (or clears the
    /// pass when none qualifies). Only the active world's machines take part, so the departing machine
    /// draws until the camera cuts and the arriving one from then on. The world is checked at the push,
    /// not at the offer: a camera cut made by a coroutine lands between Update and LateUpdate, and a
    /// choice made in Update drew the departing glass, off-screen, for the cut's frame (in a TTPREHIST
    /// take that one frame was the world-switch hitch, 0.3 s and 0.6 s of no tunnel).
    /// The material lives for the process and the handle is never disposed.
    /// </summary>
    public static class WormholePass
    {
        public const string Id = "FxSandbox.Wormhole";

        /// <summary>The iris at full open, in viewport heights: past the far corner from anywhere on a 16:9 view.</summary>
        public const float CoverHeights = 2.2f;

        private static readonly Color StreakTint = new Color(0.35f, 0.8f, 1f, 1f);

        private static ScreenEffectHandle handle;
        private static Material material;
        private static readonly List<TimeMachine> offered = new List<TimeMachine>();
        private static TimeMachine drawn;
        private static int offeredFrame = -1, pushedFrame = -1;

        /// <summary>The registered handle, or null before the first wormhole or without a shader.</summary>
        public static ScreenEffectHandle Handle => handle;

        /// <summary>The open level pushed this frame (0 when nothing draws).</summary>
        public static float Level { get; private set; }

        /// <summary>The machine whose wormhole was pushed this frame, or null.</summary>
        public static TimeMachine Drawn => drawn;

        /// <summary>Frames the pass was given a point, since load; the rigs check it moves.</summary>
        public static int FramesPushed { get; private set; }

        /// <summary>From <see cref="TimeMachine"/>'s Update, after its timeline has run.</summary>
        internal static void Offer(TimeMachine machine)
        {
            if (machine.WormholeLevel <= 0f && machine.WormholeFlare <= 0f)
            {
                return;
            }
            if (offeredFrame != Time.frameCount)
            {
                offered.Clear();
                offeredFrame = Time.frameCount;
            }
            offered.Add(machine);
        }

        /// <summary>From every machine's LateUpdate; only the first call in a frame does anything.</summary>
        internal static void Push()
        {
            if (pushedFrame == Time.frameCount)
            {
                return;
            }
            pushedFrame = Time.frameCount;
            TimeMachine best = null;
            if (offeredFrame == Time.frameCount && ClusterManager.Instance != null)
            {
                int world = ClusterManager.Instance.activeWorldId;
                for (int i = 0; i < offered.Count; i++)
                {
                    TimeMachine m = offered[i];
                    if (m != null && m.GetMyWorldId() == world && (best == null || m.WormholeLevel > best.WormholeLevel))
                    {
                        best = m;
                    }
                }
            }
            if (best == null)
            {
                if (Level > 0f && handle != null)
                {
                    handle.ClearPoints();
                }
                Level = 0f;
                drawn = null;
                return;
            }
            EnsureRegistered();
            if (handle == null)
            {
                return;
            }
            Camera cam = CameraController.Instance != null ? CameraController.Instance.baseCamera : null;
            float half = cam != null && cam.orthographicSize > 0f ? cam.orthographicSize : 10f;
            Vector3 c = Grid.CellToPosCCC(Grid.PosToCell(best), Grid.SceneLayer.Building);
            handle.SetPoint(new ScreenEffectPoint
            {
                WorldCentre = new Vector2(c.x + TimeMachine.GlassCentre.x, c.y + TimeMachine.GlassCentre.y),
                RadiusCells = CoverHeights * 2f * half / Grid.CellSizeInMeters,
                Strength = best.WormholeLevel,
                Phase = best.WormholeClock,
                ColourShift = best.WormholeFlare,
                Shimmer = 1f,
                Rings = 1f,
                Tint = StreakTint,
            });
            Level = best.WormholeLevel;
            drawn = best;
            FramesPushed++;
        }

        /// <summary>A machine going away takes its wormhole with it.</summary>
        internal static void Remove(TimeMachine machine)
        {
            offered.Remove(machine);
            if (drawn == machine)
            {
                drawn = null;
                if (handle != null)
                {
                    handle.ClearPoints();
                }
                Level = 0f;
            }
        }

        private static void EnsureRegistered()
        {
            if (handle != null || Mod.WormholeShader == null)
            {
                return;
            }
            if (material == null)
            {
                material = new Material(Mod.WormholeShader) { name = Id };
            }
            // After the warp (Order 0): the tunnel covers the warped picture, not the other way round.
            handle = ScreenEffects.Register(new ScreenEffectSpec
            {
                Id = Id,
                Material = material,
                Order = 10,
                CheapKeyword = Mod.CheapKeyword,
            });
        }
    }
}
