using System;
using System.Collections.Generic;

using System.Linq;
using System.Text;
using HarmonyLib;
using Klei;
using OniFramework;
using ProcGen;
using ProcGenGame;


namespace FxSandbox
{
    /// <summary>
    /// The prehistoric twin of the home asteroid, generated with it when a new game picks the TIME
    /// TRAVEL card.
    ///
    /// <b>SAME SHAPE FIRST, PREHISTORIC SECOND.</b> Where the dinosaur is, is where the bed will be
    /// in the future, so the twin must have home's shape cell for cell. Worldgen
    /// draws a world's shape from its yaml and its four seeds, so the twin uses home's yaml
    /// (worldgen/make-twin.py) and home's seeds (<see cref="SeedPatch"/>; Klei's
    /// <c>Cluster.Generate</c> would give world i the seeds plus i). It is then made prehistoric in
    /// place, after the shape is drawn and before the settle (<see cref="Convert"/>):
    /// <list type="bullet">
    /// <item>each cell of a converted zone gets the prehistoric element of the SAME phase and the
    /// SAME stability (a falling solid stays falling, a standing one stands) at its own mass and
    /// temperature, so the settle has nothing new to collapse or melt;</item>
    /// <item>its terrain cells get the prehistoric biome's mob tags, which are what
    /// <c>MobSpawning.PlaceBiomeAmbientMobs</c> reads, so DLC4's own critters and plants spawn
    /// there by Klei's own rules;</item>
    /// <item>its saved zone type (<c>WorldDetailSave.OverworldCell.zoneType</c>, which
    /// <c>SubworldZoneRenderData</c> paints the background from) becomes the prehistoric one.</item>
    /// </list>
    /// Prehistoric subworlds swapped in by name (what DLC4's biome mixing does) would instead
    /// re-roll the terrain of every zone they replace. That is why the mixing route in the first
    /// scope was dropped.
    ///
    /// Zones converted: every biome of Terra becomes Garden, Raptor or Wetlands, the sandstone start
    /// included.
    /// Sandstone and wasteland go to Garden, jungle, frozen and radioactive to Raptor (DLC4's chilly
    /// biome), marsh, ocean and oil to Wetlands. Three zones stay as home's because they are the
    /// asteroid's physics, not a biome: the magma core, the barren surface crust and space.
    /// </summary>
    public static class TimeTwin
    {
        /// <summary>The command line flag of the dev showcase rig (FxTtPrehistRig).</summary>
        public const string Flag = "--fx-ttprehist";
        public const string HomeWorldFile = "FxTimeTerra";
        public const string TwinWorldFile = "FxTimeTwinTerra";
        public const string PlaystyleId = "FxTimeTravel";
        public const int ClusterCategory = PlaystyleRegistry.FirstModdedClusterCategory + 1;

        /// <summary>One converted zone: its new zone type, element swaps and DLC4 mob tags.</summary>
        private sealed class ZoneConversion
        {
            public SubWorld.ZoneType To;
            public Dictionary<string, string> Elements;
            public string[] MobTags;
        }

        // Elements: Terra's biome bands (worldgen/biomes/*.yaml, expansion1 Wasteland/Radioactive) onto
        // DLC4's (dlc4/worldgen/biomes/Garden.yaml, Raptor.yaml, Wetlands.yaml). An element missing from
        // a table is kept. A swap whose phase or stability differs is refused at run time and reported,
        // not applied, so the unstable sand and snow always stay. No target is harder than the DLC4 band
        // a dupe on the twin can dig without a skill (hardness < 150: no Obsidian, Katairite).
        // Mob tags: the biomes' own tag lists in dlc4/worldgen/subworlds/{garden/GardenStart,
        // raptor/RaptorBasic, wetlands/WetlandsBasic, garden/GardenBasic}.yaml; Stego from dlc4 mobs.yaml
        // (GardenMix places it through a StegoHome feature, which would carve the shape, so it is a
        // tag here), kept out of the start so the arrival stays clear.
        private static readonly string[] GardenTags =
        {
            "GardenFoodPlant", "GardenFoodPlantSeed", "GardenForagePlantPlanted", "GardenForagePlantBuried",
            "GardenDecorPlant", "GardenDecorPlantSeed", "ButterflyPlant", "VineMother", "VineMotherSeed",
        };
        private static readonly string[] RaptorTags = { "DewDripperPlant", "Raptor", "Dinofern", "Chameleon", "ColdBreather" };
        private static readonly string[] WetlandsTags =
        {
            "AnyLiquidPacu", "PrehistoricPacu", "KelpPlant", "Mosquito", "FlyTrapPlant",
            "PrehistoricFossilBitsSmall", "PrehistoricFossilBitsLarge", "KelpSeed",
        };

        private static readonly Dictionary<SubWorld.ZoneType, ZoneConversion> Zones =
            new Dictionary<SubWorld.ZoneType, ZoneConversion>
            {
                // SandstoneStart and the three SandstoneMini rings: GardenStart's bands.
                [SubWorld.ZoneType.Sandstone] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricGarden,
                    Elements = new Dictionary<string, string>
                    {
                        ["SandStone"] = "Shale", ["Dirt"] = "Peat", ["Cuprite"] = "NickelOre", ["Carbon"] = "Fertilizer",
                    },
                    MobTags = GardenTags,
                },
                [SubWorld.ZoneType.Wasteland] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricGarden,
                    Elements = new Dictionary<string, string>
                    {
                        ["MaficRock"] = "Shale", ["IgneousRock"] = "Shale", ["Cuprite"] = "NickelOre",
                        ["SandStone"] = "Peat", ["Sulfur"] = "Fertilizer",
                    },
                    MobTags = GardenTags.Concat(new[] { "Stego" }).ToArray(),
                },
                [SubWorld.ZoneType.ToxicJungle] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricRaptor,
                    Elements = new Dictionary<string, string>
                    {
                        ["Algae"] = "BleachStone", ["Carbon"] = "Granite", ["Hydrogen"] = "Oxygen",
                    },
                    MobTags = RaptorTags,
                },
                // Raptor is DLC4's cold biome (temperatureRange Chilly, brine ice bands).
                [SubWorld.ZoneType.FrozenWastes] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricRaptor,
                    Elements = new Dictionary<string, string>
                    {
                        ["Ice"] = "BrineIce", ["DirtyIce"] = "BrineIce", ["CarbonDioxide"] = "ChlorineGas",
                    },
                    MobTags = RaptorTags,
                },
                [SubWorld.ZoneType.Radioactive] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricRaptor,
                    Elements = new Dictionary<string, string>
                    {
                        ["Ice"] = "BrineIce", ["CarbonDioxide"] = "ChlorineGas",
                    },
                    MobTags = RaptorTags,
                },
                [SubWorld.ZoneType.BoggyMarsh] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricWetlands,
                    Elements = new Dictionary<string, string>
                    {
                        ["Clay"] = "ToxicSand", ["SedimentaryRock"] = "IgneousRock", ["SlimeMold"] = "ToxicSand",
                        ["Algae"] = "ToxicSand", ["ContaminatedOxygen"] = "Oxygen",
                    },
                    MobTags = WetlandsTags,
                },
                [SubWorld.ZoneType.Ocean] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricWetlands,
                    Elements = new Dictionary<string, string>
                    {
                        ["SaltWater"] = "DirtyWater", ["Granite"] = "IgneousRock", ["SedimentaryRock"] = "IgneousRock",
                        ["BleachStone"] = "ToxicSand", ["Hydrogen"] = "Oxygen",
                    },
                    MobTags = WetlandsTags,
                },
                // A tar swamp: the crude oil stays; what will be fossil is still amber.
                [SubWorld.ZoneType.OilField] = new ZoneConversion
                {
                    To = SubWorld.ZoneType.PrehistoricWetlands,
                    Elements = new Dictionary<string, string>
                    {
                        ["Fossil"] = "Amber", ["Granite"] = "IgneousRock",
                    },
                    MobTags = WetlandsTags,
                },
            };

        /// <summary>Zones the conversion leaves as home's: the asteroid's physics, not a biome.</summary>
        public static readonly HashSet<SubWorld.ZoneType> KeptZones = new HashSet<SubWorld.ZoneType>
        {
            SubWorld.ZoneType.MagmaCore, SubWorld.ZoneType.Barren, SubWorld.ZoneType.Space,
        };

        /// <summary>What the worldgen hooks did, for the rig's log. Filled on the worldgen thread.</summary>
        public static readonly StringBuilder Report = new StringBuilder();

        /// <summary>Seconds of GenerateOffline + RenderOffline per world file: how much the twin adds
        /// to a new game's wait.</summary>
        public static readonly Dictionary<string, double> WorldgenSeconds = new Dictionary<string, double>();

        public static int CellsConverted { get; private set; }

        /// <summary>Non-vacuum cells inside the converted zones, the denominator of the visible change.</summary>
        public static int CellsInConvertedZones { get; private set; }

        private static int[] homeSeeds;
        private static bool savingTwin;
        private static bool installed;

        /// <summary>Registers the TIME TRAVEL card and the worldgen hooks.</summary>
        public static void Install(Harmony harmony)
        {
            if (installed)
            {
                return;
            }
            installed = true;
            Playstyle card = new Playstyle();
            card.Id = PlaystyleId;
            card.Title = "TIME TRAVEL";
            card.Description = "Terra, and the same Terra long ago, with dinosaurs. A Time Machine rides between them.";
            card.ClusterCategory = ClusterCategory;
            card.ApplySettings = null;
            PlaystyleRegistry.Register(card);
            PlaystyleRegistry.Install(harmony);

            harmony.Patch(AccessTools.Method(typeof(WorldGen), nameof(WorldGen.Initialise)),
                prefix: new HarmonyMethod(typeof(TimeTwin), nameof(SeedPatch)));
            harmony.Patch(AccessTools.Method(typeof(WorldGenSimUtil), nameof(WorldGenSimUtil.DoSettleSim)),
                prefix: new HarmonyMethod(typeof(TimeTwin), nameof(Convert)),
                finalizer: new HarmonyMethod(typeof(TimeTwin), nameof(SettleDone)));
            harmony.Patch(AccessTools.Constructor(typeof(WorldDetailSave.OverworldCell),
                    new[] { typeof(SubWorld.ZoneType), typeof(TerrainCell) }),
                postfix: new HarmonyMethod(typeof(TimeTwin), nameof(ZonePatch)));
            harmony.Patch(AccessTools.Method(typeof(WorldGen), nameof(WorldGen.GenerateOffline)),
                prefix: new HarmonyMethod(typeof(TimeTwin), nameof(TimeStart)),
                postfix: new HarmonyMethod(typeof(TimeTwin), nameof(TimeStop)));
            harmony.Patch(AccessTools.Method(typeof(WorldGen), nameof(WorldGen.RenderOffline)),
                prefix: new HarmonyMethod(typeof(TimeTwin), nameof(TimeStart)),
                postfix: new HarmonyMethod(typeof(TimeTwin), nameof(TimeStop)));
        }

        public static bool IsWorld(WorldGenSettings settings, string file) =>
            settings?.world?.filePath != null && settings.world.filePath.EndsWith(file, StringComparison.Ordinal);

        /// <summary>Home is generated first (it is placement 0, the start); the twin takes its seeds.</summary>
        private static void SeedPatch(WorldGen __instance, ref int worldSeed, ref int layoutSeed,
            ref int terrainSeed, ref int noiseSeed)
        {
            if (IsWorld(__instance.Settings, HomeWorldFile))
            {
                homeSeeds = new[] { worldSeed, layoutSeed, terrainSeed, noiseSeed };
                Report.AppendLine("home seeds " + string.Join(",", homeSeeds));
            }
            else if (IsWorld(__instance.Settings, TwinWorldFile))
            {
                if (homeSeeds == null)
                {
                    Report.AppendLine("FAIL twin initialised before home: seeds NOT shared");
                    return;
                }
                Report.AppendLine("twin seeds " + worldSeed + "," + layoutSeed + "," + terrainSeed + "," + noiseSeed
                    + " -> home's " + string.Join(",", homeSeeds));
                worldSeed = homeSeeds[0];
                layoutSeed = homeSeeds[1];
                terrainSeed = homeSeeds[2];
                noiseSeed = homeSeeds[3];
            }
        }

        /// <summary>
        /// The conversion, run on the twin only, just before Klei's settle loads the cells into the
        /// sim. Templates (geysers, POIs) are stamped inside the settle, after this, so they stay
        /// exactly as home's.
        /// </summary>
        private static void Convert(WorldGenSettings settings, ref WorldgenSimData simData, Data data)
        {
            if (!IsWorld(settings, TwinWorldFile))
            {
                return;
            }
            savingTwin = true;
            List<Element> elements = ElementLoader.elements;
            Dictionary<(SubWorld.ZoneType, ushort), ushort> swap = new Dictionary<(SubWorld.ZoneType, ushort), ushort>();
            foreach (KeyValuePair<SubWorld.ZoneType, ZoneConversion> zone in Zones)
            {
                foreach (KeyValuePair<string, string> pair in zone.Value.Elements)
                {
                    Element from = ElementLoader.FindElementByName(pair.Key);
                    Element to = ElementLoader.FindElementByName(pair.Value);
                    if (from == null || to == null)
                    {
                        Report.AppendLine("skip " + pair.Key + "->" + pair.Value + ": element missing");
                        continue;
                    }
                    if (from.IsSolid != to.IsSolid || from.IsLiquid != to.IsLiquid || from.IsUnstable != to.IsUnstable)
                    {
                        Report.AppendLine("skip " + pair.Key + "->" + pair.Value + ": phase or stability differs");
                        continue;
                    }
                    swap[(zone.Key, (ushort)elements.IndexOf(from))] = (ushort)elements.IndexOf(to);
                }
            }

            Dictionary<string, int> counts = new Dictionary<string, int>();
            int tooHot = 0, zonesHit = 0;
            HashSet<SubWorld.ZoneType> unmapped = new HashSet<SubWorld.ZoneType>();
            ushort vacuum = (ushort)elements.IndexOf(ElementLoader.FindElementByHash(SimHashes.Vacuum));
            foreach (TerrainCell tc in data.terrainCells)
            {
                SubWorld.ZoneType zoneType;
                try
                {
                    // Klei's own lookup (WorldGen.RenderOffline's biome pass); it throws on a
                    // subworld the world data does not hold, which must not abort the whole worldgen.
                    zoneType = settings.GetSubWorld(tc.node.GetSubworld()).zoneType;
                }
                catch (Exception)
                {
                    continue;
                }
                if (!Zones.TryGetValue(zoneType, out ZoneConversion conv))
                {
                    if (!KeptZones.Contains(zoneType))
                    {
                        unmapped.Add(zoneType);
                    }
                    continue;
                }
                zonesHit++;
                tc.node.biomeSpecificTags = new TagSet(conv.MobTags);
                foreach (int cell in tc.GetAllCells())
                {
                    if (cell >= 0 && cell < simData.cells.Length && simData.cells[cell].elementIdx != vacuum)
                    {
                        CellsInConvertedZones++;
                    }
                    if (cell < 0 || cell >= simData.cells.Length
                        || !swap.TryGetValue((zoneType, simData.cells[cell].elementIdx), out ushort to))
                    {
                        continue;
                    }
                    Element target = elements[to];
                    float t = simData.cells[cell].temperature;
                    // A swap the settle would melt, boil or freeze would move the shape.
                    if (t >= target.highTemp - 5f || t <= target.lowTemp + 5f)
                    {
                        tooHot++;
                        continue;
                    }
                    string key = elements[simData.cells[cell].elementIdx].id + "->" + target.id;
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    simData.cells[cell].SetValues(to, t, simData.cells[cell].mass);
                    CellsConverted++;
                }
            }
            if (unmapped.Count > 0)
            {
                Report.AppendLine("zones with no conversion (kept as home's): " + string.Join(", ", unmapped));
            }
            Report.AppendLine(string.Format("changed {0} of {1} non-vacuum cells in the converted zones ({2:P1})",
                CellsConverted, CellsInConvertedZones, CellsInConvertedZones > 0 ? (float)CellsConverted / CellsInConvertedZones : 0f));
            Report.AppendLine("converted " + CellsConverted + " cells in " + zonesHit + " terrain cells; "
                + tooHot + " kept for temperature; " + string.Join(", ",
                    counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
        }

        private static Exception SettleDone(Exception __exception)
        {
            if (savingTwin)
            {
                Debug.Log("[FxSandbox] prehistoric twin generated: " + CellsConverted + " of " + CellsInConvertedZones
                    + " cells in its biomes converted");
            }
            savingTwin = false;
            return __exception;
        }

        /// <summary>Runs inside the settle's SaveSim, which builds every OverworldCell of the world it saves.</summary>
        private static void ZonePatch(WorldDetailSave.OverworldCell __instance)
        {
            if (savingTwin && Zones.TryGetValue(__instance.zoneType, out ZoneConversion conv))
            {
                __instance.zoneType = conv.To;
            }
        }

        private static void TimeStart(WorldGen __instance, out System.Diagnostics.Stopwatch __state)
        {
            __state = System.Diagnostics.Stopwatch.StartNew();
        }

        private static void TimeStop(WorldGen __instance, System.Diagnostics.Stopwatch __state)
        {
            string file = __instance.Settings?.world?.filePath ?? "?";
            WorldgenSeconds[file] = (WorldgenSeconds.TryGetValue(file, out double s) ? s : 0) + __state.Elapsed.TotalSeconds;
        }

        /// <summary>The home and twin worlds of the loaded game, or null when this is not a twin game.</summary>
        public static bool FindWorlds(out WorldContainer home, out WorldContainer twin)
        {
            home = twin = null;
            foreach (WorldContainer w in ClusterManager.Instance.WorldContainers)
            {
                if (w.worldName != null && w.worldName.EndsWith(TwinWorldFile, StringComparison.Ordinal))
                {
                    twin = w;
                }
                else if (w.worldName != null && w.worldName.EndsWith(HomeWorldFile, StringComparison.Ordinal))
                {
                    home = w;
                }
            }
            return home != null && twin != null;
        }

        /// <summary>The twin's cell at the same place relative to its world as <paramref name="homeCell"/>.</summary>
        public static int TwinCellOf(int homeCell, WorldContainer home, WorldContainer twin)
        {
            Grid.CellToXY(homeCell, out int x, out int y);
            return Grid.XYToCell(x - home.WorldOffset.x + twin.WorldOffset.x, y - home.WorldOffset.y + twin.WorldOffset.y);
        }

        public static void InstallStrings()
        {
            Strings.Add("STRINGS.WORLDS.FXTIMETERRA.NAME", "Terra");
            Strings.Add("STRINGS.WORLDS.FXTIMETERRA.DESCRIPTION", "Home, now.");
            Strings.Add("STRINGS.WORLDS.FXTIMETWINTERRA.NAME", "Prehistoric Terra");
            Strings.Add("STRINGS.WORLDS.FXTIMETWINTERRA.DESCRIPTION", "Home, long ago. Reached only by the time machine.");
            Strings.Add("STRINGS.CLUSTER_NAMES.FXTIMETWIN.NAME", "Terra, Then and Now");
            Strings.Add("STRINGS.CLUSTER_NAMES.FXTIMETWIN.DESCRIPTION", "Terra now, and Terra long ago, joined by the Time Machine.");
            Debug.Log("[FxSandbox] TimeTwin strings installed");
        }
    }
}
