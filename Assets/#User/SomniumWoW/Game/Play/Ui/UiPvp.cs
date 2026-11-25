// iteration 11 - owner queues (proposal-interact.md 4.6 / 4.9 / 6.3.6): WoW's PvP window (PVPParentFrame: the PvP / honor tab and the
// Battlegrounds tab), the battlemaster's BattlefieldFrame, the scoreboard (WorldStateScoreFrame) and UiQueues - the hub of the queue icon
// (MiniMapBattlefieldFrame), the Dungeon Finder eye (MiniMapLFGFrame), the queue popups (CONFIRM_BATTLEFIELD_ENTRY, VOTE_BOOT_PLAYER,
// LFG_OFFER_CONTINUE) and the Dungeon Finder's role check / dungeon ready frames. Desktop: the mouse; VR: the laser / poke (the session
// windows on uicore's dialog surface, the icons and the Dungeon Finder popups on their own world surfaces). Online only: offline the PvP
// window says so (ours). Texts are WoW's (GlobalStrings.lua line in the comment) unless marked ours.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    // =============================================================================================================== PVPParentFrame

    public sealed class UiPvp : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.PvP, Frame = "PVPParentFrame", TitleKey = "PLAYER_V_PLAYER", Title = "Player vs. Player",   // GlobalStrings.lua:5689
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, Binding = "TOGGLECHARACTER4", Micro = "#PVP", MicroOrder = 6, MinLevel = 10,   // UIPanelWindows PVPParentFrame left 0 whileDead (UIParent.lua:52); SHOW_PVP_LEVEL (Constants.lua:130)
                Closed = s => { IWowInteractions ix = s != null ? s.Interactions : null; if (ix != null && ix.Battlefield.List != null && ix.Battlefield.List.BattlemasterGuid == 0) ix.CloseBattlefieldList(); },
                Owner = "queues", Hidden = false,
                Build = (parent, ctx, close) => new UiPvp(parent, ctx, close)
            };
        }

        /// <summary>The scoreboard (WorldStateScoreFrame) is wanted open: the queue icon's click in a battle, the PvP window, the end of a battle.</summary>
        public static bool ScoreOpen;

        private const string Gen = @"Interface\PaperDollInfoFrame\UI-Character-General-", Bf = @"Interface\BattlefieldFrame\";
        private readonly Action _close;
        private readonly RectTransform _honorArt, _bgArt, _honor, _bgs;
        private readonly TextMeshProUGUI _title, _notice, _bgInfo;
        private readonly UiQk.Tab[] _tabs = new UiQk.Tab[2];
        private readonly TextMeshProUGUI[] _honorCells = new TextMeshProUGUI[8];
        private readonly UiQk.Row[] _bgRows = new UiQk.Row[9], _instRows = new UiQk.Row[6];
        private readonly UiButton _join, _groupJoin;
        private List<BattlefieldListView> _rows = new List<BattlefieldListView>();
        private int _tab, _rowsLevel = -1, _selected = -1, _instance = -1, _bgTop, _version = -1;
        private bool _logged;

        public UiPvp(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "PVPParentFrame", 0f, 0f, W, H);
            // PVPFrame: the character frame's general art (PVPFrame.xml:63-99); PVPBattlegroundFrame: UI-Battlefield-TopLeft / -TopRight +
            // UI-QueueFromAnywhere-BotLeft / -BotRight (PVPBattlegroundFrame.xml:97-121)
            _honorArt = UiKit.Frame4(Root, "PvP art", Gen + "TopLeft.blp", Gen + "TopRight.blp", Gen + "BottomLeft.blp", Gen + "BottomRight.blp");
            _bgArt = UiKit.Frame4(Root, "Battlegrounds art", Bf + "UI-Battlefield-TopLeft.blp", Bf + "UI-Battlefield-TopRight.blp", Bf + "UI-QueueFromAnywhere-BotLeft.blp", Bf + "UI-QueueFromAnywhere-BotRight.blp");
            UiKit.Pic(Root, "Portrait", UiArt.Tex(Bf + "UI-Battlefield-Icon.blp"), 7f, 6f, 60f, 60f);   // UI-Battlefield-Icon (PVPBattlegroundFrame.xml:83) - ours on both tabs
            _title = Title("Player vs. Player", 17f);
            UiKit.CloseButton(Root, 384f - 32f - 30f, 8f, () => { if (_close != null) _close(); });            // TOPRIGHT (-30, -8) (ours, the character frame's)
            _notice = UiQk.Notice(Root, 40f, 150f, 300f, 120f);

            // ---- the PvP tab (honor): ours layout on the paper, WoW's labels
            _honor = UiKit.Rect(Root, "PvP", 0f, 0f, 384f, 512f);
            string[] heads = { "Today", "Yesterday", "Lifetime" };   // HONOR_TODAY :4192, HONOR_YESTERDAY :4193, HONOR_LIFETIME :4188
            for (int i = 0; i < 3; i++) UiKit.Text(_honor, "Head " + i, heads[i], 11f, UiArt.Gold, TextAlignmentOptions.Center, 150f + i * 66f, 90f, 66f, 14f);
            UiKit.Text(_honor, "Kills label", "Honorable Kills", 11f, UiArt.Gold, TextAlignmentOptions.Left, 30f, 112f, 120f, 14f);   // HONORABLE_KILLS :4180
            UiKit.Text(_honor, "Honor label", "Honor", 11f, UiArt.Gold, TextAlignmentOptions.Left, 30f, 132f, 120f, 14f);              // HONOR :4179
            for (int i = 0; i < 6; i++)
                _honorCells[i] = UiKit.Text(_honor, "Cell " + i, "", 11f, UiArt.White, TextAlignmentOptions.Center, 150f + (i % 3) * 66f, i < 3 ? 112f : 132f, 66f, 14f);
            UiKit.Text(_honor, "Honor points label", "Honor Points", 11f, UiArt.Gold, TextAlignmentOptions.Left, 30f, 170f, 150f, 14f);  // HONOR_POINTS :4189
            UiKit.Text(_honor, "Arena points label", "Arena Points", 11f, UiArt.Gold, TextAlignmentOptions.Left, 30f, 190f, 150f, 14f);  // ARENA_POINTS :563
            _honorCells[6] = UiKit.Text(_honor, "Honor points", "", 11f, UiArt.White, TextAlignmentOptions.Right, 180f, 170f, 100f, 14f);
            _honorCells[7] = UiKit.Text(_honor, "Arena points", "", 11f, UiArt.White, TextAlignmentOptions.Right, 180f, 190f, 100f, 14f);

            // ---- the Battlegrounds tab: the list (BattlemasterList.dbc) + the instance list + Join Battle / Join as Group
            _bgs = UiKit.Rect(Root, "Battlegrounds", 0f, 0f, 384f, 512f);
            for (int i = 0; i < _bgRows.Length; i++)
            {
                int k = i;
                _bgRows[i] = UiQk.MakeRow(_bgs, "Battleground " + i, 24f, 84f + i * 18f, 300f, 18f, new[] { 6f, 210f }, new[] { 200f, 86f }, 11f);
                _bgRows[i].B.Click = () => PickBattleground(k);
            }
            UiQk.ScrollPair(_bgs, 330f, 84f, 84f + 9f * 18f - 16f, () => { _bgTop = Math.Max(0, _bgTop - 1); _version = -1; }, () => { _bgTop++; _version = -1; });
            _bgInfo = UiKit.Text(_bgs, "Info", "", 11f, UiArt.Gold, TextAlignmentOptions.Left, 30f, 256f, 300f, 16f);
            for (int i = 0; i < _instRows.Length; i++)
            {
                int k = i;
                _instRows[i] = UiQk.MakeRow(_bgs, "Instance " + i, 24f, 276f + i * 18f, 300f, 18f, new[] { 6f }, new[] { 290f }, 11f);
                _instRows[i].B.Click = () => { _instance = k; _version = -1; };
            }
            _groupJoin = UiKit.PanelButton(_bgs, "Join as Group", 70f, 404f, 115f, 22f, () => Join(true));   // BATTLEFIELD_GROUP_JOIN :687
            _join = UiKit.PanelButton(_bgs, "Join Battle", 190f, 404f, 115f, 22f, () => Join(false));        // BATTLEFIELD_JOIN :691

            // tabs PVP / BATTLEGROUNDS (PVP.xml: PVPParentFrameTab1 / 2) - ours at the character frame's tab line
            _tabs[0] = UiQk.BottomTab(Root, "PvP", 11f, 434f, () => SetTab(0));                                   // PVP :5753
            _tabs[1] = UiQk.BottomTab(Root, "Battlegrounds", 11f + _tabs[0].W - 15f, 434f, () => SetTab(1));     // BATTLEGROUNDS :703
            SetTab(0);
            Root.gameObject.SetActive(false);
            UiQueues.Ensure(parent, ctx);
        }

        private void SetTab(int t)
        {
            if (t != _tab) UiSound.Named("igCharacterInfoTab");
            _tab = t;
            for (int i = 0; i < 2; i++) _tabs[i].Select(i == t);
            _version = -1;
            WowLog.Info("UI: PVPParentFrame tab " + (t == 0 ? "PvP" : "Battlegrounds") + ".");
        }

        private void PickBattleground(int rowIndex)
        {
            int i = _bgTop + rowIndex;
            if (i < 0 || i >= _rows.Count) return;
            _selected = i;
            _instance = -1;
            _version = -1;
            IWowInteractions ix = UiQk.Ix(Ctx);
            if (ix != null) ix.RequestBattlefieldList(_rows[i].BgTypeId);
            WowLog.Info("UI: PVPParentFrame - " + _rows[i].Name + " picked (CMSG_BATTLEFIELD_LIST).");
        }

        private void Join(bool group)
        {
            IWowInteractions ix = UiQk.Ix(Ctx);
            if (ix == null || _selected < 0 || _selected >= _rows.Count) return;
            BattlefieldListView l = ix.Battlefield.List;
            int inst = _instance > 0 && l != null && _instance - 1 < l.Instances.Count ? l.Instances[_instance - 1] : 0;
            WowLog.Info("UI: PVPParentFrame - " + (group ? "Join as Group" : "Join Battle") + " " + _rows[_selected].Name + ".");
            ix.JoinBattlefield(_rows[_selected].BgTypeId, inst, group);
        }

        protected override void OnOpen()
        {
            _version = -1;
            _rowsLevel = -1;
            WowLog.Info("UI: PVPParentFrame opened (" + (UiQk.Live(Ctx) ? "online" : "offline - the note") + (Ctx != null && Ctx.Vr ? ", VR" : ", desktop") + ").");
        }

        public override void Refresh(float now)
        {
            PlayerState p = Ctx != null ? Ctx.P : null;
            IWowInteractions ix = UiQk.Ix(Ctx);
            bool live = UiQk.Live(Ctx) && p != null;
            UiQk.Show(_honor, live && _tab == 0);
            UiQk.Show(_bgs, live && _tab == 1);
            UiQk.Show(_honorArt, _tab == 0);
            UiQk.Show(_bgArt, _tab == 1);
            UiQk.Show(_notice, !live);
            if (!live) { UiQk.Set(_notice, "Battlegrounds and arenas are on the online realm.", UiArt.Gold); return; }   // ours
            if (!_logged) { _logged = true; WowLog.Info("UI: PVPParentFrame first refresh (honor + " + _rows.Count + " battlegrounds)."); }
            if (_tab == 0)
            {
                _title.text = "Player vs. Player";
                PvpView v = ix.Pvp;
                UiQk.Set(_honorCells[0], v.TodayKills.ToString(), UiArt.White);
                UiQk.Set(_honorCells[1], v.YesterdayKills.ToString(), UiArt.White);
                UiQk.Set(_honorCells[2], p.LifetimeHonorableKills.ToString(), UiArt.White);
                UiQk.Set(_honorCells[3], v.TodayHonor.ToString(), UiArt.White);
                UiQk.Set(_honorCells[4], v.YesterdayHonor.ToString(), UiArt.White);
                UiQk.Set(_honorCells[5], "", UiArt.White);
                UiQk.Set(_honorCells[6], p.HonorPoints.ToString(), UiArt.White);
                UiQk.Set(_honorCells[7], p.ArenaPoints.ToString(), UiArt.White);
                return;
            }
            _title.text = "Battlegrounds";   // BATTLEGROUNDS :703
            OnlineSession os = UiQk.Online(Ctx);
            if (_rowsLevel != p.Level && os != null) { _rows = os.BgWindowRows(p.Level); _rowsLevel = p.Level; _version = -1; }
            BattlefieldListView l = ix.Battlefield.List;
            int ver = ix.Battlefield.Version * 31 + _bgTop * 7 + _selected * 3 + _instance;
            if (ver == _version) return;
            _version = ver;
            _bgTop = Mathf.Clamp(_bgTop, 0, Math.Max(0, _rows.Count - _bgRows.Length));
            for (int i = 0; i < _bgRows.Length; i++)
            {
                int k = _bgTop + i;
                UiQk.Row r = _bgRows[i];
                UiQk.Show(r.B, k < _rows.Count);
                if (k >= _rows.Count) continue;
                BattlefieldListView b = _rows[k];
                UiQk.Set(r.Col[0], b.Name, UiArt.Gold);
                UiQk.Set(r.Col[1], b.MaxLevel > 0 ? string.Format("({0} - {1})", b.MinLevel, b.MaxLevel) : "", UiArt.White);   // LFD_LEVEL_FORMAT_RANGE :4722 (ours here)
                r.Sel.enabled = k == _selected;
            }
            bool mine = l != null && l.BattlemasterGuid == 0 && _selected >= 0 && _selected < _rows.Count && l.BgTypeId == _rows[_selected].BgTypeId;
            int inst = mine ? l.Instances.Count + 1 : 0;
            for (int i = 0; i < _instRows.Length; i++)
            {
                UiQk.Row r = _instRows[i];
                UiQk.Show(r.B, i < inst);
                if (i >= inst) continue;
                UiQk.Set(r.Col[0], i == 0 ? "First Available" : (l.Name ?? "") + " " + l.Instances[i - 1], UiArt.Gold);   // FIRST_AVAILABLE :3703
                r.Sel.enabled = i == Math.Max(0, _instance);
            }
            UiQk.Set(_bgInfo, mine ? (l.Name ?? "") + (l.MaxLevel > 0 ? string.Format(" ({0} - {1})", l.MinLevel, l.MaxLevel) : "") : _selected >= 0 ? "..." : "", UiArt.Gold);
            _join.Interactable = mine;
            PlayerState pl = Ctx.P;
            bool leader = pl.GroupLeaderGuid != 0 && pl.GroupLeaderGuid == pl.Guid;
            _groupJoin.Interactable = mine && leader;
        }
    }

    // =============================================================================================================== BattlefieldFrame (the battlemaster)

    /// <summary>The battlemaster's BattlefieldFrame (BattlefieldFrame.xml 384 x 512): the instance list, Join Battle / Join as Group / Cancel.</summary>
    public sealed class UiBattlefield : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Battlefield, Frame = "BattlefieldFrame", TitleKey = "BATTLEFIELDS", Title = "Battlegrounds",   // GlobalStrings.lua:683
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, SessionWindow = true, Palm = false,                     // UIPanelWindows BattlefieldFrame left 0 whileDead (UIParent.lua:42)
                Wanted = s => s != null && s.Interactions != null && s.Interactions.Battlefield.List != null && s.Interactions.Battlefield.List.BattlemasterGuid != 0,
                NearGuid = s => s != null && s.Interactions != null && s.Interactions.Battlefield.List != null ? s.Interactions.Battlefield.List.BattlemasterGuid : 0,
                Closed = s => { if (s != null && s.Interactions != null) s.Interactions.CloseBattlefieldList(); },
                Owner = "queues", Hidden = false,
                Build = (parent, ctx, close) => new UiBattlefield(parent, ctx, close)
            };
        }

        private const string Bf = @"Interface\BattlefieldFrame\";
        private readonly Action _close;
        private readonly TextMeshProUGUI _title, _info;
        private readonly UiQk.Row[] _rows = new UiQk.Row[12];
        private readonly UiButton _join, _groupJoin;
        private int _instance, _top, _version = -1;

        public UiBattlefield(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "BattlefieldFrame", 0f, 0f, W, H);
            UiKit.Frame4(Root, "Art", Bf + "UI-Battlefield-TopLeft.blp", Bf + "UI-Battlefield-TopRight.blp", Bf + "UI-Battlefield-BotLeft.blp", Bf + "UI-Battlefield-BotRight.blp");   // BattlefieldFrame.xml:403-427
            UiKit.Pic(Root, "Portrait", UiArt.Tex(Bf + "UI-Battlefield-Icon.blp"), 7f, 6f, 60f, 60f);                // BattlefieldFrame.xml:389
            _title = Title("Battlegrounds", 17f);
            UiKit.CloseButton(Root, 384f - 32f - 30f, 8f, () => { if (_close != null) _close(); });
            _info = UiKit.Text(Root, "Info", "", 11f, UiArt.Gold, TextAlignmentOptions.TopLeft, 30f, 80f, 310f, 30f);
            for (int i = 0; i < _rows.Length; i++)
            {
                int k = i;
                _rows[i] = UiQk.MakeRow(Root, "Instance " + i, 24f, 116f + i * 18f, 300f, 18f, new[] { 6f }, new[] { 290f }, 11f);   // list 333 x 241 (ours rows)
                _rows[i].B.Click = () => { _instance = _top + k; _version = -1; };
            }
            UiQk.ScrollPair(Root, 330f, 116f, 116f + 12f * 18f - 16f, () => { _top = Math.Max(0, _top - 1); _version = -1; }, () => { _top++; _version = -1; });
            // BattlefieldFrameGroupJoinButton / JoinButton / CancelButton (BattlefieldFrame.xml: Cancel at (305, -423)) - 115 / 77 x 22 (ours widths)
            _groupJoin = UiKit.PanelButton(Root, "Join as Group", 68f, 423f, 115f, 22f, () => Join(true));     // BATTLEFIELD_GROUP_JOIN :687
            _join = UiKit.PanelButton(Root, "Join Battle", 186f, 423f, 115f, 22f, () => Join(false));          // BATTLEFIELD_JOIN :691
            UiKit.PanelButton(Root, "Cancel", 305f, 423f, 77f, 22f, () => { if (_close != null) _close(); });  // CANCEL :1338
            Root.gameObject.SetActive(false);
            UiQueues.Ensure(parent, ctx);
        }

        private void Join(bool group)
        {
            IWowInteractions ix = UiQk.Ix(Ctx);
            BattlefieldListView l = ix != null ? ix.Battlefield.List : null;
            if (l == null) return;
            int inst = _instance > 0 && _instance - 1 < l.Instances.Count ? l.Instances[_instance - 1] : 0;
            WowLog.Info("UI: BattlefieldFrame - " + (group ? "Join as Group" : "Join Battle") + " " + (l.Name ?? "?") + (inst > 0 ? " " + inst : " (first available)") + ".");
            ix.JoinBattlefield(l.BgTypeId, inst, group);
            if (_close != null) _close();   // BattlefieldFrameJoinButton_OnClick hides the frame (BattlefieldFrame.lua)
        }

        protected override void OnOpen()
        {
            _instance = 0;
            _top = 0;
            _version = -1;
            IWowInteractions ix = UiQk.Ix(Ctx);
            BattlefieldListView l = ix != null ? ix.Battlefield.List : null;
            WowLog.Info("UI: BattlefieldFrame opened (" + (l != null ? l.Name + ", " + l.Instances.Count + " instances" : "?") + (Ctx != null && Ctx.Vr ? ", VR" : ", desktop") + ").");
        }

        public override void Refresh(float now)
        {
            IWowInteractions ix = UiQk.Ix(Ctx);
            BattlefieldListView l = ix != null ? ix.Battlefield.List : null;
            if (l == null || Ctx.P == null) return;
            int ver = ix.Battlefield.Version * 31 + _top * 7 + _instance;
            if (ver == _version) return;
            _version = ver;
            _title.text = l.Name ?? "Battlegrounds";
            UiQk.Set(_info, (l.MaxLevel > 0 ? string.Format("({0} - {1})", l.MinLevel, l.MaxLevel) : "") +
                (l.HasWin ? "\nWin: " + l.WinHonor + " honor" + (l.WinArena > 0 ? ", " + l.WinArena + " arena points" : "") + "   Loss: " + l.LossHonor + " honor" : ""), UiArt.Gold);   // ours
            int n = l.Instances.Count + 1;
            _top = Mathf.Clamp(_top, 0, Math.Max(0, n - _rows.Length));
            for (int i = 0; i < _rows.Length; i++)
            {
                int k = _top + i;
                UiQk.Show(_rows[i].B, k < n);
                if (k >= n) continue;
                UiQk.Set(_rows[i].Col[0], k == 0 ? "First Available" : (l.Name ?? "") + " " + l.Instances[k - 1], UiArt.Gold);   // FIRST_AVAILABLE :3703
                _rows[i].Sel.enabled = k == _instance;
            }
            PlayerState p = Ctx.P;
            _groupJoin.Interactable = p.GroupLeaderGuid != 0 && p.GroupLeaderGuid == p.Guid;
            _join.Interactable = true;
        }
    }

    // =============================================================================================================== WorldStateScoreFrame

    /// <summary>WorldStateScoreFrame (WorldStateFrame.xml:737-1467, 640 x 512, CENTER): the columns, All / Alliance / Horde, the winner, Leave.</summary>
    public sealed class UiBattlefieldScore : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.BattlefieldScore, Frame = "WorldStateScoreFrame", Area = UiPanelArea.Center, WhileDead = true,   // UIPanelWindows CENTER 0 whileDead (UIParent.lua:44)
                Width = 640f, Height = 512f, SessionWindow = true, Palm = false,
                Wanted = s => UiPvp.ScoreOpen && s != null && s.Interactions != null && s.Interactions.Available,
                Closed = s => { UiPvp.ScoreOpen = false; },
                Owner = "queues", Hidden = false,
                Build = (parent, ctx, close) => new UiBattlefieldScore(parent, ctx, close)
            };
        }

        private const string Ws = @"Interface\WorldStateFrame\WorldStateFinalScoreFrame-";
        private const int Rows = 18;
        private static readonly float[] ColX = { 30f, 190f, 250f, 305f, 355f, 410f, 480f, 550f };
        private static readonly float[] ColW = { 158f, 58f, 53f, 48f, 53f, 68f, 68f, 70f };
        private readonly Action _close;
        private readonly TextMeshProUGUI _title, _victory;
        private readonly UiQk.Row[] _rows = new UiQk.Row[Rows];
        private readonly UiButton[] _filters = new UiButton[3];
        private readonly UiButton _leave;
        private readonly List<BattlefieldScoreView> _shown = new List<BattlefieldScoreView>();
        private int _filter = -1, _top, _version = -1;
        private float _nextAsk;

        public UiBattlefieldScore(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            W = 640f;
            H = 512f;
            Root = UiKit.Rect(parent, "WorldStateScoreFrame", 0f, 0f, W, H);
            // the art (WorldStateFrame.xml:748-800): TopLeft 128 x 256 at (8, -16), TopRight 256 x 256 at TOPRIGHT (0, -16), Top between; the Bot row below
            UiKit.Pic(Root, "TopLeft", UiArt.Tex(Ws + "TopLeft.blp"), 8f, 16f, 128f, 256f);
            UiKit.Pic(Root, "Top", UiArt.Tex(Ws + "Top.blp"), 136f, 16f, 640f - 256f - 136f, 256f);
            UiKit.Pic(Root, "TopRight", UiArt.Tex(Ws + "TopRight.blp"), 640f - 256f, 16f, 256f, 256f);
            UiKit.Pic(Root, "BotLeft", UiArt.Tex(Ws + "BotLeft.blp"), 8f, 272f, 128f, 256f);
            UiKit.Pic(Root, "Bot", UiArt.Tex(Ws + "Bot.blp"), 136f, 272f, 640f - 256f - 136f, 256f);
            UiKit.Pic(Root, "BotRight", UiArt.Tex(Ws + "BotRight.blp"), 640f - 256f, 272f, 256f, 256f);
            _title = UiKit.Text(Root, "Title", "", 14f, UiArt.Gold, TextAlignmentOptions.Center, 120f, 30f, 400f, 18f);
            UiKit.CloseButton(Root, 640f - 40f, 20f, () => { if (_close != null) _close(); });
            // column heads (WorldStateFrame.xml:889-1060): NAME, KILLING_BLOWS, HONORABLE_KILLS, DEATHS, SCORE_HONOR_GAINED, DAMAGE, HEALING + the objectives (ours)
            string[] heads = { "Name", "Killing Blows", "Honorable Kills", "Deaths", "Honor Gained", "Damage", "Healing", "" };   // :5060 :4657 :4180 :1930 :6187 :1905 HEALING (WS.xml)
            for (int i = 0; i < heads.Length; i++)
            {
                TextMeshProUGUI t = UiKit.Text(Root, "Head " + i, heads[i], 9f, UiArt.Gold, i == 0 ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.Bottom, ColX[i], 62f, ColW[i], 24f);
                t.textWrappingMode = TextWrappingModes.Normal;
            }
            for (int i = 0; i < Rows; i++) _rows[i] = UiQk.MakeRow(Root, "Score " + i, 24f, 92f + i * 16f, 600f, 16f, ColX.Shift(-24f), ColW, 10f);
            UiQk.ScrollPair(Root, 604f, 92f, 92f + Rows * 16f - 16f, () => { _top = Math.Max(0, _top - 1); _version = -1; }, () => { _top++; _version = -1; });
            _victory = UiKit.Text(Root, "Victory", "", 16f, UiArt.Gold, TextAlignmentOptions.Center, 120f, 386f, 400f, 22f);
            string[] filt = { "All", "Alliance", "Horde" };   // ALL :526, FACTION_ALLIANCE :3640, FACTION_HORDE :3642 (WorldStateScoreFrameTab1-3, WS.xml:1432-1467)
            for (int i = 0; i < 3; i++)
            {
                int f = i - 1;
                _filters[i] = UiKit.PanelButton(Root, filt[i], 30f + i * 92f, 420f, 90f, 22f, () => { _filter = f; _version = -1; });
            }
            _leave = UiKit.PanelButton(Root, "Leave Battleground", 450f, 420f, 150f, 22f, Leave);   // LEAVE_BATTLEGROUND :4698 / LEAVE_ARENA :4697
            Root.gameObject.SetActive(false);
            UiQueues.Ensure(parent, ctx);
        }

        private void Leave()
        {
            IWowInteractions ix = UiQk.Ix(Ctx);
            if (ix == null) return;
            WowLog.Info("UI: WorldStateScoreFrame - Leave (CMSG_LEAVE_BATTLEFIELD).");
            ix.LeaveBattlefield();
            if (_close != null) _close();
        }

        protected override void OnOpen()
        {
            _version = -1;
            _top = 0;
            _nextAsk = 0f;
            WowLog.Info("UI: WorldStateScoreFrame opened" + (Ctx != null && Ctx.Vr ? " (VR)." : " (desktop)."));
        }

        public override void Refresh(float now)
        {
            IWowInteractions ix = UiQk.Ix(Ctx);
            if (ix == null || !ix.Available) return;
            BattlefieldView bf = ix.Battlefield;
            // MSG_PVP_LOG_DATA every 5 s while open and the battle runs (ours; WoW asks on show and on UPDATE_BATTLEFIELD_SCORE)
            if (!bf.Ended && bf.InBattleground && Time.unscaledTime >= _nextAsk) { _nextAsk = Time.unscaledTime + 5f; ix.RequestScores(); }
            int ver = bf.Version * 31 + _top * 5 + _filter;
            if (ver == _version) return;
            _version = ver;
            string name = null;
            for (int i = 0; i < bf.Queues.Length; i++) if (bf.Queues[i].Status == BattlefieldStatus.InProgress) name = bf.Queues[i].Name;
            _title.text = name ?? "";
            _leave.SetLabel(bf.ScoresArena ? "Leave Arena" : "Leave Battleground");   // LEAVE_ARENA :4697 / LEAVE_BATTLEGROUND :4698
            _leave.Interactable = bf.InBattleground;
            for (int i = 0; i < 3; i++) UiQk.Show(_filters[i], !bf.ScoresArena);
            _shown.Clear();
            for (int i = 0; i < bf.Scores.Count; i++)
            {
                BattlefieldScoreView sc = bf.Scores[i];
                if (_filter < 0 || bf.ScoresArena || sc.Team == (_filter == 0 ? 1 : 0)) _shown.Add(sc);
            }
            _top = Mathf.Clamp(_top, 0, Math.Max(0, _shown.Count - Rows));
            for (int i = 0; i < Rows; i++)
            {
                int k = _top + i;
                UiQk.Row r = _rows[i];
                UiQk.Show(r.B, k < _shown.Count);
                if (k >= _shown.Count) continue;
                BattlefieldScoreView sc = _shown[k];
                Color team = sc.Team == 1 ? UiQk.AllianceBlue : sc.Team == 0 ? UiQk.HordeRed : UiArt.White;
                UiQk.Set(r.Col[0], sc.Name ?? "?", team);
                UiQk.Set(r.Col[1], sc.KillingBlows.ToString(), UiArt.White);
                UiQk.Set(r.Col[2], sc.HonorableKills.ToString(), UiArt.White);
                UiQk.Set(r.Col[3], sc.Deaths.ToString(), UiArt.White);
                UiQk.Set(r.Col[4], sc.BonusHonor.ToString(), UiArt.White);
                UiQk.Set(r.Col[5], sc.Damage.ToString(), UiArt.White);
                UiQk.Set(r.Col[6], sc.Healing.ToString(), UiArt.White);
                UiQk.Set(r.Col[7], sc.Objectives != null && sc.Objectives.Length > 0 ? string.Join(" / ", Array.ConvertAll(sc.Objectives, o => o.ToString())) : "", UiArt.White);   // ours: the battle's own columns as numbers
                for (int c = 1; c < r.Col.Length; c++) r.Col[c].alignment = TextAlignmentOptions.Center;
            }
            string win = null;
            if (bf.Ended && bf.Winner >= 0)
                win = bf.ScoresArena ? (bf.Winner == 0 ? "Green Team Wins" : "Gold Team Wins") : (bf.Winner == 0 ? "Horde Wins" : "Alliance Wins");   // VICTORY_TEXT_ARENA0/1 :7942-7943, VICTORY_TEXT0/1 :7940-7941
            UiQk.Set(_victory, win ?? "", UiArt.Gold);
        }
    }

    // =============================================================================================================== the hub

    /// <summary>
    /// The queues hub (ours, proposal-interact.md 6.3.2 / 6.3.6 / 6.3.8): the queue icon and the Dungeon Finder eye (desktop: top right where WoW's
    /// minimap cluster is; VR: a small floating surface right of the view, mirroring the HUD's place on the left), their tooltips and menus, the
    /// popups CONFIRM_BATTLEFIELD_ENTRY / VOTE_BOOT_PLAYER / LFG_OFFER_CONTINUE (uicore's UiPopup, proposal 6.3.2's Which names - shown only when
    /// no other module shows the same Which), the Dungeon Finder role check and dungeon ready frames (UiLfgPopups) and the scoreboard opening when
    /// a battle ends. Installed on the arm UI's object by Ensure (called by this area's window constructors) or by WowGame (request). Inert
    /// offline and without a session; nothing is built before the first online frame.
    /// </summary>
    [DefaultExecutionOrder(10013)]
    public sealed class UiQueues : MonoBehaviour
    {
        private static UiQueues _inst;
        private static UiContext _ctx;

        /// <summary>Adds the hub once (to WowArmUi's object, else the given UI's root); remembers the arm UI's context.</summary>
        public static void Ensure(Transform anyUi, UiContext ctx)
        {
            if (ctx != null) _ctx = ctx;
            if (_inst != null) return;
            WowArmUi arm = anyUi != null ? anyUi.GetComponentInParent<WowArmUi>() : null;
            if (arm == null) arm = UnityEngine.Object.FindFirstObjectByType<WowArmUi>();
            GameObject host = arm != null ? arm.gameObject : anyUi != null ? anyUi.root.gameObject : null;
            if (host == null) return;
            _inst = host.GetComponent<UiQueues>();
            if (_inst == null) _inst = host.AddComponent<UiQueues>();
        }

        // VR placement (ours): the HUD's distance, mirrored yaw, the same pitch and follow speed (UiVrHud: Distance 1, Yaw -17, Pitch 14, Follow 5)
        private const float VrDistance = 1f, VrYaw = 17f, VrPitch = 14f, VrFollow = 5f, VrMetresPerUnit = 0.0007f, PopupMetresPerUnit = 0.0012f;
        private const float IconsW = 160f, IconsH = 190f;

        private sealed class IconSet
        {
            public RectTransform Host, Menu;
            public UiButton Bf, Eye;
            public RawImage EyeArt;
            public readonly UiButton[] Items = new UiButton[6];
        }

        private UiSurface _desk, _vrIcons, _vrPopup;
        private IconSet _deskSet, _vrSet;
        private UiLfgPopups _deskLfd, _vrLfd;
        private bool _built, _vrPlaced, _vrIconsShown, _vrPopupShown, _endedSeen, _bootShown, _menuOpen, _menuEye;
        private readonly string[] _entryKey = new string[2];
        private readonly BattlefieldStatus[] _lastStatus = new BattlefieldStatus[2];
        private int _offerVersion, _eyeFrame;
        private float _eyeAt;
        private LfgStatus _lastLfg;
        private bool _roleAskedSeen, _proposalSeen;
        private readonly HashSet<ulong> _proposalAccepted = new HashSet<ulong>();

        /// <summary>duelsnd: LFDDungeonReadyStatus_UpdateIcon (LFDFrame.lua:675-686) - once our answer is in, the status frame shows the
        /// members and plays "LFG_RoleCheck" for each one whose readyStatus turns "accepted"; LFG_PROPOSAL_SHOW resets them
        /// (LFDDungeonReadyStatus_ResetReadyStates :52, :668-673). Ours: one play per frame however many turned at once.</summary>
        private void ProposalAcceptSounds(LfgView lfg, bool fresh)
        {
            if (fresh || !lfg.ProposalAsked) _proposalAccepted.Clear();
            if (!lfg.ProposalAsked) return;
            bool answered = false;
            for (int i = 0; i < lfg.ProposalMembers.Count; i++) if (lfg.ProposalMembers[i].IsMe && lfg.ProposalMembers[i].Answered) answered = true;
            if (!answered) return;   // LFDDungeonReadyPopup_Update shows the status frame only after our own answer (hasResponded, :488-493)
            bool play = false;
            for (int i = 0; i < lfg.ProposalMembers.Count; i++)
            {
                LfgMemberView m = lfg.ProposalMembers[i];
                if (m.Answered && m.Accepted && _proposalAccepted.Add(m.Guid)) play = true;
            }
            if (play) UiSound.Added("LFG_RoleCheck", "LFDDungeonReadyStatus_UpdateIcon, LFDFrame.lua:685");
        }
        private readonly HashSet<string> _logged = new HashSet<string>();

        private void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(this); return; }
            _inst = this;
        }

        private void OnDestroy()
        {
            if (_inst == this) _inst = null;
            RemoveExtra(_vrIcons);
            RemoveExtra(_vrPopup);
        }

        private void Log(string key, string text)
        {
            if (_logged.Add(key)) WowLog.Info(text);
        }

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            IWowInteractions ix = s != null ? s.Interactions : null;
            PlayerState p = s != null ? s.Player : null;
            if (ix == null || !ix.Available || p == null) { HideAll(); return; }
            try { Tick(s, ix, p); }
            catch (Exception e) { Log("tick fail " + e.GetType().Name, "UI: queues hub failed: " + e); }
        }

        private void HideAll()
        {
            if (!_built) return;
            if (_desk != null && _desk.Shown) _desk.SetShown(false);
            ShowVrIcons(false);
            ShowVrPopup(false);
            for (int i = 0; i < 2; i++) _entryKey[i] = null;
        }

        private void Tick(IWowSession s, IWowInteractions ix, PlayerState p)
        {
            Build();
            bool vr = _ctx != null ? _ctx.Vr : WowPlayer.XrPresent;
            BattlefieldView bf = ix.Battlefield;
            LfgView lfg = ix.Lfg;
            OnlineSession os = s as OnlineSession;
            Popups(ix, lfg, bf, os, p);

            bool anyQueue = false;
            for (int i = 0; i < bf.Queues.Length; i++)
            {
                BattlefieldStatus st = bf.Queues[i].Status;
                if (st != BattlefieldStatus.None) anyQueue = true;
                if (st == BattlefieldStatus.Queued && _lastStatus[i] == BattlefieldStatus.None) UiSound.Named("PVPENTERQUEUE");   // BF.u.lua:276
                _lastStatus[i] = st;
            }
            if (bf.Ended && !_endedSeen) { _endedSeen = true; UiPvp.ScoreOpen = true; Log("score end", "UI: the battle ended - the scoreboard opens (WorldStateScoreFrame)."); }
            if (!bf.Ended) _endedSeen = false;
            bool eye = lfg.Status != LfgStatus.None && lfg.Status != LfgStatus.Finished;
            if (lfg.Status != _lastLfg) { _lastLfg = lfg.Status; if (eye) Log("eye " + lfg.Status, "UI: the Dungeon Finder eye shows (" + lfg.Status + ")."); }
            if (!anyQueue && !eye) _menuOpen = false;
            if (anyQueue) Log("bf icon", "UI: the queue icon (MiniMapBattlefieldFrame) shows.");

            // the eye's animation (AnimateTexCoords, UIParent.lua:2936-2959: LFG-Eye 512 x 256, 64 x 64 frames, 8 a row, 29 frames, 0.1 s) while
            // queued or in the role check (Minimap.lua:213-225), frame 1 otherwise
            bool animate = lfg.Status == LfgStatus.Queued || lfg.Status == LfgStatus.RoleCheck;
            if (!animate) _eyeFrame = 0;
            else if (Time.unscaledTime >= _eyeAt) { _eyeAt = Time.unscaledTime + 0.1f; _eyeFrame = (_eyeFrame + 1) % 29; }

            IconSet set = vr ? _vrSet : _deskSet;
            IconSet other = vr ? _deskSet : _vrSet;
            UiQk.Show(other.Host, false);
            UiQk.Show(set.Host, anyQueue || eye);
            UiQk.Show(set.Bf, anyQueue);
            UiQk.Show(set.Eye, eye);
            if (eye)
            {
                int col = _eyeFrame % 8, row = _eyeFrame / 8;
                UiKit.Coords(set.EyeArt, col * 0.125f, col * 0.125f + 0.125f, row * 0.25f, row * 0.25f + 0.25f);
            }
            UiQk.Show(set.Menu, _menuOpen);
            UiQk.Show(other.Menu, false);
            if (_menuOpen) FillMenu(set, ix, bf, lfg);

            // the Dungeon Finder frames
            UiLfgPopups lfd = vr ? _vrLfd : _deskLfd, lfdOther = vr ? _deskLfd : _vrLfd;
            lfdOther.Hide();
            bool asked = lfg.RoleCheckAsked, proposal = lfg.ProposalAsked;
            // duelsnd: LFG_ROLE_CHECK_SHOW only shows LFDRoleCheckPopup (LFDFrame.lua:60-61, StaticPopupSpecial_Show - no sound);
            // "LFG_RoleCheck" belongs to LFDDungeonReadyStatus_UpdateIcon: a proposal member whose answer turns "accepted" (:675-686).
            if (asked && !_roleAskedSeen) { UiQk.Pulse(vr); Log("role check", "UI: LFDRoleCheckPopup shown (LFDFrame.lua:60-61)."); }
            if (proposal && !_proposalSeen) { UiSound.Named("ReadyCheck"); UiQk.Pulse(vr); Log("proposal", "UI: LFDDungeonReadyPopup shown (LFD.lua:54)."); }
            ProposalAcceptSounds(lfg, proposal && !_proposalSeen);
            _roleAskedSeen = asked;
            _proposalSeen = proposal;
            lfd.Refresh(ix, os, p);
            bool lfdUp = lfd.AnyShown;

            if (_desk.Shown != (!vr && (anyQueue || eye || lfdUp))) _desk.SetShown(!vr && (anyQueue || eye || lfdUp));
            if (!vr) _desk.Tick();
            ShowVrIcons(vr && (anyQueue || eye));
            if (_vrIconsShown) { PlaceVrIcons(); _vrIcons.Tick(); }
            bool popupWas = _vrPopupShown;
            ShowVrPopup(vr && lfdUp);
            if (_vrPopupShown)
            {
                if (!popupWas) PlaceInFront(_vrPopup, 0.9f, -0.05f);
                _vrPopup.Tick();
            }
        }

        // ------------------------------------------------------------------ popups

        private void Popups(IWowInteractions ix, LfgView lfg, BattlefieldView bf, OnlineSession os, PlayerState p)
        {
            for (int i = 0; i < bf.Queues.Length; i++)
            {
                BattlefieldQueueView q = bf.Queues[i];
                string which = "CONFIRM_BATTLEFIELD_ENTRY#" + i;
                string key = q.Status == BattlefieldStatus.Invited ? q.BgTypeId + "/" + q.ArenaType + "/" + q.ClientInstanceId : null;
                if (key == null)
                {
                    if (_entryKey[i] != null) { UiPopup.HideWhich(which); _entryKey[i] = null; }
                    continue;
                }
                if (_entryKey[i] == key) continue;   // once per invite: Escape hides it without leaving (R-U1); the icon's menu offers it again
                _entryKey[i] = key;
                if (UiPopup.FindVisible(which) != 0) continue;   // another module shows it already
                ShowEntry(ix, q, i);
            }
            bool boot = lfg.BootActive && !lfg.BootVoted && lfg.BootVictim != p.Guid;
            if (boot && !_bootShown)
            {
                _bootShown = true;
                if (UiPopup.FindVisible("VOTE_BOOT_PLAYER") == 0)
                {
                    string victim = lfg.BootVictimName ?? "?";
                    // VOTE_BOOT_PLAYER (GlobalStrings.lua:8910; StaticPopup.lua:2752-2767: YES / NO, showAlert, whileDead, timeout 0)
                    UiPopup.Show(new UiPopupSpec
                    {
                        Which = "VOTE_BOOT_PLAYER", Text = "A vote has been initiated to remove " + victim + " from the group.\n\nThe reason given was:\n<color=#ffd200>" + (lfg.BootReason ?? "") + "</color>\n\nDo you want to kick " + victim + "?",
                        Button1 = "Yes", Button2 = "No", OnAccept = () => ix.LfgBootVote(true), OnCancel = () => ix.LfgBootVote(false),   // YES :8982, NO :5181
                        ShowAlert = true, WhileDead = true, Timeout = 0f, HideOnEscape = false, NoCancelOnReuse = true
                    });
                    UiQk.Pulse(_ctx != null && _ctx.Vr);
                    WowLog.Info("UI: VOTE_BOOT_PLAYER shown (" + victim + ").");
                }
            }
            else if (!boot && _bootShown) { _bootShown = false; UiPopup.HideWhich("VOTE_BOOT_PLAYER"); }

            if (os != null && os.LfgOfferVersion != _offerVersion)
            {
                _offerVersion = os.LfgOfferVersion;
                int entry = os.LfgOfferContinueEntry;
                if (entry != 0 && UiPopup.FindVisible("LFG_OFFER_CONTINUE") == 0)
                {
                    string name = os.LfName(entry & 0xFFFFFF);
                    // LFG_OFFER_CONTINUE (GlobalStrings.lua:4735; StaticPopup.lua:2804-2813: YES -> PartyLFGStartBackfill = CMSG_LFG_JOIN from the
                    // LFG group, LFGMgr.cpp:609-626 isContinue; NO; noCancelOnReuse, timeout 0)
                    UiPopup.Show(new UiPopupSpec
                    {
                        Which = "LFG_OFFER_CONTINUE", Text = "A player has left your Dungeon group. Would you like to find another player to finish " + name + "?",
                        Button1 = "Yes", Button2 = "No", NoCancelOnReuse = true, Timeout = 0f, WhileDead = true,
                        OnAccept = () => { ix.JoinLfg(UiLfg.RolesForJoin(lfg, p), new List<int> { entry }); os.LfgClearOffer(); },
                        OnCancel = () => os.LfgClearOffer()
                    });
                    WowLog.Info("UI: LFG_OFFER_CONTINUE shown (" + name + ").");
                }
            }
        }

        private static void ShowEntry(IWowInteractions ix, BattlefieldQueueView q, int slot)
        {
            bool arena = q.ArenaType > 0;
            // CONFIRM_BATTLEFIELD_ENTRY (GlobalStrings.lua:1801; StaticPopup.lua:486-519: ENTER_BATTLE :2821 / LEAVE_QUEUE :4700 - the second
            // disabled for arenas (ours: not shown), timeout 0, whileDead, hideOnEscape + noCancelOnEscape (Escape hides WITHOUT leaving the queue))
            UiPopup.Show(new UiPopupSpec
            {
                Which = "CONFIRM_BATTLEFIELD_ENTRY#" + slot, Text = "You are now eligible to enter " + (q.Name ?? "?") + ", choose an action:",
                Button1 = "Enter Battle", Button2 = arena ? null : "Leave Queue",
                OnAccept = () => ix.AnswerBattlefieldInvite(slot, true),
                OnCancel = arena ? (Action)null : () => ix.AnswerBattlefieldInvite(slot, false),
                Timeout = 0f, WhileDead = true, HideOnEscape = true, NoCancelOnEscape = true, NoCancelOnReuse = true, Sound = "PVPTHROUGHQUEUE"   // BF.u.lua:291
            });
            UiQk.Pulse(_ctx != null && _ctx.Vr);
            WowLog.Info("UI: CONFIRM_BATTLEFIELD_ENTRY shown (" + (q.Name ?? "?") + ", slot " + slot + (arena ? ", arena" : "") + ").");
        }

        // ------------------------------------------------------------------ icons + menu

        private string BfTip()
        {
            IWowInteractions ix = WowPlay.Session != null ? WowPlay.Session.Interactions : null;
            if (ix == null) return null;
            var sb = new System.Text.StringBuilder();
            foreach (BattlefieldQueueView q in ix.Battlefield.Queues)
            {
                string t = null;
                if (q.Status == BattlefieldStatus.Queued)
                    t = "You are in the queue for " + q.Name + "\nAverage wait time: " + UiQk.Mmss(q.AvgWaitSeconds) + " (Last 10 players)\nTime in queue: " + UiQk.Mmss(q.QueuedSeconds);   // BATTLEFIELD_IN_QUEUE :689
                else if (q.Status == BattlefieldStatus.Invited)
                    t = "You are eligible to enter " + q.Name + "\nYou will be removed from the queue in " + UiQk.Mmss(q.SecondsLeft);   // BATTLEFIELD_QUEUE_CONFIRM :698
                else if (q.Status == BattlefieldStatus.InProgress)
                    t = "You are in " + q.Name + "\n<color=#ffffff><Left-Click To Show Score></color>";   // BATTLEFIELD_IN_BATTLEFIELD :688 (the zone map line: ours, no battle map here)
                if (t == null) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(t);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private string EyeTip()
        {
            IWowInteractions ix = WowPlay.Session != null ? WowPlay.Session.Interactions : null;
            LfgView l = ix != null ? ix.Lfg : null;
            if (l == null) return null;
            string head = "Dungeon Finder";   // LOOKING_FOR_DUNGEON :4783
            switch (l.Status)
            {
                case LfgStatus.RoleCheck: return head + "\n" + "Confirm your role:";   // CONFIRM_YOUR_ROLE :1844
                case LfgStatus.Queued:
                    return head + "\nYou are queued as: " + OnlineSession.LfRoleNames(l.MyRoles) + "\nAverage Wait Time: " + UiQk.Mmss(l.AvgWait) +   // SEARCHING_FOR_GROUPS_NEEDS :6195, AVERAGE_WAIT_TIME :666
                           "\n" + "Time In Queue: " + UiQk.Mmss(l.QueuedSeconds);                                                                  // TIME_IN_QUEUE :7567
                case LfgStatus.Proposal: return head + "\n" + "Assembling Group...";   // ASSEMBLING_GROUP :585
                case LfgStatus.InDungeon: case LfgStatus.Boot: return head + (l.ProposalDungeonName != null ? "\n" + l.ProposalDungeonName : "");
                default: return head;
            }
        }

        private void BfClick()
        {
            IWowInteractions ix = WowPlay.Session != null ? WowPlay.Session.Interactions : null;
            if (ix == null) return;
            // in a battle with no other queue: the click shows the score (BF.u.lua:590-680); else the menu (MiniMapBattlefieldDropDown)
            int inBattle = -1, others = 0;
            for (int i = 0; i < ix.Battlefield.Queues.Length; i++)
            {
                BattlefieldStatus st = ix.Battlefield.Queues[i].Status;
                if (st == BattlefieldStatus.InProgress) inBattle = i; else if (st != BattlefieldStatus.None) others++;
            }
            if (inBattle >= 0 && others == 0) { UiPvp.ScoreOpen = !UiPvp.ScoreOpen; WowLog.Info("UI: queue icon - the scoreboard " + (UiPvp.ScoreOpen ? "shown." : "hidden.")); return; }
            _menuOpen = !_menuOpen || _menuEye;
            _menuEye = false;
        }

        private void EyeClick(bool right)
        {
            bool vr = _ctx != null && _ctx.Vr;
            if (!right && !vr)   // MiniMapLFGFrame: left click = the Dungeon Finder, right click = the menu (Minimap.lua)
            {
                if (_ctx != null && _ctx.Toggle != null) _ctx.Toggle(UiWindowKind.DungeonFinder);
                _menuOpen = false;
                return;
            }
            _menuOpen = !_menuOpen || !_menuEye;
            _menuEye = true;
        }

        private void FillMenu(IconSet set, IWowInteractions ix, BattlefieldView bf, LfgView lfg)
        {
            int n = 0;
            if (_menuEye)
            {
                Item(set, ref n, "Dungeon Finder", () => { if (_ctx != null && _ctx.Toggle != null) _ctx.Toggle(UiWindowKind.DungeonFinder); });   // DUNGEONS_BUTTON :2032
                if (lfg.InLfgDungeon)
                {
                    bool inside = UiLfg.InsideLfgDungeon(lfg, WowPlay.Session);
                    Item(set, ref n, inside ? "Teleport Out Of Dungeon" : "Teleport To Dungeon", () => ix.LfgTeleport(inside));   // :7482 / :7483
                }
                if (lfg.Status == LfgStatus.Queued || lfg.Status == LfgStatus.RoleCheck || lfg.Status == LfgStatus.Proposal)
                    Item(set, ref n, "Leave Queue", () => ix.LeaveLfg());   // LEAVE_QUEUE :4700
            }
            else
            {
                for (int i = 0; i < bf.Queues.Length; i++)
                {
                    BattlefieldQueueView q = bf.Queues[i];
                    int slot = i;
                    string name = q.Name ?? "?";
                    if (q.Status == BattlefieldStatus.Queued) Item(set, ref n, name + ": Leave Queue", () => ix.AnswerBattlefieldInvite(slot, false));
                    else if (q.Status == BattlefieldStatus.Invited)
                    {
                        Item(set, ref n, name + ": Enter Battle", () => ix.AnswerBattlefieldInvite(slot, true));   // ENTER_BATTLE :2821
                        if (q.ArenaType == 0) Item(set, ref n, name + ": Leave Queue", () => ix.AnswerBattlefieldInvite(slot, false));
                    }
                    else if (q.Status == BattlefieldStatus.InProgress)
                    {
                        Item(set, ref n, "Show Score", () => UiPvp.ScoreOpen = true);   // ours (WoW: the left click)
                        Item(set, ref n, q.ArenaType > 0 ? "Leave Arena" : "Leave Battleground", () => ix.LeaveBattlefield());   // :4697 / :4698
                    }
                }
            }
            for (int i = n; i < set.Items.Length; i++) UiQk.Show(set.Items[i], false);
            if (n == 0) _menuOpen = false;
        }

        private void Item(IconSet set, ref int n, string label, Action a)
        {
            if (n >= set.Items.Length) return;
            UiButton b = set.Items[n++];
            UiQk.Show(b, true);
            if (b.Label != null && b.Label.text != label) b.SetLabel(label);
            b.Click = () => { WowLog.Info("UI: queue menu - " + label + "."); _menuOpen = false; a(); };
        }

        // ------------------------------------------------------------------ build / surfaces

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
                _desk = UiSurface.MakeScreen("WoW Queues (desktop)", transform, 8);
                UiKit.Surface = _desk;
                // TOPRIGHT (-180, -30): where WoW's minimap cluster's left edge would be (ours - there is no minimap here)
                _deskSet = MakeIcons(_desk.Anchor("Queue icons", new Vector2(1f, 1f), IconsW, IconsH, -180f, -30f));
                _deskLfd = new UiLfgPopups();
                _deskLfd.Build(_desk.Anchor("Dungeon Finder popups", new Vector2(0.5f, 0.5f), UiLfgPopups.W, UiLfgPopups.H, 0f, 60f));
                _desk.Finish();
                _desk.SetShown(false);
                UiKit.Pointable = false;
                _vrIcons = UiSurface.MakeWorld("WoW Queue Icons (VR)", transform, IconsW, IconsH, VrMetresPerUnit, false, 207);
                UiKit.Surface = _vrIcons;
                _vrSet = MakeIcons(UiKit.Rect(_vrIcons.Content, "Queue icons", 0f, 0f, IconsW, IconsH));
                _vrIcons.Finish();
                _vrIcons.SetShown(false);
                _vrPopup = UiSurface.MakeWorld("WoW Dungeon Finder Popups (VR)", transform, UiLfgPopups.W, UiLfgPopups.H, PopupMetresPerUnit, false, 104);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrPopup);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrPopup;
                _vrLfd = new UiLfgPopups();
                _vrLfd.Build(_vrPopup.Content);
                _vrPopup.Finish();
                _vrPopup.SetShown(false);
                WowLog.Info("UI: queues hub built (queue icon + Dungeon Finder eye, entry / vote / offer popups, role check + dungeon ready frames; desktop + VR). " +
                            "Ours: the icons at TOPRIGHT (-180, -30) on the desktop, in VR 1 m away at yaw +17 / pitch 14 (the HUD mirrored); times as m:ss; " +
                            "the scoreboard asked every 5 s; LFD roles by class (warrior / paladin / druid / death knight tank, priest / paladin / druid / shaman heal, all damage); " +
                            "the entry popup's Leave Queue hidden for arenas; dropdowns are cycling buttons (right click = back).");
            }
            finally
            {
                UiKit.Surface = old;
                UiKit.Over = oldOver;
                UiKit.Pointable = oldPointable;
            }
        }

        private IconSet MakeIcons(RectTransform host)
        {
            var s = new IconSet { Host = host };
            // MiniMapBattlefieldFrame 33 x 33: UI-Battlefield-Icon 32 x 32 + MiniMap-TrackingBorder 54 x 54 (Minimap.u.xml:165-232) - at the right
            s.Bf = UiKit.Button(host, "MiniMapBattlefieldFrame", IconsW - 33f, 0f, 33f, 33f);
            UiKit.Pic(s.Bf.transform, "Icon", UiArt.Tex(@"Interface\BattlefieldFrame\UI-Battlefield-Icon.blp"), 1f, 1f, 32f, 32f);
            UiKit.Pic(s.Bf.transform, "Border", UiArt.Tex(@"Interface\Minimap\MiniMap-TrackingBorder.blp"), 0f, 0f, 54f, 54f);
            s.Bf.Hilite = UiKit.Pic(s.Bf.transform, "Highlight", UiArt.Add(@"Interface\Minimap\UI-Minimap-ZoomButton-Highlight.blp"), 0f, 0f, 33f, 33f);
            s.Bf.Hilite.enabled = false;
            s.Bf.Tip = BfTip;
            s.Bf.Click = BfClick;
            s.Bf.RightClick = () => { _menuOpen = !_menuOpen || _menuEye; _menuEye = false; };
            // MiniMapLFGFrame 33 x 33 with the LFG-Eye (EyeTemplate 45 x 45 shown 30 x 30; Minimap.u.xml:4-12, :505-560) - left of the queue icon (ours)
            s.Eye = UiKit.Button(host, "MiniMapLFGFrame", IconsW - 33f - 38f, 0f, 33f, 33f);
            s.EyeArt = UiKit.Pic(s.Eye.transform, "Eye", UiArt.Tex(@"Interface\LFGFrame\LFG-Eye.blp"), 0f, 0.125f, 0f, 0.25f, 1.5f, 1.5f, 30f, 30f);
            UiKit.Pic(s.Eye.transform, "Border", UiArt.Tex(@"Interface\Minimap\MiniMap-TrackingBorder.blp"), 0f, 0f, 54f, 54f);
            s.Eye.Hilite = UiKit.Pic(s.Eye.transform, "Highlight", UiArt.Add(@"Interface\Minimap\UI-Minimap-ZoomButton-Highlight.blp"), 0f, 0f, 33f, 33f);
            s.Eye.Hilite.enabled = false;
            s.Eye.Tip = EyeTip;
            s.Eye.Click = () => EyeClick(false);
            s.Eye.RightClick = () => EyeClick(true);
            // the menu (ours, UiGroup's menu look: panel buttons under the icons)
            s.Menu = UiKit.Rect(host, "Menu", 0f, 40f, IconsW, IconsH - 40f);
            UiKit.Solid(s.Menu, "Back", new Color(0f, 0f, 0f, 0.75f), 0f, 0f, IconsW, IconsH - 40f);
            for (int i = 0; i < s.Items.Length; i++) s.Items[i] = UiKit.PanelButton(s.Menu, "Item", 4f, 4f + i * 24f, IconsW - 8f, 22f, null);
            s.Menu.gameObject.SetActive(false);
            s.Bf.gameObject.SetActive(false);
            s.Eye.gameObject.SetActive(false);
            host.gameObject.SetActive(false);
            return s;
        }

        private void ShowVrIcons(bool on)
        {
            if (_vrIcons == null || _vrIconsShown == on) return;
            _vrIconsShown = on;
            _vrIcons.SetShown(on);
            if (on) { AddExtra(_vrIcons); _vrPlaced = false; } else RemoveExtra(_vrIcons);
        }

        private void ShowVrPopup(bool on)
        {
            if (_vrPopup == null || _vrPopupShown == on) return;
            _vrPopupShown = on;
            _vrPopup.SetShown(on);
            if (on) AddExtra(_vrPopup); else RemoveExtra(_vrPopup);
        }

        private static void AddExtra(UiSurface s)
        {
            if (s != null && !UiVrLayout.ExtraSurfaces.Contains(s)) UiVrLayout.ExtraSurfaces.Add(s);
        }

        private static void RemoveExtra(UiSurface s)
        {
            if (s != null) UiVrLayout.ExtraSurfaces.Remove(s);
        }

        private void PlaceVrIcons()
        {
            Camera head = WowPlayer.Head();
            if (head == null || _vrIcons == null) return;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Quaternion look = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(-VrPitch, VrYaw, 0f);
            Vector3 target = head.transform.position + look * Vector3.forward * VrDistance;
            Transform t = _vrIcons.Root;
            t.position = _vrPlaced ? Vector3.Lerp(t.position, target, 1f - Mathf.Exp(-VrFollow * Time.unscaledDeltaTime)) : target;
            Vector3 away = t.position - head.transform.position;
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
            _vrPlaced = true;
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

    // =============================================================================================================== the area's small kit (ours)

    internal static class UiQk
    {
        internal const string TabOff = @"Interface\PaperDollInfoFrame\UI-Character-InActiveTab.blp", TabOn = @"Interface\PaperDollInfoFrame\UI-Character-ActiveTab.blp",
            TabHilite = @"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp";   // CharacterFrameTabButtonTemplate (AuctionTabTemplate inherits it, AHT.xml:970)
        internal const string RowHilite = @"Interface\Buttons\UI-Listbox-Highlight2.blp";   // BattlefieldFrame.xml:357 (ADD)
        internal static readonly Color AllianceBlue = new Color(0.3f, 0.6f, 1f), HordeRed = new Color(1f, 0.25f, 0.25f);   // ours (team colours of the scoreboard names)

        internal static IWowInteractions Ix(UiContext c) { return c != null && c.S != null ? c.S.Interactions : null; }
        internal static OnlineSession Online(UiContext c) { return c != null ? c.S as OnlineSession : null; }
        internal static bool Live(UiContext c) { IWowInteractions ix = Ix(c); return ix != null && ix.Available; }

        internal static float[] Shift(this float[] a, float d)
        {
            var r = new float[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] + d;
            return r;
        }

        internal sealed class Tab
        {
            public UiButton B;
            public TextMeshProUGUI Label;
            public readonly RawImage[] Off = new RawImage[3], On = new RawImage[3];
            public float W;

            public void Select(bool on)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (Off[i] != null) Off[i].enabled = !on;
                    if (On[i] != null) On[i].enabled = on;
                }
                Label.color = on ? UiArt.White : UiArt.Gold;
                UiKit.Move(Label.rectTransform, 0f, on ? -4f : -1f);
            }
        }

        /// <summary>CharacterFrameTabButtonTemplate: UI-Character-InActiveTab / -ActiveTab 20 + middle + 20 (texcoords 0-0.15625 / -0.84375 / -1),
        /// the active one 5 units up, UI-Character-Tab-Highlight (ADD); the width from the label (+40, at least 60).</summary>
        internal static Tab BottomTab(Transform parent, string label, float x, float y, Action click)
        {
            var t = new Tab();
            t.B = UiKit.Button(parent, "Tab " + label, x, y, 100f, 32f);
            t.Label = UiKit.Text(t.B.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 100f, 32f);
            t.Label.textWrappingMode = TextWrappingModes.NoWrap;
            float tw = Mathf.Ceil(t.Label.GetPreferredValues(label).x);
            t.W = Mathf.Max(60f, tw + 40f);
            float mid = t.W - 40f;
            UiKit.Size(t.B.Rt, t.W, 32f);
            UiKit.Size(t.Label.rectTransform, t.W, 32f);
            Texture2D off = UiArt.Tex(TabOff), on = UiArt.Tex(TabOn);
            t.Off[0] = UiKit.Pic(t.B.transform, "L", off, 0f, 0.15625f, 0f, 1f, 0f, 0f, 20f, 32f);
            t.Off[1] = UiKit.Pic(t.B.transform, "M", off, 0.15625f, 0.84375f, 0f, 1f, 20f, 0f, mid, 32f);
            t.Off[2] = UiKit.Pic(t.B.transform, "R", off, 0.84375f, 1f, 0f, 1f, 20f + mid, 0f, 20f, 32f);
            t.On[0] = UiKit.Pic(t.B.transform, "L on", on, 0f, 0.15625f, 0f, 1f, 0f, -5f, 20f, 32f);
            t.On[1] = UiKit.Pic(t.B.transform, "M on", on, 0.15625f, 0.84375f, 0f, 1f, 20f, -5f, mid, 32f);
            t.On[2] = UiKit.Pic(t.B.transform, "R on", on, 0.84375f, 1f, 0f, 1f, 20f + mid, -5f, 20f, 32f);
            t.B.Hilite = UiKit.Pic(t.B.transform, "Highlight", UiArt.Add(TabHilite), 10f, 2f, t.W - 20f, 32f);
            t.B.Hilite.enabled = false;
            t.Label.transform.SetAsLastSibling();
            t.B.Click = click;
            t.Select(false);
            return t;
        }

        /// <summary>UICheckButtonTemplate: UI-CheckBox-Up / -Check / -Highlight (ADD), 20 x 20 + its label.</summary>
        internal static UiButton Check(Transform parent, string label, float x, float y, float labelW, Action click)
        {
            UiButton b = UiKit.Button(parent, "Check " + label, x, y, 20f + labelW, 20f);
            b.Art = UiKit.Pic(b.transform, "Up", UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Up.blp"), -2f, -2f, 24f, 24f);
            b.Checked = UiKit.Pic(b.transform, "Check", UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Check.blp"), -2f, -2f, 24f, 24f);
            b.Checked.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(@"Interface\Buttons\UI-CheckBox-Highlight.blp"), -2f, -2f, 24f, 24f);
            b.Hilite.enabled = false;
            TextMeshProUGUI t = UiKit.Text(b.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 24f, 0f, labelW, 20f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            b.Label = t;
            b.LabelColor = UiArt.Gold;
            b.Click = click;
            return b;
        }

        internal static void SetCheck(UiButton b, bool on)
        {
            if (b != null && b.Checked != null && b.Checked.enabled != on) b.Checked.enabled = on;
        }

        internal sealed class Row
        {
            public UiButton B;
            public RawImage Sel;
            public TextMeshProUGUI[] Col;
        }

        /// <summary>A clickable list row (the battle / instance / score / filter lists): UI-Listbox-Highlight2 on hover, a gold "selected" bar.</summary>
        internal static Row MakeRow(Transform parent, string name, float x, float y, float w, float h, float[] colX, float[] colW, float size)
        {
            var r = new Row();
            r.B = UiKit.Button(parent, name, x, y, w, h);
            r.Sel = UiKit.Pic(r.B.transform, "Selected", UiArt.Add(RowHilite), 0f, 0f, w, h);
            r.Sel.color = new Color(1f, 0.82f, 0f, 0.6f);
            r.Sel.enabled = false;
            r.B.Hilite = UiKit.Pic(r.B.transform, "Highlight", UiArt.Add(RowHilite), 0f, 0f, w, h);
            r.B.Hilite.enabled = false;
            r.Col = new TextMeshProUGUI[colX.Length];
            for (int i = 0; i < colX.Length; i++)
            {
                TextMeshProUGUI t = UiKit.Text(r.B.transform, "Col " + i, "", size, UiArt.Gold, TextAlignmentOptions.MidlineLeft, colX[i], 0f, colW[i], h);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                r.Col[i] = t;
            }
            r.B.gameObject.SetActive(false);
            return r;
        }

        internal static void Set(TextMeshProUGUI t, string s, Color c)
        {
            if (t == null) return;
            s = s ?? "";
            if (!string.Equals(t.text, s)) t.text = s;
            if (t.color != c) t.color = c;
        }

        internal static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>A centred note (the offline "on the online realm" text, ours).</summary>
        internal static TextMeshProUGUI Notice(Transform parent, float x, float y, float w, float h)
        {
            TextMeshProUGUI t = UiKit.Text(parent, "Notice", "", 13f, UiArt.Gold, TextAlignmentOptions.Center, x, y, w, h);
            t.gameObject.SetActive(false);
            return t;
        }

        /// <summary>UIPanelScrollUpButtonTemplate / Down at the right edge of a list (16 x 16), one row per press.</summary>
        internal static void ScrollPair(Transform parent, float x, float yTop, float yBottom, Action up, Action down)
        {
            UiKit.ArtButton(parent, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, x, yTop, 16f, up);
            UiKit.ArtButton(parent, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, x, yBottom, 16f, down);
        }

        /// <summary>The mouse wheel over a rect (desktop): +1 up, -1 down, 0 none.</summary>
        internal static int Wheel(RectTransform r)
        {
            if (r == null || WowPlayer.XrPresent) return 0;
            float y = Input.mouseScrollDelta.y;
            if (Mathf.Abs(y) < 0.01f) return 0;
            if (!RectTransformUtility.RectangleContainsScreenPoint(r, Input.mousePosition, null)) return 0;
            return y > 0f ? 1 : -1;
        }

        /// <summary>A text box (WowTextField) + its VR stand-in button (a laser / poke opens the headset keyboard; shown only in VR).</summary>
        internal sealed class Box
        {
            public TMP_InputField F;
            public UiButton Vr;
            public string Title;
        }

        internal static Box Field(Transform parent, string title, float x, float y, float w, float h, int limit, bool numeric)
        {
            var b = new Box { Title = title };
            b.F = WowTextField.Create(parent, title, x, y, w, h, 10f, "", false, limit);
            if (numeric) b.F.contentType = TMP_InputField.ContentType.IntegerNumber;
            b.F.gameObject.SetActive(true);
            b.Vr = UiKit.Button(parent, "VR box " + title, x, y, w, h);
            b.Vr.gameObject.SetActive(false);
            return b;
        }

        internal static int Int(Box b)
        {
            int v;
            return b != null && b.F != null && int.TryParse(b.F.text, out v) ? Math.Max(0, v) : 0;
        }

        internal static void SyncVr(Box b, bool vr)
        {
            if (b != null && b.Vr != null && b.Vr.gameObject.activeSelf != vr) b.Vr.gameObject.SetActive(vr);
        }

        /// <summary>MoneyInputFrameTemplate (MoneyInputFrame.xml) - ours: three digit boxes gold / silver / copper with UI-MoneyIcons, as UiTrade.</summary>
        internal sealed class MoneyBox
        {
            public readonly Box[] B = new Box[3];

            public int Copper
            {
                get
                {
                    long c = (long)Mathf.Min(Int(B[0]), 200000) * 10000 + Mathf.Min(Int(B[1]), 99) * 100 + Mathf.Min(Int(B[2]), 99);
                    return (int)Math.Min(c, 2000000000L);   // MAXIMUM_BID_PRICE (Blizzard_AuctionUI.lua:12)
                }
            }

            public void Set(int copper)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = k == 0 ? copper / 10000 : k == 1 ? copper / 100 % 100 : copper % 100;
                    if (B[k] != null && !B[k].F.isFocused) B[k].F.SetTextWithoutNotify(v > 0 ? v.ToString() : "");
                }
            }

            public void SyncVr(bool vr)
            {
                for (int k = 0; k < 3; k++) UiQk.SyncVr(B[k], vr);
            }

            public bool Focused { get { for (int k = 0; k < 3; k++) if (B[k] != null && B[k].F.isFocused) return true; return false; } }
        }

        public const string InputBorder = @"Interface\Common\Common-Input-Border";

        internal static MoneyBox Money(Transform parent, string name, float x, float y, Action<Box> vrOpen, Action changed)
        {
            var m = new MoneyBox();
            Texture2D coins = UiArt.Tex(UiArt.MoneyIcons);
            float[] bx = { x, x + 56f, x + 94f };
            float[] bw = { 40f, 24f, 24f };
            string[] what = { "Gold", "Silver", "Copper" };
            Texture2D border = UiArt.Tex(InputBorder);
            for (int k = 0; k < 3; k++)
            {
                // lead 2026-10-10 (the user: "give it some UI background etc like in WoW"): InputBoxTemplate's Common-Input-Border behind
                // each box (UIPanelTemplates.xml: Left 8x20 TexCoords 0 0.0625 0 0.625, Middle 0.0625 0.9375, Right 0.9375 1), the box's
                // own black plate lighter and its outline off
                if (border != null)
                {
                    float ax = bx[k] - 5f, aw = bw[k] + 8f;
                    UiKit.Pic(parent, name + " border L " + k, border, 0f, 0.0625f, 0f, 0.625f, ax, y - 2f, 8f, 20f);
                    UiKit.Pic(parent, name + " border M " + k, border, 0.0625f, 0.9375f, 0f, 0.625f, ax + 8f, y - 2f, aw - 16f, 20f);
                    UiKit.Pic(parent, name + " border R " + k, border, 0.9375f, 1f, 0f, 0.625f, ax + aw - 8f, y - 2f, 8f, 20f);
                }
                Box b = Field(parent, name + " " + what[k], bx[k], y, bw[k], 16f, k == 0 ? 6 : 2, true);
                if (border != null)
                {
                    var plate = b.F.GetComponent<UnityEngine.UI.Image>();
                    if (plate != null) plate.color = new Color(0f, 0f, 0f, 0.35f);
                    var ol = b.F.GetComponent<UnityEngine.UI.Outline>();
                    if (ol != null) ol.enabled = false;
                }
                b.F.textComponent.alignment = TextAlignmentOptions.MidlineRight;   // WoW's money boxes are right-aligned (MoneyInputFrame.xml justifyH RIGHT)
                b.Title = what[k];
                if (changed != null) b.F.onValueChanged.AddListener(_ => changed());
                if (vrOpen != null) b.Vr.Click = () => vrOpen(b);
                m.B[k] = b;
                UiKit.Pic(parent, name + " coin " + k, coins, k * 0.25f, k * 0.25f + 0.25f, 0f, 1f, bx[k] + bw[k] + 1f, y + 1f, 13f, 13f);
            }
            return m;
        }

        /// <summary>"m:ss" (ours - WoW's SecondsToTime writes "5 Min 3 Sec").</summary>
        internal static string Mmss(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>The VR haptic for "something asks you" (proposal-interact.md 6.3: both hands 0.5 / 0.1 s, GearXr honours Settings > Haptics).</summary>
        internal static void Pulse(bool vr)
        {
            if (!vr) return;
            try { GearXr.Pulse(true, 0.5f, 0.1f); GearXr.Pulse(false, 0.5f, 0.1f); }
            catch (Exception) { }
        }
    }
}
