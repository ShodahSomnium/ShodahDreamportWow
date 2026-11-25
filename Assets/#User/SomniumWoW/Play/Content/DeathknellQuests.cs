using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Deathknell
    {
        /// <summary>Where the explore objective of "Watchers on the Ridge" completes: below the Scarlet camp on the north-eastern ridge,
        /// terrain height 65.60, area 154 (harness probe BAt: slope 4 degrees).</summary>
        public static readonly WowPos RidgeExplorePos = new WowPos(1960f, 1430f, 65.6f, 0f);

        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6, 7600 in total to level 6). Quest XP sums to
        // 4280 for one character (11 quests incl. its class quest); the kills the quests ask for give about 3000 more with
        // AzerothCore's kill formula (L*5+45 at equal level), so finishing everything lands at about level 5.5-6.
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

            // 1. Intro: the priestess on the crypt forecourt sends you to the Deathguard captain.
            q = Quest(QuestRisenAgain, "Risen Again", 1, 0, ShadowPriestessVessra, CaptainDorrin, 80, 0);
            q.Description = "You climbed the stairs on your own two feet. Not everyone who wakes in that crypt manages as much.\n\nYou will have questions - who you were, why you are standing, why your heart no longer beats. Those answers will come in their own time. For now, what you need is purpose, and Captain Dorrin Ashgrave has more work than hands. Find him above the crypt and tell him I sent you.";
            q.ObjectivesText = "Speak with Dorrin Ashgrave above the crypt.";
            q.ProgressText = "Still here? The captain will not wait all night.";
            q.CompletionText = "Vessra sent you? Then you have already passed the first test - you did not run screaming into the fields. Welcome to the Deathguard's watch.";
            q.Objectives.Add(TalkTo(CaptainDorrin, "Speak with Dorrin Ashgrave"));
            L.Add(q);

            // 2. Kill mindless zombies on the graveyard slopes just below the crypt.
            q = Quest(QuestTheMindless, "The Mindless", 1, QuestRisenAgain, CaptainDorrin, CaptainDorrin, 250, 35);
            q.Description = "Not everyone who rises comes back with their mind intact. The ones who do not wander the slopes below this crypt, clawing at anything that moves - including the newly awakened like you.\n\nThey were our people once. There is no cure for them and no mercy in leaving them as they are. Go down to the graveyard and put eight of them to rest.";
            q.ObjectivesText = "Slay 8 Mindless Zombies on the graveyard slopes below the crypt, then return to Dorrin Ashgrave.";
            q.ProgressText = "The slopes are still crawling with them. Is it done?";
            q.CompletionText = "Clean work. It does not get easier, but it gets quicker. Take some bread - you no longer need it, but old habits comfort the mind.";
            q.Objectives.Add(Kill(MindlessZombie, 8, "Mindless Zombie slain"));
            q.RewardItems.Add(4540);
            L.Add(q);

            // 3. Delivery: carry the captain's report down to the apothecary at the chapel (chain: captain -> apothecary -> executor).
            q = Quest(QuestReportToTheChapel, "Report to the Chapel", 2, QuestTheMindless, CaptainDorrin, ApothecaryMerrow, 150, 25);
            q.Description = "The apothecary Merrow Vantis has set up his work beside the old chapel, down the hill to the north. He has been asking for a count of the mindless we have put down - something about the state of the bodies.\n\nTake him my report. And do not touch anything on his tables.";
            q.ObjectivesText = "Take Dorrin Ashgrave's report to Merrow Vantis beside the chapel.";
            q.ProgressText = "Yes? You have something for me?";
            q.CompletionText = "The captain's count - excellent. Eight more, and every one of them still rotting at the usual rate. Fascinating. You may be useful to me, messenger.";
            q.Objectives.Add(TalkTo(ApothecaryMerrow, "Deliver the report to Merrow Vantis"));
            L.Add(q);

            // 4. Collect duskbat wings (the meadows east of the chapel).
            q = Quest(QuestWingsInTheDusk, "Wings in the Dusk", 2, QuestReportToTheChapel, ApothecaryMerrow, ApothecaryMerrow, 400, 60);
            q.Description = "The duskbats in the meadows east of the chapel carry something in their blood - a resistance to rot that I would very much like to understand. Their wings hold most of it.\n\nBring me six duskbat wings. Try not to tear the membranes; torn wings are useless to me.";
            q.ObjectivesText = "Bring 6 Duskbat Wings to Merrow Vantis.";
            q.ProgressText = "The wings - do you have them? Intact, I hope.";
            q.CompletionText = "Six, and barely a tear among them. You have steadier hands than most of the living I have worked with. Take one of these; they were stitched for the Deathguard.";
            q.Objectives.Add(Collect(ItemDuskbatWing, 6, "Duskbat Wing"));
            q.RewardChoiceItems.Add(2957); q.RewardChoiceItems.Add(2961); q.RewardChoiceItems.Add(2965);
            L.Add(q);

            // 5. Collect silk glands from the spiders below Night Web's Hollow (north).
            q = Quest(QuestSilkForTheApothecary, "Silk for the Apothecary", 3, QuestWingsInTheDusk, ApothecaryMerrow, ApothecaryMerrow, 550, 100);
            q.Description = "North of the village, the spiders of Night Web's Hollow have spilled out of their cave and spun their webs across the fields. Their silk glands hold a binding agent I need for my tonics.\n\nGather six glands from the Night Web spiders - the young ones below the hollow will do, but the grown ones at its mouth carry more. Stay out of the cave itself.";
            q.ObjectivesText = "Collect 6 Night Web Silk Glands from the spiders north of the village, then return to Merrow Vantis.";
            q.ProgressText = "The tonics will not bind themselves. Have you gathered the glands?";
            q.CompletionText = "Perfect. The tonic will hold for weeks with these. Here - a pair of boots for someone who spends so much time wading through webs.";
            q.Objectives.Add(Collect(ItemNightWebSilk, 6, "Night Web Silk Gland"));
            q.RewardChoiceItems.Add(2959); q.RewardChoiceItems.Add(2963); q.RewardChoiceItems.Add(2967);
            L.Add(q);

            // 6. Delivery: the apothecary sends you on to the executor on the east road.
            q = Quest(QuestTheExecutorsSummons, "The Executor's Summons", 3, QuestSilkForTheApothecary, ApothecaryMerrow, ExecutorHarrow, 150, 25);
            q.Description = "Executor Harrow Blackmere keeps watch on the east road, where the fields give way to the ridge. He sent word that he needs someone capable, and you have proven more capable than most.\n\nFollow the road east past the chapel. He will not be hard to find - he is the one glaring at the horizon.";
            q.ObjectivesText = "Speak with Harrow Blackmere on the east road.";
            q.ProgressText = "Merrow sent you? Speak quickly.";
            q.CompletionText = "The apothecary vouches for you. That is rare. Very well - I have work that needs doing, and none of it is pleasant.";
            q.Objectives.Add(TalkTo(ExecutorHarrow, "Speak with Harrow Blackmere"));
            q.RewardItems.Add(118);
            L.Add(q);

            // 7. Kill skeletons and ghouls in the northern fields.
            q = Quest(QuestBonesThatWalk, "Bones That Walk", 3, QuestTheExecutorsSummons, ExecutorHarrow, ExecutorHarrow, 600, 120);
            q.Description = "Something stirs the old dead in the northern fields. Skeletons claw out of the farmland, and the ghouls that follow them feed on whatever they uncover. They answer to no one - not to us, and not to anything we can find.\n\nThin them out before they reach the village. Six skeletons and six ghouls will do for a start.";
            q.ObjectivesText = "Slay 6 Restless Skeletons and 6 Wretched Ghouls in the fields north of the village, then return to Harrow Blackmere.";
            q.ProgressText = "The fields are still moving. Keep at it.";
            q.CompletionText = "Quieter already. Take these gloves - the ghouls' claws tear through bare hands.";
            q.Objectives.Add(Kill(RestlessSkeleton, 6, "Restless Skeleton slain"));
            q.Objectives.Add(Kill(WretchedGhoul, 6, "Wretched Ghoul slain"));
            q.RewardChoiceItems.Add(2960); q.RewardChoiceItems.Add(2964); q.RewardChoiceItems.Add(2968);
            L.Add(q);

            // 8. Explore: scout the approach to the Scarlet camp on the north-eastern ridge.
            q = Quest(QuestWatchersOnTheRidge, "Watchers on the Ridge", 4, QuestBonesThatWalk, ExecutorHarrow, ExecutorHarrow, 350, 60);
            q.Description = "There are living men on the ridge to the north-east - zealots in crimson, who came here to burn what they call an abomination. That would be you, and me, and everyone in this village.\n\nI need to know how many there are and where they have dug in. Go down below their camp, take a good look, and come back in one piece.";
            q.ObjectivesText = "Scout the ground below the Scarlet camp on the north-eastern ridge, then return to Harrow Blackmere.";
            q.ProgressText = "Well? What did you see?";
            q.CompletionText = "Tents, banners and too many blades. They are not here to talk. Good work - you kept your head down. Now we move.";
            q.Objectives.Add(Explore(RidgeExplorePos, 15f, "Scout the Scarlet camp"));
            L.Add(q);

            // 9. Collect armbands from the Scarlet converts and initiates.
            q = Quest(QuestCrimsonArmbands, "Crimson Armbands", 4, QuestWatchersOnTheRidge, ExecutorHarrow, ExecutorHarrow, 650, 150);
            q.Description = "Every zealot in that camp wears a crimson armband - their mark of faith. I want the Dark Lady's court to see exactly how many have come to our doorstep.\n\nBring me six of those armbands. Their wearers will not part with them willingly.";
            q.ObjectivesText = "Bring 6 Crimson Armbands from the Scarlet converts and initiates on the north-eastern ridge to Harrow Blackmere.";
            q.ProgressText = "The armbands. How many?";
            q.CompletionText = "Six fewer zealots on the ridge. The court will hear of this. Take something sturdier to wear - you have earned it.";
            q.Objectives.Add(Collect(ItemCrimsonArmband, 6, "Crimson Armband"));
            q.RewardChoiceItems.Add(2958); q.RewardChoiceItems.Add(2962); q.RewardChoiceItems.Add(2966);
            L.Add(q);

            // 10. Final: slay the inquisitor at the far end of the Scarlet camp, then report back to the priestess.
            q = Quest(QuestTheInquisitor, "The Inquisitor", 5, QuestCrimsonArmbands, ExecutorHarrow, ShadowPriestessVessra, 1000, 300);
            q.Description = "The zealots on the ridge answer to Inquisitor Marren Vale. He preaches that every one of us must burn, and his followers believe him. Cut off the head, and the rest of that camp will scatter back to wherever it came from.\n\nHe waits at the far end of their camp, surrounded by the faithful. When it is done, return to Vessra at the crypt - she will want to know that her newest risen did this.";
            q.ObjectivesText = "Slay Inquisitor Marren Vale at the far end of the Scarlet camp, then return to Vessra Duskhallow at the crypt.";
            q.ProgressText = "You have come back from the ridge. Tell me.";
            q.CompletionText = "The inquisitor is dead, and you are still standing. When you climbed out of that crypt, you did not know who you were. Now the whole of Deathknell does. Choose something from our stores - and when you are ready, the road out of Deathknell leads on into the rest of Tirisfal.";
            q.Objectives.Add(Kill(InquisitorMarrenVale, 1, "Inquisitor Marren Vale slain"));
            q.RewardChoiceItems.Add(5393); q.RewardChoiceItems.Add(2139); q.RewardChoiceItems.Add(2777); q.RewardChoiceItems.Add(923);
            L.Add(q);

            // 11. One class quest per class: the priestess sends you to your trainer on the crypt forecourt.
            L.Add(ClassQuest(QuestWarriorPath, WowClass.Warrior, "The Warrior's Path", WarriorTrainer, "Garrick Holloway",
                "You stand like someone who once knew how to fight. The body forgets, but it can be taught again - and there is someone here who teaches well.",
                "So, Vessra thinks you have a fighter in you. Show me your grip... terrible. Good - bad habits are harder to unlearn than none at all. We begin."));
            L.Add(ClassQuest(QuestRoguePath, WowClass.Rogue, "The Rogue's Path", RogueTrainer, "Silas Crane",
                "You moved through the crypt without a sound. Most of the newly risen stumble. Someone here would like to meet a person with such quiet feet.",
                "Vessra told me to watch for you. I have been watching for some time - you only just noticed. That will improve. Let us begin."));
            L.Add(ClassQuest(QuestPriestPath, WowClass.Priest, "The Priest's Path", PriestTrainer, "Maudra Veil",
                "Something in you still answers when I pray. Few of the risen keep that spark - and fewer still learn what to do with it.",
                "Vessra has a good eye. I can feel it in you - light and shadow both, circling each other. We will learn to use them together."));
            L.Add(ClassQuest(QuestMagePath, WowClass.Mage, "The Mage's Path", MageTrainer, "Ezra Morrowind",
                "Your mind is restless, reaching for things you cannot yet name. Death has given you all the time in the world to study - spend it well.",
                "Ah, a fresh mind! Vessra always sends me the curious ones. No more sleeping through lectures - you do not sleep at all now. Wonderful."));
            L.Add(ClassQuest(QuestWarlockPath, WowClass.Warlock, "The Warlock's Path", WarlockTrainer, "Lucan Graves",
                "There is a hunger in you that no grave could settle. Some would fear it. There is one here who would rather teach you to use it.",
                "I sensed you before you reached the stairs. You have a gift for the darker arts - the kind that frightens the living. Let us make it frighten them more."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestRisenAgain, ShadowPriestessVessra, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches on the crypt forecourt, just above the stairs. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " on the crypt forecourt.";
            q.ProgressText = "Vessra spoke of you.";
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

        // Class start items = CharStartOutfit.dbc race 5 (identical for both sexes), minus 6948 (hearthstone, no inn offline).
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 49778, 6125, 139, 140 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, StartItems = new[] { 2092, 2105, 120, 121, 50055, 28979 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 35, 6144, 53, 52, 51 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Mage, StartItems = new[] { 35, 6140, 1395, 6096, 55 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warlock, StartItems = new[] { 35, 6129, 1396, 59 }, StartSpells = new int[0] });
        }
    }
}
