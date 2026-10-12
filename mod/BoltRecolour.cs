using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using KMod;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// The time machine's blue bolts, made at load time from the player's own DLC3 kanims, so the mod
    /// folder ships nothing of Klei's (for the licence; the copies bolt.py once wrote are gone).
    ///
    /// Reads bionic_dupe_stress_beam_fx and bionic_dupe_stress_beam_impact_fx as Assets.LoadAnims holds
    /// them while mods load their animation content: the DLC bundles' kanims are already in the anim
    /// table and still carry their anim/build bytes (KAnimFile.FinalizeLoading drops those later, after
    /// KAnimGroupFile.LoadAll). Registers recoloured copies under our names through ModUtil.AddKAnimMod,
    /// exactly as a mod's anim/assets folder is registered, so Assets.LoadAnims adds them to its tables
    /// itself and the time machine looks them up by name as before. Vanilla's kanims are never changed.
    ///
    /// Recolour = art/time-machine/bolt.py's: every warm pixel (hue below 90 deg or above 324 deg,
    /// saturation above 0.05, alpha above 0) moves into the blue band 195..240 deg, keeping saturation,
    /// value, alpha and the order of its hues; the anim bytes are copied as they are and the build bytes
    /// get the new build name. Without DLC3 nothing is registered and the bolts stay off.
    /// </summary>
    [HarmonyPatch(typeof(Manager), nameof(Manager.Load))]
    internal static class BoltRecolour
    {
        private static readonly string[,] Copies =
        {
            { "bionic_dupe_stress_beam_fx", "fx_time_machine_bolt" },
            { "bionic_dupe_stress_beam_impact_fx", "fx_time_machine_bolt_impact" },
        };
        private const float BlueLo = 195f / 360f, BlueHi = 240f / 360f;

        /// <summary>Kanims registered by the recolour (2 with DLC3, 0 without).</summary>
        public static int Made { get; private set; }

        /// <summary>The recoloured textures by kanim name, kept readable so a rig can write them out.</summary>
        public static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        /// <summary>How the source textures were read, for the rig log (colour space, sRGB, readable).</summary>
        public static string ReadPath { get; private set; } = "not run";

        private static bool done;

        private static void Postfix(Content content)
        {
            if (done || (content & Content.Animation) == 0)
            {
                return;
            }
            done = true;
            var path = new StringBuilder("colour space " + QualitySettings.activeColorSpace);
            for (int i = 0; i < Copies.GetLength(0); i++)
            {
                string from = Copies[i, 0], to = Copies[i, 1];
                if (!Assets.TryGetAnim(from + "_kanim", out KAnimFile src) || src == null)
                {
                    Debug.Log("[FxSandbox] bolt recolour: " + from + "_kanim is not loaded (DLC3), no bolts");
                    continue;
                }
                byte[] anim = src.animBytes, build = src.buildBytes;
                if (anim == null || build == null || src.textureList == null || src.textureList.Count == 0)
                {
                    Debug.LogWarning("[FxSandbox] bolt recolour: " + from + "_kanim has no bytes or textures left");
                    continue;
                }
                byte[] renamed = RenameBuild(build, to);
                if (renamed == null)
                {
                    Debug.LogWarning("[FxSandbox] bolt recolour: " + from + "_kanim build is not a BILD file");
                    continue;
                }
                var mod = new KAnimFile.Mod { anim = (byte[])anim.Clone(), build = renamed };
                foreach (Texture2D tex in src.textureList)
                {
                    path.Append("; ").Append(tex.name).Append(' ').Append(tex.width).Append('x').Append(tex.height)
                        .Append(' ').Append(tex.format).Append(tex.isDataSRGB ? " sRGB" : " linear")
                        .Append(tex.isReadable ? " readable" : " GPU");
                    Texture2D blue = Recolour(Readable(tex));
                    blue.name = to + "_" + mod.textures.Count;
                    mod.textures.Add(blue);
                }
                if (ModUtil.AddKAnimMod(to + "_kanim", mod) != null)
                {
                    Made++;
                    Textures[to + "_kanim"] = mod.textures[0];
                }
            }
            ReadPath = path.ToString();
            Debug.Log("[FxSandbox] bolt recolour: made " + Made + " of " + Copies.GetLength(0) + " (" + ReadPath + ")");
        }

        /// <summary>
        /// A readable RGBA32 copy. Bundle textures are GPU-only, so they go through a blit and ReadPixels;
        /// the render texture takes the source's own sRGB flag so the bytes come back as stored.
        /// </summary>
        private static Texture2D Readable(Texture2D src)
        {
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, !src.isDataSRGB)
            {
                filterMode = src.filterMode,
                wrapMode = src.wrapMode,
            };
            if (src.isReadable)
            {
                copy.SetPixels32(src.GetPixels32());
                return copy;
            }
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32,
                src.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static Texture2D Recolour(Texture2D tex)
        {
            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (c.a == 0)
                {
                    continue;
                }
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (s > 0.05f && (h < 0.25f || h > 0.9f))
                {
                    float warm = (h + 0.1f) % 1f / 0.35f;     // 0 = red .. 1 = yellow
                    h = BlueHi - warm * (BlueHi - BlueLo);    // red -> deep blue, yellow -> light cyan-blue
                    Color rgb = Color.HSVToRGB(h, s, v);
                    px[i] = new Color32((byte)Mathf.RoundToInt(rgb.r * 255f), (byte)Mathf.RoundToInt(rgb.g * 255f),
                        (byte)Mathf.RoundToInt(rgb.b * 255f), c.a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>The build's name is a length-prefixed string at byte 16, after the BILD header.</summary>
        private static byte[] RenameBuild(byte[] data, string name)
        {
            if (data.Length < 20 || data[0] != 'B' || data[1] != 'I' || data[2] != 'L' || data[3] != 'D')
            {
                return null;
            }
            int n = System.BitConverter.ToInt32(data, 16);
            if (n < 0 || 20 + n > data.Length)
            {
                return null;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            var result = new byte[data.Length - n + bytes.Length];
            System.Array.Copy(data, 0, result, 0, 16);
            System.BitConverter.GetBytes(bytes.Length).CopyTo(result, 16);
            bytes.CopyTo(result, 20);
            System.Array.Copy(data, 20 + n, result, 20 + bytes.Length, data.Length - 20 - n);
            return result;
        }
    }
}
