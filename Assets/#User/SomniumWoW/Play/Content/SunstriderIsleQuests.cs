using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class SunstriderIsle
    {
        /// <summary>Where the explore objective of "The Old Ring" completes: open ground just north of the ring-shaped building in the
        /// southern bowl (its footprint spans about X 10082..10142, Y -6190..-6270 on the ContentDPlace map); terrain height 16.79 in
        /// area 3431 (probe ContentDAt), re-checked by the ContentD probe.</summary>
        public static readonly WowPos OldRingExplorePos = new WowPos(10155f, -6232f, 16.79f, 0f);

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

            // 1. Intro: the magister at the start sends you to the ranger a few steps away.
            q = Quest(QuestMorningOnTheIsle, "A Morning on the Isle", 1, 0, MagisterAerondis, RangerTalvaria, 80, 0);
            q.Description = "Every novice of our people begins here, on this island, under the eyes of the magisters. It is a gentle place - mostly - and it will teach you the first things you need to know about your gifts and yourself.\n\nBefore you learn anything grand, learn to be useful. Ranger Talvaria keeps the lawns safe. Go to her, and do as she asks.";
            q.ObjectivesText = "Speak with Ranger Talvaria on the plaza near Magister Aerondis.";
            q.ProgressText = "Another new face. Let me guess - the magister sent you.";
            q.CompletionText = "Good. I can always use another pair of hands that do not tremble. Stay close to the plaza for now; you will know when you are ready to wander further.";
            q.Objectives.Add(TalkTo(RangerTalvaria, "Speak with Ranger Talvaria"));
            L.Add(q);

            // 2. Kill mana wyrms on the lawns around the plaza.
            q = Quest(QuestWyrmsOnTheLawns, "Wyrms on the Lawns", 1, QuestMorningOnTheIsle, RangerTalvaria, RangerTalvaria, 250, 35);
            q.Description = "The mana wyrms are pretty, I will give them that. They are also greedy. They gather wherever the ley energy rises close to the surface, and this season they have been gathering on our lawns - nipping at novices and draining the wards around the spire.\n\nThin them out. Eight should teach the rest some manners.";
            q.ObjectivesText = "Slay 8 Mana Wyrms on the lawns around the plaza, then return to Ranger Talvaria.";
            q.ProgressText = "I can still see them glittering over the grass. Keep at it.";
            q.CompletionText = "Much quieter. The wardstones will thank you, even if they cannot say so. Here - something to eat. Novices always forget to eat.";
            q.Objectives.Add(Kill(ManaWyrm, 8, "Mana Wyrm slain"));
            q.RewardItems.Add(4540);
            L.Add(q);

            // 3. Collect lynx pelts from the Eversong lynxes in the southern bowl.
            q = Quest(QuestPeltsForTheWatch, "Pelts for the Watch", 2, QuestWyrmsOnTheLawns, RangerTalvaria, RangerTalvaria, 350, 60);
            q.Description = "The lynxes south of the plaza have grown bold. They used to keep to the meadows around the old ring; now they stalk the path and circle anyone walking alone.\n\nThe watch needs warm cloaks for the night posts anyway. Bring me six good pelts from the Eversong lynxes in the southern bowl. Leave the cubs on the western slope alone - they are no threat to anyone.";
            q.ObjectivesText = "Bring 6 Eversong Lynx Pelts from the lynxes in the southern bowl to Ranger Talvaria.";
            q.ProgressText = "Those claws are sharper than they look. Did you manage it?";
            q.CompletionText = "Clean cuts, and not a single tear. You have a steady hand. Take a pair of these - the southern paths are rough on soft novice feet.";
            q.Objectives.Add(Collect(ItemLynxPelt, 6, "Eversong Lynx Pelt"));
            q.RewardChoiceItems.Add(20985); q.RewardChoiceItems.Add(20921); q.RewardChoiceItems.Add(20915);
            L.Add(q);

            // 4. Explore the old ring in the southern bowl and report to the arcanist in the south-east - the start of the chain across
            //    the isle (magister at the start -> arcanist in the south-east -> lieutenant in the south-west -> back to the magister).
            q = Quest(QuestTheOldRing, "The Old Ring", 2, QuestMorningOnTheIsle, MagisterAerondis, ArcanistVeyselle, 300, 50);
            q.Description = "Arcanist Veyselle has been studying the ley lines in the south of the isle, and her last letter troubled me. She writes that the currents near the old ring in the southern bowl have begun to twist.\n\nWalk down to the old ring and take a look for yourself - then go on to Veyselle, below the hill in the south-east, and tell her what you saw. A fresh pair of eyes may notice what a tired scholar does not.";
            q.ObjectivesText = "Visit the old ring in the southern bowl, then speak with Arcanist Veyselle in the south-east of the isle.";
            q.ProgressText = "You have come from the ring? Tell me everything.";
            q.CompletionText = "A prickling on the skin, and the grass leaning the wrong way... yes. That is exactly what I feared. The currents are being pulled - drawn toward something in the south-west. Drink this; there is more walking ahead of you.";
            q.Objectives.Add(Explore(OldRingExplorePos, 15f, "Visit the old ring"));
            q.RewardItems.Add(159);
            L.Add(q);

            // 5. Arcane residue from the arcane wraiths (south-east hill and the south).
            q = Quest(QuestResidueOfTheWraiths, "Residue of the Wraiths", 3, QuestTheOldRing, ArcanistVeyselle, ArcanistVeyselle, 550, 100);
            q.Description = "The arcane wraiths are old things - loose knots of magic left over from the spells that raised the spire. Normally they drift about harmlessly. Now they lash out at anyone who comes near.\n\nWhen a wraith comes apart, it leaves a glittering residue behind. Bring me six measures of it from the wraiths on the hill above me and in the south. The residue remembers which way the currents were pulling.";
            q.ObjectivesText = "Collect 6 measures of Shimmering Arcane Residue from the Arcane Wraiths, then return to Arcanist Veyselle.";
            q.ProgressText = "Careful - the residue stings if you hold it too long.";
            q.CompletionText = "Six measures, and every one of them leaning south-west, like iron filings toward a magnet. Something is drinking from the ley lines. Wear these; they are warded against stray sparks.";
            q.Objectives.Add(Collect(ItemArcaneResidue, 6, "Shimmering Arcane Residue"));
            q.RewardChoiceItems.Add(20988); q.RewardChoiceItems.Add(20922); q.RewardChoiceItems.Add(20916);
            L.Add(q);

            // 6. Delivery across the isle: from the arcanist (south-east) to the lieutenant (south-west).
            q = Quest(QuestWarningForTheLieutenant, "A Warning for the Lieutenant", 3, QuestResidueOfTheWraiths, ArcanistVeyselle, LieutenantDathrien, 150, 25);
            q.Description = "I cannot leave my instruments, and this cannot wait. Lieutenant Dathrien keeps watch over the hollow in the south-west - exactly where the currents are being pulled.\n\nFind him and tell him what the residue shows. He will know better than I what to do with a warning like this.";
            q.ObjectivesText = "Speak with Lieutenant Dathrien in the south-west of the isle.";
            q.ProgressText = "You came the long way round. What news?";
            q.CompletionText = "Drawn toward the hollow. That explains the wraiths down there - they turned green and vicious three days ago. Keep this phial close. You will want it before the day is done.";
            q.Objectives.Add(TalkTo(LieutenantDathrien, "Speak with Lieutenant Dathrien"));
            q.RewardItems.Add(118);
            L.Add(q);

            // 7. Kill the tainted arcane wraiths in the south-west hollow.
            q = Quest(QuestTheTaintedHollow, "The Tainted Hollow", 4, QuestWarningForTheLieutenant, LieutenantDathrien, LieutenantDathrien, 550, 120);
            q.Description = "Look down there. The wraiths in that hollow were blue like the others until recently. Now they burn a sickly green, and they attack anything that moves - my rangers included.\n\nWhatever tainted them is still at work. Until we find it, the hollow must be held. Destroy seven of the tainted wraiths and push the rest back.";
            q.ObjectivesText = "Destroy 7 Tainted Arcane Wraiths in the south-west hollow, then return to Lieutenant Dathrien.";
            q.ProgressText = "Still green light down there. Keep pushing.";
            q.CompletionText = "Well fought. You held your ground better than some of my rangers. But the taint is not fading, and that means its source is still out there, further down the hollow.";
            q.Objectives.Add(Kill(TaintedArcaneWraith, 7, "Tainted Arcane Wraith destroyed"));
            q.RewardChoiceItems.Add(20990); q.RewardChoiceItems.Add(20925); q.RewardChoiceItems.Add(20919);
            L.Add(q);

            // 8. Corrupted crystals from the tainted wraiths.
            q = Quest(QuestCorruptedCrystals, "Corrupted Crystals", 4, QuestTheTaintedHollow, LieutenantDathrien, LieutenantDathrien, 650, 150);
            q.Description = "One of my rangers found this inside a fallen wraith: a mana crystal, cut by a jeweller's hand and turned dark from the inside. Someone planted these in the wraiths deliberately, to twist them and to draw the island's power through them.\n\nBring me five more. If they were cut by a magister's hand, I want to know whose.";
            q.ObjectivesText = "Recover 5 Corrupted Mana Crystals from the Tainted Arcane Wraiths, then return to Lieutenant Dathrien.";
            q.ProgressText = "Handle them carefully. They whisper, if you listen too long.";
            q.CompletionText = "Five crystals, and every one bears the same sigil on its facets. Velanya's. She was cast out of the magisters' circle for draining the wells she was sworn to guard - and it seems she never truly left.";
            q.Objectives.Add(Collect(ItemCorruptedCrystal, 5, "Corrupted Mana Crystal"));
            q.RewardChoiceItems.Add(20987); q.RewardChoiceItems.Add(20923); q.RewardChoiceItems.Add(20917);
            L.Add(q);

            // 9. Final: the exiled magistrix at the far end of the hollow; report back to the magister at the start.
            q = Quest(QuestTheExilesEnd, "The Exile's End", 5, QuestCorruptedCrystals, LieutenantDathrien, MagisterAerondis, 1000, 300);
            q.Description = "Magistrix Velanya hides at the far end of the hollow, beyond her tainted wraiths, feeding on the currents she has twisted toward herself. She is no novice. She will fight with ice and fury, and she will not surrender.\n\nEnd her theft. Then return to Magister Aerondis at the spire and tell him the isle's ley lines are free again. He trained her once - he deserves to hear it from you.";
            q.ObjectivesText = "Defeat Magistrix Velanya the Exiled at the far end of the south-west hollow, then report to Magister Aerondis at the spire.";
            q.ProgressText = "You have the look of someone who has been to the hollow and back. Speak.";
            q.CompletionText = "So Velanya has fallen at last. I taught her once, long ago, and I hoped she would find her way back to us. Instead it was you who found her.\n\nChoose one of these, with the thanks of the whole isle. You arrived here a novice - you leave it one of us. Eversong is waiting.";
            q.Objectives.Add(Kill(MagistrixVelanya, 1, "Magistrix Velanya the Exiled defeated"));
            q.RewardChoiceItems.Add(20977); q.RewardChoiceItems.Add(20981); q.RewardChoiceItems.Add(20910); q.RewardChoiceItems.Add(20983);
            L.Add(q);

            // 10. One class quest per class: the magister sends you to your trainer on the plaza.
            L.Add(ClassQuest(QuestPaladinPath, WowClass.Paladin, "The Paladin's Choice", PaladinTrainer, "Saelthar",
                "Few of our people take up the Light as a weapon and a shield. You carry yourself as though you already have. Saelthar can teach you what that choice demands.",
                "Aerondis sent you? Then he sees what I see. Stand straight, novice. The Light will ask a great deal of you - and give back more, if you are worthy."));
            L.Add(ClassQuest(QuestHunterPath, WowClass.Hunter, "The Hunter's Patience", HunterTrainer, "Nerilla",
                "You watched the lynxes on the slope for a long time this morning. A hunter's habit. Nerilla will be glad to meet someone with that kind of patience.",
                "The magister has a good eye for talent. Let us see if you have a good eye for everything else. Watch, wait, and strike once."));
            L.Add(ClassQuest(QuestRoguePath, WowClass.Rogue, "The Rogue's Shadow", RogueTrainer, "Kaeldris",
                "Twice today I turned around and found you standing behind me. Unsettling - and promising. Kaeldris lurks somewhere on the plaza. If you can find him, he will teach you.",
                "Hm. You found me faster than most. Do not let it go to your head. Soft steps, quick hands, and never the same trick twice."));
            L.Add(ClassQuest(QuestPriestPath, WowClass.Priest, "The Priest's Vow", PriestTrainer, "Solaira",
                "You spent the morning tending a novice who scraped her knee on the steps. Small kindnesses are where every priest begins. Solaira will show you the rest.",
                "Welcome, child. Your hands are steady and your heart is kind. We begin with the simplest prayers - the rest will follow as you grow."));
            L.Add(ClassQuest(QuestMagePath, WowClass.Mage, "The Mage's Focus", MageTrainer, "Vaeryn",
                "The ley energy on this isle bends toward you when you pass. That is a gift, and a danger. Vaeryn teaches novices how to master one without falling to the other.",
                "Aerondis was right to send you. I can feel the currents stirring around you already. Now we teach them to obey."));
            L.Add(ClassQuest(QuestWarlockPath, WowClass.Warlock, "The Warlock's Resolve", WarlockTrainer, "Zarethil",
                "There is a hunger for power in you, novice. Better that it is guided than left to grow wild. Zarethil teaches the discipline such a hunger requires.",
                "So the magister sent you to me. Good. Power listens to those who command it firmly and fear it wisely. We will start with both."));
        }

        static QuestDef ClassQuest(int id, WowClass cls, string title, int trainer, string trainerName, string intro, string completion)
        {
            var q = Quest(id, title, 1, QuestMorningOnTheIsle, MagisterAerondis, trainer, 100, 0);
            q.ClassOnly = cls;
            q.Description = intro + "\n\n" + trainerName + " teaches on the plaza around the spire. Seek them out and learn what they can show you.";
            q.ObjectivesText = "Speak with " + trainerName + " on the plaza around the spire.";
            q.ProgressText = "Aerondis spoke of you.";
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

        // Class start items = CharStartOutfit.dbc race 10 (identical for both sexes), minus 6948 (hearthstone) and 2512 (arrows).
        static void AddClassStarts(ZoneContent z)
        {
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Paladin, StartItems = new[] { 23346, 24143, 24145, 24146 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Hunter, StartItems = new[] { 12282, 20901, 20899, 20900, 20980, 2101 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Rogue, StartItems = new[] { 20982, 20897, 20896, 20898, 50057, 28979 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Priest, StartItems = new[] { 20978, 20891, 53, 52, 51 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Mage, StartItems = new[] { 20978, 20893, 20894, 6096, 20895 }, StartSpells = new int[0] });
            z.ClassStarts.Add(new ClassStart { Class = WowClass.Warlock, StartItems = new[] { 20978, 20892, 1396, 59, 6097 }, StartSpells = new int[0] });
        }
    }
}
