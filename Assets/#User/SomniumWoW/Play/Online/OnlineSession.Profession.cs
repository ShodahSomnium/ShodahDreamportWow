// iteration 11 - owner trainer (proposal-trainer.md 3.4-3.5, 5.3, 5.4 "Profession"): skills, the profession window, "Create N", skinning and
// fishing online. The hooks other owners apply call into this file: spellfx S1-S4 (Spells.cs), quests L1 / Q2 (Loot.cs / Quests.cs), w0play O4 / O5.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Professions;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const uint PfUnitFlagSkinnable = 0x04000000;   // UNIT_FLAG_SKINNABLE (AC UnitDefines.h:283)
        const int PfMaxCount = 999;                    // WoW's count box holds 3 digits (Blizzard_TradeSkillUI.xml:775-964)
        const double PfNextGap = 0.1;                  // ours: the next "Create N" cast 0.1 s after the previous SMSG_SPELL_GO
        const double PfGoWatchdog = 2.0;               // ours (R4): no SMSG_SPELL_GO 2 s after the cast bar's end stops the queue
        const double PfRebuildGap = 0.25;              // ours: the profession window is rebuilt at most 4 times a second

        readonly int[] _pfIdx = new int[PlayerMaxSkills], _pfVal = new int[PlayerMaxSkills], _pfBonus = new int[PlayerMaxSkills];
        readonly Dictionary<int, int> _pfRawValue = new Dictionary<int, int>(), _pfRawMax = new Dictionary<int, int>();
        int _pfSkillSig;
        bool _pfHaveSkills, _pfSkillsDirty;
        TradeSkillView _trade;
        int _tradeSkill, _pfTradeSig;
        bool _pfTradeDirty;
        double _pfTradeNextAt, _pfCooldownTickAt;
        int _pfQueueId, _pfQueueLeft, _pfQueueTotal;
        double _pfNextAt = -1, _pfGoDeadline = -1;
        bool _pfWaitGo;
        bool _pfSkinPending;
        double _pfSkinCheckAt;
        int _pfLogs;
        PfFacts _pfFacts;
        System.Reflection.FieldInfo _pfTotemField;
        bool _pfTotemFieldTried;

        /// <summary>The character as the profession window sees it (live).</summary>
        sealed class PfFacts : ITradeFacts
        {
            readonly OnlineSession _s;
            int _abId;
            AbilityView _ab;
            public PfFacts(OnlineSession s) { _s = s; }
            public IEnumerable<int> KnownSpells { get { return _s._known; } }
            public int Race { get { return _s._player.Race; } }
            public WowClass Class { get { return _s._player.Class; } }
            public int SkillValue(int skillLine) { return _s.PfSkillValue(skillLine); }
            public int SkillMax(int skillLine) { return _s.PfSkillMax(skillLine); }
            public int Have(int itemEntry) { return _s.PfHave(itemEntry); }
            public bool HasItem(int itemEntry) { return _s.PfHave(itemEntry) > 0 || _s.PfEquipped(itemEntry); }
            public bool HasTotemCategory(int category) { return _s.PfHasTotemCategory(category); }
            public bool FocusNear(int focusId) { return _s.PfFocusNear(focusId); }
            public float Cooldown(int spellId) { float c = _s.SpCooldownLeft(spellId); return c == float.MaxValue ? 0f : c; }
            public string ItemName(int itemEntry) { var v = _s.ItemInfoView(itemEntry, 1); return v != null ? v.Name : null; }
            public string ItemIcon(int itemEntry) { var v = _s.ItemInfoView(itemEntry, 1); return v != null ? v.IconPath : null; }
            public string ItemTooltip(int itemEntry) { return _s.ItemInfoTooltip(itemEntry, 1); }   // lead fix 2026-10-11
            public string TooltipOf(SpellInfo s) { var a = Ab(s); return a != null ? a.Tooltip : null; }
            public string DescriptionOf(SpellInfo s) { var a = Ab(s); return a != null ? a.Description : null; }
            AbilityView Ab(SpellInfo s)
            {
                if (s == null) return null;
                if (_ab == null || _abId != s.Id) { _ab = _s.Ability(s, false); _abId = s.Id; }
                return _ab;
            }
        }

        PfFacts PfMe { get { if (_pfFacts == null) _pfFacts = new PfFacts(this); return _pfFacts; } }

        partial void InitProfession()
        {
            _world.On(Op.SMSG_SPELL_GO, PfOnSpellGo, this);
            _world.On(Op.SMSG_CAST_FAILED, PfOnCastFailed, this);
            _world.On(Op.SMSG_SPELL_FAILURE, PfOnSpellFailure, this);
            _world.On(Op.SMSG_GAMEOBJECT_CUSTOM_ANIM, PfOnCustomAnim, this);
            _world.On(Op.SMSG_FISH_NOT_HOOKED, p => { Error("No fish are hooked."); WowLog.Info("Online profession: SMSG_FISH_NOT_HOOKED"); }, this);   // ERR_FISH_NOT_HOOKED (GlobalStrings.lua:3050)
            _world.On(Op.SMSG_FISH_ESCAPED, p => { Error("Your fish got away!"); WowLog.Info("Online profession: SMSG_FISH_ESCAPED"); }, this);       // ERR_FISH_ESCAPED (GlobalStrings.lua:3049)
            WowLog.Info("Online profession: ready (skills from PLAYER_SKILL_INFO_1_1, Create N = one cast after the other " + PfNextGap + " s after each SMSG_SPELL_GO, " +
                        "stopped by a failure / a move / Cancel / closing the window (ours, decision D2); skinning, fishing bobber)");
        }

        // ------------------------------------------------------------------ skills (PLAYER_SKILL_INFO_1_1, AC Player.h:79-89)

        /// <summary>O5 (RefreshSelf): the 128 x 3 skill ints + PLAYER_CHARACTER_POINTS2 (free primary professions, Player.h:1795).</summary>
        partial void PfReadSkills(OnlineObject o)
        {
            int sig;
            unchecked
            {
                sig = 17;
                for (int i = 0; i < PlayerMaxSkills; i++)
                {
                    int b = UF.PLAYER_SKILL_INFO_1_1 + i * 3;
                    _pfIdx[i] = o.I32(b);
                    _pfVal[i] = o.I32(b + 1);
                    _pfBonus[i] = o.I32(b + 2);
                    sig = sig * 31 + _pfIdx[i];
                    sig = sig * 31 + _pfVal[i];
                    sig = sig * 31 + _pfBonus[i];
                }
                int cp2 = o.I32(UF.PLAYER_CHARACTER_POINTS2);
                sig = sig * 31 + cp2;
                _player.FreeProfessions = cp2;
            }
            if (_pfHaveSkills && sig == _pfSkillSig) return;
            _pfSkillSig = sig;
            _pfRawValue.Clear();
            _pfRawMax.Clear();
            for (int i = 0; i < PlayerMaxSkills; i++)
            {
                int id = _pfIdx[i] & 0xFFFF;
                if (id == 0) continue;
                _pfRawValue[id] = _pfVal[i] & 0xFFFF;
                _pfRawMax[id] = (_pfVal[i] >> 16) & 0xFFFF;
            }
            PfRebuildSkills(true);
        }

        void PfRebuildSkills(bool messages)
        {
            bool first = !_pfHaveSkills;
            var list = SkillBook.FromSlots(_pfIdx, _pfVal, _pfBonus, _player.Race, _player.Class, Pdb, _known);
            List<GameEvent> diff = messages && !first ? SkillBook.Diff(new List<SkillView>(_player.Skills), list, Pdb, _player.Race, _player.Class) : null;
            _player.Skills.Clear();
            _player.Skills.AddRange(list);
            _player.SkillsVersion++;
            _pfHaveSkills = true;
            _pfSkillsDirty = false;
            _pfTradeDirty = true;
            if (first)
            {
                int prof = 0;
                foreach (var s in list) if (s.IsPrimaryProfession) prof++;
                WowLog.Info("Online profession: " + list.Count + " skills shown (" + prof + " primary professions, " + _player.FreeProfessions + " free profession slots)");
            }
            if (diff != null)
                foreach (var ev in diff)
                {
                    _events.Enqueue(ev);
                    if (ev.Text == null) continue;
                    _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, SourceGuid = 0, Amount = (int)ChatKind.System, Text = ev.Text });   // WoW shows skill-ups in the chat
                    if (_pfLogs++ < 40) WowLog.Info("Online profession: " + ev.Text);
                }
            if (_tradeSkill != 0 && !_pfRawValue.ContainsKey(_tradeSkill)) { WowLog.Info("Online profession: skill " + _tradeSkill + " is gone - window closed"); CloseTradeSkill(); }
        }

        /// <summary>A learned / removed / superseded spell (the trainer's own handlers): HasTradeWindow and the window's recipes follow.</summary>
        void PfKnownChanged()
        {
            _pfSkillsDirty = true;
            _pfTradeDirty = true;
        }

        /// <summary>S1 (spellfx, Spells.cs RebuildSpells): Player.Recipes changed.</summary>
        partial void PfRecipesChanged()
        {
            _pfSkillsDirty = true;
            _pfTradeDirty = true;
        }

        int PfSkillValue(int skill)
        {
            int v;
            return _pfRawValue.TryGetValue(skill, out v) ? v : 0;
        }

        int PfSkillMax(int skill)
        {
            int v;
            return _pfRawMax.TryGetValue(skill, out v) ? v : 0;
        }

        SkillView PfSkillView(int skill)
        {
            foreach (var s in _player.Skills) if (s != null && s.SkillId == skill) return s;
            return null;
        }

        /// <summary>CMSG_UNLEARN_SKILL: uint32 skillId (SkillHandler.cpp:91-99; only primary professions - the server answers with the field updates
        /// and SMSG_REMOVED_SPELL per spell).</summary>
        public void UnlearnSkill(int skillId)
        {
            var sv = PfSkillView(skillId);
            if (sv == null || !sv.Abandonable || _world == null || _world.IsClosed)
            {
                WowLog.Info("Online profession: unlearn " + skillId + " - not an abandonable skill of this character (nothing sent)");
                return;
            }
            _world.Send(Op.CMSG_UNLEARN_SKILL, new PacketWriter().U32((uint)skillId));
            WowLog.Info("Online profession: CMSG_UNLEARN_SKILL " + skillId + " '" + sv.Name + "' (" + sv.Value + "/" + sv.Max + ")");
            if (_tradeSkill == skillId) CloseTradeSkill();
        }

        // ------------------------------------------------------------------ recipes and profession spells (hooks S1-S3)

        /// <summary>S1: a recipe (SPELL_ATTR0_IS_TRADESKILL 0x20, SharedDefines.h:375) goes to Player.Recipes, not the spellbook.</summary>
        bool PfIsRecipe(SpellInfo s) { return ProfessionDb.IsRecipe(s); }

        /// <summary>S3: ERR_LEARN_RECIPE_S "You have learned how to create a new item: %s." (GlobalStrings.lua:3177) for a recipe.</summary>
        bool PfIsRecipeId(int id) { return ProfessionDb.IsRecipe(Db.Find(id)); }

        /// <summary>S2: a profession spell (SPELL_EFFECT_TRADE_SKILL 47 with a profession line) opens its window instead of being sent
        /// (AzerothCore's EffectTradeSkill does nothing, SpellEffects.cpp:2976-2990; sending it would only start a global cooldown).</summary>
        bool PfIsTradeSkillSpell(int id)
        {
            bool yes = Pdb.TradeSkillOf(id) != 0;
            if (yes && _pfLogs++ < 40) WowLog.Info("Online profession: " + SpellName(id) + " (" + id + ") opens its window - not sent (ours: EffectTradeSkill does nothing on the server)");
            return yes;
        }

        int PfTradeSkillOf(int id) { return Pdb.TradeSkillOf(id); }

        // ------------------------------------------------------------------ the profession window

        public TradeSkillView CurrentTradeSkill { get { return _trade; } }

        public void OpenTradeSkill(int skillId)
        {
            var sv = PfSkillView(skillId);
            bool has = sv != null && sv.HasTradeWindow;
            if (!has && PfSkillValue(skillId) > 0 && Pdb.TradeSpellFor(skillId, _known) != 0) has = true;   // the skills list may lag a learned spell
            if (!has) { Error("You don't know that skill."); return; }   // ours
            if (_trainer != null) CloseTrainer();                          // WoW's "left" panels (ours: one NPC / profession window at a time)
            if (_tradeSkill != skillId) PfStopQueue("another profession window");
            _tradeSkill = skillId;
            PfRebuildTrade();
            int craftable = 0;
            foreach (var r in _trade.Recipes) if (r.Craftable > 0) craftable++;
            WowLog.Info("Online profession: " + _trade.Name + " " + _trade.Rank + "/" + _trade.MaxRank + " - " + _trade.Recipes.Count + " recipes (" + craftable + " craftable)");
        }

        public void CloseTradeSkill()
        {
            if (_tradeSkill == 0 && _trade == null) return;
            PfStopQueue("window closed");
            WowLog.Info("Online profession: window closed (" + (_trade != null ? _trade.Name : "?") + ")");
            _trade = null;
            _tradeSkill = 0;
        }

        void PfRebuildTrade()
        {
            if (_tradeSkill == 0) { _trade = null; return; }
            var v = TradeSkillBook.Build(_tradeSkill, PfMe, Pdb, Db);
            v.QueueSpellId = _pfQueueId;
            v.QueueLeft = _pfQueueLeft;
            v.Version = _trade != null ? _trade.Version + 1 : 1;
            _trade = v;
            _pfTradeDirty = false;
            _pfTradeSig = PfTradeSignature();
            _pfTradeNextAt = _now + PfRebuildGap;
        }

        /// <summary>The bags (entry x count), equipment, skill value, known set and the queue - a change rebuilds the window.</summary>
        int PfTradeSignature()
        {
            unchecked
            {
                int h = PfSkillValue(_tradeSkill) * 31 + PfSkillMax(_tradeSkill);
                h = h * 31 + _known.Count;
                h = h * 31 + _pfQueueId;
                h = h * 31 + _pfQueueLeft;
                h = h * 31 + _itemProtos.Count;   // lead fix 2026-10-11: an item answer (made item / reagent name, icon, tooltip) rebuilds the window
                foreach (var it in _player.Bags) if (it != null) h = h * 31 + it.Entry * 7 + it.Count;
                for (int i = 0; i < _player.Equipment.Length; i++) { var it = _player.Equipment[i]; h = h * 31 + (it != null ? it.Entry : 0); }
                return h;
            }
        }

        int PfHave(int entry)
        {
            int n = 0;
            foreach (var it in _player.Bags) if (it != null && it.Entry == entry) n += Math.Max(1, it.Count);
            return n;
        }

        bool PfEquipped(int entry)
        {
            for (int i = 0; i < _player.Equipment.Length; i++) { var it = _player.Equipment[i]; if (it != null && it.Entry == entry) return true; }
            return false;
        }

        /// <summary>An item's ItemTemplate.TotemCategory from its server answer (npc's hook I1 adds ItemProto.TotemCategory; read by reflection so this
        /// file compiles with or without it). -1 = unknown (the field is not in the build: the window then shows the tool as present - the server decides).</summary>
        int PfItemTotemCategory(int entry)
        {
            if (!_pfTotemFieldTried)
            {
                _pfTotemFieldTried = true;
                _pfTotemField = typeof(ItemProto).GetField("TotemCategory");
                WowLog.Info("Online profession: ItemProto.TotemCategory " + (_pfTotemField != null ? "read from the item answers" : "not in this build - tool categories show as present (the server decides)"));
            }
            if (_pfTotemField == null) return -1;
            ItemProto proto;
            if (!_itemProtos.TryGetValue(entry, out proto) || proto == null) { AskItem(entry); return 0; }
            try { return Convert.ToInt32(_pfTotemField.GetValue(proto)); }
            catch (Exception) { return -1; }
        }

        /// <summary>Spell.TotemCategory: an item of the bags or the equipment whose TotemCategory is compatible (PlayerStorage.cpp:860-915).</summary>
        bool PfHasTotemCategory(int category)
        {
            foreach (var it in _player.Bags)
            {
                if (it == null) continue;
                int c = PfItemTotemCategory(it.Entry);
                if (c < 0 || Pdb.TotemCompatible(c, category)) return true;
            }
            for (int i = 0; i < _player.Equipment.Length; i++)
            {
                var it = _player.Equipment[i];
                if (it == null) continue;
                int c = PfItemTotemCategory(it.Entry);
                if (c < 0 || Pdb.TotemCompatible(c, category)) return true;
            }
            return false;
        }

        /// <summary>Spell.cpp:8083-8100 / GridNotifiers.h:679-701: a spawned GAMEOBJECT_TYPE_SPELL_FOCUS (8) whose data0 is the focus within data1 yards
        /// (+ our reach + the object's default size, IsWithinDistInMap with bounding radius). Colours only - the server decides.</summary>
        bool PfFocusNear(int focus)
        {
            float reach = LdMyReach() + LdDefaultObjectSize;
            foreach (var e in _entities)
            {
                if (e == null || e.Kind != EntityKind.GameObject || e.GoType != 8) continue;
                uint[] d;
                if (!_qGoData.TryGetValue(e.Entry, out d) || d == null || d.Length < 3 || (int)d[1] != focus) continue;
                float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z;
                float r = (int)d[2] + reach;
                if (dx * dx + dy * dy + dz * dz <= r * r) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Create / Create All (ours: 3.3.5 has no repeat count)

        public void CraftRecipe(int recipeSpellId, int count)
        {
            var s = Db.Find(recipeSpellId);
            if (s == null || !_known.Contains(recipeSpellId) || !ProfessionDb.IsRecipe(s)) { Error("You don't know that recipe."); return; }   // ours
            PfStopQueue("a new Create");
            var f = Pdb.Recipe(recipeSpellId);
            if (f != null && f.ItemTarget)
            {
                WowLog.Info("Online profession: " + s.Name + " (" + recipeSpellId + ") targets an item - cast once, the item is picked next (spellfx's item targeting)");
                CastSpell(recipeSpellId, 0);
                return;
            }
            int n = Math.Max(1, Math.Min(PfMaxCount, count));
            _pfQueueId = recipeSpellId;
            _pfQueueLeft = n;
            _pfQueueTotal = n;
            _pfTradeDirty = true;
            PfSendQueued();
        }

        void PfSendQueued()
        {
            var s = Db.Find(_pfQueueId);
            _pfWaitGo = true;
            _pfNextAt = -1;
            _pfGoDeadline = _now + (s != null ? s.CastTimeMs / 1000.0 : 0.0) + PfGoWatchdog;
            int k = _pfQueueTotal - _pfQueueLeft + 1;
            if (_pfLogs++ < 60 || k == 1)
                WowLog.Info("Profession: Create " + _pfQueueTotal + " x " + (s != null ? s.Name : "?") + " (" + _pfQueueId + ") - " + k + "/" + _pfQueueTotal + " sent");
            CastSpell(_pfQueueId, 0);
        }

        /// <summary>S4 (CancelCast) and every stop of the queue.</summary>
        partial void PfStopQueue(string why)
        {
            if (_pfQueueId == 0) return;
            int made = _pfQueueTotal - _pfQueueLeft;
            WowLog.Info("Profession: Create of " + SpellName(_pfQueueId) + " (" + _pfQueueId + ") " + (why == null ? "done" : "stopped (" + why + ")") + " - " + made + " of " + _pfQueueTotal + " made");
            _pfQueueId = 0;
            _pfQueueLeft = 0;
            _pfQueueTotal = 0;
            _pfWaitGo = false;
            _pfNextAt = -1;
            _pfGoDeadline = -1;
            _pfTradeDirty = true;
        }

        /// <summary>SMSG_SPELL_GO (the same reads as Spells.cs OnSpellGo): our queued recipe went off - the next one 0.1 s later, after the GCD.</summary>
        void PfOnSpellGo(WowPacket p)
        {
            if (_pfQueueId == 0) return;
            p.PackedGuid();
            ulong caster = p.PackedGuid();
            p.U8();
            int spell = (int)p.U32();
            if (caster != _player.Guid || spell != _pfQueueId) return;
            _pfQueueLeft--;
            _pfWaitGo = false;
            _pfGoDeadline = -1;
            _pfTradeDirty = true;
            if (_pfQueueLeft <= 0) { PfStopQueue(null); return; }
            _pfNextAt = Math.Max(_now + PfNextGap, _spGcdEnd);
        }

        /// <summary>SMSG_CAST_FAILED: uint8 castCount; uint32 spell; ... (OnCastFailed Spells.cs) - our queued recipe failed: stop.</summary>
        void PfOnCastFailed(WowPacket p)
        {
            if (_pfQueueId == 0) return;
            p.U8();
            int spell = (int)p.U32();
            if (spell == _pfQueueId) PfStopQueue("cast failed");
        }

        /// <summary>SMSG_SPELL_FAILURE: packed caster; uint8; uint32 spell (OnSpellFailure Spells.cs) - interrupted: stop.</summary>
        void PfOnSpellFailure(WowPacket p)
        {
            if (_pfQueueId == 0) return;
            ulong caster = p.PackedGuid();
            p.U8();
            int spell = (int)p.U32();
            if (caster == _player.Guid && spell == _pfQueueId) PfStopQueue("interrupted");
        }

        partial void TickProfession(float dt)
        {
            if (_pfSkillsDirty && _pfHaveSkills) PfRebuildSkills(false);
            if (_pfSkinPending && _now >= _pfSkinCheckAt) PfRecheckSkinSkills();
            if (_pfQueueId != 0)
            {
                string stop = null;
                if (_player.Dead) stop = "dead";
                else if (_tradeSkill == 0) stop = "window closed";
                else if (SpMovingNow()) stop = "moving";
                else if (_pfWaitGo && _pfGoDeadline > 0 && _now > _pfGoDeadline) stop = "no SMSG_SPELL_GO";
                else if (!_pfWaitGo && _pfNextAt >= 0 && _now >= _pfNextAt && SpCooldownLeft(_pfQueueId) <= 0f && _player.CastingSpellId == 0)
                {
                    RecipeView r = null;
                    if (_trade != null) foreach (var x in _trade.Recipes) if (x.SpellId == _pfQueueId) { r = x; break; }
                    if (r != null && r.Craftable <= 0) stop = "out of reagents";
                    else PfSendQueued();
                }
                if (stop != null) PfStopQueue(stop);
            }
            if (_tradeSkill == 0) return;
            bool cooldowns = false;
            if (_now >= _pfCooldownTickAt && _trade != null)
            {
                _pfCooldownTickAt = _now + 1.0;
                foreach (var r in _trade.Recipes) if (r.CooldownRemaining > 0f) { cooldowns = true; break; }
            }
            if (_now < _pfTradeNextAt) return;
            if (_pfTradeDirty || cooldowns || PfTradeSignature() != _pfTradeSig) PfRebuildTrade();
        }

        // ------------------------------------------------------------------ skinning / gathering a looted corpse (hooks O4 + L1)

        /// <summary>O4 (Refresh after Lootable): UNIT_FLAG_SKINNABLE on a dead creature + the skill its creature_template type_flags ask for.</summary>
        partial void PfRefreshSkinnable(OnlineObject o, EntityState s, uint unitFlags)
        {
            bool sk = s.Dead && (unitFlags & PfUnitFlagSkinnable) != 0 && o.TypeId == TypeId.Unit;
            s.Skinnable = sk;
            if (!sk) { s.SkinSkill = 0; return; }
            CreatureInfo ci;
            if (_creatureInfo.TryGetValue(o.Entry, out ci) && ci != null) s.SkinSkill = PfSkinSkillOf(ci.TypeFlags);
            else { s.SkinSkill = 393; _pfSkinPending = true; }   // ours (R5): Skinning until the creature answer arrives
        }

        /// <summary>CreatureTemplate::GetRequiredLootSkill (AC CreatureData.h:252-262): type_flags 0x100 Herbalism 182, 0x200 Mining 186, 0x8000
        /// Engineering 202, else Skinning 393.</summary>
        static int PfSkinSkillOf(uint typeFlags)
        {
            if ((typeFlags & 0x100u) != 0) return 182;
            if ((typeFlags & 0x200u) != 0) return 186;
            if ((typeFlags & 0x8000u) != 0) return 202;
            return 393;
        }

        void PfRecheckSkinSkills()
        {
            _pfSkinCheckAt = _now + 0.5;
            bool still = false;
            foreach (var kv in _objects)
            {
                var s = kv.Value.State;
                if (s == null || !s.Skinnable) continue;
                CreatureInfo ci;
                if (_creatureInfo.TryGetValue(kv.Value.Entry, out ci) && ci != null) s.SkinSkill = PfSkinSkillOf(ci.TypeFlags);
                else still = true;
            }
            _pfSkinPending = still;
        }

        /// <summary>L1 (TryLootInteract, a dead creature that is no longer lootable): cast the gathering spell on a skinnable corpse - the highest known
        /// SPELL_EFFECT_SKINNING spell of its skill (3.5), through the normal cast path (cast bar, moving refusal; TARGET_FLAG_UNIT + the corpse).
        /// True = handled (also with an error line).</summary>
        bool PfTrySkin(OnlineObject o)
        {
            var s = o != null ? o.State : null;
            if (s == null || !s.Skinnable || s.SkinSkill == 0) return false;
            float reach = LdInteractionDistance + LdMyReach() + LdReachOf(o);
            if (o.HasPos && LdDist(o.X, o.Y, o.Z) > reach) { Error("You are too far away to loot that corpse."); return true; }   // ERR_LOOT_TOO_FAR (GlobalStrings.lua:3230)
            int spell = Pdb.SkinSpellFor(s.SkinSkill, _known);
            if (spell == 0) { Error("Requires " + Pdb.SkillName(s.SkinSkill)); return true; }   // ERR_USE_LOCKED_WITH_SPELL_S "Requires %s" (GlobalStrings.lua:3575)
            WowLog.Info("Online profession: skin " + (s.Name ?? "?") + " with " + SpellName(spell) + " " + spell + " (skill " + s.SkinSkill + " " + PfSkillValue(s.SkinSkill) + ")");
            SpCast(spell, o.Guid);
            return true;
        }

        // ------------------------------------------------------------------ fishing (hook Q2 + the bobber's packets)

        /// <summary>Q2 (QUseGameObject case 17 FISHINGNODE, after its 100 yd reach check): CMSG_GAMEOBJ_USE uint64 guid (GameObject::Use FISHINGNODE,
        /// GameObject.cpp:1741-1822: owner only; ready -> the fishing loot, else SMSG_FISH_NOT_HOOKED).</summary>
        partial void PfUseBobber(EntityState e)
        {
            if (e == null || _world == null || _world.IsClosed) return;
            OnlineObject o;
            ulong owner = _objects.TryGetValue(e.Guid, out o) ? o.U64(UF.OBJECT_FIELD_CREATED_BY) : 0UL;
            if (owner != 0 && owner != _player.Guid) { WowLog.Info("Online profession: " + (e.Name ?? "bobber") + " belongs to someone else - nothing sent"); return; }
            _world.Send(Op.CMSG_GAMEOBJ_USE, new PacketWriter().U64(e.Guid));
            WowLog.Info("Online profession: CMSG_GAMEOBJ_USE on " + (e.Name ?? "?") + " (FISHINGNODE, entry " + e.Entry + ")");
        }

        /// <summary>SMSG_GAMEOBJECT_CUSTOM_ANIM: uint64 guid; uint32 anim (GameObject::SendCustomAnim, GameObject.cpp:2164-2170) - the bobber's bite
        /// (FISHING_BOBBER_READY_TIME, GameObject.cpp:500-523): the view plays AnimationData 153 + n 'CustomN' once; our own bobber = FishingBite.</summary>
        void PfOnCustomAnim(WowPacket p)
        {
            ulong guid = p.U64();
            uint anim = p.U32();
            OnlineObject o;
            if (!_objects.TryGetValue(guid, out o) || o.State == null) return;
            var s = o.State;
            s.Emote = anim <= 3 ? 153 + (int)anim : 0;    // AnimationData 153-156 'Custom0'..'Custom3' (harness TrAnims)
            s.EmoteOnce = false;
            s.EmoteCounter++;
            ulong owner = o.U64(UF.OBJECT_FIELD_CREATED_BY);
            if (owner == 0 || owner != _player.Guid) return;
            _events.Enqueue(new GameEvent { Type = GameEventType.FishingBite, SourceGuid = guid, Amount = (int)anim });
            WowLog.Info("Online profession: a fish bites (" + (s.Name ?? "bobber") + ", custom anim " + anim + ")");
        }
    }
}
