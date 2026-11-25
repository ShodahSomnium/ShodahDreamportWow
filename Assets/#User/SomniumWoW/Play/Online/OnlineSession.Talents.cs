// iteration 11 - owner talents (wave-0 stub by w0play, filled by the talents builder; proposal-talents.md 3.1-3.5, 3.11, 5.4)
// WoW's talents, glyphs, dual specialization and hunter pet talents on the AzerothCore realm (truth = AzerothCore master, copies in
// P/acsrc11/talents/):
// - S->C SMSG_TALENTS_INFO (Player::SendTalentsInfoData Player.cpp:14855-14864): uint8 isPet; player (BuildPlayerTalentsInfoData :14749-14781):
//   uint32 unspent, uint8 groupCount, uint8 activeGroup, per group { uint8 n, n x (uint32 Talent.dbc id, uint8 rank 0..4), uint8 6,
//   6 x uint16 GlyphProperties id }; pet (BuildPetTalentsInfoData :14783-14853): uint32 unspent, uint8 n, n x (uint32 id, uint8 rank 0..4).
// - C->S CMSG_LEARN_TALENT (SkillHandler.cpp:25-31): uint32 talentId, uint32 rank (0-based rank TO REACH); CMSG_LEARN_PREVIEW_TALENTS (:34-56):
//   uint32 count, count x (uint32 id, uint32 rank), learned one by one in order (max 150); the server answers SMSG_TALENTS_INFO and is
//   SILENT on a refusal (Player::LearnTalent :14283-14417) - so every request is checked here first with the same rules (TalentDb.CheckLearn).
// - Pets: CMSG_PET_LEARN_TALENT (PetHandler.cpp:1128-1138): uint64 pet, uint32 id, uint32 rank; CMSG_LEARN_PREVIEW_TALENTS_PET (:1140-1165):
//   uint64 pet, uint32 count, pairs (max 30); rules Player::LearnPetTalent :14419-14552.
// - Trainer wipe: S->C MSG_TALENT_WIPE_CONFIRM (Player::SendTalentWipeConfirm :9118-9125): uint64 trainer, uint32 cost; C->S the same opcode with
//   uint64 trainer (SkillHandler.cpp:58-88; the trainer must be in interaction range, Player.cpp:2157-2159); S->C {0, 0} = nothing spent.
// - Dual spec: the hidden spells 63645 "Activate Primary Spec" / 63644 "Activate Secondary Spec" (effect 162 TALENT_SPEC_SELECT bp 1 / 2,
//   SpellEffects.cpp:6470-6482), cast like any spell (5 s, SpellCastTimes 6).
// - Glyphs: CMSG_USE_ITEM with glyphIndex = the socket (SpellHandler.cpp:58-79); EffectApplyGlyph (SpellEffects.cpp:4514-4599) refuses a locked
//   or wrong-type socket AFTER the item is taken (Spell::handle_immediate Spell.cpp:4218-4280 + TakeCastItem :5438-5478; the glyph items are
//   charges -1, AC item_template) - so the item would be LOST: InscribeGlyph checks the socket here first and never sends a refused glyph.
//   CMSG_REMOVE_GLYPH (CharacterHandler.cpp:1599-1634): uint32 socket.
// - Spell modifiers (P4): SMSG_SET_FLAT_SPELL_MODIFIER / SMSG_SET_PCT_SPELL_MODIFIER (Player::AddSpellMod Player.cpp:10095-10125): uint8 bit
//   (0..95 of SpellFamilyFlags), uint8 SpellModOp, int32 value = the SUM of all mods of that type, op and bit.
// The self fields (PLAYER_CHARACTER_POINTS1, PLAYER_FIELD_GLYPH_SLOTS_1, PLAYER_FIELD_GLYPHS_1, PLAYER_GLYPHS_ENABLED) are read in TickTalents
// (the RefreshSelfPanes partial of Hooks11 is left to the character builder: titles, currencies, stats).
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const int TalSpellActivatePrimary = 63645, TalSpellActivateSecondary = 63644;   // Spell.dbc (measured, proposal-talents.md 3.3)
        const int TalMaxPreview = 150, TalMaxPetPreview = 30;                         // SkillHandler.cpp:44 / PetHandler.cpp:1153
        const int TalInscriptionLevel = 15;                                             // SHOW_INSCRIPTION_LEVEL (Constants.lua:147)
        const int TalModOps = 32, TalModBits = 96;                                      // SpellModOp 0..31 (AC SpellDefines.h:82-115), 96 family bits

        TalentDb _talDb;
        bool _talDbTried;
        ulong _talWipeTrainer;                 // MSG_TALENT_WIPE_CONFIRM's trainer
        ulong _glyphPickGuid;                  // the glyph item waiting for its socket (by guid: the bags can move)
        int _glyphPickSpell;
        int _talFieldPoints = -1;
        readonly int[] _talFieldGlyphs = { -1, -1, -1, -1, -1, -1 };
        int _talPetFamily = -1;
        string _talLastLog, _talLastPetLog;
        readonly List<KeyValuePair<int, int>> _talAsked = new List<KeyValuePair<int, int>>();   // the last request (talent -> count), compared with the answer
        bool _talAskedPet;
        readonly int[] _talModFlat = new int[TalModOps * TalModBits], _talModPct = new int[TalModOps * TalModBits];
        int _talModPackets;
        bool _talModLogged;

        TalentDb TalDb
        {
            get
            {
                if (_talDb == null && !_talDbTried) { _talDbTried = true; _talDb = TalentDb.Get(_files); }
                return _talDb;
            }
        }

        partial void InitTalents()
        {
            _world.On(Op.SMSG_TALENTS_INFO, TalOnInfo, this);
            _world.On(Op.MSG_TALENT_WIPE_CONFIRM, TalOnWipeConfirm, this);
            _world.On(Op.SMSG_SET_FLAT_SPELL_MODIFIER, p => TalOnSpellMod(p, false), this);
            _world.On(Op.SMSG_SET_PCT_SPELL_MODIFIER, p => TalOnSpellMod(p, true), this);
        }

        partial void TickTalents(float dt)
        {
            var T = _player.Talents;
            TalReadSelfFields(T);
            TalTickPet(T);
            if (T.WipePending)
            {
                // WoW hides CONFIRM_TALENT_WIPE when CheckTalentMasterDist() fails (StaticPopup.lua:2505-2509); ours = the server's own refusal rule
                // GetNPCIfCanInteractWith (Player.cpp:2112-2160: INTERACTION_DISTANCE + both combat reaches) through QInReach.
                OnlineObject o;
                bool near = _objects.TryGetValue(_talWipeTrainer, out o) && o.State != null && QInReach(o, o.State, QInteractionDistance);
                if (!near)
                {
                    T.WipePending = false; T.WipeCost = 0; _talWipeTrainer = 0; T.Counter++;
                    WowLog.Info("Online talents: the talent wipe question closed - the trainer is out of reach (WoW's CheckTalentMasterDist)");
                }
            }
            if (T.GlyphPick && TalBagPos(_glyphPickGuid) < 0)
            {
                WowLog.Info("Online talents: the waiting glyph left the bags - the glyph pick is cancelled");
                TalCancelGlyphPick(T);
            }
        }

        // ------------------------------------------------------------------ server packets

        /// <summary>SMSG_TALENTS_INFO (see the header).</summary>
        void TalOnInfo(WowPacket p)
        {
            var T = _player.Talents;
            int isPet = p.U8();
            if (isPet != 0) { TalOnPetInfo(p, T); return; }
            int unspent = (int)p.U32();
            int groups = p.U8();
            int active = p.U8();
            int read = Math.Min(groups, 2);                                   // MAX_TALENT_SPECS (AC SharedDefines.h)
            for (int g = 0; g < groups; g++)
            {
                TalentSpecView spec = g < 2 ? T.Specs[g] : null;
                if (spec != null) { spec.Ranks.Clear(); Array.Clear(spec.Glyphs, 0, spec.Glyphs.Length); }
                int n = p.U8();
                for (int i = 0; i < n; i++)
                {
                    int id = (int)p.U32();
                    int rank = p.U8();
                    if (spec != null) spec.Ranks[id] = rank + 1;                   // 0-based rank -> COUNT
                }
                int gc = p.U8();
                for (int i = 0; i < gc; i++)
                {
                    int glyph = p.U16();
                    if (spec != null && i < spec.Glyphs.Length) spec.Glyphs[i] = glyph;
                }
            }
            for (int g = read; g < 2; g++) { T.Specs[g].Ranks.Clear(); Array.Clear(T.Specs[g].Glyphs, 0, T.Specs[g].Glyphs.Length); }
            T.FreePoints = unspent;
            T.SpecCount = Math.Max(1, read);
            T.ActiveSpec = active >= 0 && active < T.SpecCount ? active : 0;
            T.Known = true;
            // The packet is newer than the self fields (the server sets the fields, sends this packet, and the field update follows on its next
            // world tick): remember the fields as they are now, so only a LATER change of a field (a level-up, a glyph field) overrides the packet.
            TalSnapshotFields();
            T.Counter++;
            TalLog(T);
            if (_talAsked.Count > 0 && !_talAskedPet)
            {
                var spec = T.Specs[T.ActiveSpec];
                foreach (var kv in _talAsked)
                {
                    int has = spec.Rank(kv.Key);
                    if (has != kv.Value) WowLog.Info("Online talents: asked " + kv.Key + " rank " + kv.Value + ", the server has " + has);
                }
                _talAsked.Clear();
            }
        }

        void TalOnPetInfo(WowPacket p, TalentsView T)
        {
            int unspent = (int)p.U32();
            int n = p.U8();
            T.Pet.Ranks.Clear();
            for (int i = 0; i < n && p.Remaining >= 5; i++)
            {
                int id = (int)p.U32();
                int rank = p.U8();
                T.Pet.Ranks[id] = rank + 1;
            }
            T.PetFreePoints = unspent;
            T.PetKnown = true;
            _talPetFamily = -1;                                                  // re-resolve the tree from the pet's family now
            TalTickPet(T);
            T.Counter++;
            string log = "Online pet talents: " + unspent + " points free, " + n + " talents, tree " + T.PetTalentTabId + ", family category " + T.PetCategory;
            if (log != _talLastPetLog) { _talLastPetLog = log; WowLog.Info(log); }
            if (_talAsked.Count > 0 && _talAskedPet)
            {
                foreach (var kv in _talAsked)
                {
                    int has = T.Pet.Rank(kv.Key);
                    if (has != kv.Value) WowLog.Info("Online pet talents: asked " + kv.Key + " rank " + kv.Value + ", the server has " + has);
                }
                _talAsked.Clear();
            }
        }

        /// <summary>MSG_TALENT_WIPE_CONFIRM from a class trainer's "I wish to unlearn my talents" (PlayerGossip.cpp:350-353) or the refusal {0, 0}.</summary>
        void TalOnWipeConfirm(WowPacket p)
        {
            var T = _player.Talents;
            ulong guid = p.U64();
            int cost = (int)p.U32();
            if (guid == 0)
            {
                T.WipePending = false; T.WipeCost = 0; _talWipeTrainer = 0; T.Counter++;
                Error("You have not spent any talent points.");                 // ERR_TALENT_WIPE_ERROR (GlobalStrings.lua:3509)
                WowLog.Info("Online talents: MSG_TALENT_WIPE_CONFIRM {0, 0} - nothing to unlearn");
                return;
            }
            _talWipeTrainer = guid;
            T.WipePending = true;
            T.WipeCost = cost;
            T.OpenRequest++;                                                     // the Talents window opens on the active spec (UIParent.lua:880-891)
            T.Counter++;
            WowLog.Info("Online talents: trainer " + guid.ToString("X") + " asks to unlearn all talents for " + cost + " copper (MSG_TALENT_WIPE_CONFIRM)");
        }

        /// <summary>SMSG_SET_FLAT_SPELL_MODIFIER / SMSG_SET_PCT_SPELL_MODIFIER: uint8 bit, uint8 op, int32 sum (Player.cpp:10095-10125).</summary>
        void TalOnSpellMod(WowPacket p, bool pct)
        {
            int bit = p.U8();
            int op = p.U8();
            int value = p.I32();
            _talModPackets++;
            if (bit >= TalModBits || op >= TalModOps) return;
            (pct ? _talModPct : _talModFlat)[op * TalModBits + bit] = value;
            if (!_talModLogged && _talModPackets >= 1)
            {
                _talModLogged = true;
                WowLog.Info("Online talents: the server sends spell modifiers (SMSG_SET_" + (pct ? "PCT" : "FLAT") + "_SPELL_MODIFIER bit " + bit + " op " + op + " = " + value
                            + "); they apply to the class's own spell family (ours: ChrClasses SpellClassSet), summed over a spell's family bits - logged once");
            }
        }

        // ------------------------------------------------------------------ self fields (between talent packets)

        void TalSnapshotFields()
        {
            _talFieldPoints = _self != null ? (int)_self.U32(UF.PLAYER_CHARACTER_POINTS1) : -1;
            for (int i = 0; i < TalentDb.GlyphSockets; i++) _talFieldGlyphs[i] = _self != null ? (int)_self.U32(UF.PLAYER_FIELD_GLYPHS_1 + i) : -1;
        }

        void TalReadSelfFields(TalentsView T)
        {
            if (_self == null) return;
            bool changed = false;
            int pts = (int)_self.U32(UF.PLAYER_CHARACTER_POINTS1);               // SetFreeTalentPoints (Player.cpp:3968-3972)
            if (pts != _talFieldPoints)
            {
                _talFieldPoints = pts;
                if (T.Known && pts != T.FreePoints) { T.FreePoints = pts; changed = true; }
            }
            var spec = T.Specs[T.ActiveSpec >= 0 && T.ActiveSpec < 2 ? T.ActiveSpec : 0];
            for (int i = 0; i < TalentDb.GlyphSockets; i++)
            {
                int slot = (int)_self.U32(UF.PLAYER_FIELD_GLYPH_SLOTS_1 + i);    // InitGlyphsForLevel (Player.cpp:13620-13643)
                if (slot != T.GlyphSlotIds[i]) { T.GlyphSlotIds[i] = slot; changed = true; }
                int glyph = (int)_self.U32(UF.PLAYER_FIELD_GLYPHS_1 + i);         // the ACTIVE spec's glyphs (Player.h:1785-1792)
                if (glyph != _talFieldGlyphs[i])
                {
                    _talFieldGlyphs[i] = glyph;
                    if (T.Known && glyph != spec.Glyphs[i]) { spec.Glyphs[i] = glyph; changed = true; }
                }
            }
            int mask = (int)_self.U32(UF.PLAYER_GLYPHS_ENABLED);
            if (mask != T.GlyphsEnabledMask) { T.GlyphsEnabledMask = mask; changed = true; }
            if (changed) T.Counter++;
        }

        /// <summary>The pet's tree from its family (area pets' PetInfo.Family): CreatureFamily.dbc fields 8 (petTalentType) and 9 (category).</summary>
        void TalTickPet(TalentsView T)
        {
            int family = _player.PetInfo != null ? _player.PetInfo.Family : 0;
            if (family == _talPetFamily) return;
            _talPetFamily = family;
            var db = TalDb;
            int type = db != null && db.Available ? db.PetTalentType(family) : -1;
            var tab = db != null && db.Available ? db.PetTab(type) : null;
            int tabId = tab != null ? tab.Id : 0;
            int cat = db != null && db.Available && family > 0 ? db.PetCategory(family) : -1;
            if (tabId != T.PetTalentTabId || cat != T.PetCategory)
            {
                T.PetTalentTabId = tabId;
                T.PetCategory = cat;
                if (family == 0) { T.PetKnown = false; T.Pet.Ranks.Clear(); T.PetFreePoints = 0; }
                T.Counter++;
                if (family != 0) WowLog.Info("Online pet talents: pet family " + family + " -> petTalentType " + type + " -> tree " + tabId + ", category " + cat);
            }
        }

        void TalLog(TalentsView T)
        {
            var db = TalDb;
            var sb = new StringBuilder(160);
            var spec = T.Specs[T.ActiveSpec];
            sb.Append("Online talents: ").Append(T.FreePoints).Append(" points free, spec ").Append(T.ActiveSpec + 1).Append('/').Append(T.SpecCount)
              .Append(", ").Append(spec.Ranks.Count).Append(" talents");
            if (db != null && db.Available)
            {
                var tabs = db.ClassTabs(_player.Class);
                sb.Append(" (");
                for (int i = 0; i < tabs.Count; i++) { if (i > 0) sb.Append('/'); sb.Append(db.PointsInTab(spec, tabs[i].Id)); }
                sb.Append(')');
            }
            sb.Append(", glyphs ");
            for (int i = 0; i < spec.Glyphs.Length; i++) { if (i > 0) sb.Append(','); sb.Append(spec.Glyphs[i]); }
            string s = sb.ToString();
            if (s != _talLastLog) { _talLastLog = s; WowLog.Info(s); }
        }

        // ------------------------------------------------------------------ IWowSession

        /// <summary>Spends talent points in the ACTIVE spec (pet = the hunter pet's tree): one pair = WoW's click (CMSG_LEARN_TALENT /
        /// CMSG_PET_LEARN_TALENT), several = the preview's Learn (CMSG_LEARN_PREVIEW_TALENTS(_PET), by tier). Checked locally first with the
        /// server's rules (it is silent on a refusal); a refused pair is logged and not sent.</summary>
        public void LearnTalents(bool pet, IList<KeyValuePair<int, int>> talentRanks)
        {
            if (_world == null || talentRanks == null || talentRanks.Count == 0) return;
            var T = _player.Talents;
            var db = TalDb;
            if (db == null || !db.Available) { WowLog.Warn("Online talents: no talent data (Talent.dbc) - nothing sent"); return; }
            if (!pet && !T.Known) { WowLog.Info("Online talents: no SMSG_TALENTS_INFO yet - nothing sent"); return; }
            int petType = -1;
            if (pet)
            {
                int family = _player.PetInfo != null ? _player.PetInfo.Family : 0;
                petType = db.PetTalentType(family);
                if (_player.PetGuid == 0 || petType < 0 || !T.PetKnown) { WowLog.Info("Online pet talents: no hunter pet with talents (family " + family + ") - nothing sent"); return; }
            }
            // simulate the server's one-by-one learning on a copy of the spec (Player::LearnTalent / LearnPetTalent)
            var sim = new TalentSpecView();
            var src = pet ? T.Pet : T.Specs[T.ActiveSpec];
            foreach (var kv in src.Ranks) sim.Ranks[kv.Key] = kv.Value;
            int free = pet ? T.PetFreePoints : T.FreePoints;
            var send = new List<KeyValuePair<int, int>>();
            foreach (var kv in db.SendOrder(talentRanks))
            {
                string why = db.CheckLearn(_player.Class, sim, free, kv.Key, kv.Value, pet, petType);
                if (why != null) { WowLog.Info("Online " + (pet ? "pet " : "") + "talents: talent " + kv.Key + " rank " + kv.Value + " refused here (" + why + ") - not sent"); continue; }
                free -= kv.Value - sim.Rank(kv.Key);
                sim.Ranks[kv.Key] = kv.Value;
                send.Add(kv);
            }
            foreach (var kv in talentRanks) if (db.Find(kv.Key) == null) WowLog.Info("Online talents: unknown talent " + kv.Key + " - not sent");
            if (send.Count == 0) return;
            int max = pet ? TalMaxPetPreview : TalMaxPreview;
            if (send.Count > max) send.RemoveRange(max, send.Count - max);
            var w = new PacketWriter();
            ushort op;
            if (send.Count == 1)
            {
                op = pet ? Op.CMSG_PET_LEARN_TALENT : Op.CMSG_LEARN_TALENT;
                if (pet) w.U64(_player.PetGuid);
                w.U32((uint)send[0].Key).U32((uint)(send[0].Value - 1));       // 0-based rank to reach
            }
            else
            {
                op = pet ? Op.CMSG_LEARN_PREVIEW_TALENTS_PET : Op.CMSG_LEARN_PREVIEW_TALENTS;
                if (pet) w.U64(_player.PetGuid);
                w.U32((uint)send.Count);
                foreach (var kv in send) w.U32((uint)kv.Key).U32((uint)(kv.Value - 1));
            }
            _world.Send(op, w);
            _talAsked.Clear();
            _talAsked.AddRange(send);
            _talAskedPet = pet;
            var sb = new StringBuilder();
            foreach (var kv in send) { if (sb.Length > 0) sb.Append(", "); sb.Append(kv.Key).Append(" -> ").Append(kv.Value); }
            WowLog.Info("Online " + (pet ? "pet " : "") + "talents: " + (send.Count == 1 ? (pet ? "CMSG_PET_LEARN_TALENT " : "CMSG_LEARN_TALENT ") : (pet ? "CMSG_LEARN_PREVIEW_TALENTS_PET " : "CMSG_LEARN_PREVIEW_TALENTS "))
                        + sb + " (" + ((pet ? T.PetFreePoints : T.FreePoints) - free) + " points)");
        }

        /// <summary>Answers TalentsView.WipePending: true = MSG_TALENT_WIPE_CONFIRM (the trainer guid); false = nothing sent.</summary>
        public void AnswerTalentWipe(bool accept)
        {
            var T = _player.Talents;
            if (!T.WipePending) return;
            if (accept && _world != null)
            {
                _world.Send(Op.MSG_TALENT_WIPE_CONFIRM, new PacketWriter().U64(_talWipeTrainer));
                WowLog.Info("Online talents: unlearn all talents accepted (MSG_TALENT_WIPE_CONFIRM to " + _talWipeTrainer.ToString("X") + ", " + T.WipeCost + " copper)");
            }
            else WowLog.Info("Online talents: unlearn all talents declined - nothing sent");
            T.WipePending = false; T.WipeCost = 0; _talWipeTrainer = 0; T.Counter++;
        }

        /// <summary>Dual spec: casts 63645 Activate Primary Spec (0) / 63644 Activate Secondary Spec (1), a 5 s cast.</summary>
        public void ActivateSpec(int spec)
        {
            var T = _player.Talents;
            if (spec < 0 || spec > 1) return;
            if (T.SpecCount < 2) { WowLog.Info("Online talents: no second specialization - nothing cast"); return; }
            if (spec == T.ActiveSpec) { WowLog.Info("Online talents: spec " + (spec + 1) + " is already active"); return; }
            WowLog.Info("Online talents: activate spec " + (spec + 1) + " (spell " + (spec == 0 ? TalSpellActivatePrimary : TalSpellActivateSecondary) + ")");
            CastSpell(spec == 0 ? TalSpellActivatePrimary : TalSpellActivateSecondary, 0);
        }

        /// <summary>Puts the WAITING glyph into socket 0..5 (CMSG_USE_ITEM with glyphIndex = socket) after the server's checks done here (a refused
        /// glyph would be destroyed by the server); socket -1 cancels the pick.</summary>
        public void InscribeGlyph(int socket)
        {
            var T = _player.Talents;
            if (socket < 0) { if (T.GlyphPick) { WowLog.Info("Online talents: glyph pick cancelled"); TalCancelGlyphPick(T); } return; }
            if (!T.GlyphPick || _world == null) { WowLog.Info("Online talents: InscribeGlyph(" + socket + ") without a waiting glyph"); return; }
            if (socket >= TalentDb.GlyphSockets) return;
            int bagIndex = TalBagPos(_glyphPickGuid);
            if (bagIndex < 0)
            {
                TalCancelGlyphPick(T);
                Error("The item was not found.");                               // ERR_ITEM_NOT_FOUND (GlobalStrings.lua:3169)
                return;
            }
            var db = TalDb;
            var g = db != null ? db.Glyph(T.GlyphPickGlyphId) : null;
            if (_player.Level < TalentDb.GlyphSocketLevel[socket])
            {
                Error(SpellCastResults.Text(177));                               // GLYPH_SOCKET_LOCKED "You cannot inscribe a Glyph there yet." (GS 7062)
                WowLog.Info("Online talents: glyph socket " + socket + " opens at level " + TalentDb.GlyphSocketLevel[socket] + " - not sent (the server would destroy the glyph)");
                return;
            }
            int slotType = db != null ? db.GlyphSlotType(T.GlyphSlotIds[socket]) : -1;
            if (g != null && slotType >= 0 && slotType != g.Type)
            {
                Error(SpellCastResults.Text(175));                               // INVALID_GLYPH "That Glyph cannot be inscribed there." (GS 7068)
                WowLog.Info("Online talents: glyph " + g.Id + " (type " + g.Type + ") does not fit socket " + socket + " (GlyphSlot " + T.GlyphSlotIds[socket] + " type " + slotType + ") - not sent");
                return;
            }
            var spec = T.Specs[T.ActiveSpec];
            for (int i = 0; i < spec.Glyphs.Length; i++)
                if (spec.Glyphs[i] == T.GlyphPickGlyphId && T.GlyphPickGlyphId != 0)
                {
                    Error(SpellCastResults.Text(176));                           // UNIQUE_GLYPH "That Glyph is already inscribed in your spellbook." (GS 7200)
                    WowLog.Info("Online talents: glyph " + T.GlyphPickGlyphId + " is already in socket " + i + " - not sent");
                    return;
                }
            var bp = _bagPos[bagIndex];
            _itemCastCount++;
            if (_itemCastCount == 0) _itemCastCount = 1;                       // 0 marks triggered casts in AC
            // CMSG_USE_ITEM (SpellHandler.cpp:58-79): bag, slot, castCount, spell, item guid, glyphIndex, castFlags 0, SpellCastTargets mask 0
            _world.Send(Op.CMSG_USE_ITEM, new PacketWriter().U8(bp.Bag).U8(bp.Slot).U8(_itemCastCount).U32((uint)_glyphPickSpell).U64(bp.Guid)
                                                         .U32((uint)socket).U8(0).U32(0));
            WowLog.Info("Online talents: inscribe glyph " + T.GlyphPickGlyphId + " (item spell " + _glyphPickSpell + ", bag " + bp.Bag + " slot " + bp.Slot
                        + ") into socket " + socket + " (CMSG_USE_ITEM glyphIndex " + socket + ", castCount " + _itemCastCount + ")");
            T.GlyphPick = false; T.GlyphPickGlyphId = 0; _glyphPickGuid = 0; _glyphPickSpell = 0; T.Counter++;
        }

        /// <summary>Destroys the glyph in socket 0..5 of the active spec (CMSG_REMOVE_GLYPH).</summary>
        public void RemoveGlyph(int socket)
        {
            var T = _player.Talents;
            if (_world == null || socket < 0 || socket >= TalentDb.GlyphSockets) return;
            int glyph = T.Specs[T.ActiveSpec].Glyphs[socket];
            if (glyph == 0) { WowLog.Info("Online talents: RemoveGlyph(" + socket + ") - the socket is empty, nothing sent"); return; }
            _world.Send(Op.CMSG_REMOVE_GLYPH, new PacketWriter().U32((uint)socket));
            WowLog.Info("Online talents: remove glyph " + glyph + " from socket " + socket + " (CMSG_REMOVE_GLYPH)");
        }

        /// <summary>A talent rank spell the spellbook must not list (TalentDb.IsHiddenTalentSpell; spellfx's RebuildSpells hook).</summary>
        bool TalentHidden(int spellId)
        {
            var db = TalDb;
            return db != null && db.Available && db.IsHiddenTalentSpell(spellId);
        }

        /// <summary>A glyph item used from the bags waits for its socket (TalentsView.GlyphPick); true = handled (nothing sent). npc's UseItem hook.</summary>
        bool TryStartGlyph(BagPos bp, int spell)
        {
            var db = TalDb;
            int glyph = db != null && db.Available ? db.GlyphOfItemSpell(spell) : 0;
            if (glyph <= 0) return false;
            var T = _player.Talents;
            if (_player.Level < TalInscriptionLevel)
            {
                // WoW does not open the GlyphFrame below SHOW_INSCRIPTION_LEVEL (UIParent.lua:381-389); ours: the server's socket text
                Error(SpellCastResults.Text(177));                               // "You cannot inscribe a Glyph there yet." (GS 7062)
                WowLog.Info("Online talents: glyph item used at level " + _player.Level + " (sockets open at " + TalInscriptionLevel + ") - nothing sent");
                return true;
            }
            _glyphPickGuid = bp.Guid;
            _glyphPickSpell = spell;
            T.GlyphPick = true;
            T.GlyphPickGlyphId = glyph;
            T.OpenRequest++;                                                     // the Talents window opens on its Glyphs tab
            T.Counter++;
            var g = db.Glyph(glyph);
            WowLog.Info("Online talents: glyph " + glyph + (g != null ? (g.Type == 0 ? " (major)" : " (minor)") : "") + " from bag " + bp.Bag + " slot " + bp.Slot
                        + " waits for a socket (WoW's USE_GLYPH -> the Glyphs tab)");
            return true;
        }

        void TalCancelGlyphPick(TalentsView T)
        {
            T.GlyphPick = false; T.GlyphPickGlyphId = 0; _glyphPickGuid = 0; _glyphPickSpell = 0; T.Counter++;
        }

        int TalBagPos(ulong guid)
        {
            if (guid == 0) return -1;
            for (int i = 0; i < _bagPos.Count; i++) if (_bagPos[i].Guid == guid) return i;
            return -1;
        }

        // ------------------------------------------------------------------ spell modifiers (P4)

        /// <summary>Talent / glyph modifiers of one spell for a SpellModOp (0 DAMAGE .. 5 RANGE, 6 RADIUS, 10 CASTING_TIME, 11 COOLDOWN, 14 COST ...;
        /// AC SpellDefines.h:82-115): flat = the flat sum, pct = the percent sum (Player::ApplySpellMod: base * (1 + pct / 100) + flat). Only the
        /// class's own SpellFamilyName (the packet carries no family; ours) and summed over the spell's family bits (the packet carries per-bit
        /// sums; an upper bound when one mod covers several bits of the same spell). True when a modifier applies.</summary>
        bool SpellModValue(int spellId, int op, out float flat, out int pct)
        {
            flat = 0f; pct = 0;
            if (op < 0 || op >= TalModOps || _talModPackets == 0) return false;
            var s = Db.Find(spellId);
            int family = TalentDb.SpellFamilyOf(_player.Class);
            if (s == null || family == 0 || s.SpellFamilyName != family || s.SpellFamilyFlags == null) return false;
            int f = 0;
            for (int bit = 0; bit < TalModBits; bit++)
            {
                int w = bit >> 5;
                if (w >= s.SpellFamilyFlags.Length || (s.SpellFamilyFlags[w] & (1u << (bit & 31))) == 0) continue;
                f += _talModFlat[op * TalModBits + bit];
                pct += _talModPct[op * TalModBits + bit];
            }
            flat = f;
            return f != 0 || pct != 0;
        }

        /// <summary>spellfx's hook (OnlineSession.Targeting.cs SpRangeMods, _hooks_by_file "pets UiPetBar / talents"): SPELLMOD_RANGE 5 and
        /// SPELLMOD_RADIUS 6 (AC SpellDefines.h:88-89) of the spell from the server's modifier table. Known once the first SMSG_TALENTS_INFO
        /// arrived (the modifiers of the learnt talents are sent with the login packets; none sent = no modifier).</summary>
        partial void SpRangeModsFromTalents(int spellId, ref bool known, ref int rangePct, ref float rangeFlat, ref int radiusPct, ref float radiusFlat)
        {
            if (!_player.Talents.Known) return;
            known = true;
            float f; int p;
            if (SpellModValue(spellId, 5, out f, out p)) { rangeFlat += f; rangePct += p; }
            if (SpellModValue(spellId, 6, out f, out p)) { radiusFlat += f; radiusPct += p; }
        }
    }
}
