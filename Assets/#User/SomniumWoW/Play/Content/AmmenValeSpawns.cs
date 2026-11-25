namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class AmmenVale
    {
        // Spawn points picked on the real terrain by the harness probe ContentDPlace (P/harness/Probes/ContentDPlace.cs): z is the ADT
        // terrain height at (x, y); each point is in area 3526 Ammen Vale (NPCs: 3526 or 3527 Crash Site), off water, slope <= 30
        // degrees, clear of WMO/M2 collision geometry and not under a roof, and every wander circle was checked point by point (radii
        // shrunk where the ground did not allow them). Layout: the Elder and a peacekeeper just below the waking platform (the start);
        // the artificer and the six trainers along the lamp-lit road that runs down from the platform towards the crash site; vale moths
        // in the meadows north and west of the road; mutated root lashers in the north-western fields; volatile mutations in the western
        // hollows near the botanist at the crash site; blood elf infiltrators in the south-west corner with Vaelith Dawnthief at its far
        // end. NPC orientation faces the start (or what they watch). Re-validated by the probe ContentD.

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
            NpcAt(z, ElderTaamun, -3972.0f, -13898.0f, 97.14f, 5.01f); // Elder (start)
            NpcAt(z, ArtificerZhaaro, -4003.0f, -13848.0f, 87.43f, 5.17f); // Artificer (road)
            NpcAt(z, BotanistNuraeli, -4130.0f, -13648.0f, 74.76f, 4.85f); // Botanist (crash W)
            NpcAt(z, VindicatorSelaar, -4200.0f, -13690.0f, 70.41f, 2.60f); // Vindicator (SW)
            NpcAt(z, WarriorTrainer, -4012.0f, -13832.0f, 82.73f, 5.24f); // Warrior trainer
            NpcAt(z, PaladinTrainer, -4020.0f, -13842.0f, 86.36f, 5.53f); // Paladin trainer
            NpcAt(z, HunterTrainer, -3995.0f, -13832.0f, 84.12f, 4.84f); // Hunter trainer
            NpcAt(z, PriestTrainer, -3987.0f, -13851.0f, 85.64f, 3.10f); // Priest trainer
            NpcAt(z, ShamanTrainer, -4022.0f, -13815.0f, 78.71f, 5.24f); // Shaman trainer
            NpcAt(z, MageTrainer, -3992.0f, -13868.0f, 90.42f, 2.36f); // Mage trainer
            NpcAt(z, PeacekeeperVoraan, -3978.0f, -13912.0f, 98.88f, 0.72f); // Peacekeeper (start)
            NpcAt(z, PeacekeeperMaadun, -4112.0f, -13655.0f, 74.67f, 0.28f); // Peacekeeper (crash)
            NpcAt(z, PeacekeeperIlaari, -4035.0f, -13785.0f, 74.46f, 3.06f); // Peacekeeper (road)
            Sp(z, ValeMoth, -3948.9f, -13841.34f, 82.94f, 5.3f, 45.0f);
            Sp(z, ValeMoth, -3941.96f, -13841.48f, 82.65f, 5.3f, 45.0f);
            Sp(z, ValeMoth, -3942.92f, -13834.36f, 82.41f, 8.0f, 45.0f);
            Sp(z, ValeMoth, -3931.18f, -13825.18f, 82.2f, 8.0f, 45.0f);
            Sp(z, ValeMoth, -3953.58f, -13832.66f, 81.55f, 8.0f, 45.0f);
            Sp(z, ValeMoth, -3911.45f, -13855.2f, 89.11f, 5.3f, 45.0f);
            // ^ Vale Moth A (N of start)
            Sp(z, ValeMoth, -3910.11f, -13779.09f, 73.38f, 0.0f, 45.0f);
            Sp(z, ValeMoth, -3943.91f, -13799.3f, 65.37f, 0.0f, 45.0f);
            Sp(z, ValeMoth, -3954.63f, -13797.68f, 65.99f, 0.0f, 45.0f);
            Sp(z, ValeMoth, -3934.75f, -13777.78f, 66.24f, 3.5f, 45.0f);
            Sp(z, ValeMoth, -3947.76f, -13780.74f, 65.38f, 8.0f, 45.0f);
            // ^ Vale Moth B (NW meadow)
            Sp(z, ValeMoth, -3971.23f, -13762.62f, 67.55f, 8.0f, 45.0f);
            Sp(z, ValeMoth, -3994.35f, -13752.15f, 67.6f, 8.0f, 45.0f);
            Sp(z, ValeMoth, -3984.4f, -13744.87f, 65.49f, 5.3f, 45.0f);
            Sp(z, ValeMoth, -3979.47f, -13755.3f, 65.64f, 8.0f, 45.0f);
            // ^ Vale Moth C (W of road)
            Sp(z, MutatedRootLasher, -3855.88f, -13624.37f, 61.21f, 3.3f, 60.0f);
            Sp(z, MutatedRootLasher, -3898.12f, -13660.02f, 62.92f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3889.41f, -13658.39f, 62.46f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3877.75f, -13678.78f, 64.34f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3873.9f, -13643.0f, 63.32f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3863.23f, -13619.98f, 60.91f, 5.0f, 60.0f);
            // ^ Root Lasher A (N)
            Sp(z, MutatedRootLasher, -3927.32f, -13594.38f, 59.17f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3973.35f, -13589.38f, 53.8f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3934.44f, -13570.47f, 55.66f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3961.04f, -13579.91f, 55.3f, 5.0f, 60.0f);
            Sp(z, MutatedRootLasher, -3962.8f, -13552.92f, 53.69f, 5.0f, 60.0f);
            // ^ Root Lasher B (NW)
            Sp(z, VolatileMutation, -4026.11f, -13619.11f, 54.45f, 7.0f, 75.0f);
            Sp(z, VolatileMutation, -4054.8f, -13623.04f, 55.82f, 4.6f, 75.0f);
            Sp(z, VolatileMutation, -4022.04f, -13608.04f, 54.96f, 3.0f, 75.0f);
            Sp(z, VolatileMutation, -4054.26f, -13578.33f, 52.01f, 3.0f, 75.0f);
            Sp(z, VolatileMutation, -4023.87f, -13595.94f, 54.99f, 2.0f, 75.0f);
            // ^ Volatile Mutation A (W)
            Sp(z, VolatileMutation, -4142.76f, -13576.47f, 53.0f, 7.0f, 75.0f);
            Sp(z, VolatileMutation, -4101.73f, -13569.28f, 51.88f, 2.0f, 75.0f);
            Sp(z, VolatileMutation, -4140.82f, -13568.13f, 51.23f, 0.0f, 75.0f);
            Sp(z, VolatileMutation, -4135.23f, -13577.92f, 52.63f, 7.0f, 75.0f);
            Sp(z, VolatileMutation, -4134.35f, -13585.97f, 54.42f, 7.0f, 75.0f);
            // ^ Volatile Mutation B (W2)
            Sp(z, BloodElfInfiltrator, -4237.76f, -13605.89f, 51.89f, 1.7f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4227.1f, -13595.8f, 50.92f, 4.0f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4210.12f, -13600.44f, 52.19f, 4.0f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4221.38f, -13634.61f, 55.53f, 4.0f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4232.06f, -13642.06f, 53.67f, 1.7f, 75.0f);
            // ^ Infiltrator A (SW)
            Sp(z, BloodElfInfiltrator, -4273.32f, -13716.51f, 53.5f, 4.0f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4245.59f, -13710.37f, 58.19f, 4.0f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4251.7f, -13703.74f, 57.53f, 2.6f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4265.46f, -13724.8f, 56.93f, 0.0f, 75.0f);
            Sp(z, BloodElfInfiltrator, -4260.04f, -13682.94f, 53.01f, 4.0f, 75.0f);
            // ^ Infiltrator B (S)
            Sp(z, VaelithDawnthief, -4275.95f, -13644.32f, 50.9f, 3.0f, 90.0f);
            // ^ Dawnthief (boss)
        }
    }
}
