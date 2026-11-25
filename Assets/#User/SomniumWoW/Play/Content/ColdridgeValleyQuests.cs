using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class ColdridgeValley
    {
        /// <summary>Where the explore objective of "Smoke over the Tents" completes: on the slope below the Snowbrand trolls'
        /// tents, terrain height 388.09, area 132 Coldridge Valley (harness ZaAt).</summary>
        public static readonly WowPos TentsExplorePos = new WowPos(-6385f, 722f, 388.09f, 0f);

        // XP sizing (real WotLK table: 400/900/1400/2100/2800 to reach levels 2-6, 7600 in total to level 6). Quest XP sums to
        // 4120 for one character (12 quests incl. its class quest); the kills the quests ask for give about 3000 more with
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

            // 1. Intro: the captain sends you to the quartermaster (both stand in front of Anvilmar).
            q = Quest(QuestAColdWelcome, "A Cold Welcome", 1, 0, CaptainDorgan, QuartermasterSenna, 80, 0);
            q.Description = "So you're the new blood they promised me. Good. Coldridge is a hard valley in a good year, and this is not a good year. The wolves are bold, the troggs are digging where they shouldn't, and there's smoke in the west that I don't like one bit.\n\nBefore you go marching off to fix all that, see Quartermaster Senna Copperkettle. She'll know where a fresh pair of hands is needed first.";
            q.ObjectivesText = "Speak with Quartermaster Senna Copperkettle in front of Anvilmar.";
            q.ProgressText = "Another one for the roster? Come in out of the wind.";
            q.CompletionText = "The captain sent you? Then he likes the look of you, and he doesn't like the look of anybody. Let's put you to work before your toes freeze.";
            q.Objectives.Add(TalkTo(QuartermasterSenna, "Speak with Quartermaster Senna Copperkettle"));
            L.Add(q);

            // 2. Kill young snow wolves (west and south-west of the start).
            q = Quest(QuestTeethInTheSnow, "Teeth in the Snow", 1, QuestAColdWelcome, QuartermasterSenna, QuartermasterSenna, 220, 35);
            q.Description = "The young snow wolves have taken to prowling right up to our doors. Two nights ago they dragged off a whole side of salted ram I'd left to cool by the wall, and last night they tried the storeroom shutters.\n\nThin them out for me. You'll find them in the snowfields just west and south of Anvilmar. Eight should make the rest think twice.";
            q.ObjectivesText = "Slay 8 Young Snow Wolves near Anvilmar, then return to Quartermaster Senna Copperkettle.";
            q.ProgressText = "Still hearing them yip out there. How's the hunting?";
            q.CompletionText = "That'll quiet them for a bit. Here, take a wedge of the good cheese - I hide it where the wolves can't reach, and the captain can't either.";
            q.Objectives.Add(Kill(YoungSnowWolf, 8, "Young Snow Wolf slain"));
            q.RewardItems.Add(2070);
            L.Add(q);

            // 3. Collect boar flanks (valley middle and south meadow).
            q = Quest(QuestBoarFlanks, "Boar Flanks for the Larder", 2, QuestTeethInTheSnow, QuartermasterSenna, QuartermasterSenna, 300, 60);
            q.Description = "With the ram gone, the larder's looking thin, and a hungry dwarf is a grumpy dwarf. Luckily the crag boars have fattened up nicely on the valley's roots this season.\n\nHunt the small crag boars out in the meadows past the wolves and bring me six good flanks. Watch the tusks; they're small, but they don't know it.";
            q.ObjectivesText = "Bring 6 Crag Boar Flanks from the small crag boars in the valley's meadows to Quartermaster Senna Copperkettle.";
            q.ProgressText = "Six flanks, friend. The stew pot's waiting.";
            q.CompletionText = "Now that's what I call supper! You've earned more than a bowl of stew for this. Pick something from the stores - it'll serve you better than those thin sleeves.";
            q.Objectives.Add(Collect(ItemCragBoarFlank, 6, "Crag Boar Flank"));
            q.RewardChoiceItems.Add(710); q.RewardChoiceItems.Add(763); q.RewardChoiceItems.Add(1182);
            L.Add(q);

            // 4. Delivery: the captain sends you up to the surveyor at the ruined dig (the chain across the zone:
            //    Anvilmar -> surveyor at the dig -> mountaineer at the western watch -> back to the captain).
            q = Quest(QuestWordForTheSurveyor, "Word for the Surveyor", 2, QuestAColdWelcome, CaptainDorgan, SurveyorFizzla, 120, 20);
            q.Description = "One of the gnomes who came to us from the city - Fizzla Sprocketwhistle, a surveyor - went out to the old dig west of here three days ago and hasn't sent word since. She's clever, but she has a habit of forgetting that the world bites.\n\nHead up the valley and find her. If she's in trouble, help her. If she's not, tell her to send a runner once in a while.";
            q.ObjectivesText = "Find Surveyor Fizzla Sprocketwhistle at the ruined dig west of Anvilmar.";
            q.ProgressText = "Oh! Hello! Please don't step on anything.";
            q.CompletionText = "The captain was worried? How sweet. And also fair, honestly - things have been a teensy bit exciting up here.";
            q.Objectives.Add(TalkTo(SurveyorFizzla, "Find Surveyor Fizzla Sprocketwhistle"));
            L.Add(q);

            // 5. Collect the surveyor's stolen gear from the trogg scavengers at the ruined dig.
            q = Quest(QuestScavengersInTheDig, "Scavengers in the Dig", 2, QuestWordForTheSurveyor, SurveyorFizzla, SurveyorFizzla, 400, 70);
            q.Description = "I came up here to map the old dig before we reopen it. The troggs had other ideas. They came crawling out of the ground at dawn, snatched every instrument I'd set out - my levels, my sighting rods, my good calipers! - and carried them off like shiny toys.\n\nThe scavengers are still picking through the dig just past my camp. Get my gear back. Six pieces should be enough to start over.";
            q.ObjectivesText = "Recover 6 pieces of Stolen Surveying Gear from the trogg scavengers at the ruined dig, then return to Surveyor Fizzla Sprocketwhistle.";
            q.ProgressText = "Any luck? I've been measuring things with my thumb, and my thumb is not calibrated.";
            q.CompletionText = "My calipers! And barely chewed! You're a marvel. Here - the stores sent these up for the dig crew, but the dig crew is you now.";
            q.Objectives.Add(Collect(ItemStolenSurveyGear, 6, "Stolen Surveying Gear"));
            q.RewardChoiceItems.Add(80); q.RewardChoiceItems.Add(210); q.RewardChoiceItems.Add(287);
            L.Add(q);

            // 6. Kill the burly troggs at the two far digs.
            q = Quest(QuestTheDeeperDig, "The Deeper Dig", 3, QuestScavengersInTheDig, SurveyorFizzla, SurveyorFizzla, 450, 90);
            q.Description = "Here's what bothers me. The scavengers are just the little ones. My readings say the troggs broke through into the valley from much deeper tunnels, and the big brutes guarding the diggings further out are the ones keeping those tunnels open.\n\nIf we're ever going to seal them, those burly troggs have to go. You'll find them at the diggings further up the valley. Eight of them, please - I'm very bad at swinging things.";
            q.ObjectivesText = "Slay 8 Burly Troggs at the diggings beyond the ruined dig, then return to Surveyor Fizzla Sprocketwhistle.";
            q.ProgressText = "Are they still thumping about out there? I can feel it through my boots.";
            q.CompletionText = "The ground's gone quiet! Well, quieter. I'll have those tunnels mapped and sealed in no time. Take a flask of water from the spring; you've been doing all the running.";
            q.Objectives.Add(Kill(BurlyTrogg, 8, "Burly Trogg slain"));
            q.RewardItems.Add(159);
            L.Add(q);

            // 7. Delivery (continued): the surveyor sends her findings on to the western watch.
            q = Quest(QuestAMessageForTheWatch, "A Message for the Watch", 3, QuestTheDeeperDig, SurveyorFizzla, MountaineerTharna, 150, 25);
            q.Description = "While you were out there, I found something odd in the trogg tunnels: tusk-carved charms, frost-rimed and still smelling of smoke. Troll work, if I'm any judge. Mountaineer Tharna Stonebrook keeps the western watch; she'll want to see these right away.\n\nFollow the valley west past the diggings. Tharna's post looks out toward the far end of Coldridge.";
            q.ObjectivesText = "Bring the surveyor's findings to Mountaineer Tharna Stonebrook at the western watch.";
            q.ProgressText = "You came from the digs? What's that you're carrying?";
            q.CompletionText = "Troll charms, in the trogg tunnels? That's no coincidence. The Snowbrand have been busier than we thought.";
            q.Objectives.Add(TalkTo(MountaineerTharna, "Speak with Mountaineer Tharna Stonebrook"));
            L.Add(q);

            // 8. Explore: scout the approach to the troll tents.
            q = Quest(QuestSmokeOverTheTents, "Smoke over the Tents", 3, QuestAMessageForTheWatch, MountaineerTharna, MountaineerTharna, 250, 50);
            q.Description = "A band of ice trolls calling themselves the Snowbrand came down out of the high passes this autumn and pitched their tents at the far end of the valley. They've been quiet, and I don't trust quiet.\n\nGet close enough to see their camp - the tents sit on the slope beyond this watch - and come back and tell me how many there are. Don't pick a fight. Not yet.";
            q.ObjectivesText = "Scout the Snowbrand trolls' tents beyond the western watch, then return to Mountaineer Tharna Stonebrook.";
            q.ProgressText = "Well? What did you see?";
            q.CompletionText = "That many, with seers among them? Then they're not passing through. They mean to stay - and they mean to take the valley.";
            q.Objectives.Add(Explore(TentsExplorePos, 14f, "Scout the Snowbrand tents"));
            L.Add(q);

            // 9. Kill youngbloods and seers at the tents.
            q = Quest(QuestSnowbrandRaiders, "Snowbrand Raiders", 4, QuestSmokeOverTheTents, MountaineerTharna, MountaineerTharna, 550, 120);
            q.Description = "If they mean to take the valley, we'll take the fight to them first. The youngbloods are eager and reckless; the seers behind them are the real danger - they call the cold down on anyone who gets close.\n\nBreak their strength. Eight youngbloods and four seers. Keep moving when the seers start chanting, or you'll feel the frost in your bones.";
            q.ObjectivesText = "Slay 8 Snowbrand Youngbloods and 4 Snowbrand Seers at the troll tents, then return to Mountaineer Tharna Stonebrook.";
            q.ProgressText = "The tents are still standing. How goes it?";
            q.CompletionText = "You fight like a mountaineer born. Pick something from my spare kit - you'll need your shoulders covered where you're going next.";
            q.Objectives.Add(Kill(SnowbrandYoungblood, 8, "Snowbrand Youngblood slain"));
            q.Objectives.Add(Kill(SnowbrandSeer, 4, "Snowbrand Seer slain"));
            q.RewardChoiceItems.Add(1379); q.RewardChoiceItems.Add(1371); q.RewardChoiceItems.Add(1445);
            L.Add(q);

            // 10. Collect the frost fetishes from the trolls.
            q = Quest(QuestFetishesOfFrost, "Fetishes of Frost", 4, QuestSnowbrandRaiders, MountaineerTharna, MountaineerTharna, 550, 150);
            q.Description = "Those charms the surveyor found - every Snowbrand carries one. The seers bless them in the cold of the ice cave, and the trolls believe they carry the winter with them wherever they go. Take the charms, and you take their courage.\n\nBring me five Snowbrand frost fetishes from the youngbloods and seers. We'll see how brave they are without them.";
            q.ObjectivesText = "Bring 5 Snowbrand Frost Fetishes from the trolls at the tents to Mountaineer Tharna Stonebrook.";
            q.ProgressText = "They'll fight harder for those charms than for their lives. Careful.";
            q.CompletionText = "Five of them. I can already hear the drums faltering. Wear this - the wind off the peaks is cruel, and you've earned a proper cloak.";
            q.Objectives.Add(Collect(ItemSnowbrandFetish, 5, "Snowbrand Frost Fetish"));
            q.RewardItems.Add(1270);
            L.Add(q);

            // 11. Final: the tribe's leader at the mouth of the ice cave; report back to the captain at Anvilmar.
            q = Quest(QuestTheRimecaller, "The Rimecaller", 5, QuestFetishesOfFrost, MountaineerTharna, CaptainDorgan, 950, 300);
            q.Description = "One troll holds the Snowbrand together: Vakjin, the one they call the Rimecaller. He keeps to the mouth of the ice cave south of the boar meadows, where the cold is strongest, and he sends his seers out to do his work.\n\nWithout the fetishes, his band is shaken. Now is the time. Go to the ice cave, bring down the Rimecaller, and then tell Captain Coalbrow the valley is ours again.";
            q.ObjectivesText = "Slay Vakjin the Rimecaller at the mouth of the ice cave, then report to Captain Dorgan Coalbrow at Anvilmar.";
            q.ProgressText = "You've got frost in your beard and fire in your eyes. Tell me.";
            q.CompletionText = "The Rimecaller, fallen? Ha! I'll have the kegs tapped tonight, and your name carved over the hearth before the week is out.\n\nAnvilmar looks after its own, and you're one of ours now. Choose one of these from the armoury, and carry it proudly out into Dun Morogh.";
            q.Objectives.Add(Kill(VakjinTheRimecaller, 1, "Vakjin the Rimecaller slain"));
            q.RewardChoiceItems.Add(791); q.RewardChoiceItems.Add(816); q.RewardChoiceItems.Add(853); q.RewardChoiceItems.Add(1196); q.RewardChoiceItems.Add(2100);
            L.Add(q);

            // 12. One class quest per class: the captain sends you to your trainer.
            L.Add(ClassQuest(QuestWarriorCalling, WowClass.Warrior, "The Warrior's Calling", WarriorTrainer, "Hurgan Oakbrand",
                "You've got the shoulders for an axe and the stubbornness to go with it. Hurgan Oakbrand has trained every warrior in this valley worth the name. Go and let him knock the rough edges off you.",
                "Dorgan sent you? Then you're either tough or foolish. Let's find out which. Come back as you grow, and I'll teach you everything that's kept me breathing."));
            L.Add(ClassQuest(QuestPaladinCalling, WowClass.Paladin, "The Paladin's Calling", PaladinTrainer, "Ysolde Brightanvil",
                "There's a light about you that has nothing to do with the snow glare. Ysolde Brightanvil teaches the Light's champions. She'll know what to make of you.",
                "Welcome, friend. The Light saw fit to bring you to Coldridge; let's see that you're ready for whatever comes next. We'll walk this road one lesson at a time."));
            L.Add(ClassQuest(QuestHunterCalling, WowClass.Hunter, "The Hunter's Calling", HunterTrainer, "Kelm Deepshot",
                "You watch the treeline like someone who knows what lives in it. Kelm Deepshot can turn a keen eye into a steady aim. Go and find him.",
                "The captain's sent me a shooter, has he? Good. Keep your powder dry and your eyes open, and come back as you grow - there's plenty more I can show you."));
            L.Add(ClassQuest(QuestRogueCalling, WowClass.Rogue, "The Rogue's Calling", RogueTrainer, "Nimsy Quickcog",
                "Quick hands, quiet feet. I won't ask where you picked those up. Nimsy Quickcog teaches that sort of thing, if you can spot her.",
                "Found me! That's a good start - most don't. Stay small, stay quiet, strike where it hurts. We'll talk again as you grow."));
            L.Add(ClassQuest(QuestPriestCalling, WowClass.Priest, "The Priest's Calling", PriestTrainer, "Odrin Emberhall",
                "The Light rests on you gently, like warmth from a hearth. Father Odrin Emberhall has guided many young priests through their first prayers. Go and sit by his fire.",
                "Ah, come in, come in. Mend what's broken, shield what's weak, and the Light will answer you. Come back whenever you are ready for more."));
            L.Add(ClassQuest(QuestMageCalling, WowClass.Mage, "The Mage's Calling", MageTrainer, "Pellia Frostspark",
                "I've seen the frost on the windows draw little patterns whenever you walk past. Pellia Frostspark studies the arcane just outside the hall - she'll want to meet you.",
                "A new apprentice! Wonderful. Arcane power rewards the curious and punishes the careless - so we'll start small, and grow from there."));
            L.Add(ClassQuest(QuestWarlockCalling, WowClass.Warlock, "The Warlock's Calling", WarlockTrainer, "Grizwick Umbergear",
                "I won't pretend to understand the path that calls to you, but I won't stand in your way either. Grizwick Umbergear keeps to himself beside Anvilmar. Speak with him - and keep your wits about you.",
                "The captain sent you to me? He's more open-minded than his beard suggests. Power has a price; learn to pay it on your own terms, and you'll never be its servant."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestAColdWelcome, CaptainDorgan, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches in front of Anvilmar. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " in front of Anvilmar.";
            q.ProgressText = "Captain Coalbrow mentioned you might come by.";
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

        // Class start items = CharStartOutfit.dbc (identical for both sexes), minus 6948 (hearthstone) and 2516 (bullets).
        // ClassStart has no race field, so a class the two races share uses the Dwarf row (race 3, listed first); the Gnome rows
        // differ only in the weapon: warrior 49778 (two-hand sword) instead of 12282, rogue 28979 (throwing knife) instead of 25861.
        // Mage and Warlock exist only for Gnomes (race 7 rows); Paladin, Hunter and Priest only for Dwarves.
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, StartItems = new[] { 12282, 38, 39, 40 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Paladin, StartItems = new[] { 2361, 6117, 6118, 43 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Hunter, StartItems = new[] { 12282, 148, 147, 129, 2508, 2102 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, StartItems = new[] { 2092, 49, 48, 47, 50055, 25861 }, StartSpells = new int[0] });
            // Integrator (iteration 3): ClassStart.Race now exists - the Gnome (race 7) rows the zone owner listed win for Gnomes.
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warrior, Race = 7, StartItems = new[] { 49778, 38, 39, 40 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, Race = 7, StartItems = new[] { 2092, 49, 48, 47, 50055, 28979 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 35, 6098, 53, 52, 51 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Mage, StartItems = new[] { 35, 56, 1395, 6096, 55 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warlock, StartItems = new[] { 35, 57, 6097, 1396, 59 }, StartSpells = new int[0] });
        }
    }
}
