// iteration 11 - owner interact (proposal-interact.md 6.3.2): the hub MonoBehaviour of the player-interaction UI. Added by WowGame after UiGroup.
// - registers the Raid fallback window (UiRaid.Spec, kind 51; Trade 39 / Inspect 40 are in UiRegistry's Wave0 list with their real specs) and
//   hands the Raid tab to the Social window (UiRaid.HookSocial);
// - drives WoW's StaticPopups of this area by polling the views (uicore's UiPopup): TRADE, DUEL_REQUESTED, DUEL_OUTOFBOUNDS, CONFIRM_SUMMON
//   (StaticPopup.lua 1291-1302 / 2156-2173 / 2534-2558; the timer texts as StaticPopup_OnUpdate formats them, :3307-3339) + the VR haptic;
// - ticks UiRaidHud (the ReadyCheckFrame and the desktop raid pullouts);
// - the unit-frame icon hooks (uicore H6): raid target icon + PvP icon of the target / focus frame, our PvP icon + its 5:00 timer, the sound
//   igPVPUpdate when our icon appears (PlayerFrame.lua:89-91, :103-105);
// - UiBags.Use (hook H8): the open trade window takes the item (UseContainerItem); the Auctions tab through AuctionUseBagItem (queues);
// - dropping a bag item on a friendly player (UiPickup.DropOnUnit): InitiateTrade, the item goes into the trade once it opens (ours: 15 s);
// - UiChat's slash commands (hook H9): /duel /yield /concede /forfeit /f /follow /fol /ins /inspect /tr /trade /readycheck /pvp /mt /maintank
//   /mtoff /maintankoff /ma /mainassist /maoff /mainassistoff /clearmt /clearmaintank /clearma /clearmainassist (GlobalStrings.lua 6445-6829).
// Everything runs only while a game session exists (WowPlay.Session; visitors never start the game).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.Interact;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    [DefaultExecutionOrder(10012)]
    public sealed class UiInteract : MonoBehaviour
    {
        private const float DropTradeSeconds = 15f;      // ours: an item dropped on a player goes into the trade if it opens within 15 s
        private const float TradeYards = 11.11f;         // TRADE_DISTANCE (AzerothCore ObjectDefines.h:29)
        private const float NameWaitSeconds = 1f;        // ours: a popup waits up to 1 s for the other player's name

        // PvP icon art (PlayerFrame.lua:86-118, TargetFrame.lua:274-285): one string instance each (the frames compare references).
        private static readonly string PvpAlliance = @"Interface\TargetingFrame\UI-PVP-Alliance.blp";
        private static readonly string PvpHorde = @"Interface\TargetingFrame\UI-PVP-Horde.blp";
        private static readonly string PvpFfa = @"Interface\TargetingFrame\UI-PVP-FFA.blp";

        /// <summary>queues (UiAuction): the Auctions tab takes a bag item (index into PlayerState.Bags); true = taken. Null = no auction window.</summary>
        public static Func<int, bool> AuctionUseBagItem;

        private static bool _registered;
        private UiRaidHud _raidHud;
        private IWowSession _s;

        // popups
        private int _tradePopup, _duelPopup, _boundsPopup, _summonPopup;
        private ulong _tradeFor, _duelFor;
        private float _tradeSeen, _duelSeen;
        private string _boundsText, _summonText;
        private SummonView _summonFor;

        // a bag item dropped on a player
        private ulong _dropPartner;
        private int _dropBag = -1, _dropSlot = -1;
        private float _dropAt;

        // our PvP icon (the igPVPUpdate sound)
        private int _ownPvpIcon = -1;

        private static readonly HashSet<string> Logged = new HashSet<string>();

        // ------------------------------------------------------------------ lifetime

        private void OnEnable()
        {
            if (!_registered)
            {
                _registered = true;
                UiRegistry.Register(UiRaid.Spec);   // the RaidFrame fallback (kind 51) while the Social window has no Raid tab
                WowLog.Info("UI: interact hub up - Trade / Inspect / Raid windows, TRADE / DUEL_REQUESTED / DUEL_OUTOFBOUNDS / CONFIRM_SUMMON popups, " +
                            "raid + PvP frame icons, bag-use / drop-on-player / slash hooks (ours: drop-to-trade window " + DropTradeSeconds + " s).");
            }
            UiRaid.HookSocial();
            UiPickup.DropOnUnit.Remove(OnDroppedOnUnit);
            UiPickup.DropOnUnit.Add(OnDroppedOnUnit);
            UiTargetFrame.RaidIconOf = RaidIconOf;
            UiTargetFrame.PvpIconOf = UnitPvpArt;
            UiPlayerFrame.PvpIconOf = OwnPvpArt;
            UiPlayerFrame.PvpTimer = OwnPvpTimer;
            if (_raidHud == null) _raidHud = new UiRaidHud(transform);
        }

        private void OnDisable()
        {
            UiPickup.DropOnUnit.Remove(OnDroppedOnUnit);
            if (UiTargetFrame.RaidIconOf == (Func<ulong, int>)RaidIconOf) UiTargetFrame.RaidIconOf = null;
            if (UiTargetFrame.PvpIconOf == (Func<ulong, string>)UnitPvpArt) UiTargetFrame.PvpIconOf = null;
            if (UiPlayerFrame.PvpIconOf == (Func<string>)OwnPvpArt) UiPlayerFrame.PvpIconOf = null;
            if (UiPlayerFrame.PvpTimer == (Func<float>)OwnPvpTimer) UiPlayerFrame.PvpTimer = null;
            HidePopups();
        }

        private void OnDestroy()
        {
            if (_raidHud != null) { _raidHud.Destroy(); _raidHud = null; }
        }

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null)
            {
                if (_s != null) { HidePopups(); _s = null; }
                return;
            }
            if (s != _s) { HidePopups(); _s = s; _ownPvpIcon = -1; }
            IWowInteractions ix = s.Interactions;
            if (ix == null) return;
            bool vr = WowPlayer.XrPresent;
            Guard("trade popup", () => TradePopup(ix));
            Guard("duel popups", () => DuelPopups(ix));
            Guard("summon popup", () => SummonPopup(s, ix));
            Guard("drop to trade", () => DropToTrade(ix));
            Guard("pvp sound", () => PvpSound(s, ix));
            if (_raidHud != null) Guard("raid hud", () => _raidHud.Tick(s, vr));
        }

        private static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { if (Logged.Add(what)) WowLog.Warn("UI: interact " + what + " failed: " + e.Message + " (logged once)."); }
        }

        private void HidePopups()
        {
            if (_tradePopup != 0) UiPopup.Hide(_tradePopup);
            if (_duelPopup != 0) UiPopup.Hide(_duelPopup);
            if (_boundsPopup != 0) UiPopup.Hide(_boundsPopup);
            if (_summonPopup != 0) UiPopup.Hide(_summonPopup);
            _tradePopup = _duelPopup = _boundsPopup = _summonPopup = 0;
            _tradeFor = _duelFor = 0;
            _boundsText = _summonText = null;
            _summonFor = null;
        }

        /// <summary>The VR haptic for "something asks you" (ours, proposal-interact.md 6.3; GearXr honours Settings > Haptics).</summary>
        private static void Buzz()
        {
            if (!WowPlayer.XrPresent) return;
            GearXr.Pulse(true, 0.5f, 0.1f);
            GearXr.Pulse(false, 0.5f, 0.1f);
        }

        // ------------------------------------------------------------------ StaticPopup "TRADE" (StaticPopup.lua:1291-1302)

        private void TradePopup(IWowInteractions ix)
        {
            TradeView t = ix.Trade;
            bool incoming = t != null && t.State == TradeState.Incoming;
            if (!incoming)
            {
                _tradeSeen = 0f;
                if (_tradeFor != 0) { if (_tradePopup != 0) UiPopup.Hide(_tradePopup); _tradePopup = 0; _tradeFor = 0; }
                return;
            }
            if (_tradeFor == t.PartnerGuid) return;
            if (t.PartnerName == null)
            {
                if (_tradeSeen == 0f) _tradeSeen = Time.unscaledTime;
                if (Time.unscaledTime - _tradeSeen < NameWaitSeconds) return;
            }
            _tradeSeen = 0f;
            _tradeFor = t.PartnerGuid;
            ulong partner = t.PartnerGuid;
            _tradePopup = UiPopup.Show(new UiPopupSpec
            {
                Which = "TRADE",
                Text = "Trade with " + (t.PartnerName ?? "?") + "?",   // TRADE_WITH_QUESTION (GlobalStrings.lua:7651)
                Button1 = "Yes", Button2 = "No",                        // YES (:8982) / NO (:5181)
                OnAccept = () => { WowLog.Info("UI: TRADE popup - Yes."); Ix()?.AnswerTradeRequest(true); },    // BeginTrade()
                OnCancel = () => { WowLog.Info("UI: TRADE popup - No."); Ix()?.AnswerTradeRequest(false); },    // CancelTrade()
                KeepOpen = () => { IWowInteractions i = Ix(); return i != null && i.Trade.State == TradeState.Incoming && i.Trade.PartnerGuid == partner; },
                Timeout = 60f, HideOnEscape = true                      // timeout 60, hideOnEscape 1
            });
            Buzz();
        }

        // ------------------------------------------------------------------ "DUEL_REQUESTED" / "DUEL_OUTOFBOUNDS" (StaticPopup.lua:2156-2173)

        private void DuelPopups(IWowInteractions ix)
        {
            DuelView d = ix.Duel;
            bool incoming = d != null && d.State == DuelState.Incoming;
            if (!incoming)
            {
                _duelSeen = 0f;
                if (_duelFor != 0) { if (_duelPopup != 0) UiPopup.Hide(_duelPopup); _duelPopup = 0; _duelFor = 0; }
            }
            else if (_duelFor != d.OpponentGuid)
            {
                bool wait = false;
                if (d.OpponentName == null)
                {
                    if (_duelSeen == 0f) _duelSeen = Time.unscaledTime;
                    wait = Time.unscaledTime - _duelSeen < NameWaitSeconds;
                }
                if (!wait)
                {
                    _duelSeen = 0f;
                    _duelFor = d.OpponentGuid;
                    ulong flag = d.FlagGuid;
                    _duelPopup = UiPopup.Show(new UiPopupSpec
                    {
                        Which = "DUEL_REQUESTED",
                        Text = (d.OpponentName ?? "?") + " has challenged you to a duel.",   // DUEL_REQUESTED (GlobalStrings.lua:2029)
                        Button1 = "Accept", Button2 = "Decline",                              // ACCEPT (:11) / DECLINE (:1945)
                        Sound = "igPlayerInvite",
                        OnAccept = () => { WowLog.Info("UI: DUEL_REQUESTED popup - Accept."); Ix()?.AnswerDuel(true); },     // AcceptDuel()
                        OnCancel = () => { WowLog.Info("UI: DUEL_REQUESTED popup - Decline."); Ix()?.AnswerDuel(false); },   // CancelDuel()
                        KeepOpen = () => { IWowInteractions i = Ix(); return i != null && i.Duel.State == DuelState.Incoming && i.Duel.FlagGuid == flag; },
                        Timeout = 60f, HideOnEscape = true
                    });
                    Buzz();
                }
            }

            bool outside = d != null && d.State != DuelState.None && d.OutOfBounds;
            if (!outside)
            {
                if (_boundsText != null) { if (_boundsPopup != 0) UiPopup.Hide(_boundsPopup); _boundsPopup = 0; _boundsText = null; }
                return;
            }
            // DUEL_OUTOFBOUNDS_TIMER "Exiting duel area, you will forfeit in %d %s." (GlobalStrings.lua:2028), re-formatted as the time runs
            // (StaticPopup_OnUpdate :3307-3339: ceil(timeleft), < 60 -> SECONDS) - in place (UiPopup.SetText).
            string text = "Exiting duel area, you will forfeit in " + TimeLeft(d.SecondsLeft) + ".";
            if (text == _boundsText) return;
            bool first = _boundsText == null;
            _boundsText = text;
            if (!first && _boundsPopup != 0 && UiPopup.SetText(_boundsPopup, text)) return;   // duelsnd: StaticPopup_OnUpdate re-formats the timer in place (:3307-3339)
            _boundsPopup = UiPopup.Show(new UiPopupSpec
            {
                Which = "DUEL_OUTOFBOUNDS", Text = text,
                KeepOpen = () => { IWowInteractions i = Ix(); return i != null && i.Duel.State != DuelState.None && i.Duel.OutOfBounds; },
                HideOnEscape = false, NoCancelOnReuse = true, WhileDead = true   // no buttons, timeout 10 (the server forfeits; ours: hidden on DUEL_INBOUNDS / the end)
            });
            if (first) Buzz();
        }

        /// <summary>StaticPopup_OnUpdate's "%d %s" (StaticPopup.lua:3320-3339): seconds rounded up; under 60 "n Second(s)", else "n Minute(s)"
        /// (SECONDS "|4Second:Seconds;" :6200, MINUTES "|4Minute:Minutes;" :4952 - |4 picks the singular for 1).</summary>
        private static string TimeLeft(float seconds)
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if (t < 60) return t + (t == 1 ? " Second" : " Seconds");
            int m = Mathf.CeilToInt(t / 60f);
            return m + (m == 1 ? " Minute" : " Minutes");
        }

        // ------------------------------------------------------------------ "CONFIRM_SUMMON" (StaticPopup.lua:2534-2558)

        private void SummonPopup(IWowSession s, IWowInteractions ix)
        {
            SummonView v = ix.SummonRequest;
            if (v == null)
            {
                if (_summonFor != null) { if (_summonPopup != 0) UiPopup.Hide(_summonPopup); _summonPopup = 0; _summonFor = null; _summonText = null; }
                return;
            }
            // CONFIRM_SUMMON "%s wants to summon you to %s.  The spell will be cancelled in %d %s." (GlobalStrings.lua:1831)
            string text = (v.SummonerName ?? "?") + " wants to summon you to " + (string.IsNullOrEmpty(v.ZoneName) ? "?" : v.ZoneName) +
                          ".  The spell will be cancelled in " + TimeLeft(v.SecondsLeft) + ".";
            if (ReferenceEquals(v, _summonFor) && text == _summonText) return;
            if (ReferenceEquals(v, _summonFor) && _summonPopup != 0 && UiPopup.SetText(_summonPopup, text)) { _summonText = text; return; }   // duelsnd: the timer text in place (StaticPopup_OnUpdate :3307-3339)
            bool first = !ReferenceEquals(v, _summonFor);
            _summonFor = v;
            _summonText = text;
            SummonView mine = v;
            _summonPopup = UiPopup.Show(new UiPopupSpec
            {
                Which = "CONFIRM_SUMMON", Text = text,
                Button1 = "Accept", Button2 = "Cancel",                           // ACCEPT (:11) / CANCEL (:1338)
                Button1Enabled = _ => { PlayerState p = WowPlay.Session != null ? WowPlay.Session.Player : null; return p != null && !p.InCombat; },   // disabled in combat (OnUpdate :2547-2552)
                OnAccept = () => { WowLog.Info("UI: CONFIRM_SUMMON popup - Accept."); Ix()?.AnswerSummon(true); },     // ConfirmSummon()
                OnCancel = () => { WowLog.Info("UI: CONFIRM_SUMMON popup - Cancel."); Ix()?.AnswerSummon(false); },   // CancelSummon()
                KeepOpen = () => { IWowInteractions i = Ix(); return i != null && ReferenceEquals(i.SummonRequest, mine); },
                HideOnEscape = true, NoCancelOnReuse = true                       // timeout 0 (the time is the server's), hideOnEscape 1
            });
            if (first) Buzz();
        }

        private static IWowInteractions Ix()
        {
            IWowSession s = WowPlay.Session;
            return s != null ? s.Interactions : null;
        }

        // ------------------------------------------------------------------ unit-frame icons (uicore H6 draws them)

        private static int RaidIconOf(ulong guid)
        {
            IWowInteractions ix = Ix();
            return ix != null && guid != 0 ? ix.RaidTargetIconOf(guid) : 0;
        }

        private static string PvpArt(int icon)
        {
            switch (icon)
            {
                case 1: return PvpAlliance;
                case 2: return PvpHorde;
                case 3: return PvpFfa;
                default: return null;
            }
        }

        private static string UnitPvpArt(ulong guid)
        {
            IWowInteractions ix = Ix();
            return ix != null && guid != 0 ? PvpArt(ix.UnitPvpIcon(guid)) : null;
        }

        private static string OwnPvpArt()
        {
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null || s.Interactions == null) return null;
            return PvpArt(s.Interactions.UnitPvpIcon(s.Player.Guid));
        }

        private static float OwnPvpTimer()
        {
            IWowInteractions ix = Ix();
            PvpView v = ix != null ? ix.Pvp : null;
            return v != null && v.TimerRunning ? Mathf.Max(0f, v.TimerSecondsLeft) : -1f;
        }

        private void PvpSound(IWowSession s, IWowInteractions ix)
        {
            int icon = ix.UnitPvpIcon(s.Player.Guid);
            if (icon == _ownPvpIcon) return;
            bool appeared = _ownPvpIcon == 0 && icon != 0;   // not at login (-1): WoW plays it when the flag turns on (PlayerFrame.lua:89-91, :103-105)
            _ownPvpIcon = icon;
            if (appeared) UiSound.Named("igPVPUpdate");
        }

        // ------------------------------------------------------------------ bag items: UiBags.Use (H8) and drops on players

        /// <summary>UiBags.Use asks first (hook H8; bagIndex = index into PlayerState.Bags): the open trade window takes the item
        /// (UseContainerItem -> the first free trade slot, TradeFrame_GetAvailableSlot TF.lua:188-197); the Auctions tab through
        /// AuctionUseBagItem (queues). True = taken (the bag's normal use is skipped).</summary>
        public static bool TryUseBagItem(int bagIndex)
        {
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            IWowInteractions ix = s != null ? s.Interactions : null;
            if (p == null || ix == null || bagIndex < 0 || bagIndex >= p.Bags.Count) return false;
            if (ix.Trade != null && ix.Trade.State == TradeState.Open)
            {
                ItemView it = p.Bags[bagIndex];
                if (it == null || it.Bag < 0 || it.Slot < 0) return false;
                WowLog.Info("UI: " + (it.Name ?? "an item") + " offered to the trade (bag use).");
                ix.SetTradeItem(-1, it.Bag, it.Slot);
                return true;
            }
            if (UiAuction.PutInSellSlot(p.Bags[bagIndex])) return true;   // integrator (queues request): the Auctions tab takes the item (ClickAuctionSellItemButton)
            Func<int, bool> ah = AuctionUseBagItem;
            if (ah != null)
            {
                try { if (ah(bagIndex)) return true; }
                catch (Exception e) { WowLog.Warn("UI: the auction bag-use hook failed: " + e.Message); }
            }
            return false;
        }

        /// <summary>An item dropped on a unit in the world (UiPickup.DropOnUnit): a friendly player within 11.11 yd -> InitiateTrade and the item
        /// goes into the trade when it opens (the client's DropItemOnUnit C code - ours). True = consumed.</summary>
        private bool OnDroppedOnUnit(UiPickupPayload pay, ulong guid)
        {
            if (pay == null || pay.Kind != UiPickupKind.Item || pay.Bag < 0 || pay.Slot < 0) return false;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            IWowInteractions ix = s != null ? s.Interactions : null;
            if (p == null || ix == null || guid == p.Guid) return false;
            EntityState e = s.Find(guid);
            if (e == null || e.Kind != EntityKind.Player || e.Reaction != Reaction.Friendly) return false;
            float dx = e.Pos.X - p.Pos.X, dy = e.Pos.Y - p.Pos.Y, dz = e.Pos.Z - p.Pos.Z;
            if (Mathf.Sqrt(dx * dx + dy * dy + dz * dz) > TradeYards) return false;
            WowLog.Info("UI: " + (pay.Name ?? "an item") + " dropped on " + e.Name + " - trade requested, the item follows when it opens.");
            TradeView t = ix.Trade;
            if (t != null && t.State == TradeState.Open && t.PartnerGuid == guid) { ix.SetTradeItem(-1, pay.Bag, pay.Slot); return true; }
            _dropPartner = guid;
            _dropBag = pay.Bag;
            _dropSlot = pay.Slot;
            _dropAt = Time.unscaledTime;
            if (t == null || t.State == TradeState.None) ix.InitiateTrade(guid);
            return true;
        }

        private void DropToTrade(IWowInteractions ix)
        {
            if (_dropPartner == 0) return;
            if (Time.unscaledTime - _dropAt > DropTradeSeconds) { _dropPartner = 0; return; }
            TradeView t = ix.Trade;
            if (t == null || t.State != TradeState.Open || t.PartnerGuid != _dropPartner) return;
            ix.SetTradeItem(-1, _dropBag, _dropSlot);
            _dropPartner = 0;
        }

        // ------------------------------------------------------------------ slash commands (UiChat Submit, hook H9)

        /// <summary>UiChat Submit asks first (hook H9); cmd upper case with or without its '/', arg = the rest. True = handled.</summary>
        public static bool TrySlash(string cmd, string arg)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            string c = cmd.TrimStart('/').ToUpperInvariant();
            arg = arg != null ? arg.Trim() : "";
            IWowSession s = WowPlay.Session;
            IWowInteractions ix = s != null ? s.Interactions : null;
            switch (c)
            {
                case "DUEL": break;                                                  // SLASH_DUEL1 (GlobalStrings.lua:6464)
                case "YIELD": case "CONCEDE": case "FORFEIT": break;                 // SLASH_DUEL_CANCEL1-3 (:6466-6468)
                case "F": case "FOLLOW": case "FOL": break;                          // SLASH_FOLLOW1-3 (:6503-6505)
                case "INS": case "INSPECT": break;                                   // SLASH_INSPECT1-2 (:6587-6588)
                case "TR": case "TRADE": break;                                      // SLASH_TRADE1-2 (:6826-6827)
                case "READYCHECK": break;                                            // SLASH_READYCHECK1 (:6724)
                case "PVP": break;                                                   // SLASH_PVP1 (:6695)
                case "MT": case "MAINTANK": case "MTOFF": case "MAINTANKOFF": break;  // SLASH_MAINTANKON1-2 / OFF1-2 (:6652-6657)
                case "MA": case "MAINASSIST": case "MAOFF": case "MAINASSISTOFF": break;   // SLASH_MAINASSISTON1-2 / OFF1-2 (:6644-6649)
                case "CLEARMT": case "CLEARMAINTANK": case "CLEARMA": case "CLEARMAINASSIST": break;   // (:6445-6450)
                default: return false;
            }
            if (s == null || s.Player == null || ix == null) return true;
            WowLog.Info("UI: /" + c.ToLowerInvariant() + (arg.Length > 0 ? " " + arg : "") + " (chat).");
            switch (c)
            {
                case "YIELD": case "CONCEDE": case "FORFEIT": ix.ForfeitDuel(); return true;
                case "READYCHECK": ix.StartReadyCheck(); return true;
                case "PVP": ix.SetPvp(!(ix.Pvp != null && ix.Pvp.Desired)); return true;   // TogglePVP()
                case "CLEARMT": case "CLEARMAINTANK": ClearAssignment(s, ix, true); return true;
                case "CLEARMA": case "CLEARMAINASSIST": ClearAssignment(s, ix, false); return true;
            }
            ulong guid = Unit(s, arg);
            if (guid == 0) { Error("Unknown unit."); return true; }   // ERR_UNIT_NOT_FOUND (GlobalStrings.lua:3565)
            switch (c)
            {
                case "DUEL": ix.RequestDuel(guid); break;
                case "F": case "FOLLOW": case "FOL": WowFollow.Begin(guid); break;
                case "INS": case "INSPECT": UiInspect.OpenedForTarget = guid == s.TargetGuid; ix.InspectPlayer(guid); break;
                case "TR": case "TRADE": ix.InitiateTrade(guid); break;
                case "MT": case "MAINTANK": ix.SetMainTank(guid, true); break;
                case "MTOFF": case "MAINTANKOFF": ix.SetMainTank(guid, false); break;
                case "MA": case "MAINASSIST": ix.SetMainAssist(guid, true); break;
                case "MAOFF": case "MAINASSISTOFF": ix.SetMainAssist(guid, false); break;
            }
            return true;
        }

        /// <summary>/clearmt /clearma: ClearPartyAssignment for whoever holds it (one holder each, GroupHandler.cpp:728-758).</summary>
        private static void ClearAssignment(IWowSession s, IWowInteractions ix, bool tank)
        {
            RaidView r = ix.Raid;
            if (r == null || !r.IsRaid) { Error("You are not in a raid group"); return; }   // ERR_NOT_IN_RAID (GlobalStrings.lua:3312)
            ulong who = 0;
            if (tank ? r.IAmMainTank : r.IAmMainAssist) who = s.Player.Guid;
            for (int i = 0; i < r.Members.Count && who == 0; i++)
                if (tank ? r.Members[i].MainTank : r.Members[i].MainAssist) who = r.Members[i].Guid;
            if (who == 0) return;
            if (tank) ix.SetMainTank(who, false);
            else ix.SetMainAssist(who, false);
        }

        /// <summary>A slash command's unit: a player's name in view (any case), else (no name) the target.</summary>
        private static ulong Unit(IWowSession s, string name)
        {
            if (string.IsNullOrEmpty(name)) return s.TargetGuid;
            string n = name;
            int sp = n.IndexOf(' ');
            if (sp > 0) n = n.Substring(0, sp);
            if (string.Equals(n, s.Player.Name, StringComparison.OrdinalIgnoreCase)) return s.Player.Guid;
            IReadOnlyList<EntityState> all = s.Entities;
            for (int i = 0; i < all.Count; i++)
            {
                EntityState e = all[i];
                if (e != null && e.Kind == EntityKind.Player && string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase)) return e.Guid;
            }
            return 0;
        }

        private static void Error(string text)
        {
            WowPlay.Publish(new GameEvent { Type = GameEventType.Error, Text = text });
        }
    }
}
