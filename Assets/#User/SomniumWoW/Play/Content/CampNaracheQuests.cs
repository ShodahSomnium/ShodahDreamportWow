using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class CampNarache
    {
        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6, 7600 in total to level 6). Quest XP sums to
        // 3980 for one character (10 quests incl. its class quest); the kills the quests ask for give about 3000 more with
        // AzerothCore's kill formula (L*5+45 at equal level), so finishing everything lands at about level 5.5-6 (the probe
        // NaracheCheck prints the estimate).

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

            // 1. Intro: the Elder sends you to the Hunt Leader (both stand in the camp beside the start).
            q = Quest(QuestMorningOnTheMesa, "Morning on the Mesa", 1, 0, ElderTayanka, HuntLeaderWahkeen, 80, 0);
            q.Description = "The sun climbs over Red Cloud Mesa, young one, and you climb with it. Here our people learn the ways of the hunt, the voices of the ancestors and the strength of the herd before they walk the wider plains of Mulgore.\n\nWahkeen Plainsrunner leads the camp's hunters. Go to him. There is always work for willing hooves.";
            q.ObjectivesText = "Speak with Wahkeen Plainsrunner in Camp Narache.";
            q.ProgressText = "Hm? Did the Elder send you?";
            q.CompletionText = "Tayanka sent you? Then she sees a hunter in you. Let us find out if she is right.";
            q.Objectives.Add(TalkTo(HuntLeaderWahkeen, "Speak with Wahkeen Plainsrunner"));
            L.Add(q);

            // 2. Kill plainstriders (around the camp).
            q = Quest(QuestFirstHunt, "The First Hunt", 1, QuestMorningOnTheMesa, HuntLeaderWahkeen, HuntLeaderWahkeen, 250, 35);
            q.Description = "Every hunter on the mesa begins with the plainstriders. They are quick, they are loud, and they peck at anything that wanders near their nests - including new tauren who do not watch where they step.\n\nThe camp needs meat and the striders need thinning. Hunt eight of them in the grass around Camp Narache, then come back to me.";
            q.ObjectivesText = "Slay 8 Plainstriders around Camp Narache, then return to Wahkeen Plainsrunner.";
            q.ProgressText = "The striders will not come to you. Well - some of them will. Is it done?";
            q.CompletionText = "Not bad for a first hunt. You did not even lose a horn. Here, eat - a hunter who forgets to eat does not stay a hunter for long.";
            q.Objectives.Add(Kill(Plainstrider, 8, "Plainstrider slain"));
            q.RewardItems.Add(117);
            L.Add(q);

            // 3. Collect cougar pelts (the western slopes).
            q = Quest(QuestCougarPelts, "Pelts for the Cold Moons", 2, QuestFirstHunt, HuntLeaderWahkeen, HuntLeaderWahkeen, 350, 60);
            q.Description = "The nights on the mesa grow cold when the moons turn, and the elders feel it in their bones. The mountain cougars on the western slopes wear the warmest coats on Red Cloud Mesa.\n\nBring me six of their pelts. Mind their claws - a cougar does not give up its coat gladly.";
            q.ObjectivesText = "Bring 6 Mountain Cougar Pelts to Wahkeen Plainsrunner.";
            q.ProgressText = "The cougars keep to the rocks west of camp. Do you have the pelts?";
            q.CompletionText = "Good, thick pelts. The elders will sleep warm this winter, and they will know whose hunt it was. Take one of these - the camp's crafters made them for hunters like you.";
            q.Objectives.Add(Collect(ItemCougarPelt, 6, "Mountain Cougar Pelt"));
            q.RewardChoiceItems.Add(2578); q.RewardChoiceItems.Add(1794); q.RewardChoiceItems.Add(2016);
            L.Add(q);

            // 4. Explore the northern rim of the mesa, then go to the Keeper of the Pyres (west) - the start of the chain across
            //    the zone (Elder in the camp -> the rim -> Aponi at the pyres -> Tala on the eastern plain -> back to the Elder).
            q = Quest(QuestViewFromTheRim, "The View from the Rim", 2, QuestMorningOnTheMesa, ElderTayanka, KeeperAponi, 300, 50);
            q.Description = "Before you walk among the ancestors, you should see the land they gave us. Go north to the rim of the mesa, where the windbreaks stand, and look out over Mulgore - the grass, the lakes, the long road to Thunder Bluff.\n\nWhen you have seen it, go to Aponi Mistcaller at the pyres west of camp. Tell her what you saw. She will know what it means.";
            q.ObjectivesText = "Look out over Mulgore from the northern rim of Red Cloud Mesa, then speak with Aponi Mistcaller at the pyres west of Camp Narache.";
            q.ProgressText = "You have the wind of the rim in your mane. Come, sit.";
            q.CompletionText = "So you have seen it - all of Mulgore laid out below like a blanket. Every tauren who stood on that rim before you rests here now, watching. Drink; the climb is long and the ancestors are patient.";
            q.Objectives.Add(Explore(RimExplorePos, 14f, "Look out from the northern rim"));
            q.RewardItems.Add(1179);
            L.Add(q);

            // 5. Collect battleboar tusks (the southern plains).
            q = Quest(QuestTramplersTusks, "The Tramplers", 3, QuestViewFromTheRim, KeeperAponi, KeeperAponi, 550, 100);
            q.Description = "Something is driving the battleboars up from the southern grass. They charge through the camp's trails, scatter the offerings and root at the foot of the pyres. The ancestors do not rest while their offerings lie trampled.\n\nBring me six battleboar tusks. Carved with the old signs and set around the pyres, they will warn the others away - and calm the spirits.";
            q.ObjectivesText = "Collect 6 Battleboar Tusks from the battleboars on the southern plains, then return to Aponi Mistcaller.";
            q.ProgressText = "The spirits grow louder every night. Have you brought the tusks?";
            q.CompletionText = "Six good tusks. I will carve the signs tonight. You walk the plains like one who belongs there - these will help you walk them farther.";
            q.Objectives.Add(Collect(ItemBattleboarTusk, 6, "Battleboar Tusk"));
            q.RewardChoiceItems.Add(2583); q.RewardChoiceItems.Add(1788); q.RewardChoiceItems.Add(2037);
            L.Add(q);

            // 6. Delivery across the zone: from the pyres (west) to the eastern watch.
            q = Quest(QuestSmokeInTheEast, "Smoke in the East", 3, QuestTramplersTusks, KeeperAponi, ScoutTala, 150, 25);
            q.Description = "While you hunted, the ancestors spoke to me in the smoke of the pyres. They showed me thorns, and tusks, and fires burning on the eastern plain. The boars are not running on their own - they are being driven.\n\nTala Thornwatch keeps the eastern watch below Kodo Rock. Carry my words to her: the spirits have seen the Bristleback. She must be ready.";
            q.ObjectivesText = "Speak with Tala Thornwatch on the eastern plain of Red Cloud Mesa.";
            q.ProgressText = "Stay low. What brings you out this far?";
            q.CompletionText = "Aponi saw it too? Then I was right to worry. Quilboar - Bristleback, up from the ravine, camped on our own mesa. Keep this close; you will need it before the day is out.";
            q.Objectives.Add(TalkTo(ScoutTala, "Speak with Tala Thornwatch"));
            q.RewardItems.Add(858);
            L.Add(q);

            // 7. Kill quilboar and mystics at the camp on the eastern plain.
            q = Quest(QuestBristlebackIncursion, "Bristleback on the Mesa", 3, QuestSmokeInTheEast, ScoutTala, ScoutTala, 550, 120);
            q.Description = "The Bristleback have always kept to Brambleblade Ravine. Now they have climbed onto the mesa and pitched their camp in the shadow of Kodo Rock, and their mystics call on the storm to guard it.\n\nWe will not let them take root. Slay six Bristleback Quilboar and four Bristleback Mystics. Strike the mystics first if you can - their lightning bites.";
            q.ObjectivesText = "Slay 6 Bristleback Quilboar and 4 Bristleback Mystics at the quilboar camp on the eastern plain, then return to Tala Thornwatch.";
            q.ProgressText = "I can still smell their fires. Keep at it.";
            q.CompletionText = "That will make them think twice. But quilboar do not climb this far without someone pushing them - and I have seen the thorn charms they carry.";
            q.Objectives.Add(Kill(BristlebackQuilboar, 6, "Bristleback Quilboar slain"));
            q.Objectives.Add(Kill(BristlebackMystic, 4, "Bristleback Mystic slain"));
            q.RewardChoiceItems.Add(2158); q.RewardChoiceItems.Add(1791); q.RewardChoiceItems.Add(2230);
            L.Add(q);

            // 8. Collect the thorn fetishes the quilboar carry.
            q = Quest(QuestThornFetishes, "Thorns and Charms", 4, QuestBristlebackIncursion, ScoutTala, ScoutTala, 650, 150);
            q.Description = "Every Bristleback in that camp wears a fetish of bramble and bone. I watched them touch those charms before they charged, like warriors asking for a blessing - or obeying an order.\n\nBring me five of them. If we know who made them, we know who leads them.";
            q.ObjectivesText = "Collect 5 Bristleback Thorn Fetishes from the quilboar camp, then return to Tala Thornwatch.";
            q.ProgressText = "Careful with those thorns. Did you find the fetishes?";
            q.CompletionText = "Look at the knots - all the same hand. These were bound by a chieftain for his own warriors. Bramblehide. He is here, on our mesa. I know it now.";
            q.Objectives.Add(Collect(ItemThornFetish, 5, "Bristleback Thorn Fetish"));
            q.RewardChoiceItems.Add(2159); q.RewardChoiceItems.Add(1792); q.RewardChoiceItems.Add(2152);
            L.Add(q);

            // 9. Final: the named quilboar at the heart of the camp; report back to the Elder in Camp Narache.
            q = Quest(QuestChiefBramblehide, "Chief Bramblehide", 5, QuestThornFetishes, ScoutTala, ElderTayanka, 1000, 300);
            q.Description = "Chief Bramblehide leads the Bristleback on the mesa. He drove the boars through our trails, he bound his warriors with thorn charms, and he means to make Red Cloud Mesa his own.\n\nHe waits at the heart of the quilboar camp. He is stronger than anything you have faced - be ready, then end him. When it is done, carry the news to Elder Tayanka in Camp Narache. She should hear it from you.";
            q.ObjectivesText = "Slay Chief Bramblehide at the heart of the quilboar camp, then report to Tayanka Sunmane in Camp Narache.";
            q.ProgressText = "You come back with dust on your hooves and battle in your eyes. Speak, young one.";
            q.CompletionText = "Bramblehide, fallen - by one who woke on this mesa only days ago. The ancestors will rest tonight, and the herd will graze in peace.\n\nYou have earned more than words. Choose from these, and carry it with pride. Red Cloud Mesa will remember your name.";
            q.Objectives.Add(Kill(ChiefBramblehide, 1, "Chief Bramblehide slain"));
            q.RewardChoiceItems.Add(1197); q.RewardChoiceItems.Add(766); q.RewardChoiceItems.Add(1159); q.RewardChoiceItems.Add(2509);
            L.Add(q);

            // 10. One class quest per class: the Elder sends you to your trainer.
            L.Add(ClassQuest(QuestWarriorPath, WowClass.Warrior, "The Warrior's Path", WarriorTrainer, "Hoskan Ironmane",
                "You carry yourself like one who will stand in front when the others must fall back. The warriors of the mesa can teach you how to make that stand count.",
                "So the Elder sent you. Plant your hooves. Wider. Good - now nothing will move you that you do not allow. Each lesson waits for its season; come back as you grow."));
            L.Add(ClassQuest(QuestHunterPath, WowClass.Hunter, "The Hunter's Path", HunterTrainer, "Chayton Longsight",
                "Your eyes follow every movement in the grass. That is the beginning of a hunter. The rest - the rifle, the trail, the patience - can be taught.",
                "Tayanka has a good eye for hunters. You already watch the horizon without being told. The rest comes with time and with the trail."));
            L.Add(ClassQuest(QuestShamanPath, WowClass.Shaman, "The Shaman's Path", ShamanTrainer, "Makya Earthsong",
                "The elements lean toward you when you pass, young one. The spirits of earth, fire, water and air do not do that for everyone.",
                "Yes... the stones were humming this morning, and now I know why. The elements have noticed you. We will learn to answer them together, one voice at a time."));
            L.Add(ClassQuest(QuestDruidPath, WowClass.Druid, "The Druid's Path", DruidTrainer, "Enapay Greenbough",
                "The grass leans toward you and the birds do not flee when you walk by. The wild has marked you, as it once marked the first druids of our people.",
                "I felt you coming before I saw you. The Earth Mother has chosen you to keep the balance, young one. We will walk that path together, one step at a time."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestMorningOnTheMesa, ElderTayanka, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches in Camp Narache. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " in Camp Narache.";
            q.ProgressText = "The Elder spoke of you.";
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

        // Class start items = CharStartOutfit.dbc race 6 (identical for both sexes), minus 6948 (hearthstone) and 2516 (bullets).
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 2361, 6125, 139 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Hunter, StartItems = new[] { 12282, 127, 6126, 2508, 2102 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Shaman, StartItems = new[] { 36, 2362, 154, 153 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Druid, StartItems = new[] { 35, 6139, 6124 }, StartSpells = new int[0] });
        }
    }
}
