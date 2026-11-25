using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Shadowglen
    {
        /// <summary>Where the explore objective of "The Moonwell's Light" completes: beside the moonwell (NightElfMoonwellOrnate
        /// WMO at (10707.7, 764.6)), terrain height 1321.25.</summary>
        public static readonly WowPos MoonwellExplorePos = new WowPos(10703f, 771f, 1321.25f, 0f);

        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6, 7600 in total to level 6). Quest XP sums to
        // 3980 for one character (10 quests incl. its class quest); the ~60 kills the quests ask for give about 3500 more with
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

            // 1. Intro: the Keeper sends you to the Warden (both stand in front of the start).
            q = Quest(QuestWakingGlen, "The Waking Glen", 1, 0, KeeperLorellen, WardenFaelos, 80, 0);
            q.Description = "You stir beneath the boughs of Aldrassil, young one, and the glen stirs with you. Shadowglen has cradled our people's newest children since Teldrassil first took root, yet even here the balance wavers.\n\nFaelos Nightbrook watches over the wild paths of the glen. Go to him - he will show you where your hands are needed.";
            q.ObjectivesText = "Speak with Faelos Nightbrook at the foot of Aldrassil.";
            q.ProgressText = "Yes? You look like someone who was sent.";
            q.CompletionText = "Lorellen sent you? Then the Keeper sees something in you. Good. The glen has need of every willing blade and every steady heart.";
            q.Objectives.Add(TalkTo(WardenFaelos, "Speak with Faelos Nightbrook"));
            L.Add(q);

            // 2. Kill young nightsabers (meadows around the start).
            q = Quest(QuestClawsInTheGlen, "Claws in the Glen", 1, QuestWakingGlen, WardenFaelos, WardenFaelos, 250, 35);
            q.Description = "The nightsaber cubs of Shadowglen have grown bold this season. Their mothers roam farther for food, and the young ones stalk anything that moves near the roots of Aldrassil - including our newest arrivals.\n\nThey are not evil, only hungry. Still, the balance must be kept. Thin their numbers in the meadows around the great tree, and the glen will be safer for it.";
            q.ObjectivesText = "Slay 8 Young Nightsabers in the meadows around Aldrassil, then return to Faelos Nightbrook.";
            q.ProgressText = "The cubs will not wait for you to feel ready. Is it done?";
            q.CompletionText = "Well done. You move quietly for one so new to the world. Take some bread for the road - and keep your blade close. The glen is only beginning to test you.";
            q.Objectives.Add(Kill(YoungNightsaber, 8, "Young Nightsaber slain"));
            q.RewardItems.Add(4540);
            L.Add(q);

            // 3. Collect pelts from the mangy nightsabers (eastern slopes).
            q = Quest(QuestMangyPelts, "Mangy Pelts", 2, QuestClawsInTheGlen, WardenFaelos, WardenFaelos, 350, 60);
            q.Description = "A sickness has taken hold of the older nightsabers east of the tree. Their coats fall out in patches and their tempers have soured. I would know what ails them before it spreads.\n\nBring me six of their pelts from the eastern slopes. The druids of the glen will study them.";
            q.ObjectivesText = "Bring 6 Mangy Nightsaber Pelts to Faelos Nightbrook.";
            q.ProgressText = "The pelts - do you have them? Handle them with care.";
            q.CompletionText = "These will tell us much. This mange is no natural thing... it is as if the soil itself were souring. Take one of these for your trouble; they were made for the glen's defenders.";
            q.Objectives.Add(Collect(ItemMangyPelt, 6, "Mangy Nightsaber Pelt"));
            q.RewardChoiceItems.Add(2957); q.RewardChoiceItems.Add(2961); q.RewardChoiceItems.Add(2965);
            L.Add(q);

            // 4. Explore the moonwell (north) and speak with its tender - the start of the chain across the zone
            //    (Keeper at the tree -> Aravel at the moonwell -> Veyra in the western glade -> back to the Keeper).
            q = Quest(QuestMoonwellsLight, "The Moonwell's Light", 2, QuestWakingGlen, KeeperLorellen, TenderAravel, 300, 50);
            q.Description = "North of the great tree, where the glen opens beneath the stars, lies the moonwell of Shadowglen. Its waters carry Elune's blessing to every root and leaf of this valley.\n\nAravel Silverpool tends it, and he has grown worried of late. Walk to the moonwell, look upon its waters, and speak with Aravel. Tell him the Keeper sent you.";
            q.ObjectivesText = "Visit the moonwell north of Aldrassil, then speak with Aravel Silverpool.";
            q.ProgressText = "You have come from the tree? Then you have seen the well.";
            q.CompletionText = "The Keeper sent you? Then you have come at the right hour. Look closely at the water, young one. Do you see how its glow has dimmed? Drink this spring water instead - the well has little to spare.";
            q.Objectives.Add(Explore(MoonwellExplorePos, 12f, "Visit the moonwell"));
            q.RewardItems.Add(159);
            L.Add(q);

            // 5. Venom sacs from the webwood spiders (the northern cave mouth).
            q = Quest(QuestVenomInTheWater, "Venom in the Water", 3, QuestMoonwellsLight, TenderAravel, TenderAravel, 550, 100);
            q.Description = "The webwood spiders nest by the cave at the northern end of the glen, and their numbers have swelled. Their venom seeps into the streams that feed this moonwell - I can taste the bitterness in every drop.\n\nBring me six of their venom sacs. With them I can brew a cleansing draught, and the well may yet recover.";
            q.ObjectivesText = "Collect 6 Webwood Venom Sacs from the webwood spiders near the northern cave, then return to Aravel Silverpool.";
            q.ProgressText = "The water grows dimmer with every hour. Have you gathered the venom sacs?";
            q.CompletionText = "Six sacs, and hardly a bite on you. I will begin the draught at once. Take a pair of these - webs cling less to good boots.";
            q.Objectives.Add(Collect(ItemVenomSac, 6, "Webwood Venom Sac"));
            q.RewardChoiceItems.Add(2959); q.RewardChoiceItems.Add(2963); q.RewardChoiceItems.Add(2967);
            L.Add(q);

            // 6. Delivery across the zone: from the moonwell (north) to the Glade Watch (west).
            q = Quest(QuestWordToTheGladeWatch, "Word to the Glade Watch", 3, QuestVenomInTheWater, TenderAravel, ScoutVeyra, 150, 25);
            q.Description = "The venom was only part of it. Something else sours this valley - I felt it in the roots the night the grell first appeared in the western glade.\n\nVeyra Shadesong of the Glade Watch keeps an eye on those creatures. Carry my words to her: the moonwell weakens, and the source lies to the west. She will know what must be done.";
            q.ObjectivesText = "Speak with Veyra Shadesong at the edge of the western glade, west of Aldrassil.";
            q.ProgressText = "Quietly now. What brings you out here?";
            q.CompletionText = "Aravel feels it too, then. I had hoped I was imagining things. The grell grow bolder every night - and they are not acting on their own. Keep this draught close; you will want it.";
            q.Objectives.Add(TalkTo(ScoutVeyra, "Speak with Veyra Shadesong"));
            q.RewardItems.Add(118);
            L.Add(q);

            // 7. Kill grellkin and grell in the western glade.
            q = Quest(QuestFelStirring, "Fel Stirring", 3, QuestWordToTheGladeWatch, ScoutVeyra, ScoutVeyra, 550, 120);
            q.Description = "Grell and their little ones, the grellkin, have crept out of the western woods and made that glade their own. They are creatures of fel. They do not belong in Shadowglen, and they spoil everything they touch.\n\nDrive them back. Slay six grellkin and four grell, and we will see if the rest lose their nerve.";
            q.ObjectivesText = "Slay 6 Grellkin and 4 Grell in the western glade, then return to Veyra Shadesong.";
            q.ProgressText = "I can still hear their chittering from here. Keep at it.";
            q.CompletionText = "That will give them something to think about. But grell are cowards at heart - they would never have come this far without something driving them.";
            q.Objectives.Add(Kill(Grellkin, 6, "Grellkin slain"));
            q.Objectives.Add(Kill(Grell, 4, "Grell slain"));
            q.RewardChoiceItems.Add(2960); q.RewardChoiceItems.Add(2964); q.RewardChoiceItems.Add(2968);
            L.Add(q);

            // 8. Collect the charms the grell wear.
            q = Quest(QuestTaintedCharms, "Tainted Charms", 4, QuestFelStirring, ScoutVeyra, ScoutVeyra, 650, 150);
            q.Description = "I watched the grell last night. Each one wears a little charm of bone and dark stone around its neck, and they cower whenever those charms glow. Someone is binding them.\n\nBring me five of those charms - carefully - and I will learn who.";
            q.ObjectivesText = "Collect 5 Fel-Touched Charms from the grell and grellkin of the western glade, then return to Veyra Shadesong.";
            q.ProgressText = "Don't hold those charms longer than you must. Did you find them?";
            q.CompletionText = "Feel that? Even through my gloves... cold. These were made by a satyr's hand. There is one of them in the glade - I am certain of it now.";
            q.Objectives.Add(Collect(ItemFelCharm, 5, "Fel-Touched Charm"));
            q.RewardChoiceItems.Add(2958); q.RewardChoiceItems.Add(2962); q.RewardChoiceItems.Add(2966);
            L.Add(q);

            // 9. Final: the named satyr at the far end of the glade; report back to the Keeper at the tree.
            q = Quest(QuestDefilerOfTheGlade, "The Defiler of the Glade", 5, QuestTaintedCharms, ScoutVeyra, KeeperLorellen, 1000, 300);
            q.Description = "A satyr named Sythrel has made his lair at the far end of the western glade, beyond the grell. He is the one binding them, and it is his corruption that sours the moonwell and sickens the nightsabers.\n\nHe is stronger than anything you have faced here. Be ready - then end him. When it is done, carry the news to Lorellen Dawnbough at Aldrassil. She should hear it from you.";
            q.ObjectivesText = "Slay Sythrel the Defiler at the western end of the glade, then report to Lorellen Dawnbough at the foot of Aldrassil.";
            q.ProgressText = "You have the look of one returning from battle. Speak, young one.";
            q.CompletionText = "Sythrel, defeated - by one who woke beneath our boughs only days ago. The glen will breathe easier tonight, and the moonwell will shine again.\n\nYou have earned more than thanks. Choose from these, and carry it with pride. Shadowglen will remember you.";
            q.Objectives.Add(Kill(SythrelTheDefiler, 1, "Sythrel the Defiler slain"));
            q.RewardChoiceItems.Add(5393); q.RewardChoiceItems.Add(2139); q.RewardChoiceItems.Add(2777); q.RewardChoiceItems.Add(923);
            L.Add(q);

            // 10. One class quest per class: the Keeper sends you to your trainer.
            L.Add(ClassQuest(QuestWarriorPath, WowClass.Warrior, "The Warrior's Path", WarriorTrainer, "Tharion Bladesworn",
                "Your stance is strong and your grip is sure. You were born for the front of the line, where the glen's defenders stand between the danger and those they protect.",
                "So Lorellen sent you. Show me your stance... adequate. We will make something of you. Each lesson waits for its season - come back as you grow."));
            L.Add(ClassQuest(QuestHunterPath, WowClass.Hunter, "The Hunter's Path", HunterTrainer, "Kaelen Swiftarrow",
                "You have the sharp eyes of one who walks with the wild. The hunters of Shadowglen read the forest the way scholars read their scrolls.",
                "Lorellen has a good eye. You already watch the treeline without being told. The rest - the bow, the trail, the beasts - comes with time."));
            L.Add(ClassQuest(QuestRoguePath, WowClass.Rogue, "The Rogue's Path", RogueTrainer, "Nysera Duskveil",
                "Few noticed you arrive, and fewer will notice when you leave. That is a gift, and a dangerous one. Someone in the glen knows how to sharpen it.",
                "You found me, which is more than most manage. Keep to the shadows, strike where it counts, and never stand where they expect you. We will talk again as you grow."));
            L.Add(ClassQuest(QuestPriestPath, WowClass.Priest, "The Priest's Path", PriestTrainer, "Serelith Moonvale",
                "Elune's light rests easily upon you, child. Those who can carry it are rare, and they are needed now more than ever.",
                "The Goddess brought you here for a reason. I will teach you to mend and to shield - one prayer at a time, as you are ready for it."));
            L.Add(ClassQuest(QuestDruidPath, WowClass.Druid, "The Druid's Path", DruidTrainer, "Oranthir Deeproot",
                "The roots of the glen hum when you pass. Few feel the pulse of the wild so soon after waking - you should learn what it means.",
                "Yes... I felt you coming before I saw you. The wild has chosen you, young one. We will walk the path of the Cenarion Circle together, one step at a time."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestWakingGlen, KeeperLorellen, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches at the foot of Aldrassil. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " at the foot of Aldrassil.";
            q.ProgressText = "Lorellen spoke of you.";
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

        // Class start items = CharStartOutfit.dbc race 4 (identical for both sexes), minus 6948 (hearthstone) and 2512 (arrows).
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 49778, 6120, 6121, 6122 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Hunter, StartItems = new[] { 12282, 148, 147, 129, 2504, 2101 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, StartItems = new[] { 2092, 49, 48, 47, 50055, 28979 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 3661, 6119, 52, 51, 53 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Druid, StartItems = new[] { 3661, 6123, 6124 }, StartSpells = new int[0] });
        }
    }
}
