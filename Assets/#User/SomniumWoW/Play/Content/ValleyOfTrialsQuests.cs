using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class ValleyOfTrials
    {
        /// <summary>Where the explore objective of "The Coven's Shadow" completes: the flat ground below the Burning Blade's cave in
        /// the far north, terrain height 57.33, area 363 (harness probe BAt: slope 4 degrees).</summary>
        public static readonly WowPos CovenExplorePos = new WowPos(-300f, -4390f, 57.33f, 0f);

        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6, 7600 in total to level 6). Quest XP sums to
        // 4080 for one character (10 quests incl. its class quest); the kills the quests ask for give about 3000 more with
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

            // 1. Intro: the elder beside the start sends you to the warband captain.
            q = Quest(QuestBloodAndSand, "Blood and Sand", 1, 0, ElderGarosh, WarbandCaptain, 80, 0);
            q.Description = "Every orc and troll who would fight for the Horde begins here, in this valley of red stone. Nobody is handed a place among us. It is earned, one trial at a time.\n\nYour first trial is simple: find Rakka Ironjaw, the captain of this camp's warband, and tell her you are ready to work. She stands a few steps away. Do not keep her waiting.";
            q.ObjectivesText = "Speak with Rakka Ironjaw in the camp.";
            q.ProgressText = "Why are you still standing here? Rakka is waiting.";
            q.CompletionText = "Garosh sent you? Then he thinks you might survive the week. Let us find out if he is right.";
            q.Objectives.Add(TalkTo(WarbandCaptain, "Speak with Rakka Ironjaw"));
            L.Add(q);

            // 2. Kill mottled boars in the meadows around the camp.
            q = Quest(QuestTusksOfTheValley, "Tusks of the Valley", 1, QuestBloodAndSand, WarbandCaptain, WarbandCaptain, 250, 35);
            q.Description = "The boars of this valley have grown fat on our grain stores and bold enough to charge anyone who walks the meadows. A warrior who cannot bring down a boar has no business carrying a weapon.\n\nGo out into the meadows north-east and south of camp and kill eight mottled boars. The cooks will thank you, even if the boars will not.";
            q.ObjectivesText = "Slay 8 Mottled Boars in the meadows around the camp, then return to Rakka Ironjaw.";
            q.ProgressText = "The boars will not hunt themselves. Well?";
            q.CompletionText = "Not bad. You still have all your limbs, which is more than the last recruit could say. Take some flatbread - the boar meat goes to the warband first.";
            q.Objectives.Add(Kill(MottledBoar, 8, "Mottled Boar slain"));
            q.RewardItems.Add(4540);
            L.Add(q);

            // 3. Collect scorpid tails from the scorpid workers (the northern meadows).
            q = Quest(QuestStingers, "Stingers", 2, QuestTusksOfTheValley, WarbandCaptain, WarbandCaptain, 400, 60);
            q.Description = "The scorpids that swarm the meadows north of camp have stung three of my grunts this week. The shaman says their tails can be ground into a salve that draws the poison back out.\n\nBring me six scorpid worker tails. Mind the stinger - it is still dangerous after the scorpid is dead.";
            q.ObjectivesText = "Bring 6 Scorpid Worker Tails to Rakka Ironjaw.";
            q.ProgressText = "Do you have the tails? My grunts are still swelling.";
            q.CompletionText = "Six tails - the salve will be ready by nightfall. You have earned some real armour. Pick one.";
            q.Objectives.Add(Collect(ItemScorpidTail, 6, "Scorpid Worker Tail"));
            q.RewardChoiceItems.Add(2957); q.RewardChoiceItems.Add(2961); q.RewardChoiceItems.Add(2965);
            L.Add(q);

            // 4. Delivery: the captain sends word to the scout on the north path (chain: captain -> scout -> watch leader -> elder).
            q = Quest(QuestWordToTheScout, "Word to the Scout", 2, QuestStingers, WarbandCaptain, ScoutZanji, 150, 25);
            q.Description = "Zanji Talonspear, one of the Darkspear scouts, watches the path to the northern rocks. He has not reported in two days, and I need to know what he has seen.\n\nFollow the path north out of camp and find him. If he is dead, bring me whatever he was carrying. If he is alive, tell him Rakka wants answers.";
            q.ObjectivesText = "Find Zanji Talonspear on the path north of the camp.";
            q.ProgressText = "Rakka sent you? Keep your voice low.";
            q.CompletionText = "Alive, mon, just busy. Rakka always did worry too much. Here - a draught for the road. You will be needing it where I am sending you.";
            q.Objectives.Add(TalkTo(ScoutZanji, "Speak with Zanji Talonspear"));
            q.RewardItems.Add(118);
            L.Add(q);

            // 5. Kill vile familiars among the north-western rocks.
            q = Quest(QuestImpsInTheRocks, "Imps in the Rocks", 3, QuestWordToTheScout, ScoutZanji, ScoutZanji, 550, 100);
            q.Description = "Those little fire-spitting imps in the rocks - vile familiars, the warlocks call them. The Burning Blade summoned them up and then lost their grip on them. Now they burn anything that walks by, including me.\n\nKill eight of them. They are small and fast, but they burn bright and they die easy.";
            q.ObjectivesText = "Slay 8 Vile Familiars in the rocks north-west of the camp, then return to Zanji Talonspear.";
            q.ProgressText = "Still smelling smoke up there. More of them left?";
            q.CompletionText = "Ha! The rocks be quiet again. You fight like a Darkspear, mon. Take some boots - the ground up there gets hot where the imps have been.";
            q.Objectives.Add(Kill(VileFamiliar, 8, "Vile Familiar slain"));
            q.RewardChoiceItems.Add(2959); q.RewardChoiceItems.Add(2963); q.RewardChoiceItems.Add(2967);
            L.Add(q);

            // 6. Collect venom glands from the dusk scorpids (north-eastern rocks).
            q = Quest(QuestDuskVenom, "Dusk Venom", 3, QuestImpsInTheRocks, ScoutZanji, ScoutZanji, 550, 100);
            q.Description = "The big black scorpids in the north-eastern rocks - dusk scorpids - carry a venom far stronger than the workers in the meadows. My people know how to brew it into a poison for our arrows.\n\nBring me six of their venom glands. Cut clean and quick, mon, or the gland bursts and you get nothing but a sore hand.";
            q.ObjectivesText = "Collect 6 Dusk Scorpid Venom Glands from the dusk scorpids in the north-eastern rocks, then return to Zanji Talonspear.";
            q.ProgressText = "The glands - you got them?";
            q.CompletionText = "Good and full, every one. The Horde's arrows will fly a little deadlier now. Take these gloves - you will be needing steady hands.";
            q.Objectives.Add(Collect(ItemDuskVenomGland, 6, "Dusk Scorpid Venom Gland"));
            q.RewardChoiceItems.Add(2960); q.RewardChoiceItems.Add(2964); q.RewardChoiceItems.Add(2968);
            L.Add(q);

            // 7. Explore: scout the ground below the Burning Blade's cave, then report to the watch leader.
            q = Quest(QuestTheCovensShadow, "The Coven's Shadow", 3, QuestDuskVenom, ScoutZanji, WatchLeader, 350, 60);
            q.Description = "Further north, past the rocks, there be a cave. The Burning Blade hides in it - orcs who still bow to the demons the rest of your people cast off. The imps came from there. So does the smoke.\n\nGo look at the ground below that cave, see how many guard it, then find Urtha Bloodfang. She commands the watch up there, and she will know what to do with what you saw.";
            q.ObjectivesText = "Scout the ground below the Burning Blade's cave in the far north, then speak with Urtha Bloodfang.";
            q.ProgressText = "Who sent you up here? Speak.";
            q.CompletionText = "Zanji sent you? And you walked right up to their doorstep to count heads. Brave, or foolish - out here there is little difference. Good work.";
            q.Objectives.Add(Explore(CovenExplorePos, 15f, "Scout the Burning Blade's cave"));
            L.Add(q);

            // 8. Collect sigils from the Burning Blade neophytes and thugs.
            q = Quest(QuestSigilsOfTheBlade, "Sigils of the Blade", 4, QuestTheCovensShadow, WatchLeader, WatchLeader, 650, 150);
            q.Description = "Every one of those traitors carries a sigil of the Burning Blade - a mark of the demon pact they swore. Orgrimmar wants proof of how many have gathered here, and I want fewer of them breathing.\n\nBring me six sigils. The neophytes throw shadow at you and the thugs will try to open you up. Do not let either of them do it twice.";
            q.ObjectivesText = "Bring 6 Burning Blade Sigils from the neophytes and thugs below the cave to Urtha Bloodfang.";
            q.ProgressText = "How many sigils?";
            q.CompletionText = "Six. Six fewer traitors wearing our colours. Take something sturdier to wear into that cave - you will be going back.";
            q.Objectives.Add(Collect(ItemBladeSigil, 6, "Burning Blade Sigil"));
            q.RewardChoiceItems.Add(2958); q.RewardChoiceItems.Add(2962); q.RewardChoiceItems.Add(2966);
            L.Add(q);

            // 9. Final: slay the cult leader beyond his thugs, then report back to the elder at the camp.
            q = Quest(QuestAshbrandsEnd, "Ashbrand's End", 5, QuestSigilsOfTheBlade, WatchLeader, ElderGarosh, 1000, 300);
            q.Description = "The one who leads these traitors is called Vorzul Ashbrand. He summoned the imps, he raised the smoke and he preaches that the old demon masters will return to reward the faithful. As long as he lives, the Burning Blade will keep coming back to this valley.\n\nHe waits beyond his thugs at the far end of the camp below the cave. Kill him. Then go back to Garosh at the camp - an elder should hear this from the one who did it.";
            q.ObjectivesText = "Slay Vorzul Ashbrand below the Burning Blade's cave, then return to Garosh Emberhide at the camp.";
            q.ProgressText = "You come back from the north with blood on your blade. Tell me.";
            q.CompletionText = "Ashbrand is dead? Then you have done what the valley asks of every one of us - you faced your trials and came back standing. Take a weapon from our stores. When you are ready, the road out of the valley leads to Razor Hill and the wider Horde beyond.";
            q.Objectives.Add(Kill(VorzulAshbrand, 1, "Vorzul Ashbrand slain"));
            q.RewardChoiceItems.Add(5393); q.RewardChoiceItems.Add(2139); q.RewardChoiceItems.Add(2777); q.RewardChoiceItems.Add(923);
            L.Add(q);

            // 10. One class quest per class: the elder sends you to your trainer around the camp.
            L.Add(ClassQuest(QuestWarriorPath, WowClass.Warrior, "The Warrior's Path", WarriorTrainer, "Drogash Steelbrow",
                "You have the shoulders for an axe and the temper to swing it. Drogash will teach you the rest - if you can take a beating.",
                "Garosh sent you? Hit that post. Harder. Again. ...Good. You will do. Come back when you have bled a little more."));
            L.Add(ClassQuest(QuestHunterPath, WowClass.Hunter, "The Hunter's Path", HunterTrainer, "Tak'ren Farshot",
                "Your eyes keep drifting to the tracks in the dust. The valley's hunters notice things like that. One of them would like a word with you.",
                "The elder was right about you, mon. You read the ground like a Darkspear. The bow, the beasts, the waiting - that all comes with time."));
            L.Add(ClassQuest(QuestRoguePath, WowClass.Rogue, "The Rogue's Path", RogueTrainer, "Skarn Quickblade",
                "You move without being noticed. In a camp full of warriors shouting about their axes, that is a rare and useful thing.",
                "You found me, which means Garosh told you where to look. Next time, find me without help. We will start with the basics."));
            L.Add(ClassQuest(QuestPriestPath, WowClass.Priest, "The Priest's Path", PriestTrainer, "Zel'ani Moonsong",
                "The spirits of your ancestors crowd close around you. There is one in this camp who can teach you to hear them clearly.",
                "The loa whispered that you would come, and here you are. Sit with me, and we will begin with the simplest of prayers."));
            L.Add(ClassQuest(QuestShamanPath, WowClass.Shaman, "The Shaman's Path", ShamanTrainer, "Thrak Stormcaller",
                "The wind turns when you walk past, and the fire leans toward you. The elements have noticed you. Thrak can teach you to answer them.",
                "Yes... the earth told me someone was coming. The elements chose you, young one. We will learn to speak with them together."));
            L.Add(ClassQuest(QuestMagePath, WowClass.Mage, "The Mage's Path", MageTrainer, "Rhazi Emberweave",
                "There is a spark in you that has nothing to do with muscle or blade. Rhazi can show you how to fan it into flame.",
                "Ah, the elder sends me a thinker! Good - this camp has plenty of axes. Let us see how much fire you can hold before it burns you."));
            L.Add(ClassQuest(QuestWarlockPath, WowClass.Warlock, "The Warlock's Path", WarlockTrainer, "Vasha Gloomheart",
                "You feel the same pull the Burning Blade felt. The difference between them and us is control. Vasha teaches control.",
                "So Garosh finally sent me one with backbone. The fel is a weapon, not a master - remember that, and you will never end up like the fools in that cave."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestBloodAndSand, ElderGarosh, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches in the camp around the valley's centre. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " in the camp.";
            q.ProgressText = "Garosh spoke of you.";
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

        // Class start items = CharStartOutfit.dbc (identical for both sexes), minus 6948 (hearthstone) and 2512 (arrows).
        // ClassStart has no race field: classes both races can be use the ORC rows (race 2); Priest and Mage, which only
        // Trolls can be here, use the TROLL rows (race 8). Troll rows for the shared classes (not used): warrior 49778, 6125,
        // 139, 25861; hunter 12282, 2101, 2504, 6126, 127; rogue 2092, 6136, 6137, 6138, 50055, 25861; shaman 36, 6134, 6135, 2362.
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 12282, 6125, 139, 140 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Hunter, StartItems = new[] { 12282, 127, 6126, 6127, 2504, 2101 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, StartItems = new[] { 2092, 2105, 120, 121, 50055, 25861 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Shaman, StartItems = new[] { 36, 154, 153, 2362 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warlock, StartItems = new[] { 35, 6129, 1396, 59 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 35, 6144, 53, 52 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Mage, StartItems = new[] { 35, 6140, 1395, 6096, 55 }, StartSpells = new int[0] });
        }
    }
}
