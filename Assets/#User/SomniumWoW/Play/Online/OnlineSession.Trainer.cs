// iteration 11 - owner trainer (proposal-trainer.md 3.1-3.3, 5.3, 5.4 "Trainer"): class / profession / riding / pet trainers online.
using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Professions;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const uint TrNpcFlagTrainer = 0x10;        // UNIT_NPC_FLAG_TRAINER (AC UnitDefines.h:326)
        const float TrCloseYards = 10f;            // ours = the quest dialog's QDialogCloseYards (Quests.cs:37)
        const double TrNoAnswerSeconds = 3.0;      // ours: AzerothCore answers nothing for an invalid trainer (NPCHandler.cpp:100-111)
        const double TrRelistGap = 0.5;            // ours: at most one CMSG_TRAINER_LIST per 0.5 s while the window is open
        // out11_questfix (a friend's trainer spam "stuck in a loop") - the trainer guard, all ours:
        const double TrBuyAnswerSeconds = 2.0;     // one CMSG_TRAINER_BUY_SPELL per service until its SUCCEEDED / FAILED (or this long)
        const double TrBuyGap = 0.25;              // and at most one buy (any service) per 0.25 s
        const double TrRelistAnswerSeconds = 2.0;  // one relist in flight at a time (until its SMSG_TRAINER_LIST or this long)
        const double TrErrorRepeatSeconds = 1.0;   // the same buy-failed text at most once a second
        const int TrGuardLogLines = 40;            // drop lines logged per session (then counted only)

        TrainerView _trainer;
        ulong _trainerGuid;
        ulong _trAskGuid;
        double _trAskAt;
        string _trAskName;
        double _trRelistAt = -1, _trLastAsk = -10;
        int _trSig, _trSrvSig, _trOpens, _trRelists;
        bool _trFailTextLogged;
        // out11_questfix: the guard's state
        readonly System.Collections.Generic.Dictionary<int, double> _trBuyAt = new System.Collections.Generic.Dictionary<int, double>();   // service -> its buy in flight
        double _trLastBuy = -10, _trClosedAt = -10, _trRelistSentAt = -10, _trLastErrAt = -10;
        bool _trRelistInFlight;
        string _trLastErr;
        int _trDrops;
        TrFacts _trFacts;
        ProfessionDb _pfDb;

        ProfessionDb Pdb
        {
            get
            {
                if (_pfDb == null) _pfDb = ProfessionDb.Get(_files);
                return _pfDb;
            }
        }

        /// <summary>The character as the trainer window sees it (live: every call reads the session).</summary>
        sealed class TrFacts : ITrainerFacts
        {
            readonly OnlineSession _s;
            public TrFacts(OnlineSession s) { _s = s; }
            public int Level { get { return _s._player.Level; } }
            public int Money { get { return _s._player.Money; } }
            public int FreeProfessions { get { return _s._player.FreeProfessions; } }
            public bool Knows(int spellId) { return _s._known.Contains(spellId); }
            public int SkillValue(int skillLine) { return _s.PfSkillValue(skillLine); }
            public string TooltipOf(SpellInfo s) { return s != null ? _s.Ability(s, false).Tooltip : null; }
        }

        TrFacts TrMe { get { if (_trFacts == null) _trFacts = new TrFacts(this); return _trFacts; } }

        public TrainerView CurrentTrainer { get { return _trainer; } }

        partial void InitTrainer()
        {
            // npc's service registry (proposal-npc.md H4): a trainer without GOSSIP / QUESTGIVER gets CMSG_TRAINER_LIST at Interact; a gossip trainer
            // keeps the dialog and its "train" option (AzerothCore answers that option with the same SMSG_TRAINER_LIST, PlayerGossip.cpp:335-337).
            RegisterNpcService(TrNpcFlagTrainer, g => { TrSendList(g); return true; }, "trainer");
            _world.On(Op.SMSG_TRAINER_LIST, OnTrainerList, this);
            _world.On(Op.SMSG_TRAINER_BUY_SUCCEEDED, OnTrainerBuySucceeded, this);
            _world.On(Op.SMSG_TRAINER_BUY_FAILED, OnTrainerBuyFailed, this);
            // a learned / removed / superseded spell changes the states: ask the list again (own handlers; Spells.cs keeps its own - WorldClient.On
            // keeps every handler of an opcode and rewinds the packet for each)
            _world.On(Op.SMSG_LEARNED_SPELL, p => TrKnownChanged(), this);
            _world.On(Op.SMSG_REMOVED_SPELL, p => TrKnownChanged(), this);
            _world.On(Op.SMSG_SUPERCEDED_SPELL, p => TrKnownChanged(), this);
            WowLog.Info("Online trainer: ready (CMSG_TRAINER_LIST for trainers without gossip; the list re-asked at most every " + TrRelistGap +
                        " s while open, the window closes beyond " + TrCloseYards + " yd, " + TrNoAnswerSeconds + " s without an answer = 'cannot train you' - ours)");
            WowLog.Info("Online trainer: guard - ours: one buy per service until its answer (" + TrBuyAnswerSeconds + " s at most), one buy per " + TrBuyGap +
                        " s, one relist in flight (" + TrRelistAnswerSeconds + " s at most), a list nobody asked for (or asked before the window closed) never opens it, the same failure text once per " +
                        TrErrorRepeatSeconds + " s");
        }

        /// <summary>out11_questfix: one log line per thing the trainer guard drops (the first TrGuardLogLines, then counted).</summary>
        void TrDrop(string what)
        {
            _trDrops++;
            if (_trDrops <= TrGuardLogLines) WowLog.Info("Online trainer: guard dropped " + what + (_trDrops == TrGuardLogLines ? " (further drops are counted, not logged)" : ""));
        }

        /// <summary>out11_questfix: when THIS trainer's window was last asked for - our CMSG_TRAINER_LIST, the npc routing / a gossip option
        /// (NpcAnswerFor), or a conversation with it; -1 = never / too long ago.</summary>
        double TrAskedAt(ulong g)
        {
            double at = -1;
            if (g == 0) return at;
            if (g == _trAskGuid) at = Math.Max(at, _trAskAt);
            if (g == _npcAskGuid && _now - _npcAskAt <= NpcAnswerSeconds) at = Math.Max(at, _npcAskAt);   // NpcAnswerFor's two rules (Npc.cs)
            if (_qMenu != null && _qMenu.Npc == g) at = Math.Max(at, _qMenu.Started);
            return at;
        }

        void TrKnownChanged()
        {
            PfKnownChanged();
            if (_trainer != null) TrRelistSoon(0.3);
        }

        /// <summary>CMSG_TRAINER_LIST: uint64 guid (NPCPackets.cpp:20-23, handler NPCHandler.cpp:81-91).</summary>
        partial void TrSendList(ulong guid)
        {
            TrAsk(guid, false);
        }

        void TrAsk(ulong guid, bool relist)
        {
            if (_world == null || _world.IsClosed || guid == 0) return;
            _world.Send(Op.CMSG_TRAINER_LIST, new PacketWriter().U64(guid));
            _trLastAsk = _now;
            if (relist)
            {
                _trRelistInFlight = true;                                       // out11_questfix: the next relist waits for this one's answer
                _trRelistSentAt = _now;
                if (_trRelists++ < 20) WowLog.Info("Online trainer: list asked again (" + (_trainer != null ? _trainer.NpcName : "?") + ")");
                return;
            }
            var e = Find(guid);
            OnlineObject o;
            uint npc = _objects.TryGetValue(guid, out o) ? o.U32(UF.UNIT_NPC_FLAGS) : 0u;
            _trAskGuid = guid;
            _trAskAt = _now;
            _trAskName = e != null ? e.Name : null;
            WowLog.Info("Online trainer: CMSG_TRAINER_LIST to " + (_trAskName ?? "?") + " (" + guid.ToString("X16") + ", npc flags 0x" + npc.ToString("X") + ")");
        }

        void TrRelistSoon(double delay)
        {
            if (_trainer == null) return;
            double at = _now + delay;
            if (_trRelistAt < 0 || at < _trRelistAt) _trRelistAt = at;
        }

        void OnTrainerList(WowPacket p)
        {
            TrainerListPacket t;
            string why;
            if (!TrainerListPacket.Parse(p, out t, out why)) { WowLog.Warn("Online trainer: SMSG_TRAINER_LIST: " + why + " - dropped"); return; }
            var e = Find(t.Trainer);
            string name = e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : (_trAskName ?? "Trainer");
            var v = TrainerBook.Build(t, name, TrMe, Db, Pdb);
            bool open = _trainer != null && _trainerGuid == t.Trainer;
            // out11_questfix: a list nobody asked for - a relist answered after the window closed, an older request - never (re)opens the window
            // (the vendor / bank rule, Vendor.cs:74 / Bank.cs:59); while it is open, any list of its trainer refreshes it
            if (!open)
            {
                double asked = TrAskedAt(t.Trainer);
                if (asked < 0 || asked <= _trClosedAt)
                {
                    TrDrop("SMSG_TRAINER_LIST of " + name + " (" + t.Trainer.ToString("X16") + "): " + (asked < 0 ? "not asked for" : "asked before the window closed") + " - the window stays closed");
                    if (t.Trainer == _trAskGuid) _trAskGuid = 0;
                    return;
                }
            }
            if (t.Trainer == _trAskGuid) _trAskGuid = 0;
            if (open)
            {
                _trRelistInFlight = false;
                _trainer.Services = v.Services;
                _trainer.Greeting = v.Greeting;
                _trainer.Kind = v.Kind;
                _trainer.Version++;
                return;
            }
            v.Version = 1;
            _trainer = v;
            _trainerGuid = t.Trainer;
            _trRelistInFlight = false;                                     // out11_questfix
            _trBuyAt.Clear();
            _trSig = TrSignature();
            _trSrvSig = TrServerSignature();
            NpcServiceOpened(t.Trainer, "trainer");                        // npc H4: closes the gossip dialog locally, stops their watchdog
            if (CurrentDialog != null) CloseDialog();                      // WoW's "left" panel swap (Blizzard_TrainerUI.lua:14)
            if (_trade != null) CloseTradeSkill();                         // ours: one NPC-driven left window at a time (VR)
            int a = 0, u = 0, k = 0;
            foreach (var sv in v.Services) { if (sv.State == TrainerSpellState.Available) a++; else if (sv.State == TrainerSpellState.Known) k++; else u++; }
            string g = v.Greeting ?? "";
            if (_trOpens++ < 50)
                WowLog.Info("Online trainer: " + name + " (" + v.Kind + ") - " + v.Services.Count + " services: " + a + " available, " + u + " unavailable, " + k +
                            " known; greeting '" + (g.Length > 40 ? g.Substring(0, 40) + "..." : g) + "'");
        }

        /// <summary>SMSG_TRAINER_BUY_SUCCEEDED: uint64 trainer; int32 spell (NPCPackets.cpp:63-69). Also the innkeeper's bind (spell 3286,
        /// NPCHandler.cpp:317-329): only the open trainer's guid is a purchase.</summary>
        void OnTrainerBuySucceeded(WowPacket p)
        {
            ulong guid = p.U64();
            int spell = p.I32();
            _trBuyAt.Remove(spell);                                              // out11_questfix: its buy is answered
            if (_trainer == null || guid != _trainerGuid)
            {
                WowLog.Info("Online trainer: SMSG_TRAINER_BUY_SUCCEEDED " + spell + " from " + guid.ToString("X16") + " (not the open trainer - nothing to show)");
                return;
            }
            foreach (var sv in _trainer.Services)
                if (sv.SpellId == spell && sv.State == TrainerSpellState.Known) { TrDrop("a repeated SMSG_TRAINER_BUY_SUCCEEDED " + spell + " (known already - no second sound / haptic)"); return; }   // out11_questfix
            WowLog.Info("Online trainer: learned service " + spell + " from " + _trainer.NpcName);
            _events.Enqueue(new GameEvent { Type = GameEventType.TrainerTrained, SourceGuid = guid, SpellId = spell });
            // out11_questfix (ours): the service reads "known" at once - until the relist it stayed Available and trainable, and every further
            // tap of Train sent the same CMSG_TRAINER_BUY_SPELL again (AzerothCore answers each with SMSG_TRAINER_BUY_FAILED, Trainer.cpp:95-98)
            foreach (var sv in _trainer.Services)
                if (sv.SpellId == spell && sv.State != TrainerSpellState.Known) { sv.State = TrainerSpellState.Known; sv.CanTrain = false; _trainer.Version++; }
            TrRelistSoon(0.3);
        }

        /// <summary>SMSG_TRAINER_BUY_FAILED: uint64 trainer; int32 spell; int32 reason (NPCPackets.cpp:54-61; Trainer.h:46-51: 0 Unavailable,
        /// 1 NotEnoughMoney, 2 NotEnoughSkill).</summary>
        void OnTrainerBuyFailed(WowPacket p)
        {
            ulong guid = p.U64();
            int spell = p.I32();
            int reason = p.I32();
            _trBuyAt.Remove(spell);                                              // out11_questfix: its buy is answered
            string text = reason == 1 ? "You don't have enough money." : "You can't learn that yet.";   // ERR_NOT_ENOUGH_MONEY (GlobalStrings.lua:3307) / ours
            string what = "SMSG_TRAINER_BUY_FAILED spell " + spell + " reason " + reason + " (" + (reason == 1 ? "not enough money" : reason == 2 ? "not enough skill" : "unavailable") + ")";
            if (text == _trLastErr && _now - _trLastErrAt < TrErrorRepeatSeconds) TrDrop("the text of a repeated " + what);   // out11_questfix
            else
            {
                _trLastErr = text; _trLastErrAt = _now;
                Error(text);
                if (reason != 1 && !_trFailTextLogged) { _trFailTextLogged = true; WowLog.Info("Online trainer: 'You can't learn that yet.' is our text for SMSG_TRAINER_BUY_FAILED reasons 0 / 2 (no GlobalStrings line exists)"); }
                WowLog.Info("Online trainer: " + what);
            }
            if (_trainer != null && guid == _trainerGuid) TrRelistSoon(0.3);
        }

        /// <summary>CMSG_TRAINER_BUY_SPELL: uint64 trainer; int32 spell (NPCPackets.cpp:48-52). The server teaches and answers (3.3).</summary>
        public void BuyTrainerSpell(int spellId)
        {
            if (_trainer == null || _world == null || _world.IsClosed) return;
            TrainerServiceView sv = null;
            foreach (var x in _trainer.Services) if (x.SpellId == spellId) { sv = x; break; }
            if (sv == null) return;
            // out11_questfix - the guard: idempotent (one buy per service until its answer), rate-limited, nothing for a service known already
            double at;
            if (sv.State == TrainerSpellState.Known) { TrDrop("CMSG_TRAINER_BUY_SPELL " + spellId + " '" + sv.Name + "' (known already)"); return; }
            if (_trBuyAt.TryGetValue(spellId, out at) && _now - at < TrBuyAnswerSeconds)
            {
                TrDrop("CMSG_TRAINER_BUY_SPELL " + spellId + " '" + sv.Name + "' (its buy " + ((int)((_now - at) * 1000)).ToString() + " ms ago is not answered yet)");
                return;
            }
            if (_now - _trLastBuy < TrBuyGap) { TrDrop("CMSG_TRAINER_BUY_SPELL " + spellId + " '" + sv.Name + "' (" + ((int)((_now - _trLastBuy) * 1000)).ToString() + " ms after the last buy)"); return; }
            _trBuyAt[spellId] = _now;
            _trLastBuy = _now;
            _world.Send(Op.CMSG_TRAINER_BUY_SPELL, new PacketWriter().U64(_trainerGuid).I32(spellId));
            WowLog.Info("Online trainer: buy " + spellId + " '" + sv.Name + (sv.Rank.Length > 0 ? " (" + sv.Rank + ")" : "") + "' for " + sv.MoneyCost + " copper");
        }

        /// <summary>Client-only in 3.3.5 (no close packet; the server keeps no trainer state).</summary>
        public void CloseTrainer()
        {
            if (_trainer == null) return;
            WowLog.Info("Online trainer: window closed (" + _trainer.NpcName + ")");
            _trainer = null;
            _trainerGuid = 0;
            _trRelistAt = -1;
            _trClosedAt = _now;                                                 // out11_questfix: lists asked before this never reopen it
            _trRelistInFlight = false;
            _trBuyAt.Clear();
        }

        /// <summary>What the server's states depend on beyond the known set: the level and the skills (a change = ask the list again).
        /// out11_questfix: the raw skill fields' signature (Profession.cs _pfSkillSig), not SkillsVersion - that one counts every skills rebuild
        /// (each learned spell rebuilds them, PfKnownChanged), so each buy asked the list twice.</summary>
        int TrServerSignature() { unchecked { return _player.Level * 7919 + _pfSkillSig; } }

        /// <summary>What the local parts depend on (requirement colours, CanTrain): money, level, free professions, known set, skills.</summary>
        int TrSignature()
        {
            unchecked
            {
                int h = _player.Money;
                h = h * 31 + _player.Level;
                h = h * 31 + _player.FreeProfessions;
                h = h * 31 + _known.Count;
                h = h * 31 + _player.SkillsVersion;
                return h;
            }
        }

        partial void TickTrainer(float dt)
        {
            if (_trAskGuid != 0 && _now - _trAskAt > TrNoAnswerSeconds)
            {
                string n = _trAskName ?? "That trainer";
                _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = n + " cannot train you." });   // ours
                WowLog.Info("Online trainer: no SMSG_TRAINER_LIST from " + n + " within " + TrNoAnswerSeconds + " s - AzerothCore answers nothing when the trainer is not " +
                            "for this character (Trainer::IsTrainerValidForPlayer: class / race / known profession) or has no trainer data (NPCHandler.cpp:100-111)");
                _trAskGuid = 0;
            }
            if (_trainer == null) return;
            var e = Find(_trainerGuid);
            string why = null;
            if (e == null) why = "the trainer is gone";
            else if (e.Dead) why = "the trainer died";
            else if (_player.Dead) why = "you died";
            else
            {
                float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z;
                if (dx * dx + dy * dy + dz * dz > TrCloseYards * TrCloseYards) why = "more than " + TrCloseYards + " yd away";
            }
            if (why != null) { WowLog.Info("Online trainer: closing (" + why + ")"); CloseTrainer(); return; }
            int sig = TrSignature();
            if (sig != _trSig)
            {
                _trSig = sig;
                if (TrainerBook.RefreshLocal(_trainer, TrMe, Db, Pdb)) _trainer.Version++;
            }
            int srv = TrServerSignature();
            if (srv != _trSrvSig) { _trSrvSig = srv; TrRelistSoon(TrRelistGap); }   // a level / skill change can flip the server's states
            if (_trRelistAt >= 0 && _now >= _trRelistAt && _now - _trLastAsk >= TrRelistGap)
            {
                // out11_questfix: one relist in flight - the wanted one waits for its answer (TrRelistAnswerSeconds at most) and then goes once
                if (_trRelistInFlight && _now - _trRelistSentAt < TrRelistAnswerSeconds) return;
                if (_trRelistInFlight) TrDrop("the wait for an unanswered relist (" + TrRelistAnswerSeconds + " s) - asking again");
                _trRelistAt = -1;
                TrAsk(_trainerGuid, true);
            }
        }
    }
}
