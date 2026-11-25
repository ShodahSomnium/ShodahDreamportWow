using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Northshire
    {
        /// <summary>Where the explore objective of "Echoes in the Ridge" completes: in front of the Echo Ridge Mine's mouth
        /// (MD_GOLDMINE WMO at (-8646.2, -131.7)), terrain height 89.08, area 34 Echo Ridge Mine.</summary>
        public static readonly WowPos MineExplorePos = new WowPos(-8690f, -112f, 89.08f, 0f);

        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6, 7600 in total to level 6). Quest XP sums to
        // 4090 for one character (12 quests incl. its class quest); the kills the quests ask for give about 3000 more with
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

            // 1. Intro: the steward sends you to the marshal (both stand in front of the start).
            q = Quest(QuestMorningAtTheAbbey, "Morning at the Abbey", 1, 0, BrotherAldric, MarshalCorwin, 80, 0);
            q.Description = "Welcome to Northshire, friend. You have come to us on a restless morning. The abbey has sheltered travelers and trained the defenders of Elwynn for generations, yet lately the whole valley seems to be holding its breath.\n\nMarshal Corwin Hale keeps watch over the abbey grounds. Go and speak with him - he will know where a willing pair of hands is needed most.";
            q.ObjectivesText = "Speak with Marshal Corwin Hale in front of Northshire Abbey.";
            q.ProgressText = "Another new face. Good - we can use every one.";
            q.CompletionText = "Brother Aldric sent you? He has a knack for finding help just when we run short of it. Stand easy, recruit. There is work enough for all of us.";
            q.Objectives.Add(TalkTo(MarshalCorwin, "Speak with Marshal Corwin Hale"));
            L.Add(q);

            // 2. Kill young wolves (meadows west and south-west of the abbey).
            q = Quest(QuestWolvesAtTheFence, "Wolves at the Fence", 1, QuestMorningAtTheAbbey, MarshalCorwin, MarshalCorwin, 220, 35);
            q.Description = "The young wolves in the meadows west of the abbey have lost their fear of us. They circle the fences at dusk and snap at the brothers when they go out for water. Last night one of them made off with a lamb.\n\nThin the pack in the meadows west and south-west of the abbey. Eight of them should teach the rest to keep their distance.";
            q.ObjectivesText = "Slay 8 Young Wolves in the meadows west of Northshire Abbey, then return to Marshal Corwin Hale.";
            q.ProgressText = "I can still hear them howling out there. How goes the hunt?";
            q.CompletionText = "That will quiet them for a while. You handle yourself well for someone fresh off the road. Here - the abbey orchard gives more apples than the brothers can ever eat.";
            q.Objectives.Add(Kill(YoungWolf, 8, "Young Wolf slain"));
            q.RewardItems.Add(4536);
            L.Add(q);

            // 3. Collect pelts from the timber wolves (north meadow).
            q = Quest(QuestTimberPelts, "Timber Pelts", 2, QuestWolvesAtTheFence, MarshalCorwin, MarshalCorwin, 300, 60);
            q.Description = "Those pups came down from the north meadow, where the grown timber wolves have their dens. Their coats are thick this season - thick enough to line the watch's cloaks before the first frost.\n\nHunt the timber wolves in the meadow north of the abbey and bring me six good pelts. Mind their teeth. They are nothing like the pups.";
            q.ObjectivesText = "Bring 6 Matted Wolf Pelts from the timber wolves north of the abbey to Marshal Corwin Hale.";
            q.ProgressText = "Six pelts, recruit. The nights are getting colder.";
            q.CompletionText = "Fine pelts, every one of them. The quartermaster will be glad of these. Take something for yourself as well - you have earned better than what you walked in wearing.";
            q.Objectives.Add(Collect(ItemMattedWolfPelt, 6, "Matted Wolf Pelt"));
            q.RewardChoiceItems.Add(1770); q.RewardChoiceItems.Add(1211); q.RewardChoiceItems.Add(1488);
            L.Add(q);

            // 4. Delivery: the steward sends you up the mine road (the chain across the zone:
            //    abbey -> foreman on the mine road -> vineyard keeper across the river -> back to the abbey).
            q = Quest(QuestLetterForTheForeman, "A Letter for the Foreman", 2, QuestMorningAtTheAbbey, BrotherAldric, ForemanEdda, 120, 20);
            q.Description = "Foreman Edda Brask sent word from the Echo Ridge Mine: something has been getting into the abbey's grain stores, and she believes she knows what. The abbey owes her an answer, and I would rather send a capable pair of hands than another letter.\n\nFollow the road north past the abbey. You will find Edda where the path begins to climb toward the mine.";
            q.ObjectivesText = "Speak with Foreman Edda Brask on the road north of the abbey, toward the Echo Ridge Mine.";
            q.ProgressText = "You're from the abbey? About time.";
            q.CompletionText = "So Aldric finally sent someone. I hope you don't mind getting your boots dirty, because that whole ridge is crawling.";
            q.Objectives.Add(TalkTo(ForemanEdda, "Speak with Foreman Edda Brask"));
            L.Add(q);

            // 5. Explore the mine mouth.
            q = Quest(QuestEchoesInTheRidge, "Echoes in the Ridge", 2, QuestLetterForTheForeman, ForemanEdda, ForemanEdda, 200, 40);
            q.Description = "Kobolds. The mine was ours until a month ago, when they came boiling up out of the deep tunnels and chased my crew out. Now they scurry down the hill every night and carry off anything they can lift.\n\nBefore we do anything rash, I want to know what we're up against. Get up to the mouth of the mine and take a good look - then come back and tell me what you saw.";
            q.ObjectivesText = "Scout the entrance of the Echo Ridge Mine, then return to Foreman Edda Brask.";
            q.ProgressText = "Well? How bad is it up there?";
            q.CompletionText = "That many? Light preserve us. Well, at least now we know. Here, drink something - you look like you ran the whole way back.";
            q.Objectives.Add(Explore(MineExplorePos, 12f, "Scout the Echo Ridge Mine"));
            q.RewardItems.Add(1205);
            L.Add(q);

            // 6. Kill kobolds at their camp and at the mine mouth.
            q = Quest(QuestKoboldTrouble, "Kobold Trouble", 3, QuestEchoesInTheRidge, ForemanEdda, ForemanEdda, 450, 90);
            q.Description = "If we let them settle in, we'll never get the mine back. The little ones - vermin, my crew calls them - have made a camp between here and the abbey, and the bigger laborers hold the mine mouth itself.\n\nDrive them back. Eight of the vermin and five of the laborers ought to make the rest think twice.";
            q.ObjectivesText = "Slay 8 Kobold Vermin and 5 Kobold Laborers near the Echo Ridge Mine, then return to Foreman Edda Brask.";
            q.ProgressText = "I can still hear them chattering. Keep at it.";
            q.CompletionText = "Ha! Did you see them run? My crew will sleep easier tonight. These boots are from our old stores - better on your feet than on a kobold's.";
            q.Objectives.Add(Kill(KoboldVermin, 8, "Kobold Vermin slain"));
            q.Objectives.Add(Kill(KoboldLaborer, 5, "Kobold Laborer slain"));
            q.RewardChoiceItems.Add(80); q.RewardChoiceItems.Add(210); q.RewardChoiceItems.Add(287);
            L.Add(q);

            // 7. Collect the abbey's grain back from the kobolds.
            q = Quest(QuestTheAbbeysGrain, "The Abbey's Grain", 3, QuestKoboldTrouble, ForemanEdda, ForemanEdda, 450, 110);
            q.Description = "Those kobolds have been hauling the abbey's grain up the hill, sack after sack. If we don't get it back, the brothers will be counting crumbs by midwinter.\n\nThe kobolds carry the sacks on them - the vermin and the laborers alike. Take back six sacks and I'll see they go down to the abbey.";
            q.ObjectivesText = "Recover 6 Sacks of Abbey Grain from the kobolds near the Echo Ridge Mine, then return to Foreman Edda Brask.";
            q.ProgressText = "Any luck with those sacks?";
            q.CompletionText = "Six sacks, and hardly a kernel spilled. The brothers will bless your name at supper. Take these gloves - a miner's gift, but they'll serve you well.";
            q.Objectives.Add(Collect(ItemSackOfAbbeyGrain, 6, "Sack of Abbey Grain"));
            q.RewardChoiceItems.Add(203); q.RewardChoiceItems.Add(239); q.RewardChoiceItems.Add(718);
            L.Add(q);

            // 8. Delivery across the zone: from the mine road to the vineyards across the river.
            q = Quest(QuestWordFromTheVineyards, "Word from the Vineyards", 3, QuestTheAbbeysGrain, ForemanEdda, HollisMarrow, 120, 25);
            q.Description = "While you were up the hill, a boy came running from the vineyards across the river - Hollis Marrow's lad. Seems the kobolds aren't the valley's only trouble. There are armed men among the vines, and Hollis is too stubborn to leave.\n\nCross the bridge south-east of the abbey and find Hollis at the edge of his vineyard. He could use someone who knows which end of a blade to hold.";
            q.ObjectivesText = "Cross the bridge over the river and speak with Hollis Marrow at the Northshire Vineyards.";
            q.ProgressText = "Keep your head down. They have eyes everywhere.";
            q.CompletionText = "Edda sent you? Bless her. Keep low and listen close: they came three nights ago and they haven't left since. Take this draught - you'll want it before the day is done.";
            q.Objectives.Add(TalkTo(HollisMarrow, "Speak with Hollis Marrow"));
            q.RewardItems.Add(118);
            L.Add(q);

            // 9. Kill the bandits in the vineyards.
            q = Quest(QuestBanditsAmongTheVines, "Bandits Among the Vines", 4, QuestWordFromTheVineyards, HollisMarrow, HollisMarrow, 550, 130);
            q.Description = "Masked cutthroats with rusty swords, trampling my vines and helping themselves to the harvest. They wear red kerchiefs over their faces and talk big about taking back what they're owed. All I know is that none of it was ever theirs.\n\nDrive them out of my vineyard. Six of the ones working the rows, and four of the lookouts watching from the far end.";
            q.ObjectivesText = "Slay 6 Vineyard Bandits and 4 Bandit Lookouts in the Northshire Vineyards, then return to Hollis Marrow.";
            q.ProgressText = "Are they still out there? I can hear them laughing.";
            q.CompletionText = "The rows are quieter already. My father planted those vines with his own hands - you've done more than you know. Take these; they were meant for the harvest hands, but nobody has earned them more.";
            q.Objectives.Add(Kill(VineyardBandit, 6, "Vineyard Bandit slain"));
            q.Objectives.Add(Kill(BanditLookout, 4, "Bandit Lookout slain"));
            q.RewardChoiceItems.Add(79); q.RewardChoiceItems.Add(61); q.RewardChoiceItems.Add(286);
            L.Add(q);

            // 10. Collect the stolen harvest.
            q = Quest(QuestTheStolenHarvest, "The Stolen Harvest", 4, QuestBanditsAmongTheVines, HollisMarrow, HollisMarrow, 550, 150);
            q.Description = "They didn't just trample the vines - they packed my harvest into crates to haul off and sell in some back alley. Every crate that leaves this valley is a winter the abbey goes without wine and I go without coin.\n\nThe bandits keep the crates close. Take them back. Five crates, and I'll count myself lucky.";
            q.ObjectivesText = "Recover 5 Pilfered Vineyard Crates from the bandits in the Northshire Vineyards, then return to Hollis Marrow.";
            q.ProgressText = "Did you find my crates?";
            q.CompletionText = "My grapes! Bruised, but whole. You have my thanks, friend. Pick a belt from the stores - a small thing, but honestly given.";
            q.Objectives.Add(Collect(ItemPilferedVineyardCrate, 5, "Pilfered Vineyard Crate"));
            q.RewardChoiceItems.Add(2283); q.RewardChoiceItems.Add(1215); q.RewardChoiceItems.Add(1154);
            L.Add(q);

            // 11. Final: the bandit leader at the far end of the vineyard; report back to the steward at the abbey.
            q = Quest(QuestTheVinecutter, "The Vinecutter", 5, QuestTheStolenHarvest, HollisMarrow, BrotherAldric, 950, 300);
            q.Description = "Their leader calls himself Harlan Crowe. His men call him the Vinecutter, because he cuts down whatever stands in his way. He has made his camp at the far end of the vineyard, past the last rows.\n\nHe is no common cutpurse. Be ready before you face him. When it's done, go to Brother Aldric at the abbey - the brothers will want to hear that the valley is safe again.";
            q.ObjectivesText = "Slay Harlan Crowe the Vinecutter at the far end of the Northshire Vineyards, then report to Brother Aldric Venn at Northshire Abbey.";
            q.ProgressText = "You return with the look of someone who has come through a hard fight.";
            q.CompletionText = "Harlan Crowe, brought down - and the vineyards free once more. The Light walks with you, friend. I felt it the morning you arrived.\n\nNorthshire owes you a debt. Choose one of these, with the abbey's blessing, and carry it wherever the road takes you.";
            q.Objectives.Add(Kill(HarlanCrowe, 1, "Harlan Crowe the Vinecutter slain"));
            q.RewardChoiceItems.Add(767); q.RewardChoiceItems.Add(816); q.RewardChoiceItems.Add(766); q.RewardChoiceItems.Add(1194);
            L.Add(q);

            // 12. One class quest per class: the steward sends you to your trainer.
            L.Add(ClassQuest(QuestWarriorCalling, WowClass.Warrior, "The Warrior's Calling", WarriorTrainer, "Bram Holloway",
                "You carry yourself like someone who would rather stand in front than hide behind. The watch could use more like you, and Bram Holloway knows how to turn that stubbornness into skill.",
                "Aldric sent you, did he? Let's see your stance... Hm. We'll fix that. Come back as you grow, and we'll build a warrior out of you one bruise at a time."));
            L.Add(ClassQuest(QuestPaladinCalling, WowClass.Paladin, "The Paladin's Calling", PaladinTrainer, "Maren Ashby",
                "The Light shines brightly in you - I noticed it the moment you walked through our gate. Sister Maren Ashby teaches those who would carry it into battle.",
                "Welcome, sister, brother - the Light does not care which. Keep your faith close and your shield closer. We will walk this road together, one lesson at a time."));
            L.Add(ClassQuest(QuestRogueCalling, WowClass.Rogue, "The Rogue's Calling", RogueTrainer, "Lissa Crane",
                "You have quick hands and quicker eyes. I will not ask where you learned to use them. There is someone near the abbey who can sharpen those talents - if you can find her.",
                "You found me, which already puts you ahead of most. Keep to the edges, strike where it hurts, and never stand where they expect you. We'll talk again as you grow."));
            L.Add(ClassQuest(QuestPriestCalling, WowClass.Priest, "The Priest's Calling", PriestTrainer, "Tomas Whitlow",
                "The Light rests gently on you, child. Brother Tomas Whitlow has guided many young priests through their first prayers - go to him and begin.",
                "Aldric sends me the best ones, I've always said. Mend what is broken and shield what is weak, and the Light will answer you. Come back whenever you are ready for more."));
            L.Add(ClassQuest(QuestMageCalling, WowClass.Mage, "The Mage's Calling", MageTrainer, "Aldous Penrose",
                "I have seen the way candle flames lean toward you when you pass. Aldous Penrose studies the arcane just outside the abbey - he will know what to make of you.",
                "Ah, a new student! Mind the scorch marks. Arcane power is a patient teacher and a cruel master - we will start small, and grow from there."));
            L.Add(ClassQuest(QuestWarlockCalling, WowClass.Warlock, "The Warlock's Calling", WarlockTrainer, "Silas Morrow",
                "I will not pretend to understand the path that calls to you, but I will not stand in your way either. Silas Morrow keeps to himself beside the abbey. Speak with him, and be careful.",
                "The steward sent you to me? He must be more open-minded than he lets on. Power has a price; learn to pay it on your own terms, and you will never be its servant."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestMorningAtTheAbbey, BrotherAldric, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches in front of Northshire Abbey. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " in front of Northshire Abbey.";
            q.ProgressText = "Brother Aldric mentioned you might come by.";
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

        // Class start items = CharStartOutfit.dbc race 1 (identical for both sexes), minus 6948 (hearthstone).
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 49778, 38, 39, 40 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Paladin, StartItems = new[] { 2361, 45, 44, 43 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, StartItems = new[] { 2092, 49, 48, 47, 50055, 28979 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 35, 6098, 52, 53, 51 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Mage, StartItems = new[] { 35, 56, 1395, 55, 6096 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warlock, StartItems = new[] { 35, 57, 6097, 1396, 59 }, StartSpells = new int[0] });
        }
    }
}
