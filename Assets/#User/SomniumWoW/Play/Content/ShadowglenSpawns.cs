namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Shadowglen
    {
        // Spawn points picked on the real terrain by the harness probe ContentPlace (P/harness/Probes/ContentPlace.cs): z is
        // the ADT terrain height at (x, y); each point is in area 188 (NPCs: 188 or 256), off water, slope <= 30 degrees, clear
        // of WMO/M2 collision geometry (Aldrassil's trunk and ramps, trees, rocks, lamps), and every wander circle was checked
        // point by point (radii shrunk where the ground did not allow them). Camps: young nightsabers in the meadows around
        // the start; mangy nightsabers on the eastern slopes; neutral nightsabers in the north meadow by the pools; stags in the
        // western and south-eastern meadows; grellkin and grell in the western glade with Sythrel at its far end; webwood spiders
        // at the mouth of the northern cave (Shadowthread Cave) and in the woods south of it. NPC orientation faces the start
        // (or what they watch). Re-validated by the probe ContentCheck.

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
            NpcAt(z, KeeperLorellen, 10327.0f, 829.0f, 1326.37f, 2.92f); // Elder (main giver)
            NpcAt(z, WardenFaelos, 10322.0f, 848.0f, 1328.03f, 4.11f); // Warden (nightsabers)
            NpcAt(z, TenderAravel, 10693.0f, 781.0f, 1321.11f, 5.44f); // Moonwell keeper
            NpcAt(z, ScoutVeyra, 10366.0f, 986.0f, 1331.18f, 1.29f); // Glade scout
            NpcAt(z, WarriorTrainer, 10350.6f, 870.6f, 1326.52f, 3.91f); // Warrior trainer
            NpcAt(z, HunterTrainer, 10336.0f, 886.0f, 1328.9f, 4.28f); // Hunter trainer
            NpcAt(z, RogueTrainer, 10300.0f, 868.0f, 1335.38f, 5.02f); // Rogue trainer
            NpcAt(z, PriestTrainer, 10356.0f, 780.0f, 1324.62f, 2.28f); // Priest trainer
            NpcAt(z, DruidTrainer, 10338.0f, 772.0f, 1328.24f, 1.99f); // Druid trainer
            NpcAt(z, SentinelAyrith, 10374.0f, 800.0f, 1319.58f, 2.66f); // Sentinel (graveyard)
            NpcAt(z, SentinelMaelis, 10222.0f, 704.0f, 1359.09f, 0.82f); // Sentinel (road out)
            NpcAt(z, SentinelTaelyn, 10640.0f, 790.0f, 1316.28f, 3.00f); // Sentinel (north path)
            Sp(z, YoungNightsaber, 10281.42f, 901.28f, 1335.86f, 8.0f, 45.0f);
            Sp(z, YoungNightsaber, 10277.04f, 874.66f, 1339.92f, 5.3f, 45.0f);
            Sp(z, YoungNightsaber, 10270.83f, 887.39f, 1337.92f, 2.3f, 45.0f);
            Sp(z, YoungNightsaber, 10273.84f, 866.77f, 1341.46f, 2.3f, 45.0f);
            Sp(z, YoungNightsaber, 10262.94f, 892.86f, 1341.34f, 2.3f, 45.0f);
            Sp(z, YoungNightsaber, 10284.1f, 891.7f, 1334.66f, 3.5f, 45.0f);
            // ^ Young Nightsaber A (SW of start)
            Sp(z, YoungNightsaber, 10292.98f, 725.71f, 1335.79f, 8.0f, 45.0f);
            Sp(z, YoungNightsaber, 10263.92f, 732.21f, 1344.21f, 5.3f, 45.0f);
            Sp(z, YoungNightsaber, 10305.41f, 767.51f, 1330.95f, 2.3f, 45.0f);
            Sp(z, YoungNightsaber, 10269.62f, 744.02f, 1342.24f, 0.0f, 45.0f);
            Sp(z, YoungNightsaber, 10283.39f, 748.02f, 1338.17f, 8.0f, 45.0f);
            // ^ Young Nightsaber B (SE of start)
            Sp(z, YoungNightsaber, 10402.26f, 951.51f, 1322.24f, 8.0f, 45.0f);
            Sp(z, YoungNightsaber, 10417.62f, 941.39f, 1319.99f, 8.0f, 45.0f);
            Sp(z, YoungNightsaber, 10380.66f, 939.21f, 1326.05f, 8.0f, 45.0f);
            Sp(z, YoungNightsaber, 10387.18f, 923.4f, 1324.6f, 8.0f, 45.0f);
            // ^ Young Nightsaber C (W of tree)
            Sp(z, MangyNightsaber, 10464.53f, 675.5f, 1323.19f, 2.9f, 60.0f);
            Sp(z, MangyNightsaber, 10448.5f, 685.3f, 1324.33f, 0.0f, 60.0f);
            Sp(z, MangyNightsaber, 10485.2f, 697.93f, 1320.06f, 4.4f, 60.0f);
            Sp(z, MangyNightsaber, 10470.29f, 709.9f, 1319.7f, 6.6f, 60.0f);
            Sp(z, MangyNightsaber, 10472.27f, 678.13f, 1321.86f, 2.9f, 60.0f);
            Sp(z, MangyNightsaber, 10486.92f, 659.22f, 1322.82f, 10.0f, 60.0f);
            // ^ Mangy Nightsaber A (E)
            Sp(z, MangyNightsaber, 10589.6f, 724.57f, 1319.18f, 6.6f, 60.0f);
            Sp(z, MangyNightsaber, 10609.74f, 718.92f, 1320.81f, 2.9f, 60.0f);
            Sp(z, MangyNightsaber, 10569.0f, 708.41f, 1318.67f, 10.0f, 60.0f);
            Sp(z, MangyNightsaber, 10576.82f, 713.41f, 1318.58f, 10.0f, 60.0f);
            Sp(z, MangyNightsaber, 10591.84f, 702.87f, 1322.11f, 2.9f, 60.0f);
            // ^ Mangy Nightsaber B (NE)
            Sp(z, Nightsaber, 10650.09f, 860.6f, 1315.71f, 0.0f, 75.0f);
            Sp(z, Nightsaber, 10620.31f, 856.51f, 1312.27f, 0.0f, 75.0f);
            Sp(z, Nightsaber, 10635.86f, 879.72f, 1316.44f, 7.9f, 75.0f);
            Sp(z, Nightsaber, 10645.74f, 875.12f, 1317.53f, 5.2f, 75.0f);
            // ^ Nightsaber (neutral, N meadow)
            Sp(z, ShadowglenStag, 10525.53f, 959.27f, 1317.06f, 15.0f, 60.0f);
            Sp(z, ShadowglenStag, 10516.36f, 970.46f, 1317.99f, 15.0f, 60.0f);
            Sp(z, ShadowglenStag, 10513.9f, 953.49f, 1316.83f, 9.9f, 60.0f);
            // ^ Stag (neutral, W meadow)
            Sp(z, ShadowglenStag, 10546.34f, 715.12f, 1317.27f, 9.9f, 60.0f);
            Sp(z, ShadowglenStag, 10573.96f, 732.86f, 1316.14f, 6.5f, 60.0f);
            // ^ Stag (neutral, SE meadow)
            Sp(z, Grellkin, 10355.13f, 1049.62f, 1344.24f, 2.0f, 60.0f);
            Sp(z, Grellkin, 10338.18f, 1032.3f, 1339.18f, 0.0f, 60.0f);
            Sp(z, Grellkin, 10356.58f, 1042.26f, 1342.55f, 0.0f, 60.0f);
            Sp(z, Grellkin, 10337.02f, 1022.63f, 1337.76f, 7.0f, 60.0f);
            Sp(z, Grellkin, 10348.55f, 1022.18f, 1337.18f, 7.0f, 60.0f);
            Sp(z, Grellkin, 10321.94f, 1039.13f, 1339.32f, 0.0f, 60.0f);
            // ^ Grellkin (W glade)
            Sp(z, Grell, 10440.32f, 1068.42f, 1354.83f, 0.0f, 75.0f);
            Sp(z, Grell, 10432.81f, 1064.89f, 1358.21f, 0.0f, 75.0f);
            Sp(z, Grell, 10435.7f, 1059.42f, 1357.9f, 3.0f, 75.0f);
            Sp(z, Grell, 10450.55f, 1067.17f, 1354.84f, 0.0f, 75.0f);
            Sp(z, Grell, 10426.69f, 1061.57f, 1358.39f, 0.0f, 75.0f);
            // ^ Grell (W glade, deeper)
            Sp(z, WebwoodSpider, 10737.45f, 907.81f, 1334.03f, 2.6f, 75.0f);
            Sp(z, WebwoodSpider, 10715.18f, 930.31f, 1327.94f, 6.0f, 75.0f);
            Sp(z, WebwoodSpider, 10737.11f, 890.86f, 1331.76f, 2.6f, 75.0f);
            Sp(z, WebwoodSpider, 10731.45f, 916.39f, 1332.49f, 6.0f, 75.0f);
            Sp(z, WebwoodSpider, 10745.78f, 894.04f, 1334.93f, 1.7f, 75.0f);
            Sp(z, WebwoodSpider, 10732.01f, 938.99f, 1332.29f, 6.0f, 75.0f);
            // ^ Webwood Spider A (cave mouth)
            Sp(z, WebwoodSpider, 10702.44f, 829.68f, 1322.81f, 6.0f, 75.0f);
            Sp(z, WebwoodSpider, 10714.15f, 832.66f, 1324.56f, 6.0f, 75.0f);
            Sp(z, WebwoodSpider, 10719.3f, 839.75f, 1325.49f, 6.0f, 75.0f);
            Sp(z, WebwoodSpider, 10709.67f, 847.47f, 1324.81f, 6.0f, 75.0f);
            // ^ Webwood Spider B (N woods)
            Sp(z, SythrelTheDefiler, 10484.13f, 1095.14f, 1341.45f, 2.0f, 90.0f);
            // ^ Boss satyr (end of the glade)
        }
    }
}
