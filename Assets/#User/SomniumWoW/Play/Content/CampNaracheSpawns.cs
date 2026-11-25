namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class CampNarache
    {
        // Spawn points picked on the real terrain by the harness probe NarachePlace (P/harness/Probes/ContentC.cs): z is the ADT
        // terrain height at (x, y); every creature stands in area 220 Red Cloud Mesa (NPCs: 220 or 221 Camp Narache), off water,
        // slope <= 30 degrees, clear of WMO/M2 collision geometry (huts, windbreak fences, pyres, lamps, kiln, drums), and every
        // wander circle was checked point by point (radii shrunk where the ground did not allow them). Camps, growing tougher
        // with distance from the start: plainstriders in the grass north-east, east and south of the camp (100-120 yd); neutral
        // kodo calves in the north-east meadow; mountain cougars on the western and south-western slopes (170-270 yd);
        // battleboars on the southern plains (270-400 yd); the Bristleback quilboar camp on the eastern plain west and south of
        // Kodo Rock (350-420 yd) with Chief Bramblehide at its far south-eastern end (480 yd). NPCs face the start (or what they
        // watch). Re-validated by the probe NaracheCheck.

        /// <summary>Where the explore objective of "The View from the Rim" completes: just inside the windbreak fence on the northern
        /// rim of the mesa, above the Mulgore lowland; terrain height 55.64, area 220.</summary>
        public static readonly WowPos RimExplorePos = new WowPos(-2800f, -330f, 55.64f, 0f);

        static void NpcAt(ZoneContent z, int entry, float x, float y, float h, float o)
        {
            z.Spawns.Add(new SpawnDef { Entry = entry, Pos = new WowPos(x, y, h, o), WanderRadius = 0f, RespawnSeconds = 60f });
        }

        static void Sp(ZoneContent z, int entry, float x, float y, float h, float wander, float respawn)
        {
            // Deterministic facing from the position (data only, no randomness at load).
            float o = (x * 7.31f + y * 3.17f) % 6.2831853f;
            if (o < 0f) o += 6.2831853f;
            z.Spawns.Add(new SpawnDef { Entry = entry, Pos = new WowPos(x, y, h, o), WanderRadius = wander, RespawnSeconds = respawn });
        }

        static void AddSpawns(ZoneContent z)
        {
            NpcAt(z, ElderTayanka, -2906.0f, -262.0f, 52.96f, 2.81f); // Elder (main giver)
            NpcAt(z, HuntLeaderWahkeen, -2912.0f, -276.0f, 53.92f, 1.87f); // Hunt Leader
            NpcAt(z, KeeperAponi, -2936.0f, -168.0f, 64.13f, 1.98f); // Keeper of the Pyres
            NpcAt(z, ScoutTala, -2955.0f, -505.0f, 44.81f, 4.09f); // Eastern Watch scout
            NpcAt(z, WarriorTrainer, -2888.0f, -262.0f, 53.72f, 3.01f); // Warrior trainer
            NpcAt(z, HunterTrainer, -2928.0f, -185.0f, 60.7f, 5.86f); // Hunter trainer
            NpcAt(z, ShamanTrainer, -2955.0f, -272.0f, 54.08f, 0.36f); // Shaman trainer
            NpcAt(z, DruidTrainer, -2872.0f, -282.0f, 53.95f, 2.66f); // Druid trainer
            NpcAt(z, BraveAhiga, -2855.0f, -262.0f, 54.11f, 0.00f); // Brave (north)
            NpcAt(z, BraveSakima, -2978.0f, -265.0f, 52.16f, 2.76f); // Brave (south road)
            NpcAt(z, BraveOnawa, -2960.0f, -300.0f, 56.89f, 3.66f); // Brave (south-east)
            Sp(z, Plainstrider, -2852.09f, -356.17f, 52.76f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -2868.87f, -353.58f, 53.73f, 3.5f, 45.0f);
            Sp(z, Plainstrider, -2877.43f, -349.68f, 54.89f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -2835.42f, -350.62f, 54.85f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -2866.28f, -318.57f, 54.95f, 8.0f, 45.0f);
            // ^ Plainstrider A (NE of camp)
            Sp(z, Plainstrider, -2942.64f, -394.57f, 57.32f, 2.3f, 45.0f);
            Sp(z, Plainstrider, -2943.41f, -352.51f, 56.08f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -2964.45f, -393.47f, 55.64f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -2920.17f, -376.65f, 56.33f, 5.3f, 45.0f);
            Sp(z, Plainstrider, -2955.31f, -379.32f, 57.34f, 0.0f, 45.0f);
            // ^ Plainstrider B (E of camp)
            Sp(z, Plainstrider, -3005.54f, -262.78f, 50.24f, 2.3f, 45.0f);
            Sp(z, Plainstrider, -3008.57f, -239.29f, 49.06f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -3007.07f, -232.91f, 49.85f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -3036.87f, -237.43f, 49.26f, 8.0f, 45.0f);
            Sp(z, Plainstrider, -3025.7f, -247.19f, 49.66f, 8.0f, 45.0f);
            // ^ Plainstrider C (S of camp)
            Sp(z, KodoCalf, -2881.52f, -504.28f, 45.94f, 12.0f, 60.0f);
            Sp(z, KodoCalf, -2870.59f, -485.37f, 50.42f, 12.0f, 60.0f);
            Sp(z, KodoCalf, -2867.53f, -470.69f, 52.34f, 12.0f, 60.0f);
            Sp(z, KodoCalf, -2893.76f, -469.82f, 45.96f, 12.0f, 60.0f);
            // ^ Kodo Calf (neutral, NE meadow)
            Sp(z, MountainCougar, -3022.28f, -139.6f, 68.91f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3061.15f, -139.57f, 52.6f, 0.0f, 60.0f);
            Sp(z, MountainCougar, -3055.23f, -152.21f, 56.46f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3040.54f, -147.11f, 62.18f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3050.19f, -159.89f, 56.92f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3050.11f, -138.47f, 57.38f, 0.0f, 60.0f);
            // ^ Mountain Cougar A (W slopes)
            Sp(z, MountainCougar, -3136.79f, -112.13f, 45.81f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3131.95f, -142.75f, 43.54f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3140.92f, -96.54f, 45.6f, 5.3f, 60.0f);
            Sp(z, MountainCougar, -3153.92f, -100.81f, 48.41f, 8.0f, 60.0f);
            Sp(z, MountainCougar, -3149.23f, -135.67f, 46.9f, 8.0f, 60.0f);
            // ^ Mountain Cougar B (SW slopes)
            Sp(z, Battleboar, -3194.92f, -343.37f, 32.34f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3170.87f, -319.53f, 38.24f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3180.94f, -324.76f, 36.77f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3152.57f, -334.17f, 37.96f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3178.49f, -331.83f, 35.89f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3171.65f, -343.73f, 33.8f, 10.0f, 60.0f);
            // ^ Battleboar A (S plains)
            Sp(z, Battleboar, -3232.11f, -450.69f, 25.31f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3255.64f, -438.03f, 29.86f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3265.06f, -449.56f, 31.43f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3270.04f, -444.92f, 32.33f, 10.0f, 60.0f);
            Sp(z, Battleboar, -3245.41f, -440.71f, 26.47f, 10.0f, 60.0f);
            // ^ Battleboar B (far S plains)
            Sp(z, BristlebackQuilboar, -3005.16f, -610.72f, 33.51f, 5.0f, 75.0f);
            Sp(z, BristlebackQuilboar, -3013.9f, -594.87f, 34.33f, 5.0f, 75.0f);
            Sp(z, BristlebackQuilboar, -3007.36f, -598.02f, 35.3f, 5.0f, 75.0f);
            Sp(z, BristlebackQuilboar, -3011.64f, -579.19f, 34.23f, 5.0f, 75.0f);
            // ^ Bristleback Quilboar A
            Sp(z, BristlebackMystic, -2986.31f, -608.82f, 34.94f, 3.0f, 75.0f);
            Sp(z, BristlebackMystic, -3016.75f, -608.88f, 32.15f, 3.0f, 75.0f);
            // ^ Bristleback Mystic A
            Sp(z, BristlebackQuilboar, -3091.06f, -618.66f, 34.55f, 5.0f, 75.0f);
            Sp(z, BristlebackQuilboar, -3085.93f, -629.81f, 36.09f, 5.0f, 75.0f);
            Sp(z, BristlebackQuilboar, -3090.24f, -660.08f, 35.12f, 5.0f, 75.0f);
            Sp(z, BristlebackQuilboar, -3110.67f, -653.22f, 33.91f, 5.0f, 75.0f);
            // ^ Bristleback Quilboar B
            Sp(z, BristlebackMystic, -3080.16f, -651.71f, 34.41f, 3.0f, 75.0f);
            Sp(z, BristlebackMystic, -3078.75f, -627.58f, 35.24f, 3.0f, 75.0f);
            Sp(z, BristlebackMystic, -3106.52f, -619.55f, 34.16f, 3.0f, 75.0f);
            // ^ Bristleback Mystic B
            Sp(z, ChiefBramblehide, -3115.0f, -693.52f, 29.45f, 3.0f, 90.0f);
            // ^ Chief Bramblehide (SE end)
        }
    }
}
