// iteration 11 - owner socialui (proposal-social.md 6.5.3; wave-0 stub by w0game, filled by socialui)
// The Guild tab of the Social window (WoW's GuildFrame: roster in the Player Status / Guild Status views, member detail, Guild Information,
// Guild Control) and the two charter windows: GuildRegistrarFrame (buy / turn in a guild charter at a guild master) and PetitionFrame (the
// charter's signatures). Petition 46 / GuildRegistrar 47 are registered in UiRegistry.Wave0.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// WoW's GuildFrame inside the Social window's Guild tab (FriendsFrame.xml:2640-4470, FriendsFrame.lua GuildStatus_Update): 13 rows of
    /// 14 (GUILDMEMBERS_TO_DISPLAY / FRIENDS_FRAME_GUILD_HEIGHT), the toggle between Player Status (Name 83 / Zone 105 / Lvl 32 / Class 92)
    /// and Guild Status (Name 83 / Rank 75 / Note 75 / Last Online 75), "Show Offline Members", the totals, the MOTD (click = SET_GUILDMOTD
    /// with the right), Guild Information / Add Member / Guild Control; a member click opens GuildMemberDetailFrame. Every button is enabled
    /// by the rank rights exactly as AzerothCore judges them (GuildView.Can = Guild.h:819-825); the server has the last word.
    /// </summary>
    internal sealed class UiGuildTab
    {
        const int Rows = 13;                     // GUILDMEMBERS_TO_DISPLAY (FriendsFrame.lua:13)
        const float RowH = 14f;                  // FRIENDS_FRAME_GUILD_HEIGHT (:14)
        const float RefreshSeconds = 30f;        // ours: the roster re-asked every 30 s while the tab shows (the session throttles to 2 s)
        const int MaxRanks = 10;                 // GuildControlPopupFrameAddRankButton disabled at 10 ranks (FriendsFrame.lua:1795-1798)
        const uint BankBits = 0x1E0000;          // GR_RIGHT_GUILD_BANK_* / WITHDRAW_* 0x20000-0x100000 (Guild.h:90-93): echoed unchanged (gap G3)

        private readonly UiContext _ctx;
        private readonly UiSocial _owner;
        private readonly RectTransform _page, _list;
        private readonly SocialUi.Row[] _rows = new SocialUi.Row[Rows];
        private readonly UiButton[] _headers = new UiButton[4];
        private readonly UiButton _toggle, _showOffline, _addMember, _control, _motdButton;
        private readonly TextMeshProUGUI _totals, _motd;
        private readonly List<GuildMemberView> _shown = new List<GuildMemberView>();
        private bool _guildStatus, _offline;
        private int _top, _seen = -1, _sort, _version, _drawn = -1;
        private string _sel;
        private float _asked = -999f;
        // member detail (GuildMemberDetailFrame 212 x 270)
        private readonly RectTransform _detail;
        private readonly TextMeshProUGUI _dName, _dLevel, _dZone, _dRank, _dOnline, _dNote, _dOfficer, _dOfficerLabel;
        private readonly UiButton _dPromote, _dDemote, _dRemove, _dInvite, _dNoteButton, _dOfficerButton;
        // Guild Information (GuildInfoFrame 297 x 298)
        private readonly RectTransform _info;
        private readonly TMP_InputField _infoField;
        private readonly TextMeshProUGUI _infoLine;
        private readonly UiButton _infoSave;
        private bool _infoLoaded;
        // Guild Control (GuildControlPopupFrame)
        private readonly RectTransform _controlPanel;
        private readonly TMP_InputField _rankName;
        private readonly TextMeshProUGUI _rankLabel;
        private readonly UiButton[] _checks = new UiButton[14];
        private readonly UiButton _addRank, _removeRank;
        private int _rank = 1;
        private uint _rights;

        public UiGuildTab(RectTransform page, UiContext ctx, UiSocial owner, SocialUi.Scope scope)
        {
            _page = page;
            _ctx = ctx;
            _owner = owner;
            // GuildFrameTotals: BOTTOM of the frame's LEFT (75, -75) + GuildFrameOnlineTotals right of it (FriendsFrame.xml:2649-2670)
            _totals = UiKit.Text(page, "GuildFrameTotals", "", 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 24f, 315f, 220f, 16f);
            _totals.textWrappingMode = TextWrappingModes.NoWrap;
            // GuildFrameLFGFrame 210 x 23 TOPRIGHT (-42, -38): SHOW_OFFLINE_MEMBERS (GlobalStrings.lua:6289)
            _showOffline = SocialUi.Check(page, SocialUi.G("SHOW_OFFLINE_MEMBERS", "Show Offline Members"), 140f, 40f, 180f, ToggleOffline);
            // the column headers at TOPLEFT (20, -70)
            float[] hx = { 20f, 103f, 208f, 240f };
            float[] hw = { 83f, 105f, 32f, 92f };
            for (int i = 0; i < 4; i++)
            {
                int k = i + 1;
                _headers[i] = Header(page, "", hx[i], 70f, hw[i], () => Sort(k));
            }
            // GuildListScrollFrame 296 x 237 TOPRIGHT (-67, -98)
            _list = UiKit.Rect(page, "GuildList", 21f, 98f, 312f, Rows * RowH);
            for (int i = 0; i < Rows; i++)
            {
                int k = i;
                SocialUi.Row r = SocialUi.MakeRow(_list, "Member " + (i + 1), 0f, i * RowH, 312f, RowH,
                    new[] { 2f, 85f, 187f, 221f }, new[] { 81f, 100f, 30f, 90f }, 10f, false);
                r.B.Click = () => MemberClick(k, false);
                r.B.RightClick = () => MemberClick(k, true);
                r.B.LongPress = () => MemberClick(k, true);
                _rows[i] = r;
            }
            SocialUi.ScrollPair(page, 335f, 98f, 266f, () => Scroll(-1), () => Scroll(1));
            // GuildFrameGuildListToggleButton 32 x 32 at LEFT (245, -67): UI-SpellbookIcon-NextPage
            _toggle = UiKit.ArtButton(page, "GuildListToggle", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.MouseHilight, 245f, 307f, 32f, ToggleView);
            // the MOTD: GuildFrameNotesLabel 225 x 11 TOPLEFT (23, -344) + GuildFrameNotesText 315 x 45 + GuildMOTDEditButton over it
            UiKit.Text(page, "GuildFrameNotesLabel", SocialUi.G("GUILD_MOTD_LABEL", "Guild Message Of The Day:"), 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 23f, 340f, 225f, 13f);   // :3962
            _motdButton = UiKit.Button(page, "GuildMOTDEditButton", 23f, 355f, 315f, 45f);
            _motdButton.Hilite = UiKit.Pic(_motdButton.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, 315f, 45f);
            _motdButton.Hilite.enabled = false;
            _motd = UiKit.Text(_motdButton.transform, "GuildFrameNotesText", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 2f, 0f, 311f, 45f);
            _motd.richText = false;
            _motd.overflowMode = TextOverflowModes.Ellipsis;
            _motdButton.Click = EditMotd;
            // the buttons from BOTTOMRIGHT (-39, 82) leftwards: Guild Control 104, Add Member 98, Guild Information 123
            _control = UiKit.PanelButton(page, SocialUi.G("GUILDCONTROL", "Guild Control"), 241f, 408f, 104f, 22f, () => ShowControl(true));     // :3884
            _addMember = UiKit.PanelButton(page, SocialUi.G("ADDMEMBER", "Add Member"), 143f, 408f, 98f, 22f, AddMemberClick);                 // :475
            UiKit.PanelButton(page, SocialUi.G("GUILD_INFORMATION", "Guild Information"), 20f, 408f, 123f, 22f, () => ShowInfo(!_info.gameObject.activeSelf));   // :3952

            // ---- GuildMemberDetailFrame: desktop TOPLEFT at the window's TOPRIGHT (-33, -28); VR (ours) over the list's right half
            float dx = ctx.Vr ? 160f : 351f, dy = ctx.Vr ? 60f : 28f;
            _detail = UiKit.Rect(page, "GuildMemberDetailFrame", dx, dy, 212f, 270f);
            SocialUi.Catch(_detail);
            UiBackdrop db = UiBackdrop.Make(_detail, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);   // ours: the dialog backdrop
            db.Place(0f, 0f, 212f, 270f);
            UiKit.CloseButton(_detail, 182f, -2f, () => { _sel = null; _version++; });
            _dName = Line(_detail, "Name", 16f, 14f, 13f, UiArt.Gold);
            _dLevel = Line(_detail, "Level", 16f, 32f, 10f, Color.white);
            _dZone = Line(_detail, "Zone", 16f, 46f, 10f, Color.white);
            _dRank = Line(_detail, "Rank", 16f, 66f, 10f, UiArt.Gold);
            UiKit.Size(_dRank.rectTransform, 120f, 14f);
            _dPromote = UiKit.ArtButton(_detail, "GuildFramePromoteButton", @"Interface\Buttons\UI-ScrollBar-ScrollUpButton-Up.blp", @"Interface\Buttons\UI-ScrollBar-ScrollUpButton-Down.blp",
                @"Interface\Buttons\UI-ScrollBar-ScrollUpButton-Disabled.blp", UiArt.ScrollHighlight, 140f, 58f, 28f, () => RankChange(true));
            _dDemote = UiKit.ArtButton(_detail, "GuildFrameDemoteButton", @"Interface\Buttons\UI-ScrollBar-ScrollDownButton-Up.blp", @"Interface\Buttons\UI-ScrollBar-ScrollDownButton-Down.blp",
                @"Interface\Buttons\UI-ScrollBar-ScrollDownButton-Disabled.blp", UiArt.ScrollHighlight, 168f, 58f, 28f, () => RankChange(false));
            _dOnline = Line(_detail, "Online", 16f, 86f, 10f, Color.white);
            Line(_detail, "Notes label", 16f, 106f, 10f, UiArt.Gold).text = SocialUi.G("GUILD_NOTES_LABEL", "Player Notes:");             // :3967
            _dNoteButton = UiKit.Button(_detail, "PersonalNoteText", 14f, 120f, 184f, 30f);
            _dNoteButton.Hilite = UiKit.Pic(_dNoteButton.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, 184f, 30f);
            _dNoteButton.Hilite.enabled = false;
            _dNote = UiKit.Text(_dNoteButton.transform, "Text", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 2f, 1f, 180f, 28f);
            _dNote.richText = false;
            _dNoteButton.Click = () => EditNote(false);
            _dOfficerLabel = Line(_detail, "Officer label", 16f, 154f, 10f, UiArt.Gold);
            _dOfficerLabel.text = SocialUi.G("GUILD_OFFICERNOTES_LABEL", "Officer's Notes");                                                   // :3970
            _dOfficerButton = UiKit.Button(_detail, "OfficerNoteText", 14f, 168f, 184f, 30f);
            _dOfficerButton.Hilite = UiKit.Pic(_dOfficerButton.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, 184f, 30f);
            _dOfficerButton.Hilite.enabled = false;
            _dOfficer = UiKit.Text(_dOfficerButton.transform, "Text", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 2f, 1f, 180f, 28f);
            _dOfficer.richText = false;
            _dOfficerButton.Click = () => EditNote(true);
            // GuildMemberRemoveButton 96 x 22 BOTTOMLEFT (10, 12), GuildMemberGroupInviteButton right of it
            _dRemove = UiKit.PanelButton(_detail, SocialUi.G("REMOVE", "Remove"), 10f, 236f, 96f, 22f, RemoveClick);                       // :6043
            _dInvite = UiKit.PanelButton(_detail, SocialUi.G("GROUP_INVITE", "Group Invite"), 106f, 236f, 96f, 22f, () =>
            {
                if (_ctx.S != null && !string.IsNullOrEmpty(_sel)) _ctx.S.InviteToGroup(_sel);
            });
            _detail.gameObject.SetActive(false);

            // ---- GuildInfoFrame 297 x 298 (FriendsFrame.xml:4012-4130): the info text (GuildInfoEditBox letters 500) + Save
            _info = UiKit.Rect(page, "GuildInfoFrame", 44f, 70f, 297f, 298f);
            SocialUi.Catch(_info);
            UiBackdrop ib = UiBackdrop.Make(_info, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            ib.Place(0f, 0f, 297f, 298f);
            UiKit.Text(_info, "Title", SocialUi.G("GUILD_INFORMATION", "Guild Information"), 13f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 14f, 297f, 18f);
            UiKit.CloseButton(_info, 267f, -2f, () => ShowInfo(false));
            _infoField = SocialUi.Field(_info, "GuildInfoEditBox", 18f, 40f, 261f, 190f, 500, true, 11f, SocialUi.G("GUILD_INFORMATION", "Guild Information"), null);
            _infoLine = UiKit.Text(_info, "GuildInfoText", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 18f, 234f, 261f, 28f);
            _infoLine.richText = false;
            _infoSave = UiKit.PanelButton(_info, SocialUi.G("SAVE", "Save"), 18f, 262f, 90f, 22f, SaveInfo);                                // :6157
            UiKit.PanelButton(_info, SocialUi.G("CANCEL", "Cancel"), 189f, 262f, 90f, 22f, () => ShowInfo(false));
            _info.gameObject.SetActive(false);

            // ---- GuildControlPopupFrame (FriendsFrame.xml:3361-3800): the rank, its label (15 letters), the 13 rights (two columns)
            _controlPanel = UiKit.Rect(page, "GuildControlPopupFrame", 24f, 64f, 336f, 336f);
            SocialUi.Catch(_controlPanel);
            UiBackdrop cb = UiBackdrop.Make(_controlPanel, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            cb.Place(0f, 0f, 336f, 336f);
            UiKit.Text(_controlPanel, "Title", SocialUi.G("GUILDCONTROL", "Guild Control"), 13f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 14f, 336f, 18f);
            UiKit.CloseButton(_controlPanel, 306f, -2f, () => ShowControl(false));
            UiKit.Text(_controlPanel, "Select label", SocialUi.G("GUILDCONTROL_SELECTRANK", "Select guild rank to modify:"), 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 18f, 38f, 300f, 14f);   // :3907
            UiKit.ArtButton(_controlPanel, "Prev rank", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.MouseHilight, 16f, 52f, 26f, () => PickRank(-1));
            _rankLabel = UiKit.Text(_controlPanel, "Rank", "", 12f, Color.white, TextAlignmentOptions.Center, 44f, 52f, 200f, 26f);
            _rankLabel.richText = false;
            UiKit.ArtButton(_controlPanel, "Next rank", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.MouseHilight, 246f, 52f, 26f, () => PickRank(1));
            _addRank = UiKit.ArtButton(_controlPanel, "GuildControlPopupFrameAddRankButton", @"Interface\Buttons\UI-PlusButton-Up.blp", @"Interface\Buttons\UI-PlusButton-Down.blp",
                @"Interface\Buttons\UI-PlusButton-Disabled.blp", @"Interface\Buttons\UI-PlusButton-Hilight.blp", 280f, 57f, 16f, AddRankClick);
            _removeRank = UiKit.ArtButton(_controlPanel, "GuildControlPopupFrameRemoveRankButton", @"Interface\Buttons\UI-MinusButton-Up.blp", @"Interface\Buttons\UI-MinusButton-Down.blp",
                @"Interface\Buttons\UI-MinusButton-Disabled.blp", @"Interface\Buttons\UI-PlusButton-Hilight.blp", 300f, 57f, 16f, RemoveRankClick);
            UiKit.Text(_controlPanel, "Rank label", SocialUi.G("GUILDCONTROL_RANKLABEL", "Rank Label:"), 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 18f, 86f, 100f, 20f);   // :3908
            _rankName = SocialUi.Field(_controlPanel, "GuildControlPopupFrameEditBox", 110f, 86f, 150f, 20f, 15, false, 11f, SocialUi.G("GUILDCONTROL_RANKLABEL", "Rank Label:"), null);
            for (int i = 1; i <= 13; i++)
            {
                int k = i;
                // odd ids left, even right (FriendsFrame.xml:3530-3700); labels GUILDCONTROL_OPTION1..13 (GlobalStrings.lua:3887-3905)
                float x = (i % 2 == 1) ? 18f : 172f, y = 116f + ((i - 1) / 2) * 24f;
                _checks[i] = SocialUi.Check(_controlPanel, SocialUi.G("GUILDCONTROL_OPTION" + i, ControlFallback[i]), x, y, 128f, () => ToggleRight(k));
            }
            UiKit.PanelButton(_controlPanel, SocialUi.G("ACCEPT", "Accept"), 60f, 296f, 100f, 22f, AcceptControl);
            UiKit.PanelButton(_controlPanel, SocialUi.G("CANCEL", "Cancel"), 176f, 296f, 100f, 22f, () => ShowControl(false));
            _controlPanel.gameObject.SetActive(false);
            SetHeaders();
        }

        static readonly string[] ControlFallback = { "", "Guildchat Listen", "Guildchat Speak", "Officerchat Listen", "Officerchat Speak", "Promote", "Demote",
            "Invite Member", "Remove Member", "Set MOTD", "Edit Public Note", "View Officer Note", "Edit Officer Note", "Modify Guild Info" };

        private static TextMeshProUGUI Line(Transform parent, string name, float x, float y, float size, Color c)
        {
            TextMeshProUGUI t = UiKit.Text(parent, name, "", size, c, TextAlignmentOptions.MidlineLeft, x, y, 180f, 14f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            return t;
        }

        private static UiButton Header(Transform parent, string label, float x, float y, float w, Action click)
        {
            // GuildFrameColumnHeaderTemplate (FriendsFrame.xml:1036-1090) = WhoFrame-ColumnTabs 5 + middle + 4, GameFontHighlightSmall at (8, 0)
            UiButton b = UiKit.Button(parent, "Header", x, y, w, 24f);
            Texture2D art = UiArt.Tex(@"Interface\FriendsFrame\WhoFrame-ColumnTabs.blp");
            UiKit.Pic(b.transform, "Left", art, 0f, 0.078125f, 0f, 0.75f, 0f, 0f, 5f, 24f);
            UiKit.Pic(b.transform, "Middle", art, 0.078125f, 0.90625f, 0f, 0.75f, 5f, 0f, w - 9f, 24f);
            UiKit.Pic(b.transform, "Right", art, 0.90625f, 0.96875f, 0f, 0.75f, w - 4f, 0f, 4f, 24f);
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(@"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp"), -2f, -5f, w + 4f, 22f);
            b.Hilite.enabled = false;
            b.Label = UiKit.Text(b.transform, "Text", label, 10f, Color.white, TextAlignmentOptions.MidlineLeft, 8f, 0f, w - 10f, 24f);
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            b.LabelColor = Color.white;
            b.Click = click;
            return b;
        }

        private GuildView G()
        {
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            return soc != null ? soc.State.Guild : null;
        }

        public void OnShown()
        {
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            if (soc == null) return;
            _asked = Time.unscaledTime;
            soc.GuildRefresh();   // WoW's GuildRoster() on show (FriendsFrame.lua:1435-1460)
            _version++;
        }

        public void OnHidden()
        {
            ShowInfo(false);
            ShowControl(false);
        }

        // ------------------------------------------------------------------ the roster

        private void SetHeaders()
        {
            if (_guildStatus)
            {
                string[] k = { "NAME", "RANK", "LABEL_NOTE", "LASTONLINE" }, f = { "Name", "Rank", "Note", "Last Online" };   // :6016 / :4666 / :4671
                float[] w = { 83f, 75f, 75f, 75f };
                float x = 20f;
                for (int i = 0; i < 4; i++) { Place(_headers[i], x, w[i]); _headers[i].SetLabel(SocialUi.G(k[i], f[i])); x += w[i]; }
            }
            else
            {
                string[] k = { "NAME", "ZONE", "LEVEL_ABBR", "CLASS" }, f = { "Name", "Zone", "Lvl", "Class" };
                float[] w = { 83f, 105f, 32f, 92f };
                float x = 20f;
                for (int i = 0; i < 4; i++) { Place(_headers[i], x, w[i]); _headers[i].SetLabel(SocialUi.G(k[i], f[i])); x += w[i]; }
            }
            float[] cx = _guildStatus ? new[] { 2f, 85f, 160f, 235f } : new[] { 2f, 85f, 187f, 221f };
            float[] cw = _guildStatus ? new[] { 81f, 73f, 73f, 75f } : new[] { 81f, 100f, 30f, 90f };
            for (int i = 0; i < Rows; i++)
                for (int c = 0; c < 4; c++)
                {
                    TextMeshProUGUI t = _rows[i].Col[c];
                    UiKit.Move(t.rectTransform, cx[c], 0f);
                    UiKit.Size(t.rectTransform, cw[c], RowH);
                    t.alignment = !_guildStatus && c == 2 ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
                }
            // the toggle shows the other view's arrow
            _toggle.ArtUp = UiArt.Tex(_guildStatus ? UiArt.PrevUp : UiArt.NextUp);
            if (_toggle.Art != null) _toggle.Art.texture = _toggle.ArtUp;
        }

        private static void Place(UiButton b, float x, float w)
        {
            UiKit.Move(b.Rt, x, 70f);
            UiKit.Size(b.Rt, w, 24f);
            Transform mid = b.transform.Find("Middle"), right = b.transform.Find("Right"), hl = b.transform.Find("Highlight");
            if (mid != null) UiKit.Size((RectTransform)mid, w - 9f, 24f);
            if (right != null) UiKit.Move((RectTransform)right, w - 4f, 0f);
            if (hl != null) UiKit.Size((RectTransform)hl, w + 4f, 22f);
            if (b.Label != null) UiKit.Size(b.Label.rectTransform, w - 10f, 24f);
        }

        private void ToggleView()
        {
            _guildStatus = !_guildStatus;   // GuildFrameGuildListToggleButton_OnClick (FriendsFrame.lua:1854)
            _sort = 0;
            SetHeaders();
            _version++;
        }

        private void ToggleOffline()
        {
            _offline = !_offline;           // SetGuildRosterShowOffline (off at first use, like GetGuildRosterShowOffline())
            _version++;
        }

        private void Sort(int by) { _sort = by; _version++; }

        private void Scroll(int by) { _top = Mathf.Clamp(_top + by, 0, Mathf.Max(0, _shown.Count - Rows)); _version++; }

        public void Refresh(float now)
        {
            GuildView g = G();
            if (g == null) return;
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            if (soc != null && now - _asked > RefreshSeconds) { _asked = now; soc.GuildRefresh(); }
            int wheel = SocialUi.Wheel(_list);
            if (wheel != 0) Scroll(-wheel);
            int counter = soc != null ? soc.State.GuildCounter : 0;
            if (counter == _seen && _drawn == _version) return;
            _seen = counter;
            _drawn = _version;
            Draw(g);
        }

        private string SortKey(GuildMemberView m, int col)
        {
            if (_guildStatus)
            {
                switch (col) { case 2: return m.Rank.ToString("D2"); case 3: return m.Note ?? ""; case 4: return m.Online ? "" : m.DaysOffline.ToString("0000.000"); default: return m.Name ?? ""; }
            }
            switch (col) { case 2: return m.ZoneName ?? ""; case 3: return m.Level.ToString("D3"); case 4: return SocialUi.ClassName(m.Class); default: return m.Name ?? ""; }
        }

        private void Draw(GuildView g)
        {
            _shown.Clear();
            for (int i = 0; i < g.Members.Count; i++) if (_offline || g.Members[i].Online) _shown.Add(g.Members[i]);
            if (_sort > 0) _shown.Sort((a, b) => string.Compare(SortKey(a, _sort), SortKey(b, _sort), StringComparison.OrdinalIgnoreCase));
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _shown.Count - Rows));
            for (int i = 0; i < Rows; i++)
            {
                SocialUi.Row r = _rows[i];
                int idx = _top + i;
                if (idx >= _shown.Count) { SocialUi.Show(r.B, false); continue; }
                GuildMemberView m = _shown[idx];
                SocialUi.Show(r.B, true);
                bool sel = !string.IsNullOrEmpty(_sel) && string.Equals(m.Name, _sel, StringComparison.OrdinalIgnoreCase);
                r.Sel.enabled = sel;
                Color name = !m.Online ? UiArt.Grey : sel ? Color.white : UiArt.Gold, rest = m.Online ? Color.white : UiArt.Grey;
                SocialUi.SetText(r.Col[0], SocialUi.Name(m.Name), name);
                if (_guildStatus)
                {
                    SocialUi.SetText(r.Col[1], g.RankName(m.Rank), rest);
                    SocialUi.SetText(r.Col[2], m.Note ?? "", rest);
                    SocialUi.SetText(r.Col[3], m.Online ? SocialUi.G("GUILD_ONLINE_LABEL", "Online") : SocialUi.LastOnline(m.DaysOffline), rest);
                }
                else
                {
                    SocialUi.SetText(r.Col[1], m.ZoneName ?? "", rest);
                    SocialUi.SetText(r.Col[2], m.Level.ToString(), rest);
                    SocialUi.SetText(r.Col[3], SocialUi.ClassName(m.Class), rest);
                }
            }
            // GuildFrameTotals / GuildFrameOnlineTotals (FriendsFrame.lua:986-987)
            string tot = SocialUi.FRich("GUILD_TOTAL", "|cffffffff%d|r |4Guild Member:Guild Members;", g.Members.Count) + " "
                         + SocialUi.FRich("GUILD_TOTALONLINE", "(|cffffffff%d|r |cff00ff00Online|r)", g.OnlineCount);   // GlobalStrings.lua:3987 / :3988
            if (_totals.text != tot) _totals.text = tot;
            SocialUi.SetText(_motd, g.Motd ?? "", Color.white);
            bool on = _showOffline.Checked != null && _showOffline.Checked.enabled;
            if (on != _offline && _showOffline.Checked != null) _showOffline.Checked.enabled = _offline;
            _addMember.Interactable = g.Can(GuildRights.Invite);
            _control.Interactable = g.MyRank == 0;   // IsGuildLeader
            _motdButton.Interactable = g.Can(GuildRights.SetMotd);
            DrawDetail(g);
            if (_info.gameObject.activeSelf) DrawInfo(g);
            if (_controlPanel.gameObject.activeSelf) DrawControl(g);
        }

        private GuildMemberView Member(GuildView g, string name)
        {
            if (g == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < g.Members.Count; i++) if (string.Equals(g.Members[i].Name, name, StringComparison.OrdinalIgnoreCase)) return g.Members[i];
            return null;
        }

        private void MemberClick(int row, bool menu)
        {
            int idx = _top + row;
            if (idx < 0 || idx >= _shown.Count) return;
            GuildMemberView m = _shown[idx];
            bool again = string.Equals(m.Name, _sel, StringComparison.OrdinalIgnoreCase);
            _sel = m.Name;
            _version++;
            if (menu || (_ctx.Vr && again && !_detail.gameObject.activeSelf))
            {
                Vector2 p = new Vector2(150f, 98f + row * RowH + 4f);
                if (!_ctx.Vr) UiNameMenu.MouseIn(_owner.Root, out p);
                UiNameMenu.Open(_owner.Root, p, m.Name, m.Guid, m.Online, false, true);
            }
        }

        // ------------------------------------------------------------------ the member detail

        private string _me;

        private void DrawDetail(GuildView g)
        {
            GuildMemberView m = Member(g, _sel);
            bool show = m != null && !_info.gameObject.activeSelf && !_controlPanel.gameObject.activeSelf;
            SocialUi.Show(_detail, show);
            if (!show) return;
            _me = _ctx.P != null ? _ctx.P.Name : null;
            bool self = string.Equals(m.Name, _me, StringComparison.OrdinalIgnoreCase);
            SocialUi.SetText(_dName, SocialUi.Name(m.Name), UiArt.Gold);
            SocialUi.SetText(_dLevel, SocialUi.F("FRIENDS_LEVEL_TEMPLATE", "Level %d %s", m.Level, SocialUi.ClassName(m.Class)), Color.white);   // :3741
            SocialUi.SetText(_dZone, m.ZoneName ?? "", Color.white);
            SocialUi.SetText(_dRank, g.RankName(m.Rank), UiArt.Gold);
            SocialUi.SetText(_dOnline, m.Online ? SocialUi.G("GUILD_ONLINE_LABEL", "Online") : SocialUi.G("LASTONLINE", "Last Online") + ": " + SocialUi.LastOnline(m.DaysOffline),
                m.Online ? Color.green : UiArt.Grey);
            bool canPublic = g.Can(GuildRights.EditPublicNote), viewOfficer = g.Can(GuildRights.ViewOfficerNote), editOfficer = g.Can(GuildRights.EditOfficerNote);
            SocialUi.SetText(_dNote, string.IsNullOrEmpty(m.Note) ? (canPublic ? SocialUi.G("GUILD_NOTE_EDITLABEL", "Click here to set a Public Note.") : "") : m.Note,   // :3968
                string.IsNullOrEmpty(m.Note) ? UiArt.Grey : Color.white);
            _dNoteButton.Interactable = canPublic;
            SocialUi.Show(_dOfficerLabel, viewOfficer);
            SocialUi.Show(_dOfficerButton, viewOfficer);
            if (viewOfficer)
            {
                SocialUi.SetText(_dOfficer, string.IsNullOrEmpty(m.OfficerNote) ? (editOfficer ? SocialUi.G("GUILD_OFFICERNOTE_EDITLABEL", "Click here to set an Officer's Note.") : "") : m.OfficerNote,   // :3971
                    string.IsNullOrEmpty(m.OfficerNote) ? UiArt.Grey : Color.white);
                _dOfficerButton.Interactable = editOfficer;
            }
            // the roster rules (proposal-social.md 3.1 / 6.5.3): promote only a rank more than one below ours, demote only below ours and not the lowest
            _dPromote.Interactable = !self && g.Can(GuildRights.Promote) && m.Rank > g.MyRank + 1;
            _dDemote.Interactable = !self && g.Can(GuildRights.Demote) && m.Rank > g.MyRank && m.Rank < g.Ranks.Count - 1;
            _dRemove.Interactable = !self && g.Can(GuildRights.Remove) && m.Rank > g.MyRank;
            _dInvite.Interactable = !self && m.Online;
        }

        private void RankChange(bool promote)
        {
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            if (soc == null || string.IsNullOrEmpty(_sel)) return;
            if (promote) soc.GuildPromote(_sel);
            else soc.GuildDemote(_sel);
        }

        /// <summary>StaticPopup REMOVE_GUILDMEMBER (StaticPopup.lua:1964): REMOVE_GUILDMEMBER_LABEL, Yes / No -> GuildUninvite.</summary>
        private void RemoveClick()
        {
            string n = _sel;
            if (string.IsNullOrEmpty(n)) return;
            SocialUi.Confirm("REMOVE_GUILDMEMBER", SocialUi.F("REMOVE_GUILDMEMBER_LABEL", "Are you sure you want to remove %s from the guild?", n),   // GlobalStrings.lua:6048
                SocialUi.G("YES", "Yes"), SocialUi.G("NO", "No"), () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildRemove(n); });
        }

        /// <summary>SET_GUILDPLAYERNOTE / SET_GUILDOFFICERNOTE (StaticPopup.lua:2052 / :2084, maxLetters 31).</summary>
        private void EditNote(bool officer)
        {
            GuildView g = G();
            GuildMemberView m = Member(g, _sel);
            if (m == null) return;
            string n = m.Name;
            if (officer)
                SocialUi.Ask("SET_GUILDOFFICERNOTE", SocialUi.G("SET_GUILDOFFICERNOTE_LABEL", "Set Officer Note:"), 31, m.OfficerNote ?? "",   // :6227
                    t => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildSetNote(n, t, true); }, true);
            else
                SocialUi.Ask("SET_GUILDPLAYERNOTE", SocialUi.G("SET_GUILDPLAYERNOTE_LABEL", "Set Player Note:"), 31, m.Note ?? "",          // :6228
                    t => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildSetNote(n, t, false); }, true);
        }

        /// <summary>GuildMOTDEditButton -> SET_GUILDMOTD (StaticPopup.lua:2021, maxLetters 128) with CanEditMOTD.</summary>
        private void EditMotd()
        {
            GuildView g = G();
            if (g == null || !g.Can(GuildRights.SetMotd)) return;
            SocialUi.Ask("SET_GUILDMOTD", SocialUi.G("SET_GUILDMOTD_LABEL", "Set Guild Message Of The Day:"), 128, g.Motd ?? "",   // :6226
                t => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildSetMotd(t); }, true);
        }

        /// <summary>GuildFrameAddMemberButton -> ADD_GUILDMEMBER (StaticPopup.lua:1905, maxLetters 12) -> GuildInvite.</summary>
        private void AddMemberClick()
        {
            SocialUi.Ask("ADD_GUILDMEMBER", SocialUi.G("ADD_GUILDMEMBER_LABEL", "Add Guild Member:"), 12, "",   // :505
                t => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildInvite(SocialUi.FirstWord(t)); });
        }

        // ------------------------------------------------------------------ Guild Information

        private void ShowInfo(bool on)
        {
            if (on) ShowControl(false);
            if (!on) SocialUi.StopTyping(_infoField);
            SocialUi.Show(_info, on);
            if (on)
            {
                _infoLoaded = false;
                IWowSocial soc = SocialUi.Soc(_ctx.S);
                if (soc != null) soc.GuildRefresh();   // CMSG_GUILD_ROSTER + CMSG_GUILD_INFO (the info text and the created line)
                GuildView g = G();
                if (g != null) DrawInfo(g);
            }
            _version++;
        }

        private void DrawInfo(GuildView g)
        {
            bool can = g.Can(GuildRights.ModifyInfo);
            if (!_infoLoaded || !_infoField.isFocused && !(UiSocialHost.KeyboardIfAny != null && UiSocialHost.KeyboardIfAny.IsOpen && UiSocialHost.KeyboardIfAny.Target == _infoField))
            {
                if (!_infoLoaded || !can) { _infoField.text = g.Info ?? ""; _infoLoaded = true; }
            }
            _infoField.readOnly = !can;
            _infoField.interactable = can;
            SocialUi.Show(_infoSave, can);
            // GUILD_INFO_TEMPLATE "Guild created %1$d-%2$d-%3$d, %4$d players, %5$d accounts" (GlobalStrings.lua:3954); ours: month, day, year
            string line = g.CreatedYear > 0
                ? SocialUi.F("GUILD_INFO_TEMPLATE", "Guild created %1$d-%2$d-%3$d, %4$d players, %5$d accounts", g.CreatedMonth, g.CreatedDay, g.CreatedYear, g.Members.Count, g.NumAccounts)
                : "";
            SocialUi.SetText(_infoLine, line, Color.white);
        }

        private void SaveInfo()
        {
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            GuildView g = G();
            if (soc == null || g == null || !g.Can(GuildRights.ModifyInfo)) return;
            soc.GuildSetInfo(_infoField.text ?? "");   // CMSG_GUILD_INFO_TEXT (500 letters, the session cleans '|')
            ShowInfo(false);
        }

        // ------------------------------------------------------------------ Guild Control (guild master)

        private void ShowControl(bool on)
        {
            GuildView g = G();
            if (on && (g == null || g.MyRank != 0)) return;
            if (on) ShowInfo(false);
            if (!on) SocialUi.StopTyping(_rankName);
            SocialUi.Show(_controlPanel, on);
            if (on)
            {
                _rank = Mathf.Clamp(_rank, 1, Mathf.Max(1, g.Ranks.Count - 1));
                LoadRank(g);
                DrawControl(g);
            }
            _version++;
        }

        private void PickRank(int by)
        {
            GuildView g = G();
            if (g == null || g.Ranks.Count < 2) return;
            int n = g.Ranks.Count - 1;   // rank 0's rights are ALL and not editable (GuildControlPopupFrame skips the leader rank)
            _rank = (_rank - 1 + by + n) % n + 1;
            LoadRank(g);
            DrawControl(g);
        }

        private void LoadRank(GuildView g)
        {
            if (_rank < 0 || _rank >= g.Ranks.Count) return;
            _rights = g.Ranks[_rank].Rights;
            _rankName.text = g.Ranks[_rank].Name ?? "";
        }

        private void ToggleRight(int i)
        {
            uint bit = GuildRights.ControlBits[i];
            _rights ^= bit;
            UiSound.Named((_rights & bit) != 0 ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            GuildView g = G();
            if (g != null) DrawControl(g);
        }

        private void DrawControl(GuildView g)
        {
            if (_rank < 0 || _rank >= g.Ranks.Count) return;
            SocialUi.SetText(_rankLabel, (_rank + 1) + ". " + g.Ranks[_rank].Name, Color.white);
            for (int i = 1; i <= 13; i++)
                if (_checks[i] != null && _checks[i].Checked != null) _checks[i].Checked.enabled = (_rights & GuildRights.ControlBits[i]) != 0;
            _addRank.Interactable = g.Ranks.Count < MaxRanks;
            // GuildControlPopupFrameRemoveRankButton (FriendsFrame.lua:1817-1827): only on the lowest rank of more than 5, not while it has members
            bool lowest = _rank == g.Ranks.Count - 1 && g.Ranks.Count > 5;
            SocialUi.Show(_removeRank, lowest);
            if (lowest)
            {
                bool members = false;
                for (int i = 0; i < g.Members.Count; i++) if (g.Members[i].Rank == _rank) { members = true; break; }
                _removeRank.Interactable = !members;
            }
        }

        /// <summary>GuildControlPopupAcceptButton (FriendsFrame.lua:1738-1749): GuildControlSaveRank(name) with the checked rights. The value always
        /// carries GR_RIGHT_EMPTY 0x40 (risk R2: AzerothCore tests != 0x40, a value without it would grant everything) and the bank bits echoed.</summary>
        private void AcceptControl()
        {
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            GuildView g = G();
            if (soc == null || g == null || _rank < 1 || _rank >= g.Ranks.Count) return;
            uint checkedBits = 0;
            for (int i = 1; i <= 13; i++) checkedBits |= _rights & GuildRights.ControlBits[i];
            uint rights = GuildRights.Empty | checkedBits | (g.Ranks[_rank].Rights & BankBits);
            string name = SocialUi.Text(_rankName);
            if (name.Length == 0) name = g.Ranks[_rank].Name ?? "";
            soc.GuildSetRank(_rank, name, rights);
            ShowControl(false);
        }

        /// <summary>GuildControlPopupFrameAddRankButton -> ADD_GUILDRANK (StaticPopup.lua:1980-1990, maxLetters 15) -> GuildControlAddRank.</summary>
        private void AddRankClick()
        {
            SocialUi.Ask("ADD_GUILDRANK", SocialUi.G("ADD_GUILDRANK_LABEL", "Add Guild Rank:"), 15, "",
                t => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildAddRank(t); });
        }

        private void RemoveRankClick()
        {
            IWowSocial soc = SocialUi.Soc(_ctx.S);
            if (soc == null) return;
            soc.GuildRemoveLastRank();   // CMSG_GUILD_DEL_RANK removes the lowest rank (GuildHandler.cpp:217-223)
            _rank = Mathf.Max(1, _rank - 1);
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// WoW's PetitionFrame (PetitionFrame.xml / .lua, proposal-social.md 4.10): a guild charter's name, its guild master, one line per
    /// required signature (the signer or NOT_YET_SIGNED), the instructions; the owner: Request Signature (from the player target), Rename
    /// Guild, Close; a charter offered to us: Sign Charter, Close - closing it without signing declines (ours by inference: the 3.3.5 frame
    /// has no Decline button, its OnHide calls ClosePetition). Opened by the session (Social.Charter, not the registrar).
    /// </summary>
    public sealed class UiPetition : UiWindow
    {
        const string CharterIcon = @"Interface\PetitionFrame\GuildCharter-Icon.blp";   // PetitionFrame.xml portrait 58 x 58 at (10, -8)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Petition, Frame = "PetitionFrame", TitleKey = "GUILD_CHARTER", Title = "Guild Charter",   // GlobalStrings.lua:3929
                Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,                                    // UIParent.lua:36
                Wanted = s => { IWowSocial x = SocialUi.Soc(s); return x != null && x.Charter != null && !x.Charter.Registrar; },
                NearGuid = s => 0UL,                                                                                           // VR: in front of the player
                Closed = Close,
                OpenSound = "igQuestListOpen", CloseSound = "igQuestListClose",                                               // PetitionFrame.xml:384-389
                Owner = "socialui", Hidden = false,
                Build = (parent, ctx, close) => new UiPetition(parent, ctx, close)
            };
        }

        /// <summary>The player closed it: an offered charter we did not sign is declined (MSG_PETITION_DECLINE), else just closed.</summary>
        private static void Close(IWowSession s)
        {
            IWowSocial soc = SocialUi.Soc(s);
            GuildCharterView c = soc != null ? soc.Charter : null;
            if (c == null) return;
            if (!c.Registrar && !c.Mine && !Signed(s, c)) soc.DeclineCharter();
            else soc.CloseCharter();
        }

        private static bool Signed(IWowSession s, GuildCharterView c)
        {
            string me = s != null && s.Player != null ? s.Player.Name : null;
            if (string.IsNullOrEmpty(me)) return false;
            for (int i = 0; i < c.Signers.Count; i++) if (string.Equals(c.Signers[i], me, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        const int MaxLines = 10;   // ours: the signature lines drawn (AzerothCore's MinPetitionSigns is 0..9, worldserver.conf.dist)

        private readonly TextMeshProUGUI _npcName, _guildName, _master, _instructions, _membersTitle;
        private readonly TextMeshProUGUI[] _members = new TextMeshProUGUI[MaxLines];
        private readonly UiButton _sign, _request, _rename;
        private int _seen = -1;

        public UiPetition(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "PetitionFrame", 0f, 0f, W, H);
            UiKit.Solid(Root, "Left button socket", new Color(0.13f, 0.12f, 0.11f, 1f), 20f, 418f, 78f, 22f);   // lead fix 2026-10-03: the art's see-through socket (UiDialogs)
            UiKit.Frame4(Root, "Art", UiArt.QuestGreeting + "TopLeft.blp", UiArt.QuestGreeting + "TopRight.blp", UiArt.QuestGreeting + "BotLeft.blp", UiArt.QuestGreeting + "BotRight.blp");
            UiKit.Pic(Root, "Portrait", UiArt.Tex(CharterIcon), 10f, 8f, 58f, 58f);
            UiKit.CloseButton(Root, 326f, 14f, close);                                      // CENTER at TOPRIGHT (-42, -30)
            _npcName = UiKit.Text(Root, "PetitionFrameNpcNameText", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 80f, 15f, 245f, 16f);   // TOP (10, -23)
            _npcName.richText = false;
            float y = 95f;
            UiKit.Text(Root, "CharterTitle", SocialUi.G("GUILD_NAME", "Guild Name"), 14f, UiArt.QuestTitle, TextAlignmentOptions.TopLeft, 30f, y, 285f, 18f, UiArt.Morpheus, false);   // :3965
            _guildName = UiKit.Text(Root, "CharterName", "", 12f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 30f, y + 20f, 285f, 16f, null, false);
            UiKit.Text(Root, "MasterTitle", SocialUi.G("GUILD_RANK0_DESC", "Guild Master"), 14f, UiArt.QuestTitle, TextAlignmentOptions.TopLeft, 30f, y + 44f, 285f, 18f, UiArt.Morpheus, false);   // :3977
            _master = UiKit.Text(Root, "MasterName", "", 12f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 30f, y + 64f, 285f, 16f, null, false);
            _membersTitle = UiKit.Text(Root, "MemberTitle", SocialUi.G("MEMBERS", "Members"), 14f, UiArt.QuestTitle, TextAlignmentOptions.TopLeft, 30f, y + 88f, 285f, 18f, UiArt.Morpheus, false);   // :4920
            for (int i = 0; i < MaxLines; i++)
            {
                _members[i] = UiKit.Text(Root, "PetitionFrameMemberName" + (i + 1), "", 11f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 30f, y + 108f + i * 14f, 285f, 14f, null, false);
                _members[i].richText = false;
            }
            _instructions = UiKit.Text(Root, "PetitionFrameInstructions", "", 11f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 30f, y + 250f, 285f, 60f, null, false);
            _guildName.richText = _master.richText = _instructions.richText = false;
            _sign = UiKit.PanelButton(Root, SocialUi.G("SIGN_CHARTER", "Sign Charter"), 22f, 418f, 110f, 22f, () => { IWowSocial x = SocialUi.Soc(Ctx.S); if (x != null) x.SignCharter(); });   // :6320
            _request = UiKit.PanelButton(Root, SocialUi.G("REQUEST_SIGNATURE", "Request Signature"), 22f, 418f, 140f, 22f, RequestClick);   // :6082
            _rename = UiKit.PanelButton(Root, SocialUi.G("RENAME_GUILD", "Rename Guild"), 164f, 418f, 103f, 22f, RenameClick);              // :6056
            UiKit.PanelButton(Root, SocialUi.G("CLOSE", "Close"), 269f, 418f, 75f, 22f, close);                                             // :1645, BOTTOMRIGHT (-40, 72)
            Root.gameObject.SetActive(false);
        }

        public override void Refresh(float now)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            GuildCharterView c = soc != null ? soc.Charter : null;
            if (c == null || c.Registrar) return;
            int sig = c.Counter * 31 + c.Signers.Count;
            if (sig == _seen) return;
            _seen = sig;
            SocialUi.SetText(_npcName, SocialUi.F("GUILD_CHARTER_TEMPLATE", "%s Guild Charter", c.GuildName ?? ""), UiArt.Gold);   // :3933
            SocialUi.SetText(_guildName, c.GuildName ?? "", UiArt.QuestText);
            SocialUi.SetText(_master, SocialUi.Name(c.OwnerName), UiArt.QuestText);
            int need = Mathf.Clamp(c.Needed, 0, MaxLines);
            for (int i = 0; i < MaxLines; i++)
            {
                SocialUi.Show(_members[i], i < need);
                if (i < need) SocialUi.SetText(_members[i], i < c.Signers.Count ? SocialUi.Name(c.Signers[i]) : SocialUi.G("NOT_YET_SIGNED", "<not yet signed>"), UiArt.QuestText);   // :5196
            }
            UiKit.Move(_instructions.rectTransform, 30f, 95f + 108f + need * 14f + 10f);
            SocialUi.SetText(_instructions, c.Mine
                ? SocialUi.G("GUILD_PETITION_LEADER_INSTRUCTIONS", "Select a player you wish to invite and click <request signature>.   To create this guild, turn it in to the guild registrar when you have filled the charter.")   // :3974
                : SocialUi.G("GUILD_PETITION_MEMBER_INSTRUCTIONS", "Click the <Sign Charter> button to become a charter member of this guild."), UiArt.QuestText);   // :3975
            SocialUi.Show(_request, c.Mine);
            SocialUi.Show(_rename, c.Mine);
            SocialUi.Show(_sign, !c.Mine);
            _sign.Interactable = !c.Mine && !Signed(Ctx.S, c);                         // CanSignPetition (ours: not signed yet, not the owner)
            _request.Interactable = c.Mine && (c.Needed <= 0 || c.Signers.Count < c.Needed);
        }

        /// <summary>PetitionFrameRequestButton -> OfferPetition() to the current target; no player target -> ERR_GENERIC_NO_TARGET (GlobalStrings.lua:3064).</summary>
        private void RequestClick()
        {
            IWowSession s = Ctx.S;
            IWowSocial soc = SocialUi.Soc(s);
            if (soc == null) return;
            EntityState t = s.TargetGuid != 0 ? s.Find(s.TargetGuid) : null;
            if (t == null || t.Kind != EntityKind.Player || (s.Player != null && t.Guid == s.Player.Guid))
            {
                Ctx.Say(SocialUi.G("ERR_GENERIC_NO_TARGET", "You have no target."), UiArt.ErrorRed);
                return;
            }
            soc.OfferCharter(t.Guid);
        }

        /// <summary>PetitionFrameRenameButton -> RENAME_GUILD (StaticPopup.lua:650-666, RENAME_GUILD_LABEL, maxLetters 24) -> MSG_PETITION_RENAME.</summary>
        private void RenameClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            GuildCharterView c = soc != null ? soc.Charter : null;
            if (c == null || !c.Mine) return;
            SocialUi.Ask("RENAME_GUILD", SocialUi.G("RENAME_GUILD_LABEL", "Enter new guild name:"), 24, c.GuildName ?? "",   // :6057
                t => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.RenameCharter(t); });
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// WoW's GuildRegistrarFrame (GuildRegistrarFrame.xml / .lua, proposal-social.md 4.10): the greeting (AVAILABLE_SERVICES: Purchase a Guild
    /// Charter / Register a Guild Charter) and the purchase page (the text, ours "This realm needs N signatures." when the realm's number is
    /// not 9, Cost, the guild name of 24 letters, Purchase). Opened by the session's SMSG_PETITION_SHOWLIST (Social.Charter.Registrar).
    /// </summary>
    public sealed class UiGuildRegistrar : UiWindow
    {
        const int StockSignatures = 9;   // GUILD_REGISTRAR_PURCHASE_TEXT says 9 (AzerothCore MinPetitionSigns default)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.GuildRegistrar, Frame = "GuildRegistrarFrame", TitleKey = "GUILD_CHARTER", Title = "Guild Charter",
                Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,                                    // UIParent.lua:38
                Wanted = s => { IWowSocial x = SocialUi.Soc(s); return x != null && x.Charter != null && x.Charter.Registrar; },
                NearGuid = s => { IWowSocial x = SocialUi.Soc(s); return x != null && x.Charter != null ? x.Charter.NpcGuid : 0UL; },
                Closed = s => { IWowSocial x = SocialUi.Soc(s); if (x != null && x.Charter != null) x.CloseCharter(); },
                OpenSound = "igQuestListOpen", CloseSound = "igQuestListClose",                                               // GuildRegistrarFrame.xml:340-346
                Owner = "socialui", Hidden = false,
                Build = (parent, ctx, close) => new UiGuildRegistrar(parent, ctx, close)
            };
        }

        private readonly RectTransform _greeting, _purchase;
        private readonly TextMeshProUGUI _npcName, _text;
        private readonly UiMoney _cost;
        private readonly TMP_InputField _name;
        private readonly UiButton _buy;
        private int _seen = -1;

        public UiGuildRegistrar(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "GuildRegistrarFrame", 0f, 0f, W, H);
            UiKit.Frame4(Root, "Art", UiArt.QuestGreeting + "TopLeft.blp", UiArt.QuestGreeting + "TopRight.blp", UiArt.QuestGreeting + "BotLeft.blp", UiArt.QuestGreeting + "BotRight.blp");
            Portrait(UiArt.Tex(UiArt.PortraitNpc));
            UiKit.CloseButton(Root, 326f, 14f, close);
            _npcName = UiKit.Text(Root, "GuildRegistrarFrameNpcNameText", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 80f, 15f, 245f, 16f);
            _npcName.richText = false;

            // the greeting (GuildRegistrarFrame.xml:150-190)
            _greeting = UiKit.Rect(Root, "Greeting", 0f, 0f, W, H);
            UiKit.Text(_greeting, "AvailableServices", SocialUi.G("AVAILABLE_SERVICES", "Available Services"), 16f, UiArt.QuestTitle, TextAlignmentOptions.TopLeft, 35f, 100f, 300f, 22f, UiArt.Morpheus, false);   // :665
            TitleButton(_greeting, SocialUi.G("GUILD_CHARTER_PURCHASE", "Purchase a Guild Charter"), 35f, 128f, () => Page(true));                    // :3931
            TitleButton(_greeting, SocialUi.G("GUILD_CHARTER_REGISTER", "Register a Guild Charter"), 35f, 148f, () =>                                 // :3932
            {
                IWowSocial x = SocialUi.Soc(Ctx.S);
                if (x != null) x.TurnInCharter();   // TurnInGuildCharter()
            });
            UiKit.PanelButton(_greeting, SocialUi.G("CANCEL", "Cancel"), 269f, 418f, 75f, 22f, close);                                                  // BOTTOMRIGHT (-40, 72)

            // the purchase page
            _purchase = UiKit.Rect(Root, "Purchase", 0f, 0f, W, H);
            _text = UiKit.Text(_purchase, "GuildRegistrarPurchaseText", "", 12f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 35f, 95f, 270f, 110f, null, false);   // 270 wide at (35, -95)
            _text.richText = false;
            UiKit.Text(_purchase, "CostLabel", SocialUi.G("COSTS_LABEL", "Cost:"), 13f, UiArt.QuestTitle, TextAlignmentOptions.MidlineLeft, 35f, 214f, 80f, 16f, null, false);   // :1868
            _cost = new UiMoney(_purchase, 200f, 214f, Color.white);
            _name = SocialUi.Field(_purchase, "GuildRegistrarFrameEditBox", 35f, 240f, 220f, 24f, 24, false, 12f, SocialUi.G("GUILD_NAME", "Guild Name"), BuyClick);   // letters 24
            _buy = UiKit.PanelButton(_purchase, SocialUi.G("PURCHASE", "Purchase"), 22f, 418f, 85f, 22f, BuyClick);                                     // :5748, BOTTOMLEFT (22, 72)
            UiKit.PanelButton(_purchase, SocialUi.G("CANCEL", "Cancel"), 269f, 418f, 75f, 22f, () => Page(false));
            Page(false);
            Root.gameObject.SetActive(false);
        }

        private static void TitleButton(Transform parent, string label, float x, float y, Action click)
        {
            UiButton b = UiKit.Button(parent, "Title " + label, x, y, 300f, 18f);
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, 300f, 18f);
            b.Hilite.enabled = false;
            b.Label = UiKit.Text(b.transform, "Text", label, 12f, UiArt.QuestText, TextAlignmentOptions.MidlineLeft, 4f, 0f, 292f, 18f, null, false);
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            b.LabelColor = UiArt.QuestText;
            b.Click = click;
        }

        private void Page(bool purchase)
        {
            SocialUi.Show(_greeting, !purchase);
            SocialUi.Show(_purchase, purchase);
            if (!purchase) SocialUi.StopTyping(_name);
            else { _name.text = ""; }
            _seen = -1;
        }

        protected override void OnOpen() { Page(false); }

        protected override void OnClose() { SocialUi.StopTyping(_name); }

        public override void Refresh(float now)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            GuildCharterView c = soc != null ? soc.Charter : null;
            if (c == null || !c.Registrar) return;
            _buy.Interactable = SocialUi.Text(_name).Length > 0;   // disabled while the name is empty
            if (c.Counter == _seen) return;
            _seen = c.Counter;
            EntityState npc = Ctx.S.Find(c.NpcGuid);
            SocialUi.SetText(_npcName, npc != null && !string.IsNullOrEmpty(npc.Name) ? npc.Name : "", UiArt.Gold);
            string text = SocialUi.G("GUILD_REGISTRAR_PURCHASE_TEXT", "To create a guild you must purchase this charter, get 9 unique player signatures, and return the charter to me.  Please enter the desired name for your guild.");   // :3982
            if (c.RequiredSignatures > 0 && c.RequiredSignatures != StockSignatures)
                text += "\n\nThis realm needs " + c.RequiredSignatures + " signatures.";   // ours (proposal-social.md 4.7)
            SocialUi.SetText(_text, text, UiArt.QuestText);
            _cost.Set(Mathf.Max(0, c.Cost));
        }

        /// <summary>GuildRegistrarFramePurchaseButton -> BuyGuildCharter(text) (CMSG_PETITION_BUY, the session cuts the name to 24).</summary>
        private void BuyClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            string n = SocialUi.Text(_name);
            if (soc == null || soc.Charter == null || n.Length == 0) return;
            soc.BuyGuildCharter(n);
            SocialUi.StopTyping(_name);
        }
    }
}
