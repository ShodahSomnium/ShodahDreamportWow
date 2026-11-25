using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class AmmenVale
    {
        /// <summary>Where the explore objective of "The Scattered Pods" completes: the escape-pod field west of the road (pod doodads
        /// DR_CRYOPOD_TOP at (-4002.6, -13643.0) and DR_CRYOPOD_BOTTOM at (-4016.4, -13646.4)); terrain height checked by the ContentD probe.</summary>
        public static readonly WowPos PodFieldExplorePos = new WowPos(-4008f, -13652f, 63.08f, 0f);

        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6). Quest XP sums to 3980 for one character (10 quests
        // incl. its class quest); the kills the quests ask for add roughly 2500-3500 more with AzerothCore's kill formula, so finishing
        // everything lands at about level 5.5-6 (the ContentD probe prints the estimate).

        static QuestObjectiveDef Kill(int entry, int count, string text) { return new QuestObjectiveDef { Kind = ObjectiveKind.Kill, TargetEntry = entry, Count = count, Text = text }; }
        static QuestObjectiveDef Collect(int item, int count, string text) { return new QuestObjectiveDef { Kind = ObjectiveKind.Collect, TargetEntry = item, Count = count, Text = text }; }
        static QuestObjectiveDef TalkTo(int npc, string text) { return new QuestObjectiveDef { Kind = ObjectiveKind.TalkTo, TargetEntry = npc, Count = 1, Text = text }; }
        static QuestObjectiveDef Explore(WowPos pos, float radius, string text) { return new QuestObjectiveDef { Kind = ObjectiveKind.Explore, ExplorePos = pos, ExploreRadius = radius, Count = 1, Text = text }; }

        static QuestDef Quest(int id, string title, int level, int prev, int giver, int ender, int xp, int money)
        {
            return new QuestDef { Id = id, Title = title, Level = level, MinLevel = 1, PrevQuestId = prev, GiverEntry = giver, EnderEntry = ender, RewardXp = xp, RewardMoney = money };
        }

        static void AddQuests(ZoneContent z)
        {
            var L = z.Quests;
            QuestDef q;

            // 1. Intro: the Elder at the platform sends you down the road to the artificer.
            q = Quest(QuestWakingAmongWreckage, "Waking Among the Wreckage", 1, 0, ElderTaamun, ArtificerZhaaro, 80, 0);
            q.Description = "You are awake. Good. Many of us are not, and some never will be.\n\nWhen our vessel fell from the sky it scattered us across this vale like seeds on the wind. Those who can stand must help those who cannot. Artificer Zhaaro is gathering the crystals that still hold light - go down the road and put yourself at his service.";
            q.ObjectivesText = "Speak with Artificer Zhaaro on the road below the waking platform.";
            q.ProgressText = "You are one of the sleepers, aren't you? You still have the look.";
            q.CompletionText = "Taamun sent you? Then you arrive at the right moment. The crystals will not gather themselves, and I have only two hands and a great deal of wreckage.";
            q.Objectives.Add(TalkTo(ArtificerZhaaro, "Speak with Artificer Zhaaro"));
            L.Add(q);

            // 2. Kill vale moths (meadows north and west of the road).
            q = Quest(QuestMothsInTheLight, "Moths in the Light", 1, QuestWakingAmongWreckage, ArtificerZhaaro, ArtificerZhaaro, 250, 35);
            q.Description = "The moths of this vale are drawn to the glow of our crystals - and they do not simply flutter around them. They cluster, they chew at the housings, and they swarm anyone who comes to carry the shards away.\n\nThin them out in the meadows around the road. Eight should buy us a quiet night.";
            q.ObjectivesText = "Slay 8 Vale Moths in the meadows around the road, then return to Artificer Zhaaro.";
            q.ProgressText = "Still hearing wings out there. Is it done?";
            q.CompletionText = "Much better. I can hear the crystals humming again instead of that endless fluttering. Here - something from the supply crates, before the moths find those too.";
            q.Objectives.Add(Kill(ValeMoth, 8, "Vale Moth slain"));
            q.RewardItems.Add(4536);
            L.Add(q);

            // 3. Collect root cuttings from the mutated lashers (north-western fields).
            q = Quest(QuestRootsOfTheBlight, "Roots of the Blight", 2, QuestMothsInTheLight, ArtificerZhaaro, ArtificerZhaaro, 350, 60);
            q.Description = "Energy is bleeding out of the broken crystals and into the ground. I can measure it, but I cannot see where it goes. The plants can: the root lashers in the fields to the north-west have twisted into something new since we landed.\n\nCut six samples from their roots and bring them to me. If I can read the damage, perhaps I can stop the bleeding.";
            q.ObjectivesText = "Bring 6 Tainted Root Cuttings from the Mutated Root Lashers in the north-western fields to Artificer Zhaaro.";
            q.ProgressText = "Careful with those cuttings - they still twitch.";
            q.CompletionText = "Look at the veins in this one - pure crystal light, running where sap should be. Worse than I feared. Take a pair of these for your trouble; the fields will be muddy for a while yet.";
            q.Objectives.Add(Collect(ItemRootCutting, 6, "Tainted Root Cutting"));
            q.RewardChoiceItems.Add(23413); q.RewardChoiceItems.Add(23414); q.RewardChoiceItems.Add(23412);
            L.Add(q);

            // 4. Explore the escape-pod field and report to the botanist at the crash site - the start of the chain across the zone
            //    (Elder at the platform -> botanist at the crash site -> vindicator in the south-west -> back to the Elder).
            q = Quest(QuestScatteredPods, "The Scattered Pods", 2, QuestWakingAmongWreckage, ElderTaamun, BotanistNuraeli, 300, 50);
            q.Description = "Not every pod came down near the platform. Some rolled far to the west, past the road, where the ground falls away toward the great wreck.\n\nWalk through the pod field west of the road and see what remains - then go on to Botanist Nuraeli at the edge of the crash site. She asked for news of the pods, and I would rather she heard it from someone who has seen them.";
            q.ObjectivesText = "Search the escape-pod field west of the road, then speak with Botanist Nuraeli at the western edge of the crash site.";
            q.ProgressText = "You came from the pods? Tell me what you saw.";
            q.CompletionText = "Empty, all of them? Then the sleepers either walked away on their own... or something carried them. I will choose to believe the first. Drink this; the walk is longer than it looks.";
            q.Objectives.Add(Explore(PodFieldExplorePos, 12f, "Search the escape-pod field"));
            q.RewardItems.Add(1179);
            L.Add(q);

            // 5. Spore glands from the volatile mutations (western hollows).
            q = Quest(QuestSeedsOfChange, "Seeds of Change", 3, QuestScatteredPods, BotanistNuraeli, BotanistNuraeli, 550, 100);
            q.Description = "The creatures in the hollows west of here were ordinary bog-dwellers when we landed. Now they swell and split and spit spores that glow in the dark. The crash has seeded them with something.\n\nBring me six of their spore glands. If I can learn what they carry, I may be able to keep it out of the seedlings we planted for the survivors.";
            q.ObjectivesText = "Collect 6 Unstable Spore Glands from the Volatile Mutations in the western hollows, then return to Botanist Nuraeli.";
            q.ProgressText = "The glands - did you get them? Please tell me you did not squeeze one.";
            q.CompletionText = "Six, and all intact. You have steady hands. These will take days to study, but already I can see the pattern... Wear these. You will want your wrists covered out there.";
            q.Objectives.Add(Collect(ItemSporeGland, 6, "Unstable Spore Gland"));
            q.RewardChoiceItems.Add(23407); q.RewardChoiceItems.Add(23408); q.RewardChoiceItems.Add(23406);
            L.Add(q);

            // 6. Delivery across the zone: from the crash site (west) to the vindicator (south-west).
            q = Quest(QuestWordToTheWatch, "Word to the Watch", 3, QuestSeedsOfChange, BotanistNuraeli, VindicatorSelaar, 150, 25);
            q.Description = "There is one more thing, and I do not like it. Some of the spore glands were cut open before you ever reached them - neatly, with a blade. Someone else is collecting what the crash left behind.\n\nVindicator Selaar keeps watch to the south-west. Tell her what we found. She will know what it means.";
            q.ObjectivesText = "Speak with Vindicator Selaar in the south-west of the vale.";
            q.ProgressText = "Quietly. What brings you this far from the road?";
            q.CompletionText = "Cut with a blade. So they are bolder than I thought. I have been watching tracks in the ferns for two days - elves, light-footed, and not the kind we trust. Keep this draught close; you are going to need it.";
            q.Objectives.Add(TalkTo(VindicatorSelaar, "Speak with Vindicator Selaar"));
            q.RewardItems.Add(858);
            L.Add(q);

            // 7. Kill the infiltrators in the south-west.
            q = Quest(QuestEyesInTheFerns, "Eyes in the Ferns", 4, QuestWordToTheWatch, VindicatorSelaar, VindicatorSelaar, 550, 120);
            q.Description = "Blood elves. They followed the light of our fall across the sea, and now they creep through the south-west corner of the vale, picking at our wreckage like carrion birds.\n\nI cannot leave this post, and they must not report what they have seen. Drive them out. Seven of them should make the rest think twice.";
            q.ObjectivesText = "Slay 7 Blood Elf Infiltrators in the south-west of the vale, then return to Vindicator Selaar.";
            q.ProgressText = "I can still see them moving in the ferns. Keep at it.";
            q.CompletionText = "Well fought. You move like one who was trained before the fall - or one who learns quickly. Either way, the vale is in your debt. But I did not see their leader among the fallen.";
            q.Objectives.Add(Kill(BloodElfInfiltrator, 7, "Blood Elf Infiltrator slain"));
            q.RewardChoiceItems.Add(23399); q.RewardChoiceItems.Add(23405); q.RewardChoiceItems.Add(23490);
            L.Add(q);

            // 8. Take back the crystal shards the infiltrators carry.
            q = Quest(QuestStolenShards, "The Stolen Shards", 4, QuestEyesInTheFerns, VindicatorSelaar, VindicatorSelaar, 650, 150);
            q.Description = "Every infiltrator you felled carried a pouch of crystal shards - pieces of our vessel, pried out of the wreck while we buried our dead. They mean to study them, or to sell them, or worse.\n\nThose crystals are the only light many of our wounded have left. Bring back five of the stolen shards.";
            q.ObjectivesText = "Recover 5 Stolen Crystal Shards from the Blood Elf Infiltrators, then return to Vindicator Selaar.";
            q.ProgressText = "Do you have them? Every shard matters.";
            q.CompletionText = "Five shards, still warm with our light. Zhaaro will weep when he sees them. And look at the markings scratched on this pouch - a name. Dawnthief. Their leader signs his thefts.";
            q.Objectives.Add(Collect(ItemCrystalShard, 5, "Stolen Crystal Shard"));
            q.RewardChoiceItems.Add(23397); q.RewardChoiceItems.Add(20993); q.RewardChoiceItems.Add(23376);
            L.Add(q);

            // 9. Final: the named thief at the far south-west corner; report back to the Elder at the platform.
            q = Quest(QuestTheDawnthief, "The Dawnthief", 5, QuestStolenShards, VindicatorSelaar, ElderTaamun, 1000, 300);
            q.Description = "The one who calls himself Vaelith Dawnthief waits at the far end of the south-west corner, beyond his scouts, with the largest shards in his keeping. He is no scout - he is a hardened fighter, and he will not flee.\n\nEnd his thieving. Then climb back to the waking platform and tell Elder Taamun that the vale is ours again. He should hear it from you.";
            q.ObjectivesText = "Slay Vaelith Dawnthief in the far south-west of the vale, then report to Elder Taamun at the waking platform.";
            q.ProgressText = "You have the look of someone who has walked a long way. Speak.";
            q.CompletionText = "The thief is fallen, and our crystals are coming home. A few days ago you lay asleep in a broken pod, and today the whole vale owes you its light.\n\nChoose one of these, with the thanks of every survivor. Carry it well - Azuremyst is wide, and it will ask much more of you.";
            q.Objectives.Add(Kill(VaelithDawnthief, 1, "Vaelith Dawnthief slain"));
            q.RewardChoiceItems.Add(23416); q.RewardChoiceItems.Add(23392); q.RewardChoiceItems.Add(23409); q.RewardChoiceItems.Add(23423);
            L.Add(q);

            // 10. One class quest per class: the Elder sends you to your trainer on the road.
            L.Add(ClassQuest(QuestWarriorPath, WowClass.Warrior, "The Warrior's Resolve", WarriorTrainer, "Kordaan",
                "You carried two of the wounded out of the wreck before you even knew your own name. That is the heart of a warrior, and it deserves a teacher.",
                "Taamun sent you, and I can see why. Plant your feet. Good. We will make a wall of you yet - one lesson at a time, as you grow."));
            L.Add(ClassQuest(QuestPaladinPath, WowClass.Paladin, "The Paladin's Oath", PaladinTrainer, "Oraelos",
                "The Light shone from you while you slept - I saw it with my own eyes. There is one among the survivors who can teach you what that means.",
                "Yes... I feel it in you. The Light chose you long before our vessel fell. Stand with me, and I will teach you to carry it for others."));
            L.Add(ClassQuest(QuestHunterPath, WowClass.Hunter, "The Hunter's Eye", HunterTrainer, "Aeleth",
                "Your eyes have not stopped moving since you woke - the treeline, the sky, the path. A hunter's eyes. This new world will need them.",
                "Taamun has a good eye too, it seems. Watch everything, waste nothing, and the creatures of this island will teach you as much as I can."));
            L.Add(ClassQuest(QuestPriestPath, WowClass.Priest, "The Priest's Calling", PriestTrainer, "Ishaara",
                "So many wounded, and you have already knelt beside three of them. The Light listens to such hearts, child. Let someone teach you to answer it.",
                "Welcome. Your hands are gentle and your faith is steady. We will begin with the simplest prayers - the rest will come as you are ready."));
            L.Add(ClassQuest(QuestShamanPath, WowClass.Shaman, "The Shaman's Listening", ShamanTrainer, "Tzuraan",
                "When you woke, the wind turned toward you. The spirits of this island are curious about us, and you, it seems, are curious about them.",
                "Ah. The earth hums a little louder with you standing on it. Good. Be patient with the spirits here - they are frightened of us yet."));
            L.Add(ClassQuest(QuestMagePath, WowClass.Mage, "The Mage's Discipline", MageTrainer, "Zaevor",
                "The loose energy from the wreck bends toward you like water toward a drain. That is a dangerous gift unless someone teaches you to master it.",
                "Taamun was right to send you. You feel the currents already - now learn to shape them rather than drown in them. We start small."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestWakingAmongWreckage, ElderTaamun, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches on the road below the waking platform. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " on the road below the waking platform.";
            q.ProgressText = "Taamun spoke of you.";
            q.CompletionText = completion;
            q.Objectives.Add(TalkTo(trainer, "Speak with " + trainerName));
            return q;
        }

        /// <summary>The quest with this id, or null.</summary>
        public static QuestDef FindQuest(ZoneContent z, int id)
        {
            foreach (var q in z.Quests) if (q.Id == id) return q;
            return null;
        }

        // Class start items = CharStartOutfit.dbc race 11 (identical for both sexes), minus 6948 (hearthstone) and 2512 (arrows).
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 23346, 23473, 23474, 23475 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Paladin, StartItems = new[] { 2361, 23476, 23477 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Hunter, StartItems = new[] { 12282, 23345, 23344, 23348, 23347, 2101 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 3661, 1396, 6097, 23322 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Shaman, StartItems = new[] { 36, 2362, 23345, 23344, 23348 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Mage, StartItems = new[] { 3661, 23478, 23479, 23473 }, StartSpells = new int[0] });
        }
    }
}
