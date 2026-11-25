// iteration 11 - owner queues (proposal-interact.md 4.8 / 6.3.8): WoW's Dungeon Finder - LFDParentFrame (the role buttons, the type choice
// Random Dungeon / Specific Dungeons, the random rewards, the specific list with its locks, Find Group / Leave Queue, the queue status) and
// the two frames the hub (UiQueues, UiPvp.cs) shows: LFDRoleCheckPopup and LFDDungeonReadyPopup. Desktop: the mouse; VR: the laser / poke.
// Offline the window says the Dungeon Finder is on the online realm (ours). Texts are WoW's (GlobalStrings.lua line) unless marked ours.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiLfg : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.DungeonFinder, Frame = "LFDParentFrame", TitleKey = "LOOKING_FOR_DUNGEON", Title = "Dungeon Finder",   // GlobalStrings.lua:4783
                Area = UiPanelArea.Left, Pushable = 0, Width = 355f, Height = 440f, WhileDead = true, Binding = "TOGGLELFGPARENT", Micro = "LFG", MicroOrder = 7, MinLevel = 15,   // UIParent.lua:48; SHOW_LFD_LEVEL (Constants.lua:131); LFD.xml:863-866
                Owner = "queues", Hidden = false,
                Build = (parent, ctx, close) => new UiLfg(parent, ctx, close)
            };
        }

        // ---- shared rules (also used by the hub) ----------------------------------------------------------------------------------

        /// <summary>The roles a class can take (GetAvailableRoles is C code, LFG.lua:210-230 calls it) - ours: warrior / paladin / druid / death
        /// knight tank; priest / paladin / druid / shaman heal; every class damage.</summary>
        public static int AvailableRoles(WowClass c)
        {
            int r = LfgView.RoleDamage;
            if (c == WowClass.Warrior || c == WowClass.Paladin || c == WowClass.Druid || c == WowClass.DeathKnight) r |= LfgView.RoleTank;
            if (c == WowClass.Priest || c == WowClass.Paladin || c == WowClass.Druid || c == WowClass.Shaman) r |= LfgView.RoleHealer;
            return r;
        }

        /// <summary>The roles CMSG_LFG_JOIN sends: the chosen ones (damage when none), + Leader when we lead a group.</summary>
        public static int RolesForJoin(LfgView l, PlayerState p)
        {
            int r = (l != null ? l.MyRoles : 0) & ~LfgView.RoleLeader;
            if (r == 0) r = LfgView.RoleDamage;
            if (p != null && p.GroupLeaderGuid != 0 && p.GroupLeaderGuid == p.Guid) r |= LfgView.RoleLeader;
            return r;
        }

        /// <summary>Inside the LFG dungeon (the eye's Teleport Out Of / To Dungeon): our map is the proposal's dungeon map; when that dungeon is not
        /// in the lists, any map other than the four continents (ours).</summary>
        public static bool InsideLfgDungeon(LfgView l, IWowSession s)
        {
            if (l == null || s == null) return false;
            int id = l.ProposalEntry & 0xFFFFFF;
            foreach (LfgDungeonView d in l.Dungeons) if (d.Id == id && d.MapId > 0) return s.MapId == d.MapId;
            foreach (LfgDungeonView d in l.RandomDungeons) if (d.Id == id && d.MapId > 0) return s.MapId == d.MapId;
            int m = s.MapId;
            return m != 0 && m != 1 && m != 530 && m != 571 && m >= 0;
        }

        /// <summary>LFG_INSTANCE_INVALID_CODES (LFG.lua:30-42) -> INSTANCE_UNAVAILABLE_SELF_* (GlobalStrings.lua:4266-4274).</summary>
        public static string LockText(int code)
        {
            switch (code)
            {
                case 1: return "You do not have the World of Warcraft expansion.";
                case 2: case 1001: return "You must advance to a higher level.";
                case 3: case 1002: return "Your level is too high.";
                case 4: return "You must obtain better gear.";
                case 5: return "Your gear is too good.";
                case 6: return "You are already locked to this instance.";
                case 1022: return "You have not completed the required quest.";
                case 1025: return "You do not have the required item.";
                default: return "You do not meet the requirements for this dungeon.";
            }
        }

        /// <summary>UI-LFG-ICON-ROLES (256 x 256, 67 x 67 cells): GetTexCoordsForRole (LFG.lua:324-340) - GUIDE (1,1), TANK (1,2), HEALER (2,1), DAMAGER (2,2).</summary>
        public static Vector4 RoleCoords(int role)
        {
            int cx, cy;
            if (role == LfgView.RoleTank) { cx = 1; cy = 2; }
            else if (role == LfgView.RoleHealer) { cx = 2; cy = 1; }
            else if (role == LfgView.RoleDamage) { cx = 2; cy = 2; }
            else { cx = 1; cy = 1; }
            const float c = 67f / 256f;
            return new Vector4((cx - 1) * c, cx * c, (cy - 1) * c, cy * c);
        }

        public const string RolesArt = @"Interface\LFGFrame\UI-LFG-ICON-ROLES.blp";

        // ---- the window ------------------------------------------------------------------------------------------------------------

        private const string Lfg = @"Interface\LFGFrame\";
        private const int ListRows = 12;
        private static readonly int[] RoleBits = { LfgView.RoleTank, LfgView.RoleHealer, LfgView.RoleDamage, LfgView.RoleLeader };
        private static readonly string[] RoleNames = { "Tank", "Healer", "Damage", "Leader" };   // TANK :7468, HEALER :4006, DAMAGER :1906, LEADER :4692
        private static readonly float[] RoleX = { 65f, 138f, 209f, 289f };                       // LFD.xml:918-969 (TOPLEFT (65, -60), then +25 / +23 / +32 after 48-wide buttons - ours width)

        private readonly Action _close;
        private readonly TextMeshProUGUI _notice, _typeLabel, _rTitle, _rText, _rRewardLabel, _rRewardText, _status;
        private readonly RectTransform _content, _random, _specific, _moneyHolder;
        private readonly UiMoney _money;
        private readonly UiButton[] _roleButtons = new UiButton[4], _roleChecks = new UiButton[4], _rewardItems = new UiButton[4];
        private readonly ItemView[] _rewardShown = new ItemView[4];
        private readonly string[] _rewardIcon = new string[4];
        private readonly bool[] _rewardLoaded = new bool[4];
        private readonly UiButton _type, _find;
        private readonly UiQk.Row[] _rows = new UiQk.Row[ListRows];
        private readonly UiButton[] _rowChecks = new UiButton[ListRows];
        private readonly RawImage[] _rowLocks = new RawImage[ListRows];
        private readonly List<object> _list = new List<object>();   // string = a group header, LfgDungeonView = a dungeon
        private readonly HashSet<int> _picked = new HashSet<int>();  // specific: the entries checked (ours: kept while the window lives)
        private int _typeIndex, _top, _version = -1, _roles = -1;
        private bool _logged;

        public UiLfg(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            W = 355f;
            H = 440f;
            Root = UiKit.Rect(parent, "LFDParentFrame", 0f, 0f, W, H);
            // UI-LFG-BACKGROUND-QUESTPAPER 512 x 256 at LEFT (21, -64), UI-LFG-FRAME 512 x 512 at TOPLEFT (LFD.xml:875-905)
            UiKit.Pic(Root, "Background", UiArt.Tex(Lfg + "UI-LFG-BACKGROUND-QUESTPAPER.blp"), 21f, H * 0.5f + 64f - 128f, 512f, 256f);
            UiKit.Pic(Root, "Layout", UiArt.Tex(Lfg + "UI-LFG-FRAME.blp"), 0f, 0f, 512f, 512f);
            UiKit.Pic(Root, "Portrait", UiArt.Tex(Lfg + "LFG-Eye.blp"), 0f, 0.125f, 0f, 0.25f, 10f, 8f, 52f, 52f);   // LFDParentFramePortrait (EyeTemplate, LFD.xml:1771) - frame 1
            TextMeshProUGUI title = UiKit.Text(Root, "Title", "Dungeon Finder", 13f, UiArt.Gold, TextAlignmentOptions.Center, 177.5f + 12f - 120f, 11f, 240f, 17f);   // TitleText TOP (12, -17)
            title.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.CloseButton(Root, W - 34f, 8f, () => { if (_close != null) _close(); });
            _notice = UiQk.Notice(Root, 30f, 170f, 295f, 120f);

            // role buttons (LFDQueueFrameRoleButtonTank / Healer / DPS / Leader): the icon + its check box (ours: the check at the icon's bottom left)
            Texture2D roles = UiArt.Tex(RolesArt);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                float size = i == 3 ? 40f : 48f;
                UiButton b = UiKit.Button(Root, "Role " + RoleNames[i], RoleX[i], i == 3 ? 64f : 60f, size, size);
                Vector4 c = RoleCoords(RoleBits[i]);
                b.Art = UiKit.Pic(b.transform, "Icon", roles, c.x, c.y, c.z, c.w, 0f, 0f, size, size);
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(@"Interface\Buttons\UI-Common-MouseHilight.blp"), -4f, -4f, size + 8f, size + 8f);
                b.Hilite.enabled = false;
                b.Click = () => ToggleRole(k);
                b.Tip = () => RoleNames[k];
                _roleButtons[i] = b;
                _roleChecks[i] = UiQk.Check(Root, "", RoleX[i] - 4f, (i == 3 ? 64f : 60f) + size - 14f, 0f, () => ToggleRole(k));
            }

            // type (LFDQueueFrameTypeDropDown TOPLEFT (131, -125)) - ours: a button that cycles the entries (right click: back)
            _typeLabel = UiKit.Text(Root, "Type label", "Type:", 11f, UiArt.Gold, TextAlignmentOptions.Right, 40f, 127f, 88f, 16f);   // CHOOSE_YOUR_DUNGEON :1621
            _type = UiKit.PanelButton(Root, "Type", 131f, 124f, 190f, 22f, () => CycleType(1));
            _type.RightClick = () => CycleType(-1);

            _content = UiKit.Rect(Root, "Content", 24f, 152f, 310f, 230f);
            // random: LFDQueueFrameRandom (LFD.xml:1017-1236): the title, LFD_RANDOM_EXPLANATION, LFD_REWARDS, the reward text, money, items
            _random = UiKit.Rect(_content, "Random", 0f, 0f, 310f, 230f);
            _rTitle = UiKit.Text(_random, "Title", "", 14f, UiArt.Gold, TextAlignmentOptions.TopLeft, 10f, 8f, 290f, 18f);
            _rText = UiKit.Text(_random, "Description", "Using the Dungeon Finder to do a Random Dungeon will earn you extra rewards.", 10f, Color.black, TextAlignmentOptions.TopLeft, 10f, 30f, 280f, 30f, null, false);   // LFD_RANDOM_EXPLANATION :4724
            _rRewardLabel = UiKit.Text(_random, "Rewards", "Rewards", 13f, UiArt.Gold, TextAlignmentOptions.TopLeft, 10f, 70f, 280f, 16f);   // LFD_REWARDS :4728
            _rRewardText = UiKit.Text(_random, "Reward text", "", 10f, Color.black, TextAlignmentOptions.TopLeft, 10f, 90f, 280f, 30f, null, false);
            _moneyHolder = UiKit.Rect(_random, "Money", 10f, 124f, 280f, 16f);
            _money = new UiMoney(_moneyHolder, 140f, 0f, Color.white);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                UiButton s = UiKit.Slot(_random, "Reward " + i, 10f + i * 44f, 148f, 37f, false);
                s.Icon.enabled = false;
                s.Count = UiKit.Text(s.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 35f, 35f, UiArt.Numbers);
                s.Tip = () => _rewardShown[k] != null ? UiTooltip.Item(_rewardShown[k], null) : null;
                s.gameObject.SetActive(false);
                _rewardItems[i] = s;
            }
            // specific: LFDQueueFrameSpecific: the list (group headers + check boxes), locks greyed with UI-LFG-ICON-LOCK and the reason on hover
            _specific = UiKit.Rect(_content, "Specific", 0f, 0f, 310f, 230f);
            for (int i = 0; i < ListRows; i++)
            {
                int k = i;
                _rows[i] = UiQk.MakeRow(_specific, "Dungeon " + i, 0f, 4f + i * 18f, 290f, 18f, new[] { 24f, 214f }, new[] { 190f, 74f }, 10f);
                _rows[i].B.Click = () => ToggleDungeon(k);
                _rows[i].B.Tip = () => RowTip(k);
                _rowChecks[i] = UiQk.Check(_rows[i].B.transform, "", 2f, -1f, 0f, () => ToggleDungeon(k));
                _rowLocks[i] = UiKit.Pic(_rows[i].B.transform, "Lock", UiArt.Tex(Lfg + "UI-LFG-ICON-LOCK.blp"), 2f, 1f, 16f, 16f);
                _rowLocks[i].enabled = false;
            }
            UiQk.ScrollPair(_specific, 292f, 4f, 4f + ListRows * 18f - 16f, () => { _top = Math.Max(0, _top - 1); _version = -1; }, () => { _top++; _version = -1; });
            _status = UiKit.Text(Root, "Status", "", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 24f, 372f, 310f, 36f);
            // LFDQueueFrameFindGroupButton BOTTOMLEFT (18, 6) 135 x 22; LFDQueueFrameCancelButton BOTTOMRIGHT (-7, 6) 112 x 22 (LFD.xml:1429-1460)
            _find = UiKit.PanelButton(Root, "Find Group", 18f, H - 6f - 22f, 135f, 22f, FindOrLeave);   // FIND_A_GROUP :3697 / LEAVE_QUEUE :4700
            UiKit.PanelButton(Root, "Close", W - 7f - 112f, H - 6f - 22f, 112f, 22f, () => { if (_close != null) _close(); });   // CLOSE :1645
            Root.gameObject.SetActive(false);
            UiQueues.Ensure(parent, ctx);
        }

        private LfgView View { get { IWowInteractions ix = UiQk.Ix(Ctx); return ix != null ? ix.Lfg : null; } }

        private bool Queued(LfgView l)
        {
            return l != null && (l.Status == LfgStatus.Queued || l.Status == LfgStatus.RoleCheck || l.Status == LfgStatus.Proposal);
        }

        private void ToggleRole(int i)
        {
            PlayerState p = Ctx != null ? Ctx.P : null;
            if (p == null) return;
            int bit = RoleBits[i];
            if (bit != LfgView.RoleLeader && (AvailableRoles(p.Class) & bit) == 0) return;   // a greyed role
            if (bit == LfgView.RoleLeader && !(p.GroupLeaderGuid != 0 && p.GroupLeaderGuid == p.Guid)) return;
            if (_roles < 0) _roles = DefaultRoles();
            _roles ^= bit;
            UiSound.Named((_roles & bit) != 0 ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");   // LFD.lua:395
            _version = -1;
        }

        private int DefaultRoles()
        {
            LfgView l = View;
            PlayerState p = Ctx != null ? Ctx.P : null;
            int r = l != null ? l.MyRoles & ~LfgView.RoleLeader : 0;
            if (r == 0 && p != null) r = AvailableRoles(p.Class) & LfgView.RoleDamage;
            return r;
        }

        private void CycleType(int d)
        {
            LfgView l = View;
            int n = (l != null ? l.RandomDungeons.Count : 0) + 1;
            _typeIndex = ((_typeIndex + d) % n + n) % n;
            _top = 0;
            _version = -1;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
        }

        private void ToggleDungeon(int row)
        {
            int k = _top + row;
            if (k < 0 || k >= _list.Count) return;
            var d = _list[k] as LfgDungeonView;
            if (d == null || d.LockStatus != 0) return;
            if (!_picked.Remove(d.Entry)) _picked.Add(d.Entry);
            _version = -1;
        }

        private string RowTip(int row)
        {
            int k = _top + row;
            var d = k >= 0 && k < _list.Count ? _list[k] as LfgDungeonView : null;
            if (d == null) return null;
            return d.Name + (d.LockStatus != 0 ? "\n<color=#ff2020>" + LockText(d.LockStatus) + "</color>" : "");
        }

        private void FindOrLeave()
        {
            IWowInteractions ix = UiQk.Ix(Ctx);
            LfgView l = View;
            PlayerState p = Ctx != null ? Ctx.P : null;
            if (ix == null || l == null || p == null) return;
            if (Queued(l)) { WowLog.Info("UI: LFDParentFrame - Leave Queue."); ix.LeaveLfg(); return; }
            var entries = new List<int>();
            if (_typeIndex < l.RandomDungeons.Count) entries.Add(l.RandomDungeons[_typeIndex].Entry);
            else foreach (int e in _picked) entries.Add(e);
            if (entries.Count == 0) { Ctx.Say("Choose a dungeon first.", UiArt.ErrorRed); return; }   // ours (WoW keeps Find Group grey)
            int roles = (_roles < 0 ? DefaultRoles() : _roles) & ~LfgView.RoleLeader;
            if (roles == 0) { Ctx.Say("Choose a role first.", UiArt.ErrorRed); return; }              // ours (WoW keeps Find Group grey)
            if (p.GroupLeaderGuid != 0 && p.GroupLeaderGuid == p.Guid && ((_roles < 0 ? 0 : _roles) & LfgView.RoleLeader) != 0) roles |= LfgView.RoleLeader;
            WowLog.Info("UI: LFDParentFrame - Find Group (" + entries.Count + " dungeon(s), roles " + roles + ").");
            ix.JoinLfg(roles, entries);
        }

        protected override void OnOpen()
        {
            _version = -1;
            IWowInteractions ix = UiQk.Ix(Ctx);
            if (ix != null && ix.Available) ix.RequestLfgInfo();   // LFDFrame_OnShow asks for the lists (RequestLFDPlayerLockInfo / RequestLFDPartyLockInfo)
            UiSound.Named("igCharacterInfoOpen");
            WowLog.Info("UI: LFDParentFrame opened (" + (ix != null && ix.Available ? "online" : "offline - the note") + (Ctx != null && Ctx.Vr ? ", VR" : ", desktop") + ").");
        }

        protected override void OnClose()
        {
            UiSound.Named("igCharacterInfoClose");
        }

        public override void Refresh(float now)
        {
            LfgView l = View;
            PlayerState p = Ctx != null ? Ctx.P : null;
            bool live = UiQk.Live(Ctx) && l != null && p != null;
            UiQk.Show(_notice, !live);
            UiQk.Show(_content, live);
            UiQk.Show(_type, live);
            UiQk.Show(_typeLabel, live);
            UiQk.Show(_find, live);
            UiQk.Show(_status, live);
            for (int i = 0; i < 4; i++) { UiQk.Show(_roleButtons[i], live); UiQk.Show(_roleChecks[i], live); }
            if (!live) { UiQk.Set(_notice, "The Dungeon Finder is on the online realm.", UiArt.Gold); return; }   // ours
            if (_specific.gameObject.activeSelf && UiQk.Wheel(_specific) != 0) { _top = Math.Max(0, _top - UiQk.Wheel(_specific)); _version = -1; }
            bool queued = Queued(l);
            UiQk.Set(_status, queued ? "You are queued as: " + OnlineSession.LfRoleNames(l.MyRoles) + "\nAverage Wait Time: " + UiQk.Mmss(l.AvgWait) + "   " +   // :6195 :666
                                       "Time In Queue: " + UiQk.Mmss(l.QueuedSeconds) : "", UiArt.Gold);                                                         // :7567
            int ver = l.Version * 131 + _typeIndex * 17 + _top * 5 + (_roles & 0xF) + _picked.Count * 1000 + (queued ? 7 : 0) + p.Level * 100000;
            if (ver == _version)
            {
                RefreshRewardIcons();
                return;
            }
            _version = ver;
            if (!_logged) { _logged = true; WowLog.Info("UI: LFDParentFrame first refresh (" + l.RandomDungeons.Count + " random, " + l.Dungeons.Count + " specific dungeons, info " + (l.InfoLoaded ? "loaded" : "asked") + ")."); }
            if (_typeIndex > l.RandomDungeons.Count) _typeIndex = 0;
            bool random = _typeIndex < l.RandomDungeons.Count;
            _type.SetLabel(random ? l.RandomDungeons[_typeIndex].Name ?? "Random Dungeon" : "Specific Dungeons");   // LFG_TYPE_RANDOM_DUNGEON :4757 / SPECIFIC_DUNGEONS :6899
            _type.Interactable = !queued;
            _find.SetLabel(queued ? "Leave Queue" : "Find Group");
            // roles: the class's possible ones, the leader for a group's leader (LFDQueueFrame_SetRoles)
            int avail = AvailableRoles(p.Class);
            bool leader = p.GroupLeaderGuid != 0 && p.GroupLeaderGuid == p.Guid;
            int mine = _roles < 0 ? DefaultRoles() : _roles;
            if (queued) mine = l.MyRoles;
            for (int i = 0; i < 4; i++)
            {
                bool can = RoleBits[i] == LfgView.RoleLeader ? leader : (avail & RoleBits[i]) != 0;
                _roleButtons[i].Art.color = can ? Color.white : new Color(0.4f, 0.4f, 0.4f, 1f);
                _roleButtons[i].Interactable = can && !queued;
                _roleChecks[i].Interactable = can && !queued;
                UiQk.Show(_roleChecks[i], can);
                UiQk.SetCheck(_roleChecks[i], (mine & RoleBits[i]) != 0);
            }
            UiQk.Show(_random, random);
            UiQk.Show(_specific, !random);
            if (random) FillRandom(l.RandomDungeons[_typeIndex]);
            else FillSpecific(l, queued);
        }

        private void FillRandom(LfgDungeonView d)
        {
            UiQk.Set(_rTitle, d.Name ?? "Random Dungeon", UiArt.Gold);
            UiQk.Set(_rRewardText, d.Done ? "Random dungeons which you complete subsequent to the first each day will earn you:"     // LFD_RANDOM_REWARD_EXPLANATION2 :4726
                                          : "The first random dungeon that you complete each day will earn you:", Color.black);       // LFD_RANDOM_REWARD_EXPLANATION1 :4725
            _money.Set(d.RewardMoney);
            UiQk.Show(_moneyHolder, d.RewardMoney > 0);
            UiQk.Set(_rRewardLabel, "Rewards" + (d.RewardXp > 0 ? "   <size=10><color=#ffffff>Experience: " + d.RewardXp + "</color></size>" : ""), UiArt.Gold);   // LFD_REWARDS :4728, EXPERIENCE_COLON :3623
            for (int i = 0; i < 4; i++)
            {
                ItemView it = i < d.RewardItems.Count ? d.RewardItems[i] : null;
                if (!ReferenceEquals(it, _rewardShown[i])) { _rewardShown[i] = it; _rewardLoaded[i] = false; _rewardIcon[i] = null; _rewardItems[i].Icon.enabled = false; }
                UiQk.Show(_rewardItems[i], it != null);
                if (it != null && _rewardItems[i].Count != null) _rewardItems[i].Count.text = it.Count > 1 ? it.Count.ToString() : "";
            }
            RefreshRewardIcons();
        }

        private void RefreshRewardIcons()
        {
            for (int i = 0; i < 4; i++)
            {
                ItemView it = _rewardShown[i];
                if (it == null || _rewardLoaded[i] || string.IsNullOrEmpty(it.IconPath)) continue;
                SetIcon(_rewardItems[i], it.IconPath, ref _rewardIcon[i], ref _rewardLoaded[i]);
            }
        }

        private void FillSpecific(LfgView l, bool queued)
        {
            _list.Clear();
            OnlineSession os = UiQk.Online(Ctx);
            string group = null;
            foreach (LfgDungeonView d in l.Dungeons)
            {
                string g = os != null ? os.LfgGroupNameOf(d.Id) : null;
                if (!string.IsNullOrEmpty(g) && g != group) { group = g; _list.Add(g); }
                _list.Add(d);
            }
            _top = Mathf.Clamp(_top, 0, Math.Max(0, _list.Count - ListRows));
            for (int i = 0; i < ListRows; i++)
            {
                int k = _top + i;
                UiQk.Row r = _rows[i];
                UiQk.Show(r.B, k < _list.Count);
                if (k >= _list.Count) continue;
                var head = _list[k] as string;
                if (head != null)
                {
                    UiQk.Set(r.Col[0], head, UiArt.Gold);
                    UiQk.Set(r.Col[1], "", UiArt.Gold);
                    UiQk.Show(_rowChecks[i], false);
                    _rowLocks[i].enabled = false;
                    r.B.Interactable = false;
                    r.Sel.enabled = false;
                    continue;
                }
                var d = (LfgDungeonView)_list[k];
                bool locked = d.LockStatus != 0;
                Color c = locked ? UiArt.Grey : Color.black;
                UiQk.Set(r.Col[0], d.Name ?? "?", c);
                UiQk.Set(r.Col[1], d.MinLevel == d.MaxLevel ? string.Format("({0})", d.MinLevel) : string.Format("({0} - {1})", d.MinLevel, d.MaxLevel), c);   // LFD_LEVEL_FORMAT_SINGLE / _RANGE :4722-4723
                UiQk.Show(_rowChecks[i], !locked);
                UiQk.SetCheck(_rowChecks[i], queued ? l.Queued.Contains(d.Entry) : _picked.Contains(d.Entry));
                _rowChecks[i].Interactable = !queued;
                _rowLocks[i].enabled = locked;
                r.B.Interactable = true;
                r.Sel.enabled = false;
            }
        }
    }

    // =============================================================================================================== the LFD popups

    /// <summary>LFDRoleCheckPopup (LFD.xml:543-650) and LFDDungeonReadyPopup (LFD.xml:109-420, 306 x 223) - one set per surface (UiQueues builds
    /// a desktop and a VR set and shows the one of the current mode).</summary>
    internal sealed class UiLfgPopups
    {
        public const float W = 306f, H = 223f;

        private RectTransform _role, _ready;
        private readonly UiButton[] _roleChecks = new UiButton[3];
        private readonly UiButton[] _roleIcons = new UiButton[3];
        private TextMeshProUGUI _readyName, _readyRole, _readyTime;
        private RawImage _readyRoleIcon;
        private readonly RawImage[] _marks = new RawImage[5];
        private UiButton _roleAccept, _enter, _leave;
        private int _roles = -1, _proposalShown = -1;
        private static readonly int[] Bits = { LfgView.RoleTank, LfgView.RoleHealer, LfgView.RoleDamage };

        public bool AnyShown { get { return (_role != null && _role.gameObject.activeSelf) || (_ready != null && _ready.gameObject.activeSelf); } }

        public void Build(Transform parent)
        {
            // ---- the role check: CONFIRM_YOUR_ROLE "Confirm your role:" :1844, the three role check boxes, ACCEPT / DECLINE (:11 / :1945)
            _role = UiKit.Rect(parent, "LFDRoleCheckPopup", 0f, 30f, W, 160f);
            UiKit.Blocker(_role);
            UiBackdrop.Make(_role, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white).Place(0f, 0f, W, 160f);
            UiKit.Text(_role, "Text", "Confirm your role:", 12f, UiArt.Gold, TextAlignmentOptions.Center, 20f, 16f, W - 40f, 16f);
            Texture2D roles = UiArt.Tex(UiLfg.RolesArt);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                float x = 40f + i * 82f;
                UiButton b = UiKit.Button(_role, "Role " + i, x, 40f, 48f, 48f);
                Vector4 c = UiLfg.RoleCoords(Bits[i]);
                b.Art = UiKit.Pic(b.transform, "Icon", roles, c.x, c.y, c.z, c.w, 0f, 0f, 48f, 48f);
                b.Click = () => Toggle(k);
                _roleIcons[i] = b;
                _roleChecks[i] = UiQk.Check(_role, "", x - 4f, 74f, 0f, () => Toggle(k));
            }
            _roleAccept = UiKit.PopupButton(_role, "Accept", 22f, 118f, () => AnswerRoles(true));   // ACCEPT :11
            UiKit.PopupButton(_role, "Decline", W - 22f - 128f, 118f, () => AnswerRoles(false));    // DECLINE :1945
            _role.gameObject.SetActive(false);

            // ---- the dungeon ready popup: READY_CHECK label, the dungeon's name, YOUR_ROLE + the role, the members' marks, ENTER_DUNGEON / LEAVE_QUEUE
            _ready = UiKit.Rect(parent, "LFDDungeonReadyPopup", 0f, 0f, W, H);
            UiKit.Blocker(_ready);
            UiBackdrop.Make(_ready, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white).Place(0f, 0f, W, H);
            UiKit.Text(_ready, "Label", "Ready Check", 12f, Color.white, TextAlignmentOptions.Center, 20f, 15f, W - 40f, 16f);   // READY_CHECK :6027 (LFD.xml: $parentLabel TOP (0, -15))
            _readyName = UiKit.Text(_ready, "Dungeon", "", 14f, UiArt.Gold, TextAlignmentOptions.Center, 20f, 36f, W - 40f, 18f);
            UiKit.Text(_ready, "Your role", "Your Role", 10f, UiArt.Gold, TextAlignmentOptions.Center, 20f, 60f, W - 40f, 14f);   // YOUR_ROLE :8986
            _readyRoleIcon = UiKit.Pic(_ready, "Role icon", roles, 0f, 1f, 0f, 1f, W * 0.5f - 24f, 76f, 48f, 48f);
            _readyRole = UiKit.Text(_ready, "Role", "", 11f, Color.white, TextAlignmentOptions.Center, 20f, 126f, W - 40f, 14f);
            Texture2D wait = UiArt.Tex(@"Interface\RaidFrame\ReadyCheck-Waiting.blp");
            for (int i = 0; i < 5; i++) _marks[i] = UiKit.Pic(_ready, "Member " + i, wait, W * 0.5f - 60f + i * 24f, 146f, 20f, 20f);
            _readyTime = UiKit.Text(_ready, "Time", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 20f, 168f, W - 40f, 14f);
            // ENTER_DUNGEON :2823 / LEAVE_QUEUE :4700, 115 x 22 each (LFD.xml:109-420)
            _enter = UiKit.PanelButton(_ready, "Enter Dungeon", W * 0.5f - 117f, H - 36f, 115f, 22f, () => AnswerProposal(true));
            _leave = UiKit.PanelButton(_ready, "Leave Queue", W * 0.5f + 2f, H - 36f, 115f, 22f, () => AnswerProposal(false));
            _ready.gameObject.SetActive(false);
        }

        private void Toggle(int i)
        {
            PlayerState p = WowPlay.Session != null ? WowPlay.Session.Player : null;
            if (p == null || (UiLfg.AvailableRoles(p.Class) & Bits[i]) == 0) return;
            if (_roles < 0) _roles = 0;
            _roles ^= Bits[i];
            UiSound.Named((_roles & Bits[i]) != 0 ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
        }

        private void AnswerRoles(bool accept)
        {
            IWowInteractions ix = WowPlay.Session != null ? WowPlay.Session.Interactions : null;
            if (ix == null) return;
            int r = accept ? Math.Max(0, _roles) : 0;
            if (accept && r == 0) return;   // LFDRoleCheckPopupAcceptButton stays grey without a role
            WowLog.Info("UI: LFDRoleCheckPopup - " + (accept ? "Accept (roles " + r + ")" : "Decline") + ".");
            ix.SetLfgRoles(r);
            _roles = -1;
        }

        private void AnswerProposal(bool accept)
        {
            IWowInteractions ix = WowPlay.Session != null ? WowPlay.Session.Interactions : null;
            if (ix == null) return;
            WowLog.Info("UI: LFDDungeonReadyPopup - " + (accept ? "Enter Dungeon" : "Leave Queue") + ".");
            ix.AnswerLfgProposal(accept);
        }

        public void Hide()
        {
            UiQk.Show(_role, false);
            UiQk.Show(_ready, false);
        }

        public void Refresh(IWowInteractions ix, OnlineSession os, PlayerState p)
        {
            LfgView l = ix.Lfg;
            bool role = l.RoleCheckAsked;
            UiQk.Show(_role, role);
            if (role)
            {
                if (_roles < 0) _roles = l.MyRoles & ~LfgView.RoleLeader;
                int avail = UiLfg.AvailableRoles(p.Class);
                for (int i = 0; i < 3; i++)
                {
                    bool can = (avail & Bits[i]) != 0;
                    _roleIcons[i].Art.color = can ? Color.white : new Color(0.4f, 0.4f, 0.4f, 1f);
                    UiQk.Show(_roleChecks[i], can);
                    UiQk.SetCheck(_roleChecks[i], (_roles & Bits[i]) != 0);
                }
                _roleAccept.Interactable = _roles > 0;
            }
            else _roles = -1;
            bool ready = l.ProposalAsked;
            UiQk.Show(_ready, ready);
            if (!ready) { _proposalShown = -1; return; }
            if (_proposalShown != l.ProposalId)
            {
                _proposalShown = l.ProposalId;
                WowLog.Info("UI: LFDDungeonReadyPopup for " + (l.ProposalDungeonName ?? "?") + " (proposal " + l.ProposalId + ").");
            }
            UiQk.Set(_readyName, l.ProposalDungeonName ?? "", UiArt.Gold);
            int myRole = l.MyProposalRole & ~LfgView.RoleLeader;
            int shown = (myRole & LfgView.RoleTank) != 0 ? LfgView.RoleTank : (myRole & LfgView.RoleHealer) != 0 ? LfgView.RoleHealer : LfgView.RoleDamage;
            Vector4 c = UiLfg.RoleCoords(shown);
            UiKit.Coords(_readyRoleIcon, c.x, c.y, c.z, c.w);
            UiQk.Set(_readyRole, shown == LfgView.RoleTank ? "Tank" : shown == LfgView.RoleHealer ? "Healer" : "Damage", Color.white);
            bool answered = false;
            for (int i = 0; i < 5; i++)
            {
                LfgMemberView m = i < l.ProposalMembers.Count ? l.ProposalMembers[i] : null;
                _marks[i].enabled = m != null;
                if (m == null) continue;
                if (m.IsMe && m.Answered) answered = true;
                string tex = !m.Answered ? @"Interface\RaidFrame\ReadyCheck-Waiting.blp" : m.Accepted ? @"Interface\RaidFrame\ReadyCheck-Ready.blp" : @"Interface\RaidFrame\ReadyCheck-NotReady.blp";   // ReadyCheck.lua:1-4
                Texture2D t = UiArt.Tex(tex);
                if (_marks[i].texture != t) _marks[i].texture = t;
            }
            UiQk.Set(_readyTime, l.ProposalSecondsLeft > 0f ? UiQk.Mmss(l.ProposalSecondsLeft) : "", UiArt.Gold);   // ours
            _enter.Interactable = !answered;
            _leave.Interactable = !answered;
        }
    }
}
