// iteration 11 - owner socialui (proposal-social.md 6.5.1 / 6.5.2; wave-0 stub by w0game, filled by socialui)
// WoW's FriendsFrame (the Social window: Friends / Ignore, Who, Guild, Chat channels, Raid) and the FRIEND unit menu (UiNameMenu) of every
// view that shows a player name. Registered in UiRegistry.Wave0; the O key, the Socials micro button and the VR palm button come from Spec().
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiSocial : UiWindow
    {
        public const int TabFriends = 1, TabWho = 2, TabGuild = 3, TabChat = 4, TabRaid = 5;   // FriendsFrameTab1-5 (FriendsFrame.xml:4729-4830)

        // ---- WoW's (FriendsFrame.lua:1-16, FriendsFrame.xml)
        const int FriendsToDisplay = 9;     // FRIENDS_TO_DISPLAY 10 x FRIENDS_FRAME_FRIEND_HEIGHT 34 in a 307 high scroll frame: 9 whole rows
        const float FriendHeight = 34f;     // FRIENDS_FRAME_FRIEND_HEIGHT
        const int IgnoresToDisplay = 19;    // IGNORES_TO_DISPLAY
        const float IgnoreHeight = 16f;     // FRIENDS_FRAME_IGNORE_HEIGHT
        const int WhosToDisplay = 17;       // WHOS_TO_DISPLAY
        const float WhoHeight = 16f;        // FRIENDS_FRAME_WHO_HEIGHT
        const int ChannelButtons = 16;      // MAX_DISPLAY_CHANNEL_BUTTONS (ChannelFrame.lua:1-4)
        const int ChannelMembers = 22;      // MAX_CHANNEL_MEMBER_BUTTONS
        const float ChannelRosterHeight = 15f;   // CHANNEL_ROSTER_HEIGHT

        // ---- ours (proposal-social.md 4.7)
        const float FriendsRefreshSeconds = 30f;   // the contact list re-asked every 30 s while the Friends tab shows
        const int RaidRows = 15;                   // the fallback Raid page (until interact's UiRaid draws it)

        const string FF = @"Interface\FriendsFrame\", CT = @"Interface\ClassTrainerFrame\", PD = @"Interface\PaperDollInfoFrame\";
        const string ScrollIcon = FF + "FriendsFrameScrollIcon.blp";
        // FriendsFrame.lua:254-326 (the corner art of each tab; the Ignore list without voice uses the "Mute" bottoms like Friends)
        static readonly string[][] Corners =
        {
            null,
            new[] { FF + "UI-FriendsFrame-TopLeft-bnet.blp", FF + "UI-FriendsFrame-TopRight-bnet.blp", FF + "UI-FriendsFrameMute-BotLeft-bnet.blp", FF + "UI-FriendsFrameMute-BotRight-bnet.blp" },
            new[] { CT + "UI-ClassTrainer-TopLeft.blp", CT + "UI-ClassTrainer-TopRight.blp", FF + "WhoFrame-BotLeft.blp", FF + "WhoFrame-BotRight.blp" },
            new[] { CT + "UI-ClassTrainer-TopLeft.blp", CT + "UI-ClassTrainer-TopRight.blp", FF + "GuildFrame-BotLeft.blp", FF + "GuildFrame-BotRight.blp" },
            new[] { PD + "UI-Character-General-TopLeft.blp", PD + "UI-Character-General-TopRight.blp", FF + "UI-ChannelFrame-BotLeft.blp", FF + "UI-ChannelFrame-BotRight.blp" },
            new[] { PD + "UI-Character-General-TopLeft.blp", PD + "UI-Character-General-TopRight.blp", PD + "UI-Character-General-BottomLeft.blp", PD + "UI-Character-General-BottomRight.blp" },
        };
        const string StatusOnline = FF + "StatusIcon-Online.blp", StatusAway = FF + "StatusIcon-Away.blp", StatusDnd = FF + "StatusIcon-DnD.blp",
            StatusOffline = FF + "StatusIcon-Offline.blp";
        const string LeaderIcon = @"Interface\GroupFrame\UI-Group-LeaderIcon.blp";   // UiVrHud.cs:29

        static int _openRequest, _tabRequest, _tabRequestVersion;
        static bool _loggedBuilt;

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Social, Frame = "FriendsFrame", TitleKey = "SOCIAL_BUTTON", Title = "Social",   // GlobalStrings.lua:6873
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, Key = KeyCode.O, Binding = "TOGGLESOCIAL",   // UIParent.lua:31; PLAN11 s2: O = Social
                Micro = "Socials", MicroOrder = 5, Palm = true,                                                      // MainMenuBarMicroButtons.xml:180-200
                OpenSound = "igCharacterInfoTab", CloseSound = "igMainMenuClose",                                    // FriendsFrame.lua:247 / :335
                OpenRequest = s => _openRequest,                                                                     // /friends /who /groster (UiChat)
                Owner = "socialui", Hidden = false,
                Build = (parent, ctx, close) => new UiSocial(parent, ctx, close)
            };
        }

        /// <summary>Opens the Social window on a tab (/friends, /who, /groster, /ginfo): the registry's OpenRequest opens it, Refresh switches.</summary>
        public static void Open(int tab)
        {
            _tabRequest = tab;
            _tabRequestVersion++;
            _openRequest++;
        }

        /// <summary>interact's UiRaid builds the Raid tab into the given 384 x 512 content (bool = VR) - proposal-social.md H9 (its H12).
        /// While it is null the tab shows ours: the group, Invite, Leave Party, Raid Info.</summary>
        public static Action<RectTransform, bool> RaidTab;
        /// <summary>interact's UiRaid refreshes its Raid tab (now) while it shows.</summary>
        public static Action<float> RaidTabRefresh;

        /// <summary>The last built Social window (one per UI layout).</summary>
        public static UiSocial Current { get; private set; }

        private readonly Action _close;
        private readonly SocialUi.Scope _scope;
        private readonly RawImage[] _corner = new RawImage[4];
        private readonly TextMeshProUGUI _title;
        private readonly SocialUi.Tab[] _tabs = new SocialUi.Tab[6];
        private readonly SocialUi.Tab _subFriends, _subIgnore;
        private readonly RectTransform _pFriends, _pIgnore, _pWho, _pGuild, _pChat, _pRaid, _pRaidOurs;
        private RectTransform _pRaidTheirs;
        private int _tab = 0, _sub = 1, _seenTabRequest, _cornerSet = -1;
        private bool _raidDelegated;
        internal readonly UiGuildTab Guild;

        // Friends
        private readonly RectTransform _friendsList;
        private readonly SocialUi.Row[] _friendRows = new SocialUi.Row[FriendsToDisplay];
        private readonly List<FriendView> _friends = new List<FriendView>();
        private readonly UiButton _sendMessage;
        private ulong _selFriend;
        private int _friendsTop, _friendsSeen = -1;
        private float _friendsAsked = -999f;
        // Ignore
        private readonly RectTransform _ignoreList;
        private readonly SocialUi.Row[] _ignoreRows = new SocialUi.Row[IgnoresToDisplay];
        private readonly UiButton _removeIgnore;
        private ulong _selIgnore;
        private int _ignoreTop;
        // Who
        private readonly RectTransform _whoList;
        private readonly SocialUi.Row[] _whoRows = new SocialUi.Row[WhosToDisplay];
        private readonly List<WhoView> _who = new List<WhoView>();
        private readonly TMP_InputField _whoField;
        private readonly TextMeshProUGUI _whoTotals;
        private readonly UiButton _whoColumn, _whoAddFriend, _whoInvite;
        private int _whoTop, _whoSeen = -1, _whoSort = 0, _whoVariable = 0;   // sort: 0 none, 1 name, 2 variable, 3 level, 4 class; variable: 0 zone 1 guild 2 race
        private string _selWho;
        private bool _whoAsked;
        // Chat (channels)
        private readonly UiButton[] _chanRows = new UiButton[ChannelButtons];
        private readonly TextMeshProUGUI[] _chanText = new TextMeshProUGUI[ChannelButtons];
        private readonly RawImage[] _chanSel = new RawImage[ChannelButtons];
        private readonly SocialUi.Row[] _memberRows = new SocialUi.Row[ChannelMembers];
        private readonly RectTransform _memberList, _newChannel;
        private readonly TMP_InputField _newName, _newPassword;
        private readonly TextMeshProUGUI _membersTitle;
        private int _selChannel, _chanSeen = -1, _memberTop;
        // Raid (ours)
        private readonly SocialUi.Row[] _raidRows = new SocialUi.Row[RaidRows];
        private readonly UiButton _leaveParty;
        private int _raidSig = int.MinValue, _raidTop;

        private int _version, _drawn = -1;

        public UiSocial(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            _scope = SocialUi.Scope.Capture();
            Root = UiKit.Rect(parent, "FriendsFrame", 0f, 0f, W, H);
            float[] cx = { 0f, 256f, 0f, 256f }, cy = { 0f, 0f, 256f, 256f }, cw = { 256f, 128f, 256f, 128f };
            string[] cn = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
            for (int i = 0; i < 4; i++) _corner[i] = UiKit.Pic(Root, cn[i], null, cx[i], cy[i], cw[i], 256f);
            Portrait(UiArt.Tex(ScrollIcon));                                    // FriendsFrame.xml:1224-1236, 60 x 60 at (7, -6)
            _title = Title("", 16f);                                            // FriendsFrameTitleText TOP (3, -16)
            UiKit.CloseButton(Root, 322f, 8f, close);                           // TOPRIGHT (-30, -8)

            _pFriends = Page("Friends page");
            _pIgnore = Page("Ignore page");
            _pWho = Page("Who page");
            _pGuild = Page("Guild page");
            _pChat = Page("Chat page");
            _pRaid = Page("Raid page");
            _pRaidOurs = UiKit.Rect(_pRaid, "Raid (ours)", 0f, 0f, W, H);

            // ---- Friends / Ignore sub-tabs (FriendsTabHeaderTab1 / 2 at TOPLEFT (30, -64) of the header; ours 80 x 24)
            Transform hdr = Root;
            _subFriends = SocialUi.SubTab(hdr, SocialUi.G("FRIENDS", "Friends"), 30f, 64f, 80f, () => SetSub(1));      // GlobalStrings.lua:3730
            _subIgnore = SocialUi.SubTab(hdr, SocialUi.G("IGNORE", "Ignore"), 112f, 64f, 80f, () => SetSub(2));        // :4220

            // ---- Friends list (FriendsFrameFriendsScrollFrame 302 x 307 at TOPLEFT (21, -100))
            _friendsList = UiKit.Rect(_pFriends, "FriendsList", 21f, 100f, 302f, 307f);
            for (int i = 0; i < FriendsToDisplay; i++)
            {
                int k = i;
                SocialUi.Row r = SocialUi.MakeRow(_friendsList, "Friend " + (i + 1), 0f, i * FriendHeight, 298f, FriendHeight,
                    new[] { 22f, 22f }, new[] { 270f, 270f }, 12f, true);
                UiKit.Move(r.Icon.rectTransform, 3f, 3f);
                UiKit.Move(r.Col[0].rectTransform, 22f, 1f); UiKit.Size(r.Col[0].rectTransform, 270f, 16f);
                UiKit.Move(r.Col[1].rectTransform, 22f, 17f); UiKit.Size(r.Col[1].rectTransform, 270f, 14f);
                r.Col[1].fontSize = 10f;
                r.B.Click = () => FriendClick(k, false);
                r.B.RightClick = () => FriendClick(k, true);
                r.B.LongPress = () => FriendClick(k, true);
                _friendRows[i] = r;
            }
            SocialUi.ScrollPair(_pFriends, 325f, 100f, 391f, () => ScrollFriends(-1), () => ScrollFriends(1));
            UiKit.PanelButton(_pFriends, SocialUi.G("ADD_FRIEND", "Add Friend"), 15f, 410f, 131f, 21f, AddFriendClick);              // :503, BOTTOMLEFT (15, 81)
            _sendMessage = UiKit.PanelButton(_pFriends, SocialUi.G("SEND_MESSAGE", "Send Message"), 212f, 410f, 131f, 21f, SendMessageClick);   // :6213, BOTTOMRIGHT (-41, 81)

            // ---- Ignore list (19 x 16)
            _ignoreList = UiKit.Rect(_pIgnore, "IgnoreList", 21f, 100f, 298f, IgnoresToDisplay * IgnoreHeight);
            for (int i = 0; i < IgnoresToDisplay; i++)
            {
                int k = i;
                SocialUi.Row r = SocialUi.MakeRow(_ignoreList, "Ignore " + (i + 1), 0f, i * IgnoreHeight, 298f, IgnoreHeight, new[] { 8f }, new[] { 280f }, 12f, false);
                r.B.Click = () => IgnoreClick(k);
                _ignoreRows[i] = r;
            }
            SocialUi.ScrollPair(_pIgnore, 325f, 100f, 391f, () => ScrollIgnore(-1), () => ScrollIgnore(1));
            UiKit.PanelButton(_pIgnore, SocialUi.G("IGNORE_PLAYER", "Ignore Player"), 15f, 410f, 131f, 21f, IgnorePlayerClick);      // :4225
            _removeIgnore = UiKit.PanelButton(_pIgnore, SocialUi.G("REMOVE_PLAYER", "Remove Player"), 212f, 410f, 134f, 21f, RemoveIgnoreClick);   // :6052, BOTTOMRIGHT (-38, 81)

            // ---- Who (headers TOPLEFT (20, -70): 83 / 105 / 32 / 92; WhoListScrollFrame 296 x 287 TOPRIGHT (-67, -96))
            HeaderButton(_pWho, SocialUi.G("NAME", "Name"), 20f, 70f, 83f, () => SortWho(1));
            _whoColumn = HeaderButton(_pWho, SocialUi.G("ZONE", "Zone"), 103f, 70f, 105f, CycleWhoColumn);
            HeaderButton(_pWho, SocialUi.G("LEVEL_ABBR", "Lvl"), 208f, 70f, 32f, () => SortWho(3));
            HeaderButton(_pWho, SocialUi.G("CLASS", "Class"), 240f, 70f, 92f, () => SortWho(4));
            _whoList = UiKit.Rect(_pWho, "WhoList", 21f, 96f, 296f, WhosToDisplay * WhoHeight);
            for (int i = 0; i < WhosToDisplay; i++)
            {
                int k = i;
                SocialUi.Row r = SocialUi.MakeRow(_whoList, "Who " + (i + 1), 0f, i * WhoHeight, 312f, WhoHeight,
                    new[] { 2f, 85f, 187f, 221f }, new[] { 81f, 100f, 30f, 90f }, 11f, false);
                r.Col[2].alignment = TextAlignmentOptions.Center;
                r.B.Click = () => WhoClick(k, false);
                r.B.RightClick = () => WhoClick(k, true);
                r.B.LongPress = () => WhoClick(k, true);
                _whoRows[i] = r;
            }
            SocialUi.ScrollPair(_pWho, 335f, 96f, 352f, () => ScrollWho(-1), () => ScrollWho(1));
            _whoTotals = UiKit.Text(_pWho, "WhoFrameTotals", "", 11f, UiArt.Gold, TextAlignmentOptions.Center, 33f, 366f, 298f, 16f);   // BOTTOM (-10, 127)
            _whoField = SocialUi.Field(_pWho, "WhoFrameEditBox", 24f, 386f, 296f, 20f, 0, false, 12f, SocialUi.G("WHO", "Who"), WhoRefreshClick);   // BOTTOM (-20, 100); ours: 20 high
            UiKit.PanelButton(_pWho, SocialUi.G("REFRESH", "Refresh"), 19f, 408f, 85f, 22f, WhoRefreshClick);                             // :6039
            _whoAddFriend = UiKit.PanelButton(_pWho, SocialUi.G("ADD_FRIEND", "Add Friend"), 104f, 408f, 120f, 22f, WhoAddFriendClick);
            _whoInvite = UiKit.PanelButton(_pWho, SocialUi.G("GROUP_INVITE", "Group Invite"), 224f, 408f, 120f, 22f, WhoInviteClick);   // :3855, BOTTOMRIGHT (-40, 82)

            // ---- Guild (UiGuild.cs)
            Guild = new UiGuildTab(_pGuild, ctx, this, _scope);

            // ---- Chat channels (ChannelFrame: channel buttons 155 x 20, the roster 130 x 15)
            for (int i = 0; i < ChannelButtons; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(_pChat, "Channel " + (i + 1), 22f, 74f + i * 20f, 155f, 20f);
                _chanSel[i] = UiKit.Pic(b.transform, "Selected", UiArt.Add(FF + "UI-FriendsFrame-HighlightBar.blp"), 0f, 0f, 155f, 20f);
                _chanSel[i].color = new Color(1f, 0.82f, 0f, 0.6f);
                _chanSel[i].enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(FF + "UI-FriendsFrame-HighlightBar.blp"), 0f, 0f, 155f, 20f);
                b.Hilite.enabled = false;
                _chanText[i] = UiKit.Text(b.transform, "Text", "", 11f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 6f, 0f, 147f, 20f);
                _chanText[i].textWrappingMode = TextWrappingModes.NoWrap;
                _chanText[i].overflowMode = TextOverflowModes.Ellipsis;
                _chanText[i].richText = false;
                b.Click = () => ChannelClick(k, false);
                b.RightClick = () => ChannelClick(k, true);
                b.LongPress = () => ChannelClick(k, true);
                b.gameObject.SetActive(false);
                _chanRows[i] = b;
            }
            _membersTitle = UiKit.Text(_pChat, "Roster title", "", 11f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 192f, 56f, 158f, 16f);
            _membersTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _membersTitle.overflowMode = TextOverflowModes.Ellipsis;
            _membersTitle.richText = false;
            _memberList = UiKit.Rect(_pChat, "ChannelRoster", 192f, 74f, 158f, ChannelMembers * ChannelRosterHeight);
            for (int i = 0; i < ChannelMembers; i++)
            {
                int k = i;
                SocialUi.Row r = SocialUi.MakeRow(_memberList, "Member " + (i + 1), 0f, i * ChannelRosterHeight, 130f, ChannelRosterHeight, new[] { 4f }, new[] { 124f }, 11f, false);
                r.B.Click = () => MemberClick(k);
                r.B.RightClick = () => MemberClick(k);
                r.B.LongPress = () => MemberClick(k);
                _memberRows[i] = r;
            }
            SocialUi.ScrollPair(_pChat, 326f, 74f, 390f, () => ScrollMembers(-1), () => ScrollMembers(1));
            UiKit.PanelButton(_pChat, SocialUi.G("ADD", "Add"), 263f, 408f, 80f, 22f, () => ShowNewChannel(true));   // ChannelFrame.xml:350-380, BOTTOMRIGHT (-12, 82)
            // WoW's New Channel box (ChannelFrameDaughterFrame 212 x 200, ChannelFrame.xml:800-990)
            _newChannel = UiKit.Rect(_pChat, "ChannelFrameDaughterFrame", 86f, 150f, 212f, 200f);
            UiBackdrop nb = UiBackdrop.Make(_newChannel, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            nb.Place(0f, 0f, 212f, 200f);
            UiKit.Text(_newChannel, "Title", SocialUi.G("CHANNEL_NEW_CHANNEL", "New Channel"), 13f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 14f, 212f, 18f);   // GlobalStrings
            UiKit.Text(_newChannel, "Name label", SocialUi.G("CHANNEL_CHANNEL_NAME", "Channel Name"), 11f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 20f, 40f, 172f, 16f);
            _newName = SocialUi.Field(_newChannel, "ChannelName", 20f, 58f, 172f, 22f, 31, false, 12f, SocialUi.G("CHANNEL_CHANNEL_NAME", "Channel Name"), NewChannelOkay);   // letters 31
            UiKit.Text(_newChannel, "Password label", SocialUi.G("PASSWORD", "Password") + " " + SocialUi.G("OPTIONAL_PARENS", "(optional)"), 11f, UiArt.Gold,
                TextAlignmentOptions.MidlineLeft, 20f, 88f, 172f, 16f);
            _newPassword = SocialUi.Field(_newChannel, "ChannelPassword", 20f, 106f, 172f, 22f, 31, false, 12f, SocialUi.G("PASSWORD", "Password"), NewChannelOkay);
            UiKit.PanelButton(_newChannel, SocialUi.G("OKAY", "Okay"), 20f, 152f, 80f, 22f, NewChannelOkay);
            UiKit.PanelButton(_newChannel, SocialUi.G("CANCEL", "Cancel"), 112f, 152f, 80f, 22f, () => ShowNewChannel(false));
            _newChannel.gameObject.SetActive(false);

            // ---- Raid (ours until interact's UiRaid fills it, H9)
            UiKit.Text(_pRaidOurs, "Group title", SocialUi.G("RAID", "Raid"), 12f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 24f, 74f, 200f, 16f);
            for (int i = 0; i < RaidRows; i++)
            {
                int k = i;
                SocialUi.Row r = SocialUi.MakeRow(_pRaidOurs, "Member " + (i + 1), 22f, 94f + i * 20f, 310f, 20f, new[] { 22f, 160f }, new[] { 136f, 150f }, 11f, true);
                r.B.Click = () => RaidMemberClick(k);
                r.B.RightClick = () => RaidMemberClick(k);
                r.B.LongPress = () => RaidMemberClick(k);
                _raidRows[i] = r;
            }
            SocialUi.ScrollPair(_pRaidOurs, 335f, 94f, 378f, () => ScrollRaid(-1), () => ScrollRaid(1));
            UiKit.PanelButton(_pRaidOurs, SocialUi.G("PARTY_INVITE", "Invite"), 19f, 408f, 96f, 22f, RaidInviteClick);         // ADD_RAIDMEMBER popup
            _leaveParty = UiKit.PanelButton(_pRaidOurs, SocialUi.G("PARTY_LEAVE", "Leave party"), 119f, 408f, 110f, 22f, () => { if (Ctx.S != null) Ctx.S.LeaveGroup(); });
            UiKit.PanelButton(_pRaidOurs, SocialUi.G("RAID_INFO", "Raid Info"), 233f, 408f, 110f, 22f, () => UiInstance.ShowRaidInfo());   // GlobalStrings.lua:5961

            // ---- the bottom tabs (FriendsFrameTabTemplate: Tab1 BOTTOMLEFT (11, 46), each next LEFT of the previous RIGHT (-15, 0))
            string[] labels = { null, SocialUi.G("FRIENDS", "Friends"), SocialUi.G("WHO", "Who"), SocialUi.G("GUILD", "Guild"), SocialUi.G("CHAT", "Chat"), SocialUi.G("RAID", "Raid") };
            float tx = 11f;
            for (int t = 1; t <= 5; t++)
            {
                int k = t;
                _tabs[t] = SocialUi.BottomTab(Root, labels[t], tx, H - 46f - 32f, () => TabClick(k));
                tx += _tabs[t].W - 15f;
            }

            if (RaidTab != null) DelegateRaid();
            SetTab(TabFriends);
            Root.gameObject.SetActive(false);
            Current = this;
            if (!_loggedBuilt)
            {
                _loggedBuilt = true;
                WowLog.Info("UI: Social window built (WoW's FriendsFrame 384 x 512: Friends / Ignore, Who, Guild, Chat, Raid" + (_raidDelegated ? " (interact's raid page)" : " (ours until interact's raid page)")
                            + "; O / the Socials micro button / the palm; " + (ctx.Vr ? "VR lid" : "desktop") + ").");
            }
        }

        private RectTransform Page(string name)
        {
            RectTransform r = UiKit.Rect(Root, name, 0f, 0f, W, H);
            r.gameObject.SetActive(false);
            return r;
        }

        private static UiButton HeaderButton(Transform parent, string label, float x, float y, float w, Action click)
        {
            // WhoFrameColumnHeaderTemplate (FriendsFrame.xml:796-850): WhoFrame-ColumnTabs 5 + middle + 4, GameFontHighlightSmall at LEFT (8, 0)
            UiButton b = UiKit.Button(parent, "Header " + label, x, y, w, 24f);
            UiKit.Pic(b.transform, "Left", UiArt.Tex(@"Interface\FriendsFrame\WhoFrame-ColumnTabs.blp"), 0f, 0.078125f, 0f, 0.75f, 0f, 0f, 5f, 24f);
            UiKit.Pic(b.transform, "Middle", UiArt.Tex(@"Interface\FriendsFrame\WhoFrame-ColumnTabs.blp"), 0.078125f, 0.90625f, 0f, 0.75f, 5f, 0f, w - 9f, 24f);
            UiKit.Pic(b.transform, "Right", UiArt.Tex(@"Interface\FriendsFrame\WhoFrame-ColumnTabs.blp"), 0.90625f, 0.96875f, 0f, 0.75f, w - 4f, 0f, 4f, 24f);
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(@"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp"), -2f, -5f, w + 4f, 22f);   // TOPLEFT (-2, 5) BOTTOMRIGHT (2, -7)
            b.Hilite.enabled = false;
            b.Label = UiKit.Text(b.transform, "Text", label, 10f, Color.white, TextAlignmentOptions.MidlineLeft, 8f, 0f, w - 10f, 24f);
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            b.LabelColor = Color.white;
            b.Click = click;
            return b;
        }

        // ================================================================== tabs

        private void TabClick(int t)
        {
            if (t == TabGuild && !InGuild()) return;   // FriendsFrame.lua:1435 - a click on the disabled Guild tab does nothing
            if (t == _tab) return;
            UiSound.Named("igCharacterInfoTab");       // FriendsFrameTab OnClick (PanelTemplates_Tab_OnClick + PlaySound)
            SetTab(t);
        }

        private bool InGuild()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            return soc != null && soc.State.Guild != null;
        }

        internal void SetTab(int t)
        {
            if (t < TabFriends || t > TabRaid) t = TabFriends;
            if (t == TabGuild && !InGuild()) t = TabFriends;   // InGuildCheck (FriendsFrame.lua:1841-1850)
            bool changed = t != _tab;
            _tab = t;
            for (int i = 1; i <= 5; i++) _tabs[i].Select(i == t);
            SocialUi.Show(_pFriends, t == TabFriends && _sub == 1);
            SocialUi.Show(_pIgnore, t == TabFriends && _sub == 2);
            SocialUi.Show(_subFriends.B, t == TabFriends);
            SocialUi.Show(_subIgnore.B, t == TabFriends);
            _subFriends.Select(_sub == 1);
            _subIgnore.Select(_sub == 2);
            SocialUi.Show(_pWho, t == TabWho);
            SocialUi.Show(_pGuild, t == TabGuild);
            SocialUi.Show(_pChat, t == TabChat);
            SocialUi.Show(_pRaid, t == TabRaid);
            if (t != TabWho) SocialUi.StopTyping(_whoField);
            if (t != TabChat) ShowNewChannel(false);
            SetCorners();
            _version++;
            if (!changed || !IsOpen) return;
            OnTabShown();
        }

        private void SetSub(int sub)
        {
            if (sub == _sub) return;
            UiSound.Named("igCharacterInfoTab");
            _sub = sub;
            SetTab(TabFriends);
            _version++;
        }

        private void SetCorners()
        {
            int k = _tab;
            if (_cornerSet == k) return;
            _cornerSet = k;
            for (int i = 0; i < 4; i++) _corner[i].texture = UiArt.Tex(Corners[k][i]);
            for (int i = 0; i < 4; i++) _corner[i].color = Color.white;
        }

        /// <summary>The server asks of a tab that just showed (ours: the Who list once when empty, the contact list, the guild roster).</summary>
        private void OnTabShown()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null) return;
            if (_tab == TabFriends) { _friendsAsked = Time.unscaledTime; soc.RefreshFriends(); }
            else if (_tab == TabWho && soc.State.Who.Count == 0 && !_whoAsked) { _whoAsked = true; soc.Who(""); }   // ours: Who("") once (4.7)
            else if (_tab == TabGuild) Guild.OnShown();
            else if (_tab == TabRaid && !_raidDelegated && RaidTab != null) DelegateRaid();
        }

        protected override void OnOpen()
        {
            Current = this;
            _version++;
            OnTabShown();
        }

        protected override void OnClose()
        {
            SocialUi.StopTyping(_whoField);
            ShowNewChannel(false);
            Guild.OnHidden();
            UiNameMenu.CloseIn(Root);
        }

        private void DelegateRaid()
        {
            if (_raidDelegated || RaidTab == null) return;
            _raidDelegated = true;
            try
            {
                _scope.Run(() =>
                {
                    _pRaidTheirs = UiKit.Rect(_pRaid, "Raid (interact)", 0f, 0f, W, H);
                    RaidTab(_pRaidTheirs, Ctx.Vr);
                });
                _pRaidOurs.gameObject.SetActive(false);
                WowLog.Info("UI: Social window - the Raid tab is interact's raid page (UiSocial.RaidTab).");
            }
            catch (Exception e)
            {
                WowLog.Error("UI: Social window - the Raid tab's page failed to build, ours stays: " + e);
                if (_pRaidTheirs != null) _pRaidTheirs.gameObject.SetActive(false);
                _pRaidOurs.gameObject.SetActive(true);
            }
        }

        // ================================================================== refresh

        public override void Refresh(float now)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null) return;
            SocialState st = soc.State;
            if (_seenTabRequest != _tabRequestVersion)
            {
                _seenTabRequest = _tabRequestVersion;
                SetTab(_tabRequest);   // a change asks the server what the tab needs (OnTabShown)
            }
            bool inGuild = st.Guild != null;
            _tabs[TabGuild].Enabled = inGuild;
            if (_tab == TabGuild && !inGuild) SetTab(TabFriends);
            UpdateTitle(st);
            switch (_tab)
            {
                case TabFriends:
                    if (_sub == 1) RefreshFriends(soc, st, now);
                    else RefreshIgnore(st);
                    break;
                case TabWho: RefreshWho(st); break;
                case TabGuild: Guild.Refresh(now); break;
                case TabChat: RefreshChannels(soc, st); break;
                case TabRaid:
                    if (_raidDelegated) { Action<float> r = RaidTabRefresh; if (r != null) { try { r(now); } catch (Exception e) { RaidFailed(e); } } }
                    else RefreshRaid();
                    break;
            }
            _drawn = _version;
        }

        private bool _raidFailedLogged;

        private void RaidFailed(Exception e)
        {
            if (_raidFailedLogged) return;
            _raidFailedLogged = true;
            WowLog.Error("UI: Social window - the Raid tab's refresh failed (logged once): " + e);
        }

        private string _titleShown;

        private void UpdateTitle(SocialState st)
        {
            string t;
            switch (_tab)
            {
                case TabWho: t = SocialUi.G("WHO_LIST", "Who List"); break;                                    // GlobalStrings.lua:8937
                case TabGuild:
                    t = st.Guild != null ? SocialUi.F("GUILD_TITLE_TEMPLATE", "%s of %s", st.Guild.RankName(st.Guild.MyRank), st.Guild.Name)   // :3986
                                         : SocialUi.G("GUILD", "Guild");
                    break;
                case TabChat: t = SocialUi.G("CHAT_CHANNELS", "Chat Channels"); break;                         // :1409
                case TabRaid: t = SocialUi.G("RAID", "Raid"); break;                                           // :5940
                default: t = _sub == 2 ? SocialUi.G("IGNORE_LIST", "Ignore List") : SocialUi.G("FRIENDS_LIST", "Friends List"); break;   // :4224 / :3739
            }
            if (t == _titleShown) return;
            _titleShown = t;
            _title.richText = false;
            _title.text = t;
        }

        // ================================================================== Friends

        private void ScrollFriends(int by) { _friendsTop = Mathf.Clamp(_friendsTop + by, 0, Mathf.Max(0, _friends.Count - FriendsToDisplay)); _version++; }

        private void RefreshFriends(IWowSocial soc, SocialState st, float now)
        {
            if (now - _friendsAsked > FriendsRefreshSeconds) { _friendsAsked = now; soc.RefreshFriends(); }
            int wheel = SocialUi.Wheel(_friendsList);
            if (wheel != 0) ScrollFriends(-wheel);
            if (_friendsSeen == st.FriendsCounter && _drawn == _version) return;
            _friendsSeen = st.FriendsCounter;
            // online first (by name), then offline (by name) - FriendsList_Update's order
            _friends.Clear();
            _friends.AddRange(st.Friends);
            _friends.Sort((a, b) =>
            {
                if (a.Online != b.Online) return a.Online ? -1 : 1;
                return string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
            });
            _friendsTop = Mathf.Clamp(_friendsTop, 0, Mathf.Max(0, _friends.Count - FriendsToDisplay));
            bool selOnline = false;
            for (int i = 0; i < FriendsToDisplay; i++)
            {
                SocialUi.Row r = _friendRows[i];
                int idx = _friendsTop + i;
                if (idx >= _friends.Count) { SocialUi.Show(r.B, false); continue; }
                FriendView f = _friends[idx];
                SocialUi.Show(r.B, true);
                bool sel = f.Guid == _selFriend;
                r.Sel.enabled = sel;
                r.Icon.texture = UiArt.Tex(!f.Online ? StatusOffline : f.Afk ? StatusAway : f.Dnd ? StatusDnd : StatusOnline);
                r.Icon.color = Color.white;
                r.Icon.enabled = true;
                Color nameColor = !f.Online ? UiArt.Grey : sel ? Color.white : UiArt.Gold;
                SocialUi.SetText(r.Col[0], SocialUi.Name(f.Name), nameColor);
                string info = f.Online
                    ? SocialUi.F("FRIENDS_LIST_WOW_TEMPLATE", "%1$s, %2$d %3$s", f.ZoneName ?? "", f.Level, SocialUi.ClassName(f.Class))   // GlobalStrings.lua:3753
                    : SocialUi.G("FRIENDS_LIST_OFFLINE", "Offline");                                                                     // :3746
                if (!string.IsNullOrEmpty(f.Note)) info += " " + SocialUi.F("FRIENDS_LIST_NOTE_TEMPLATE", "(%s)", f.Note);              // :3745
                SocialUi.SetText(r.Col[1], info, f.Online ? new Color(0.85f, 0.85f, 0.85f) : UiArt.Grey);
                if (sel && f.Online) selOnline = true;
            }
            _sendMessage.Interactable = selOnline;
        }

        private void FriendClick(int row, bool menu)
        {
            int idx = _friendsTop + row;
            if (idx < 0 || idx >= _friends.Count) return;
            FriendView f = _friends[idx];
            bool again = f.Guid == _selFriend;
            _selFriend = f.Guid;
            _version++;
            // right click (desktop) / long press, or (VR, ours) a press on the already selected row: WoW's FRIEND / FRIEND_OFFLINE menu
            if (menu || (Ctx.Vr && again))
                UiNameMenu.Open(Root, MenuPoint(_friendRows[row].B, row * FriendHeight + 100f), f.Name, f.Guid, f.Online, true, false);
        }

        /// <summary>Where a menu opened from a row hangs: desktop = the mouse, VR = right of the row.</summary>
        private Vector2 MenuPoint(UiButton row, float y)
        {
            if (!Ctx.Vr)
            {
                Vector2 p;
                if (UiNameMenu.MouseIn(Root, out p)) return p;
            }
            return new Vector2(150f, y + 4f);
        }

        /// <summary>FriendsFrameAddFriendButton (FriendsFrame.lua:1301-1313): a friendly player target not on the list is added at once (+ the
        /// sound UChatScrollButton), else the ADD_FRIEND name popup (StaticPopup.lua:1719, maxLetters 12 + 1 + 64 = name space realm).</summary>
        private void AddFriendClick()
        {
            IWowSession s = Ctx.S;
            IWowSocial soc = SocialUi.Soc(s);
            if (soc == null) return;
            EntityState t = s.TargetGuid != 0 ? s.Find(s.TargetGuid) : null;
            if (t != null && t.Kind == EntityKind.Player && t.Reaction == Reaction.Friendly && s.Player != null && t.Guid != s.Player.Guid
                && !string.IsNullOrEmpty(t.Name) && FriendByName(soc.State, t.Name) == null)
            {
                soc.AddFriend(t.Name, "");
                UiSound.Named("UChatScrollButton");
                return;
            }
            SocialUi.Ask("ADD_FRIEND", SocialUi.G("ADD_FRIEND_LABEL", "Enter name of friend to add:"), 12 + 1 + 64, "",   // GlobalStrings.lua:504
                text => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.AddFriend(SocialUi.FirstWord(text), ""); });
        }

        private void SendMessageClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            FriendView f = soc != null ? FriendByGuid(soc.State, _selFriend) : null;
            if (f != null && f.Online && !string.IsNullOrEmpty(f.Name)) UiChat.StartWhisper(f.Name);   // FriendsFrame_SendMessage -> ChatFrame_SendTell
        }

        internal static FriendView FriendByName(SocialState st, string name)
        {
            if (st == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < st.Friends.Count; i++) if (string.Equals(st.Friends[i].Name, name, StringComparison.OrdinalIgnoreCase)) return st.Friends[i];
            return null;
        }

        internal static FriendView FriendByGuid(SocialState st, ulong guid)
        {
            if (st == null || guid == 0) return null;
            for (int i = 0; i < st.Friends.Count; i++) if (st.Friends[i].Guid == guid) return st.Friends[i];
            return null;
        }

        internal static IgnoreView IgnoredByName(SocialState st, string name)
        {
            if (st == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < st.Ignored.Count; i++) if (string.Equals(st.Ignored[i].Name, name, StringComparison.OrdinalIgnoreCase)) return st.Ignored[i];
            return null;
        }

        // ================================================================== Ignore

        private void ScrollIgnore(int by)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            int n = soc != null ? soc.State.Ignored.Count : 0;
            _ignoreTop = Mathf.Clamp(_ignoreTop + by, 0, Mathf.Max(0, n - IgnoresToDisplay));
            _version++;
        }

        private int _ignoreSeen = -1;

        private void RefreshIgnore(SocialState st)
        {
            int wheel = SocialUi.Wheel(_ignoreList);
            if (wheel != 0) ScrollIgnore(-wheel);
            if (_ignoreSeen == st.FriendsCounter && _drawn == _version) return;
            _ignoreSeen = st.FriendsCounter;
            List<IgnoreView> list = st.Ignored;
            _ignoreTop = Mathf.Clamp(_ignoreTop, 0, Mathf.Max(0, list.Count - IgnoresToDisplay));
            bool any = false;
            for (int i = 0; i < IgnoresToDisplay; i++)
            {
                SocialUi.Row r = _ignoreRows[i];
                int idx = _ignoreTop + i;
                if (idx >= list.Count) { SocialUi.Show(r.B, false); continue; }
                IgnoreView v = list[idx];
                SocialUi.Show(r.B, true);
                bool sel = v.Guid == _selIgnore;
                if (sel) any = true;
                r.Sel.enabled = sel;
                SocialUi.SetText(r.Col[0], SocialUi.Name(v.Name), sel ? Color.white : UiArt.Gold);
            }
            _removeIgnore.Interactable = any;
        }

        private void IgnoreClick(int row)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null) return;
            int idx = _ignoreTop + row;
            if (idx < 0 || idx >= soc.State.Ignored.Count) return;
            _selIgnore = soc.State.Ignored[idx].Guid;
            _version++;
        }

        /// <summary>FriendsFrameIgnorePlayerButton: the player target (not yourself) at once, else the ADD_IGNORE popup (StaticPopup.lua:1817).</summary>
        private void IgnorePlayerClick()
        {
            IWowSession s = Ctx.S;
            IWowSocial soc = SocialUi.Soc(s);
            if (soc == null) return;
            EntityState t = s.TargetGuid != 0 ? s.Find(s.TargetGuid) : null;
            if (t != null && t.Kind == EntityKind.Player && s.Player != null && t.Guid != s.Player.Guid && !string.IsNullOrEmpty(t.Name)
                && IgnoredByName(soc.State, t.Name) == null)
            {
                soc.AddIgnore(t.Name);
                return;
            }
            SocialUi.Ask("ADD_IGNORE", SocialUi.G("ADD_IGNORE_LABEL", "Enter name of player to ignore\nor\nShift-Click a name from the chat window:"), 12 + 1 + 64, "",   // :507
                text => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.AddIgnore(SocialUi.FirstWord(text)); });
        }

        private void RemoveIgnoreClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null || _selIgnore == 0) return;
            soc.RemoveIgnore(_selIgnore);
            _selIgnore = 0;
            _version++;
        }

        // ================================================================== Who

        private void ScrollWho(int by) { _whoTop = Mathf.Clamp(_whoTop + by, 0, Mathf.Max(0, _who.Count - WhosToDisplay)); _version++; }

        private void CycleWhoColumn()
        {
            // WHOFRAME_DROPDOWN_LIST (FriendsFrame.lua:70-74): Zone / Guild / Race; ours: a click cycles them (and sorts by it)
            _whoVariable = (_whoVariable + 1) % 3;
            string[] keys = { "ZONE", "GUILD", "RACE" }, fb = { "Zone", "Guild", "Race" };
            _whoColumn.SetLabel(SocialUi.G(keys[_whoVariable], fb[_whoVariable]));
            SortWho(2);
        }

        private void SortWho(int by)
        {
            _whoSort = by;   // WoW's SortWho asks the server again; ours sorts the shown list locally (proposal-social.md 6.5.1)
            _whoSeen = -1;
            _version++;
        }

        private string WhoVariable(WhoView w)
        {
            switch (_whoVariable)
            {
                case 1: return w.GuildName ?? "";
                case 2: return w.RaceName ?? "";
                default: return w.ZoneName ?? "";
            }
        }

        private void RefreshWho(SocialState st)
        {
            int wheel = SocialUi.Wheel(_whoList);
            if (wheel != 0) ScrollWho(-wheel);
            if (_whoSeen == st.WhoCounter && _drawn == _version) return;
            if (_whoSeen != st.WhoCounter) _whoTop = 0;
            _whoSeen = st.WhoCounter;
            _who.Clear();
            _who.AddRange(st.Who);
            switch (_whoSort)
            {
                case 1: _who.Sort((a, b) => string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase)); break;
                case 2: _who.Sort((a, b) => string.Compare(WhoVariable(a), WhoVariable(b), StringComparison.OrdinalIgnoreCase)); break;
                case 3: _who.Sort((a, b) => a.Level.CompareTo(b.Level)); break;
                case 4: _who.Sort((a, b) => string.Compare(a.ClassName ?? "", b.ClassName ?? "", StringComparison.OrdinalIgnoreCase)); break;
            }
            _whoTop = Mathf.Clamp(_whoTop, 0, Mathf.Max(0, _who.Count - WhosToDisplay));
            bool sel = false;
            for (int i = 0; i < WhosToDisplay; i++)
            {
                SocialUi.Row r = _whoRows[i];
                int idx = _whoTop + i;
                if (idx >= _who.Count) { SocialUi.Show(r.B, false); continue; }
                WhoView w = _who[idx];
                SocialUi.Show(r.B, true);
                bool s = !string.IsNullOrEmpty(w.Name) && string.Equals(w.Name, _selWho, StringComparison.OrdinalIgnoreCase);
                if (s) sel = true;
                r.Sel.enabled = s;
                Color c = s ? Color.white : UiArt.Gold;
                SocialUi.SetText(r.Col[0], SocialUi.Name(w.Name), c);
                SocialUi.SetText(r.Col[1], WhoVariable(w), Color.white);
                SocialUi.SetText(r.Col[2], w.Level.ToString(), Color.white);
                SocialUi.SetText(r.Col[3], !string.IsNullOrEmpty(w.ClassName) ? w.ClassName : SocialUi.ClassName(w.Class), Color.white);
            }
            // WhoFrameTotals (FriendsFrame.lua:784-787): WHO_FRAME_TOTAL_TEMPLATE + WHO_FRAME_SHOWN_TEMPLATE when the server matched more
            string tot = SocialUi.F("WHO_FRAME_TOTAL_TEMPLATE", "%d |4Person:People; Found", st.WhoTotal);   // GlobalStrings.lua:8936
            if (st.WhoTotal > st.Who.Count) tot += " " + SocialUi.F("WHO_FRAME_SHOWN_TEMPLATE", "(%d displayed)", st.Who.Count);   // :8935
            SocialUi.SetText(_whoTotals, st.WhoCounter > 0 ? tot : "", UiArt.Gold);
            _whoAddFriend.Interactable = sel;
            _whoInvite.Interactable = sel;
        }

        private void WhoClick(int row, bool menu)
        {
            int idx = _whoTop + row;
            if (idx < 0 || idx >= _who.Count) return;
            WhoView w = _who[idx];
            bool again = string.Equals(w.Name, _selWho, StringComparison.OrdinalIgnoreCase);
            _selWho = w.Name;
            _version++;
            if (menu || (Ctx.Vr && again))
                UiNameMenu.Open(Root, MenuPoint(_whoRows[row].B, 96f + row * WhoHeight), w.Name, 0UL, true, false, false);
        }

        private void WhoRefreshClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null) return;
            _whoAsked = true;
            soc.Who(SocialUi.Text(_whoField));
            SocialUi.StopTyping(_whoField);
        }

        private void WhoAddFriendClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc != null && !string.IsNullOrEmpty(_selWho)) soc.AddFriend(_selWho, "");
        }

        private void WhoInviteClick()
        {
            if (Ctx.S != null && !string.IsNullOrEmpty(_selWho)) Ctx.S.InviteToGroup(_selWho);
        }

        // ================================================================== Chat channels

        private void ChannelClick(int row, bool menu)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null || row >= soc.State.Channels.Count) return;
            ChannelView c = soc.State.Channels[row];
            bool again = c.Number == _selChannel;
            if (!again || !menu)
            {
                _selChannel = c.Number;
                _memberTop = 0;
                soc.ListChannel(c.Name);   // CMSG_CHANNEL_LIST -> the roster (ChannelFrame.lua ChannelList_UpdateHighlight / ChannelRoster)
                _chanSeen = -1;
                _version++;
            }
            if (menu || (Ctx.Vr && again))
            {
                // the channel dropdown (ChannelFrame.lua:486-489): CHAT_LEAVE "Leave" (GlobalStrings.lua:1469) -> LeaveChannelByName
                string name = c.Name;
                Vector2 p = new Vector2(120f, 74f + row * 20f + 4f);
                if (!Ctx.Vr) UiNameMenu.MouseIn(Root, out p);
                SocialMenu.Open(Root, p, c.Display, new[] { SocialUi.G("CHAT_LEAVE", "Leave"), SocialUi.G("CANCEL", "Cancel") },
                    new Action[] { () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.LeaveChannel(name); }, null });
            }
        }

        private void MemberClick(int row)
        {
            ChannelView c = SelectedChannel();
            int idx = _memberTop + row;
            if (c == null || idx < 0 || idx >= c.Members.Count) return;
            string name = c.Members[idx];
            Vector2 p = new Vector2(200f, 74f + row * ChannelRosterHeight + 4f);
            if (!Ctx.Vr) UiNameMenu.MouseIn(Root, out p);
            UiNameMenu.Open(Root, p, name, 0UL, true, false, false);
        }

        private void ScrollMembers(int by)
        {
            ChannelView c = SelectedChannel();
            int n = c != null ? c.Members.Count : 0;
            _memberTop = Mathf.Clamp(_memberTop + by, 0, Mathf.Max(0, n - ChannelMembers));
            _version++;
        }

        private ChannelView SelectedChannel()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null) return null;
            List<ChannelView> list = soc.State.Channels;
            for (int i = 0; i < list.Count; i++) if (list[i].Number == _selChannel) return list[i];
            return null;
        }

        private int _membersSeen = -1;

        private void RefreshChannels(IWowSocial soc, SocialState st)
        {
            int wheel = SocialUi.Wheel(_memberList);
            if (wheel != 0) ScrollMembers(-wheel);
            ChannelView sel = SelectedChannel();
            int msig = sel != null ? sel.MembersCounter : -1;
            if (_chanSeen == st.ChannelsCounter && _membersSeen == msig && _drawn == _version) return;
            _chanSeen = st.ChannelsCounter;
            _membersSeen = msig;
            List<ChannelView> list = st.Channels;
            if (sel == null && list.Count > 0 && _selChannel != 0) _selChannel = 0;
            for (int i = 0; i < ChannelButtons; i++)
            {
                if (i >= list.Count) { SocialUi.Show(_chanRows[i], false); continue; }
                SocialUi.Show(_chanRows[i], true);
                bool s = list[i].Number == _selChannel;
                _chanSel[i].enabled = s;
                SocialUi.SetText(_chanText[i], list[i].Display, s ? Color.white : UiArt.Gold);
            }
            SocialUi.SetText(_membersTitle, sel != null ? sel.Display : "", UiArt.Gold);
            int n = sel != null ? sel.Members.Count : 0;
            _memberTop = Mathf.Clamp(_memberTop, 0, Mathf.Max(0, n - ChannelMembers));
            for (int i = 0; i < ChannelMembers; i++)
            {
                SocialUi.Row r = _memberRows[i];
                int idx = _memberTop + i;
                if (sel == null || idx >= n) { SocialUi.Show(r.B, false); continue; }
                SocialUi.Show(r.B, true);
                r.Sel.enabled = false;
                SocialUi.SetText(r.Col[0], SocialUi.Name(sel.Members[idx]), Color.white);
            }
        }

        private void ShowNewChannel(bool on)
        {
            if (_newChannel == null) return;
            if (!on) { SocialUi.StopTyping(_newName); SocialUi.StopTyping(_newPassword); }
            else { _newName.text = ""; _newPassword.text = ""; }
            SocialUi.Show(_newChannel, on);
            if (on) SocialUi.Type(_newName, SocialUi.G("CHANNEL_CHANNEL_NAME", "Channel Name"), NewChannelOkay);
        }

        /// <summary>ChannelFrameDaughterFrameOkayButton -> JoinPermanentChannel(name, password) (ChannelFrame.lua:119-128).</summary>
        private void NewChannelOkay()
        {
            string name = SocialUi.Text(_newName), pw = SocialUi.Text(_newPassword);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null || name.Length == 0) return;
            soc.JoinChannel(name, pw);
            ShowNewChannel(false);
        }

        // ================================================================== Raid (ours, until interact's page)

        private void ScrollRaid(int by)
        {
            int n = Ctx.P != null ? Ctx.P.Group.Count : 0;
            _raidTop = Mathf.Clamp(_raidTop + by, 0, Mathf.Max(0, n - RaidRows));
            _raidSig = int.MinValue;
        }

        private void RefreshRaid()
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            int sig = p.Group.Count * 397 ^ (int)p.GroupLeaderGuid ^ _raidTop * 31;
            for (int i = 0; i < p.Group.Count; i++) sig = sig * 31 + (p.Group[i].Online ? 1 : 0) + p.Group[i].Level * 3;
            if (sig == _raidSig && _drawn == _version) return;
            _raidSig = sig;
            for (int i = 0; i < RaidRows; i++)
            {
                SocialUi.Row r = _raidRows[i];
                int idx = _raidTop + i;
                if (idx >= p.Group.Count) { SocialUi.Show(r.B, false); continue; }
                GroupMemberView m = p.Group[idx];
                SocialUi.Show(r.B, true);
                r.Sel.enabled = false;
                bool leader = m.Guid != 0 && m.Guid == p.GroupLeaderGuid;
                r.Icon.texture = leader ? UiArt.Tex(LeaderIcon) : null;
                r.Icon.color = leader ? Color.white : new Color(0f, 0f, 0f, 0f);
                SocialUi.SetText(r.Col[0], SocialUi.Name(m.Name), m.Online ? UiArt.Gold : UiArt.Grey);
                SocialUi.SetText(r.Col[1], SocialUi.F("FRIENDS_LEVEL_TEMPLATE", "Level %d %s", m.Level, SocialUi.ClassName(m.Class)), m.Online ? Color.white : UiArt.Grey);   // :3741
            }
            _leaveParty.Interactable = p.Group.Count > 0;
        }

        private void RaidMemberClick(int row)
        {
            PlayerState p = Ctx.P;
            int idx = _raidTop + row;
            if (p == null || idx < 0 || idx >= p.Group.Count) return;
            GroupMemberView m = p.Group[idx];
            Vector2 pt = new Vector2(160f, 94f + row * 20f + 4f);
            if (!Ctx.Vr) UiNameMenu.MouseIn(Root, out pt);
            UiNameMenu.Open(Root, pt, m.Name, m.Guid, m.Online, false, false);
        }

        /// <summary>RaidFrameAddMemberButton: StaticPopup ADD_RAIDMEMBER (StaticPopup.lua:1935-1950, ADD_RAIDMEMBER_LABEL, 12 letters) -> InviteUnit.</summary>
        private void RaidInviteClick()
        {
            SocialUi.Ask("ADD_RAIDMEMBER", SocialUi.G("ADD_RAIDMEMBER_LABEL", "Add Raid Member:"), 12, "",
                text => { IWowSession s = WowPlay.Session; if (s != null) s.InviteToGroup(SocialUi.FirstWord(text)); });
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// A small dropdown menu (UIDropDownMenu look of UiGroup's unit menu: DialogBox backdrop 32 / 11 / 32, rows 16 high, the title in
    /// gold, QuestTitleHighlight under the pointer) hung from a point of a host rect - so the laser presses it on the same surface. One
    /// at a time; closed by a pick, Cancel, a click elsewhere (desktop), Escape (desktop), the host hiding, or 10 s without pointing (ours).
    /// </summary>
    internal static class SocialMenu
    {
        const float MenuW = 190f, RowH = 16f, Pad = 14f, Timeout = 10f;   // ours: UiGroup's menu metrics (UIDROPDOWNMENU_BUTTON_HEIGHT 16), 10 s
        const int MaxRows = 10;

        sealed class Menu
        {
            public RectTransform Root, Host;
            public UiBackdrop Back;
            public TextMeshProUGUI Title;
            public readonly UiButton[] Rows = new UiButton[MaxRows];
            public readonly Action[] Acts = new Action[MaxRows];
            public bool Vr;
        }

        static readonly Dictionary<RectTransform, Menu> Menus = new Dictionary<RectTransform, Menu>();
        static Menu _open;
        static float _openedAt, _lastHover;

        public static bool IsOpen { get { return _open != null && _open.Root != null && _open.Root.gameObject.activeInHierarchy; } }

        /// <summary>Opens rows (label + action; a null action = Cancel) at localTopLeft (x right, y down) of host.</summary>
        public static void Open(RectTransform host, Vector2 localTopLeft, string title, string[] labels, Action[] acts)
        {
            if (host == null || labels == null || labels.Length == 0) return;
            Close("replaced");
            Menu m = Get(host);
            if (m == null) return;
            int n = Mathf.Min(labels.Length, MaxRows);
            m.Title.text = title ?? "";
            for (int i = 0; i < MaxRows; i++)
            {
                bool on = i < n;
                SocialUi.Show(m.Rows[i], on);
                if (!on) continue;
                m.Rows[i].SetLabel(labels[i]);
                m.Rows[i].Interactable = true;
                m.Acts[i] = acts != null && i < acts.Length ? acts[i] : null;
            }
            float h = Pad * 2f + RowH * (n + 1);
            UiKit.Size(m.Root, MenuW, h);
            m.Back.Place(0f, 0f, MenuW, h);
            Vector2 size = host.rect.size;
            float x = Mathf.Clamp(localTopLeft.x, 0f, Mathf.Max(0f, size.x - MenuW));
            float y = Mathf.Clamp(localTopLeft.y, 0f, Mathf.Max(0f, size.y - h));
            UiKit.Move(m.Root, x, y);
            m.Root.SetAsLastSibling();
            m.Root.gameObject.SetActive(true);
            _open = m;
            _openedAt = _lastHover = Time.unscaledTime;
        }

        /// <summary>Greys a row (an action the server would refuse) - after Open.</summary>
        public static void Disable(int row)
        {
            if (_open != null && row >= 0 && row < MaxRows && _open.Rows[row] != null) _open.Rows[row].Interactable = false;
        }

        public static bool Close(string why)
        {
            if (_open == null) return false;
            Menu m = _open;
            _open = null;
            if (m.Root != null) m.Root.gameObject.SetActive(false);
            return true;
        }

        public static void CloseIn(RectTransform host)
        {
            if (_open != null && _open.Host == host) Close("the window closed");
        }

        /// <summary>Per frame (UiSocialHost): timeout, the host hiding, desktop click elsewhere / Escape.</summary>
        public static void Tick()
        {
            if (_open == null) return;
            Menu m = _open;
            if (m.Root == null || m.Host == null || !m.Host.gameObject.activeInHierarchy) { Close("the host hid"); return; }
            float now = Time.unscaledTime;
            bool hovered = false;
            for (int i = 0; i < MaxRows; i++) if (m.Rows[i] != null && m.Rows[i].gameObject.activeSelf && m.Rows[i].Hovered) { hovered = true; break; }
            if (hovered) _lastHover = now;
            if (now - _lastHover > Timeout) { Close("timed out"); return; }
            if (m.Vr || now - _openedAt < 0.25f) return;
            bool down = false, esc = false;
            try { down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1); esc = Input.GetKeyDown(KeyCode.Escape); }
            catch (Exception) { }
            if (esc || (down && !hovered)) Close(esc ? "escape" : "clicked elsewhere");
        }

        private static void Pick(Menu m, int row)
        {
            Action a = row >= 0 && row < MaxRows ? m.Acts[row] : null;
            Close("picked");
            if (a == null) return;
            try { a(); }
            catch (Exception e) { WowLog.Error("UI: social menu row failed: " + e); }
        }

        private static Menu Get(RectTransform host)
        {
            Menu m;
            if (Menus.TryGetValue(host, out m) && m.Root != null) return m;
            // the host's surface (its buttons know it): VR presses go through UiSurface's button list, the desktop through raycasts
            UiButton any = host.GetComponentInChildren<UiButton>(true);
            UiSurface surf = any != null ? any.Surface : null;
            var scope = new SocialUi.Scope { Surface = surf, Pointable = surf == null || !surf.IsWorld, Over = surf != null && surf.IsArm };
            m = new Menu { Host = host, Vr = surf != null && surf.IsWorld };
            Menu mm = m;
            scope.Run(() =>
            {
                mm.Root = UiKit.Rect(host, "Social menu", 0f, 0f, MenuW, 60f);
                var canvas = mm.Root.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 30;   // ours: over the window's own sub-canvases (chat edit box 6)
                if (!mm.Vr) mm.Root.gameObject.AddComponent<GraphicRaycaster>();
                mm.Back = UiBackdrop.Make(mm.Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
                mm.Back.Place(0f, 0f, MenuW, 60f);
                mm.Title = UiKit.Text(mm.Root, "Title", "", 11f, UiArt.Gold, TextAlignmentOptions.Left, Pad + 2f, Pad, MenuW - Pad * 2f, RowH);
                mm.Title.textWrappingMode = TextWrappingModes.NoWrap;
                mm.Title.overflowMode = TextOverflowModes.Ellipsis;
                mm.Title.richText = false;
                for (int i = 0; i < MaxRows; i++)
                {
                    int k = i;
                    float w = MenuW - Pad * 2f;
                    UiButton b = UiKit.Button(mm.Root, "Row " + (i + 1), Pad, Pad + RowH * (i + 1), w, RowH);
                    b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, w, RowH);
                    b.Hilite.enabled = false;
                    b.Label = UiKit.Text(b.transform, "Label", "", 11f, Color.white, TextAlignmentOptions.Left, 4f, 0f, w - 4f, RowH);
                    b.Label.textWrappingMode = TextWrappingModes.NoWrap;
                    b.LabelColor = Color.white;
                    b.Click = () => Pick(mm, k);
                    b.gameObject.SetActive(false);
                    mm.Rows[i] = b;
                }
                mm.Root.gameObject.SetActive(false);
            });
            Menus[host] = m;
            return m;
        }
    }

    /// <summary>
    /// WoW's FRIEND / FRIEND_OFFLINE unit menu on a player name (UnitPopup.lua:144-145, shown rules :544-613, clicks :1196-1253) for the
    /// chat, the friends / who / guild / channel lists. Rows: Whisper, Invite, Target (a visible player of that name), Set Note (friends
    /// list), Ignore (toggles; not on yourself), Promote to Guildmaster (guild tab, we are the guild master, a member, not us), Leave Guild
    /// (guild tab, ourselves), Remove Friend (friends list), Cancel. Not built (ours): POP_OUT_CHAT, REPORT_SPAM, PVP_REPORT_AFK.
    /// </summary>
    public static class UiNameMenu
    {
        public static void Open(RectTransform host, Vector2 localTopLeft, string name, ulong guid, bool online, bool friendsList, bool guildTab)
        {
            IWowSession s = WowPlay.Session;
            IWowSocial soc = SocialUi.Soc(s);
            if (host == null || string.IsNullOrEmpty(name) || s == null || soc == null) return;
            SocialState st = soc.State;
            bool self = s.Player != null && string.Equals(s.Player.Name, name, StringComparison.OrdinalIgnoreCase);
            FriendView friend = guid != 0 ? UiSocial.FriendByGuid(st, guid) : null;
            if (friend == null) friend = UiSocial.FriendByName(st, name);
            IgnoreView ignored = UiSocial.IgnoredByName(st, name);
            GuildView g = st.Guild;
            var labels = new List<string>(10);
            var acts = new List<Action>(10);
            string n = name;
            if (online)
            {
                if (!self)
                {
                    labels.Add(SocialUi.G("WHISPER", "Whisper")); acts.Add(() => UiChat.StartWhisper(n));                         // GlobalStrings.lua:8932
                    labels.Add(SocialUi.G("INVITE", "Invite")); acts.Add(() => { IWowSession x = WowPlay.Session; if (x != null) x.InviteToGroup(n); });   // :4297
                    ulong unit = VisiblePlayer(s, n);
                    if (unit != 0) { labels.Add(SocialUi.G("TARGET", "Target")); acts.Add(() => { IWowSession x = WowPlay.Session; if (x != null) x.SetTarget(unit); }); }   // :7469
                }
            }
            if (friendsList && friend != null)
            {
                ulong fg = friend.Guid;
                string note = friend.Note ?? "";
                labels.Add(SocialUi.G("SET_NOTE", "Set Note"));                                                                      // :6231
                acts.Add(() => SocialUi.Ask("SET_FRIENDNOTE", SocialUi.F("SET_FRIENDNOTE_LABEL", "Set Notes for %s:", n), 48, note,  // StaticPopup.lua:1749, maxLetters 48
                    text => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.SetFriendNote(fg, text); }, true));
            }
            if (!self)
            {
                labels.Add(SocialUi.G("IGNORE", "Ignore"));                                                                           // :4220 (AddOrDelIgnore toggles)
                if (ignored != null) { ulong ig = ignored.Guid; acts.Add(() => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.RemoveIgnore(ig); }); }
                else acts.Add(() => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.AddIgnore(n); });
            }
            if (online && guildTab && g != null && g.MyRank == 0 && !self && GuildMember(g, n))
            {
                labels.Add(SocialUi.G("GUILD_PROMOTE", "Promote to Guildmaster"));                                                    // :3976
                acts.Add(() => SocialUi.Confirm("CONFIRM_GUILD_PROMOTE", SocialUi.F("CONFIRM_GUILD_PROMOTE", "Really promote %s to Guildmaster?", n),   // StaticPopup.lua:638
                    SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"), () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildSetLeader(n); }));
            }
            if (online && guildTab && g != null && self)
            {
                string gn = g.Name;
                labels.Add(SocialUi.G("GUILD_LEAVE", "Leave Guild"));                                                                // :3956
                acts.Add(() => SocialUi.Confirm("CONFIRM_GUILD_LEAVE", SocialUi.F("CONFIRM_GUILD_LEAVE", "Really leave %s?", gn),     // StaticPopup.lua:626
                    SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"), () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildLeave(); }));
            }
            if (friendsList && friend != null)
            {
                ulong fg = friend.Guid;
                labels.Add(SocialUi.G("REMOVE_FRIEND", "Remove Friend"));                                                            // :6046
                acts.Add(() => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.RemoveFriend(fg); });
            }
            labels.Add(SocialUi.G("CANCEL", "Cancel"));
            acts.Add(null);
            SocialMenu.Open(host, localTopLeft, name, labels.ToArray(), acts.ToArray());
        }

        public static bool IsOpen { get { return SocialMenu.IsOpen; } }

        /// <summary>Closes the menu (Escape chains may call it); true = one was open.</summary>
        public static bool Close() { return SocialMenu.Close("closed"); }

        internal static void CloseIn(RectTransform host) { SocialMenu.CloseIn(host); }

        /// <summary>Desktop: the mouse as a point of host (x right, y down from its top-left), for menus that hang from the mouse.</summary>
        public static bool MouseIn(RectTransform host, out Vector2 local)
        {
            local = Vector2.zero;
            if (host == null) return false;
            try
            {
                Canvas c = host.GetComponentInParent<Canvas>();
                Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
                Vector2 p;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, Input.mousePosition, cam, out p)) return false;
                Rect r = host.rect;
                local = new Vector2(p.x - r.xMin, r.yMax - p.y);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static ulong VisiblePlayer(IWowSession s, string name)
        {
            IReadOnlyList<EntityState> list = s.Entities;
            if (list == null) return 0;
            for (int i = 0; i < list.Count; i++)
            {
                EntityState e = list[i];
                if (e != null && e.Kind == EntityKind.Player && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e.Guid;
            }
            return 0;
        }

        private static bool GuildMember(GuildView g, string name)
        {
            for (int i = 0; i < g.Members.Count; i++) if (string.Equals(g.Members[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
