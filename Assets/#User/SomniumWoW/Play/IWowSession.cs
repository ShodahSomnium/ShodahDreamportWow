using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    /// <summary>
    /// CONTRACT (shared, pure C#). One running game for the local player. Views (creatures in the world, the left-arm UI,
    /// the desktop HUD, the weapon on the avatar) talk ONLY to this interface, so the offline simulation of iteration 2 and
    /// the AzerothCore network client of a later iteration are interchangeable. Every member is called from Unity's main
    /// thread. Owners may ADD members; do not rename or remove.
    /// </summary>
    public interface IWowSession
    {
        /// <summary>The local player's character.</summary>
        PlayerState Player { get; }

        /// <summary>The WoW map the player is on (Map.dbc id).</summary>
        int MapId { get; }

        /// <summary>Every creature, NPC and object near the player (not the player). Stable instances, updated in place.</summary>
        IReadOnlyList<EntityState> Entities { get; }

        /// <summary>An entity by guid, or null.</summary>
        EntityState Find(ulong guid);

        /// <summary>The player's current target (0 = none).</summary>
        ulong TargetGuid { get; }

        /// <summary>Open NPC conversation (after Interact on an NPC), or null.</summary>
        NpcDialog CurrentDialog { get; }

        /// <summary>Open loot window (after Interact on a lootable corpse), or null.</summary>
        LootView CurrentLoot { get; }

        /// <summary>Advances the game: offline runs the simulation, online pumps the network. Call once per frame.</summary>
        void Tick(float deltaSeconds);

        /// <summary>The avatar's position every frame (converted to WoW coordinates). moving = the avatar is walking.</summary>
        void SetPlayerPosition(WowPos pos, bool moving);

        /// <summary>Lead 2026-10-04 (Immersive eating): stand up from sitting / eating / drinking / kneeling / sleeping - online
        /// CMSG_STANDSTATECHANGE 0 (the realm removes the food / drink aura: AURA_INTERRUPT_FLAG_NOT_SEATED), offline the food stops. why
        /// goes to the log. Nothing when already standing or dead.</summary>
        void StandUp(string why);

        /// <summary>Drains one event for floating combat text, messages, sounds and teleports. Loop until false each frame.</summary>
        bool TryDequeueEvent(out GameEvent e);

        void SetTarget(ulong guid);
        void TargetNearestEnemy();

        /// <summary>Selects the target and starts auto-attack (a VR weapon swing that hits an enemy calls this).</summary>
        void StartAttack(ulong guid);
        void StopAttack();

        /// <summary>Casts a spell from the spellbook. targetGuid 0 = the current target, or self for helpful spells.</summary>
        void CastSpell(int spellId, ulong targetGuid);
        void CancelCast();

        /// <summary>Draws or sheathes the weapons (the weapon grab in VR, a key on desktop).</summary>
        void SetSheath(SheathState state);

        /// <summary>Talk to an NPC (opens CurrentDialog) or loot a corpse (opens CurrentLoot).</summary>
        void Interact(ulong guid);
        void CloseDialog();
        void CloseLoot();

        void AcceptQuest(int questId);
        void CompleteQuest(int questId, int rewardChoiceIndex);
        void AbandonQuest(int questId);

        void LootItem(int index);
        void LootAll();

        /// <summary>Equips the bag item at bagIndex (swapping the old item into the bags).</summary>
        void EquipItem(int bagIndex);

        /// <summary>Uses a bag item (food, potion...).</summary>
        void UseItem(int bagIndex);

        /// <summary>After death: come back (offline: at the zone's graveyard with part of your health).</summary>
        void ReleaseSpirit();

        /// <summary>Puts a spell on action button slot 0..143 (0..11 before iteration 11); 0 clears it. = SetActionButton(slot,
        /// WowActionSlots.Spell(spellId)) (iteration 11, vrcast: doc only - wave-0 sessions still accept 0..11 until vrcast's build).</summary>
        void SetActionBarSlot(int slot, int spellId);

        // ---- iteration 3: gameplay settings (WowSettings -> Front/UI). PlayerState.Difficulty / AutoLoot mirror them.
        /// <summary>0 easy (~0.7x creature damage, ~0.85x health), 1 normal (default), 2 hard (~1.3x / 1.2x).</summary>
        void SetDifficulty(int difficulty);
        /// <summary>Loot everything as soon as a corpse is opened.</summary>
        bool AutoLoot { get; set; }

        // ---- iteration 4 (lead contract): flight paths and immersive casting.
        /// <summary>Open flight map (after Interact on a flight master; talking to one also discovers its node), or null.</summary>
        TaxiView CurrentTaxi { get; }
        void CloseTaxi();
        /// <summary>Pays and starts the flight to destinationNodeId (TaxiStarted event, Player.OnTaxi). False + an Error event when not possible.</summary>
        bool TakeTaxi(int destinationNodeId);
        /// <summary>The Unity flight reached the end of the route (the avatar is at the landing point): ends OnTaxi (TaxiFinished).</summary>
        void FinishTaxi();
        /// <summary>Immersive casting: like CastSpell, but when the cast time is over the spell waits (CastHeld, Player.HeldSpellId).</summary>
        void CastSpellHeld(int spellId, ulong targetGuid);
        /// <summary>Fires the held spell at targetGuid (0 = the target it was cast at). No-op when nothing is held.</summary>
        void ReleaseHeldSpell(ulong targetGuid);

        // ---- iteration 7 (lead contract): mounts. Summoning = CastSpell(mountSpellId, 0); casting any mount while mounted dismounts.
        /// <summary>Leaves the mount (offline: removes the mount aura; online: CMSG_CANCEL_MOUNT_AURA). No-op when not mounted. Dismounted follows.</summary>
        void Dismount();
        /// <summary>Right-click on one of the player's buffs. Offline: removes a positive, cancellable aura (the mount aura = Dismount).
        /// Online: CMSG_CANCEL_AURA (the mount aura: CMSG_CANCEL_MOUNT_AURA).</summary>
        void CancelAura(int spellId);
        /// <summary>The local ride's motion this frame. WowGame calls it every frame right BEFORE SetPlayerPosition (Driven = false on foot).</summary>
        void SetPlayerMotion(PlayerMotion motion);
        /// <summary>Where the player stands (outdoors, area, liquid), on change and at most 5 times a second, on foot too. Offline rules use
        /// it; online only logs changes (the server decides).</summary>
        void SetPlayerPlace(PlayerPlace place);
        /// <summary>Lead decision D13 (PLAN7 s7.4): a GM chat command for the in-world testing buttons (e.g. ".learn 34091",
        /// ".tele HellfirePeninsula"). Online: CMSG_MESSAGECHAT (say, universal) - only text starting with '.' is ever sent; the server's
        /// answer (system message) comes back as an Info event. Offline: an Info event pointing at the testing switches, nothing else.</summary>
        void RunGmCommand(string command);

        // ---- stage O2 (lead contract, PLAN9 s3): WoW's death flow (ReleaseSpirit above makes the ghost)
        /// <summary>A ghost at its corpse (PlayerState.CanReclaim): resurrect there. Online: CMSG_RECLAIM_CORPSE.</summary>
        void ReclaimCorpse();
        /// <summary>A ghost at a spirit healer (PlayerState.NearSpiritHealer): resurrect there (resurrection sickness where WoW applies it).
        /// Online: CMSG_SPIRIT_HEALER_ACTIVATE.</summary>
        void AcceptSpiritHealer();

        // ---- stage S1 (lead contract, PLAN10 s3): chat and groups (online; offline answers with an Info event)
        /// <summary>Sends a chat line (Say, Yell, Emote, Party, Whisper to whisperTo). Text starting with '.' is NOT sent here (RunGmCommand).</summary>
        void SendChat(ChatKind kind, string text, string whisperTo);
        /// <summary>Invites a player by character name (CMSG_GROUP_INVITE).</summary>
        void InviteToGroup(string characterName);
        /// <summary>Answers PlayerState.PendingInviteFrom.</summary>
        void AnswerGroupInvite(bool accept);
        /// <summary>Leaves the group (CMSG_GROUP_DISBAND).</summary>
        void LeaveGroup();
        /// <summary>Leader: removes a member, online or not (CMSG_GROUP_UNINVITE_GUID).</summary>
        void UninviteFromGroup(ulong guid);
        /// <summary>Leader: makes an online member the leader (CMSG_GROUP_SET_LEADER).</summary>
        void PromoteToLeader(ulong guid);
        /// <summary>Leader: loot method 0..4 (PlayerState.GroupLootMethod) and threshold 2..4; master looter = you (CMSG_LOOT_METHOD).</summary>
        void SetLootMethod(int method, int threshold);
        /// <summary>Answers a PlayerState.LootRolls entry: 0 pass, 1 need, 2 greed, 3 disenchant (CMSG_LOOT_ROLL).</summary>
        void RollOnLoot(LootRollView roll, int rollType);
        /// <summary>The name the server gave a speaker guid (players by name query), null = not known yet.</summary>
        string ChatSender(ulong guid);

        // ==================================================================================================================================
        // Iteration 11 ("Complete", PLAN11 s4 wave 0, w0play): every proposal's IWowSession CONTRACT merged here, append-only, FROZEN after
        // wave 0 (changes go to the integrator). Each block names its owner; the wave-0 stubs live in the owner's partial files.
        // ==================================================================================================================================

        // ---- iteration 11 (vrcast, proposal-vrcast s5): WoW's 144 action buttons - spells, items, macros (PlayerState.ActionButtons)
        /// <summary>Puts a packed action (WowActionSlots.Pack) on action button slot 0..143; 0 clears it. A spell must be known (or a
        /// summonable mount), an item any entry; macro / equipment-set values only as they came from the server (moving a button). Online:
        /// CMSG_SET_ACTION_BUTTON (MiscHandler.cpp:899-938); offline: saved with the character. Player.ActionButtons (and Player.ActionBar for
        /// 0..11) change at once; a refused value raises an Error event and changes nothing.</summary>
        void SetActionButton(int slot, int packed);

        /// <summary>WoW's UseAction for action button slot 0..143: a spell -> CastSpell(id, targetGuid); an item -> the first bag stack of that
        /// entry (UseItem), else the equipped item with that entry (online: CMSG_USE_ITEM bag 255 + its equipment slot; offline: an Info event);
        /// a macro / an equipment set -> an Info event (they run in the World of Warcraft client only). Empty: nothing.</summary>
        void UseActionButton(int slot, ulong targetGuid);

        // ---- iteration 11 (gap D1, w0play; implemented by npc in both sessions - uicore's withdrawn 6.2 text with npc's R10 chunk rule):
        // the cursor / drag-and-drop item moves. Positions are InvPos (bag 255 + slot, or a bag's slot).
        /// <summary>Moves the item at (fromBag, fromSlot) onto (toBag, toSlot): into an empty slot, swapping with what is there, merging a
        /// stack of the same item, equipping onto an equipment slot (bag 255, slot 0-18) or unequipping from one. Online: both positions in
        /// bag 255 -> CMSG_SWAP_INV_ITEM (u8 dst, u8 src), else CMSG_SWAP_ITEM (u8 dstBag, u8 dstSlot, u8 srcBag, u8 srcSlot) - Player::SwapItem
        /// decides and answers SMSG_INVENTORY_CHANGE_FAILURE (an Error event) when it refuses. Offline: the simulation does the same for the
        /// backpack and the equipment.</summary>
        void MoveItem(int fromBag, int fromSlot, int toBag, int toSlot);
        /// <summary>Splits count items off a stack onto an empty slot or a stack of the same item. Online CMSG_SPLIT_ITEM (u8 srcBag, u8 srcSlot,
        /// u8 dstBag, u8 dstSlot, u32 count). No-op for count &lt;= 0 or count &gt;= the stack.</summary>
        void SplitItem(int fromBag, int fromSlot, int toBag, int toSlot, int count);
        /// <summary>Destroys the item - called after the player confirmed WoW's DELETE_ITEM popup. count &lt;= 0 or &gt;= the stack = the whole
        /// stack (online one CMSG_DESTROYITEM u8 bag, u8 slot, u8 0 = all, u8 0, u8 0, u8 0); a partial count goes in chunks of at most 255 and
        /// never removes more than asked (npc R10). Offline: removed from the record (quest items too - as on a server, the quest's count drops).</summary>
        void DestroyItem(int bag, int slot, int count);

        // ---- iteration 11 (area npc): gossip options, merchant, bags, bank, innkeeper, item text. Positions are InvPos (bag, slot) pairs;
        // MoveItem / SplitItem / DestroyItem are the D1 members above (area npc implements them in both sessions).
        /// <summary>Chooses CurrentDialog.Options[i] by its Index (online CMSG_GOSSIP_SELECT_OPTION). code = the text typed for a Coded
        /// option, else null. The UI asks first: Coded -> GOSSIP_ENTER_CODE; BoxText / BoxMoney -> GOSSIP_CONFIRM (UIParent.lua:913-934).</summary>
        void SelectGossipOption(int optionIndex, string code);

        /// <summary>The open merchant, or null. Closes on walking away (Interact's reach, 1 s grace), CloseMerchant, or another NPC window.</summary>
        MerchantView CurrentMerchant { get; }
        void CloseMerchant();
        /// <summary>Buys `purchases` x CurrentMerchant.Items[itemIndex].BuyCount items. (bag, slot) = where to put them (a drop on a bag slot),
        /// bag -1 = anywhere. False + an Error event when it cannot (not enough money, sold out...); the server may still refuse.</summary>
        bool BuyItem(int itemIndex, int purchases, int bag, int slot);
        /// <summary>Sells the item at (bag, slot) to the open merchant; count 0 = the whole stack.</summary>
        void SellItem(int bag, int slot, int count);
        /// <summary>Buys back Player.Buyback[index] (0..11) from the open merchant.</summary>
        void BuybackItem(int index);
        /// <summary>Repairs the item at (bag, slot) at the open armorer; bag -1 = Repair All.</summary>
        void RepairItem(int bag, int slot);

        /// <summary>Puts the item into any free slot of the container in bag slot dstBag (19..22, 67..73) or the backpack (255): a drop on a
        /// bag button of the bag bar (online CMSG_AUTOSTORE_BAG_ITEM).</summary>
        void StoreItemInBag(int srcBag, int srcSlot, int dstBag);
        /// <summary>Right-click on an item anywhere (bags, keyring, bank, an equipped "Use:" item): use / equip / open / read it - UseItem /
        /// EquipItem by position. Selling and banking are NOT done here (the UI calls SellItem / BankItem, like WoW's UseContainerItem rules).</summary>
        void UseItemAt(int bag, int slot);
        /// <summary>Equips the item at (bag, slot) into its own slot (CMSG_AUTOEQUIP_ITEM) - EquipItem by position.</summary>
        void EquipItemAt(int bag, int slot);

        /// <summary>The banker whose bank is open (0 = closed). Player.Bank holds the contents (online always known).</summary>
        ulong BankerGuid { get; }
        void CloseBank();
        /// <summary>Buys the next bank bag slot (Player.NextBankSlotCost; the UI asked CONFIRM_BUY_BANK_SLOT first).</summary>
        void BuyBankSlot();
        /// <summary>Bank open: the item at (bag, slot) goes to the bank if it is in the bags, to the bags if it is in the bank (right-click).</summary>
        void BankItem(int bag, int slot);

        /// <summary>Answers Player.PendingBinderGuid (CONFIRM_BINDER): accept = make this inn your home (online CMSG_BINDER_ACTIVATE).</summary>
        void ConfirmBinder(bool accept);

        /// <summary>The open ItemTextFrame, or null (UseItemAt on a readable item, Interact on a book / plaque).</summary>
        ItemTextView CurrentItemText { get; }
        /// <summary>+1 next page, -1 previous page.</summary>
        void ItemTextPage(int delta);
        void CloseItemText();

        // ---- iteration 11 (designer interact): trade, duels, inspect, raids, PvP flag, auction house, battlegrounds, dungeon finder, summons.
        // Never null; offline Available = false (OfflineInteractions answers every call with one Info line).
        IWowInteractions Interactions { get; }

        // ---- iteration 11 (owner social, proposal-social.md): guild, friends / ignore, Who, channels, text emotes, AFK / DND, mail,
        // guild charters. Never null (offline: every call answers one Info line). SendChat above also takes ChatKind.Officer, Raid,
        // RaidWarning, Battleground and Channel (whisperTo = the channel's name or its number "1".."10").
        /// <summary>The social side of the game (WowSocialTypes.cs).</summary>
        IWowSocial Social { get; }

        // ---- iteration 11 (designer spellfx): spell targeting and ground areas. Both sessions; views read Player.Targeting / AreaEffects.
        /// <summary>Ground targeting (Player.Targeting.Kind == Ground): casts the waiting spell at dest (WoW coordinates on the ground). Online
        /// CMSG_CAST_SPELL / CMSG_USE_ITEM with TARGET_FLAG_DEST_LOCATION 0x40. A place out of range is refused locally with WoW's text
        /// ("Out of range" GlobalStrings.lua:7147, "Target too close" :7193) and the targeting stays - when Targeting.RangeExact; otherwise it is
        /// sent and the server decides. Line of sight is always the server's.</summary>
        void ConfirmTargetingAt(WowPos dest);
        /// <summary>Item targeting (Player.Targeting.Kind == Item): the item clicked - bagIndex into Player.Bags, or equipSlot (EquipSlot); the other -1.</summary>
        void ConfirmTargetingItem(int bagIndex, int equipSlot);
        /// <summary>Item targeting: the item in the trade partner's "Will not be traded" slot (online TARGET_FLAG_TRADE_ITEM 0x1000 + slot 6,
        /// Spell.cpp:526-529; the server casts it when the trade is accepted). Offline: "Not available offline".</summary>
        void ConfirmTargetingTrade();
        /// <summary>Drops the waiting spell without casting (desktop right click on the world / Esc; VR a wrist menu released with nothing
        /// highlighted; 30 s). No CastFailed event (nothing was cast). No-op when nothing waits.</summary>
        void CancelTargeting();
        /// <summary>Casts a spell straight at an item (the trainer area's recipe window; ConfirmTargetingItem uses it). Online CMSG_CAST_SPELL with
        /// TARGET_FLAG_ITEM 0x10 + the item's packed guid; offline: weapon enchants on a weapon slot, else "Not available offline".
        /// (The member proposal-trainer.md s5.2 asks for - ONE definition for both, w0play.)</summary>
        void CastSpellOnItem(int spellId, int bagIndex, int equipSlot);
        /// <summary>Spell areas on the ground near the player (dynamic objects of every caster). Stable instances, updated in place.</summary>
        IReadOnlyList<AreaEffectView> AreaEffects { get; }

        // ---- iteration 11 (designer trainer): trainers, skills, professions. Offline answers what it can (proposal-trainer s7) and never throws.
        /// <summary>The open trainer window (after Interact on a trainer or its gossip "train" option: SMSG_TRAINER_LIST), or null.</summary>
        TrainerView CurrentTrainer { get; }
        /// <summary>Learns a service of CurrentTrainer (TrainerServiceView.SpellId). Online CMSG_TRAINER_BUY_SPELL; the answer refreshes the window
        /// (TrainerTrained) or shows WoW's error. The caller shows WoW's profession confirmation first when the service NeedsConfirm.</summary>
        void BuyTrainerSpell(int spellId);
        /// <summary>Closes the trainer window (client-only in 3.3.5: nothing is sent). Walking more than 10 yd away closes it too.</summary>
        void CloseTrainer();
        /// <summary>The open profession window, or null. Casting a profession spell (SPELL_EFFECT_TRADE_SKILL, e.g. Tailoring 3908, Smelting
        /// 2656) opens it instead of sending the cast.</summary>
        TradeSkillView CurrentTradeSkill { get; }
        /// <summary>Opens the profession window of a SkillLine id the character has (a Skills line, the spellbook's profession spell).</summary>
        void OpenTradeSkill(int skillId);
        void CloseTradeSkill();
        /// <summary>Makes a known recipe count times (Create = the count box, Create All = RecipeView.Craftable): one cast after the other,
        /// stopping on a failure, a move, CancelCast or when the reagents run out. An ItemTarget recipe is not queued (count 1, item picked first).</summary>
        void CraftRecipe(int recipeSpellId, int count);
        /// <summary>Unlearns a primary profession (SkillView.Abandonable) after WoW's UNLEARN_SKILL popup. Online CMSG_UNLEARN_SKILL.</summary>
        void UnlearnSkill(int skillId);

        // ---- iteration 11 (area talents): talents, glyphs, dual spec, reputation, titles (online: talents' Talents.cs + character's
        // Reputation.cs; offline: talents' OfflineSession.Talents.cs)
        /// <summary>Spends talent points in the ACTIVE spec (pet = the hunter pet's tree). Each pair = Talent.dbc id -> the rank COUNT to reach
        /// (1..5). One pair = WoW's click (CMSG_LEARN_TALENT / CMSG_PET_LEARN_TALENT); several = the preview's Learn
        /// (CMSG_LEARN_PREVIEW_TALENTS(_PET), sorted by tier). Refused locally (logged, no text: WoW greys the button then, and the server is silent).</summary>
        void LearnTalents(bool pet, IList<KeyValuePair<int, int>> talentRanks);
        /// <summary>Answers TalentsView.WipePending: true = MSG_TALENT_WIPE_CONFIRM (the trainer guid); false = nothing sent.</summary>
        void AnswerTalentWipe(bool accept);
        /// <summary>Dual spec: casts 63645 Activate Primary Spec (0) / 63644 Activate Secondary Spec (1), a 5 s cast.</summary>
        void ActivateSpec(int spec);
        /// <summary>Puts the WAITING glyph (TalentsView.GlyphPick - the item the player used from the bags) into socket 0..5 (CMSG_USE_ITEM
        /// with glyphIndex = socket, after the local checks of 3.5); socket -1 = cancel the pick. Addressing-free: the session re-finds the item by guid.</summary>
        void InscribeGlyph(int socket);
        /// <summary>Destroys the glyph in socket 0..5 of the active spec (CMSG_REMOVE_GLYPH).</summary>
        void RemoveGlyph(int socket);
        /// <summary>Reputation pane: at war (CMSG_SET_FACTION_ATWAR), inactive (CMSG_SET_FACTION_INACTIVE), watched (CMSG_SET_WATCHED_FACTION, -1 none).</summary>
        void SetFactionAtWar(int repListId, bool atWar);
        void SetFactionInactive(int repListId, bool inactive);
        void SetWatchedFaction(int repListId);
        /// <summary>Chooses a known title (CharTitles bit_index), 0 = none (CMSG_SET_TITLE).</summary>
        void SetTitle(int bitIndex);

        // ---- iteration 11 (designer quests): every quest doable, death / corpse
        /// <summary>Shares a quest of the log with the party (Share in the quest log, the tracker menu). Online CMSG_PUSHQUESTTOPARTY; the answers
        /// come back as Info lines ("Sharing quest with X...", "X has accepted your quest"). Offline: an Info line (ours).</summary>
        void ShareQuest(int questId);
        /// <summary>Answers PlayerState.PendingQuestConfirm (the QUEST_ACCEPT popup): yes = CMSG_QUEST_CONFIRM_ACCEPT, no = nothing is sent (as WoW).</summary>
        void AnswerQuestConfirm(bool accept);
        /// <summary>Uses a quest's item (QuestView.UseItemBagIndex; the tracker's item button): on the current target when its spell wants a unit,
        /// on the nearest quest object in range when it wants a gameobject, the ground circle when it wants a place, else like UseItem. Online
        /// CMSG_USE_ITEM with TARGET_FLAG_UNIT / TARGET_FLAG_GAMEOBJECT; offline UseItem.</summary>
        void UseQuestItem(int questId);
        /// <summary>Lying dead with Player.SelfResSpellId != 0 (Soulstone, Reincarnation): the DEATH popup's second button. Online CMSG_SELF_RES; offline nothing.</summary>
        void SelfResurrect();

        // ---- iteration 11 (area pets / vehicle): pets, totems, companions, the stable, vehicles, possess / charm. Offline: the imp / voidwalker
        // obey the pet calls and totems can be destroyed; the rest answers an Info event ("... is not available offline", ours).
        /// <summary>Uses Player.ControlBar slot 0..9: a command (Attack: targetGuid, 0 = the current target), a reaction, or a spell (targetGuid
        /// 0 = the current target, or self / none for helpful and untargeted spells; an Aimed spell fires along Vehicle.AimPitch). Online:
        /// CMSG_PET_ACTION (pet / charm), CMSG_PET_CAST_SPELL (possess, area spells), CMSG_CAST_SPELL (vehicle). A NeedsPlace slot: the UI opens
        /// spellfx's circle and calls UseControlSlotAt; called directly it is cast at the current target's feet, else the unit's own.</summary>
        void UseControlSlot(int slot, ulong targetGuid);
        /// <summary>The same for a slot whose spell NeedsPlace: dest = the ground point chosen with the circle (WoW coordinates).</summary>
        void UseControlSlotAt(int slot, WowPos dest);
        /// <summary>A pet command without the bar (keys, the radial, the pet frame menu). Attack: targetGuid 0 = the current target.</summary>
        void CommandPet(PetCommand command, ulong targetGuid);
        void SetPetReact(PetReact react);
        /// <summary>Right-click on a pet spell: autocast on / off (CMSG_PET_SPELL_AUTOCAST); the bar shows it at once.</summary>
        void SetPetAutocast(int spellId, bool on);
        /// <summary>Puts one of ControlBar.Spells on pet bar slot 3..6 (spellId 0 clears it) - CMSG_PET_SET_ACTION.</summary>
        void SetPetBarSlot(int slot, int spellId);
        /// <summary>Casts one of ControlBar.Spells (the pet spellbook's click; WoW's CastSpell(id, "pet")) - the same rules as a bar slot holding it.</summary>
        void UsePetSpell(int spellId, ulong targetGuid);
        /// <summary>Hunter pet with PetInfo.CanRename: CMSG_PET_RENAME; a bad name answers an Error event with WoW's ERR_NAME_* text.</summary>
        void RenamePet(string name);
        /// <summary>Hunter pet: abandons it for good (CMSG_PET_ABANDON). The UI asks first (ABANDON_PET).</summary>
        void AbandonPet();
        /// <summary>Hunter: casts Dismiss Pet (2641); any other pet, guardian or charmed unit: COMMAND_ABANDON (CMSG_PET_ACTION).</summary>
        void DismissPet();
        /// <summary>Destroys our totem of that element (0 fire .. 3 air): CMSG_TOTEM_DESTROYED; offline the totem goes.</summary>
        void DestroyTotem(int slot);
        /// <summary>Shaman: the totem bar's page 1..3 (local). Choosing a totem for a page = SetActionBarSlot(132 + (page - 1) * 4 + element (0 fire .. 3 air)
        /// , spellId) on vrcast's 144-slot bar (0-based slots 132..143) - MultiCastActionBarFrame.lua:311-324, SpellEffects.cpp:6530-6567.</summary>
        void SetTotemBarPage(int page);
        /// <summary>Dismisses the summoned companion (CMSG_DISMISS_CRITTER). Summoning = CastSpell(companionSpellId, 0).</summary>
        void DismissCompanion();
        /// <summary>Leaves the vehicle (a passenger seat: CMSG_REQUEST_VEHICLE_EXIT; a vehicle we control - driver / turret: CMSG_DISMISS_CONTROLLED_VEHICLE) or ends a
        /// possession (COMMAND_ABANDON on the possessed unit = StopCastingCharm, PetHandler.cpp:277-281). False when the seat does not allow it. (owner vehicle)</summary>
        bool LeaveVehicle();
        /// <summary>seat -1 = previous, -2 = next, else that seat index (CMSG_REQUEST_VEHICLE_PREV_SEAT / NEXT_SEAT / SWITCH_SEAT). (owner vehicle)</summary>
        void ChangeSeat(int seat);
        /// <summary>Turret / aimed vehicle: the wanted facing (WoW orientation) and aim pitch (radians). The session turns the vehicle at
        /// Vehicle.dbc TurnSpeed / PitchSpeed within PitchMin..PitchMax and sends MSG_MOVE_SET_FACING / MSG_MOVE_SET_PITCH with its guid. (owner vehicle)</summary>
        void SetVehicleAim(float orientation, float pitch);
        /// <summary>Stable (Player.Stable open): the current pet into a free slot (CMSG_STABLE_PET).</summary>
        void StablePet();
        /// <summary>That stabled pet becomes the current one (CMSG_STABLE_SWAP_PET with a current pet, else CMSG_UNSTABLE_PET).</summary>
        void UnstablePet(int petNumber);
        void BuyStableSlot();
        void CloseStable();

        // ---- iteration 11 (motion, lead contract): forced movement, transports
        /// <summary>The view finished PlayerState.ForcedMove id: a path's end was reached, a knockback landed at pos (WoW coordinates).
        /// Online: Knockback -> MSG_MOVE_FALL_LAND (fall time since the launch); Path / Fleeing / Confused -> nothing (the walker resumes
        /// from pos). Taxi ends with FinishTaxi, not here. Offline: clears it. Ignored when id is not the current move.</summary>
        void ForcedMoveDone(int id, WowPos pos);
        /// <summary>The floor under the avatar belongs to this transport (a deck, an elevator platform, a tram car), 0 = off it. Online:
        /// movement packets then carry MOVEMENTFLAG_ONTRANSPORT and the offsets in the transport's frame (the server makes us its passenger).
        /// Offline: logged only.</summary>
        void SetPlayerTransport(ulong transportGuid);

        // ---- iteration 11 (world, PLAN11): dungeons, raids, difficulty and instance locks. Online: OnlineSession.Instance.cs (area
        // triggers need no call - the online session reports them itself). Offline: one Info line "Dungeons and raids are on the online
        // realm" (ours), nothing else.
        /// <summary>Dungeon difficulty 0 normal / 1 heroic (UnitPopup DUNGEON_DIFFICULTY1/2; MSG_SET_DUNGEON_DIFFICULTY uint32). The answer
        /// sets PlayerState.DungeonDifficulty. At most one request per 1.1 s (AC kicks above 3 per second).</summary>
        void SetDungeonDifficulty(int difficulty);
        /// <summary>Raid difficulty 0 10 player, 1 25 player, 2 10 heroic, 3 25 heroic (MSG_SET_RAID_DIFFICULTY uint32) -> PlayerState.RaidDifficulty.</summary>
        void SetRaidDifficulty(int difficulty);
        /// <summary>"Reset all instances", after WoW's confirmation popup (CMSG_RESET_INSTANCES, empty). Solo or the group leader.</summary>
        void ResetInstances();
        /// <summary>Asks for the saved raid / heroic locks (CMSG_REQUEST_RAID_INFO) -> PlayerState.SavedInstances, SavedInstancesVersion + 1.</summary>
        void RequestRaidInfo();
        /// <summary>Answers PlayerState.PendingInstanceLock: true = Accept (saved to the instance), false = Leave Instance (CMSG_INSTANCE_LOCK_RESPONSE uint8).</summary>
        void AnswerInstanceLock(bool accept);
        /// <summary>Raid Info's Extend Raid Lock / Remove Raid Lock Extension (CMSG_SET_SAVED_INSTANCE_EXTEND uint32 map, uint32 difficulty, uint8 on), then RequestRaidInfo.</summary>
        void SetSavedInstanceExtend(int mapId, int difficulty, bool extend);
    }
}
