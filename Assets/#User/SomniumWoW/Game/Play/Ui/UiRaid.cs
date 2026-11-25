// iteration 11 - owner interact (proposal-interact.md 4.5 / 4.9 / 6.3.5): the Raid tab (RaidFrame / Blizzard_RaidUI), the ReadyCheckFrame and
// the raid pullouts. The Raid tab is drawn into the Social window (socialui's FriendsFrame, hook UiSocial.RaidTab / RaidTabRefresh); while that
// window is not built, a "Raid" row of the SELF / PARTY menus opens our own RaidFrame window (kind 51, ours) with the same content.
// Desktop: the mouse (click = target, right-click = the RAID menu, Shift+click a member then click another group's slot = move / swap,
// click a group's label = its pullout); VR: the laser / poke (long-press a member = pick it up), the ready check in front of you.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>Our own RaidFrame window (the fallback host of the Raid tab while the Social window is not built).</summary>
    public sealed class UiRaid : UiWindow
    {
        /// <summary>Ours: the RaidFrame fallback window's kind (the next free number of the game-window range; request R-U2 to name it Raid = 51).</summary>
        public const UiWindowKind RaidKind = (UiWindowKind)51;

        /// <summary>The fallback window is wanted (the SELF / PARTY menu's "Raid" row toggles it).</summary>
        public static bool FallbackOpen;

        /// <summary>The Social window (socialui) exists and hosts the Raid tab (UiSocial.RaidTab).</summary>
        public static bool SocialHostsRaidTab { get { return UiRegistry.Visible(UiWindowKind.Social); } }

        public static void ToggleFallbackWindow()
        {
            FallbackOpen = !FallbackOpen;
            WowLog.Info("UI: RaidFrame (ours) " + (FallbackOpen ? "opened" : "closed") + " from the unit menu.");
        }

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = RaidKind, Frame = "RaidFrame", TitleKey = "RAID", Title = "Raid",   // RAID (GlobalStrings.lua:5940)
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, SessionWindow = true, Palm = false,   // FriendsFrame left 0 whileDead (UIParent.lua:31)
                Wanted = s => FallbackOpen && !SocialHostsRaidTab && s != null && s.Player != null,
                Closed = s => { FallbackOpen = false; },
                Owner = "interact", Hidden = false,
                Build = (parent, ctx, close) => new UiRaid(parent, ctx, close)
            };
        }

        // ---- the Social window's Raid tab (socialui calls these)
        private static readonly List<UiRaidRoster> SocialRosters = new List<UiRaidRoster>();

        /// <summary>UiInteract.OnEnable: the Raid tab builder / refresher for the Social window (proposal-social.md H9 / H10).</summary>
        public static void HookSocial()
        {
            UiSocial.RaidTab = (content, vr) =>
            {
                if (content == null) return;
                SocialRosters.Add(new UiRaidRoster(content, vr));
                WowLog.Info("UI: Raid tab built into the Social window (" + (vr ? "VR" : "desktop") + ").");
            };
            UiSocial.RaidTabRefresh = now =>
            {
                for (int i = SocialRosters.Count - 1; i >= 0; i--)
                {
                    UiRaidRoster r = SocialRosters[i];
                    if (r.Root == null) { SocialRosters.RemoveAt(i); continue; }
                    if (r.Root.gameObject.activeInHierarchy) r.Refresh(now);
                }
            };
        }

        private readonly UiRaidRoster _roster;

        public UiRaid(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "RaidFrame", 0f, 0f, W, H);
            const string f = @"Interface\FriendsFrame\UI-FriendsFrame-";   // FriendsFrame.xml:1238-1285 (the Raid tab's host frame)
            UiKit.Frame4(Root, "Art", f + "TopLeft-bnet.blp", f + "TopRight-bnet.blp", f + "BotLeft-bnet.blp", f + "BotRight-bnet.blp");
            UiKit.Text(Root, "Title", "Raid", 13f, UiArt.Gold, TextAlignmentOptions.Center, 78f, 16f, 240f, 17f);   // RAID (GlobalStrings.lua:5940)
            UiKit.CloseButton(Root, 384f - 46f - 16f, 24f - 16f, close);
            _roster = new UiRaidRoster(Root, ctx != null && ctx.Vr);
            Root.gameObject.SetActive(false);
        }

        protected override void OnOpen() { WowLog.Info("UI: RaidFrame (ours) opened."); }

        public override void Refresh(float now) { _roster.Refresh(now); }
    }

    /// <summary>The Raid tab's content (RaidFrame.xml / Blizzard_RaidUI.xml, FriendsFrame coordinates): not in a raid - RAID_DESCRIPTION +
    /// Convert To Raid + Raid Info; in a raid - Ready Check and the 8 groups of 5 (RaidGroupTemplate / RaidGroupButtonTemplate).</summary>
    public sealed class UiRaidRoster
    {
        private const float GroupW = 162f, GroupH = 80f, SlotW = 156f, SlotH = 14f;   // RaidGroupTemplate / RaidGroupButtonTemplate (RUI.xml:104-106, :320-336)
        private const string RaidArt = @"Interface\RaidFrame\";

        private sealed class Slot
        {
            public UiButton Button;
            public TextMeshProUGUI Name, Level, Class;
            public RawImage Rank, Role, Ready;
            public ulong Guid;
            public int Group;
        }

        private struct Entry
        {
            public ulong Guid;
            public string Name;
            public int Level;
            public WowClass Class;
            public bool Online, Dead, Leader, Assistant, MainTank, MainAssist;
            public ReadyCheckState Ready;
        }

        public RectTransform Root;
        private readonly bool _vr;
        private readonly RectTransform _notInRaid, _inRaid;
        private readonly UiButton _convert, _raidInfo, _readyCheck;
        private readonly Slot[,] _slots = new Slot[8, 5];
        private readonly UiButton[] _labels = new UiButton[8];
        private readonly List<Entry>[] _groups = new List<Entry>[8];
        private int _version = -1, _groupSig = int.MinValue;
        private ulong _picked;
        private int _pickedGroup = -1;
        private bool _logged;

        public UiRaidRoster(RectTransform content, bool vr)
        {
            Root = content;
            _vr = vr;
            for (int g = 0; g < 8; g++) _groups[g] = new List<Entry>();
            // ---- not in a raid (RaidFrame.xml:142-232)
            _notInRaid = UiKit.Rect(content, "Raid Not In Raid", 0f, 0f, 384f, 512f);
            string desc = Pure.UiGlobalStrings.G("RAID_DESCRIPTION", "Convert your party to a raid to adventure with up to 40 players.");   // RAID_DESCRIPTION (GlobalStrings.lua:5948), read from the install; ours as the fallback
            desc = desc.Replace("|cffffffff", "<color=#ffffff>").Replace("|r", "</color>");
            UiKit.Text(_notInRaid, "Description", desc, 12f, UiArt.Gold, TextAlignmentOptions.TopLeft, 29f, 85f, 300f, 300f);   // RaidFrameNotInRaid GameFontNormal 300 wide at (29, -85)
            _convert = UiKit.PanelButton(_notInRaid, "Convert To Raid", 72f, 37f, 115f, 21f, OnConvert);                      // CONVERT_TO_RAID (:1857), RaidFrameConvertToRaidButton (72, -37)
            _raidInfo = UiKit.PanelButton(_notInRaid, "Raid Info", 72f + 115f + 65f, 37f, 90f, 21f, OnRaidInfo);               // RAID_INFO (:5961), RaidFrameRaidInfoButton
            // ---- in a raid (Blizzard_RaidUI.xml)
            _inRaid = UiKit.Rect(content, "Raid Groups", 0f, 0f, 384f, 512f);
            _readyCheck = UiKit.PanelButton(_inRaid, "Ready Check", 71f + 90f + 2f, 37f, 90f, 21f, OnReadyCheck);              // READY_CHECK (:6027), RaidFrameReadyCheckButton right of the Raid Browser (+2)
            Texture2D outline = UiArt.Tex(RaidArt + "UI-RaidFrame-GroupOutline.blp"), button = UiArt.Tex(RaidArt + "UI-RaidFrame-GroupButton.blp");
            Texture2D hilite = UiArt.Add(RaidArt + "UI-RaidFrame-GroupButton.blp");
            for (int g = 0; g < 8; g++)
            {
                float gx = 16f + (g % 2) * (GroupW + 3f), gy = 70f + (g / 2) * (GroupH + 14f);   // RaidGroup1 (16, -70), 2 RIGHT +3, 3 below 1 -14 (RUI.xml:1107-1180)
                UiKit.Pic(_inRaid, "Group " + (g + 1) + " Outline", outline, 0f, 0.6640625f, 0f, 0.625f, gx, gy, GroupW, GroupH);
                int gg = g;
                UiButton label = UiKit.Button(_inRaid, "Group " + (g + 1) + " Label", gx + 3f, gy - 14f, SlotW, 14f);
                label.Label = UiKit.Text(label.transform, "Text", "Group " + (g + 1), 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, SlotW, 14f);   // GROUP (GlobalStrings.lua:3853)
                label.LabelColor = UiArt.Gold;
                if (!vr)
                {
                    label.Click = () => OnLabel(gg);
                    label.Tip = () => "Click: show this group at the left of the screen";   // ours (WoW drags the label off - RaidPullout_GenerateGroupFrame)
                }
                _labels[g] = label;
                for (int i = 0; i < 5; i++)
                {
                    var s = new Slot { Group = g };
                    UiButton b = UiKit.Button(_inRaid, "Group " + (g + 1) + " Slot " + (i + 1), gx + 3f, gy + 3f + i * (SlotH + 1f), SlotW, SlotH);
                    b.Art = UiKit.Pic(b.transform, "Normal", button, 0f, 0.640625f, 0f, 0.4375f, 0f, 0f, SlotW, SlotH);                 // RUI.xml:292-293
                    b.Hilite = UiKit.Pic(b.transform, "Highlight", hilite, 0f, 0.640625f, 0.46875f, 0.90625f, 0f, 0f, SlotW, SlotH);     // :295-296 (ADD)
                    b.Hilite.enabled = false;
                    s.Rank = UiKit.Pic(b.transform, "Rank", null, 2f, 1.5f, 11f, 11f);
                    s.Role = UiKit.Pic(b.transform, "Role", null, 13f, 1.5f, 11f, 11f);
                    s.Ready = UiKit.Pic(b.transform, "Ready Check", null, 13f, 1.5f, 11f, 11f);
                    s.Rank.raycastTarget = s.Role.raycastTarget = s.Ready.raycastTarget = false;
                    s.Rank.enabled = s.Role.enabled = s.Ready.enabled = false;
                    s.Name = UiKit.Text(b.transform, "Name", "", 9f, UiArt.Gold, TextAlignmentOptions.Left, 29f, 0f, 55f, SlotH);
                    s.Level = UiKit.Text(b.transform, "Level", "", 9f, UiArt.Gold, TextAlignmentOptions.Center, 29f + 55f + 2f, 0f, 18f, SlotH);
                    s.Class = UiKit.Text(b.transform, "Class", "", 9f, UiArt.Gold, TextAlignmentOptions.Center, 29f + 55f + 2f + 18f + 2f, 0f, 50f, SlotH);
                    s.Name.textWrappingMode = s.Level.textWrappingMode = s.Class.textWrappingMode = TextWrappingModes.NoWrap;
                    s.Name.overflowMode = TextOverflowModes.Ellipsis;
                    Slot ss = s;
                    b.Click = () => OnSlot(ss);
                    b.RightClick = () => { if (ss.Guid != 0) UiGroup.OpenRaidMenu(ss.Guid); };
                    b.LongPress = () => Pick(ss);                    // VR (UiButton.LongPress, 0.5 s held): pick the member up
                    b.Tip = () => SlotTip(ss);
                    s.Button = b;
                    _slots[g, i] = s;
                }
            }
            _inRaid.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ clicks

        private static IWowSession S { get { return WowPlay.Session; } }

        private void OnConvert()
        {
            IWowSession s = S;
            if (s == null || s.Interactions == null) return;
            WowLog.Info("UI: Raid tab - Convert To Raid.");
            s.Interactions.ConvertToRaid();
        }

        private void OnRaidInfo()
        {
            WowLog.Info("UI: Raid tab - Raid Info.");
            UiInstance.ShowRaidInfo();
        }

        private void OnReadyCheck()
        {
            IWowSession s = S;
            if (s == null || s.Interactions == null) return;
            WowLog.Info("UI: Raid tab - Ready Check.");
            s.Interactions.StartReadyCheck();
        }

        private void OnLabel(int g)
        {
            if (_picked != 0) { DropPicked("a group label"); return; }
            UiRaidHud.TogglePullout(g);
        }

        /// <summary>Left click: a picked member goes to this slot's group (empty slot: CMSG_GROUP_CHANGE_SUB_GROUP; a member of another group:
        /// CMSG_GROUP_SWAP_SUB_GROUP); Shift+click (desktop) picks a member up (ours - WoW drags); else the member is targeted.</summary>
        private void OnSlot(Slot s)
        {
            IWowSession ses = S;
            if (ses == null || ses.Player == null) return;
            IWowInteractions ix = ses.Interactions;
            if (_picked != 0)
            {
                ulong picked = _picked;
                int from = _pickedGroup;
                DropPicked(null);
                if (ix == null || s.Group == from) return;
                if (s.Guid == 0) { WowLog.Info("UI: Raid tab - moving a member to group " + (s.Group + 1) + "."); ix.MoveToSubGroup(picked, s.Group); }
                else { WowLog.Info("UI: Raid tab - swapping two members between groups " + (from + 1) + " and " + (s.Group + 1) + "."); ix.SwapSubGroups(picked, s.Guid); }
                return;
            }
            if (s.Guid == 0) return;
            bool shift = false;
            try { shift = !_vr && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)); } catch (Exception) { }
            if (shift) { Pick(s); return; }
            Target(ses, s.Guid);
        }

        private void Pick(Slot s)
        {
            IWowSession ses = S;
            if (s.Guid == 0 || ses == null || ses.Interactions == null) return;
            if (!ses.Interactions.Raid.CanLead) { Say("You are not the party leader.", UiArt.ErrorRed); return; }   // ERR_NOT_LEADER (GlobalStrings.lua:3313)
            _picked = s.Guid;
            _pickedGroup = s.Group;
            _version = -1;
            Say("Click a place in another group to move the member there.", UiArt.InfoYellow);   // ours
            WowLog.Info("UI: Raid tab - a member of group " + (s.Group + 1) + " picked up.");
        }

        private void DropPicked(string why)
        {
            if (_picked == 0) return;
            _picked = 0;
            _pickedGroup = -1;
            _version = -1;
            if (why != null) WowLog.Info("UI: Raid tab - the picked member dropped (" + why + ").");
        }

        /// <summary>The party frames' rule (UiVrHud.cs UiParty): a member in view is targeted, else WoW's way of saying it cannot be.</summary>
        internal static void Target(IWowSession s, ulong guid)
        {
            if (guid == s.Player.Guid || s.Find(guid) != null) { s.SetTarget(guid); return; }
            GroupMemberView m = UiUnits.GroupMember(s, guid);
            Say((m != null ? m.Name : "That member") + " cannot be targeted yet (the realm has not shown them).", UiArt.InfoYellow);   // ours (UiParty's text)
        }

        private static void Say(string text, Color c)
        {
            WowPlay.Publish(new GameEvent { Type = c == UiArt.ErrorRed ? GameEventType.Error : GameEventType.Info, Text = text });
        }

        private string SlotTip(Slot s)
        {
            if (s.Guid == 0) return null;
            IWowSession ses = S;
            if (ses == null) return null;
            GroupMemberView m = UiUnits.GroupMember(ses, s.Guid);
            string zone = m != null && m.ZoneId > 0 ? SomniumSpace.Worlds.SomniumWoW.Play.Riding.MountAreaRules.AreaName(m.ZoneId) : "";
            return (s.Name.text ?? "") + (zone.Length > 0 ? "\n<color=#ffffff>" + zone + "</color>" : "") + (_vr ? "" : "\n<color=#808080>Right-click: options. Shift+click: move to another group.</color>");   // ours
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh(float now)
        {
            IWowSession s = S;
            if (s == null || s.Player == null || Root == null) return;
            IWowInteractions ix = s.Interactions;
            RaidView r = ix != null ? ix.Raid : null;
            PlayerState p = s.Player;
            bool inRaid = r != null && r.IsRaid;
            if (_inRaid.gameObject.activeSelf != inRaid) { _inRaid.gameObject.SetActive(inRaid); _notInRaid.gameObject.SetActive(!inRaid); _version = -1; }
            if (!inRaid)
            {
                if (_picked != 0) DropPicked("not a raid");
                bool leader = p.Group.Count > 0 && p.GroupLeaderGuid == p.Guid;
                _convert.Interactable = ix != null && ix.Available && leader && p.Level >= 10 && (r == null || !r.IsLfgGroup);   // RaidFrame.lua:64-78
                return;
            }
            if (!_logged) { _logged = true; WowLog.Info("UI: Raid tab shows the raid (8 groups of 5" + (_vr ? ", VR" : ", desktop") + ")."); }
            _readyCheck.Interactable = r.CanLead && !r.ReadyCheckActive;
            int sig = r.Version * 31 + p.Group.Count;
            for (int i = 0; i < p.Group.Count; i++) { GroupMemberView m = p.Group[i]; sig = sig * 7 + (m.Online ? 1 : 0) + (m.Dead ? 2 : 0) + m.Level * 5; }
            if (sig == _version && _groupSig == p.Level) return;
            _version = sig;
            _groupSig = p.Level;
            for (int g = 0; g < 8; g++) _groups[g].Clear();
            // our own row (PlayerState + MySubGroup), then the others in the server's order
            _groups[Mathf.Clamp(r.MySubGroup, 0, 7)].Add(new Entry
            {
                Guid = p.Guid, Name = p.Name, Level = p.Level, Class = p.Class, Online = true, Dead = p.Dead || p.Ghost,
                Leader = p.GroupLeaderGuid == p.Guid, Assistant = r.IAmAssistant, MainTank = r.IAmMainTank, MainAssist = r.IAmMainAssist, Ready = r.MyReady
            });
            for (int i = 0; i < r.Members.Count; i++)
            {
                RaidMemberView rm = r.Members[i];
                GroupMemberView m = UiUnits.GroupMember(s, rm.Guid);
                int g = Mathf.Clamp(rm.SubGroup, 0, 7);
                if (_groups[g].Count >= 5) continue;
                _groups[g].Add(new Entry
                {
                    Guid = rm.Guid, Name = m != null ? m.Name : "?", Level = m != null ? m.Level : 0, Class = m != null ? m.Class : 0, Online = m == null || m.Online,
                    Dead = m != null && (m.Dead || m.Ghost), Leader = rm.Guid == p.GroupLeaderGuid, Assistant = rm.Assistant, MainTank = rm.MainTank, MainAssist = rm.MainAssist, Ready = rm.Ready
                });
            }
            for (int g = 0; g < 8; g++)
                for (int i = 0; i < 5; i++) Fill(_slots[g, i], i < _groups[g].Count ? (Entry?)_groups[g][i] : null);
        }

        private void Fill(Slot s, Entry? e)
        {
            UiButton b = s.Button;
            if (e == null)
            {
                s.Guid = 0;
                s.Name.text = "Empty";                                                            // EMPTY (GlobalStrings.lua:2762), GameFontDarkGraySmall
                s.Name.color = UiArt.Grey;
                s.Level.text = "";
                s.Class.text = "";
                s.Rank.enabled = s.Role.enabled = s.Ready.enabled = false;
                if (b.Art != null) b.Art.enabled = false;
                return;
            }
            Entry x = e.Value;
            s.Guid = x.Guid;
            if (b.Art != null) b.Art.enabled = true;
            bool picked = x.Guid == _picked;
            Color cls = x.Class != 0 ? UiParty.ClassColor(x.Class) : UiArt.Gold;
            Color c = !x.Online ? UiArt.Grey : x.Dead ? new Color(0.6f, 0.2f, 0.2f) : cls;   // RaidGroupButton: offline grey, dead red (ours)
            if (picked) c = Color.white;
            s.Name.text = picked ? "> " + (x.Name ?? "?") : x.Name ?? "?";
            s.Name.color = c;
            s.Level.text = x.Level > 0 ? x.Level.ToString() : "";
            s.Level.color = c;
            s.Class.text = x.Class != 0 ? UiArt.ClassName(x.Class) : "";
            s.Class.color = c;
            // rank (RUI.u.lua:400-406): leader / assistant; role (:411-415): main tank / main assist; ready check marks (ReadyCheck.lua:1-4)
            string rank = x.Leader ? @"Interface\GroupFrame\UI-Group-LeaderIcon.blp" : x.Assistant ? @"Interface\GroupFrame\UI-Group-AssistantIcon.blp" : null;
            s.Rank.enabled = rank != null;
            if (rank != null) s.Rank.texture = UiArt.Tex(rank);
            string ready = x.Ready == ReadyCheckState.Waiting ? RaidArt + "ReadyCheck-Waiting.blp" : x.Ready == ReadyCheckState.Ready ? RaidArt + "ReadyCheck-Ready.blp"
                         : x.Ready == ReadyCheckState.NotReady || x.Ready == ReadyCheckState.Away ? RaidArt + "ReadyCheck-NotReady.blp" : null;
            s.Ready.enabled = ready != null;
            if (ready != null) s.Ready.texture = UiArt.Tex(ready);
            string role = x.MainTank ? @"Interface\GroupFrame\UI-Group-MainTankIcon.blp" : x.MainAssist ? @"Interface\GroupFrame\UI-Group-MainAssistIcon.blp" : null;
            s.Role.enabled = role != null && ready == null;
            if (role != null) s.Role.texture = UiArt.Tex(role);
        }
    }

    /// <summary>The ReadyCheckFrame (ReadyCheck.xml / .lua) and the raid pullouts (Blizzard_RaidUI RaidPulloutFrameTemplate, desktop only - VR is
    /// gap G-R2), driven every frame by UiInteract. Desktop: our own overlay canvas (order 9, UiGroup's); VR: a world panel in front of you.</summary>
    public sealed class UiRaidHud
    {
        private const float FrameW = 323f, FrameH = 97f;                       // ReadyCheckFrame (ReadyCheck.xml:14-24)
        private const float PullW = 92f, PullButtonH = 25f, PullX = 10f, PullY = 480f;   // RaidPulloutFrameTemplate 92 wide, buttons 70 x 25; ours: the column's place
        public const float VrMetresPerUnit = 0.0014f;

        public static UiRaidHud Current { get; private set; }

        private sealed class ReadyFrame
        {
            public RectTransform Root;
            public RawImage Portrait;
            public TextMeshProUGUI Text;
        }

        private sealed class Pullout
        {
            public int Group;
            public RectTransform Root;
            public TextMeshProUGUI Title;
            public readonly UiButton[] Buttons = new UiButton[5];
            public readonly TextMeshProUGUI[] Names = new TextMeshProUGUI[5];
            public readonly UiBar[] Health = new UiBar[5], Mana = new UiBar[5];
            public readonly ulong[] Guids = new ulong[5];
        }

        private readonly Transform _owner;
        private UiSurface _desk, _vrSurface;
        private ReadyFrame _deskReady, _vrReady;
        private RectTransform _pullHost;
        private readonly List<Pullout> _pullouts = new List<Pullout>();
        private readonly bool[] _pullWanted = new bool[8];
        private bool _built, _readyShown, _readyVr;
        private ulong _readyFor;

        public UiRaidHud(Transform owner)
        {
            _owner = owner;
            Current = this;
        }

        /// <summary>A group's label on the Raid tab (desktop): show / hide that group's pullout.</summary>
        public static void TogglePullout(int group)
        {
            UiRaidHud h = Current;
            if (h == null || group < 0 || group > 7) return;
            h._pullWanted[group] = !h._pullWanted[group];
            WowLog.Info("UI: raid pullout of group " + (group + 1) + " " + (h._pullWanted[group] ? "shown" : "hidden") + ".");
            h.LayoutPullouts();
        }

        public void Destroy()
        {
            if (Current == this) Current = null;
            if (_desk != null && _desk.Canvas != null) UnityEngine.Object.Destroy(_desk.Canvas.gameObject);
            if (_vrSurface != null) { ShowVr(false); UnityEngine.Object.Destroy(_vrSurface.Root.gameObject); }
        }

        /// <summary>Every frame (UiInteract.Update): the ready-check question and the pullouts.</summary>
        public void Tick(IWowSession s, bool vr)
        {
            RaidView r = s != null && s.Interactions != null ? s.Interactions.Raid : null;
            bool ask = r != null && r.ReadyCheckAsked;
            if (ask && (!_readyShown || _readyVr != vr || _readyFor != r.ReadyCheckInitiator)) ShowReady(s, r, vr);
            else if (!ask && _readyShown) HideReady("answered or over");
            bool inRaid = r != null && r.IsRaid;
            if (!inRaid)
            {
                bool any = false;
                for (int g = 0; g < 8; g++) { any |= _pullWanted[g]; _pullWanted[g] = false; }
                if (any) { LayoutPullouts(); WowLog.Info("UI: raid pullouts closed (the raid ended)."); }
                return;
            }
            if (vr) { if (_pullHost != null && _pullHost.gameObject.activeSelf) _pullHost.gameObject.SetActive(false); return; }
            if (_pullHost != null && !_pullHost.gameObject.activeSelf) _pullHost.gameObject.SetActive(true);
            for (int i = 0; i < _pullouts.Count; i++) RefreshPullout(s, r, _pullouts[i]);
        }

        // ------------------------------------------------------------------ the ready check

        private void ShowReady(IWowSession s, RaidView r, bool vr)
        {
            Build();
            _readyShown = true;
            _readyVr = vr;
            _readyFor = r.ReadyCheckInitiator;
            ReadyFrame f = vr ? _vrReady : _deskReady;
            // READY_CHECK_MESSAGE "%s has initiated a ready check.\n|cffffffffAre you ready?|r" (GlobalStrings.lua:6030)
            f.Text.text = (r.ReadyCheckInitiatorName ?? "Someone") + " has initiated a ready check.\n<color=#ffffff>Are you ready?</color>";
            GroupMemberView m = UiUnits.GroupMember(s, r.ReadyCheckInitiator);
            if (m != null && m.Class != 0) { Vector4 c = UiArt.ClassCoords(m.Class); UiKit.Coords(f.Portrait, c.x, c.y, c.z, c.w); f.Portrait.enabled = true; }
            else f.Portrait.enabled = false;
            if (vr)
            {
                _deskReady.Root.gameObject.SetActive(false);
                ShowVr(true);
                PlaceInFront(_vrSurface, 1.1f, -0.1f);   // UiGroup's popups: 1.1 m ahead, 0.1 m down (ours)
                try { SomniumSpace.Worlds.SomniumWoW.Game.Gear.GearXr.Pulse(true, 0.5f, 0.1f); SomniumSpace.Worlds.SomniumWoW.Game.Gear.GearXr.Pulse(false, 0.5f, 0.1f); } catch (Exception) { }   // ours: "something asks you"
            }
            else
            {
                ShowVr(false);
                _deskReady.Root.gameObject.SetActive(true);
            }
            try { WowAudio.PlayUiNamed("ReadyCheck"); } catch (Exception) { }   // ShowReadyCheck: PlaySound("ReadyCheck") (ReadyCheck.lua:9-20)
            WowLog.Info("UI: ReadyCheckFrame shown (" + (r.ReadyCheckInitiatorName ?? "?") + ", " + (vr ? "VR panel" : "desktop") + ").");
        }

        private void HideReady(string why)
        {
            _readyShown = false;
            if (_deskReady != null) _deskReady.Root.gameObject.SetActive(false);
            ShowVr(false);
            WowLog.Info("UI: ReadyCheckFrame hidden (" + why + ").");
        }

        private void Answer(bool ready)
        {
            IWowSession s = WowPlay.Session;
            WowLog.Info("UI: ReadyCheckFrame - " + (ready ? "Yes" : "No") + ".");
            if (s != null && s.Interactions != null) s.Interactions.AnswerReadyCheck(ready);
            HideReady("answered");
        }

        // ------------------------------------------------------------------ pullouts (desktop)

        private void LayoutPullouts()
        {
            Build();
            float y = PullY;
            for (int g = 0; g < 8; g++)
            {
                Pullout p = Find(g);
                if (!_pullWanted[g]) { if (p != null) p.Root.gameObject.SetActive(false); continue; }
                if (p == null) p = MakePullout(g);
                p.Root.gameObject.SetActive(true);
                UiKit.Move(p.Root, PullX, y);
                y += 14f + 5f * (PullButtonH + 2f) + 12f;   // title + 5 buttons + 12 (ours)
            }
        }

        private Pullout Find(int g)
        {
            for (int i = 0; i < _pullouts.Count; i++) if (_pullouts[i].Group == g) return _pullouts[i];
            return null;
        }

        private Pullout MakePullout(int g)
        {
            UiSurface old = UiKit.Surface;
            bool oldPointable = UiKit.Pointable, oldOver = UiKit.Over;
            try
            {
                UiKit.Surface = _desk;
                UiKit.Pointable = true;
                UiKit.Over = false;
                var p = new Pullout { Group = g };
                p.Root = UiKit.Rect(_pullHost, "Raid Pullout " + (g + 1), PullX, PullY, PullW, 14f + 5f * (PullButtonH + 2f));
                p.Title = UiKit.Text(p.Root, "Title", "Group " + (g + 1), 10f, UiArt.Gold, TextAlignmentOptions.Left, 2f, 0f, PullW, 14f);   // GameFontNormalSmall title (RUI.xml:472-620)
                Texture2D frame = UiArt.Tex(@"Interface\RaidFrame\UI-RaidFrame-HealthBar.blp");
                for (int i = 0; i < 5; i++)
                {
                    float by = 14f + i * (PullButtonH + 2f);
                    int k = i;
                    UiButton b = UiKit.Button(p.Root, "Member " + (i + 1), 0f, by, 70f, PullButtonH);   // RaidPulloutButtonTemplate 70 x 25
                    b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, 70f, PullButtonH);
                    b.Hilite.enabled = false;
                    p.Names[i] = UiKit.Text(b.transform, "Name", "", 9f, Color.white, TextAlignmentOptions.Left, 2f, -5f, 65f, 12f);   // Name 65 x 12 at (2, 5)
                    p.Names[i].textWrappingMode = TextWrappingModes.NoWrap;
                    p.Health[i] = UiBar.Make(b.transform, "Health", 1f, 11f, 68f, 6f, new Color(0f, 1f, 0f), true);                   // HealthBar 68 x 6 at (1, -11)
                    p.Mana[i] = UiBar.Make(b.transform, "Mana", 1f, 18f, 68f, 6f, new Color(0f, 0f, 1f), true);                       // ManaBar 68 x 6 below it (-1)
                    RawImage fr = UiKit.Pic(b.transform, "Frame", frame, 0f, 10f, 70f, 8f);                                              // UI-RaidFrame-HealthBar 70 x 8
                    fr.raycastTarget = false;
                    b.Click = () => { IWowSession s = WowPlay.Session; if (s != null && s.Player != null && p.Guids[k] != 0) UiRaidRoster.Target(s, p.Guids[k]); };
                    b.RightClick = () => { if (p.Guids[k] != 0) UiGroup.OpenUnitMenu(p.Guids[k]); };   // the RAID_PLAYER menu
                    p.Buttons[i] = b;
                }
                _pullouts.Add(p);
                return p;
            }
            finally
            {
                UiKit.Surface = old;
                UiKit.Pointable = oldPointable;
                UiKit.Over = oldOver;
            }
        }

        private static void RefreshPullout(IWowSession s, RaidView r, Pullout p)
        {
            if (!p.Root.gameObject.activeSelf) return;
            PlayerState me = s.Player;
            int n = 0;
            if (r.MySubGroup == p.Group) n = Put(p, n, me.Guid, me.Name, me.Health, me.MaxHealth, me.PowerValue, me.PowerMax, me.Power, true, me.Dead || me.Ghost, me.Class);
            for (int i = 0; i < r.Members.Count && n < 5; i++)
            {
                RaidMemberView rm = r.Members[i];
                if (rm.SubGroup != p.Group) continue;
                GroupMemberView m = UiUnits.GroupMember(s, rm.Guid);
                if (m == null) continue;
                n = Put(p, n, m.Guid, m.Name, m.Health, m.MaxHealth, m.PowerValue, m.PowerMax, m.Power, m.Online, m.Dead || m.Ghost, m.Class);
            }
            for (int i = n; i < 5; i++) { p.Guids[i] = 0; if (p.Buttons[i].gameObject.activeSelf) p.Buttons[i].gameObject.SetActive(false); }
        }

        private static int Put(Pullout p, int i, ulong guid, string name, int hp, int maxHp, int power, int maxPower, PowerType pt, bool online, bool dead, WowClass cls)
        {
            if (!p.Buttons[i].gameObject.activeSelf) p.Buttons[i].gameObject.SetActive(true);
            p.Guids[i] = guid;
            Color c = !online || dead ? UiArt.Grey : cls != 0 ? UiParty.ClassColor(cls) : Color.white;
            if (!string.Equals(p.Names[i].text, name)) p.Names[i].text = name ?? "?";
            p.Names[i].color = c;
            p.Health[i].Set(online && maxHp > 0 ? (float)hp / maxHp : 0f);
            p.Mana[i].SetColor(UiArt.PowerColor(pt));
            p.Mana[i].Set(online && maxPower > 0 ? (float)power / maxPower : 0f);
            return i + 1;
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            if (_built) return;
            _built = true;
            UiSurface old = UiKit.Surface;
            bool oldOver = UiKit.Over, oldPointable = UiKit.Pointable;
            try
            {
                UiKit.Over = false;
                UiKit.Pointable = true;
                _desk = UiSurface.MakeScreen("WoW Raid UI (desktop)", _owner, 9);
                UiKit.Surface = _desk;
                RectTransform readyHost = _desk.Anchor("Ready Check", new Vector2(0.5f, 0.5f), FrameW, FrameH, 0f, -10f);   // CENTER (0, -10) of the screen
                _deskReady = MakeReady(readyHost);
                _pullHost = _desk.Anchor("Raid Pullouts", new Vector2(0f, 1f), 1f, 1f, 0f, 0f);
                _desk.Finish();
                _deskReady.Root.gameObject.SetActive(false);
                UiKit.Pointable = false;
                _vrSurface = UiSurface.MakeWorld("WoW Ready Check (VR)", _owner, FrameW, FrameH, VrMetresPerUnit, false, 104);
                UiKit.Surface = _vrSurface;
                _vrReady = MakeReady(_vrSurface.Content);
                _vrSurface.Finish();
                _vrSurface.SetShown(false);
                WowLog.Info("UI: raid UI built (ReadyCheckFrame desktop + VR, raid pullouts on the desktop).");
            }
            finally
            {
                UiKit.Surface = old;
                UiKit.Over = oldOver;
                UiKit.Pointable = oldPointable;
            }
        }

        private ReadyFrame MakeReady(Transform parent)
        {
            var f = new ReadyFrame();
            f.Root = UiKit.Rect(parent, "ReadyCheckFrame", 0f, 0f, FrameW, FrameH);
            UiKit.Pic(f.Root, "Art", UiArt.Tex(@"Interface\RaidFrame\UI-ReadyCheckFrame.blp"), 0f, 0.630859375f, 0f, 0.7578125f, 0f, 0f, FrameW, FrameH);   // ReadyCheck.xml:43-50
            f.Portrait = UiKit.Pic(f.Root, "Portrait", UiArt.Tex(UiArt.ClassCircles), 7f, 6f, 50f, 50f);                                   // 50 x 50 at (7, -6)
            f.Portrait.enabled = false;
            f.Text = UiKit.Text(f.Root, "Text", "", 12f, UiArt.Gold, TextAlignmentOptions.Top, 62f, 18f, FrameW - 70f, 40f);               // ReadyCheckFrameText GameFontNormal TOP (15, -24) (ours: right of the portrait)
            UiButton yes = UiKit.PanelButton(f.Root, "Yes", 55f, FrameH - 18f - 24f, 109f, 24f, () => Answer(true));                    // YES (GlobalStrings.lua:8982) at BOTTOMLEFT (55, 18)
            UiButton no = UiKit.PanelButton(f.Root, "No", 55f + 109f + 2f, FrameH - 18f - 24f, 109f, 24f, () => Answer(false));          // NO (:5181) right of it
            return f;
        }

        private void ShowVr(bool on)
        {
            if (_vrSurface == null) return;
            _vrSurface.SetShown(on);
            List<UiSurface> extra = UiVrLayout.ExtraSurfaces;
            if (on) { if (!extra.Contains(_vrSurface)) extra.Add(_vrSurface); }
            else extra.Remove(_vrSurface);
        }

        private static void PlaceInFront(UiSurface s, float distance, float up)
        {
            Camera head = WowPlayer.Head();
            if (s == null || head == null) return;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            s.Root.SetPositionAndRotation(head.transform.position + fwd * distance + Vector3.up * up, Quaternion.LookRotation(fwd, Vector3.up));
        }
    }
}
