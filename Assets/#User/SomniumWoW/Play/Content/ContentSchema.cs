using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    // CONTRACT (shared, pure C#). The data an offline zone is made of. The Content owner fills it (Shadowglen.cs); the
    // Rules owner's offline simulation runs it. Models, textures, icons and spells are NOT here - they are read from the
    // player's game files at runtime by id/path. Quest text is our own writing (never copied from the game).
    // Owners may ADD fields; do not rename or remove.

    public enum ObjectiveKind : byte { Kill = 0, Collect = 1, TalkTo = 2, Explore = 3 }

    public sealed class ZoneContent
    {
        public string Name;
        public int MapId;
        public WowPos PlayerStart;
        public WowPos Graveyard;                     // where ReleaseSpirit brings the player back
        public List<CreatureTemplate> Creatures = new List<CreatureTemplate>();
        public List<SpawnDef> Spawns = new List<SpawnDef>();
        public List<QuestDef> Quests = new List<QuestDef>();
        public List<ItemDef> Items = new List<ItemDef>();
        public List<ClassStart> ClassStarts = new List<ClassStart>();
    }

    public sealed class CreatureTemplate
    {
        public int Entry;
        public string Name;
        public string SubName;
        public int[] DisplayIds;                     // CreatureDisplayInfo ids, one picked per spawn
        public int MinLevel = 1, MaxLevel = 1;
        public Reaction Reaction = Reaction.Hostile;
        public bool IsNpc;                           // friendly townsperson / quest giver (never attacks)
        public float Scale = 1f;
        public float WalkSpeed = 2.5f, RunSpeed = 7f; // yards per second
        public float AggroRadius = 10f;              // yards at equal level (hostiles only)
        public float HealthMultiplier = 1f, DamageMultiplier = 1f;
        public float AttackSpeed = 2f;               // seconds between swings
        public int MinMoney, MaxMoney;               // copper dropped
        public List<LootDef> Loot = new List<LootDef>();
        public string Greeting;                      // NPC gossip text (our own writing)
        public int MainHandDisplayId, OffHandDisplayId; // ItemDisplayInfo ids the NPC visibly holds (0 = none)
        public int Emote;                            // AnimationData id the NPC loops idle (0 = stand)
        // Added by Rules (iteration 2):
        public int UnitClass = 1;                    // creature_classlevelstats class for base health/damage: 1 warrior (beasts), 2 paladin, 8 mage (casters: less health/damage)
        public bool Elite;                           // x2 kill XP (Formulas.cpp Gain); the Content owner scales health/damage with the multipliers
        // Added by Rules (iteration 3):
        public int CreatureType;                     // CreatureType.dbc: 1 Beast, 2 Dragonkin, 3 Demon, 4 Elemental, 5 Giant, 6 Undead, 7 Humanoid, 8 Critter, 9 Mechanical, 10 Not specified; 0 = unknown (every type check passes). Used by Polymorph (beast/humanoid/critter only), Cannibalize (humanoid/undead corpses), Beast Slaying
        public int[] Spells;                         // Spell.dbc ids the creature casts in combat (damage bolts, DoTs, self heals/buffs, debuffs on the player), e.g. a caster mob's Shadow Bolt; null = melee only. Emits CastStarted/CastSucceeded with SourceGuid = the creature
        public float SpellInterval = 8f;             // seconds between the creature's casts (the first one 2-4 s after it engages)
    }

    public sealed class SpawnDef
    {
        public int Entry;
        public WowPos Pos;
        public float WanderRadius;                   // yards, 0 = stands still
        public float RespawnSeconds = 60f;
    }

    public sealed class LootDef
    {
        public int ItemEntry;
        public float Chance = 1f;                    // 0..1
        public int QuestId;                          // quest item: drops only while this quest needs it
        public int MinCount = 1, MaxCount = 1;
    }

    public sealed class ItemDef
    {
        public int Entry;                            // a real Item.dbc id, or >= 900000 for an invented quest item
        public string Name;
        public string IconPath;                      // Interface\Icons\<name> (BLP in the MPQs)
        public int DisplayId;                        // ItemDisplayInfo id (0 = take it from Item.dbc by Entry)
        public int Quality = 1;
        public int InventoryType;                    // Item.dbc InventoryType (0 = not equippable)
        public int SheatheType;                      // 0 = take it from Item.dbc
        public int QuestId;                          // quest item for this quest
        public float MinDamage, MaxDamage, Speed;    // weapons
        public int Armor, Stamina, Strength, Agility, Intellect, Spirit;
        public int SellPrice;
        public int HealAmount, ManaAmount;           // consumables (food/drink/potion): restored over UseSeconds
        public float UseSeconds;
    }

    public sealed class QuestDef
    {
        public int Id;
        public string Title;
        public int Level = 1, MinLevel = 1;
        public int PrevQuestId;                      // must be rewarded first (0 = none)
        public WowClass ClassOnly = WowClass.None;   // None = every class
        public int GiverEntry, EnderEntry;           // CreatureTemplate entries
        public string Description, ObjectivesText, ProgressText, CompletionText; // our own writing
        public List<QuestObjectiveDef> Objectives = new List<QuestObjectiveDef>();
        public int RewardXp, RewardMoney;
        public List<int> RewardItems = new List<int>();
        public List<int> RewardChoiceItems = new List<int>();
    }

    public sealed class QuestObjectiveDef
    {
        public ObjectiveKind Kind;
        public int TargetEntry;                      // creature entry (Kill), item entry (Collect), NPC entry (TalkTo)
        public int Count = 1;
        public string Text;                          // e.g. "Young Nightsaber slain"
        public WowPos ExplorePos;                    // Explore
        public float ExploreRadius = 10f;
    }

    /// <summary>What a new character of a class starts with.</summary>
    public sealed class ClassStart
    {
        public WowClass Class;
        public int Race;                             // ChrRaces id this row is for; 0 = any race (a row of the character's own race wins)
        public int[] StartItems;                     // ItemDef entries: equipped when equippable, else in the bags
        public int[] StartSpells;                    // Spell.dbc ids known at level 1 (Rules may add the class basics)
    }
}
