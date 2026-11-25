// iteration 11 - owner interact: WoW's UnitPopup rules (pure). The designer's compiled draft P/harness/Probes/D11/interact/UnitMenuRulesDraft.cs
// (scenario-tested by D11InteractPure), copied as is; UiGroup renders what Build returns.
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Interact
{
    /// <summary>The UnitPopup menus this port draws (UnitPopupMenus, FX UnitPopup.lua:138-163) and the nested levels.</summary>
    public enum UnitMenu : byte
    {
        Self = 0, Player = 1, Party = 2, RaidPlayer = 3, Raid = 4, Target = 5,
        PvpFlag = 6, RaidIcon = 7, LootMethod = 8, LootThreshold = 9, DungeonDifficulty = 10, RaidDifficulty = 11
    }

    /// <summary>What a row does (UiGroup executes it). Values are append-only.</summary>
    public enum UnitMenuAct : byte
    {
        None = 0, Cancel, SetFocus, Whisper, Invite, Promote, PromoteGuide, VoteKick, Uninvite, Leave,
        LootMethodMenu, LootThresholdMenu, SetMethod, SetThreshold,
        Inspect, Trade, Follow, Duel, ForfeitDuel, PvpMenu, PvpOn, PvpOff, RaidIconMenu, SetRaidIcon,
        RaidLeader, RaidPromote, RaidDemote, RaidMainTank, RaidMainAssist, RaidRemove, ReportAfk,
        DungeonDiffMenu, RaidDiffMenu, SetDungeonDiff, SetRaidDiff, ResetInstances, RaidInfo, RaidWindow
    }

    public struct UnitMenuRow
    {
        public UnitMenuAct Act;
        public string Text;
        public bool Enabled, Checked;
        public int Value;          // SetMethod / SetThreshold / SetRaidIcon / SetDungeonDiff / SetRaidDiff
        public int Icon;           // 1..8 = a UI-RaidTargetingIcons cell drawn left of the text, 0 = none
        public float R, G, B;      // text colour (UnitPopup.lua:111-118 for the icons), 1 / 1 / 1 = default
    }

    /// <summary>Everything the rules read, filled by UiGroup from the session when the menu opens (and each refresh).</summary>
    public sealed class UnitMenuContext
    {
        public bool Online;                        // IWowInteractions.Available (online-only rows)
        public bool InParty, InRaid, IsLeader, IsAssistant, IsLfgGroup, InBattleground, InInstance;
        public bool SelfDead, SelfGhost, SelfFullControl = true;
        public int SelfLevel = 1;
        public bool IsSelf, IsPlayer, UnitFriendly, UnitAttackable, UnitDead, UnitOnline = true, UnitInBattleground;
        public bool UnitIsLeader, UnitIsAssistant, UnitIsMainTank, UnitIsMainAssist;
        public float Distance = float.MaxValue;    // yards from us to the unit
        public int UnitRaidIcon;                   // 0..8
        public int LootMethod = -1, LootThreshold = 2;
        public bool PvpDesired, Dueling, HasRaidTab;
        public int DungeonDifficulty, RaidDifficulty;   // 0-based (world's PlayerState fields)
        public bool WorldRows;                     // the world designer's rows exist (SetDungeonDifficulty ...)
        public bool CanInvite, InviteEnabled;      // UiGroup.CanInvite (the existing rule, UnitPopup.lua:494-500, :1055-1058)
    }

    /// <summary>
    /// WoW's UnitPopup rules (FX UnitPopup.lua: shown = UnitPopup_HideButtons :455-988, enabled = UnitPopup_OnUpdate :989-1163, checked
    /// :297-358) for the rows this port implements. Distances (UnitPopupButtons dist, CheckInteractDistance is C code) are the server's own:
    /// trade 11.11 yd (TRADE_DISTANCE), inspect / follow 28 yd (INSPECT_DISTANCE), duel 10 yd (spell 7266's range). Rows WoW has that this
    /// port does not (voice MUTE / UNMUTE / SILENCE, OPT_OUT_LOOT_TITLE, LOOT_PROMOTE, ACHIEVEMENTS, RAF_*) are not added (known gaps).
    /// Ours (marked): Forfeit Duel, Raid Window, Raid Info's place, the Cancel row of nested levels.
    /// </summary>
    public static class UnitMenuRules
    {
        public const float TradeYards = 11.11f, InspectYards = 28f, FollowYards = 28f, DuelYards = 10f;

        public static readonly string[] IconNames = { "Star", "Circle", "Diamond", "Triangle", "Moon", "Square", "Cross", "Skull" };   // RAID_TARGET_1..8 (GlobalStrings.lua:5987-5994)
        public static readonly float[] IconR = { 1f, 0.98f, 0.83f, 0.04f, 0.7f, 0f, 1f, 0.98f };                                     // UnitPopup.lua:111-118
        public static readonly float[] IconG = { 0.92f, 0.57f, 0.22f, 0.95f, 0.82f, 0.71f, 0.24f, 0.98f };
        public static readonly float[] IconB = { 0f, 0f, 0.9f, 0f, 0.875f, 1f, 0.168f, 0.98f };

        /// <summary>TargetFrameDropDown_Initialize (TargetFrame.lua:646-675): which menu a unit frame opens. (Our pet / a vehicle: the pets
        /// designer's menus - the caller checks those first.)</summary>
        public static UnitMenu MenuFor(bool isSelf, bool isPlayer, bool inOurRaid, bool inOurParty)
        {
            if (isSelf) return UnitMenu.Self;
            if (!isPlayer) return UnitMenu.Target;
            if (inOurRaid) return UnitMenu.RaidPlayer;
            if (inOurParty) return UnitMenu.Party;
            return UnitMenu.Player;
        }

        /// <summary>The menu's gold title: the unit's name; TARGET uses RAID_TARGET_ICON (TargetFrame.lua:669-670); nested levels their own.</summary>
        public static string Title(UnitMenu m, string unitName)
        {
            switch (m)
            {
                case UnitMenu.Target: return "Raid Target Icon";              // RAID_TARGET_ICON (GlobalStrings.lua:5995)
                case UnitMenu.PvpFlag: return "Player vs. Player";            // PVP_FLAG (:5761)
                case UnitMenu.RaidIcon: return "Raid Target Icon";
                case UnitMenu.LootMethod: return "Loot Method";               // LOOT_METHOD (:4807)
                case UnitMenu.LootThreshold: return "Loot Threshold";         // LOOT_THRESHOLD (:4844)
                case UnitMenu.DungeonDifficulty: return "Dungeon Difficulty"; // DUNGEON_DIFFICULTY (:2034)
                case UnitMenu.RaidDifficulty: return "Raid Difficulty";       // RAID_DIFFICULTY (:5949)
                default: return unitName ?? "";
            }
        }

        static void Add(List<UnitMenuRow> rows, UnitMenuAct act, string text, bool enabled, int value = 0, bool isChecked = false, int icon = 0)
        {
            var r = new UnitMenuRow { Act = act, Text = text, Enabled = enabled, Value = value, Checked = isChecked, Icon = icon, R = 1f, G = 1f, B = 1f };
            if (icon > 0) { r.R = IconR[icon - 1]; r.G = IconG[icon - 1]; r.B = IconB[icon - 1]; }
            rows.Add(r);
        }

        // ---- the shared rows -----------------------------------------------------------------------------------------------
        static bool CanCoop(UnitMenuContext c) { return c.IsPlayer && c.UnitFriendly && !c.IsSelf; }
        static bool DeadOrGhost(UnitMenuContext c) { return c.SelfDead || c.SelfGhost; }

        static void Interactions(List<UnitMenuRow> rows, UnitMenuContext c)
        {
            if (!c.Online) return;
            if (!c.UnitAttackable) Add(rows, UnitMenuAct.Inspect, "Inspect", !DeadOrGhost(c) && c.Distance <= InspectYards);                     // INSPECT :4241; :540-543, :1083-1086
            if (CanCoop(c)) Add(rows, UnitMenuAct.Trade, "Trade", !DeadOrGhost(c) && c.SelfFullControl && !c.UnitDead && c.Distance <= TradeYards); // TRADE :7639; :490-493, :1047-1050
            if (CanCoop(c)) Add(rows, UnitMenuAct.Follow, "Follow", !c.SelfDead && c.Distance <= FollowYards);                                     // FOLLOW :3716; :515-518, :1087-1090
            if (!c.UnitAttackable) Add(rows, UnitMenuAct.Duel, "Duel", !DeadOrGhost(c) && c.SelfFullControl && !c.UnitDead && c.Distance <= DuelYards); // DUEL :2026; :536-539, :1091-1094
        }

        static void RaidIconRow(List<UnitMenuRow> rows) { Add(rows, UnitMenuAct.RaidIconMenu, "Raid Target Icon", true); }   // RAID_TARGET_ICON (no hide rule)

        static void ReportAfk(List<UnitMenuRow> rows, UnitMenuContext c)
        {
            if (c.Online && c.InBattleground && !c.IsSelf && c.UnitInBattleground) Add(rows, UnitMenuAct.ReportAfk, "Report player Away", true);   // PVP_REPORT_AFK :5857; :769-785
        }

        static void RaidRankRows(List<UnitMenuRow> rows, UnitMenuContext c, bool roster)
        {
            bool lead = c.IsLeader, assist = c.IsAssistant;
            if (lead && !c.UnitIsLeader) Add(rows, UnitMenuAct.RaidLeader, "Promote to Raid Leader", c.UnitOnline);              // SET_RAID_LEADER :6233; :725-728
            if (lead && !c.UnitIsAssistant) Add(rows, UnitMenuAct.RaidPromote, "Promote to Assistant", true);                      // SET_RAID_ASSISTANT :6232; :729-736
            if (roster)
            {
                if ((lead || assist) && !c.UnitIsMainTank) Add(rows, UnitMenuAct.RaidMainTank, "Promote to Main Tank", true);      // SET_MAIN_TANK :6230; :749-754
                if ((lead || assist) && !c.UnitIsMainAssist) Add(rows, UnitMenuAct.RaidMainAssist, "Promote to Main Assist", true); // SET_MAIN_ASSIST :6229; :755-760
            }
            // RAID_DEMOTE (:737-748)
            bool demote = lead || assist;
            if (demote && !c.UnitIsMainTank && !c.UnitIsMainAssist)
            {
                if (!lead && assist && c.UnitIsAssistant) demote = false;
                else if (c.UnitIsLeader || !c.UnitIsAssistant) demote = false;
            }
            if (demote) Add(rows, UnitMenuAct.RaidDemote, "Demote", true);                                                           // DEMOTE :1974
            // RAID_REMOVE (:761-768)
            bool remove = lead || assist;
            if (!lead && assist && c.UnitIsAssistant) remove = false;
            if (lead && c.IsSelf) remove = false;
            if (remove) Add(rows, UnitMenuAct.RaidRemove, "Remove", true);                                                           // REMOVE :6043
        }

        /// <summary>The rows of one menu level, Cancel last (CANCEL :1338).</summary>
        public static void Build(UnitMenu m, UnitMenuContext c, List<UnitMenuRow> rows)
        {
            rows.Clear();
            bool inParty = c.InParty, lead = c.IsLeader;
            switch (m)
            {
                case UnitMenu.Self:
                    Add(rows, UnitMenuAct.SetFocus, "Set Focus", true);                                                              // SET_FOCUS :6224
                    if (c.Online) Add(rows, UnitMenuAct.PvpMenu, "Player vs. Player", true);                                          // PVP_FLAG :5761
                    if (inParty)
                    {
                        Add(rows, UnitMenuAct.LootMethodMenu, LootLabel(c.LootMethod), lead && !c.IsLfgGroup);                        // LOOT_METHOD; :662-664, :1105-1108
                        Add(rows, UnitMenuAct.LootThresholdMenu, QualityName(c.LootThreshold), lead);                                 // LOOT_THRESHOLD; :691-693
                    }
                    if (c.Online && c.WorldRows)
                    {
                        if (!(c.SelfLevel < 65 && c.DungeonDifficulty == 0)) Add(rows, UnitMenuAct.DungeonDiffMenu, "Dungeon Difficulty", !c.InInstance);   // :699-702, :1118-1119
                        if (!(c.SelfLevel < 65 && c.RaidDifficulty == 0)) Add(rows, UnitMenuAct.RaidDiffMenu, "Raid Difficulty", !c.InInstance);            // :703-706, :1124-1125
                        if (!((inParty && !lead) || c.InInstance)) Add(rows, UnitMenuAct.ResetInstances, "Reset all instances", true);                    // RESET_INSTANCES :6091; :695-698
                        Add(rows, UnitMenuAct.RaidInfo, "Raid Info", true);                                                           // RAID_INFO :5961 (ours in this menu - world)
                    }
                    RaidIconRow(rows);
                    if (c.Online && c.Dueling) Add(rows, UnitMenuAct.ForfeitDuel, "Forfeit Duel", true);                               // ours (/forfeit, SLASH_DUEL_CANCEL3)
                    if (inParty) Add(rows, UnitMenuAct.Leave, "Leave party", true);                                                   // PARTY_LEAVE :5578; :638-641
                    if (c.Online && !c.HasRaidTab && inParty) Add(rows, UnitMenuAct.RaidWindow, "Raid", true);                         // ours (RAID "Raid" GlobalStrings.lua:5940, the tab's name) - only without the Social window's Raid tab
                    break;
                case UnitMenu.Player:
                    Add(rows, UnitMenuAct.SetFocus, "Set Focus", true);
                    if (CanCoop(c)) Add(rows, UnitMenuAct.Whisper, "Whisper", true);                                                  // WHISPER :8932; :519-523
                    if (c.Online && !c.UnitAttackable) Add(rows, UnitMenuAct.Inspect, "Inspect", !DeadOrGhost(c) && c.Distance <= InspectYards);
                    if (c.CanInvite) Add(rows, UnitMenuAct.Invite, "Invite", c.InviteEnabled);                                        // PARTY_INVITE :5576
                    if (c.Online && CanCoop(c)) Add(rows, UnitMenuAct.Trade, "Trade", !DeadOrGhost(c) && c.SelfFullControl && !c.UnitDead && c.Distance <= TradeYards);
                    if (c.Online && CanCoop(c)) Add(rows, UnitMenuAct.Follow, "Follow", !c.SelfDead && c.Distance <= FollowYards);
                    if (c.Online && !c.UnitAttackable) Add(rows, UnitMenuAct.Duel, "Duel", !DeadOrGhost(c) && c.SelfFullControl && !c.UnitDead && c.Distance <= DuelYards);
                    RaidIconRow(rows);
                    break;
                case UnitMenu.Party:
                    Add(rows, UnitMenuAct.SetFocus, "Set Focus", true);
                    Add(rows, UnitMenuAct.Whisper, "Whisper", c.UnitOnline);                                                          // :1075-1077
                    if (inParty && lead && !c.IsLfgGroup) Add(rows, UnitMenuAct.Promote, "Promote to Leader", c.UnitOnline);          // PARTY_PROMOTE :5581; :598-601, :1071-1072
                    if (inParty && lead && c.IsLfgGroup) Add(rows, UnitMenuAct.PromoteGuide, "Promote to Guide", c.UnitOnline);       // PARTY_PROMOTE_GUIDE :5582; :602-605
                    if (c.Online && inParty && !c.InBattleground && c.IsLfgGroup) Add(rows, UnitMenuAct.VoteKick, "Vote to Kick", true); // VOTE_TO_KICK :8912; :634-637, :1067-1070
                    if (inParty && lead && !c.InBattleground && !c.IsLfgGroup) Add(rows, UnitMenuAct.Uninvite, "Uninvite", true);     // PARTY_UNINVITE :5586; :630-633
                    Interactions(rows, c);
                    RaidIconRow(rows);
                    ReportAfk(rows, c);
                    break;
                case UnitMenu.RaidPlayer:
                    Add(rows, UnitMenuAct.SetFocus, "Set Focus", true);
                    Add(rows, UnitMenuAct.Whisper, "Whisper", c.UnitOnline);
                    Interactions(rows, c);
                    RaidIconRow(rows);
                    RaidRankRows(rows, c, false);
                    ReportAfk(rows, c);
                    break;
                case UnitMenu.Raid:
                    Add(rows, UnitMenuAct.SetFocus, "Set Focus", true);
                    RaidRankRows(rows, c, true);
                    ReportAfk(rows, c);
                    break;
                case UnitMenu.Target:
                    Add(rows, UnitMenuAct.SetFocus, "Set Focus", true);
                    RaidIconRow(rows);
                    break;
                case UnitMenu.PvpFlag:
                    Add(rows, UnitMenuAct.PvpOn, "Enable", true, 1, c.PvpDesired);                                                    // ENABLE :2770; checked GetPVPDesired() == 1 (:351-354)
                    Add(rows, UnitMenuAct.PvpOff, "Disable", true, 0, !c.PvpDesired);                                                 // DISABLE :1981; (:355-358)
                    break;
                case UnitMenu.RaidIcon:
                    for (int i = 1; i <= 8; i++) Add(rows, UnitMenuAct.SetRaidIcon, IconNames[i - 1], true, i, c.UnitRaidIcon == i, i); // RAID_TARGET_1..8; checked (:303-307)
                    Add(rows, UnitMenuAct.SetRaidIcon, "None", true, 0);                                                              // RAID_TARGET_NONE :5996
                    break;
                case UnitMenu.LootMethod:
                    for (int i = 0; i < LootLabels.Length; i++) Add(rows, UnitMenuAct.SetMethod, LootLabels[i], lead, i, c.LootMethod == i);
                    break;
                case UnitMenu.LootThreshold:
                    for (int q = 2; q <= 4; q++) Add(rows, UnitMenuAct.SetThreshold, QualityName(q), lead, q, c.LootThreshold == q);  // UnitPopupMenus["LOOT_THRESHOLD"] (:160)
                    break;
                case UnitMenu.DungeonDifficulty:
                    {
                        bool can = !((inParty && !lead) || c.InInstance);                                                             // :312-325
                        Add(rows, UnitMenuAct.SetDungeonDiff, "5 Player", can, 0, c.DungeonDifficulty == 0);                         // DUNGEON_DIFFICULTY1 :2035
                        Add(rows, UnitMenuAct.SetDungeonDiff, "5 Player (Heroic)", can, 1, c.DungeonDifficulty == 1);                // DUNGEON_DIFFICULTY2 :2036
                    }
                    break;
                case UnitMenu.RaidDifficulty:
                    {
                        bool can = !((inParty && !lead) || c.InInstance);
                        string[] names = { "10 Player", "25 Player", "10 Player (Heroic)", "25 Player (Heroic)" };                   // RAID_DIFFICULTY1..4 :5950-5953
                        for (int i = 0; i < 4; i++) Add(rows, UnitMenuAct.SetRaidDiff, names[i], can, i, c.RaidDifficulty == i);
                    }
                    break;
            }
            Add(rows, UnitMenuAct.Cancel, "Cancel", true);                                                                            // CANCEL :1338
        }

        // LOOT_FREE_FOR_ALL ... LOOT_NEED_BEFORE_GREED (GlobalStrings.lua:4794-4810) - the existing UiGroup labels, kept.
        public static readonly string[] LootLabels = { "Loot: Free for All", "Loot: Round Robin", "Loot: Master Looter", "Loot: Group Loot", "Loot: Need Before Greed" };
        static readonly string[] QualityNames = { "Poor", "Common", "Uncommon", "Rare", "Epic", "Legendary", "Artifact" };          // ITEM_QUALITYn_DESC (:4490-4496)
        public static string LootLabel(int method) { return method >= 0 && method < LootLabels.Length ? LootLabels[method] : "Loot Method"; }
        public static string QualityName(int q) { return q >= 0 && q < QualityNames.Length ? QualityNames[q] : "Loot Threshold"; }
    }
}
