namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class ColdridgeValley
    {
        // Spawn points picked on the real terrain by the harness probe ZaPlace (P/harness/Probes/ContentAPlace.cs): z is the ADT
        // terrain height at (x, y); each point is in area 132 Coldridge Valley, off water, slope <= 30 degrees, clear of WMO/M2
        // collision geometry (Anvilmar, the dig sites' timbers, the troll tents, rocks and pines), and every wander circle was
        // checked point by point (radii shrunk where the ground did not allow them). NPCs stand outdoors in front of Anvilmar
        // around the start (the trainers too), plus the surveyor at the ruined dig, the mountaineer at the western watch and three
        // guards. Camps grow tougher with distance: young snow wolves west and south of the start (~70-100 yd), small crag
        // boars in the valley middle and the south meadow (~160-200 yd), trogg scavengers at the ruined dig (~210 yd), burly
        // troggs at the far digs (~270-350 yd), the Snowbrand trolls' tents (~440-450 yd) and their leader at the ice
        // cave's mouth (~305 yd, south of the boar meadows). NPC orientation faces the start (or what they watch). Re-validated by
        // the probe ZaCheck.

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
            NpcAt(z, CaptainDorgan, -6232.0f, 343.0f, 383.37f, 4.10f); // Captain Dorgan (main giver)
            NpcAt(z, QuartermasterSenna, -6251.4f, 318.6f, 382.8f, 0.84f); // Quartermaster Senna
            NpcAt(z, SurveyorFizzla, -6271.0f, 468.0f, 386.01f, 1.19f); // Surveyor Fizzla (dig)
            NpcAt(z, MountaineerTharna, -6335.0f, 688.0f, 384.73f, 2.31f); // Mountaineer Tharna (western watch)
            NpcAt(z, WarriorTrainer, -6255.0f, 346.0f, 382.43f, 5.49f); // Warrior trainer
            NpcAt(z, PaladinTrainer, -6255.0f, 332.0f, 382.78f, 6.22f); // Paladin trainer
            NpcAt(z, HunterTrainer, -6267.0f, 318.0f, 381.94f, 0.45f); // Hunter trainer
            NpcAt(z, RogueTrainer, -6249.0f, 302.0f, 383.55f, 1.28f); // Rogue trainer
            NpcAt(z, PriestTrainer, -6236.0f, 304.0f, 385.64f, 1.73f); // Priest trainer
            NpcAt(z, MageTrainer, -6225.0f, 312.3f, 384.77f, 2.26f); // Mage trainer
            NpcAt(z, WarlockTrainer, -6274.0f, 355.5f, 379.37f, 5.65f); // Warlock trainer
            NpcAt(z, GuardBorvak, -6222.0f, 358.0f, 384.86f, 2.51f); // Guard (Anvilmar door)
            NpcAt(z, GuardDurm, -6302.0f, 332.0f, 376.52f, 2.30f); // Guard (valley road)
            NpcAt(z, GuardKelda, -6232.0f, 262.0f, 384.73f, 4.58f); // Guard (pass road)
            Sp(z, YoungSnowWolf, -6288.4f, 399.1f, 382.39f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6263.85f, 424.55f, 386.0f, 3.5f, 45.0f);
            Sp(z, YoungSnowWolf, -6275.19f, 408.61f, 382.87f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6267.84f, 408.1f, 383.46f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6289.32f, 425.64f, 383.45f, 5.3f, 45.0f);
            Sp(z, YoungSnowWolf, -6289.57f, 412.65f, 384.17f, 5.3f, 45.0f);
            // ^ Young Snow Wolf A (W of start)
            Sp(z, YoungSnowWolf, -6339.35f, 345.25f, 378.64f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6337.56f, 374.77f, 377.89f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6335.75f, 382.98f, 378.05f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6341.21f, 354.89f, 378.2f, 8.0f, 45.0f);
            Sp(z, YoungSnowWolf, -6322.09f, 340.92f, 376.43f, 8.0f, 45.0f);
            // ^ Young Snow Wolf B (SW of start)
            Sp(z, SmallCragBoar, -6348.28f, 486.62f, 381.65f, 10.0f, 60.0f);
            Sp(z, SmallCragBoar, -6356.74f, 514.98f, 385.31f, 4.4f, 60.0f);
            Sp(z, SmallCragBoar, -6354.87f, 461.94f, 377.98f, 10.0f, 60.0f);
            Sp(z, SmallCragBoar, -6332.51f, 501.43f, 386.04f, 2.9f, 60.0f);
            Sp(z, SmallCragBoar, -6347.84f, 465.78f, 378.81f, 4.4f, 60.0f);
            Sp(z, SmallCragBoar, -6325.21f, 475.15f, 382.13f, 6.6f, 60.0f);
            // ^ Small Crag Boar A (valley middle)
            Sp(z, SmallCragBoar, -6415.67f, 378.36f, 385.13f, 10.0f, 60.0f);
            Sp(z, SmallCragBoar, -6446.17f, 396.24f, 393.11f, 4.4f, 60.0f);
            Sp(z, SmallCragBoar, -6443.85f, 405.05f, 391.85f, 0.0f, 60.0f);
            Sp(z, SmallCragBoar, -6431.37f, 425.21f, 386.65f, 2.9f, 60.0f);
            // ^ Small Crag Boar B (south meadow)
            Sp(z, TroggScavenger, -6262.41f, 539.88f, 386.11f, 6.0f, 60.0f);
            Sp(z, TroggScavenger, -6212.77f, 544.95f, 386.65f, 6.0f, 60.0f);
            Sp(z, TroggScavenger, -6248.98f, 531.18f, 386.16f, 2.6f, 60.0f);
            Sp(z, TroggScavenger, -6224.89f, 568.57f, 387.03f, 6.0f, 60.0f);
            Sp(z, TroggScavenger, -6234.8f, 522.49f, 386.11f, 6.0f, 60.0f);
            Sp(z, TroggScavenger, -6226.04f, 557.78f, 386.62f, 6.0f, 60.0f);
            Sp(z, TroggScavenger, -6256.31f, 523.26f, 386.44f, 4.0f, 60.0f);
            // ^ Trogg Scavenger (ruined dig)
            Sp(z, BurlyTrogg, -6248.86f, 664.14f, 385.14f, 1.7f, 75.0f);
            Sp(z, BurlyTrogg, -6234.51f, 678.03f, 385.22f, 6.0f, 75.0f);
            Sp(z, BurlyTrogg, -6240.53f, 705.3f, 387.22f, 6.0f, 75.0f);
            Sp(z, BurlyTrogg, -6229.46f, 668.91f, 385.27f, 4.0f, 75.0f);
            Sp(z, BurlyTrogg, -6236.24f, 689.22f, 385.63f, 6.0f, 75.0f);
            // ^ Burly Trogg A (far dig, west)
            Sp(z, BurlyTrogg, -6363.82f, 597.55f, 385.36f, 1.7f, 75.0f);
            Sp(z, BurlyTrogg, -6376.64f, 576.25f, 385.79f, 2.6f, 75.0f);
            Sp(z, BurlyTrogg, -6356.31f, 575.39f, 386.19f, 0.0f, 75.0f);
            Sp(z, BurlyTrogg, -6359.81f, 557.85f, 385.65f, 1.7f, 75.0f);
            // ^ Burly Trogg B (far dig, south-west)
            Sp(z, SnowbrandYoungblood, -6380.67f, 763.85f, 386.21f, 4.0f, 75.0f);
            Sp(z, SnowbrandYoungblood, -6430.95f, 770.71f, 389.32f, 1.7f, 75.0f);
            Sp(z, SnowbrandYoungblood, -6425.2f, 764.83f, 387.42f, 4.0f, 75.0f);
            Sp(z, SnowbrandYoungblood, -6375.32f, 751.52f, 386.48f, 6.0f, 75.0f);
            Sp(z, SnowbrandYoungblood, -6430.36f, 757.69f, 387.34f, 6.0f, 75.0f);
            Sp(z, SnowbrandYoungblood, -6408.97f, 747.13f, 390.44f, 6.0f, 75.0f);
            Sp(z, SnowbrandYoungblood, -6398.08f, 754.61f, 390.02f, 4.0f, 75.0f);
            // ^ Snowbrand Youngblood (troll tents)
            Sp(z, SnowbrandSeer, -6505.95f, 670.48f, 387.86f, 0.0f, 75.0f);
            Sp(z, SnowbrandSeer, -6474.2f, 710.7f, 394.01f, 3.3f, 75.0f);
            Sp(z, SnowbrandSeer, -6480.74f, 693.27f, 387.96f, 5.0f, 75.0f);
            Sp(z, SnowbrandSeer, -6474.67f, 692.02f, 388.26f, 5.0f, 75.0f);
            Sp(z, SnowbrandSeer, -6490.2f, 710.31f, 396.09f, 0.0f, 75.0f);
            Sp(z, SnowbrandSeer, -6480.24f, 684.42f, 388.04f, 5.0f, 75.0f);
            // ^ Snowbrand Seer (south tents)
            Sp(z, VakjinTheRimecaller, -6480.0f, 520.0f, 385.85f, 3.0f, 90.0f);
            // ^ Vakjin the Rimecaller (in front of the ice cave, beside the troll gong shrine; moved by hand from the ZaPlace pick
            //   (-6458.3, 548.9) to 51 yd from the cave entrance - the terrain hole at about (-6509, 478) - and re-checked by ZaCheck)
        }
    }
}
