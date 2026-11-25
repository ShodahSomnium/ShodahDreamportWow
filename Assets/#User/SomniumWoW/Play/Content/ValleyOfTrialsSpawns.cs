namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class ValleyOfTrials
    {
        // Spawn points picked on the real terrain by the harness probe BPlace (P/harness/Probes/ContentBGen.cs +
        // ContentBPlace.cs): z is the ADT terrain height at (x, y); each point is in area 363 Valley of Trials, off water, slope
        // <= 30 degrees with no cliff within 3 yards, clear of WMO/M2 collision geometry (the camp's huts and tents, fences,
        // rocks, cacti, the canyon walls) and not under a roof; every wander circle was checked point by point (radii shrunk
        // where the ground did not allow them). Camps grow tougher with distance from the start: mottled boars in the meadows
        // north-east and south of the camp and by the pond; scorpid workers in the northern meadows; dusk scorpids among the
        // north-eastern rocks; vile familiars in the rocky north-west and north; the Burning Blade below their coven cave in
        // the far north, with Vorzul Ashbrand beyond his thugs. NPCs stand outdoors: the elder, the captain and the trainers
        // around the start, the scout on the north path, the watch leader below the coven. NPC orientation faces the start (or
        // what they watch). Re-validated by the probe ContentB.
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
            NpcAt(z, ElderGarosh, -604.0f, -4254.0f, 38.96f, 2.98f); // A intro/final giver
            NpcAt(z, WarbandCaptain, -608.0f, -4267.0f, 38.87f, 2.17f); // B warband captain
            NpcAt(z, ScoutZanji, -431.4f, -4260.6f, 49.06f, 0.12f); // C scout (north)
            NpcAt(z, WatchLeader, -335.0f, -4321.5f, 54.65f, 5.76f); // D watch leader (far north)
            NpcAt(z, WarriorTrainer, -592.0f, -4244.0f, 38.96f, 3.42f); // warrior trainer
            NpcAt(z, HunterTrainer, -589.0f, -4263.0f, 38.23f, 2.78f); // hunter trainer
            NpcAt(z, RogueTrainer, -597.0f, -4279.0f, 38.23f, 2.24f); // rogue trainer
            NpcAt(z, PriestTrainer, -613.0f, -4284.0f, 37.94f, 1.74f); // priest trainer
            NpcAt(z, ShamanTrainer, -580.0f, -4252.0f, 37.93f, 3.13f); // shaman trainer
            NpcAt(z, MageTrainer, -627.0f, -4284.0f, 39.89f, 1.31f); // mage trainer
            NpcAt(z, WarlockTrainer, -601.0f, -4233.0f, 38.13f, 3.96f); // warlock trainer
            NpcAt(z, GruntOne, -638.8f, -4297.8f, 40.86f, 5.76f); // guard (graveyard)
            NpcAt(z, GruntTwo, -570.5f, -4250.0f, 38.25f, 0.00f); // guard (east path)
            NpcAt(z, GruntThree, -420.0f, -4275.0f, 41.06f, 5.98f); // guard (north path)
            Sp(z, MottledBoar, -578.76f, -4303.03f, 38.96f, 8.0f, 45.0f);
            Sp(z, MottledBoar, -554.41f, -4326.74f, 42.18f, 5.3f, 45.0f);
            Sp(z, MottledBoar, -559.39f, -4297.05f, 37.56f, 5.3f, 45.0f);
            Sp(z, MottledBoar, -530.75f, -4294.43f, 37.37f, 8.0f, 45.0f);
            Sp(z, MottledBoar, -569.53f, -4312.47f, 39.76f, 5.3f, 45.0f);
            // ^ Boars A (east meadow)
            Sp(z, MottledBoar, -739.53f, -4228.49f, 38.57f, 3.5f, 45.0f);
            Sp(z, MottledBoar, -741.61f, -4262.33f, 44.09f, 2.3f, 45.0f);
            Sp(z, MottledBoar, -746.66f, -4249.64f, 42.4f, 0.0f, 45.0f);
            Sp(z, MottledBoar, -721.95f, -4259.23f, 41.42f, 0.0f, 45.0f);
            Sp(z, MottledBoar, -745.36f, -4281.31f, 42.85f, 2.3f, 45.0f);
            // ^ Boars B (south meadow)
            Sp(z, MottledBoar, -523.66f, -4332.54f, 41.48f, 0.0f, 45.0f);
            Sp(z, MottledBoar, -499.79f, -4330.35f, 38.62f, 0.0f, 45.0f);
            Sp(z, MottledBoar, -516.86f, -4323.77f, 41.54f, 5.3f, 45.0f);
            Sp(z, MottledBoar, -524.09f, -4326.29f, 41.3f, 3.5f, 45.0f);
            // ^ Boars C (by the pond)
            Sp(z, ScorpidWorker, -456.27f, -4200.03f, 51.55f, 0.0f, 60.0f);
            Sp(z, ScorpidWorker, -451.18f, -4221.79f, 48.97f, 6.0f, 60.0f);
            Sp(z, ScorpidWorker, -475.22f, -4233.94f, 49.23f, 6.0f, 60.0f);
            Sp(z, ScorpidWorker, -469.99f, -4203.84f, 50.86f, 6.0f, 60.0f);
            Sp(z, ScorpidWorker, -478.81f, -4201.68f, 50.38f, 0.0f, 60.0f);
            Sp(z, ScorpidWorker, -465.72f, -4253.26f, 47.36f, 6.0f, 60.0f);
            // ^ Scorpid workers A (north meadow)
            Sp(z, ScorpidWorker, -448.68f, -4134.53f, 51.36f, 6.0f, 60.0f);
            Sp(z, ScorpidWorker, -433.36f, -4159.24f, 52.02f, 2.6f, 60.0f);
            Sp(z, ScorpidWorker, -468.04f, -4162.4f, 50.25f, 1.7f, 60.0f);
            Sp(z, ScorpidWorker, -455.73f, -4148.14f, 51.64f, 2.6f, 60.0f);
            Sp(z, ScorpidWorker, -449.83f, -4165.79f, 46.47f, 6.0f, 60.0f);
            // ^ Scorpid workers B (north-west)
            Sp(z, DuskScorpid, -397.15f, -4373.32f, 39.93f, 6.0f, 60.0f);
            Sp(z, DuskScorpid, -366.36f, -4384.53f, 45.81f, 6.0f, 60.0f);
            Sp(z, DuskScorpid, -407.18f, -4401.39f, 44.16f, 1.7f, 60.0f);
            Sp(z, DuskScorpid, -388.27f, -4415.51f, 44.2f, 0.0f, 60.0f);
            Sp(z, DuskScorpid, -383.95f, -4383.37f, 41.45f, 6.0f, 60.0f);
            Sp(z, DuskScorpid, -404.18f, -4375.12f, 39.77f, 6.0f, 60.0f);
            // ^ Dusk scorpids (north-east rocks)
            Sp(z, VileFamiliar, -332.05f, -4123.75f, 51.03f, 7.0f, 60.0f);
            Sp(z, VileFamiliar, -355.99f, -4148.47f, 52.76f, 4.6f, 60.0f);
            Sp(z, VileFamiliar, -364.29f, -4142.76f, 52.19f, 3.0f, 60.0f);
            Sp(z, VileFamiliar, -326.32f, -4104.14f, 54.01f, 0.0f, 60.0f);
            Sp(z, VileFamiliar, -348.07f, -4151.01f, 52.99f, 4.6f, 60.0f);
            Sp(z, VileFamiliar, -329.83f, -4156.58f, 53.12f, 7.0f, 60.0f);
            // ^ Vile familiars A (north-west)
            Sp(z, VileFamiliar, -273.37f, -4184.79f, 52.54f, 7.0f, 60.0f);
            Sp(z, VileFamiliar, -293.33f, -4205.39f, 51.86f, 2.0f, 60.0f);
            Sp(z, VileFamiliar, -296.57f, -4197.01f, 51.85f, 7.0f, 60.0f);
            Sp(z, VileFamiliar, -268.6f, -4168.45f, 53.75f, 7.0f, 60.0f);
            // ^ Vile familiars B (north)
            Sp(z, BurningBladeNeophyte, -255.39f, -4345.3f, 59.7f, 0.0f, 75.0f);
            Sp(z, BurningBladeNeophyte, -268.83f, -4342.08f, 60.07f, 4.0f, 75.0f);
            Sp(z, BurningBladeNeophyte, -247.79f, -4338.69f, 63.01f, 4.0f, 75.0f);
            Sp(z, BurningBladeNeophyte, -233.39f, -4358.52f, 64.22f, 4.0f, 75.0f);
            Sp(z, BurningBladeNeophyte, -249.94f, -4367.58f, 63.33f, 1.1f, 75.0f);
            // ^ Neophytes (below the coven)
            Sp(z, BurningBladeThug, -273.21f, -4395.81f, 59.39f, 1.4f, 75.0f);
            Sp(z, BurningBladeThug, -285.25f, -4442.88f, 60.48f, 5.0f, 75.0f);
            Sp(z, BurningBladeThug, -286.86f, -4400.34f, 58.66f, 3.3f, 75.0f);
            Sp(z, BurningBladeThug, -280.51f, -4419.57f, 56.25f, 5.0f, 75.0f);
            Sp(z, BurningBladeThug, -264.38f, -4428.47f, 59.02f, 3.3f, 75.0f);
            // ^ Thugs (coven approach)
            Sp(z, VorzulAshbrand, -227.05f, -4409.38f, 63.28f, 2.0f, 90.0f);
            // ^ Burning Blade leader
        }
    }
}
