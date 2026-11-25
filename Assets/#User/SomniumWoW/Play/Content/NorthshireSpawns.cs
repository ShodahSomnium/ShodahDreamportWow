namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Northshire
    {
        // Spawn points picked on the real terrain by the harness probe ZaPlace (P/harness/Probes/ContentAPlace.cs): z is the ADT
        // terrain height at (x, y); each point is in area 9 / 59 / 34 (NPCs 9 or 59), off water, slope <= 30 degrees, clear of
        // WMO/M2 collision geometry (the abbey, its fences and gate, trees, the vineyard trellises, the mine's timbers), and every
        // wander circle was checked point by point (radii shrunk where the ground did not allow them). NPCs stand outdoors in
        // front of the abbey around the start (the trainers too), plus the foreman on the mine road, the vineyard keeper by the
        // bridge and three guards. Camps grow tougher with distance: young wolves in the meadows west and south-west of the abbey
        // (~100 yd), timber wolves in the north meadow (~220 yd), kobold vermin at their camp north-east of the abbey (~160 yd)
        // and laborers at the mine mouth (~235 yd), bandits in the vineyards across the river (~220-280 yd) with their leader at
        // the far end (~305 yd). NPC orientation faces the start (or what they watch). Re-validated by the probe ZaCheck.

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
            NpcAt(z, BrotherAldric, -8943.0f, -120.0f, 82.87f, 4.20f); // Brother Aldric (main giver)
            NpcAt(z, MarshalCorwin, -8956.0f, -125.0f, 83.28f, 5.39f); // Marshal Corwin
            NpcAt(z, ForemanEdda, -8845.0f, -95.0f, 84.08f, 6.15f); // Foreman Edda (mine road)
            NpcAt(z, HollisMarrow, -9000.0f, -318.0f, 73.49f, 3.38f); // Hollis Marrow (vineyards)
            NpcAt(z, WarriorTrainer, -8964.0f, -140.0f, 82.45f, 0.49f); // Warrior trainer
            NpcAt(z, PaladinTrainer, -8962.0f, -154.0f, 81.77f, 1.06f); // Paladin trainer
            NpcAt(z, RogueTrainer, -8927.0f, -120.0f, 82.01f, 3.64f); // Rogue trainer
            NpcAt(z, PriestTrainer, -8955.0f, -165.0f, 81.34f, 1.42f); // Priest trainer
            NpcAt(z, MageTrainer, -8972.0f, -126.0f, 83.67f, 6.00f); // Mage trainer
            NpcAt(z, WarlockTrainer, -8975.0f, -110.0f, 84.56f, 5.55f); // Warlock trainer
            NpcAt(z, GuardEdric, -8936.0f, -150.0f, 82.6f, 2.39f); // Guard (abbey steps)
            NpcAt(z, GuardDunstan, -9020.0f, -70.0f, 88.39f, 2.50f); // Guard (road to the gate)
            NpcAt(z, GuardMara, -8978.0f, -280.0f, 73.04f, 4.42f); // Guard (vineyard bridge)
            Sp(z, YoungWolf, -8898.28f, -27.42f, 90.81f, 2.3f, 45.0f);
            Sp(z, YoungWolf, -8887.96f, -50.01f, 86.86f, 8.0f, 45.0f);
            Sp(z, YoungWolf, -8901.79f, -38.76f, 88.03f, 2.3f, 45.0f);
            Sp(z, YoungWolf, -8921.34f, -63.69f, 88.6f, 5.3f, 45.0f);
            Sp(z, YoungWolf, -8896.62f, -15.86f, 93.5f, 2.3f, 45.0f);
            Sp(z, YoungWolf, -8892.66f, -60.46f, 86.53f, 5.3f, 45.0f);
            // ^ Young Wolf A (W meadow)
            Sp(z, YoungWolf, -8976.86f, -51.62f, 91.52f, 5.3f, 45.0f);
            Sp(z, YoungWolf, -9011.94f, -34.24f, 88.0f, 3.5f, 45.0f);
            Sp(z, YoungWolf, -8970.42f, -42.81f, 90.95f, 5.3f, 45.0f);
            Sp(z, YoungWolf, -8981.68f, -62.33f, 90.73f, 8.0f, 45.0f);
            Sp(z, YoungWolf, -8996.57f, -44.37f, 89.1f, 3.5f, 45.0f);
            // ^ Young Wolf B (SW by the gate road)
            Sp(z, ValleyDeer, -8831.04f, -43.87f, 89.36f, 0.0f, 60.0f);
            Sp(z, ValleyDeer, -8824.59f, -4.53f, 90.31f, 0.0f, 60.0f);
            Sp(z, ValleyDeer, -8816.4f, -29.87f, 90.24f, 4.0f, 60.0f);
            Sp(z, ValleyDeer, -8855.0f, -52.09f, 85.44f, 4.0f, 60.0f);
            // ^ Valley Deer (neutral, meadow N of the abbey)
            Sp(z, TimberWolf, -8763.74f, -2.48f, 97.22f, 0.0f, 60.0f);
            Sp(z, TimberWolf, -8727.03f, -14.15f, 93.87f, 0.0f, 60.0f);
            Sp(z, TimberWolf, -8742.95f, -50.63f, 91.71f, 10.0f, 60.0f);
            Sp(z, TimberWolf, -8760.41f, -9.31f, 94.83f, 4.4f, 60.0f);
            Sp(z, TimberWolf, -8752.36f, -19.64f, 92.36f, 6.6f, 60.0f);
            Sp(z, TimberWolf, -8751.21f, -33.85f, 92.15f, 4.4f, 60.0f);
            Sp(z, TimberWolf, -8737.04f, -56.87f, 91.34f, 10.0f, 60.0f);
            // ^ Timber Wolf (N meadow)
            Sp(z, KoboldVermin, -8811.74f, -175.67f, 81.78f, 6.0f, 60.0f);
            Sp(z, KoboldVermin, -8821.18f, -179.14f, 81.67f, 6.0f, 60.0f);
            Sp(z, KoboldVermin, -8797.91f, -132.58f, 83.28f, 6.0f, 60.0f);
            Sp(z, KoboldVermin, -8789.28f, -208.39f, 83.33f, 6.0f, 60.0f);
            Sp(z, KoboldVermin, -8815.09f, -166.86f, 80.97f, 6.0f, 60.0f);
            Sp(z, KoboldVermin, -8796.24f, -143.89f, 83.45f, 0.0f, 60.0f);
            Sp(z, KoboldVermin, -8764.35f, -196.33f, 85.77f, 4.0f, 60.0f);
            Sp(z, KoboldVermin, -8821.93f, -168.34f, 80.32f, 6.0f, 60.0f);
            // ^ Kobold Vermin (camp)
            Sp(z, KoboldLaborer, -8702.07f, -121.26f, 88.07f, 5.0f, 60.0f);
            Sp(z, KoboldLaborer, -8727.5f, -104.01f, 86.88f, 5.0f, 60.0f);
            Sp(z, KoboldLaborer, -8741.02f, -122.19f, 85.77f, 5.0f, 60.0f);
            Sp(z, KoboldLaborer, -8707.0f, -106.83f, 89.08f, 5.0f, 60.0f);
            Sp(z, KoboldLaborer, -8702.75f, -113.92f, 89.59f, 5.0f, 60.0f);
            Sp(z, KoboldLaborer, -8716.87f, -150.42f, 86.01f, 5.0f, 60.0f);
            // ^ Kobold Laborer (mine mouth)
            Sp(z, VineyardBandit, -9080.8f, -338.91f, 73.45f, 6.0f, 75.0f);
            Sp(z, VineyardBandit, -9040.3f, -320.15f, 73.49f, 6.0f, 75.0f);
            Sp(z, VineyardBandit, -9036.9f, -340.12f, 73.54f, 6.0f, 75.0f);
            Sp(z, VineyardBandit, -9044.19f, -353.94f, 73.65f, 6.0f, 75.0f);
            Sp(z, VineyardBandit, -9057.66f, -351.58f, 73.45f, 6.0f, 75.0f);
            Sp(z, VineyardBandit, -9072.24f, -307.16f, 73.45f, 6.0f, 75.0f);
            Sp(z, VineyardBandit, -9068.95f, -318.53f, 73.45f, 6.0f, 75.0f);
            // ^ Vineyard Bandit (vineyards)
            Sp(z, BanditLookout, -9074.09f, -354.18f, 73.45f, 5.0f, 75.0f);
            Sp(z, BanditLookout, -9083.15f, -349.1f, 73.45f, 5.0f, 75.0f);
            Sp(z, BanditLookout, -9092.4f, -353.52f, 73.45f, 5.0f, 75.0f);
            Sp(z, BanditLookout, -9080.65f, -363.84f, 73.45f, 5.0f, 75.0f);
            Sp(z, BanditLookout, -9073.02f, -389.62f, 73.09f, 1.4f, 75.0f);
            // ^ Bandit Lookout (vineyard far end)
            Sp(z, HarlanCrowe, -9108.35f, -397.71f, 72.95f, 2.0f, 90.0f);
            // ^ Boss bandit (vineyard)
        }
    }
}
