using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Stage S1 (owner party, PLAN10 s2) + iteration 11 (owner interact, proposal-interact.md 6.3.1): WoW's group UI around the session's
    /// groups and WoW's unit menus, desktop and VR. One component on the WoW Game object (WowGame.StartDreamport adds it - whitelisted
    /// players only; visitors never run the game).
    /// - The invite popup = StaticPopup "PARTY_INVITE" (StaticPopup.lua:1304-1330): INVITATION "%s invites you to a group."
    ///   (GlobalStrings.lua:4296), ACCEPT / DECLINE (:11 / :1945), sound "igPlayerInvite", timeout = 60 (hidden = DeclineGroup, OnHide),
    ///   whileDead. Desktop: StaticPopup1's place (TOP, 0, -135), below the death popup while that one is up; VR: a world panel in
    ///   front of the eyes when it opens (placed once, yaw only - it never follows the head), pointed at / poked like the other panels.
    /// - The unit menu = UnitPopup's dropdown (UnitPopup.lua), its rows and rules in the pure Play/Interact/UnitMenuRules.cs: SELF (your own
    ///   frame), PLAYER, PARTY, RAID_PLAYER (a raid member's frame), RAID (the Raid tab roster) and TARGET (NPCs / creatures: Set Focus + Raid
    ///   Target Icon), nested PvP flag / raid target icon / loot / difficulty levels in place. Desktop: at the mouse, like UIDropDownMenu;
    ///   opened by the right-clicks of the target / focus / player / party frames (UiTargetFrame.ContextClick = OpenUnitMenu) and the Raid
    ///   tab (OpenRaidMenu). VR: the palm "Party" button (OpenPartyMenu) - the target's menu, else your own; a world panel in front of you.
    /// - Every frame after WowGear (order 10010): <see cref="WowParty.Merge"/> (the party frames' data) and the portrait loads (UiPortraits).
    /// </summary>
    [DefaultExecutionOrder(10010)]
    public sealed class UiGroup : MonoBehaviour
    {
        public const float InviteTimeout = 60f;           // StaticPopupDialogs["PARTY_INVITE"].timeout = 60 (StaticPopup.lua:1325)
        public const float MenuTimeout = 30f;             // ours: an untouched menu closes by itself
        public const float VrPopupDistance = 1.1f, VrPopupDrop = 0.1f, VrMenuDistance = 0.9f, VrMenuDrop = 0.2f;   // ours (the death popup: 1.1 m, -0.1)
        public const float VrMetresPerUnit = 0.0014f;     // the death popup's scale (UiLayouts "WoW Popup")
        private const float PopupW = 320f, ButtonW = 128f, ButtonH = 21f;   // StaticPopup.xml: 320 wide, StaticPopupButtons 128 x 21
        private const float MenuW = 200f, RowH = 16f, MenuPad = 14f;        // ours: UIDropDownMenu-like rows (UIDROPDOWNMENU_BUTTON_HEIGHT 16)
        private const int MaxRows = 16;                                     // iteration 11: the longest menu (RAID_PLAYER of a raid leader) has 13 rows + Cancel
        private const float RollH = 100f, RollButtonW = 66f;                // ours: GroupLootFrame's choices in StaticPopup's look
        private const float IconSize = 16f, IconShift = 20f;               // ours: the raid target icon cell left of the text (UIDropDownMenu's icon place)
        private const string RaidIconsArt = @"Interface\TargetingFrame\UI-RaidTargetingIcons.blp";   // 256 x 256, 4 x 4 cells (TargetFrame.lua:692-701)

        public static UiGroup Current { get; private set; }

        /// <summary>Iteration 11 (interact, an optional hook for the pets builder): opens the PET menu for our pet (Player.PetGuid); null =
        /// our pet gets the TARGET menu (Set Focus, Raid Target Icon).</summary>
        public static Action<ulong> PetMenu;

        /// <summary>Iteration 11 (uicore's Escape chain, H6): closes the open unit menu, true if one was open.</summary>
        public static bool CloseMenus()
        {
            UiGroup g = Current;
            if (g == null || !g._menuOpen) return false;
            g.CloseMenu("Escape");
            return true;
        }

        /// <summary>True while the invite popup or a menu is shown (other views: the pointer is ours).</summary>
        public static bool Shown { get; private set; }

        private sealed class RollPopup
        {
            public RectTransform Root;
            public UiBackdrop Back;
            public RawImage Icon;
            public TextMeshProUGUI Text;
            public UiButton Need, Greed, Disenchant, Pass;
            // spellui (2026-10-04): GroupLootFrame's item parts (LootFrame.xml:259-381)
            public UiButton Item;                 // $parentIconFrame: hover = the item's tooltip (desktop mouse / VR laser)
            public TextMeshProUGUI Name, Count;   // $parentName (quality colour), $parentIconFrameCount (shown above 1)
            public bool IconLoaded;
            public string IconPath;
        }

        private sealed class Popup
        {
            public RectTransform Root;
            public UiBackdrop Back;
            public TextMeshProUGUI Text;
            public UiButton Accept, Decline;
            public float H = 100f;
        }

        private sealed class Menu
        {
            public RectTransform Root;
            public UiBackdrop Back;
            public TextMeshProUGUI Title;
            public readonly UiButton[] Rows = new UiButton[MaxRows];
            public readonly RawImage[] Icons = new RawImage[MaxRows];
            public readonly UnitMenuRow[] Data = new UnitMenuRow[MaxRows];
            public float H;
        }

        private IWowSession _s;
        private bool _built, _vr;
        private UiSurface _desk, _vrPopupSurface, _vrMenuSurface;
        private Transform _vrRoot;
        private Popup _deskPopup, _vrPopup;
        private Menu _deskMenu, _vrMenu;
        private string _inviteShown;
        private float _inviteAt, _menuAt;
        private UnitMenu _menuKind, _menuBase;
        private ulong _menuGuid;
        private string _menuName;
        private bool _menuOpen, _menuVr, _menuPlaced, _popupVr, _menuFromPalm;
        private RollPopup _deskRoll, _vrRoll;
        private UiSurface _vrRollSurface;
        private LootRollView _rollShown;
        private bool _rollVr;
        private int _rollSecs = -1;
        private string _rollName;
        private float _lastPortraitLog, _reserved = -1f;
        private readonly UnitMenuContext _ctx = new UnitMenuContext();
        private readonly List<UnitMenuRow> _rows = new List<UnitMenuRow>();

        // ------------------------------------------------------------------ API for the HUD (desktop right-clicks, the VR palm menu, the Raid tab)

        /// <summary>WoW's unit menu (UnitPopup) for this unit: your own guid = SELF, a raid member = RAID_PLAYER, a party member = PARTY,
        /// another player = PLAYER, an NPC / creature = TARGET. Desktop: at the mouse; VR: in front of the eyes.</summary>
        public static void OpenUnitMenu(ulong guid)
        {
            UiGroup g = Current;
            if (g == null) return;
            g.Open(guid, false, false);
        }

        /// <summary>A Dreamport addition for VR (no right-click): the unit menu of your target (a player, a member, an NPC / creature), else
        /// your own.</summary>
        public static void OpenPartyMenu()
        {
            UiGroup g = Current;
            if (g == null) return;
            g.Open(0, true, false);
        }

        /// <summary>Iteration 11: the RAID menu of a member of the Raid tab roster / a raid pullout (UnitPopupMenus["RAID"]).</summary>
        public static void OpenRaidMenu(ulong guid)
        {
            UiGroup g = Current;
            if (g == null) return;
            g.Open(guid, false, true);
        }

        /// <summary>Can this unit be invited now (UnitPopup's INVITE rules)? name = the name to invite; enabled = false in a party you do not lead.</summary>
        public static bool CanInvite(IWowSession s, ulong guid, out string name, out bool enabled)
        {
            name = null;
            enabled = false;
            if (s == null || s.Player == null || guid == 0 || guid == s.Player.Guid) return false;
            EntityState e = s.Find(guid);
            if (e == null || e.Kind != EntityKind.Player || string.IsNullOrEmpty(e.Name)) return false;
            if (IsMember(s.Player, guid)) return false;
            // UnitPopup.lua:497 "canCoop == 0 ... hidden": UnitCanCooperate = the same side (friendly).
            if (e.Reaction != Reaction.Friendly) return false;
            name = e.Name;
            // UnitPopup.lua:1056 "inParty == 1 and (isLeader == 0 and isAssistant == 0) -> disabled".
            RaidView r = s.Interactions != null ? s.Interactions.Raid : null;
            enabled = s.Player.Group.Count == 0 || s.Player.GroupLeaderGuid == s.Player.Guid || (r != null && r.IsRaid && r.IAmAssistant);
            return true;
        }

        private static bool IsMember(PlayerState p, ulong guid)
        {
            for (int i = 0; i < p.Group.Count; i++) if (p.Group[i].Guid == guid) return true;
            return false;
        }

        // ------------------------------------------------------------------ lifetime

        private void OnEnable()
        {
            Current = this;
            // The HUD's hook (UiHud.cs UiTargetFrame.ContextClick): a right-click on the desktop target / player frame opens WoW's unit menu.
            UiTargetFrame.ContextClick = OpenUnitMenu;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (UiTargetFrame.ContextClick == (Action<ulong>)OpenUnitMenu) UiTargetFrame.ContextClick = null;
            HideAll();
            UiCursor.Free(this, false);
            UiPopup.Reserve(this, 0f);
            _reserved = -1f;
            Shown = false;
        }

        private void OnDestroy()
        {
            UiPortraits.Clear();
            WowParty.Reset();
            if (_desk != null && _desk.Canvas != null) Destroy(_desk.Canvas.gameObject);
            if (_vrRoot != null) Destroy(_vrRoot.gameObject);
        }

        private void LateUpdate()
        {
            try { WowParty.Merge(WowPlay.Session); }
            catch (Exception e) { LogOnce("party merge", e); }
        }

        private void Update()
        {
            try { UiPortraits.Pump(); }
            catch (Exception e) { LogOnce("portraits", e); }
            float now = Time.unscaledTime;
            if (now - _lastPortraitLog > 120f) { _lastPortraitLog = now; if (WowParty.Members.Count > 0) WowLog.Info("Party: " + WowParty.Members.Count + " member(s) shown, " + UiPortraits.Stats + "."); }

            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null)
            {
                if (_s != null) { HideAll(); _s = null; }
                UiCursor.Free(this, false);
                Reserve(0f);
                Shown = false;
                return;
            }
            if (s != _s) { HideAll(); _s = s; }
            try
            {
                _vr = WowPlayer.XrPresent;
                RefreshInvite(s, now);
                RefreshMenu(s, now);
                RefreshRoll(s);
            }
            catch (Exception e) { LogOnce("group UI", e); }
            bool popup = _inviteShown != null || _rollShown != null;
            Shown = popup || _menuOpen;
            UiCursor.Free(this, !_vr && Shown);   // uicore H4: one owner set for the desktop cursor
            // uicore H6: our own desktop popups push UiPopup's stack down while shown
            float h = 0f;
            if (!_vr)
            {
                if (_inviteShown != null && _deskPopup != null) h += _deskPopup.H + 4f;
                if (_rollShown != null) h += RollH + 4f;
            }
            Reserve(h);
        }

        private void Reserve(float h)
        {
            if (Mathf.Approximately(h, _reserved)) return;
            _reserved = h;
            UiPopup.Reserve(this, h);
        }

        // ------------------------------------------------------------------ the invite popup

        private void RefreshInvite(IWowSession s, float now)
        {
            PlayerState p = s.Player;
            string from = p.PendingInviteFrom;
            if (from == null)
            {
                if (_inviteShown != null) { WowLog.Info("UI: group invite popup closed."); HidePopup(); }
                return;
            }
            if (from != _inviteShown || _vr != _popupVr)   // a new invite, or the headset put on / taken off: shown again on the right surface
            {
                Build();
                bool fresh = from != _inviteShown;
                if (fresh) _inviteAt = now;
                _inviteShown = from;
                _popupVr = _vr;
                Popup pop = _vr ? _vrPopup : _deskPopup;
                LayoutPopup(pop, "%s invites you to a group.".Replace("%s", from));   // INVITATION (GlobalStrings.lua:4296)
                if (_vr)
                {
                    _deskPopup.Root.gameObject.SetActive(false);
                    ShowVr(_vrPopupSurface, true);
                    PlaceInFront(_vrPopupSurface, VrPopupDistance, -VrPopupDrop, p.Dead || p.Ghost ? 0.4f : 0f);
                }
                else
                {
                    ShowVr(_vrPopupSurface, false);
                    // StaticPopup1 at TOP (0, -135); below the death popup (StaticPopup2's place, ours) while that one is up.
                    RectTransform r = (RectTransform)_deskPopup.Root.parent;
                    r.anchoredPosition = new Vector2(0f, p.Dead || p.Ghost ? -260f : -135f);
                    _deskPopup.Root.gameObject.SetActive(true);
                }
                if (fresh) UiSound.Added(UiPopup.OpenSound, "StaticPopup_OnShow, StaticPopup.lua:3428 (PARTY_INVITE)");   // duelsnd: the StaticPopup frame's OnShow first
                if (fresh) { try { WowAudio.PlayUiNamed("igPlayerInvite"); } catch (Exception) { } }   // StaticPopupDialogs["PARTY_INVITE"].sound
                WowLog.Info("UI: group invite popup (StaticPopup PARTY_INVITE) for " + from + " - Accept / Decline, declines itself after " + InviteTimeout.ToString("0") + " s" + (_vr ? " (VR panel in front of you)" : "") + ".");
            }
            if (now - _inviteAt > InviteTimeout)
            {
                WowLog.Info("UI: the group invite from " + from + " timed out after " + InviteTimeout.ToString("0") + " s - declined (StaticPopup's OnHide -> DeclineGroup).");
                s.AnswerGroupInvite(false);
                HidePopup();
            }
        }

        private void Answer(bool accept)
        {
            IWowSession s = WowPlay.Session;
            string from = s != null && s.Player != null ? s.Player.PendingInviteFrom : null;
            WowLog.Info("UI: group invite " + (accept ? "accepted" : "declined") + " (" + (from ?? "no invite pending") + ").");
            if (s != null && from != null) s.AnswerGroupInvite(accept);
            HidePopup();
        }

        private void HidePopup()
        {
            if (_inviteShown != null) UiSound.Added(UiPopup.CloseSound, "StaticPopup_OnHide, StaticPopup.lua:3445 (PARTY_INVITE)");   // duelsnd
            _inviteShown = null;
            if (_deskPopup != null) _deskPopup.Root.gameObject.SetActive(false);
            ShowVr(_vrPopupSurface, false);
        }

        // ------------------------------------------------------------------ the unit menu

        private void Open(ulong guid, bool palm, bool raidRoster)
        {
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null) return;
            PlayerState p = s.Player;
            if (palm)
            {
                // The VR palm "Party" button (ours): the target's menu (a player, a member, an NPC / creature), else your own.
                EntityState t = s.TargetGuid != 0 ? s.Find(s.TargetGuid) : null;
                guid = (t != null && t.Kind != EntityKind.GameObject) || IsMember(p, s.TargetGuid) ? s.TargetGuid : p.Guid;
            }
            if (guid == 0) return;
            UnitMenu kind;
            string name;
            bool member = IsMember(p, guid);
            EntityState e = s.Find(guid);
            if (raidRoster)
            {
                kind = UnitMenu.Raid;
                name = guid == p.Guid ? p.Name : MemberName(p, guid) ?? (e != null ? e.Name : null);
            }
            else if (guid == p.Guid) { kind = UnitMenu.Self; name = p.Name; }
            else
            {
                if (guid == p.PetGuid && p.PetGuid != 0 && PetMenu != null)
                {
                    WowLog.Info("UI: unit menu for our pet - the pets UI's PET menu.");
                    PetMenu(guid);
                    return;
                }
                bool isPlayer = member || (e != null && e.Kind == EntityKind.Player);
                if (!isPlayer && (e == null || e.Kind == EntityKind.GameObject)) { WowLog.Info("UI: no unit menu for " + (e != null ? e.Name + " (" + e.Kind + ")" : "nothing") + "."); return; }
                RaidView r = s.Interactions != null ? s.Interactions.Raid : null;
                kind = UnitMenuRules.MenuFor(false, isPlayer, member && r != null && r.IsRaid, member);
                name = member ? MemberName(p, guid) : null;
                if (string.IsNullOrEmpty(name) && e != null) name = e.Name;
            }
            Build();
            _menuKind = kind;
            _menuBase = kind;
            _menuGuid = guid;
            _menuName = name;
            _menuOpen = true;
            _menuVr = _vr;
            _menuPlaced = false;
            _menuFromPalm = palm;
            _menuAt = Time.unscaledTime;
            FillMenu(s);
            if (_vr)
            {
                _deskMenu.Root.gameObject.SetActive(false);
                ShowVr(_vrMenuSurface, true);
            }
            else
            {
                ShowVr(_vrMenuSurface, false);
                PlaceAtMouse(_deskMenu);
                _deskMenu.Root.gameObject.SetActive(true);
            }
            WowLog.Info("UI: unit menu '" + kind + "' for " + (name ?? "?") + " (" + (_vr ? "VR" : "desktop") + ", " + _rows.Count + " rows).");
        }

        private static string MemberName(PlayerState p, ulong guid)
        {
            for (int i = 0; i < p.Group.Count; i++) if (p.Group[i].Guid == guid) return p.Group[i].Name;
            return null;
        }

        private static GroupMemberView MemberOf(PlayerState p, ulong guid)
        {
            for (int i = 0; i < p.Group.Count; i++) if (p.Group[i].Guid == guid) return p.Group[i];
            return null;
        }

        // ours: the current choice marked (UIDropDownMenu shows a radio check there)
        private static string Marked(string label, bool on) { return on ? label + " <color=#ffd100>(current)</color>" : label; }

        /// <summary>UnitMenuRules' context from the session (the names of proposal-interact.md 6.3.1).</summary>
        private void FillContext(IWowSession s)
        {
            PlayerState p = s.Player;
            IWowInteractions ix = s.Interactions;
            RaidView r = ix != null ? ix.Raid : null;
            UnitMenuContext c = _ctx;
            ulong guid = _menuGuid;
            EntityState e = guid != 0 && guid != p.Guid ? s.Find(guid) : null;
            GroupMemberView m = MemberOf(p, guid);
            RaidMemberView rm = r != null ? r.Find(guid) : null;
            c.Online = ix != null && ix.Available;
            c.InParty = p.Group.Count > 0;
            c.InRaid = r != null && r.IsRaid;
            c.IsLeader = c.InParty && p.GroupLeaderGuid == p.Guid;
            c.IsAssistant = r != null && r.IAmAssistant;
            c.IsLfgGroup = r != null && r.IsLfgGroup;
            c.InBattleground = (r != null && r.IsBattlegroundGroup) || p.MapInstanceType == 3;
            c.InInstance = p.MapInstanceType != 0;
            c.SelfDead = p.Dead;
            c.SelfGhost = p.Ghost;
            c.SelfFullControl = !p.OnTaxi && !p.ControlLost;   // HasFullControl (ours: a taxi or a lost control)
            c.SelfLevel = p.Level;
            c.IsSelf = guid == p.Guid;
            c.IsPlayer = c.IsSelf || m != null || (e != null && e.Kind == EntityKind.Player);
            c.UnitFriendly = c.IsSelf || m != null || (e != null && e.Reaction == Reaction.Friendly);
            c.UnitAttackable = e != null && e.Reaction == Reaction.Hostile;
            c.UnitDead = e != null ? e.Dead : m != null && (m.Dead || m.Ghost);
            c.UnitOnline = m == null || m.Online;
            c.UnitInBattleground = c.InBattleground;
            c.UnitIsLeader = guid != 0 && guid == p.GroupLeaderGuid;
            c.UnitIsAssistant = c.IsSelf ? r != null && r.IAmAssistant : rm != null && rm.Assistant;
            c.UnitIsMainTank = c.IsSelf ? r != null && r.IAmMainTank : rm != null && rm.MainTank;
            c.UnitIsMainAssist = c.IsSelf ? r != null && r.IAmMainAssist : rm != null && rm.MainAssist;
            c.Distance = c.IsSelf ? 0f : e != null ? Yards(p.Pos, e.Pos) : float.MaxValue;
            c.UnitRaidIcon = ix != null ? ix.RaidTargetIconOf(guid) : 0;
            c.LootMethod = p.GroupLootMethod;
            c.LootThreshold = p.GroupLootThreshold;
            c.PvpDesired = ix != null && ix.Pvp != null && ix.Pvp.Desired;
            c.Dueling = ix != null && ix.Duel != null && ix.Duel.State != DuelState.None;
            c.HasRaidTab = UiRaid.SocialHostsRaidTab;
            c.DungeonDifficulty = p.DungeonDifficulty;
            c.RaidDifficulty = p.RaidDifficulty;
            c.WorldRows = c.Online;   // the world builder's SetDungeonDifficulty / SetRaidDifficulty / ResetInstances / UiInstance
            string inviteName;
            bool inviteEnabled;
            c.CanInvite = CanInvite(s, guid, out inviteName, out inviteEnabled);
            c.InviteEnabled = inviteEnabled;
        }

        private static float Yards(WowPos a, WowPos b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>The rows of the current menu level (UnitMenuRules.Build: UnitPopupMenus order, UnitPopup_HideButtons / _OnUpdate rules),
        /// drawn with the "(current)" mark for Checked rows, the raid icon cells and their colours (UnitPopup.lua:111-118).</summary>
        private void FillMenu(IWowSession s)
        {
            Menu m = _vr ? _vrMenu : _deskMenu;
            FillContext(s);
            UnitMenuRules.Build(_menuKind, _ctx, _rows);
            // ours: the VR palm menu has no own frame to right-click - a member's menu there also offers Leave party
            if (_menuFromPalm && _ctx.InParty && (_menuKind == UnitMenu.Party || _menuKind == UnitMenu.RaidPlayer) && _rows.Count > 0)
                _rows.Insert(_rows.Count - 1, new UnitMenuRow { Act = UnitMenuAct.Leave, Text = "Leave party", Enabled = true, R = 1f, G = 1f, B = 1f });   // PARTY_LEAVE (GlobalStrings.lua:5578)
            int n = Mathf.Min(_rows.Count, MaxRows);
            if (_rows.Count > MaxRows) WowLog.Warn("UI: unit menu '" + _menuKind + "' has " + _rows.Count + " rows (max " + MaxRows + ") - the last are cut.");
            for (int i = 0; i < MaxRows; i++)
            {
                UiButton b = m.Rows[i];
                if (i >= n) { b.gameObject.SetActive(false); m.Data[i] = default(UnitMenuRow); continue; }
                UnitMenuRow row = _rows[i];
                if (i == n - 1 && _rows[_rows.Count - 1].Act == UnitMenuAct.Cancel) row = _rows[_rows.Count - 1];   // Cancel stays last even when cut
                m.Data[i] = row;
                b.gameObject.SetActive(true);
                string label = row.Text ?? "";
                if (row.Act == UnitMenuAct.SetThreshold || row.Act == UnitMenuAct.LootThresholdMenu)
                    label = "<color=" + UiArt.QualityHex(row.Act == UnitMenuAct.SetThreshold ? row.Value : _ctx.LootThreshold) + ">" + label + "</color>";   // UnitPopup.lua:409-411
                b.SetLabel(Marked(label, row.Checked));
                bool colored = row.R < 0.999f || row.G < 0.999f || row.B < 0.999f;
                b.LabelColor = colored ? new Color(row.R, row.G, row.B) : Color.white;
                if (b.Label != null) b.Label.color = b.LabelColor;
                b.Interactable = row.Enabled;
                RawImage icon = m.Icons[i];
                bool hasIcon = row.Icon > 0 && row.Icon <= 8;
                icon.enabled = hasIcon;
                if (hasIcon)
                {
                    float u = ((row.Icon - 1) % 4) * 0.25f, v = ((row.Icon - 1) / 4) * 0.25f;   // SetRaidTargetIconTexture (TargetFrame.lua:692-701)
                    UiKit.Coords(icon, u, u + 0.25f, v, v + 0.25f);
                }
                if (b.Label != null) UiKit.Move(b.Label.rectTransform, hasIcon ? 4f + IconShift : 4f, 0f);
            }
            m.Title.text = UnitMenuRules.Title(_menuKind, _menuName);
            m.H = MenuPad + RowH * (n + 1) + MenuPad;
            UiKit.Size(m.Root, MenuW, m.H);
            m.Back.Place(0f, 0f, MenuW, m.H);
            if (_vr && _vrMenuSurface != null)
            {
                _vrMenuSurface.Root.sizeDelta = new Vector2(MenuW, m.H);
                _vrMenuSurface.Height = m.H;
            }
        }

        private void RefreshMenu(IWowSession s, float now)
        {
            if (!_menuOpen) return;
            if (_menuVr != _vr || now - _menuAt > MenuTimeout) { CloseMenu(now - _menuAt > MenuTimeout ? "timed out" : "the view changed"); return; }
            if (_vr && !_menuPlaced) { _menuPlaced = true; PlaceInFront(_vrMenuSurface, VrMenuDistance, -VrMenuDrop, 0f); }
            // Desktop: a click anywhere else closes a dropdown (UIDropDownMenu).
            if (!_vr && now - _menuAt > 0.25f)
            {
                bool down = false;
                try { down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1); } catch (Exception) { }
                if (down && !AnyHovered(_deskMenu)) CloseMenu("clicked elsewhere");
            }
        }

        private static bool AnyHovered(Menu m)
        {
            for (int i = 0; i < MaxRows; i++) if (m.Rows[i] != null && m.Rows[i].gameObject.activeSelf && m.Rows[i].Hovered) return true;
            return false;
        }

        /// <summary>UnitPopup_OnClick (UnitPopup.lua:1186-1365) for the rows this port has; each pick logs one line.</summary>
        private void Pick(int row)
        {
            Menu m = _menuVr ? _vrMenu : _deskMenu;
            UnitMenuRow d = row >= 0 && row < MaxRows ? m.Data[row] : default(UnitMenuRow);
            UnitMenuAct a = d.Act;
            int val = d.Value;
            IWowSession s = WowPlay.Session;
            ulong guid = _menuGuid;
            string who = _menuName ?? "?";
            if (s != null && s.Player != null)
            {
                PlayerState p = s.Player;
                IWowInteractions ix = s.Interactions;
                string name;
                bool enabled;
                switch (a)
                {
                    case UnitMenuAct.Invite:
                        if (CanInvite(s, guid, out name, out enabled) && enabled)
                        {
                            WowLog.Info("UI: Invite " + name + " (unit menu).");
                            s.InviteToGroup(name);   // UnitPopup "INVITE" -> InviteUnit(fullname)
                        }
                        break;
                    case UnitMenuAct.Leave:
                        WowLog.Info("UI: Leave party (unit menu).");
                        s.LeaveGroup();              // UnitPopup "LEAVE" -> LeaveParty()
                        break;
                    case UnitMenuAct.SetFocus:
                        WowFocus.Set(s, guid, "the unit menu (Set Focus)");   // UnitPopup "SET_FOCUS" -> FocusUnit(unit) (UnitPopup.lua:1382)
                        break;
                    case UnitMenuAct.Whisper:
                        UiChat.StartWhisper(who);   // UnitPopup "WHISPER" -> ChatFrame_SendTell(fullname) (:1193)
                        break;
                    case UnitMenuAct.Promote:
                    case UnitMenuAct.PromoteGuide:
                    case UnitMenuAct.RaidLeader:
                        WowLog.Info("UI: Promote to Leader " + who + " (unit menu, " + a + ").");
                        s.PromoteToLeader(guid);     // PROMOTE / PROMOTE_GUIDE / RAID_LEADER -> PromoteToLeader(unit, 1) (:1248, :1330-1331)
                        break;
                    case UnitMenuAct.Uninvite:
                    case UnitMenuAct.RaidRemove:
                        WowLog.Info("UI: Uninvite " + who + " (unit menu, " + a + ").");
                        s.UninviteFromGroup(guid);   // UNINVITE / RAID_REMOVE -> UninviteUnit(fullname) (:1216)
                        break;
                    case UnitMenuAct.VoteKick:
                        AskKickReason(guid, who);
                        break;
                    case UnitMenuAct.LootMethodMenu: OpenSub(s, UnitMenu.LootMethod); return;
                    case UnitMenuAct.LootThresholdMenu: OpenSub(s, UnitMenu.LootThreshold); return;
                    case UnitMenuAct.PvpMenu: OpenSub(s, UnitMenu.PvpFlag); return;
                    case UnitMenuAct.RaidIconMenu: OpenSub(s, UnitMenu.RaidIcon); return;
                    case UnitMenuAct.DungeonDiffMenu: OpenSub(s, UnitMenu.DungeonDifficulty); return;
                    case UnitMenuAct.RaidDiffMenu: OpenSub(s, UnitMenu.RaidDifficulty); return;
                    case UnitMenuAct.SetMethod:
                        WowLog.Info("UI: loot method " + UnitMenuRules.LootLabel(val) + " (unit menu).");
                        s.SetLootMethod(val, p.GroupLootThreshold);   // SetLootMethod("freeforall" ... "needbeforegreed") (:1285-1301)
                        break;
                    case UnitMenuAct.SetThreshold:
                        WowLog.Info("UI: loot threshold " + val + " (unit menu).");
                        s.SetLootMethod(Mathf.Max(0, p.GroupLootMethod), val);   // SetLootThreshold(id) (:1357)
                        break;
                    case UnitMenuAct.Inspect:
                        WowLog.Info("UI: Inspect " + who + " (unit menu).");
                        UiInspect.OpenedForTarget = guid == s.TargetGuid;
                        if (ix != null) ix.InspectPlayer(guid);   // INSPECT -> InspectUnit (:1205)
                        break;
                    case UnitMenuAct.Trade:
                        WowLog.Info("UI: Trade with " + who + " (unit menu).");
                        if (ix != null) ix.InitiateTrade(guid);   // TRADE -> InitiateTrade(unit) (:1201)
                        break;
                    case UnitMenuAct.Follow:
                        WowLog.Info("UI: Follow " + who + " (unit menu).");
                        SomniumSpace.Worlds.SomniumWoW.Game.Interact.WowFollow.Begin(guid);   // FOLLOW -> FollowUnit (:1209)
                        break;
                    case UnitMenuAct.Duel:
                        WowLog.Info("UI: Duel " + who + " (unit menu).");
                        if (ix != null) ix.RequestDuel(guid);    // DUEL -> StartDuel(unit, 1) (:1211-1212)
                        break;
                    case UnitMenuAct.ForfeitDuel:
                        WowLog.Info("UI: Forfeit Duel (unit menu).");
                        if (ix != null) ix.ForfeitDuel();         // ours (/forfeit = CancelDuel)
                        break;
                    case UnitMenuAct.PvpOn:
                    case UnitMenuAct.PvpOff:
                        {
                            bool on = a == UnitMenuAct.PvpOn;
                            WowLog.Info("UI: PvP " + (on ? "Enable" : "Disable") + " (unit menu).");
                            if (ix != null && ix.Pvp != null && ix.Pvp.Desired != on) ix.SetPvp(on);   // PVP_ENABLE / PVP_DISABLE -> SetPVP(1 / nil) (:1318-1321)
                            break;
                        }
                    case UnitMenuAct.SetRaidIcon:
                        WowLog.Info("UI: raid target icon " + (val == 0 ? "None" : UnitMenuRules.IconNames[val - 1]) + " on " + who + " (unit menu).");
                        if (ix != null) ix.SetRaidTargetIcon(guid, val);   // RAID_TARGET_n -> SetRaidTargetIcon(unit, n) (TargetFrame.lua:703-709)
                        break;
                    case UnitMenuAct.RaidPromote:
                        WowLog.Info("UI: Promote to Assistant " + who + " (unit menu).");
                        if (ix != null) ix.SetRaidAssistant(guid, true);   // RAID_PROMOTE -> PromoteToAssistant (:1332-1333)
                        break;
                    case UnitMenuAct.RaidDemote:
                        {
                            // RAID_DEMOTE -> DemoteAssistant + ClearPartyAssignment (UnitPopup.lua:1334-1344)
                            WowLog.Info("UI: Demote " + who + " (unit menu).");
                            if (ix == null) break;
                            if (_ctx.UnitIsAssistant && _ctx.IsLeader) ix.SetRaidAssistant(guid, false);
                            if (_ctx.UnitIsMainTank) ix.SetMainTank(guid, false);
                            if (_ctx.UnitIsMainAssist) ix.SetMainAssist(guid, false);
                            break;
                        }
                    case UnitMenuAct.RaidMainTank:
                        WowLog.Info("UI: Promote to Main Tank " + who + " (unit menu).");
                        if (ix != null) ix.SetMainTank(guid, true);   // RAID_MAINTANK -> SetPartyAssignment("MAINTANK", unit) (:1346-1349)
                        break;
                    case UnitMenuAct.RaidMainAssist:
                        WowLog.Info("UI: Promote to Main Assist " + who + " (unit menu).");
                        if (ix != null) ix.SetMainAssist(guid, true);
                        break;
                    case UnitMenuAct.ReportAfk:
                        WowLog.Info("UI: Report player Away " + who + " (unit menu).");
                        if (ix != null) ix.ReportPvpAfk(guid);   // PVP_REPORT_AFK -> ReportPlayerIsPVPAFK(unit) (:1361)
                        break;
                    case UnitMenuAct.SetDungeonDiff:
                        WowLog.Info("UI: dungeon difficulty " + val + " (unit menu).");
                        if (val != p.DungeonDifficulty) s.SetDungeonDifficulty(val);   // DUNGEON_DIFFICULTYn -> SetDungeonDifficulty(n) (:1310-1312)
                        break;
                    case UnitMenuAct.SetRaidDiff:
                        WowLog.Info("UI: raid difficulty " + val + " (unit menu).");
                        if (val != p.RaidDifficulty) s.SetRaidDifficulty(val);         // RAID_DIFFICULTYn -> SetRaidDifficulty(n) (:1313-1315)
                        break;
                    case UnitMenuAct.ResetInstances:
                        WowLog.Info("UI: Reset all instances (unit menu).");
                        UiInstance.ConfirmResetInstances();   // RESET_INSTANCES -> StaticPopup CONFIRM_RESET_INSTANCES (StaticPopup.lua:413-423)
                        break;
                    case UnitMenuAct.RaidInfo:
                        WowLog.Info("UI: Raid Info (unit menu).");
                        UiInstance.ShowRaidInfo();
                        break;
                    case UnitMenuAct.RaidWindow:
                        WowLog.Info("UI: Raid window (unit menu, ours).");
                        UiRaid.ToggleFallbackWindow();
                        break;
                }
            }
            CloseMenu(a == UnitMenuAct.Cancel ? "cancelled" : "picked");
        }

        /// <summary>VOTE_TO_KICK -> StaticPopup "VOTE_BOOT_REASON_REQUIRED" (StaticPopup.lua:2769-2797: GlobalStrings.lua:8911, an edit box of 64
        /// letters, OKAY disabled while empty, OKAY / CANCEL) -> the vote with the reason.</summary>
        private static void AskKickReason(ulong guid, string who)
        {
            WowLog.Info("UI: Vote to Kick " + who + " - asking the reason (VOTE_BOOT_REASON_REQUIRED).");
            int h = UiPopup.Show(new UiPopupSpec
            {
                Which = "VOTE_BOOT_REASON_REQUIRED",
                Text = "Please enter the reason to vote kick " + who + ":",      // VOTE_BOOT_REASON_REQUIRED (GlobalStrings.lua:8911)
                Button1 = "Okay", Button2 = "Cancel",                          // OKAY (:5234) / CANCEL (:1338)
                HasEditBox = true, MaxLetters = 64,
                Button1Enabled = t => t != null && t.Trim().Length > 0,
                OnAcceptText = t => { IWowSession s = WowPlay.Session; if (s != null && s.Interactions != null) s.Interactions.VoteToKick(guid, t); },
                WhileDead = true, HideOnEscape = true
            });
            if (h == 0) WowLog.Warn("UI: the VOTE_BOOT_REASON_REQUIRED popup could not be shown (UiPopup refused it).");
        }

        /// <summary>A nested UnitPopup level in the same menu (ours: in place of a second dropdown level).</summary>
        private void OpenSub(IWowSession s, UnitMenu kind)
        {
            _menuKind = kind;
            _menuAt = Time.unscaledTime;
            FillMenu(s);
            WowLog.Info("UI: unit menu '" + kind + "' (nested).");
        }

        // ------------------------------------------------------------------ group loot rolls (GroupLootFrame)

        /// <summary>The oldest roll of PlayerState.LootRolls with Need / Greed / Disenchant / Pass (GroupLootFrame's buttons; NEED, GREED,
        /// ROLL_DISENCHANT, PASS - GlobalStrings.lua:5066, :3849, :6152, :5588), the item's name in its quality colour, the count and the
        /// seconds left. Ours: StaticPopup's look instead of GroupLootFrame's art, one roll at a time (the next shows after it).</summary>
        private void RefreshRoll(IWowSession s)
        {
            List<LootRollView> rolls = s.Player.LootRolls;
            LootRollView r = rolls.Count > 0 ? rolls[0] : null;
            if (r == null) { if (_rollShown != null) HideRoll(); return; }
            if (r != _rollShown || _vr != _rollVr)
            {
                Build();
                bool fresh = r != _rollShown;
                _rollShown = r;
                _rollVr = _vr;
                _rollSecs = -1;
                _rollName = null;
                RollPopup ui = _vr ? _vrRoll : _deskRoll;
                ui.Need.Interactable = r.CanNeed;
                ui.Greed.Interactable = r.CanGreed;
                ui.Disenchant.Interactable = r.CanDisenchant;
                if (_vr)
                {
                    _deskRoll.Root.gameObject.SetActive(false);
                    ShowVr(_vrRollSurface, true);
                    PlaceInFront(_vrRollSurface, VrPopupDistance, -VrPopupDrop - 0.2f, 0f);
                }
                else
                {
                    ShowVr(_vrRollSurface, false);
                    _deskRoll.Root.gameObject.SetActive(true);
                }
                if (fresh) WowLog.Info("UI: loot roll shown for item " + r.Item.Entry + " (" + rolls.Count + " waiting)" + (_vr ? " (VR panel)" : "") + ".");
            }
            RollPopup u = _vr ? _vrRoll : _deskRoll;
            // spellui: the icon loads in the background (UiArt.Icon) - retried every frame until it is there, not only once a second.
            if (!ReferenceEquals(u.IconPath, r.Item.IconPath)) { u.IconPath = r.Item.IconPath; u.IconLoaded = false; u.Icon.enabled = false; }
            if (!u.IconLoaded && !string.IsNullOrEmpty(r.Item.IconPath))
            {
                Texture2D icon = UiArt.Icon(r.Item.IconPath);
                // lead fix 2026-10-04 (the user: "the need greed pass item shows, but I don't see the image of it"): UiKit.Pic made with no
                // texture is transparent black (UiKit.cs:64 `if (tex == null) img.color = new Color(0f, 0f, 0f, 0f)`) - the colour must be
                // set with the texture, as the bag buttons do (UiContainers.cs: B.Icon.color = tint)
                if (icon != null) { u.Icon.texture = icon; u.Icon.color = Color.white; u.Icon.enabled = true; u.IconLoaded = true; }
            }
            int secs = Mathf.CeilToInt(Mathf.Max(0f, r.SecondsLeft));
            if (secs == _rollSecs && ReferenceEquals(_rollName, r.Item.Name)) return;
            bool named = !ReferenceEquals(_rollName, r.Item.Name) && r.Item.Name != null;
            _rollSecs = secs;
            _rollName = r.Item.Name;
            // GroupLootFrame_OnShow (LootFrame.lua:357-366): the icon, the name in ITEM_QUALITY_COLORS[quality], the count only above 1.
            u.Name.text = r.Item.Name ?? UiGlobalStringsOr("RETRIEVING_ITEM_INFO", "Retrieving item information");   // GlobalStrings.lua:6131
            u.Name.color = r.Item.Name != null ? UiArt.QualityColor(r.Item.Quality) : UiArt.Gold;
            u.Count.text = r.Item.Count > 1 ? r.Item.Count.ToString() : "";
            u.Count.enabled = r.Item.Count > 1;
            u.Text.text = "<color=#bbbbbb>" + secs + " s</color>";   // ours: the seconds left (GroupLootFrame's timer bar)
            if (named) WowLog.Info("UI loot roll: item button for " + r.Item.Name + " (entry " + r.Item.Entry + ", quality " + r.Item.Quality + ", x" + r.Item.Count
                + ", tooltip " + (string.IsNullOrEmpty(r.Item.Tooltip) ? "name only" : r.Item.Tooltip.Split('\n').Length + " lines") + ") - hover it for the item's tooltip" + (_vr ? " (VR laser)" : "") + ".");
        }

        private static string UiGlobalStringsOr(string key, string fallback) { return SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure.UiGlobalStrings.G(key, fallback); }

        /// <summary>The roll item's tooltip (IconFrame OnEnter: GameTooltip:SetLootRollItem, LootFrame.xml:362-366) = the bags' and loot
        /// window's item tooltip (UiTooltip.Item); "Retrieving item information" until the realm answered the item query.</summary>
        private string RollItemTip()
        {
            LootRollView r = _rollShown;
            if (r == null || r.Item == null) return null;
            if (r.Item.Name == null) return "<color=#ffd100>" + UiGlobalStringsOr("RETRIEVING_ITEM_INFO", "Retrieving item information") + "</color>";
            return UiTooltip.Item(r.Item, null);
        }

        private void Roll(int type)
        {
            IWowSession s = WowPlay.Session;
            LootRollView r = _rollShown;
            if (s == null || r == null) return;
            WowLog.Info("UI: loot roll " + (type == 1 ? "Need" : type == 2 ? "Greed" : type == 3 ? "Disenchant" : "Pass") + " for item " + r.Item.Entry + ".");
            s.RollOnLoot(r, type);
            HideRoll();
        }

        private void HideRoll()
        {
            _rollShown = null;
            if (_deskRoll != null) _deskRoll.Root.gameObject.SetActive(false);
            ShowVr(_vrRollSurface, false);
        }

        private RollPopup MakeRoll(Transform parent)
        {
            var p = new RollPopup();
            p.Root = UiKit.Rect(parent, "Loot Roll", 0f, 0f, PopupW, RollH);
            p.Back = UiBackdrop.Make(p.Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            p.Back.Place(0f, 0f, PopupW, RollH);
            // spellui: GroupLootFrameTemplate's item parts (LootFrame.xml): $parentSlotTexture UI-EmptySlot 64 x 64 (:259-270, ours at (5, 1));
            // $parentIconFrame 34 x 34 at the slot's TOPLEFT + (15, -15) (:324-334) with its Icon 34 x 34 (:337-344) and Count NumberFontNormal
            // BOTTOMRIGHT (-5, 2) (:347-355); $parentNameFrame UI-Merchant-LabelSlots 128 x 64, LEFT at the slot's RIGHT + (-9, -10) (:271-282);
            // $parentName GameFontNormalSmall 90 x 30, LEFT at the slot's RIGHT + (-5, 5) (:283-296). No quality border in 3.3.5a: the name
            // carries the quality colour (LootFrame.lua:359-360).
            UiKit.Pic(p.Root, "SlotTexture", UiArt.Tex(UiArt.EmptySlot), 5f, 1f, 64f, 64f);
            UiKit.Pic(p.Root, "NameFrame", UiArt.Tex(@"Interface\MerchantFrame\UI-Merchant-LabelSlots.blp"), 5f + 64f - 9f, 1f + 32f + 10f - 32f, 128f, 64f);
            p.Item = UiKit.Button(p.Root, "IconFrame", 20f, 16f, 34f, 34f);
            p.Icon = UiKit.Pic(p.Item.transform, "Icon", (Texture2D)null, 0f, 0f, 34f, 34f);
            p.Icon.enabled = false;
            p.Count = UiKit.Text(p.Item.transform, "Count", "", 12f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 34f - 5f, 34f - 2f, UiArt.Numbers);
            p.Count.enabled = false;
            p.Item.Tip = RollItemTip;
            p.Name = UiKit.Text(p.Root, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Left, 5f + 64f - 5f, 1f + 32f - 5f - 15f, 90f, 30f);
            p.Text = UiKit.Text(p.Root, "Text", "", 11f, Color.white, TextAlignmentOptions.Right, PopupW - 120f, 16f, 100f, 34f);
            float x = (PopupW - (RollButtonW * 4f + 6f * 3f)) * 0.5f, y = RollH - 16f - ButtonH;
            p.Need = PopupButton(p.Root, "Need", x, y, () => Roll(1), RollButtonW);
            p.Greed = PopupButton(p.Root, "Greed", x + (RollButtonW + 6f), y, () => Roll(2), RollButtonW);
            p.Disenchant = PopupButton(p.Root, "Disenchant", x + (RollButtonW + 6f) * 2f, y, () => Roll(3), RollButtonW);
            p.Pass = PopupButton(p.Root, "Pass", x + (RollButtonW + 6f) * 3f, y, () => Roll(0), RollButtonW);
            p.Disenchant.Label.fontSize = 10f;
            return p;
        }

        private void CloseMenu(string why)
        {
            if (!_menuOpen) return;
            _menuOpen = false;
            if (_deskMenu != null) _deskMenu.Root.gameObject.SetActive(false);
            ShowVr(_vrMenuSurface, false);
            WowLog.Info("UI: unit menu closed (" + why + ").");
        }

        private void HideAll()
        {
            HidePopup();
            HideRoll();
            if (_menuOpen) CloseMenu("hidden");
        }

        // ------------------------------------------------------------------ building (lazily, on the first popup / menu)

        private void Build()
        {
            if (_built) return;
            _built = true;
            UiSurface oldSurface = UiKit.Surface;
            bool oldOver = UiKit.Over, oldPointable = UiKit.Pointable;
            try
            {
                // Desktop: our own overlay canvas above the HUD (5) and the WoW Map window (7), below the flight map (40).
                UiKit.Over = false;
                UiKit.Pointable = true;
                _desk = UiSurface.MakeScreen("WoW Group UI (desktop)", transform, 9);
                UiKit.Surface = _desk;
                RectTransform popHost = _desk.Anchor("Group Invite Popup", new Vector2(0.5f, 1f), PopupW, 100f, 0f, -135f);
                _deskPopup = MakePopup(popHost);
                RectTransform menuHost = _desk.Anchor("Group Menu Host", new Vector2(0f, 0f), 1f, 1f, 0f, 0f);
                _deskMenu = MakeMenu(menuHost);
                RectTransform rollHost = _desk.Anchor("Group Loot Roll", new Vector2(0.5f, 1f), PopupW, RollH, 0f, -380f);   // ours: below the popups
                _deskRoll = MakeRoll(rollHost);
                _desk.Finish();
                _deskPopup.Root.gameObject.SetActive(false);
                _deskMenu.Root.gameObject.SetActive(false);
                _deskRoll.Root.gameObject.SetActive(false);

                // VR: world panels under our own container (WowOrigin moves a UI container as one piece - they stand in the world).
                UiKit.Pointable = false;
                var root = new GameObject("WoW Group UI (VR)");
                root.transform.SetParent(transform, false);
                _vrRoot = root.transform;
                _vrPopupSurface = UiSurface.MakeWorld("WoW Group Invite (VR)", _vrRoot, PopupW, 100f, VrMetresPerUnit, false, 101);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrPopupSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrPopupSurface;
                _vrPopup = MakePopup(_vrPopupSurface.Content);
                _vrPopupSurface.Finish();
                _vrPopupSurface.SetShown(false);
                _vrMenuSurface = UiSurface.MakeWorld("WoW Group Menu (VR)", _vrRoot, MenuW, 100f, VrMetresPerUnit, false, 102);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrMenuSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrMenuSurface;
                _vrMenu = MakeMenu(_vrMenuSurface.Content);
                _vrMenuSurface.Finish();
                _vrMenuSurface.SetShown(false);
                _vrRollSurface = UiSurface.MakeWorld("WoW Loot Roll (VR)", _vrRoot, PopupW, RollH, VrMetresPerUnit, false, 103);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrRollSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrRollSurface;
                _vrRoll = MakeRoll(_vrRollSurface.Content);
                _vrRollSurface.Finish();
                _vrRollSurface.SetShown(false);
                WowLog.Info("UI: group UI built (invite popup + unit menu of up to " + MaxRows + " rows, desktop canvas order 9, VR panels at " + VrMetresPerUnit + " m per unit).");
            }
            finally
            {
                UiKit.Surface = oldSurface;
                UiKit.Over = oldOver;
                UiKit.Pointable = oldPointable;
            }
        }

        private Popup MakePopup(Transform parent)
        {
            var p = new Popup();
            p.Root = UiKit.Rect(parent, "Popup", 0f, 0f, PopupW, p.H);
            p.Back = UiBackdrop.Make(p.Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);   // StaticPopup's GlueDialog backdrop, as UiDeath
            p.Back.Place(0f, 0f, PopupW, p.H);
            p.Text = UiKit.Text(p.Root, "Text", "", 13f, Color.white, TextAlignmentOptions.Center, 20f, 16f, PopupW - 40f, 30f);
            p.Accept = PopupButton(p.Root, "Accept", PopupW * 0.5f - 6f - ButtonW, 56f, () => Answer(true));    // ACCEPT (GlobalStrings.lua:11)
            p.Decline = PopupButton(p.Root, "Decline", PopupW * 0.5f + 6f, 56f, () => Answer(false));        // DECLINE (:1945)
            return p;
        }

        /// <summary>StaticPopupButtonTemplate as UiDeath builds it: UI-DialogBox-Button Up / Down / Highlight, TexCoords 0, 1, 0, 0.71875.</summary>
        private static UiButton PopupButton(RectTransform parent, string label, float x, float y, Action click, float w = ButtonW)
        {
            UiButton b = UiKit.Button(parent, label, x, y, w, ButtonH);
            b.Art = UiKit.Pic(b.transform, "Up", UiArt.Tex(UiArt.DialogButtonUp), 0f, 1f, 0f, 0.71875f, 0f, 0f, w, ButtonH);
            b.ArtUp = UiArt.Tex(UiArt.DialogButtonUp);
            b.Pushed = UiKit.Pic(b.transform, "Down", UiArt.Tex(UiArt.DialogButtonDown), 0f, 1f, 0f, 0.71875f, 0f, 0f, w, ButtonH);
            b.Pushed.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.DialogButtonHighlight), 0f, 1f, 0f, 0.71875f, 0f, 0f, w, ButtonH);
            b.Hilite.enabled = false;
            b.Label = UiKit.Text(b.transform, "Label", label, 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, w, ButtonH);
            b.LabelColor = UiArt.Gold;
            b.Click = click;
            return b;
        }

        private void LayoutPopup(Popup p, string text)
        {
            p.Text.text = text;
            float tw = PopupW - 40f;
            float th = Mathf.Ceil(p.Text.GetPreferredValues(text, tw, 0f).y) + 2f;
            UiKit.Size(p.Text.rectTransform, tw, th);
            p.H = Mathf.Max(100f, 16f + th + 12f + ButtonH + 16f);
            UiKit.Size(p.Root, PopupW, p.H);
            p.Back.Place(0f, 0f, PopupW, p.H);
            float by = p.H - 16f - ButtonH;
            UiKit.Move(p.Accept.Rt, PopupW * 0.5f - 6f - ButtonW, by);
            UiKit.Move(p.Decline.Rt, PopupW * 0.5f + 6f, by);
            if (p == _vrPopup && _vrPopupSurface != null)
            {
                // VR: the panel is pointed at inside its own rect (UiSurface.RayHit), so it grows with the text (as UiDeath).
                _vrPopupSurface.Root.sizeDelta = new Vector2(PopupW, p.H);
                _vrPopupSurface.Height = p.H;
            }
        }

        private Menu MakeMenu(Transform parent)
        {
            var m = new Menu();
            m.H = MenuPad * 2f + RowH * 2f;
            m.Root = UiKit.Rect(parent, "Menu", 0f, 0f, MenuW, m.H);
            m.Back = UiBackdrop.Make(m.Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);   // ours: the popup's backdrop
            m.Back.Place(0f, 0f, MenuW, m.H);
            m.Title = UiKit.Text(m.Root, "Title", "", 11f, UiArt.Gold, TextAlignmentOptions.Left, MenuPad + 2f, MenuPad, MenuW - MenuPad * 2f, RowH);   // isTitle: GameFontNormalSmall gold
            m.Title.textWrappingMode = TextWrappingModes.NoWrap;
            m.Title.overflowMode = TextOverflowModes.Ellipsis;
            Texture2D icons = UiArt.Tex(RaidIconsArt);
            for (int i = 0; i < MaxRows; i++)
            {
                int k = i;
                float w = MenuW - MenuPad * 2f;
                UiButton b = UiKit.Button(m.Root, "Row " + (i + 1), MenuPad, MenuPad + RowH * (i + 1), w, RowH);
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, w, RowH);   // UIDropDownMenuButton's highlight
                b.Hilite.enabled = false;
                RawImage icon = UiKit.Pic(b.transform, "Icon", icons, 4f, 0f, IconSize, IconSize);
                icon.raycastTarget = false;
                icon.enabled = false;
                m.Icons[i] = icon;
                b.Label = UiKit.Text(b.transform, "Label", "", 11f, Color.white, TextAlignmentOptions.Left, 4f, 0f, w - 4f - IconShift, RowH);   // GameFontHighlightSmall
                b.Label.textWrappingMode = TextWrappingModes.NoWrap;
                b.LabelColor = Color.white;
                b.Click = () => Pick(k);
                b.gameObject.SetActive(false);
                m.Rows[i] = b;
            }
            return m;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Desktop: the menu's top-left at the mouse (UIDropDownMenu "cursor"), kept on the screen.</summary>
        private void PlaceAtMouse(Menu m)
        {
            Vector3 mouse = Vector3.zero;
            try { mouse = Input.mousePosition; } catch (Exception) { }
            float scale = Screen.height > 0 ? 768f / Screen.height : 1f;     // the overlay's reference height (CanvasScaler 1024 x 768, match height)
            float x = mouse.x * scale, y = mouse.y * scale;
            float wUnits = Screen.height > 0 ? Screen.width * scale : 1024f;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, wUnits - MenuW));
            y = Mathf.Clamp(y, m.H, 768f);
            // the 1 x 1 host sits at the screen's bottom-left; the menu (a UiKit rect: anchor and pivot top-left) hangs from the mouse
            m.Root.anchoredPosition = new Vector2(x, y - 1f);
        }

        /// <summary>VR: in front of the eyes (yaw only), once when it opens - it then stands in the world (no head-locked text).</summary>
        private static void PlaceInFront(UiSurface s, float distance, float up, float right)
        {
            if (s == null) return;
            Camera head = WowPlayer.Head();
            if (head == null) return;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            s.Root.SetPositionAndRotation(head.transform.position + fwd * distance + Vector3.up * up + side * right, Quaternion.LookRotation(fwd, Vector3.up));
        }

        /// <summary>A VR panel shown / hidden, and listed for the UI's ray / poke while shown (UiVrLayout.ExtraSurfaces).</summary>
        private static void ShowVr(UiSurface s, bool on)
        {
            if (s == null) return;
            s.SetShown(on);
            List<UiSurface> extra = UiVrLayout.ExtraSurfaces;
            if (on) { if (!extra.Contains(s)) extra.Add(s); }
            else extra.Remove(s);
        }

        private string _lastError;

        private void LogOnce(string where, Exception e)
        {
            string key = where + e.GetType().Name + e.Message;
            if (key == _lastError) return;
            _lastError = key;
            WowLog.Error("UI: " + where + " failed: " + e);
        }
    }
}
