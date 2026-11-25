namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class SunstriderIsle
    {
        // Spawn points picked on the real terrain by the harness probe ContentDPlace (P/harness/Probes/ContentDPlace.cs): z is the ADT
        // terrain height at (x, y); each point is in area 3431 Sunstrider Isle, off water, slope <= 30 degrees, clear of WMO/M2
        // collision geometry and not under a roof, and every wander circle was checked point by point (radii shrunk where the ground did
        // not allow them). Layout: the magister, the ranger, a guardian and the six trainers on the plaza around the start (between the
        // spire's buildings); a guardian by the graveyard to the north-east and one on the path south; mana wyrms on the three lawns
        // around the plaza; neutral lynx cubs on the western slope; Eversong lynxes in the southern bowl around the old ring; arcane
        // wraiths south-east (on the hill above the arcanist) and south; tainted arcane wraiths in the south-west hollow near the
        // lieutenant, with Magistrix Velanya at its far end. NPC orientation faces the start (or what they watch). Re-validated by the
        // probe ContentD.

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
            NpcAt(z, MagisterAerondis, 10365.0f, -6358.0f, 33.85f, 3.10f); // Magister (start)
            NpcAt(z, RangerTalvaria, 10338.0f, -6362.0f, 33.04f, 0.39f); // Ranger (start)
            NpcAt(z, ArcanistVeyselle, 10177.5f, -6383.0f, 38.53f, 4.78f); // Arcanist (SE)
            NpcAt(z, LieutenantDathrien, 10190.0f, -6110.0f, 15.74f, 1.86f); // Lieutenant (SW)
            NpcAt(z, PaladinTrainer, 10380.0f, -6335.0f, 33.67f, 3.77f); // Paladin trainer
            NpcAt(z, HunterTrainer, 10325.0f, -6370.0f, 32.64f, 0.48f); // Hunter trainer
            NpcAt(z, RogueTrainer, 10330.0f, -6380.0f, 35.21f, 0.86f); // Rogue trainer
            NpcAt(z, PriestTrainer, 10385.0f, -6348.0f, 34.49f, 3.40f); // Priest trainer
            NpcAt(z, MageTrainer, 10388.0f, -6362.0f, 36.08f, 3.02f); // Mage trainer
            NpcAt(z, WarlockTrainer, 10315.0f, -6385.0f, 32.92f, 0.68f); // Warlock trainer
            NpcAt(z, GuardianLorathil, 10345.0f, -6372.0f, 35.83f, 2.13f); // Guard (start)
            NpcAt(z, GuardianFaeleth, 10446.0f, -6347.0f, 35.57f, 3.25f); // Guard (graveyard)
            NpcAt(z, GuardianIrindel, 10200.0f, -6300.0f, 24.11f, 0.00f); // Guard (south path)
            Sp(z, ManaWyrm, 10319.49f, -6259.3f, 28.43f, 8.0f, 45.0f);
            Sp(z, ManaWyrm, 10328.53f, -6275.69f, 26.86f, 8.0f, 45.0f);
            Sp(z, ManaWyrm, 10299.23f, -6258.28f, 27.76f, 8.0f, 45.0f);
            Sp(z, ManaWyrm, 10338.97f, -6278.26f, 24.83f, 8.0f, 45.0f);
            Sp(z, ManaWyrm, 10310.41f, -6238.74f, 27.73f, 8.0f, 45.0f);
            Sp(z, ManaWyrm, 10313.92f, -6277.65f, 27.76f, 8.0f, 45.0f);
            // ^ Mana Wyrm A (W lawn)
            Sp(z, ManaWyrm, 10430.76f, -6275.66f, 34.46f, 2.3f, 45.0f);
            Sp(z, ManaWyrm, 10433.57f, -6247.74f, 36.15f, 0.0f, 45.0f);
            Sp(z, ManaWyrm, 10424.91f, -6238.6f, 34.09f, 0.0f, 45.0f);
            Sp(z, ManaWyrm, 10435.29f, -6239.39f, 36.63f, 0.0f, 45.0f);
            Sp(z, ManaWyrm, 10418.03f, -6245.23f, 33.07f, 8.0f, 45.0f);
            // ^ Mana Wyrm B (N lawn)
            Sp(z, ManaWyrm, 10252.98f, -6250.74f, 20.58f, 0.0f, 45.0f);
            Sp(z, ManaWyrm, 10252.76f, -6212.01f, 21.08f, 5.3f, 45.0f);
            Sp(z, ManaWyrm, 10246.31f, -6246.63f, 21.01f, 2.3f, 45.0f);
            Sp(z, ManaWyrm, 10246.44f, -6220.81f, 18.92f, 0.0f, 45.0f);
            // ^ Mana Wyrm C (S lawn)
            Sp(z, LynxCub, 10369.61f, -6135.87f, 29.53f, 10.0f, 60.0f);
            Sp(z, LynxCub, 10352.2f, -6088.58f, 22.57f, 10.0f, 60.0f);
            Sp(z, LynxCub, 10357.63f, -6085.28f, 22.78f, 6.6f, 60.0f);
            Sp(z, LynxCub, 10365.71f, -6118.61f, 26.27f, 2.9f, 60.0f);
            Sp(z, LynxCub, 10353.45f, -6128.37f, 25.98f, 2.9f, 60.0f);
            // ^ Lynx Cub (neutral, W)
            Sp(z, EversongLynx, 10178.14f, -6183.15f, 21.23f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10204.69f, -6167.47f, 19.29f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10184.78f, -6187.38f, 20.66f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10192.6f, -6197.91f, 18.42f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10185.68f, -6180.69f, 21.25f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10189.07f, -6228.85f, 14.56f, 9.0f, 60.0f);
            // ^ Eversong Lynx A (S bowl)
            Sp(z, EversongLynx, 10129.03f, -6280.07f, 14.9f, 5.9f, 60.0f);
            Sp(z, EversongLynx, 10142.57f, -6312.92f, 16.28f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10152.55f, -6293.36f, 15.97f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10126.19f, -6312.08f, 14.69f, 9.0f, 60.0f);
            Sp(z, EversongLynx, 10144.81f, -6319.47f, 16.43f, 9.0f, 60.0f);
            // ^ Eversong Lynx B (S bowl)
            Sp(z, ArcaneWraith, 10140.64f, -6410.11f, 73.18f, 0.0f, 75.0f);
            Sp(z, ArcaneWraith, 10147.93f, -6409.47f, 70.71f, 0.0f, 75.0f);
            Sp(z, ArcaneWraith, 10135.21f, -6403.03f, 75.7f, 0.0f, 75.0f);
            Sp(z, ArcaneWraith, 10145.99f, -6420.48f, 73.19f, 0.0f, 75.0f);
            Sp(z, ArcaneWraith, 10141.27f, -6404.06f, 74.98f, 0.0f, 75.0f);
            // ^ Arcane Wraith A (SE)
            Sp(z, ArcaneWraith, 10072.34f, -6341.18f, 5.2f, 6.0f, 75.0f);
            Sp(z, ArcaneWraith, 10089.98f, -6339.06f, 6.94f, 6.0f, 75.0f);
            Sp(z, ArcaneWraith, 10085.09f, -6363.43f, 7.66f, 2.6f, 75.0f);
            Sp(z, ArcaneWraith, 10067.84f, -6361.31f, 3.99f, 4.0f, 75.0f);
            Sp(z, ArcaneWraith, 10084.05f, -6371.45f, 8.99f, 1.7f, 75.0f);
            // ^ Arcane Wraith B (S)
            Sp(z, TaintedArcaneWraith, 10163.47f, -6075.41f, -0.15f, 4.0f, 75.0f);
            Sp(z, TaintedArcaneWraith, 10120.1f, -6090.71f, -0.88f, 2.6f, 75.0f);
            Sp(z, TaintedArcaneWraith, 10119.86f, -6084.47f, -2.14f, 0.0f, 75.0f);
            Sp(z, TaintedArcaneWraith, 10160.4f, -6105.81f, 7.29f, 6.0f, 75.0f);
            Sp(z, TaintedArcaneWraith, 10152.9f, -6063.52f, -1.34f, 4.0f, 75.0f);
            Sp(z, TaintedArcaneWraith, 10155.83f, -6088.26f, 1.98f, 4.0f, 75.0f);
            // ^ Tainted Wraith (SW)
            Sp(z, MagistrixVelanya, 10092.66f, -6032.43f, -8.46f, 3.0f, 90.0f);
            // ^ Exiled magister (boss)
        }
    }
}
