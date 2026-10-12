using OniFramework;
using TUNING;
using UnityEngine;

namespace FxSandbox
{
    /// <summary>
    /// The Warp Emitter: a dev-only 1x1 building whose only job is to put a spacetime warp in the
    /// world (design doc §6.2). Modelled on the game's own dev building, the DevGenerator: its anim,
    /// <c>BuildLocationRule.Anywhere</c>, and <c>DebugOnly</c>, so <c>BuildingDef.IsAvailable</c>
    /// requires <c>Game.Instance.DebugOnlyBuildingsAllowed</c> and a player never sees it. No power, no ports, no storage.
    /// </summary>
    public sealed class WarpEmitterConfig : IBuildingConfig
    {
        public const string Id = "FxWarpEmitter";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1, "dev_generator_kanim", 100, 3f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER0, MATERIALS.ALL_METALS, 2400f, BuildLocationRule.Anywhere,
                decor: BUILDINGS.DECOR.NONE, noise: NOISE_POLLUTION.NONE);
            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "HollowMetal";
            def.DebugOnly = true;
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.AddTag(GameTags.DevBuilding);
            go.AddOrGet<WarpEmitter>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }

        /// <summary>The emitter's strings. Called from Mod.OnLoad; not added to any build menu.</summary>
        internal static void InstallStrings()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Warp Emitter");
            Strings.Add(prefix + "DESC", "A dev-only test fixture for the spacetime-warp screen effect.");
            Strings.Add(prefix + "EFFECT", "Bends the view of the world around a point above it. Dev builds only.");
        }
    }

    /// <summary>
    /// One warp in the world. Holds the look of its point and advances its own phase; WarpPass
    /// gathers every live emitter into the one screen pass.
    ///
    /// NO [Serialize] FIELDS (design doc §3.1): the emitter saves nothing, and the rig sets every
    /// value it measures.
    /// </summary>
    public sealed class WarpEmitter : KMonoBehaviour
    {
        public float Strength = 0.5f;
        public float RadiusCells = 9f;
        /// <summary>Phase per real second; negative runs the rings inward, 0 freezes them.</summary>
        public float Speed = 1f;
        public float ColourShift = 0.6f;
        public float Shimmer = 0.5f;
        public float Rings = 3f;
        public Color Tint = new Color(0.55f, 0.75f, 1f, 0.25f);

        /// <summary>
        /// From the emitter's cell CENTRE, in cells. The design doc writes (0.5, 8) from the cell's
        /// corner; this is the same point, (23.5, 14.5) for the canvas emitter at (23, 6).
        /// </summary>
        public Vector2 CentreOffset = new Vector2(0f, 8f);

        /// <summary>Off: the emitter stays in the world and contributes no point.</summary>
        public bool Emitting = true;

        public float Phase;

        public Vector2 WarpCentre
        {
            get
            {
                Vector3 c = Grid.CellToPosCCC(Grid.PosToCell(this), Grid.SceneLayer.Building);
                return new Vector2(c.x + CentreOffset.x, c.y + CentreOffset.y);
            }
        }

        public ScreenEffectPoint ToPoint()
        {
            return new ScreenEffectPoint
            {
                WorldCentre = WarpCentre,
                RadiusCells = RadiusCells,
                Strength = Strength,
                Phase = Phase,
                ColourShift = ColourShift,
                Shimmer = Shimmer,
                Rings = Rings,
                Tint = Tint,
            };
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            WarpPass.Add(this);
        }

        protected override void OnCleanUp()
        {
            WarpPass.Remove(this);
            base.OnCleanUp();
        }

        private void Update()
        {
            // The framework wraps the phase it hands the shader (PhaseWrapSeconds); the raw value
            // only needs to stay finite, which a float of seconds does for days.
            Phase += Time.unscaledDeltaTime * Speed;
        }

        private void LateUpdate()
        {
            WarpPass.Refresh();
        }
    }
}
