using System.Collections.Generic;
using OniFramework;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// Owns the one <see cref="ScreenEffectHandle"/> for the spacetime warp (design doc §6.2) and
    /// feeds it the live emitters, up to <see cref="ScreenEffects.MaxPoints"/>, nearest the
    /// camera centre first.
    ///
    /// The points are pushed from the emitters' LateUpdate, not from the pass's Update callback:
    /// with no points the pass is unbound (§3.4), and an unbound pass is never called, so a
    /// callback could never bring it back. The material is built once and lives for the process
    /// (the API doc's material-lifetime rule: never destroy it while a handle may draw with it),
    /// and the handle is never disposed, so a non-null handle is a registered one.
    /// </summary>
    public static class WarpPass
    {
        public const string Id = "FxSandbox.SpacetimeWarp";

        private static readonly List<WarpEmitter> live = new List<WarpEmitter>();
        private static readonly List<WarpEmitter> nearest = new List<WarpEmitter>();
        private static readonly List<ScreenEffectPoint> points = new List<ScreenEffectPoint>(ScreenEffects.MaxPoints);
        private static ScreenEffectHandle handle;
        private static Material material;
        private static int refreshedFrame = -1;

        /// <summary>The pass's material, or null when the shader did not load (Mod.WarpLoadReport says why).</summary>
        public static Material Material => material;

        /// <summary>The registered handle, or null before the first emitter or without a shader.</summary>
        public static ScreenEffectHandle Handle => handle;

        public static int LiveEmitters => live.Count;

        /// <summary>The first live emitter, or null. The rigs have exactly one.</summary>
        public static WarpEmitter First => live.Count > 0 ? live[0] : null;

        internal static void Add(WarpEmitter emitter)
        {
            if (!live.Contains(emitter))
            {
                live.Add(emitter);
            }
            EnsureRegistered();
        }

        internal static void Remove(WarpEmitter emitter)
        {
            live.Remove(emitter);
            if (live.Count == 0 && handle != null)
            {
                handle.ClearPoints();
            }
        }

        /// <summary>Once per frame, from any emitter's LateUpdate: the live points into the handle.</summary>
        internal static void Refresh()
        {
            if (Time.frameCount == refreshedFrame || handle == null)
            {
                return;
            }
            refreshedFrame = Time.frameCount;

            Vector3 eye = CameraController.Instance != null ? CameraController.Instance.transform.position : Vector3.zero;
            nearest.Clear();
            for (int i = 0; i < live.Count; i++)
            {
                WarpEmitter e = live[i];
                if (e == null || !e.Emitting)
                {
                    continue;
                }
                // Insertion sort by distance: no comparer object, so nothing allocates per frame
                // (FXWARP phase 5 gates the per-frame allocation).
                float d = SquaredDistance(e, eye);
                int at = nearest.Count;
                while (at > 0 && SquaredDistance(nearest[at - 1], eye) > d)
                {
                    at--;
                }
                nearest.Insert(at, e);
            }

            points.Clear();
            int n = Mathf.Min(nearest.Count, ScreenEffects.MaxPoints);
            for (int i = 0; i < n; i++)
            {
                points.Add(nearest[i].ToPoint());
            }
            handle.SetPoints(points);
        }

        private static void EnsureRegistered()
        {
            if (handle != null)
            {
                return;
            }
            if (Mod.WarpShader == null)
            {
                return;
            }
            if (material == null)
            {
                material = new Material(Mod.WarpShader) { name = Id };
            }
            handle = ScreenEffects.Register(new ScreenEffectSpec
            {
                Id = Id,
                Material = material,
                Order = 0,
                CheapKeyword = Mod.CheapKeyword,
            });
        }

        private static float SquaredDistance(WarpEmitter e, Vector3 eye)
        {
            Vector2 c = e.WarpCentre;
            float dx = c.x - eye.x;
            float dy = c.y - eye.y;
            return dx * dx + dy * dy;
        }
    }
}
