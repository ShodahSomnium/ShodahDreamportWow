using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>The kind of country a zone is, for its critters and birds (ours: the files do not say which animals live where).</summary>
    public enum WowClimate : byte { Forest, Snow, Plains, Dry, Swamp, Plague, Jungle }

    /// <summary>One kind of critter: its name, the CreatureDisplayInfo ids it may wear, walk speed, health.</summary>
    public sealed class CritterKind
    {
        public readonly int Index;          // position in WowCritters.All (entry = OfflineSession.CritterEntryBase + Index)
        public readonly string Name;
        public readonly int[] DisplayIds;
        public readonly float WalkSpeed;    // yd/s: the model's Walk (AnimationData 4) MoveSpeed, measured
        public readonly int Health;         // ours: a few hit points (one or two hits)
        public readonly bool Amphibian;     // frogs/toads: preferred near water

        public CritterKind(int index, string name, int[] displays, float walk, int health, bool amphibian)
        {
            Index = index; Name = name; DisplayIds = displays; WalkSpeed = walk; Health = health; Amphibian = amphibian;
        }
    }

    /// <summary>
    /// WoW-style critters (pure data, used by OfflineSession.Critters): which critters a zone gets. Display ids MEASURED on the
    /// player's files 2026-09-27 (harness Probes/Critters CritModels/CritFind/CritSizes: CreatureModelData row by model file name
    /// -> CreatureDisplayInfo rows; the .m2, its 00.skin and the skin BLP exist; Stand 0 / Walk 4 / Run 5 / Death 1 carry data;
    /// Walk MoveSpeed 2.5 yd/s, the sheep 1.11). The prairie dog's files are spelled "PrarieDog". Not used: Stag 1917 (a 6 yd
    /// elk) and Scorpion 2485 (1.8 yd, a scorpid). The zone -> climate table (moved here from the Details' CritterTable) and
    /// the mix per climate are ours; unknown zones count as forest.
    /// </summary>
    public static class WowCritters
    {
        // CreatureDisplayInfo ids (model; skin)
        public const int RabbitBrown = 4626, RabbitWhite = 328;          // Creature\Rabbit\Rabbit; RabbitSkinBrown / RabbitSkinwhite
        public const int Squirrel = 134, SquirrelDiseased = 10090;       // Creature\Squirrel\Squirrel (scale 1.3)
        public const int Deer = 347, DeerDiseased = 10091;               // Creature\Deer\Deer
        public const int FrogGreen = 6295, FrogBrown = 901;              // Creature\Frog\Frog; FrogGreen / FrogBrownSpots
        public const int RatBlack = 1141, RatBrown = 1418;               // Creature\Rat\Rat
        public const int Chicken = 304;                                  // Creature\Chicken\Chicken
        public const int Sheep = 856;                                    // Creature\Sheep\Sheep
        public const int SnakeYellowBlack = 1987, SnakeGreenYellow = 1986, SnakeGreen = 2958;   // Creature\snake\snake
        public const int Cockroach = 2177;                               // Creature\cockroach\Cockroach
        public const int PrairieDog = 1072;                              // Creature\PrarieDog\PrarieDog

        private static readonly List<CritterKind> _all = new List<CritterKind>();

        public static readonly CritterKind Rabbit = Add("Rabbit", new[] { RabbitBrown }, 2.5f, 5, false);
        public static readonly CritterKind SnowshoeRabbit = Add("Snowshoe Rabbit", new[] { RabbitWhite }, 2.5f, 5, false);
        public static readonly CritterKind SquirrelKind = Add("Squirrel", new[] { Squirrel }, 2.5f, 5, false);
        public static readonly CritterKind DeerKind = Add("Deer", new[] { Deer }, 2.5f, 8, false);
        public static readonly CritterKind Toad = Add("Toad", new[] { FrogBrown }, 2.5f, 5, true);
        public static readonly CritterKind Frog = Add("Frog", new[] { FrogGreen }, 2.5f, 5, true);
        public static readonly CritterKind Rat = Add("Rat", new[] { RatBlack, RatBrown }, 2.5f, 5, false);
        public static readonly CritterKind ChickenKind = Add("Chicken", new[] { Chicken }, 2.5f, 5, false);
        public static readonly CritterKind SheepKind = Add("Sheep", new[] { Sheep }, 1.11f, 8, false);
        public static readonly CritterKind Adder = Add("Adder", new[] { SnakeYellowBlack }, 2.5f, 5, false);
        public static readonly CritterKind WaterSnake = Add("Water Snake", new[] { SnakeGreenYellow }, 2.5f, 5, false);
        public static readonly CritterKind Snake = Add("Snake", new[] { SnakeGreen, SnakeYellowBlack }, 2.5f, 5, false);
        public static readonly CritterKind CockroachKind = Add("Cockroach", new[] { Cockroach }, 2.5f, 5, false);
        public static readonly CritterKind PrairieDogKind = Add("Prairie Dog", new[] { PrairieDog }, 2.5f, 5, false);
        public static readonly CritterKind DiseasedSquirrel = Add("Diseased Squirrel", new[] { SquirrelDiseased }, 2.5f, 5, false);
        public static readonly CritterKind DiseasedDeer = Add("Diseased Deer", new[] { DeerDiseased }, 2.5f, 8, false);

        /// <summary>Every kind; CritterKind.Index is the position here.</summary>
        public static IReadOnlyList<CritterKind> All { get { return _all; } }

        private static CritterKind Add(string name, int[] displays, float walk, int health, bool amphibian)
        {
            var k = new CritterKind(_all.Count, name, displays, walk, health, amphibian);
            _all.Add(k);
            return k;
        }

        // ------------------------------------------------------------------ zones (AreaTable ids with ParentAreaID 0; Details report)

        private static readonly Dictionary<int, WowClimate> Zones = new Dictionary<int, WowClimate>
        {
            { 12, WowClimate.Forest }, { 141, WowClimate.Forest }, { 148, WowClimate.Forest }, { 331, WowClimate.Forest }, { 10, WowClimate.Forest },
            { 267, WowClimate.Forest }, { 36, WowClimate.Forest }, { 3430, WowClimate.Forest }, { 3524, WowClimate.Forest }, { 3525, WowClimate.Forest },
            { 44, WowClimate.Forest }, { 38, WowClimate.Forest }, { 45, WowClimate.Forest }, { 406, WowClimate.Forest },
            { 1, WowClimate.Snow }, { 618, WowClimate.Snow },
            { 40, WowClimate.Plains }, { 215, WowClimate.Plains },
            { 14, WowClimate.Dry }, { 17, WowClimate.Dry }, { 400, WowClimate.Dry }, { 405, WowClimate.Dry }, { 3, WowClimate.Dry }, { 440, WowClimate.Dry },
            { 8, WowClimate.Swamp }, { 15, WowClimate.Swamp }, { 11, WowClimate.Swamp },
            { 85, WowClimate.Plague }, { 130, WowClimate.Plague }, { 3433, WowClimate.Plague },
            { 33, WowClimate.Jungle },
        };

        // land critters per climate (repeats = weight); frogs/toads are added near water (Amphibian)
        private static readonly CritterKind[] ForestLand = { Rabbit, Rabbit, SquirrelKind, SquirrelKind, DeerKind };
        private static readonly CritterKind[] SnowLand = { SnowshoeRabbit, SnowshoeRabbit, SquirrelKind };
        private static readonly CritterKind[] PlainsLand = { PrairieDogKind, PrairieDogKind, Rabbit, ChickenKind, SheepKind };
        private static readonly CritterKind[] DryLand = { Adder, Adder, PrairieDogKind, PrairieDogKind, Rabbit };
        private static readonly CritterKind[] SwampLand = { Toad, Toad, Frog, WaterSnake, Rat };
        private static readonly CritterKind[] PlagueLand = { Rat, Rat, CockroachKind, DiseasedSquirrel, DiseasedDeer };
        private static readonly CritterKind[] JungleLand = { Snake, Snake, SquirrelKind, Frog };

        public static WowClimate ClimateOf(int zoneId)
        {
            WowClimate c;
            return Zones.TryGetValue(zoneId, out c) ? c : WowClimate.Forest;
        }

        /// <summary>The land critters of a climate (repeats = weight).</summary>
        public static CritterKind[] Land(WowClimate c)
        {
            switch (c)
            {
                case WowClimate.Snow: return SnowLand;
                case WowClimate.Plains: return PlainsLand;
                case WowClimate.Dry: return DryLand;
                case WowClimate.Swamp: return SwampLand;
                case WowClimate.Plague: return PlagueLand;
                case WowClimate.Jungle: return JungleLand;
                default: return ForestLand;
            }
        }

        /// <summary>The frog/toad of a climate for spots near water, or null (snow, dry, plague: none).</summary>
        public static CritterKind NearWater(WowClimate c)
        {
            switch (c)
            {
                case WowClimate.Forest: return Toad;
                case WowClimate.Swamp: return Toad;
                case WowClimate.Plains: return Frog;
                case WowClimate.Jungle: return Frog;
                default: return null;
            }
        }

        /// <summary>A critter for a spot: near water 40 % the climate's frog/toad; frogs/toads only near water. r0, r1: uniform random numbers in [0, 1).</summary>
        public static CritterKind Pick(WowClimate c, bool nearWater, double r0, double r1)
        {
            var wet = NearWater(c);
            if (nearWater && wet != null && r0 < 0.4) return wet;
            var land = Land(c);
            int i = Math.Min(land.Length - 1, (int)(r1 * land.Length));
            if (!nearWater && land[i].Amphibian && c != WowClimate.Swamp)
            {
                for (int k = 1; k < land.Length; k++)
                {
                    var alt = land[(i + k) % land.Length];
                    if (!alt.Amphibian) return alt;
                }
            }
            return land[i];
        }

        /// <summary>AreaTable Flags bit of the capitals: MEASURED set on all ten (Stormwind City, Ironforge, Darnassus, Orgrimmar,
        /// Thunder Bluff, Undercity, Silvermoon City, The Exodar, Shattrath City, Dalaran) and on 25 of their subzones plus Acherus
        /// (35 of 2307 rows), on no open-country zone.</summary>
        public const int AreaFlagCapital = 0x100;

        /// <summary>True when the area or one of its parents is a capital city (no critters there: the streets are WMO floors above the terrain).</summary>
        public static bool IsCity(DbcTable areaTable, int areaId)
        {
            if (areaTable == null || areaId <= 0) return false;
            int id = areaId;
            for (int guard = 0; guard < 8 && id > 0; guard++)
            {
                int r = areaTable.FindRow(id);
                if (r < 0) return false;
                if ((areaTable.GetInt(r, DbcFields.AreaTable.Flags) & AreaFlagCapital) != 0) return true;
                id = areaTable.GetInt(r, DbcFields.AreaTable.ParentAreaID);
            }
            return false;
        }

        /// <summary>The top zone (ParentAreaID chain up to 0) of an MCNK area id; 0 when unknown. AreaTable: 0 ID, 2 ParentAreaID.</summary>
        public static int ZoneOf(DbcTable areaTable, int areaId)
        {
            if (areaTable == null || areaId <= 0) return 0;
            int id = areaId;
            for (int guard = 0; guard < 8; guard++)
            {
                int r = areaTable.FindRow(id);
                if (r < 0) return guard == 0 ? 0 : id;
                int parent = areaTable.GetInt(r, DbcFields.AreaTable.ParentAreaID);
                if (parent <= 0) return id;
                id = parent;
            }
            return id;
        }
    }
}
