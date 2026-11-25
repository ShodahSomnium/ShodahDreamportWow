// iteration 11 - owner npc (proposal-npc.md 3.2 / 3.3 / 6.4 H4): the NPC routing, the service registry other owners plug their windows
// into, and the gossip options (GossipFrame) - SMSG_GOSSIP_MESSAGE options, CMSG_GOSSIP_SELECT_OPTION, the lone-option auto-select,
// SMSG_GOSSIP_POI and the answers whose window this version lacks. AzerothCore master quoted as AC <file>:<line> (copies in
// P/acsrc11/npc/). The D1 item members (MoveItem / SplitItem / DestroyItem / StoreItemInBag / UseItemAt / EquipItemAt) live in
// OnlineSession.Items.cs, ConfirmBinder in OnlineSession.Bank.cs.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        // ---- AzerothCore values: NPCFlags (UnitDefines.h:321-348). "Nf" prefix: Objects.cs already has NpcQuestGiver.
        const uint NfGossip = 0x1, NfQuestGiver = 0x2, NfTrainer = 0x10, NfVendor = 0x80, NfRepair = 0x1000, NfFlightMaster = 0x2000,
                   NfInnkeeper = 0x10000, NfBanker = 0x20000, NfPetitioner = 0x40000, NfTabard = 0x80000, NfBattlemaster = 0x100000,
                   NfAuctioneer = 0x200000, NfStable = 0x400000, NfGuildBanker = 0x800000, NfSpellClick = 0x01000000, NfMailbox = 0x04000000;
        /// <summary>Routing rule 6 order (proposal-npc.md 3.2).</summary>
        static readonly uint[] NpcServiceOrder = { NfTrainer, NfFlightMaster, NfAuctioneer, NfBattlemaster, NfPetitioner, NfTabard, NfStable, NfGuildBanker, NfMailbox };
        /// <summary>Rule 3: two or more of these = the server's menu 0 lists every service (PlayerGossip.cpp:63-64); sub-flags fold into their main flag.</summary>
        static readonly uint[] NpcDistinctServices = { NfVendor, NfTrainer, NfFlightMaster, NfInnkeeper, NfBanker, NfPetitioner, NfTabard, NfBattlemaster, NfAuctioneer, NfStable, NfGuildBanker, NfMailbox };
        /// <summary>Answers whose window is not in this version (social gap G2: the tabard designer).</summary>
        static readonly ushort[] NpcMissingWindows = { Op.MSG_TABARDVENDOR_ACTIVATE };
        /// <summary>GossipOptionIcon -> Interface\GossipFrame\&lt;X&gt;GossipIcon (GossipDef.h:61-71, the art files of the user's install).</summary>
        static readonly string[] GossipIconNames = { "Gossip", "Vendor", "Taxi", "Trainer", "Healer", "Binder", "Banker", "Petition", "Tabard", "BattleMaster", "Unlearn" };

        // ---- ours (named here, logged once in InitNpc)
        const double NpcAnswerSeconds = 5.0;          // a service answer later than this after our request is ignored (= QHelloTimeout)
        const double NpcMissingWindowRepeat = 10.0;   // the "not in this version" line at most once per NPC per this many seconds

        sealed class NpcService { public uint Flag; public Func<ulong, bool> Open; public string What; public bool EvenWithGossip; }
        readonly Dictionary<uint, NpcService> _npcServices = new Dictionary<uint, NpcService>();   // field initializer: Init order does not matter
        readonly Dictionary<ulong, double> _npcMissingSaid = new Dictionary<ulong, double>();
        ulong _npcAskGuid; double _npcAskAt = -100;  // the NPC a service request / gossip select went to (vendor list, bank, ...)
        // the last gossip menu the server sent (recorded by NpcAutoSelect, which quests' OnQGossipMessage calls for every accepted menu)
        ulong _npcMenuGuid; int _npcMenuId; List<GossipOptionView> _npcMenuOptions = new List<GossipOptionView>();
        int _npcRouted, _npcSelects, _npcAutoSelects;

        partial void InitNpc()
        {
            if (_world == null) return;
            foreach (ushort op in NpcMissingWindows) { ushort o = op; _world.On(o, p => NpcWindowMissing(o, p), this); }
            _world.On(Op.SMSG_GOSSIP_POI, OnNpcGossipPoi, this);
            WowLog.Info("Online npc: ours - a service answer counts for " + NpcAnswerSeconds + " s after our request, the 'not in this version' line repeats after " +
                        NpcMissingWindowRepeat + " s per NPC; a lone service option is always chosen directly (GossipFrame.lua:10-17, ForceGossip unknown)");
        }

        /// <summary>An owner of an NPC window: when the routing (proposal-npc.md 3.2) meets npcFlag, open(guid) sends its opcode and returns
        /// true (handled), or false to let the routing go on. evenWithGossip = also called for NPCs that have GOSSIP / QUESTGIVER flags,
        /// BEFORE their hello (rule 0: pets' spell click, motion's flight-master hello memory); otherwise only for service-only NPCs (rule 6).
        /// Registered once per flag; the last registration wins.</summary>
        internal void RegisterNpcService(uint npcFlag, Func<ulong, bool> open, string what, bool evenWithGossip = false)
        {
            if (npcFlag == 0 || open == null) return;
            _npcServices[npcFlag] = new NpcService { Flag = npcFlag, Open = open, What = what ?? "?", EvenWithGossip = evenWithGossip };
            WowLog.Info("Online npc: service 0x" + npcFlag.ToString("X") + " (" + (what ?? "?") + ") registered" + (evenWithGossip ? " - asked before the gossip / quest hello too" : ""));
        }

        /// <summary>A service window opened for this NPC (SMSG_TRAINER_LIST, SMSG_SHOWTAXINODES, SMSG_SHOW_BANK, SMSG_LIST_INVENTORY ...):
        /// the gossip dialog closes locally (the real client replaces the left panel - no packet, the server keeps its menu), and the
        /// other left-panel windows of this area (merchant / bank / item text) close like WoW's UIParent panel rule (UIPanelWindows
        /// area "left", pushable 0).</summary>
        internal void NpcServiceOpened(ulong npc, string what)
        {
            bool hadDialog = _qMenu != null || _dialog != null;
            _qMenu = null;
            _dialog = null;
            if (what != "merchant" && _merchant != null) VendClose("the " + what + " window opened");
            if (what != "bank" && _bankGuid != 0) BankClose("the " + what + " window opened");
            if (what != "item text" && _itemText != null) ItxClose("the " + what + " window opened");
            if (hadDialog) WowLog.Info("Online npc: " + what + " of " + NpcName(npc) + " opened - the dialog closes (the window replaces it)");
        }

        /// <summary>A service window may open for this NPC now: we asked for it within NpcAnswerSeconds, or its conversation is open.</summary>
        bool NpcAnswerFor(ulong guid)
        {
            return guid != 0 && ((guid == _npcAskGuid && _now - _npcAskAt <= NpcAnswerSeconds) || (_qMenu != null && _qMenu.Npc == guid));
        }

        /// <summary>
        /// The routing of an NPC with npc flags (proposal-npc.md 3.2 rules 0-7; called by quests' Interact after the reach and life checks):
        /// 0 registered evenWithGossip services (spell click, flight master); 1-2 GOSSIP / QUESTGIVER -> false (Interact sends the hello);
        /// 3 two or more distinct services -> CMSG_GOSSIP_HELLO (the server's menu 0 lists them); 4 VENDOR -> CMSG_LIST_INVENTORY
        /// (ItemHandler.cpp:844-852); 5 BANKER -> CMSG_BANKER_ACTIVATE (BankHandler.cpp:44); 6 registered owners (trainer, flight master,
        /// auctioneer, battlemaster, petitioner, tabard, stable, guild bank, mailbox); 7 anything else -> CMSG_GOSSIP_HELLO (any npc flag is
        /// accepted, NPCHandler.cpp:151-153; menu 0 then has the NPC's service options, PlayerGossip.cpp:41-64).
        /// </summary>
        bool NpcRoute(EntityState e, OnlineObject o, uint npc)
        {
            if (e == null || _world == null) return false;
            foreach (var kv in _npcServices)                                                              // rule 0
            {
                var svc = kv.Value;
                if (!svc.EvenWithGossip || (npc & kv.Key) == 0) continue;
                if (NpcCall(svc, e.Guid)) { _npcAskGuid = e.Guid; _npcAskAt = _now; return NpcLog(e, npc, svc.What + " (registered)"); }
            }
            if ((npc & (NfGossip | NfQuestGiver)) != 0) return false;                                    // rules 1-2: the hello in Interact
            int services = 0;
            foreach (uint f in NpcDistinctServices) if ((npc & f) != 0) services++;
            if (services < 2)
            {
                if ((npc & NfVendor) != 0) return NpcAsk(e, npc, Op.CMSG_LIST_INVENTORY, "merchant");     // rule 4
                if ((npc & NfBanker) != 0) return NpcAsk(e, npc, Op.CMSG_BANKER_ACTIVATE, "bank");        // rule 5
                foreach (uint f in NpcServiceOrder)                                                       // rule 6
                {
                    NpcService svc;
                    if ((npc & f) == 0 || !_npcServices.TryGetValue(f, out svc) || svc.EvenWithGossip) continue;
                    if (NpcCall(svc, e.Guid)) { _npcAskGuid = e.Guid; _npcAskAt = _now; return NpcLog(e, npc, svc.What + " (registered)"); }
                }
            }
            _qHelloGuid = e.Guid;                                                                         // rule 7 (and 3): QWantsMenu takes the menu
            _qHelloAt = _now;
            return NpcAsk(e, npc, Op.CMSG_GOSSIP_HELLO, services >= 2 ? "several services - the server's menu" : "the server's default menu");
        }

        bool NpcCall(NpcService svc, ulong guid)
        {
            try { return svc.Open(guid); }
            catch (Exception ex)
            {
                WowLog.Warn("Online npc: the " + svc.What + " service failed (" + ex.Message + ") - the routing goes on");
                return false;
            }
        }

        bool NpcAsk(EntityState e, uint npc, ushort op, string what)
        {
            _world.Send(op, new PacketWriter().U64(e.Guid));                                            // every one of them is "uint64 guid" (3.2)
            _npcAskGuid = e.Guid;
            _npcAskAt = _now;
            return NpcLog(e, npc, Op.Name(op) + " - " + what);
        }

        bool NpcLog(EntityState e, uint npc, string what)
        {
            _npcRouted++;
            WowLog.Info("Online npc: " + (e.Name ?? "?") + " (" + e.Guid.ToString("X16") + ", npc flags 0x" + npc.ToString("X") + (e.Dead ? ", looks dead" : "") + ") -> " + what);
            return true;
        }

        string NpcName(ulong guid)
        {
            var e = Find(guid);
            return e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : guid.ToString("X16");
        }

        // ------------------------------------------------------------------ gossip options

        /// <summary>The options of SMSG_GOSSIP_MESSAGE (PlayerMenu::SendGossipMenu, GossipDef.cpp:203-212): per option uint32 index (the menu
        /// item id), uint8 icon, uint8 coded (":208 makes pop up box password"), uint32 box money, string text, string box text.</summary>
        List<GossipOptionView> NpcReadGossipOptions(WowPacket p, uint n)
        {
            var list = new List<GossipOptionView>();
            for (uint i = 0; i < n && i < 0x20 && p.Remaining >= 12; i++)                           // 4 + 1 + 1 + 4 + two empty strings
            {
                var o = new GossipOptionView { Index = (int)p.U32(), Icon = p.U8() };
                o.Coded = p.U8() != 0;
                o.BoxMoney = (int)p.U32();
                o.Text = p.CString();
                o.BoxText = p.CString();
                list.Add(o);
            }
            return list;
        }

        /// <summary>
        /// Called for every menu the session accepts (quests' OnQGossipMessage, after QWantsMenu). Records the menu for SelectGossipOption;
        /// a menu with no quests and exactly ONE option whose icon is not 0 is chosen at once (GossipFrame.lua:10-17 "if there is only a
        /// non-gossip option, then go to it directly") - true = chosen, the dialog does not open. A coded / paid / confirmed option still
        /// shows (the client would pop GOSSIP_ENTER_CODE / GOSSIP_CONFIRM first, UIParent.lua:913-934).
        /// </summary>
        bool NpcAutoSelect(ulong npc, int menuId, List<GossipOptionView> options, int questCount)
        {
            _npcMenuGuid = npc;
            _npcMenuId = menuId;
            _npcMenuOptions = options ?? new List<GossipOptionView>();
            if (_world == null || questCount != 0 || options == null || options.Count != 1 || options[0].Icon == 0) return false;
            var o = options[0];
            if (o.Coded || o.BoxMoney > 0 || !string.IsNullOrEmpty(o.BoxText)) return false;
            _npcAutoSelects++;
            WowLog.Info("Online npc: " + NpcName(npc) + " menu " + menuId + " has only the " + IconName(o.Icon) + " option '" + o.Text + "' - chosen directly (GossipFrame.lua:10-17)");
            NpcSendSelect(npc, menuId, o.Index, null);
            return true;
        }

        static string IconName(int icon) { return icon >= 0 && icon < GossipIconNames.Length ? GossipIconNames[icon] : "Gossip"; }

        /// <summary>
        /// CMSG_GOSSIP_SELECT_OPTION (MiscHandler.cpp:89-107: `recv_data >> guid >> menuId >> gossipListId;` and the code string ONLY for a
        /// coded option, :106-107). The menu id must be the one of the last SMSG_GOSSIP_MESSAGE of that NPC (PlayerGossip.cpp:253-255).
        /// Works for creatures, game objects and items (MiscHandler.cpp:118-144).
        /// </summary>
        public void SelectGossipOption(int optionIndex, string code)
        {
            if (_world == null) return;
            var m = _qMenu;
            if (m == null || m.Npc != _npcMenuGuid || _npcMenuGuid == 0)
            {
                WowLog.Info("Online npc: gossip option " + optionIndex + " chosen with no menu open - nothing sent");
                return;
            }
            GossipOptionView o = _npcMenuOptions.Find(x => x.Index == optionIndex);
            if (o == null)
            {
                WowLog.Info("Online npc: the menu of " + NpcName(m.Npc) + " has no option " + optionIndex + " - nothing sent");
                return;
            }
            NpcSendSelect(m.Npc, _npcMenuId, o.Index, o.Coded ? (code ?? "") : null);
        }

        void NpcSendSelect(ulong npc, int menuId, int index, string code)
        {
            var w = new PacketWriter().U64(npc).U32((uint)menuId).U32((uint)index);
            if (code != null) w.CString(code);
            _world.Send(Op.CMSG_GOSSIP_SELECT_OPTION, w);
            _npcSelects++;
            _npcAskGuid = npc; _npcAskAt = _now;                                                       // a service window of this NPC is wanted
            _qHelloGuid = npc; _qHelloAt = _now;                                                       // a next menu / quest frame of the same NPC too
            WowLog.Info("Online npc: CMSG_GOSSIP_SELECT_OPTION to " + NpcName(npc) + " menu " + menuId + " option " + index + (code != null ? " with a code" : ""));
        }

        /// <summary>SMSG_GOSSIP_POI (PlayerMenu::SendPointOfInterest, GossipDef.cpp:261-267): uint32 flags, float x, float y, uint32 icon,
        /// uint32 importance, string name - a guard's directions. GameEvent GossipPoi (the WoW Map may pin it) + the name as an Info line.</summary>
        void OnNpcGossipPoi(WowPacket p)
        {
            if (p.Remaining < 20) return;
            p.U32();
            float x = p.F32(), y = p.F32();
            int icon = (int)p.U32(), importance = (int)p.U32();
            string name = p.Remaining > 0 ? p.CString() : "";
            _events.Enqueue(new GameEvent { Type = GameEventType.GossipPoi, Pos = new WowPos(x, y, 0f, 0f), Amount = icon, SpellId = importance, Text = name });
            if (!string.IsNullOrEmpty(name)) _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = name });
            WowLog.Info("Online npc: point of interest '" + name + "' at (" + x.ToString("0.0") + ", " + y.ToString("0.0") + "), icon " + icon + ", importance " + importance);
        }

        /// <summary>A server answer whose window this version lacks (every one of them starts with the npc guid): one Info line (ours).</summary>
        void NpcWindowMissing(ushort op, WowPacket p)
        {
            ulong npc = p.Remaining >= 8 ? p.U64() : 0;
            double said;
            if (_npcMissingSaid.TryGetValue(npc, out said) && _now - said < NpcMissingWindowRepeat) return;
            _npcMissingSaid[npc] = _now;
            NpcServiceOpened(npc, Op.Name(op));
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = "That window is not in this version yet." });   // ours
            WowLog.Info("Online npc: " + Op.Name(op) + " from " + NpcName(npc) + " - no window in this version");
        }

        /// <summary>For the session summary / probes.</summary>
        internal string NpcSummary()
        {
            return "npc routed " + _npcRouted + ", gossip selects " + _npcSelects + " (auto " + _npcAutoSelects + "), services " + _npcServices.Count;
        }
    }
}
