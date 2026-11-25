using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // CONTRACT (shared, pure C#). The game-state types every view reads, whoever runs the game: the offline simulation
    // now, the AzerothCore network client later. Positions are WoW world coordinates (yards; X north, Y west, Z up;
    // orientation radians) - Unity code converts through WowSpace. Owners may ADD fields; do not rename or remove.

    /// <summary>ChrClasses.dbc ids.</summary>
    public enum WowClass : byte { None = 0, Warrior = 1, Paladin = 2, Hunter = 3, Rogue = 4, Priest = 5, DeathKnight = 6, Shaman = 7, Mage = 8, Warlock = 9, Druid = 11 }

    /// <summary>Unit power types as the client numbers them.</summary>
    public enum PowerType : byte { Mana = 0, Rage = 1, Focus = 2, Energy = 3, Happiness = 4, RunicPower = 6, None = 255 }

    public enum EntityKind : byte { Creature = 0, Npc = 1, GameObject = 2, Player = 3, Pet = 4, Totem = 5 }

    public enum Reaction : byte { Hostile = 0, Neutral = 1, Friendly = 2 }

    /// <summary>ChrRaces.dbc "Alliance" field of the playable races (measured: 0 = Alliance for 1,3,4,7,11; 1 = Horde for 2,5,6,8,10). Added by Rules (iteration 3).</summary>
    public enum WowFaction : byte { Alliance = 0, Horde = 1 }

    /// <summary>What the character holds: weapons on the back/hip, melee weapons drawn, or the ranged weapon drawn.</summary>
    public enum SheathState : byte { Sheathed = 0, Melee = 1, Ranged = 2 }

    /// <summary>The marker floating over an NPC: "!" yellow = available, "?" grey = in progress, "?" yellow = hand in.</summary>
    public enum QuestMarker : byte { None = 0, Available = 1, Incomplete = 2, Complete = 3 }

    public enum QuestState : byte { Unavailable = 0, Available = 1, Active = 2, Complete = 3, Rewarded = 4 }

    /// <summary>Equipment slots in the client's order.</summary>
    public enum EquipSlot : byte
    {
        Head = 0, Neck = 1, Shoulders = 2, Shirt = 3, Chest = 4, Waist = 5, Legs = 6, Feet = 7, Wrists = 8, Hands = 9,
        Finger1 = 10, Finger2 = 11, Trinket1 = 12, Trinket2 = 13, Back = 14, MainHand = 15, OffHand = 16, Ranged = 17, Tabard = 18
    }

    /// <summary>A WoW world position with orientation.</summary>
    public struct WowPos
    {
        public float X, Y, Z, O;
        public WowPos(float x, float y, float z, float o) { X = x; Y = y; Z = z; O = o; }
        public override string ToString() { return "(" + X.ToString("0.0") + ", " + Y.ToString("0.0") + ", " + Z.ToString("0.0") + ", o " + O.ToString("0.00") + ")"; }
    }

    /// <summary>
    /// Everything a view needs to draw one creature/NPC/object. The session keeps one instance per entity and updates it
    /// in place; views read it every frame (do not cache values across frames).
    /// </summary>
    public sealed class EntityState
    {
        public ulong Guid;
        public EntityKind Kind;
        public int Entry;                 // creature/NPC template id
        public int DisplayId;             // CreatureDisplayInfo id (WowCreatures.Build)
        public string Name;
        public string SubName;            // e.g. "Druid Trainer"
        public int Level;
        public int Health, MaxHealth;
        public PowerType Power = PowerType.None;
        public int PowerValue, PowerMax;
        public WowPos Pos;
        public bool Moving, Running;
        public Reaction Reaction;
        public bool Dead;
        public bool Lootable;             // dead and has loot for the player (sparkle)
        public bool InCombat;
        public ulong TargetGuid;
        public QuestMarker Marker;
        public float Scale = 1f;
        public int MainHandDisplayId, OffHandDisplayId;   // ItemDisplayInfo ids an NPC visibly holds (0 = none)
        public SheathState Sheath;
        public int AttackCounter;         // +1 on every melee swing: play the attack animation
        public int WoundCounter;          // +1 when hit: play the wound animation
        public int CastingSpellId;        // 0 = not casting
        public float CastProgress;        // 0..1
        public int Emote;                 // AnimationData id to play once, 0 = none (views clear nothing; they play on change)
        public int EmoteCounter;          // +1 each time Emote is (re)triggered
        public bool EmoteOnce;
        public bool NotSelectable;        // lead fix 2026-10-14: UNIT_FLAG_NOT_SELECTABLE 0x02000000 (UnitDefines.h) - triggers, "Watcher" stalkers            // lead fix 2026-10-11: Emote came from a spell kit (a trainer's cast) - one play, never the idle loop
        public ulong OwnerGuid;           // Pet / Totem: who summoned it (the player's guid for the player's own)

        // Online (iteration 6, lead contract): another player's WoW character look (EntityKind.Player), from the server's
        // update fields, for WowCharacter.Build. Race 0 = not a player / unknown.
        public int Race, Gender, Skin, Face, HairStyle, HairColor, FacialHair;
        public int[] ItemDisplayIds;      // by EquipSlot (19), 0 = empty; null until known
        public bool IsSomniumPlayer;      // this character belongs to a Somnium player in this world: draw nothing (their avatar is there)
        // Added by World (iteration 6, online) - from the server's update fields (offline leaves them 0/null):
        public int StandState;            // UNIT_FIELD_BYTES_1 byte 0: 0 stand, 1 sit, 2-6 sit on a chair, 3 sleep, 7 dead, 8 kneel
        public ulong CorpseOwnerGuid;     // states fix 2026-10-04: this entity is a player's CORPSE object (TYPEID_CORPSE): CORPSE_FIELD_OWNER, 0 = not a corpse
        public int EmoteState;            // UNIT_NPC_EMOTESTATE: looping emote (Emotes.dbc id, 0 = none), e.g. an NPC working at an anvil
        public int MountDisplayId;        // UNIT_FIELD_MOUNTDISPLAYID: CreatureDisplayInfo id of the mount it rides (0 = none)
        public string ModelPath;          // GameObject: GameObjectDisplayInfo model (.m2, or .wmo for big ones); DisplayId = GameObjectDisplayInfo id
        public int GoType;                // GameObject: GameobjectTypes (0 door, 1 button, 2 questgiver, 3 chest, 5 generic, 8 spell focus, 9 text, 10 goober, 19 mailbox, 23 meeting stone...)
        public int GoState;               // GameObject: GOState 0 active (door open), 1 ready (door closed), 2 active alternative
        public bool Spawned;              // duelsnd: created by UPDATETYPE_CREATE_OBJECT2 (AC Object.cpp:209-218: duel arbiter, traps, flag stands / drops, owned objects) - the view plays its Spawn
        public float RotX, RotY, RotZ, RotW; // GameObject: world rotation quaternion in WoW axes (all 0 = none: use Pos.O)
        // Added by Rules (iteration 2):
        public List<AuraView> Auras = new List<AuraView>();   // visible buffs/debuffs (e.g. the player's Rend on it)
        public int ComboPoints;           // the player's combo points on this unit
        public bool Evading;              // running home, immune ("Evade")
        public int ConColor;              // 0 gray, 1 green, 2 yellow, 3 orange, 4 red (level vs the player)
        // Added by Rules (iteration 3):
        public bool Channeling;           // CastingSpellId is a channel (CastProgress runs 0..1 over the channel)
        public ulong CastTargetGuid;      // who the current cast/channel is aimed at (1 = the player)
        public int CreatureType;          // CreatureType.dbc id from the template (0 unknown); pets 3 Demon, totems 11 Totem
        // Iteration 7 (lead contract): mounts
        public bool Mounted;              // online: UNIT_FLAG_MOUNT 0x08000000 in UNIT_FIELD_FLAGS
        public bool Flying, Swimming;     // online: MOVEMENTFLAG_FLYING 0x02000000 / SWIMMING 0x200000 of its last movement
        public float Pitch;               // radians, from its movement when Flying / Swimming
        // ==== Iteration 11 (wave 0, w0play: every proposal's EntityState append merged here, append-only; owners in brackets) ====
        // Iteration 11 (owner social): another player's guild (PLAYER_GUILDID -> CMSG_GUILD_QUERY name), null = none / not known yet.
        // For the nameplate "<Guild>" line (WoW's SHOW_GUILD_NAMES, GlobalStrings.lua 6275) - entities area, optional (H12).
        public string GuildName;
        // Iteration 11 (designer spellfx):
        public int[] EquipEnchants;       // players: by EquipSlot (19), SpellItemEnchantment ids, permanent | temporary << 16 (PLAYER_VISIBLE_ITEM_n_ENCHANTMENT); null until known
        public uint AuraStateMask;        // UNIT_FIELD_AURASTATE as the server shows it to us (bit n-1 = AuraStateType n); offline: the target rules
        public bool Stealthed;            // other units: UNIT_FIELD_BYTES_1 byte 2 & UNIT_STAND_FLAGS_CREEP 0x02 - drawn translucent (offline creatures: false).
                                          // Our own PlayerState.Stealthed online + the Somnium avatar's stealth shell are proposal-avatar.md's (their s5.1 / hook 1).
        // Iteration 11 (designer trainer): a looted corpse that can be skinned / gathered (online: UNIT_FLAG_SKINNABLE + the creature's type_flags)
        public bool Skinnable;            // UNIT_FIELD_FLAGS & 0x04000000 (AC UnitDefines.h:283): Interact casts the gathering spell
        public int SkinSkill;             // the SkillLine that gathers it: 393 Skinning, 182 Herbalism, 186 Mining, 202 Engineering (AC CreatureData.h:252-262); 0 = not skinnable
        // Iteration 11 (motion, lead contract): transports and flight masters
        public bool IsTransport;          // GoType 11 TRANSPORT (elevator, tram car) / 15 MO_TRANSPORT (boat, zeppelin): Pos (and Pos.O) follow its path every Tick, draw it unsmoothed
        public ulong TransportGuid;       // this unit / object rides a transport (Pos is already its world position), 0 = none
        public sbyte TaxiStatus = -1;     // a flight master: -1 unknown / not one, 0 its node is not discovered yet, 1 discovered (SMSG_TAXINODE_STATUS)
        // Iteration 11 (avatar): online UNIT_FIELD_NATIVEDISPLAYID (0x44) - DisplayId != NativeDisplayId = transformed / shapeshifted; offline 0.
        public int NativeDisplayId;
        // Iteration 11 (area talents):
        public int ChosenTitle;           // another player's PLAYER_CHOSEN_TITLE (CharTitles bit_index), 0 none
        // Lead fix (2026-10-03, the user: "Elite mobs are not being displayed properly", "wrong icon for vendors", "wrong icons for when gathering ore"):
        public int Classification;        // creature_template.rank (SMSG_CREATURE_QUERY_RESPONSE): 0 normal, 1 elite, 2 rare elite, 3 world boss, 4 rare (UnitClassification)
        public string CursorName;         // the cursor WoW shows over it: creature_template / gameobject_template IconName ("Buy", "Directions", "Taxi" ...), null/"" = by kind
        public int GatherLockType;        // GameObject: the LockType of its lock's skill key (1 PickLock, 2 Herbalism, 3 Mining, 19 Fishing - SharedDefines.h:2596-2614), 0 none
        public int GatherSkillNeeded;     // GameObject: the skill value that key needs (Lock.dbc Skill[i]), 0 none
        // Lead (2026-10-03, the user: "make the jumping of people on WoW client look good on Somnium"):
        public bool Airborne;             // in a jump / fall (online: MOVEMENTFLAG_FALLING of its last movement, cleared when it lands)
        public int JumpCounter;           // +1 on every jump start (online MSG_MOVE_JUMP): the view plays JumpStart
        // Iteration 11 (area pets). The vehicle a unit sits on is area motion's EntityState.TransportGuid (proposal-motion s5.1, "this unit /
        // object rides a transport, Pos is already its world position") when that guid is a UNIT (high 0xF130 / 0xF140 / 0xF150 / a player).
        public ulong CharmedByGuid;       // UNIT_FIELD_CHARMEDBY: who controls this unit (a driven vehicle, a possessed / charmed unit), 0 = none
        public int VehicleId;             // Vehicle.dbc id when this unit IS a vehicle (UPDATEFLAG_VEHICLE, SMSG_PLAYER_VEHICLE_DATA), 0 = not
        public int SeatIndex = -1;        // its seat on the vehicle TransportGuid (MovementInfo transport seat / SMSG_MONSTER_MOVE_TRANSPORT int8), -1 = none
        public bool HiddenInSeat;         // its seat hides the passenger (VEHICLE_SEAT_FLAG_HIDE_PASSENGER 0x200): views draw nothing for it
        public int SeatAnim;              // AnimationData id the passenger loops while seated (VehicleSeat field 16 m_rideAnimLoop), 0 = none
        public bool SpellClick;           // UNIT_NPC_FLAG_SPELLCLICK 0x01000000: a right-click uses it (CMSG_SPELLCLICK - most quest vehicles)
        public bool PlayerVehicle;        // UNIT_NPC_FLAG_PLAYER_VEHICLE 0x02000000: a player's vehicle mount with a free seat (CMSG_PLAYER_VEHICLE_ENTER)
        public bool DisableMove;          // integrator (vehicle request): UNIT_FLAG_DISABLE_MOVE 0x4 (UnitDefines.h) - a turret (Scarlet Cannon, harpoon gun); online only
    }

    /// <summary>A class ability as the spellbook / action bar shows it.</summary>
    public sealed class AbilityView
    {
        public int SpellId;
        public string Name;
        public string Rank;               // "Rank 1", "" for none
        public string IconPath;           // Interface\Icons\... (BLP, read at runtime)
        public string Description;        // tooltip text with $-tokens resolved
        public PowerType CostType = PowerType.None;
        public int Cost;
        public float CastTime;            // seconds, 0 = instant
        public float Cooldown;            // seconds
        public float CooldownRemaining;   // seconds (includes the global cooldown when it is longer)
        public float RangeMin, RangeMax;  // yards
        public bool Harmful;              // targets enemies
        public bool NeedsTarget;
        public bool Passive;
        public bool Usable;               // right now: enough power, not on cooldown, target valid and in range
        public string UnusableReason;     // e.g. "Not enough rage", "Out of range", null when usable
        public int LearnedAtLevel;
        // Added by Rules (iteration 2):
        public bool Active;               // toggles that are on right now: current stance/form, Stealth, Auto Shot, auto-attack, a queued on-next-swing ability
        public string Tooltip;            // Name + Rank + cost/range/cast lines + Description, ready for a tooltip panel
        // Iteration 7 (lead contract):
        public bool IsMount;              // an entry of PlayerState.Mounts
        // Iteration 11 (designer spellfx; wave 0, w0play):
        public SpellTargetingKind Targeting;   // what the spell asks for before it is cast: Ground (the circle), Item, None
    }

    public sealed class AuraView
    {
        public int SpellId;
        public string Name;
        public string IconPath;
        public float Remaining, Duration; // seconds, Duration 0 = permanent
        public int Stacks = 1;
        public bool Harmful;
        public ulong CasterGuid;
        public string Tooltip;            // stage S1 (integrator): an extra tooltip line (the Dreamport's own "Immersive Mode" buff), null for game auras
    }

    public sealed class ItemView
    {
        public int Entry;
        public int DisplayId;             // ItemDisplayInfo id
        public string Name;
        public string IconPath;
        public int Quality;               // 0 poor .. 5 legendary
        public int Count = 1;
        public int InventoryType;         // Item.dbc InventoryType
        public int SheatheType;           // Item.dbc SheatheType (where the weapon hangs when sheathed)
        public bool IsQuestItem;
        public string Tooltip;
        // ==== Iteration 11 (wave 0, w0play) ====
        // Gap D1 (w0play report): uicore's withdrawn 6.1 (P/design11/uicore_parts/withdrawn_contract.md) and npc's M7 each named the other
        // as the owner, so nobody defined these - defined ONCE here; npc implements them in both sessions (uicore H15 / npc 6.4).
        // Where an OWNED item is - drag and drop addresses it (InvPos). -1 = not in the inventory (loot, a reward, a merchant row).
        // Online: set from the inventory update fields on every rebuild; offline: Bag 255, Slot 23 + backpack slot.
        public int Bag = -1, Slot = -1;
        public bool NoDestroy;            // ITEM_FLAG_NO_USER_DESTROY 0x20 (ItemTemplate.h:152): the server refuses CMSG_DESTROYITEM
        public int MaxStack = 1;          // item_template Stackable (split / merge limits), 1 = not stackable
        // Iteration 11 (area npc): what the bags, the merchant, the bank and the paper doll need (Bag / Slot / NoDestroy / MaxStack are
        // the D1 block just above). Online from the item object + SMSG_ITEM_QUERY_SINGLE_RESPONSE; offline from the zone's ItemDef +
        // Item.dbc. Defaults = "not known / not applicable".
        public int SellPrice;             // copper per item the vendor pays (ItemTemplate.SellPrice; offline ItemDef.SellPrice), 0 = "No sell price"
        public int Durability, MaxDurability;   // ITEM_FIELD_DURABILITY / _MAXDURABILITY (0 / 0 = none)
        public int ContainerSlots;        // a bag: its slots (ItemTemplate.ContainerSlots)
        public int BagFamily;             // ItemTemplate.BagFamily mask (what a special bag accepts / what this item is)
        public int ItemClass = -1, ItemSubClass = -1, ItemLevel, RequiredLevel;
        public bool Soulbound;            // ITEM_FIELD_FLAG_SOULBOUND
        public bool Readable;             // PageText != 0 or ITEM_FIELD_FLAG_READABLE: "<Right Click to Read>"
        public bool Openable;             // ITEM_FLAG_HAS_LOOT: "<Right Click to Open>"
        public bool Locked;               // a move / sell / destroy of it waits for the server (drawn desaturated, like WoW's locked item)
    }

    public sealed class QuestObjectiveView
    {
        public string Text;
        public int Have, Need;
        public bool Done { get { return Have >= Need; } }
    }

    public sealed class QuestView
    {
        public int Id;
        public string Title;
        public int Level;
        public QuestState State;
        public string Description;
        public string ObjectivesText;
        public string ProgressText;
        public string CompletionText;
        public List<QuestObjectiveView> Objectives = new List<QuestObjectiveView>();
        public int RewardXp;
        public int RewardMoney;           // copper
        public List<ItemView> RewardItems = new List<ItemView>();
        public List<ItemView> RewardChoices = new List<ItemView>();
        public ulong GiverGuid, EnderGuid;
        // Iteration 11 (designer quests; wave 0, w0play): the tracker (WatchFrame) and the log's extras. Offline fills what it knows (defaults otherwise).
        public float TimeLeft = -1f;      // timed quest: seconds left (online PLAYER_QUEST_LOG_n_5 expiry - the server clock), -1 = not timed / unknown, 0 = ran out
        public float TimeTotal;           // timed quest: seconds it had when first seen (WoW's timer colours, GetTimerTextColor); 0 = unknown
        public bool Failed;               // the log's QUEST_STATE_FAIL 0x2: "<title> - Failed" (QUEST_FAILED_TAG GS:5888)
        public bool Sharable;             // QUEST_FLAGS_SHARABLE 0x8 (Share button / tracker menu); offline false
        public bool Daily;                // QUEST_FLAGS_DAILY 0x1000 ("Daily %s", DAILY_QUEST_TAG_TEMPLATE GS:1904)
        public bool Repeatable;           // the giver's list says repeatable (GossipDef.cpp:226 / :368) - the blue icon
        public int SuggestedPlayers;      // "Group (n)" (QUEST_DETAILS suggested players), 0 = solo
        public string LogCompletionText;  // the tracker line of a complete quest (online: the template's CompletedText; offline: ours "Return to <ender>.")
        public int UseItemEntry;          // the quest's usable item in the bags (the tracker's item button), 0 = none
        public int UseItemBagIndex = -1;  // its Player.Bags index, -1 = none
        public int RewardSpellId;         // QUEST_DETAILS / OFFER_REWARD RewSpell (else RewSpellCast), 0 = none
        public string RewardSpellName;    // its Spell.dbc name, null = none
        public string RewardSpellIcon;    // Interface\Icons\... of it
        public bool RewardSpellLearned;   // the spell teaches a spell (an effect 36 LEARN_SPELL): REWARD_SPELL "You will learn:", else REWARD_AURA
        // Iteration 11 (quest map): WotLK 3.3's quest POIs. Online: SMSG_QUEST_POI_QUERY_RESPONSE (null = not answered yet, empty = the server has
        // none for it). Offline: always null - the offline content has no quest POI data.
        public List<QuestPoiView> Pois;
        // 13.7 (the user: "group the quests to the correct area it comes from, just like in the game"): the quest log's header.
        public int ZoneOrSort;            // online: the template's ZoneOrSort (> 0 AreaTable id, < 0 minus a QuestSort id, 0 none)
        public string ZoneName;           // a ready header (offline: the zone content's name); else the UI names ZoneOrSort
    }

    /// <summary>
    /// Iteration 11 (quest map): one quest POI area as AzerothCore sends it (QueryHandler.cpp:441-465 HandleQuestPOIQuery from the
    /// quest_poi / quest_poi_points rows, ObjectMgr.cpp:8499 / :8509 LoadQuestPOI). Points are world yards (WoW x, y) on MapId.
    /// </summary>
    public sealed class QuestPoiView
    {
        public int Id;                    // quest_poi.id ("POI index")
        public int ObjectiveIndex;        // quest_poi.ObjectiveIndex (int32, "objective index")
        public int MapId;                 // quest_poi.MapID
        public int WorldMapAreaId;        // quest_poi.WorldMapAreaId (the packet's "areaid": a WorldMapArea.dbc ID)
        public int Floor;                 // quest_poi.Floor ("floorid", a dungeon map level; 0 = none)
        public int Priority, Flags;       // quest_poi.Priority / Flags (the packet's Unk3 / Unk4 "unknown")
        public int[] X = new int[0], Y = new int[0];   // quest_poi_points X, Y (int32), in the server's Idx2 order
    }

    /// <summary>An open NPC conversation: greeting plus the quests this NPC offers, tracks or takes back.</summary>
    public sealed class NpcDialog
    {
        public ulong NpcGuid;
        public string NpcName;
        public string Greeting;
        public List<QuestView> Quests = new List<QuestView>();
        // Iteration 11 (designer quests; wave 0, w0play)
        public int OpenQuestId;           // the quest whose page the server sent (the conversation began with QUEST_DETAILS / REQUEST_ITEMS / OFFER_REWARD, or the next quest of a chain); 0 = the greeting
        public int OpenPage;              // 1 details, 2 progress (request items), 3 reward (offer reward); 0 = the greeting
        public int OpenSerial;            // +1 every time the session asks for a page: the quest frame opens it once per change
        public ulong SharedByGuid;        // a party member's shared quest (QUEST_DETAILS divider / the auto-complete sharer): Accept goes to them, closing declines; 0 = none
        // Iteration 11 (area npc): the NPC's gossip options (GossipFrame), in the server's order, after the quests.
        public List<GossipOptionView> Options = new List<GossipOptionView>();
        public int MenuId;                // the SMSG_GOSSIP_MESSAGE menu id (echoed by CMSG_GOSSIP_SELECT_OPTION; offline 0)
        public bool FromObject;           // a game object's menu (quest-giver object, goober) - no portrait
    }

    public sealed class LootView
    {
        public ulong CorpseGuid;
        public int Money;                 // copper
        public List<ItemView> Items = new List<ItemView>();
    }

    public sealed class StatsView
    {
        public int Strength, Agility, Stamina, Intellect, Spirit;
        public int Armor, AttackPower;
        public float MinDamage, MaxDamage, AttackSpeed;
        public float CritChance, DodgeChance;   // percent
        // Iteration 11 (area talents; wave 0, w0play): what the server sends (online); offline fills what it simulates, 0 otherwise
        public int[] StatPos = new int[5], StatNeg = new int[5];          // green / red part of each base stat
        public int[] Resist = new int[7];                                 // 0 armor (= Armor), 1 holy, 2 fire, 3 nature, 4 frost, 5 shadow, 6 arcane
        public int AttackPowerPos, AttackPowerNeg;
        public float OffMinDamage, OffMaxDamage, OffAttackSpeed;          // 0 = no off-hand
        public int RangedAttackPower, RangedAttackPowerPos, RangedAttackPowerNeg;
        public float RangedMinDamage, RangedMaxDamage, RangedSpeed;
        public float RangedCritChance, OffhandCritChance;
        public float[] SpellCrit = new float[7];                          // by school (0 physical .. 6 arcane)
        public int[] SpellDamage = new int[7];                            // bonus damage by school
        public int BonusHealing, SpellPenetration;
        public float ManaRegen, ManaRegenCasting;                         // per 5 s (= the per-second fields x 5, PaperDollFrame.lua:1012-1015)
        public float ParryChance, BlockChance;
        public int BlockValue, Defense, DefenseRatingBonus;               // defense skill (skill 95 value + rating part)
        public int[] Ratings = new int[25];                               // PLAYER_FIELD_COMBAT_RATING_1.. by CombatRating
        public float[] RatingBonus = new float[25];                       // the same in % (gtCombatRatings)
        public int Expertise, OffhandExpertise;
    }

    /// <summary>The player's own character.</summary>
    /// <summary>Lead 2026-10-26 (swimming): one of WoW's mirror timers (MirrorTimer.lua) - 0 Fatigue, 1 Breath, 2 feign death (AC MirrorTimerType).
    /// Value / Max in seconds; the session adds Scale x dt each tick unless Paused (MirrorTimer_OnUpdate: value + scale x elapsed).</summary>
    public sealed class MirrorTimerState
    {
        public bool Active, Paused;
        public float Value, Max, Scale;
    }

    public sealed class PlayerState
    {
        /// <summary>Lead 2026-10-26: the three mirror timers (SMSG_START / PAUSE / STOP_MIRROR_TIMER online; offline none).</summary>
        public readonly MirrorTimerState[] Mirror = { new MirrorTimerState(), new MirrorTimerState(), new MirrorTimerState() };
        /// <summary>Lead 2026-11-03: the realm's game time of day in hours (0..24) from SMSG_LOGIN_SETTIMESPEED, advanced at its speed;
        /// -1 = not known (offline, or before the server said it). WowGame lights the world by it, as the WoW client does.</summary>
        public float GameTimeHours = -1f;
        public ulong Guid;
        public string Name;
        public WowClass Class;
        public int Race = 4;              // ChrRaces id (4 = Night Elf)
        public int Level = 1;
        public int Xp, XpToNextLevel;
        public int Health, MaxHealth;
        public PowerType Power = PowerType.Mana;
        public int PowerValue, PowerMax;
        public int ComboPoints;
        public int Money;                 // copper
        public bool Dead, Ghost, InCombat, AutoAttacking;
        public int CastingSpellId;
        public float CastElapsed, CastTotal;
        public float GcdRemaining, GcdTotal;
        public WowPos Pos;
        public SheathState Sheath;
        public StatsView Stats = new StatsView();
        public List<AuraView> Auras = new List<AuraView>();
        public List<AbilityView> Spellbook = new List<AbilityView>();
        public int[] ActionBar = new int[12];            // spell ids per slot, 0 = empty
        public List<ItemView> Bags = new List<ItemView>();
        public ItemView[] Equipment = new ItemView[19];  // by EquipSlot, null = empty
        public List<QuestView> QuestLog = new List<QuestView>();
        // Added by Rules (iteration 2):
        public int AttackCounter;         // +1 on every swing/shot of the player (desktop weapon animation)
        public int WoundCounter;          // +1 when the player is hit
        public int ShapeshiftForm;        // SpellShapeshiftForm id: 0 none, 1 cat, 5 bear, 17 battle stance, 18 defensive stance, 30 stealth
        public bool Stealthed;
        public int Mana, MaxMana;         // druids in bear form keep their mana here while Power shows rage (= PowerValue otherwise)
        public float SwingProgress;       // 0..1 of the melee swing timer (0 = just swung)
        public bool AutoShooting;         // Auto Shot is on
        public int FreeBagSlots;
        // Added by Rules (iteration 3):
        public WowFaction Faction;        // from Race (ChrRaces.dbc Alliance field)
        public bool Channeling;           // CastingSpellId is a channel: CastElapsed counts up to CastTotal (draw the bar draining)
        public ulong CastTargetGuid;      // target of the current cast/channel (1 = self)
        public ulong PetGuid;             // the player's pet in Entities (EntityKind.Pet), 0 = none
        public int WeaponImbueSpellId;    // Shaman weapon imbue on the main hand (Rockbiter/Flametongue spell id), 0 = none
        public float WeaponImbueRemaining;// seconds
        public int Difficulty = 1;        // 0 easy, 1 normal, 2 hard (OfflineSession.SetDifficulty)
        public bool AutoLoot;             // OfflineSession.AutoLoot
        // Iteration 4 (lead contract):
        public bool OnTaxi;               // flying a flight path (no combat, creatures ignore the player)
        public int TaxiPathId;            // TaxiPath.dbc id of the current flight, 0 = none
        public int MountDisplayId;        // CreatureDisplayInfo id of the mount under the avatar: taxi OR ridden, 0 = none
        public int HeldSpellId;           // immersive casting: a finished cast waiting to be released (0 = none)
        public ulong HeldTargetGuid;
        public List<int> KnownTaxiNodes = new List<int>(); // TaxiNodes.dbc ids this character discovered
        // Added by Rules (iteration 4):
        public List<int> TaxiRoute = new List<int>();     // while OnTaxi: every TaxiPath id of the flight, hop by hop (TaxiPathId = TaxiRoute[0])
        public int TaxiDestinationNode;   // while OnTaxi: the TaxiNodes id the flight lands at
        // Iteration 7 (lead contract): mounts. Owners may ADD fields.
        public List<AbilityView> Mounts = new List<AbilityView>(); // summonable mount spells (WoW's Mounts pane), by name, IsMount = true; NOT in Spellbook
        public bool Mounted;              // on a ridden mount (not a taxi); MountDisplayId is its model
        public int MountSpellId;          // the mount spell ridden; 0 = none, or online a mount the server gave without a known aura (GM .modify mount)
        public int MountCreatureEntry;    // Spell.dbc aura-78 EffectMiscValue of MountSpellId (a SERVER creature entry), 0 = unknown
        public MountKind MountKind;
        public MountDisplaySource MountDisplaySource;
        public int MountCounter;          // +1 at every mount-up (views rebuild on a change)
        public bool CanFly;               // may leave the ground now: online SMSG_MOVE_SET_CAN_FLY acked; offline flyer aura + flyable area (or FlyAnywhere)
        public int RidingSkill;           // skill 762: 0 / 75 / 150 / 225 / 300
        public bool ColdWeatherFlying;    // knows 54197
        public float WalkSpeed = 2.5f, RunSpeed = 7f, RunBackSpeed = 4.5f, SwimSpeed = 4.722222f, SwimBackSpeed = 2.5f;  // yd/s (AC playerBaseMoveSpeed)
        public float FlightSpeed = 7f, FlightBackSpeed = 4.5f;   // yd/s
        public float TurnRate = 3.141594f;                        // rad/s (AC MOVE_TURN_RATE)
        public float CollisionHeight;     // yd; AC Unit::GetCollisionHeight while mounted (online: SMSG_MOVE_SET_COLLISION_HGT), 0 = unknown
        // Stage O2 (lead contract, PLAN9 s3): WoW's death flow in both modes. Owners may ADD fields.
        public bool HasCorpse;            // released as a ghost: the body lies at CorpsePos on CorpseMapId
        public int CorpseMapId;
        public WowPos CorpsePos;
        public float ReclaimDelay;        // seconds until the corpse may be reclaimed (0 = now)
        public bool CanReclaim;           // a ghost within the reclaim radius of its corpse and the delay is over: "Resurrect" is offered
        public bool NearSpiritHealer;     // a ghost next to a spirit healer: its resurrection is offered
        // Stage S1 (lead contract, PLAN10 s3): the server group (online). Offline stays empty (Somnium players are WowParty.Members).
        public List<GroupMemberView> Group = new List<GroupMemberView>();   // the OTHER members, server order; empty = not in a group
        public ulong GroupLeaderGuid;     // 0 = no group
        public string PendingInviteFrom;  // a group invite waiting for Accept / Decline (the inviter's name), null = none
        // Lead (2026-09-27, the user: "invite people, then I become leader, then I can remove people or promote to leader and all the
        // other options"): the group's loot settings from SMSG_GROUP_LIST and the rolls waiting for our Need / Greed / Pass.
        public int GroupLootMethod = -1;  // 0 free for all, 1 round robin, 2 master looter, 3 group loot, 4 need before greed; -1 = no group
        public int GroupLootThreshold = 2;   // item quality 2 uncommon .. 4 epic (rolls start at it)
        public ulong GroupMasterLooterGuid;
        public List<LootRollView> LootRolls = new List<LootRollView>();   // SMSG_LOOT_START_ROLL not answered yet, oldest first
        // ==== Iteration 11 (wave 0, w0play: every proposal's PlayerState append merged here, append-only; owners in brackets) ====
        // Iteration 11 (vrcast, proposal-vrcast s5): WoW's 144 action buttons, packed as SMSG_ACTION_BUTTONS carries them (WowActionSlots;
        // item values are negative ints - test != 0). Index = WoW action - 1: 0-11 main bar page 1, 12-23 page 2, 24-35 Right Bar, 36-47 Right
        // Bar 2, 48-59 Bottom Right Bar, 60-71 Bottom Left Bar, 72-119 the four form bars, 120-143 vehicle / possess. 0 = empty. The sessions keep
        // ActionBar (spell ids of 0..11) in step for older readers.
        public int[] ActionButtons = new int[WowActionSlots.Count];
        // Iteration 11 (designer spellfx):
        public SpellTargetingView Targeting;              // non-null while a spell waits for a place / an item (WoW's spell on the cursor)
        public int[] EquipEnchants = new int[19];         // by EquipSlot: SpellItemEnchantment ids, permanent | temporary << 16 (offline: the imbue as temporary)
        public uint AuraStateMask;                        // our own UNIT_FIELD_AURASTATE (offline: CasterAuraState rules)
        public ulong ComboTarget;                         // the unit our combo points are on, 0 = none (ComboPoints = the points while it is the target)
        public RuneView[] Runes;                          // death knights online: 6 runes in server order; null otherwise
        // Iteration 11 (designer quests): death extras, party quest prompts, quest clocks
        public int SelfResSpellId;        // lying dead: online PLAYER_SELF_RES_SPELL (3026 'Use Soulstone', 21169 / 27740 'Reincarnation'), 0 = none (offline 0)
        public string SelfResName;        // that spell's Spell.dbc name = the DEATH popup's second button; null = none
        public bool ReleaseTimer = true;  // the DEATH popup counts down to the automatic release (online PLAYER_FIELD_BYTES & 0x08; false in instances)
        public QuestConfirmView PendingQuestConfirm;   // SMSG_QUEST_CONFIRM_ACCEPT waiting for Yes / No (QUEST_ACCEPT popup), null = none
        public int DailyQuestsDone;       // online: the non-zero PLAYER_FIELD_DAILY_QUESTS_1..25 ("Daily: n/25"); offline 0
        public float DailyResetSeconds = -1f;   // seconds until the daily reset (SMSG_QUERY_TIME_RESPONSE TimeResponse, counted down), -1 = unknown
        public bool CorpseInInstance;     // a ghost whose corpse lies inside an instance and who stands outside (RECOVER_CORPSE_INSTANCE)
        // Iteration 11 (area pets). PetGuid (iteration 3) = online our UNIT_FIELD_SUMMON from now on (0 when it goes).
        public ControlBarView ControlBar;                 // the pet / charm / possess / vehicle bar; null = none
        public PetInfoView PetInfo;                       // our controllable pet's details; null = no pet
        public TotemView[] Totems = new TotemView[4];     // by element: 0 fire, 1 earth, 2 water, 3 air; null = none
        public int TotemBarPage = 1;                      // the multicast page shown (1 Call of the Elements, 2 Ancestors, 3 Spirits)
        public VehicleView Vehicle;                       // in a vehicle / possessing a unit; null = on our own feet
        public StableView Stable;                         // the open stable; null = closed
        public List<AbilityView> Companions = new List<AbilityView>();   // companion (minipet) spells, by name; NOT in Spellbook (WoW's Companions tab)
        public ulong CompanionGuid;                       // the summoned companion (UNIT_FIELD_CRITTER), 0 = none
        public int CompanionSpellId;                      // the spell that summoned it (its UNIT_CREATED_BY_SPELL), 0 = unknown / none
        // Iteration 11 (lead contract, owner avatar): what WoW draws the character as - the Somnium avatar follows it (Game/Play/Forms).
        public int DisplayId;             // CreatureDisplayInfo id drawn NOW. Online UNIT_FIELD_DISPLAYID (0x43). Offline: newest TRANSFORM aura's creature (WowFormModels.TransformDisplay), else the form's model (WowFormModels.ModelForForm), else NativeDisplayId. 0 = not known yet.
        public int NativeDisplayId;       // own race/gender display: online UNIT_FIELD_NATIVEDISPLAYID (0x44); offline ChrRaces.dbc MaleDisplayID (field 4; FemaleDisplayID field 5 when Gender == 1).
        public float ObjectScale = 1f;    // online OBJECT_FIELD_SCALE_X (0x04); offline 1
        public int Gender;                // 0 male, 1 female: online PLAYER_BYTES_3 byte 0 (Player.h:513 - SetDisplayId rewrites UNIT_FIELD_BYTES_0 byte 2, Unit.cpp:12776-12777); offline 0 (ours)
        public bool HideHelm, HideCloak;  // online PLAYER_FLAGS 0x400 / 0x800 (Player.h:469-470); offline false
        // Iteration 11 (designer trainer): skills, professions, recipes
        public List<SkillView> Skills = new List<SkillView>();      // every displayed skill (online PLAYER_SKILL_INFO_1_1; offline computed), SkillLineCategory order, then name
        public int SkillsVersion;         // +1 whenever Skills changed (views rebuild on a change)
        public int FreeProfessions = 2;   // PLAYER_CHARACTER_POINTS2 (AC Player.h:1795): primary professions that may still be learned (WoW's limit is 2)
        public List<AbilityView> Recipes = new List<AbilityView>(); // known recipes (Spell.dbc attr0 & 0x20 SPELL_ATTR0_IS_TRADESKILL): shown in the profession window, NOT in Spellbook (WoW); an action button that holds one still resolves (UiContext.Find, hook U8)
        // Iteration 11 (motion, lead contract): forced movement, loss of control, movement auras, transports. Owners may ADD fields.
        public ForcedMoveView ForcedMove;  // the server / a spell moves the player now (WowForcedMove drives the avatar, then IWowSession.ForcedMoveDone), null = none
        public int ForcedMoveCounter;      // +1 at every new ForcedMove (views restart on a change)
        public bool Rooted;                // root or stun: the avatar does not walk or jump (it may turn locally; AC keeps the old facing)
        public bool Stunned;               // stunned (online UNIT_FLAG_STUNNED 0x40000, offline a MOD_STUN aura)
        public int StandState;             // states fix 2026-10-04: our UNIT_FIELD_BYTES_1 byte 0 (0 stand, 1 sit, 2-6 chairs, 3 sleep, 7 dead, 8 kneel); offline: ours (eating, /sit /kneel /sleep)
        public int Consuming;              // states fix 2026-10-04: offline 1 eating / 2 drinking (its food aura has no spell); online 0 - GearStatePose reads the auras
        // rescorpse 2026-10-04: a pending resurrection by another player (online SMSG_RESURRECT_REQUEST; StaticPopup "RESURRECT" / "_NO_SICKNESS")
        public string ResurrectRequestBy;  // the resurrecter's name, null = no request
        public bool ResurrectRequestSickness;   // a creature resurrects: RESURRECT_REQUEST (sickness) instead of _NO_SICKNESS
        public float ResurrectRequestDelay;// s until Accept may be pressed (GetCorpseRecoveryDelay; 0 = now, SPELL_ATTR3_NO_RES_TIMER)
        public float ResurrectRequestLeft; // s until the popup times out (OnShow: GetCorpseRecoveryDelay() + 60; the timeout declines)
        public bool ControlLost;           // fear, confuse, charm or a taxi: the server moves the character (online SMSG_CLIENT_CONTROL_UPDATE allowMove 0)
        public bool SlowFall;              // MOVEMENTFLAG_FALLING_SLOW (Slow Fall, Levitate): falls capped at 7 yd/s
        public bool WaterWalking;          // MOVEMENTFLAG_WATERWALKING (Water Walking, Levitate): liquid surfaces carry the avatar
        public bool Hovering;              // MOVEMENTFLAG_HOVER (Levitate)
        public ulong TransportGuid;        // the transport the player rides (its EntityState.IsTransport), 0 = none
        // Iteration 11 (world, PLAN11): dungeons, rest, exploration. Online only; offline leaves the defaults (MapInstanceType 0 ...).
        public int MapInstanceType;       // Map.dbc InstanceType of the player's map: 0 none (continents, cities, Deeprun Tram), 1 party dungeon, 2 raid, 3 battleground, 4 arena
        public int DungeonDifficulty;     // 0 normal, 1 heroic (MSG_SET_DUNGEON_DIFFICULTY)
        public int RaidDifficulty;        // 0 10 player, 1 25 player, 2 10 heroic, 3 25 heroic (MSG_SET_RAID_DIFFICULTY)
        public int MapDifficulty;         // the difficulty of the map the player is on (SMSG_INSTANCE_DIFFICULTY), 0 on continents
        public bool MapDynamicDifficulty; // SMSG_INSTANCE_DIFFICULTY's second uint32 (Icecrown Citadel / Ruby Sanctum heroic switch)
        public bool HasSavedInstances;    // SMSG_UPDATE_INSTANCE_OWNERSHIP: at least one raid / heroic lock
        public List<SavedInstanceView> SavedInstances = new List<SavedInstanceView>();   // SMSG_RAID_INSTANCE_INFO (after RequestRaidInfo)
        public int SavedInstancesVersion; // +1 at every SMSG_RAID_INSTANCE_INFO (the Raid Info window redraws)
        public InstanceLockView PendingInstanceLock;   // SMSG_INSTANCE_LOCK_WARNING_QUERY waiting for AnswerInstanceLock, null = none
        public float InstanceBootSeconds; // SMSG_RAID_GROUP_ONLY: seconds until the server ports you out of an instance whose group you left, 0 = none
        public bool Resting;              // PLAYER_FLAGS_RESTING 0x20: in an inn (area trigger) or a city
        public int RestedXp;              // PLAYER_REST_STATE_EXPERIENCE
        public uint[] ExploredZones;      // PLAYER_EXPLORED_ZONES_1 .. +127 (bit = AreaTable.dbc AreaBit), null until the server sent them
        public int AreaTriggersSent;      // CMSG_AREATRIGGER sent this session (diagnostics, probes)
        // Iteration 11 (area talents)
        public TalentsView Talents = new TalentsView();
        public List<FactionView> Reputations = new List<FactionView>();   // display order; empty offline
        public int ReputationCounter;      // +1 when Reputations changed
        public int WatchedFaction = -1;    // repListId shown as the experience bar (PLAYER_FIELD_WATCHED_FACTION_INDEX), -1 none
        public List<CurrencyView> Currencies = new List<CurrencyView>();
        public int LifetimeHonorableKills; // PLAYER_FIELD_LIFETIME_HONORABLE_KILLS. HonorPoints / ArenaPoints are area npc's PlayerState fields (one field, one owner)
        public int ChosenTitle;            // CharTitles bit_index, 0 = none
        public List<TitleView> KnownTitles = new List<TitleView>();
        public AchievementsView Achievements = new AchievementsView();
        // Iteration 11 (area npc): every container, the bank, the buyback slots and the home location.
        public List<ContainerView> Containers = new List<ContainerView>();   // always 5: [0] backpack (Bag 255, SlotBase 23, 16 slots), [1..4] the bag slots 19..22 (Size 0 = empty slot)
        public ContainerView Keyring;     // Bag 255, SlotBase 86, Size = WoW's shown keyring size (ContainerFrame.lua GetKeyRingSize); null = none (offline)
        public List<ContainerView> Bank = new List<ContainerView>();         // online always 8: [0] the 28 bank slots (Bag 255, SlotBase 39), [1..7] bank bag slots 67..73 (Locked = not bought); offline empty
        public int BankBagSlotsBought;    // PLAYER_BYTES_2 byte 2 (0..7)
        public int NextBankSlotCost;      // copper of the next bank bag slot (BankBagSlotPrices.dbc), 0 = all 7 bought
        public ItemView[] Buyback = new ItemView[12];   // PLAYER_FIELD_VENDORBUYBACK_SLOT_1.. (slots 74..85), null = empty
        public int[] BuybackPrice = new int[12];        // PLAYER_FIELD_BUYBACK_PRICE_1.. copper
        public int LastBuyback = -1;      // index of the newest buyback entry (PLAYER_FIELD_BUYBACK_TIMESTAMP_1 max), -1 = none (MerchantBuyBackItem)
        public int HonorPoints, ArenaPoints;            // PLAYER_FIELD_HONOR_CURRENCY / _ARENA_CURRENCY (extended costs; talents' Currency tab reads them); offline 0
        public ulong PendingBinderGuid;   // SMSG_BINDER_CONFIRM waiting for IWowSession.ConfirmBinder (0 = none)
        public string PendingBinderPlace; // the %s of CONFIRM_BINDER ("Do you want to make %s your new home?") - ours: the area the player stands in
        public int HomeMapId = -1, HomeAreaId;          // SMSG_BINDPOINTUPDATE (-1 = unknown)
        public WowPos HomePos;
        public string HomeName;           // AreaTable name of HomeAreaId (the Hearthstone tooltip's $z), null = unknown
    }

    public enum GameEventType : byte
    {
        MeleeHit, MeleeMiss, MeleeDodge, MeleeParry, MeleeBlock,
        SpellDamage, SpellHeal, PeriodicDamage, PeriodicHeal, SpellMiss, Absorb,
        XpGain, LevelUp, SpellLearned,
        QuestAccepted, QuestProgress, QuestCompleted, QuestRewarded, QuestAbandoned,
        LootReceived, MoneyReceived,
        EntityDied, PlayerDied, PlayerResurrected,
        CastStarted, CastFailed, CastSucceeded,
        AttackStarted, AttackStopped,
        Error,        // red UI text: "Out of range", "Not enough rage", "You are facing the wrong way"...
        Say,          // an NPC speaks (Text), show a bubble / chat line
        Teleport,     // the game moved the player (Pos): move the avatar there (death, respawn, hearth)
        Info,         // yellow UI text
        // Added by Rules (iteration 3) - appended, existing values unchanged:
        AuraApplied,  // SpellId, TargetGuid (who got it), SourceGuid (caster), Amount = duration ms (0 = until cancelled), Text = aura name. Also on refresh.
        AuraRemoved,  // SpellId, TargetGuid, SourceGuid (caster), Text = aura name (expired, cancelled, broken, unit died/evaded)
        ChannelStarted, // SourceGuid (caster), TargetGuid, SpellId, Amount = channel ms (emitted right after the CastSucceeded of the same spell)
        ChannelEnded,   // SourceGuid, TargetGuid, SpellId, Text = null when it ran its course, else the reason ("Interrupted")
        // Iteration 4 (lead contract) - appended:
        TaxiStarted,    // Amount = TaxiPath id, Pos = the start, Text = destination name: the Unity flight takes over the avatar
        TaxiFinished,   // Pos = the landing point
        CastHeld,       // immersive casting: SpellId finished casting and waits for ReleaseHeldSpell (SourceGuid = the player)
        // Iteration 7 (lead contract) - appended:
        Mounted,        // SpellId = the mount spell (0 online when unknown), Amount = CreatureDisplayInfo id, Text = the mount's name
        Dismounted,     // SpellId, Amount = (int)DismountReason, Text = WoW's message when there is one, else null
        // Stage O2 (lead contract, PLAN9 s3) - appended:
        SpiritReleased, // Pos = where the ghost appears, Text = the graveyard's name when known (a Teleport event may follow for the avatar)
        CorpseReclaimable, // Amount = 1 in reach of the corpse with the delay over ("Resurrect" offered), 0 = no longer
        // Stage S1 (lead contract, PLAN10 s3) - appended:
        ChatMessage,    // SourceGuid = the speaker (0 = system), Amount = (int)ChatKind, Text = the message, ItemEntry = 0; the speaker's name via Find or ChatSender
                        // (stage S1 chat, online: SpellId = AzerothCore's chat tag bits AFK 1 / DND 2 / GM 4, TargetGuid = the packet's receiver guid)
        GroupInvite,    // Text = the inviter's name (PlayerState.PendingInviteFrom is set)
        GroupChanged,   // PlayerState.Group / GroupLeaderGuid changed (joined, left, member added / removed, leader changed)
        // ==== Iteration 11 (wave 0, w0play: every proposal's appends merged in this order; values are not persisted - readers use the names) ====
        // Iteration 11 (motion, lead contract) - appended:
        ForcedMoveStarted,  // Amount = (int)ForcedMoveKind, SpellId when known, Pos = the end (paths) / the start (knockback); PlayerState.ForcedMove is set
        ForcedMoveEnded,    // Amount = (int)ForcedMoveKind, Text = null when it ran its course, else why ("teleport", "replaced", "stopped", "timeout", "hop")
        ControlChanged,     // PlayerState.Rooted / Stunned / ControlLost / SlowFall / WaterWalking / Hovering changed (Amount: bit 1 rooted, 2 stunned, 4 control lost)
        TransportChanged,   // PlayerState.TransportGuid changed (TargetGuid = the transport, 0 = off it; Text = its name)
        // Iteration 11 (world, PLAN11) - appended:
        InstanceLockQuery,  // PlayerState.PendingInstanceLock was set: show WoW's INSTANCE_LOCK popup
        InstanceBoot,       // PlayerState.InstanceBootSeconds changed: Amount = ms left (0 = the boot timer stopped)
        // Iteration 11 (area pets) - appended:
        VehicleEntered,     // SourceGuid = the vehicle / possessed unit, Amount = (int)VehicleMode, SpellId = VehicleSeat id (0 possess), Text = its name
        VehicleLeft,        // SourceGuid = the vehicle / unit, Pos = where our character stands now (a Teleport event follows when the avatar must move)
        // Iteration 11 (owner social) - appended:
        SocialChanged,      // Amount = (int)SocialPart flags of what changed in IWowSocial.State / Mailbox / Charter
        GuildInvite,        // Text = the inviter, IWowSocial.State.PendingGuildInviteFrom / PendingGuildInviteGuild are set (the popup)
        TextEmote,          // SourceGuid = performer, TargetGuid = target (0 none), SpellId = EmotesText.dbc id, Amount = AnimationData id of its
                            // Emotes.dbc row (0 none), Text = the chat line as this player reads it. UiSocialHost plays it on the avatar.
        NewMail,            // SMSG_RECEIVED_MAIL: unread mail arrived (State.HasUnreadMail); Text = HAVE_MAIL "You have unread mail"
        // Iteration 11 (designer trainer) - appended:
        SkillChanged,       // SpellId = SkillLine id, Amount = the new value (0 = removed), Text = WoW's line or null. The text also arrives as a ChatMessage (ChatKind.System) - WoW shows skill-ups in the chat only
        TrainerTrained,     // SourceGuid = the trainer, SpellId = the service learned (SMSG_TRAINER_BUY_SUCCEEDED): UI sound, VR haptic; the window refreshes itself
        FishingBite,        // SourceGuid = the bobber, Amount = the custom animation index (SMSG_GAMEOBJECT_CUSTOM_ANIM): the view plays the splash, VR pulses both hands
        // Iteration 11 (area talents) - appended:
        AchievementEarned,  // Amount = Achievement.dbc id, SourceGuid = the player, Text = its name (toast + AchievementSound)
        // Iteration 11 (area npc) - appended:
        ItemSold,           // ItemEntry, Amount = copper received, Text = the item name (the sound + the money line; WoW prints no chat line)
        ItemBought,         // ItemEntry, Amount = items received (BuyCount x purchases), Text = the item name (the LootReceived line still comes from SMSG_ITEM_PUSH_RESULT)
        ItemDestroyed,      // ItemEntry, Amount = count, Text = the item name (one log line; no WoW text)
        HomeBound,          // Text = the home's area name: UI shows ERR_DEATHBIND_SUCCESS_S "%s is now your home." (GlobalStrings.lua:3021)
        GossipPoi,          // Pos.X / Pos.Y = the point (WoW coords, Z 0), Amount = icon, SpellId = importance, Text = name (SMSG_GOSSIP_POI): the WoW Map may pin it
        ItemRepaired,       // Amount = copper paid (0 = free / unknown), ItemEntry = 0 for "repair all"
        // Iteration 11 (designer spellfx) - appended:
        SpellKit            // SMSG_PLAY_SPELL_VISUAL / _IMPACT - SourceGuid = the unit, Amount = SpellVisualKit id
    }

    public struct GameEvent
    {
        public GameEventType Type;
        public ulong SourceGuid, TargetGuid;
        public int ItemEntry;             // added by Rules: LootReceived / item events (Amount = count, Text = item name)
        public int Amount;
        public int SpellId;
        public int QuestId;
        public bool Crit;
        public string Text;
        public WowPos Pos;
        // Iteration 11 (designer spellfx; wave 0, w0play) - CastSucceeded only:
        public ulong[] Hits;              // every unit the spell hit, server order (TargetGuid included); null = TargetGuid only
        public bool AtDest;               // the spell was aimed at a place: Pos = that place (WoW coordinates)
    }

    // ---- Iteration 7 (lead contract): mounts ------------------------------------------------------------------------
    /// <summary>What the player rides (PlayerState.MountKind).</summary>
    public enum MountKind : byte { None = 0, Ground = 1, Flying = 2 }

    /// <summary>Where a mount's CreatureDisplayInfo id came from (PlayerState.MountDisplaySource, the Mounts window line).</summary>
    public enum MountDisplaySource : byte { None = 0, Server = 1, Learned = 2, Baked = 3, StandIn = 4 }

    /// <summary>Why the mount went away (GameEvent.Amount of Dismounted).</summary>
    public enum DismountReason : byte { Chosen = 0, Spell = 1, Death = 2, Teleport = 3, Indoors = 4, Water = 5, FlyingArea = 6, Server = 7, Ended = 8 }

    /// <summary>The local ride's motion this frame. WowGame passes WowMount.Motion to IWowSession.SetPlayerMotion right BEFORE
    /// SetPlayerPosition, whose WowPos is then the MOUNT's feet and heading. Driven = false on foot (sessions behave as in iteration 6).
    /// Speeds are yd/s, angles WoW radians (orientation increases to the left).</summary>
    public struct PlayerMotion
    {
        public bool Driven;           // a mount ride (or the fall after a dismount in the air) moves the avatar this frame
        public int Forward;           // +1 forward, -1 backward, 0 - relative to the facing of SetPlayerPosition (the mount heading)
        public int Strafe;            // +1 right, -1 left, 0
        public int Turn;              // +1 turning left, -1 right, 0 - key / stick turning at Player.TurnRate only (mouse and snap turns are 0)
        public bool Walking;          // the VR walk step (MOVEMENTFLAG_WALKING, Player.WalkSpeed)
        public bool Airborne;         // jumping or falling (not flying)
        public bool Flying;           // in the air on a flying mount (only ever while Player.CanFly)
        public bool Swimming;         // riding at the surface of deep liquid
        public int Vertical;          // +1 ascending, -1 descending, 0 (flying only)
        public float Pitch;           // + up: elevation of the travel direction while Flying / Swimming (server only - the view stays level)
        public float FallTime;        // seconds since leaving the ground without flying, 0 otherwise
        public bool Jumped;           // a jump started this frame (the three Jump* fields are valid)
        public float JumpUpSpeed;     // yd/s up at launch
        public float JumpPlanarSpeed; // yd/s horizontal at launch (0 = standing jump)
        public float JumpDirection;   // WoW orientation of the horizontal launch velocity
        public float SeatedSway;      // lead fix 2026-10-04: yd the avatar may drift from where it sat before a move goes out while seated (VR; 0 = off)
        public bool Landed;           // touched the ground this frame after Airborne (online: MSG_MOVE_FALL_LAND)
        public bool AtSpeed;          // the speed ramp reached its target this frame (online: one extra heartbeat)
    }

    /// <summary>Where the player stands, sampled by the Unity side (IWowSession.SetPlayerPlace). Offline rules use it; online only logs it.</summary>
    public struct PlayerPlace
    {
        public bool Known;            // false = no sample yet: sessions treat the spot as outdoors and dry (and log that once)
        public bool Outdoors;         // AzerothCore Map.cpp: on a WMO floor above the terrain -> MOGP 0x8, overridden by WMOAreaTable flags 4 (outdoors) / 2 (indoors); else AreaTable INSIDE/OUTSIDE
        public bool InWmo;
        public int AreaId;            // WMOAreaTable areaId when the WMO row names one, else the terrain (MCNK) area
        public float LiquidDepth;     // yards of liquid above the feet, 0 = dry
        public float LiquidSurfaceZ;  // WoW z of the liquid surface when LiquidDepth > 0
        public string Where;          // log only, e.g. "WMO Stormwind.wmo group 'MB02' (MOGP 0x2841, row 14369 flags 0x4)" / "terrain area 12"
    }
}

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Stage S1 (lead contract, PLAN10 s3): chat and groups ---------------------------------------------------------
    /// <summary>What kind of chat line (GameEvent.Amount of ChatMessage; IWowSession.SendChat).</summary>
    public enum ChatKind : byte { Say = 0, Yell = 1, Emote = 2, Party = 3, Whisper = 4, WhisperSent = 5, System = 6, Guild = 7, Channel = 8, MonsterSay = 9, MonsterYell = 10, MonsterEmote = 11,
        // iteration 11 (owner social; wave 0, w0play) - appended:
        Officer = 12, Raid = 13, RaidLeader = 14, RaidWarning = 15, Battleground = 16, BattlegroundLeader = 17, PartyLeader = 18,
        Afk = 19, Dnd = 20, Achievement = 21, GuildAchievement = 22, ChannelNotice = 23, RaidBossEmote = 24, TextEmote = 25 }

    /// <summary>One other member of the server group (online).</summary>
    public sealed class GroupMemberView
    {
        public ulong Guid;
        public string Name;
        public int Level;
        public WowClass Class;
        public int Race;
        public bool Online, Dead, Ghost, Leader;
        public int Health, MaxHealth;
        public PowerType Power = PowerType.None;
        public int PowerValue, PowerMax;
        public int MapId = -1, ZoneId;
        public WowPos Pos;                // last position the server gave (SMSG_PARTY_MEMBER_STATS), zero when unknown
        public bool IsSomniumPlayer;      // the character of a Somnium player in this world (WowGear marks it)
    }

    /// <summary>One group loot roll (SMSG_LOOT_START_ROLL) waiting for IWowSession.RollOnLoot. Online only.</summary>
    public sealed class LootRollView
    {
        public ulong RollGuid;            // the roll's item guid (echoed in CMSG_LOOT_ROLL)
        public int Slot;
        public ItemView Item;             // name, icon, quality, count, tooltip (Name null until the server answered the item query)
        public float Duration, SecondsLeft;
        public bool CanNeed, CanGreed, CanDisenchant;   // the roll's vote mask (pass is always allowed)
    }
}

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Stage S1 (integrator, PLAN10 s5 - a Dreamport house rule of the user's realm): Immersive casting's damage bonus ----------
    /// <summary>
    /// "Immersive Mode": while a VR player has Immersive casting on, every spell RELEASED with Immersive casting (a held cast released
    /// by the push, an instant or channel cast through it) deals +10 % damage - damage only, never healing, only that cast. Offline the
    /// session applies it; online the realm's Dreamport hold module does (the client flags each cast: DPHOLD HOLD / BONUS). Both sessions
    /// show the buff <see cref="NewBuff"/> in PlayerState.Auras while <see cref="Active"/> - our own buff, never sent to the server,
    /// never removed by the server's aura updates; normal WoW clients do not see it.
    /// </summary>
    public static class ImmersiveMode
    {
        /// <summary>Ours (PLAN10 s5); the module's ImmersiveDamageBonus (ServerKit dreamport_hold.cpp) is the same value.</summary>
        public const float ImmersiveDamageBonus = 0.10f;
        /// <summary>The buff's reserved spell id (negative: no spell of the game; UiAuraRow never cancels it).</summary>
        public const int BuffSpellId = -5;
        public const string BuffName = "Immersive Mode";
        /// <summary>Two hands with arcane light - an existing 3.3.5a icon (Arcane Potency's), read from the player's own install.</summary>
        public const string BuffIcon = @"Interface\Icons\Spell_Arcane_ArcanePotency";
        public const string BuffTooltip = "Spells you release with Immersive casting deal 10% more damage.";

        /// <summary>VR + Settings > Immersive casting (written every frame by the Game layer, WowVrInput; false on desktop).</summary>
        public static bool Active;

        /// <summary>A fresh view of the buff (permanent: Duration 0).</summary>
        public static AuraView NewBuff()
        {
            return new AuraView { SpellId = BuffSpellId, Name = BuffName, IconPath = BuffIcon, Duration = 0f, Remaining = 0f, Harmful = false, Tooltip = BuffTooltip };
        }

        /// <summary>The damage of one released cast with the bonus (rounded half away from zero).</summary>
        public static int Boost(int damage)
        {
            if (damage <= 0) return damage;
            return (int)System.Math.Round(damage * (1.0 + ImmersiveDamageBonus), System.MidpointRounding.AwayFromZero);
        }
    }
}

// ==== Iteration 11 (wave 0, w0play): the new types of every proposal's CONTRACT, append-only, one namespace block per owner ====

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (gap D1, w0play): WoW inventory positions for drag and drop ------------------------------------------------
    // uicore's withdrawn 6.1 (P/design11/uicore_parts/withdrawn_contract.md) - npc's code and interact's contract use exactly these names.
    // (uicore's ActionType class is NOT taken: vrcast's WowActionSlots.Type* constants are the same AzerothCore values.)
    /// <summary>WoW's inventory addressing (AzerothCore Player.h:657-718). Bag 255 = the character's own slots: Slot 0-18 equipment
    /// (EquipSlot), 19-22 bag slots, 23-38 backpack, 39-66 bank, 67-73 bank bag slots, 74-85 buyback, 86-117 keyring. Bag 19-22 / 67-73 =
    /// the bag in that slot, Slot 0..n-1. "Bag -1" in a call = anywhere / all (never a real bag).</summary>
    public static class InvPos
    {
        public const int Bag0 = 255;
        public const int EquipEnd = 19, BagSlotStart = 19, BagSlotEnd = 23, PackStart = 23, PackEnd = 39;
        public const int BankStart = 39, BankEnd = 67, BankBagStart = 67, BankBagEnd = 74, BuybackStart = 74, BuybackEnd = 86;
        public const int KeyringStart = 86, KeyringEnd = 118;
        public static bool IsEquipment(int bag, int slot) { return bag == Bag0 && slot >= 0 && slot < EquipEnd; }
        public static bool IsBank(int bag, int slot) { return (bag == Bag0 && slot >= BankStart && slot < BankBagEnd) || (bag >= BankBagStart && bag < BankBagEnd); }
    }
}

// ---- Iteration 11 (world): proposal-world.md s5.4 verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (world, PLAN11): instances ---------------------------------------------------------------------
    /// <summary>One saved raid / heroic lock (SMSG_RAID_INSTANCE_INFO row; WoW's GetSavedInstanceInfo).</summary>
    public sealed class SavedInstanceView
    {
        public int MapId;
        public string Name;               // Map.dbc MapName_lang
        public int Difficulty;            // the save's difficulty 0..3
        public string DifficultyName;     // MapDifficulty.dbc field 22 key as WoW's text, e.g. "25 Player"
        public ulong InstanceGuid;        // as sent (0x1F40 << 48 | id); RaidInfoFrame shows the low 32 bits
        public bool Locked, Extended;
        public float SecondsLeft;         // counted down by the session
    }

    /// <summary>"You have entered an instance already in progress!" (SMSG_INSTANCE_LOCK_WARNING_QUERY; StaticPopup INSTANCE_LOCK).</summary>
    public sealed class InstanceLockView
    {
        public int MapId;
        public string Name;               // the map name with its difficulty when the map has more than one ("Naxxramas (25 Player)")
        public float SecondsLeft, Duration;
        public uint CompletedEncounterMask;
        public int EncountersDone, EncountersTotal;   // DungeonEncounter.dbc rows of the map + difficulty (BOSSES_KILLED)
        public bool PreviouslySaved;      // the packet's uint8 (INSTANCE_LOCK_TIMER_PREVIOUSLY_SAVED)
    }
}

// ---- Iteration 11 (pets): proposal-pets.md s5.1 verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (area pets): pets, totems, companions, the stable, vehicles, possess / charm ----------------------------------
    /// <summary>A pet command (AzerothCore CommandStates, Unit.h:574-579). Dismiss = COMMAND_ABANDON 3 (a summoned pet / guardian /
    /// charmed unit goes; a hunter pet is dismissed with the Dismiss Pet spell instead - IWowSession.DismissPet).</summary>
    public enum PetCommand : byte { Stay = 0, Follow = 1, Attack = 2, Dismiss = 3 }

    /// <summary>A pet's reaction (AzerothCore ReactStates, Unit.h:567-571).</summary>
    public enum PetReact : byte { Passive = 0, Defensive = 1, Aggressive = 2 }

    /// <summary>Whose bar PlayerState.ControlBar is (the four SMSG_PET_SPELLS writers, Player.cpp:9749-9983).</summary>
    public enum ControlBarKind : byte { None = 0, Pet = 1, Charm = 2, Possess = 3, Vehicle = 4 }

    public enum ControlSlotKind : byte { Empty = 0, Command = 1, Reaction = 2, Spell = 3 }

    /// <summary>How we are in a vehicle / in control of another unit (PlayerState.Vehicle.Mode).</summary>
    public enum VehicleMode : byte
    {
        Passenger = 0,   // a seat without VEHICLE_SEAT_FLAG_CAN_CONTROL: the server moves the vehicle, we ride (WowVehicle seats the avatar)
        Drive = 1,       // we control a vehicle that moves: WowMount drives it like a mount (movement goes out with the vehicle's guid)
        Turret = 2,      // we control a vehicle that cannot move (UNIT_FLAG_DISABLE_MOVE): we only turn it and aim (WowVehicle)
        Possess = 3      // we possess a unit (Eye of Acherus, Eye of Kilrogg, Mind Control, Eyes of the Beast): WowMount drives it, the avatar hidden
    }

    /// <summary>One button of PlayerState.ControlBar (AzerothCore's 10-slot CharmInfo bar).</summary>
    public sealed class ControlSlotView
    {
        public ControlSlotKind Kind;
        public int Action;                 // Command: (int)PetCommand; Reaction: (int)PetReact; Spell: spell id
        public string Name, IconPath, Tooltip;
        public bool Autocastable;          // a spell that may autocast (ACT_DISABLED 0x81 / ACT_ENABLED 0xC1); right-click toggles it
        public bool Autocast;              // autocast is on (ACT_ENABLED 0xC1)
        public bool Active;                // the current command / reaction; a spell the unit is casting or channelling now
        public bool Passive;               // a passive spell (shown greyed, never used)
        public float CooldownRemaining, CooldownTotal;   // seconds (the unit's own cooldowns, SMSG_SPELL_COOLDOWN with its guid)
        public bool Usable = true;
        public string UnusableReason;      // e.g. "Your pet is dead." (ERR_PET_SPELL_DEAD), null when usable
        public bool Harmful;               // aimed at enemies: uses the current target
        public bool NeedsUnit;             // needs a unit target (the current target)
        public bool NeedsPlace;            // needs a ground point (TARGET_FLAG_DEST_LOCATION): the ground circle (area spellfx) then UseControlSlotAt
        public bool Aimed;                 // TARGET_DEST_TRAJ 89 (cannons, harpoons): fired along the vehicle's aim (UseControlSlot works)
        public float RangeMax;             // yards (Spell.dbc), 0 = unknown
    }

    /// <summary>The pet / charm / possess / vehicle bar (SMSG_PET_SPELLS; offline: our imp / voidwalker).</summary>
    public sealed class ControlBarView
    {
        public ControlBarKind Kind;
        public ulong UnitGuid;             // the pet / charmed / possessed unit / the vehicle
        public int Family;                 // CreatureFamily.dbc id of a pet, 0 otherwise
        public float DurationTotal, DurationLeft;   // seconds the unit lasts (temporary pets and vehicles), 0 = permanent
        public PetCommand Command = PetCommand.Follow;
        public PetReact React = PetReact.Defensive;
        public readonly ControlSlotView[] Slots = new ControlSlotView[10];   // never null entries (Kind Empty)
        public readonly List<ControlSlotView> Spells = new List<ControlSlotView>();   // a permanent pet's spellbook (WoW's spellbook Pet tab)
        public int Counter;                // +1 whenever the server re-sends the bar (views rebuild)
    }

    /// <summary>Our own controllable pet's details (the unit of PlayerState.PetGuid).</summary>
    public sealed class PetInfoView
    {
        public ulong Guid;
        public int Number;                 // UNIT_FIELD_PETNUMBER (the stable's id), 0 = none (guardians)
        public string Name;                // the chosen name (SMSG_PET_NAME_QUERY_RESPONSE), else the creature's
        public int Family;                 // CreatureFamily.dbc id, 0 = unknown
        public string FamilyName;          // CreatureFamily.dbc field 10 ("Wolf"), null = unknown
        public string FamilyIcon;          // CreatureFamily.dbc field 27 (Interface\Icons\...), null = unknown
        public bool IsHunterPet;           // abandonable (UNIT_FIELD_BYTES_2 byte 2 & 0x02)
        public bool CanRename;             // UNIT_FIELD_BYTES_2 byte 2 & 0x01
        public int Happiness;              // 1 unhappy, 2 content, 3 happy (Pet.cpp:894-902); 0 = not a hunter pet
        public int HappinessValue;         // UNIT_FIELD_POWER5 raw (0..1050000)
        public int Xp, XpNext;             // UNIT_FIELD_PETEXPERIENCE / PETNEXTLEVELEXP (owner only)
    }

    /// <summary>One of our totems (SMSG_TOTEM_CREATED; offline: the session's own totems).</summary>
    public sealed class TotemView
    {
        public int Slot;                   // 0 fire, 1 earth, 2 water, 3 air (SummonProperties.Slot - 1)
        public ulong Guid;
        public int SpellId;                // the summoning spell (icon, name)
        public string Name, IconPath;
        public float Duration, Remaining;  // seconds
    }

    /// <summary>The open stable (MSG_LIST_STABLED_PETS).</summary>
    public sealed class StableView
    {
        public ulong NpcGuid;              // the stable master (our own guid when an aura opened it)
        public int SlotsBought;            // 0..4
        public int NextSlotPrice;          // copper (StableSlotPrices.dbc row SlotsBought + 1), 0 when all 4 are bought
        public readonly List<StablePetView> Pets = new List<StablePetView>();
        public int Counter;                // +1 per list received
    }

    public sealed class StablePetView
    {
        public int Number;                 // the server's pet number
        public int Entry;                  // creature entry
        public int Level;
        public string Name;
        public bool Current;               // flags 1 = the active pet (shown in the "current pet" slot), 2 = in the stable
        public int Slot;                   // 0 = current, 1..4 = stable slot (in list order)
        public int Family;                 // from the creature query (0 until it answers)
        public string FamilyName, IconPath;
    }

    /// <summary>We sit in a vehicle or control a possessed unit (PlayerState.Vehicle).</summary>
    public sealed class VehicleView
    {
        public ulong Guid;                 // the vehicle / the possessed unit
        public VehicleMode Mode;
        public int VehicleId;              // Vehicle.dbc id (0 for a possessed unit)
        public int SeatIndex = -1;         // our seat (transport seat byte), -1 for a possessed unit
        public int SeatId;                 // VehicleSeat.dbc id (0 unknown)
        public uint SeatFlags, SeatFlagsB; // VehicleSeat.dbc fields 1 / 45
        public bool CanLeave;              // VehicleSeatEntry::CanEnterOrExit (DBCStructure.h:2149-2154); possess: always
        public bool CanSwitchSeat;         // VEHICLE_SEAT_FLAG_CAN_SWITCH 0x04000000
        public bool HideAvatar;            // possess, or VEHICLE_SEAT_FLAG_HIDE_PASSENGER 0x200: the Somnium avatar is not drawn
        public bool OwnSpells;             // VEHICLE_SEAT_FLAG_CAN_ATTACK 0x4000: our own spells work from this seat
        public int DisplayId;              // the vehicle's CreatureDisplayInfo (UNIT_FIELD_DISPLAYID)
        public string Name;
        public WowPos SeatOffset;          // our offset on it (VehicleSeat m_attachmentOffsetX/Y/Z, + the boarding spline's facing)
        public float TurnSpeed, PitchSpeed, PitchMin, PitchMax;   // Vehicle.dbc fields 2-5 (rad/s, rad); PitchMin / Max = the DBC pair only with VEHICLE_FLAG_CUSTOM_PITCH 0x40, else -pi/2..pi/2 (VehicleDefines.h:45)
        public bool Pitching;              // VEHICLE_FLAG_ALLOW_PITCHING 0x10: the vehicle pitches (MOVEMENTFLAG2_ALWAYS_ALLOW_PITCHING, every MovementInfo carries pitch)
        public bool AimAdjustable;         // VEHICLE_FLAG_ADJUST_AIM_ANGLE 0x400: WoW shows the pitch buttons (MainMenuBar.lua:106)
        public float AimPitch;             // the current aim (radians), turrets and aimed spells
        public bool CanFly;                // the controlled unit flies (MOVEMENTFLAG_CAN_FLY / FLYING / DISABLE_GRAVITY in its movement)
        public int SeatCount;              // seats of the vehicle (Vehicle.dbc seat ids != 0)
        public int Counter;                // +1 per change (seat, mode)
        public bool HasVehicleUi;          // VEHICLE_SEAT_FLAG_CAN_CAST 0x20000000 "Lua_UnitHasVehicleUI" (DBCEnums.h:473): WoW's VehicleMenuBar; else the bonus-bar look
        public bool CanSteer;              // Drive / Possess: SMSG_CLIENT_CONTROL_UPDATE(unit, 1) received and not taken back (we send its movement)
        public bool ServerMoving;          // the server runs a path on the controlled unit (its SMSG_MONSTER_MOVE): we follow it, we do not steer
    }
}

// ---- Iteration 11 (spellfx): proposal-spellfx.md s5.1 verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (designer spellfx): spell targeting, ground areas, every-target hits, weapon enchants, runes -------------------
    /// <summary>What a spell waits for before it is cast (PlayerState.Targeting.Kind, AbilityView.Targeting).</summary>
    public enum SpellTargetingKind : byte { None = 0, Ground = 1, Item = 2 }

    /// <summary>
    /// WoW's "spell on the cursor" (SpellIsTargeting): a chosen spell waits for a place on the ground (Spell.dbc Targets has
    /// TARGET_FLAG_DEST_LOCATION 0x40: the green circle) or for an item (TARGET_FLAG_ITEM 0x10 without SPELL_ATTR0_HELD_ITEM_ONLY: enchants,
    /// poisons, Disenchant...). Set by CastSpell / CastSpellHeld / UseItem of such a spell; ended by ConfirmTargetingAt / ConfirmTargetingItem /
    /// CancelTargeting, by death, by another CastSpell (which replaces it, as WoW) and by a far teleport. Views draw it and route clicks to it.
    /// </summary>
    public sealed class SpellTargetingView
    {
        public int SpellId;
        public string Name;               // the spell's name (an item's spell: the item's name)
        public SpellTargetingKind Kind;
        public float Radius;              // Ground: yards - the largest SpellRadius of the effects aimed at the place (+ SPELLMOD_RADIUS when known); a spell
                                          //   without an area (Eagle Eye, Far Sight) gets 2 (SpellTargeting.MarkerRadius, ours)
        public float RangeMin, RangeMax;  // Ground: yards from the player - AC's range of a place = the FRIENDLY pair (Object.cpp:3967-3989, no unit target),
                                          //   with SPELLMOD_RANGE talent mods when known; RangeMax 0 = no limit
        public bool RangeExact;           // the range includes our talent mods (offline always): out of range is refused locally; else only coloured red
        public bool Held;                 // started by CastSpellHeld (immersive casting): the confirmed place starts a held cast
        public int FromBagIndex = -1;     // the spell of a used bag item (online CMSG_USE_ITEM); -1 = a spellbook spell
        public int ItemClass = -1;        // Item kind: Spell.dbc EquippedItemClass (-1 = any item), EquippedItemSubClassMask, EquippedItemInventoryTypeMask -
        public int ItemSubClassMask, ItemInventoryTypeMask;   //   a hint for the bag / character windows (the server decides)
        public int Serial;                // +1 at every new targeting (views reset their aiming state on a change)
    }

    /// <summary>A spell area on the ground: online a TYPEID_DYNAMICOBJECT (every caster's), offline our simulation's. Stable instances.</summary>
    public sealed class AreaEffectView
    {
        public ulong Guid;                // online: the dynamic object's guid; offline: ours (0xF1F0000000000000 | n)
        public int SpellId;
        public ulong CasterGuid;
        public WowPos Pos;                // centre on the ground (WoW coordinates)
        public float Radius;              // yards (DYNAMICOBJECT_RADIUS)
        public int Type = 1;              // DynamicObjectType: 1 area spell, 2 far-sight focus (views draw type 1 only)
        public float Age;                 // seconds since it appeared here
    }

    /// <summary>One death knight rune (PlayerState.Runes in server order: 0-1 blood, 2-3 unholy, 4-5 frost). Online only.</summary>
    public sealed class RuneView
    {
        public int Type;                  // RuneType now: 0 blood, 1 unholy, 2 frost, 3 death
        public int BaseType;              // the slot's own type
        public bool Ready;
        public float Cooldown;            // seconds of a full regeneration (1 / PLAYER_RUNE_REGEN of its type; 10 s at login)
        public float Remaining;           // seconds until ready (0 = ready)
    }
}

// ---- Iteration 11 (quests): proposal-quests.md contract (QuestConfirmView) verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    /// <summary>A party member accepted a party-accept quest (SMSG_QUEST_CONFIRM_ACCEPT): WoW's QUEST_ACCEPT popup (PlayerState.PendingQuestConfirm).</summary>
    public sealed class QuestConfirmView
    {
        public int QuestId;
        public string Title;
        public ulong FromGuid;            // the party member who accepted it
        public string FromName;           // their name ("Someone", ours, until the name query answers)
        public bool LogFull;              // our log is full: QUEST_ACCEPT_LOG_FULL (Yes disabled)
        public int Serial;                // +1 per new request (the popup shows once per change)
    }
}

// ---- Iteration 11 (trainer): proposal-trainer.md s5.1(d) verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (designer trainer): trainers, skills, professions ------------------------------------------------------
    /// <summary>AzerothCore Trainer::SpellState (Trainer.h:39-44) = the SMSG_TRAINER_LIST "Usable" byte: green / red / grey.</summary>
    public enum TrainerSpellState : byte { Available = 0, Unavailable = 1, Known = 2 }

    /// <summary>AzerothCore Trainer::Type (Trainer.h:31-37) = SMSG_TRAINER_LIST TrainerType. Tradeskill = WoW's IsTradeskillTrainer().</summary>
    public enum TrainerKind : byte { Class = 0, Mount = 1, Tradeskill = 2, Pet = 3 }

    /// <summary>One line of the trainer window.</summary>
    public sealed class TrainerServiceView
    {
        public int SpellId;               // the service (CMSG_TRAINER_BUY_SPELL id; for professions a LEARN_SPELL wrapper, e.g. 3911)
        public int ShowSpellId;           // the spell it teaches (the wrapper's first TriggerSpell, else SpellId): name, rank, icon, tooltip
        public string Name, Rank;         // Spell.dbc of ShowSpellId ("Tailoring", "Apprentice"); Rank "" = none
        public string IconPath, Tooltip;
        public TrainerSpellState State;
        public int MoneyCost;             // copper (the server applied the reputation discount)
        public int ProfessionCost;        // PointCost[1]: 1 = the first rank of a primary profession (takes one of the two profession slots)
        public int ReqLevel, ReqSkillLine, ReqSkillRank;
        public int[] ReqAbility = new int[3];
        public string Requirements;       // "Requires: Level <color=#ff2020>12</color>, Tailoring (<color=#ffffff>50</color>), <color=#ff2020>Fireball</color>" - GlobalStrings TRAINER_REQ_* formats (5.3), null = none
        public bool CanTrain;             // Available, affordable and (ProfessionCost 0 or PlayerState.FreeProfessions >= ProfessionCost)
        public bool NeedsConfirm;         // Tradeskill trainer + ProfessionCost > 0: WoW's CONFIRM_PROFESSION popup before buying
    }

    /// <summary>The open trainer (IWowSession.CurrentTrainer).</summary>
    public sealed class TrainerView
    {
        public ulong NpcGuid;
        public string NpcName;
        public string Greeting;           // the server's trainer greeting (may be ""); offline our own text
        public TrainerKind Kind;
        public List<TrainerServiceView> Services = new List<TrainerServiceView>();   // ours: ReqLevel, then name, then rank (logged once)
        public int Version;               // +1 on every change (a buy, a re-list, money, level): views rebuild on a change
        public bool Offline;              // offline trainer (spells are learned on level-up there; Train teaches a missed one for free)
    }

    /// <summary>One line of the Skills pane (SkillFrame).</summary>
    public sealed class SkillView
    {
        public int SkillId;               // SkillLine.dbc id
        public string Name;               // SkillLine DisplayName_lang
        public int Category;              // SkillLine categoryId: 5 attributes, 6 weapon, 7 class, 8 armor, 9 secondary, 10 languages, 11 professions
        public string CategoryName;       // SkillLineCategory.dbc name ("Professions")
        public int Value, Max, Step;      // PLAYER_SKILL_INFO: value | max << 16; id | step << 16. Shown as 1/1 (a proficiency bar) when the SRCI flags have 0x400 SKILL_FLAG_MONO_VALUE ("clientside display flag", AC DBCEnums.h:390)
        public int TempBonus, PermBonus;  // the bonus int: int16 temp | int16 perm << 16
        public bool Abandonable;          // SkillRaceClassInfo flags & 0x20 SKILL_FLAG_UNLEARNABLE: the "Unlearn" button
        public bool IsPrimaryProfession;  // Category 11
        public bool HasTradeWindow;       // a known spell with SPELL_EFFECT_TRADE_SKILL (47) has its SkillLineAbility row on this line: a profession window exists
        public string IconPath;           // SkillLine spellIcon -> SpellIcon.dbc path, null = none
        public string Verb;               // SkillLine AlternateVerb_lang @38 ("Enchant", "Embroider", "Engrave"), "" = none
        public string Description;        // SkillLine Description_lang @20 (SkillFrame detail; "" for class skills / languages, measured)
    }

    /// <summary>WoW's TradeSkillTypeColor (Blizzard_TradeSkillUI.lua:15-20): orange, yellow, green, grey.</summary>
    public enum RecipeDifficulty : byte { Optimal = 0, Medium = 1, Easy = 2, Trivial = 3 }

    public sealed class ReagentView
    {
        public int ItemEntry;
        public string Name, IconPath;     // online: the item answer (asked when unknown, Name null until then)
        public int Need, Have;            // Have = this item in the backpack + bags
    }

    /// <summary>One recipe of the profession window.</summary>
    public sealed class RecipeView
    {
        public int SpellId;
        public string Name, IconPath, Description, Tooltip;
        public string MadeIconPath, MadeTooltip, MadeName;   // lead fix 2026-10-11: the made item's icon / whole tooltip / name (null = none or not known yet)
        public RecipeDifficulty Difficulty;
        public string Group;              // ours: the made item's ItemSubClass name, "" for enchants (header rows)
        public int Craftable;             // how many the reagents allow now (WoW's "[n]")
        public int CreatesItem, MinMade, MaxMade;   // the EffectItemType + BasePoints+1 / BasePoints+DieSides of a CREATE_ITEM 24 / CREATE_ITEM_2 157 / CREATE_RANDOM_ITEM 59 effect; 0 = makes no item (an enchant's effect-53 item is the vellum scroll, not counted)
        public List<ReagentView> Reagents = new List<ReagentView>();
        public string Tools;              // "Anvil, <color=#ff2020>Blacksmith Hammer</color>": WoW's BuildColoredListString - present = plain, missing = RED_FONT_COLOR_CODE; null = none
        public bool ToolsMet;             // every focus / tool present (the server decides; this only colours)
        public float CooldownRemaining;   // seconds (transmutes ...)
        public bool ItemTarget;           // Spell.dbc Targets & 0x10 TARGET_FLAG_ITEM and attr0 lacks 0x200 HELD_ITEM_ONLY (enchants, runes): Create starts spellfx's item targeting
        public string Verb;               // null = a recipe that makes an item ("Create" + Create All / count); else the skill line's AlternateVerb ("Enchant") for a recipe that makes no item (Blizzard_TradeSkillUI.lua:386-411)
    }

    /// <summary>The open profession window (IWowSession.CurrentTradeSkill).</summary>
    public sealed class TradeSkillView
    {
        public int SkillId;               // SkillLine id ("Mining" for the Smelting window)
        public string Name;
        public int Rank, MaxRank;
        public string Verb;               // the skill line's AlternateVerb ("" = none); the Create button reads the SELECTED recipe's RecipeView.Verb ?? "Create"
        public List<RecipeView> Recipes = new List<RecipeView>();   // ours: Group, then difficulty (orange first), then name
        public int Version;               // +1 on every change (bags, skill, cooldowns, learned recipes)
        public int QueueSpellId, QueueLeft;   // "Create N" in progress: the recipe and how many casts are still to come (0 = idle)
    }
}

// ---- Iteration 11 (motion): proposal-motion.md contract (ForcedMoveKind / ForcedMoveView) verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (motion, lead contract): movement the server or a spell forces on the player ----------------------------
    /// <summary>What moves the local player (ForcedMoveView.Kind).</summary>
    public enum ForcedMoveKind : byte
    {
        None = 0,
        Path = 1,       // a server path on the player (Charge, Intercept, Intervene, Feral Charge, Death Grip / pulls, jumps): follow Points over DurationMs
        Knockback = 2,  // a ballistic launch (knockbacks, Disengage): fly from Start with the launch velocity until the ground, then IWowSession.ForcedMoveDone
        Fleeing = 3,    // one run segment of a fear (one after the other while PlayerState.ControlLost)
        Confused = 4,   // one walk segment of a confuse / polymorph wander (ControlLost)
        Taxi = 5        // the flight path's spline: WowFlight rides it (WowForcedMove never drives it)
    }

    /// <summary>A forced movement of the local player (PlayerState.ForcedMove). WoW world coordinates. Never changed after it is
    /// published; a new move replaces it (Id grows). Pure data: Play/Motion/ForcedMoveMath evaluates it.</summary>
    public sealed class ForcedMoveView
    {
        public int Id;                     // +1 per forced move of this session
        public ForcedMoveKind Kind;
        public int SpellId;                // the spell that caused it when known, 0 = unknown / server movement
        public string Source;              // log text, e.g. "SMSG_MONSTER_MOVE spline 7731" / "SMSG_MOVE_KNOCK_BACK counter 12" / "offline Charge"
        public float AgeAtPublish;         // seconds of the move already gone when it was published (0 normally)
        // Path / Fleeing / Confused / Taxi:
        public float[] Points;             // x,y,z triples; Points[0..2] = the start (the server's position of the player)
        public int DurationMs;             // the whole path
        public bool Smooth;                // Catmull-Rom through the points (flying spline), else straight segments at constant speed
        public float ParabolicAccel;       // MoveJump: vertical acceleration (yd/s^2) of the arc, 0 = none
        public int ParabolicStartMs;       // MoveJump: when the arc starts (ms)
        public float EndFacing = float.NaN;// WoW orientation at the end when the server fixed one, NaN = keep the travel direction
        public ulong FacingGuid;           // face this unit at the end (a charge at its target), 0 = none
        public int ContinuesOnMap = -1;    // Taxi only: the flight goes on on this map after the spline's end (AC waits for
                                           // CMSG_MOVE_SPLINE_DONE, then far-teleports: TaxiHandler.cpp:228-239), -1 = the spline ends the flight
        // Knockback:
        public WowPos Start;               // where the launch happened
        public float DirCos, DirSin;       // horizontal launch direction (WoW x = cos, y = sin), away from the source
        public float SpeedXY;              // yd/s horizontal
        public float SpeedUp;              // yd/s vertical at launch, + = up
        public bool SlowFall;              // FALLING_SLOW at launch: the fall speed is capped at 7 yd/s instead of 60.148
    }
}

// ---- Iteration 11 (talents): proposal-talents.md contract verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (area talents): talents, glyphs, dual spec, pet talents, reputation, currency, titles, achievements -----------

    /// <summary>One talent group (spec) of the player, or the pet's talents. Owners may ADD fields.</summary>
    public sealed class TalentSpecView
    {
        /// <summary>Talent.dbc id -> learned rank COUNT 1..5 (absent = 0). Online: SMSG_TALENTS_INFO's 0-based rank + 1.</summary>
        public readonly Dictionary<int, int> Ranks = new Dictionary<int, int>();
        /// <summary>GlyphProperties.dbc id per socket 0..5 (socket = GlyphFrame button id - 1 = GlyphSlot Order - 1), 0 = empty. Player specs only.</summary>
        public readonly int[] Glyphs = new int[6];
        public int Rank(int talentId) { int r; return Ranks.TryGetValue(talentId, out r) ? r : 0; }
    }

    /// <summary>The player's talents (PlayerState.Talents). Counter +1 on every change: views rebuild when it moves.</summary>
    public sealed class TalentsView
    {
        public bool Known;                 // online: the first player SMSG_TALENTS_INFO arrived; offline: always true
        public int FreePoints;             // unspent points of the ACTIVE spec (SMSG_TALENTS_INFO / PLAYER_CHARACTER_POINTS1)
        public int SpecCount = 1;          // 1, or 2 after "Purchase a Dual Talent Specialization"
        public int ActiveSpec;             // 0 primary, 1 secondary
        public readonly TalentSpecView[] Specs = { new TalentSpecView(), new TalentSpecView() };
        public readonly int[] GlyphSlotIds = new int[6];   // GlyphSlot.dbc id per socket (PLAYER_FIELD_GLYPH_SLOTS_1); its TypeFlags 0 major / 1 minor
        public int GlyphsEnabledMask;      // PLAYER_GLYPHS_ENABLED: bit i = socket i unlocked
        public bool GlyphPick;             // a glyph item used from the bags waits for a socket (the session keeps its item guid); answer with InscribeGlyph
        public int GlyphPickGlyphId;       // its GlyphProperties id
        public bool WipePending;           // a class trainer asked "unlearn all talents?" (MSG_TALENT_WIPE_CONFIRM); answer with AnswerTalentWipe
        public int WipeCost;               // copper
        // hunter pet (SMSG_TALENTS_INFO isPet 1)
        public bool PetKnown;
        public int PetFreePoints;
        public int PetTalentTabId;         // TalentTab.dbc id of the pet's tree (409 / 410 / 411), 0 = no pet with talents
        public int PetCategory = -1;       // CreatureFamily.dbc field 9 of the pet (bit in Talent.dbc CategoryMask), -1 unknown
        public readonly TalentSpecView Pet = new TalentSpecView();
        public int Counter;
        public int OpenRequest;            // +1 = the Talents window should open (a glyph waits for its socket -> Glyphs tab; a trainer's wipe
                                           // confirm -> the active spec, UIParent.lua:880-891); read by the registry spec (6.2 hook 7)
    }

    /// <summary>One row of WoW's reputation list, display order (headers, sub headers, factions). PlayerState.Reputations.</summary>
    public sealed class FactionView
    {
        public int FactionId;              // Faction.dbc id (0 for the "Other" / "Inactive" headers of ours)
        public int RepListId = -1;         // Faction.dbc reputationListID (the server's index), -1 = a header without its own reputation
        public string Name, Description;
        public bool IsHeader, IsChild, HasRep;   // ReputationFrame.lua's isHeader / isChild / hasRep
        public int Standing;               // base + server standing (ReputationMgr::GetReputation)
        public int Rank;                   // 0 Hated .. 7 Exalted
        public int RankMin, RankMax;       // standing bounds of Rank (bar = Standing - RankMin of RankMax - RankMin + 1)
        public bool AtWar, CanToggleAtWar, Inactive, Watched;
    }

    /// <summary>One currency of WoW's Currency tab. PlayerState.Currencies (known ones only).</summary>
    public sealed class CurrencyView
    {
        public int ItemEntry;              // CurrencyTypes.dbc ItemId
        public int CategoryId;             // CurrencyCategory.dbc id
        public string CategoryName;
        public ItemView Item;              // name / icon / tooltip from the server's item answer (Name null until it arrived)
        public int Count;
    }

    /// <summary>A known title (PlayerState.KnownTitles).</summary>
    public sealed class TitleView
    {
        public int BitIndex;               // CharTitles.dbc bit_index (PLAYER_CHOSEN_TITLE value)
        public string Format;              // the gender's name with "%s" for the character name, e.g. "Private %s"
    }

    /// <summary>The player's achievements (PlayerState.Achievements). Online only; offline stays empty (Known false).</summary>
    public sealed class AchievementsView
    {
        public bool Known;                 // SMSG_ALL_ACHIEVEMENT_DATA arrived
        public readonly Dictionary<int, long> Completed = new Dictionary<int, long>();   // Achievement.dbc id -> completion (unix seconds, UTC)
        public readonly Dictionary<int, long> Criteria = new Dictionary<int, long>();    // Achievement_Criteria.dbc id -> counter
        public int Points;                 // sum of Achievement.dbc points over Completed
        public int Counter;
    }
}

// ---- Iteration 11 (npc): proposal-npc.md s5.2 verbatim
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (area npc): containers, merchant, gossip options, item text. Positions are uicore's InvPos (bag, slot) pairs. ----

    /// <summary>One container as the bag windows draw it: the address of slot i is (Bag, SlotBase + i).</summary>
    public sealed class ContainerView
    {
        public int Bag;                   // 255 (InvPos.Bag0) for backpack / bank / keyring, else the bag's slot (19..22, 67..73)
        public int SlotBase;              // 23 backpack, 39 bank, 86 keyring, 0 inside a bag
        public int InvSlot = -1;          // where the bag ITEM sits in bag 255 (19..22 / 67..73), -1 for backpack / bank / keyring
        public ItemView BagItem;          // the bag item itself (name, icon, tooltip), null for backpack / bank / keyring / an empty bag slot
        public int Size;                  // slots (0 = no bag in this bag slot)
        public ItemView[] Slots = new ItemView[0];   // Length == Size; null = empty slot (stable instances, rebuilt only on change)
        public int Free;                  // empty slots
        public int Family;                // ItemBagFamily mask it accepts (0 = any item)
        public bool Locked;               // a bank bag slot not bought yet (drawn red, BankFrame.lua UpdateBagSlotStatus)
    }

    /// <summary>One line of the merchant (SMSG_LIST_INVENTORY entry; offline: our vendor list).</summary>
    public sealed class MerchantItemView
    {
        public int VendorSlot;            // the server's 1-based list slot (echoed in CMSG_BUY_ITEM)
        public ItemView Item;             // name / icon / quality / tooltip; Item.Count = BuyCount (the number on the button)
        public int Price;                 // copper per purchase (reputation discount already applied by the server); 0 = extended cost only
        public int BuyCount = 1;          // items per purchase
        public int Stock = -1;            // left in stock, -1 = unlimited
        public int MaxDurability;
        public int ExtendedCostId;        // ItemExtendedCost.dbc id, 0 = none
        public int HonorCost, ArenaCost;  // ItemExtendedCost.dbc HonorPoints / ArenaPoints per purchase
        public int[] CostItems = new int[0], CostCounts = new int[0];   // ItemExtendedCost.dbc ItemID / ItemCount (non-zero ones), per purchase
        public int RequiredArenaRating;
        public bool Usable = true;        // class / race / level allow it (red tint when false; MerchantFrame.lua:139-144)
        public int MaxPurchases = 1;      // what the quantity picker allows: min(255, MaxStack / BuyCount, affordable, Stock / BuyCount)
    }

    /// <summary>The open merchant (after Interact on a vendor or its gossip option), or null.</summary>
    public sealed class MerchantView
    {
        public ulong VendorGuid;
        public string VendorName;
        public List<MerchantItemView> Items = new List<MerchantItemView>();
        public bool CanRepair;            // UNIT_NPC_FLAG_REPAIR (an armorer): the repair buttons show
        public int RepairAllCost;         // copper, the client's estimate (AC Player::DurabilityRepair formula, no reputation discount), 0 = nothing to repair
        public bool Empty;                // the vendor has nothing for you (count 0)
        public int Version;               // +1 whenever Items / stock change (views redraw on change)
    }

    /// <summary>A gossip option of an NPC / object dialog (GossipFrame title button).</summary>
    public sealed class GossipOptionView
    {
        public int Index;                 // the server's option id (CMSG_GOSSIP_SELECT_OPTION); offline: ours
        public int Icon;                  // 0 Gossip, 1 Vendor, 2 Taxi, 3 Trainer, 4 Healer, 5 Binder, 6 Banker, 7 Petition, 8 Tabard, 9 BattleMaster, 10 Unlearn (art Interface\GossipFrame\<X>GossipIcon); others draw Gossip
        public string Text;               // $N/$C/$R/$B resolved
        public bool Coded;                // asks for a code first (GOSSIP_ENTER_CODE "Please enter code:")
        public int BoxMoney;              // copper the option costs (GOSSIP_CONFIRM with a money frame), 0 = none
        public string BoxText;            // GOSSIP_CONFIRM text, null/empty = no confirmation
    }

    /// <summary>The open ItemTextFrame (a readable item, a book / plaque object, a letter), or null.</summary>
    public sealed class ItemTextView
    {
        public ulong SourceGuid;          // the item or game object
        public string Title;              // the item / object name
        public string Text;               // the current page ($B resolved), null while it loads
        public int Page = 1;              // 1-based
        public bool HasNext, HasPrevious;
        public string Material = "Parchment";   // PageTextMaterial.dbc name ("Parchment", "Stone", "Marble", "Silver", "Bronze", "Valentine", "Illidan")
        public int Version;               // +1 on every change
    }

    /// <summary>
    /// Lead 2026-10-04 (the user: "add an immersion toggle, so people have to sit down irl to eat and drink"): the Game layer's rule for
    /// starting food / drink, asked by both sessions just before they start one (online SendUseItem before CMSG_USE_ITEM, offline before
    /// the food "aura") - so a refused item is never used up. SitRule returns null = allowed, else the refusal (shown as a UI error).
    /// </summary>
    public static class WowConsumeGate
    {
        public static System.Func<string> SitRule;

        public static string Refusal()
        {
            try { return SitRule != null ? SitRule() : null; }
            catch (System.Exception) { return null; }
        }

        /// <summary>Food / drink: an aura interrupted when not seated (AURA_INTERRUPT_FLAG_NOT_SEATED 0x40000) with SPELL_AURA_MOD_REGEN 84
        /// or MOD_POWER_REGEN 85 - GearStatePose.Consuming's rule (measured: Food 433 / Drink 430 auraInt 0x40080).</summary>
        public static bool IsFoodOrDrink(Spells.SpellInfo s)
        {
            return s != null && (s.AuraInterruptFlags & 0x40000) != 0 && (s.HasAura(Spells.SpellAura.ModRegen) || s.HasAura(Spells.SpellAura.ModPowerRegen));
        }
    }
}
