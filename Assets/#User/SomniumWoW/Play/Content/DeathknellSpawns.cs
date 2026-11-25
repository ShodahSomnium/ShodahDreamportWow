namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Deathknell
    {
        // Spawn points picked on the real terrain by the harness probe BPlace (P/harness/Probes/ContentBGen.cs +
        // ContentBPlace.cs): z is the ADT terrain height at (x, y); each point is in area 154 Deathknell, off water, slope
        // <= 30 degrees with no cliff within 3 yards, clear of WMO/M2 collision geometry (the crypt mausoleum, the chapel and
        // houses, fences, gravestones, trees) and not under a roof; every wander circle was checked point by point (radii
        // shrunk where the ground did not allow them). Camps grow tougher with distance from the crypt: mindless zombies on the
        // graveyard slopes just below it; duskbats in the meadows east and south-east of the chapel; young spiders under Night Web's Hollow and the
        // Night Web spiders at its mouth; skeletons and ghouls in the northern fields; the Scarlet camp on the north-eastern ridge
        // with the inquisitor at its far end. NPCs stand outdoors: the priestess, the captain and the trainers on the crypt
        // forecourt, the apothecary by the chapel, the executor on the east road. NPC orientation faces the crypt (or what
        // they watch). Re-validated by the probe ContentB.
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
            NpcAt(z, ShadowPriestessVessra, 1655.0f, 1645.0f, 141.35f, 1.57f); // A intro/final giver
            NpcAt(z, CaptainDorrin, 1687.0f, 1668.0f, 134.43f, 3.84f); // B deathguard captain
            NpcAt(z, ApothecaryMerrow, 1876.0f, 1612.0f, 94.08f, 2.90f); // C apothecary (chapel)
            NpcAt(z, ExecutorHarrow, 1950.0f, 1470.0f, 77.56f, 5.33f); // D executor (east road)
            NpcAt(z, WarriorTrainer, 1689.1f, 1647.2f, 134.29f, 2.57f); // warrior trainer
            NpcAt(z, RogueTrainer, 1688.9f, 1686.1f, 136.56f, 4.24f); // rogue trainer
            NpcAt(z, PriestTrainer, 1679.9f, 1642.9f, 138.49f, 1.97f); // priest trainer
            NpcAt(z, MageTrainer, 1696.0f, 1662.0f, 131.46f, 3.37f); // mage trainer
            NpcAt(z, WarlockTrainer, 1684.0f, 1678.0f, 137.21f, 4.27f); // warlock trainer
            NpcAt(z, DeathguardOne, 1647.9f, 1646.5f, 141.16f, 5.35f); // guard (crypt door)
            NpcAt(z, DeathguardTwo, 1705.0f, 1625.0f, 122.88f, 5.86f); // guard (path down)
            NpcAt(z, DeathguardThree, 1868.0f, 1600.0f, 93.76f, 3.14f); // guard (chapel)
            Sp(z, MindlessZombie, 1765.83f, 1610.21f, 111.1f, 4.0f, 45.0f);
            Sp(z, MindlessZombie, 1726.11f, 1588.41f, 117.87f, 1.7f, 45.0f);
            Sp(z, MindlessZombie, 1753.01f, 1614.07f, 113.33f, 6.0f, 45.0f);
            Sp(z, MindlessZombie, 1763.02f, 1599.69f, 109.9f, 6.0f, 45.0f);
            Sp(z, MindlessZombie, 1766.21f, 1592.32f, 110.13f, 4.0f, 45.0f);
            // ^ Zombies A (graveyard slope)
            Sp(z, MindlessZombie, 1750.77f, 1668.34f, 121.16f, 4.0f, 45.0f);
            Sp(z, MindlessZombie, 1774.76f, 1662.82f, 115.83f, 1.7f, 45.0f);
            Sp(z, MindlessZombie, 1734.56f, 1684.07f, 126.35f, 6.0f, 45.0f);
            Sp(z, MindlessZombie, 1752.07f, 1660.49f, 120.48f, 2.6f, 45.0f);
            Sp(z, MindlessZombie, 1762.2f, 1697.42f, 121.65f, 6.0f, 45.0f);
            // ^ Zombies B (graveyard field)
            Sp(z, MindlessZombie, 1776.77f, 1656.27f, 114.09f, 6.0f, 45.0f);
            Sp(z, MindlessZombie, 1794.37f, 1622.13f, 109.47f, 0.0f, 45.0f);
            Sp(z, MindlessZombie, 1771.77f, 1637.45f, 111.77f, 0.0f, 45.0f);
            Sp(z, MindlessZombie, 1779.59f, 1647.09f, 112.11f, 2.6f, 45.0f);
            // ^ Zombies C (below the crypt hill)
            Sp(z, Duskbat, 1793.77f, 1469.55f, 98.11f, 0.0f, 60.0f);
            Sp(z, Duskbat, 1807.61f, 1495.92f, 91.73f, 2.9f, 60.0f);
            Sp(z, Duskbat, 1827.2f, 1481.15f, 90.03f, 10.0f, 60.0f);
            Sp(z, Duskbat, 1817.79f, 1493.56f, 90.3f, 10.0f, 60.0f);
            Sp(z, Duskbat, 1790.55f, 1506.39f, 99.69f, 4.4f, 60.0f);
            Sp(z, Duskbat, 1811.47f, 1457.77f, 87.97f, 10.0f, 60.0f);
            // ^ Duskbats A (south-east meadow)
            Sp(z, Duskbat, 1885.63f, 1425.35f, 73.02f, 4.4f, 60.0f);
            Sp(z, Duskbat, 1899.13f, 1469.98f, 84.15f, 4.4f, 60.0f);
            Sp(z, Duskbat, 1864.18f, 1441.17f, 76.73f, 10.0f, 60.0f);
            Sp(z, Duskbat, 1901.67f, 1459.13f, 82.0f, 10.0f, 60.0f);
            // ^ Duskbats B (east of the village)
            Sp(z, RestlessSkeleton, 2052.52f, 1562.86f, 77.73f, 0.0f, 60.0f);
            Sp(z, RestlessSkeleton, 2023.67f, 1557.54f, 79.08f, 8.0f, 60.0f);
            Sp(z, RestlessSkeleton, 2016.43f, 1563.24f, 78.9f, 8.0f, 60.0f);
            Sp(z, RestlessSkeleton, 2066.61f, 1547.17f, 76.26f, 5.3f, 60.0f);
            Sp(z, RestlessSkeleton, 2016.65f, 1551.76f, 79.06f, 8.0f, 60.0f);
            Sp(z, RestlessSkeleton, 2045.87f, 1543.59f, 78.17f, 5.3f, 60.0f);
            // ^ Skeletons (north fields)
            Sp(z, WretchedGhoul, 2037.62f, 1643.77f, 71.16f, 8.0f, 60.0f);
            Sp(z, WretchedGhoul, 2045.95f, 1626.14f, 71.1f, 0.0f, 60.0f);
            Sp(z, WretchedGhoul, 2061.84f, 1613.11f, 69.53f, 8.0f, 60.0f);
            Sp(z, WretchedGhoul, 2051.22f, 1659.81f, 74.06f, 8.0f, 60.0f);
            Sp(z, WretchedGhoul, 2045.54f, 1644.19f, 70.78f, 8.0f, 60.0f);
            // ^ Ghouls (north-west fields)
            Sp(z, YoungNightWebSpider, 2006.78f, 1704.37f, 78.96f, 6.0f, 60.0f);
            Sp(z, YoungNightWebSpider, 1980.29f, 1717.44f, 84.23f, 0.0f, 60.0f);
            Sp(z, YoungNightWebSpider, 1992.17f, 1719.13f, 83.08f, 0.0f, 60.0f);
            Sp(z, YoungNightWebSpider, 2012.41f, 1697.52f, 78.2f, 6.0f, 60.0f);
            Sp(z, YoungNightWebSpider, 1970.05f, 1704.54f, 81.22f, 4.0f, 60.0f);
            Sp(z, YoungNightWebSpider, 1998.33f, 1697.26f, 78.69f, 6.0f, 60.0f);
            // ^ Young spiders (below the hollow)
            Sp(z, NightWebSpider, 2100.31f, 1701.97f, 66.6f, 4.0f, 75.0f);
            Sp(z, NightWebSpider, 2079.45f, 1706.0f, 68.72f, 6.0f, 75.0f);
            Sp(z, NightWebSpider, 2090.89f, 1727.33f, 68.47f, 6.0f, 75.0f);
            Sp(z, NightWebSpider, 2064.67f, 1696.73f, 72.44f, 6.0f, 75.0f);
            Sp(z, NightWebSpider, 2087.76f, 1693.51f, 68.11f, 6.0f, 75.0f);
            // ^ Night Web spiders (hollow mouth)
            Sp(z, ScarletConvert, 1991.05f, 1379.77f, 62.2f, 3.3f, 75.0f);
            Sp(z, ScarletConvert, 2022.27f, 1381.67f, 60.49f, 5.0f, 75.0f);
            Sp(z, ScarletConvert, 1988.85f, 1392.94f, 62.03f, 3.3f, 75.0f);
            Sp(z, ScarletConvert, 1997.36f, 1402.5f, 61.97f, 5.0f, 75.0f);
            Sp(z, ScarletConvert, 1988.82f, 1420.25f, 62.1f, 5.0f, 75.0f);
            Sp(z, ScarletConvert, 1981.98f, 1418.17f, 62.79f, 5.0f, 75.0f);
            // ^ Scarlet converts (east camp)
            Sp(z, ScarletInitiate, 2070.01f, 1387.29f, 59.96f, 4.0f, 75.0f);
            Sp(z, ScarletInitiate, 2046.94f, 1383.03f, 58.8f, 4.0f, 75.0f);
            Sp(z, ScarletInitiate, 2041.14f, 1387.59f, 58.61f, 4.0f, 75.0f);
            Sp(z, ScarletInitiate, 2081.29f, 1374.74f, 58.86f, 4.0f, 75.0f);
            Sp(z, ScarletInitiate, 2041.32f, 1378.41f, 58.99f, 4.0f, 75.0f);
            // ^ Scarlet initiates (east camp, deeper)
            Sp(z, InquisitorMarrenVale, 2105.92f, 1398.65f, 61.2f, 2.0f, 90.0f);
            // ^ Scarlet commander (far east)
        }
    }
}
