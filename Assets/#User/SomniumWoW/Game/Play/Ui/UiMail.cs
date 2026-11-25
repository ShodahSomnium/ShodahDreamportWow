// iteration 11 - owner socialui (proposal-social.md 6.5.5; wave-0 stub by w0game, filled by socialui)
// WoW's MailFrame (Inbox / Send Mail) + OpenMailFrame at a mailbox: opened by the session (Social.Mailbox, a mailbox gameobject used),
// registered in UiRegistry.Wave0 as Mail 37. Desktop: the opened mail stands right of the inbox like WoW's (-10, 0); VR (ours): it takes
// the inbox's place on the same 384-wide surface and its Close returns to the inbox. The backpack opens with the window (MailFrame.lua:47).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiMail : UiWindow
    {
        // ---- WoW's (MailFrame.lua:1-13, MailFrame.xml)
        const int InboxItemsToDisplay = 7;          // INBOXITEMS_TO_DISPLAY
        const int AttachmentsMaxSend = 12, AttachmentsPerRowSend = 7;              // ATTACHMENTS_MAX_SEND / _PER_ROW_SEND
        const int AttachmentsMaxReceive = 16, AttachmentsPerRowReceive = 7;        // ATTACHMENTS_MAX_RECEIVE / _PER_ROW_RECEIVE (3 rows)
        const int MaxCodGold = 10000;               // MAX_COD_AMOUNT (MailFrame.lua:13), gold
        const int MailBodyItem = 8383;              // MAIL_BODY_ITEM_TEMPLATE (AzerothCore Mail.h): the letter CMSG_MAIL_CREATE_TEXT_ITEM makes
        const string MailIcon = @"Interface\MailFrame\Mail-Icon.blp";
        const string Stationery = @"Interface\Stationery\";   // STATIONERY_PATH (MailFrame.lua:12)

        static readonly string[] InboxArt = { @"Interface\ItemTextFrame\UI-ItemText-TopLeft.blp", @"Interface\Spellbook\UI-SpellbookPanel-TopRight.blp",
            @"Interface\ItemTextFrame\UI-ItemText-BotLeft.blp", @"Interface\Spellbook\UI-SpellbookPanel-BotRight.blp" };            // MailFrame.lua:109-112
        static readonly string[] SendArt = { @"Interface\ClassTrainerFrame\UI-ClassTrainer-TopLeft.blp", @"Interface\ClassTrainerFrame\UI-ClassTrainer-TopRight.blp",
            @"Interface\ClassTrainerFrame\UI-ClassTrainer-BotLeft.blp", @"Interface\ClassTrainerFrame\UI-ClassTrainer-BotRight.blp" }; // :119-122

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Mail, Frame = "MailFrame", TitleKey = "INBOX", Title = "Inbox",                      // GlobalStrings.lua:4230
                Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,                             // UIParent.lua:41
                Wanted = s => { IWowSocial x = SocialUi.Soc(s); return x != null && x.Mailbox != null; },
                NearGuid = s => { IWowSocial x = SocialUi.Soc(s); return x != null && x.Mailbox != null ? x.Mailbox.MailboxGuid : 0UL; },   // VR: next to the mailbox
                Closed = s => { IWowSocial x = SocialUi.Soc(s); if (x != null && x.Mailbox != null) x.CloseMailbox(); },
                OpenSound = "igSpellBookOpen", CloseSound = "igCharacterInfoClose",                                    // MailFrame.lua:130, MailFrame.xml:1298
                Owner = "socialui", Hidden = false,
                Build = (parent, ctx, close) => new UiMail(parent, ctx, close)
            };
        }

        static readonly List<UiMail> Built = new List<UiMail>();
        static bool _loggedBuilt;

        /// <summary>An item entry's inventory icon path from the install (Item.dbc DisplayInfoID -> ItemDisplayInfo.dbc InventoryIcon), cached;
        /// null when unknown. Shared with npcui's merchant (item costs).</summary>
        public static string ItemIcon(int entry) { return MailArt.ItemIcon(entry); }

        /// <summary>npcui's container right-click asks first (proposal-social.md H4): an open Send Mail tab takes the bag item into the draft
        /// (WoW's SetSendMailShowing, MailFrame.lua:125). True = handled (the session answers an Error when it cannot be attached).</summary>
        public static bool TryAttachFromBag(int bag, int slot)
        {
            for (int i = 0; i < Built.Count; i++)
            {
                UiMail m = Built[i];
                if (m == null || !m.IsOpen || m._tab != 2) continue;
                IWowSocial soc = SocialUi.Soc(m.Ctx.S);
                if (soc == null || soc.Mailbox == null) return false;
                soc.AttachMailItem(bag, slot);
                return true;
            }
            return false;
        }

        private readonly RawImage[] _corner = new RawImage[4];
        private readonly TextMeshProUGUI _title;
        private readonly SocialUi.Tab _tabInbox, _tabSend;
        private int _tab = 1;
        // inbox
        private readonly RectTransform _inbox;
        private readonly UiButton[] _rows = new UiButton[InboxItemsToDisplay];
        private readonly RawImage[] _rowIcon = new RawImage[InboxItemsToDisplay], _rowSel = new RawImage[InboxItemsToDisplay];
        private readonly TextMeshProUGUI[] _rowSender = new TextMeshProUGUI[InboxItemsToDisplay], _rowSubject = new TextMeshProUGUI[InboxItemsToDisplay],
            _rowExpire = new TextMeshProUGUI[InboxItemsToDisplay], _rowCod = new TextMeshProUGUI[InboxItemsToDisplay];
        private readonly string[] _rowTip = new string[InboxItemsToDisplay];
        private readonly UiButton _prev, _next;
        private readonly TextMeshProUGUI _page, _tooMuch;
        private int _pageIndex, _inboxSeen = -1;
        // open mail
        private readonly RectTransform _open;
        private readonly TextMeshProUGUI _oSender, _oSubject, _oBody, _oAttachLabel;
        private readonly RawImage _oStationeryL, _oStationeryR;
        private readonly UiButton[] _oItems = new UiButton[AttachmentsMaxReceive];
        private readonly UiButton _oMoney, _oLetter, _oReply, _oDelete;
        private uint _openId;
        private int _openSeen = -1;
        // send mail
        private readonly RectTransform _send;
        private readonly TMP_InputField _to, _subject, _body, _gold, _silver, _copper;
        private readonly UiButton[] _slots = new UiButton[AttachmentsMaxSend];
        private readonly RectTransform _postageOk, _postageRed;
        private readonly UiMoney _postageWhite, _postageRedMoney;
        private readonly UiButton _radioMoney, _radioCod, _sendButton;
        private readonly TextMeshProUGUI _moneyLabel, _codError;
        private bool _cod;
        private string _autoSubject = "";
        private bool _sending;
        private int _sendSeen = -1;

        public UiMail(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "MailFrame", 0f, 0f, W, H);
            float[] cx = { 0f, 256f, 0f, 256f }, cy = { 0f, 0f, 256f, 256f }, cw = { 256f, 128f, 256f, 128f };
            for (int i = 0; i < 4; i++) _corner[i] = UiKit.Pic(Root, "Corner " + i, UiArt.Tex(InboxArt[i]), cx[i], cy[i], cw[i], 256f);
            Portrait(UiArt.Tex(MailIcon));                                                      // MailFrame.xml:389 Mail-Icon
            _title = Title(SocialUi.G("INBOX", "Inbox"), 17f);                                  // InboxTitleText CENTER (6, 232)
            _title.richText = false;
            UiKit.CloseButton(Root, 323f, 9f, close);                                           // InboxCloseButton CENTER at TOPRIGHT (-45, -25)

            // ---- the inbox (MailItemTemplate 305 x 45 from TOPLEFT (28, -80))
            _inbox = UiKit.Rect(Root, "InboxFrame", 0f, 0f, W, H);
            _tooMuch = UiKit.Text(_inbox, "InboxTooMuchMail", SocialUi.G("INBOX_TOO_MUCH_MAIL", "Your inbox is full."), 10f, UiArt.ErrorRed,   // GlobalStrings
                TextAlignmentOptions.Center, 70f, 50f, 260f, 14f);
            _tooMuch.enabled = false;
            Texture2D border = UiArt.Tex(@"Interface\MailFrame\MailItemBorder.blp");
            for (int i = 0; i < InboxItemsToDisplay; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(_inbox, "MailItem" + (i + 1), 28f, 80f + i * 45f, 305f, 45f);
                UiKit.Pic(b.transform, "BorderLeft", border, 0f, 0.1640625f, 0f, 0.75f, 0f, 0f, 42f, 48f);
                UiKit.Pic(b.transform, "BorderRight", border, 0.1640625f, 1f, 0f, 0.75f, 42f, 0f, 263f, 48f);
                _rowSel[i] = UiKit.Pic(b.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 4f, 3f, 37f, 37f);
                _rowSel[i].enabled = false;
                _rowIcon[i] = UiKit.Pic(b.transform, "Icon", null, 4f, 3f, 37f, 37f);
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 4f, 3f, 37f, 37f);
                b.Hilite.enabled = false;
                _rowCod[i] = UiKit.Text(b.transform, "ButtonCOD", SocialUi.G("COD", "C.O.D."), 9f, UiArt.ErrorRed, TextAlignmentOptions.Bottom, 4f, 3f, 37f, 36f);   // :1653
                _rowCod[i].enabled = false;
                _rowSender[i] = UiKit.Text(b.transform, "Sender", "", 12f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 47f, 4f, 150f, 16f);   // (47, -4) 200 x 16
                _rowSubject[i] = UiKit.Text(b.transform, "Subject", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 47f, 20f, 248f, 18f);   // under the sender 248 x 18
                _rowExpire[i] = UiKit.Text(b.transform, "ExpireTime", "", 10f, Color.green, TextAlignmentOptions.MidlineRight, 201f, 4f, 100f, 16f);   // TOPRIGHT (-4, -4)
                foreach (TextMeshProUGUI t in new[] { _rowSender[i], _rowSubject[i], _rowExpire[i] })
                {
                    t.richText = false;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                }
                b.Click = () => RowClick(k);
                b.Tip = () => _rowTip[k];
                b.gameObject.SetActive(false);
                _rows[i] = b;
            }
            // InboxPrevPageButton / NextPageButton CENTER at BOTTOMLEFT (50, 104) / (314, 104), InboxCurrentPage BOTTOM (-14, 96)
            _prev = UiKit.ArtButton(_inbox, "InboxPrevPageButton", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.MouseHilight, 34f, 392f, 32f, () => Page(-1));
            _next = UiKit.ArtButton(_inbox, "InboxNextPageButton", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.MouseHilight, 298f, 392f, 32f, () => Page(1));
            _page = UiKit.Text(_inbox, "InboxCurrentPage", "", 11f, UiArt.Gold, TextAlignmentOptions.Center, 118f, 400f, 120f, 16f);

            // ---- the opened mail (OpenMailFrame 384 x 512 right of the inbox (-10, 0); VR: in its place)
            _open = UiKit.Rect(Root, "OpenMailFrame", ctx.Vr ? 0f : 374f, 0f, W, H);
            SocialUi.Catch(_open);
            UiKit.Frame4(_open, "Art", @"Interface\ClassTrainerFrame\UI-ClassTrainer-TopLeft.blp", @"Interface\ClassTrainerFrame\UI-ClassTrainer-TopRight.blp",
                @"Interface\MailFrame\UI-OpenMail-BotLeft.blp", @"Interface\ClassTrainerFrame\UI-ClassTrainer-BotRight.blp");                // MailFrame.xml:1507-1531
            UiKit.Pic(_open, "OpenMailFrameIcon", UiArt.Tex(MailIcon), 7f, 6f, 60f, 60f);
            UiKit.CloseButton(_open, 322f, 8f, CloseOpenMail);
            Label(_open, SocialUi.G("FROM", "From:"), 14f, 45f);                                 // OpenMailSenderLabel TOPRIGHT at (114, -45)
            _oSender = UiKit.Text(_open, "OpenMailSender", "", 12f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 118f, 45f, 200f, 16f);
            Label(_open, SocialUi.G("MAIL_SUBJECT_LABEL", "Subject:"), 14f, 65f);                // OpenMailSubjectLabel at (114, -65)
            _oSubject = UiKit.Text(_open, "OpenMailSubject", "", 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 118f, 67f, 200f, 28f);
            // OpenMailScrollFrame 296 x 257 TOPLEFT (21, -97) over the stationery (STATIONERY_PATH .. texture .. "1" / "2", MailFrame.lua:465-466)
            _oStationeryL = UiKit.Pic(_open, "OpenStationeryBackgroundLeft", null, 21f, 97f, 256f, 257f);
            _oStationeryR = UiKit.Pic(_open, "OpenStationeryBackgroundRight", null, 277f, 97f, 64f, 257f);
            _oBody = UiKit.Text(_open, "OpenMailBodyText", "", 11f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 31f, 105f, 276f, 245f, null, false);   // OpenMailBodyText 276 wide
            _oBody.richText = false;
            _oBody.overflowMode = TextOverflowModes.Ellipsis;
            _oAttachLabel = UiKit.Text(_open, "OpenMailAttachmentText", SocialUi.G("TAKE_ATTACHMENTS", "Take Attachments:"), 10f, Color.white, TextAlignmentOptions.MidlineLeft, 26f, 358f, 200f, 14f);   // :7444
            for (int i = 0; i < AttachmentsMaxReceive; i++)
            {
                int k = i;
                UiButton b = UiKit.Slot(_open, "OpenMailAttachmentButton" + (i + 1), 0f, 0f, 37f, false);
                b.Click = () => TakeItem(k);
                b.Tip = () => ItemTip(k);
                b.Count = UiKit.Text(b.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 35f, 35f, UiArt.Numbers);
                b.gameObject.SetActive(false);
                _oItems[i] = b;
            }
            _oMoney = UiKit.Slot(_open, "OpenMailMoneyButton", 26f, 374f, 37f, false);
            _oMoney.Icon.texture = UiArt.Tex(UiArt.CoinIcon);
            _oMoney.Icon.color = Color.white;
            _oMoney.Click = TakeMoney;
            _oMoney.Tip = () => { MailView mm = Find(_openId); return mm != null && mm.Money > 0 ? UiArt.MoneyText(mm.Money) : null; };   // SetTooltipMoney
            _oLetter = UiKit.Slot(_open, "OpenMailLetterButton", 65f, 374f, 37f, false);
            _oLetter.Click = CopyLetter;
            _oLetter.Tip = () => SocialUi.G("MAIL_LETTER_TOOLTIP", "Click to make a permanent\ncopy of this letter.");                 // :4867
            // OpenMailCancelButton 80 x 22 BOTTOMRIGHT (-39, 80), OpenMailDeleteButton 82 x 22 left of it, OpenMailReplyButton 82 x 22 left of that
            UiKit.PanelButton(_open, SocialUi.G("CLOSE", "Close"), 265f, 410f, 80f, 22f, CloseOpenMail);                                // :1645
            _oDelete = UiKit.PanelButton(_open, SocialUi.G("DELETE", "Delete"), 183f, 410f, 82f, 22f, DeleteOrReturn);                  // :1968 / MAIL_RETURN :4871
            _oReply = UiKit.PanelButton(_open, SocialUi.G("REPLY_MESSAGE", "Reply"), 101f, 410f, 82f, 22f, Reply);                     // :6063
            _open.gameObject.SetActive(false);

            // ---- Send Mail (SendMailFrame, MailFrame.xml:653-1240)
            _send = UiKit.Rect(Root, "SendMailFrame", 0f, 0f, W, H);
            Label(_send, SocialUi.G("MAIL_TO_LABEL", "To:"), 4f, 46f, 96f);                      // :4873
            _to = SocialUi.Field(_send, "SendMailNameEditBox", 105f, 46f, 109f, 20f, 12, false, 12f, SocialUi.G("MAIL_TO_LABEL", "To:"), null);   // (105, -46) 109 x 20, letters 12
            Label(_send, SocialUi.G("MAIL_SUBJECT_LABEL", "Subject:"), 4f, 70f, 96f);
            _subject = SocialUi.Field(_send, "SendMailSubjectEditBox", 105f, 70f, 220f, 20f, 64, false, 12f, SocialUi.G("MAIL_SUBJECT_LABEL", "Subject:"), null);   // letters 64
            // SendMailCostMoneyFrame TOPRIGHT (-36, -48) with SEND_MAIL_COST left of it (red above the player's money - SendMailFrame_Update)
            UiKit.Text(_send, "SendMailCostLabel", SocialUi.G("SEND_MAIL_COST", "Postage:"), 10f, UiArt.Gold, TextAlignmentOptions.MidlineRight, 214f, 46f, 80f, 16f);   // :6212
            _postageOk = UiKit.Rect(_send, "Postage", 0f, 0f, W, 80f);
            _postageWhite = new UiMoney(_postageOk, 348f, 47f, Color.white);
            _postageRed = UiKit.Rect(_send, "Postage (too much)", 0f, 0f, W, 80f);
            _postageRedMoney = new UiMoney(_postageRed, 348f, 47f, UiArt.ErrorRed);
            _postageRed.gameObject.SetActive(false);
            // the body: SendMailScrollFrame 296 x 257 at TOPLEFT (21, -97), letters 500; ours: 190 high so both attachment rows fit under it
            _body = SocialUi.Field(_send, "SendMailBodyEditBox", 21f, 97f, 296f, 190f, 500, true, 11f, SocialUi.G("SENDMAIL", "Send Mail"), null);
            Texture2D slotBack = UiArt.Tex(@"Interface\Buttons\UI-Slot-Background.blp");   // SendMailAttachment (MailFrame.xml:284)
            for (int i = 0; i < AttachmentsMaxSend; i++)
            {
                int k = i;
                float x = 30f + (i % AttachmentsPerRowSend) * 41f, y = 292f + (i / AttachmentsPerRowSend) * 41f;   // from BOTTOMLEFT (30, 110), 7 a row (ours: 4 apart)
                UiKit.Pic(_send, "Slot background " + (i + 1), slotBack, 0f, 0.640625f, 0f, 0.640625f, x - 1f, y - 1f, 39f, 39f);
                UiButton b = UiKit.Slot(_send, "SendMailAttachment" + (i + 1), x, y, 37f, false);
                b.Click = () => Detach(k);
                b.Tip = () => DraftTip(k);
                b.Count = UiKit.Text(b.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 35f, 35f, UiArt.Numbers);
                // uicore's drag and drop: a bag item dropped on a slot joins the draft (it stays in its bag slot until the mail is sent)
                UiKit.DropTarget(b, p => p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0 && Mailbox() != null, p =>
                {
                    IWowSocial x2 = SocialUi.Soc(Ctx.S);
                    if (x2 != null && p != null) x2.AttachMailItem(p.Bag, p.Slot);
                    UiPickup.Clear("placed");
                });
                _slots[i] = b;
            }
            // SendMailMoneyText / MoneyInputFrameTemplate (gold 58 x 20, silver / copper) + the Send Money / C.O.D. radios
            _moneyLabel = UiKit.Text(_send, "SendMailMoneyText", SocialUi.G("SEND_MONEY", "Send Money"), 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 24f, 372f, 180f, 12f);   // :6214
            Texture2D coins = UiArt.Tex(UiArt.MoneyIcons);
            _gold = Money(_send, "SendMailMoneyGold", 24f, 386f, 58f, 5);
            UiKit.Pic(_send, "Gold coin", coins, 0f, 0.25f, 0f, 1f, 84f, 389f, 13f, 13f);
            _silver = Money(_send, "SendMailMoneySilver", 102f, 386f, 30f, 2);
            UiKit.Pic(_send, "Silver coin", coins, 0.25f, 0.5f, 0f, 1f, 134f, 389f, 13f, 13f);
            _copper = Money(_send, "SendMailMoneyCopper", 152f, 386f, 30f, 2);
            UiKit.Pic(_send, "Copper coin", coins, 0.5f, 0.75f, 0f, 1f, 184f, 389f, 13f, 13f);
            _radioMoney = Radio(_send, SocialUi.G("SEND_MONEY", "Send Money"), 214f, 368f, () => SetCod(false));
            _radioCod = Radio(_send, SocialUi.G("COD", "C.O.D."), 214f, 388f, () => SetCod(true));
            _codError = UiKit.Text(_send, "SendMailErrorText", "", 10f, UiArt.ErrorRed, TextAlignmentOptions.MidlineLeft, 24f, 356f, 300f, 14f);
            _codError.richText = false;
            // SendMailMailButton 80 x 22 left of SendMailCancelButton 80 x 22 BOTTOMRIGHT (-39, 80)
            _sendButton = UiKit.PanelButton(_send, SocialUi.G("SEND_LABEL", "Send"), 185f, 410f, 80f, 22f, SendClick);                 // :6211
            UiKit.PanelButton(_send, SocialUi.G("CANCEL", "Cancel"), 265f, 410f, 80f, 22f, close);
            SetCod(false);

            // ---- MailFrameTab1 BOTTOMLEFT (24, 44) INBOX, Tab2 right of it (-8, 0) SENDMAIL
            _tabInbox = SocialUi.BottomTab(Root, SocialUi.G("INBOX", "Inbox"), 24f, H - 44f - 32f, () => SetTab(1, true));
            _tabSend = SocialUi.BottomTab(Root, SocialUi.G("SENDMAIL", "Send Mail"), 24f + _tabInbox.W - 8f, H - 44f - 32f, () => SetTab(2, true));   // :6208
            SetTab(1, false);
            Root.gameObject.SetActive(false);
            Built.Add(this);
            if (!_loggedBuilt)
            {
                _loggedBuilt = true;
                WowLog.Info("UI: Mail window built (WoW's MailFrame: Inbox 7 a page, the opened mail " + (ctx.Vr ? "in the inbox's place (VR, ours)" : "right of it")
                            + ", Send Mail with 12 attachment slots - drag a bag item onto a slot or right-click it in the bags).");
            }
        }

        private static void Label(Transform parent, string text, float x, float y, float w = 100f)
        {
            TextMeshProUGUI t = UiKit.Text(parent, "Label " + text, text, 12f, Color.white, TextAlignmentOptions.MidlineRight, x, y, w, 16f);   // GameFontHighlight
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private TMP_InputField Money(Transform parent, string name, float x, float y, float w, int digits)
        {
            TMP_InputField f = SocialUi.Field(parent, name, x, y, w, 20f, digits, false, 12f, SocialUi.G("SEND_MONEY", "Send Money"), null);
            f.contentType = TMP_InputField.ContentType.IntegerNumber;   // MoneyInputFrameTemplate numeric="true"
            return f;
        }

        /// <summary>SendMailRadioButtonTemplate = UIRadioButtonTemplate (UIPanelTemplates.xml): 16 x 16 UI-RadioButton (0-0.25 normal, 0.25-0.5
        /// checked, 0.5-0.75 highlight ADD), the label right of it (5, 0).</summary>
        private static UiButton Radio(Transform parent, string label, float x, float y, Action click)
        {
            Texture2D t = UiArt.Tex(@"Interface\Buttons\UI-RadioButton.blp");
            UiButton b = UiKit.Button(parent, "Radio " + label, x, y, 130f, 16f);
            UiKit.Pic(b.transform, "Normal", t, 0f, 0.25f, 0f, 1f, 0f, 0f, 16f, 16f);
            b.Checked = UiKit.Pic(b.transform, "Checked", t, 0.25f, 0.5f, 0f, 1f, 0f, 0f, 16f, 16f);
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(@"Interface\Buttons\UI-RadioButton.blp"), 0.5f, 0.75f, 0f, 1f, 0f, 0f, 16f, 16f);
            b.Hilite.enabled = false;
            TextMeshProUGUI l = UiKit.Text(b.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 21f, 0f, 109f, 16f);
            l.textWrappingMode = TextWrappingModes.NoWrap;
            b.Click = click;
            return b;
        }

        private MailboxView Mailbox()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            return soc != null ? soc.Mailbox : null;
        }

        // ================================================================== tabs / open / close

        private void SetTab(int t, bool sound)
        {
            if (sound && t != _tab) UiSound.Named("igSpellBookOpen");   // MailFrame.lua:130
            _tab = t;
            _tabInbox.Select(t == 1);
            _tabSend.Select(t == 2);
            string[] art = t == 1 ? InboxArt : SendArt;
            for (int i = 0; i < 4; i++) _corner[i].texture = UiArt.Tex(art[i]);
            SocialUi.Show(_inbox, t == 1 && !(Ctx.Vr && _openId != 0));
            SocialUi.Show(_send, t == 2);
            if (t == 2) ShowOpenMail(0);
            else { SocialUi.StopTyping(_to); SocialUi.StopTyping(_subject); SocialUi.StopTyping(_body); }
            if (_title != null) _title.text = t == 1 ? SocialUi.G("INBOX", "Inbox") : SocialUi.G("SENDMAIL", "Send Mail");
            _inboxSeen = _sendSeen = -1;
        }

        protected override void OnOpen()
        {
            // MAIL_SHOW (MailFrame.lua:36-52): the Inbox tab, the backpack opens (OpenBackpack), the list was asked by the session
            _pageIndex = 0;
            _openId = 0;
            _open.gameObject.SetActive(false);
            ResetSend();
            SetTab(1, false);
            UiBags b = UiBags.Current;
            if (b != null) b.ServiceOpen(true);
        }

        protected override void OnClose()
        {
            CloseOpenMail();
            SocialUi.StopTyping(_to); SocialUi.StopTyping(_subject); SocialUi.StopTyping(_body);
            SocialUi.StopTyping(_gold); SocialUi.StopTyping(_silver); SocialUi.StopTyping(_copper);
            UiBags b = UiBags.Current;
            if (b != null) b.ServiceOpen(false);
        }

        public override void Refresh(float now)
        {
            MailboxView box = Mailbox();
            if (box == null) return;
            if (_tab == 1) RefreshInbox(box);
            else RefreshSend(box);
            if (_openId != 0) RefreshOpen(box);
        }

        // ================================================================== inbox

        private void Page(int by)
        {
            _pageIndex = Mathf.Max(0, _pageIndex + by);
            _inboxSeen = -1;
        }

        private void RefreshInbox(MailboxView box)
        {
            int wheel = SocialUi.Wheel(_inbox);
            if (wheel != 0) Page(-wheel);
            int sig = box.Counter * 7919 + _pageIndex * 31 + (int)(_openId & 0xFFFF);
            if (sig == _inboxSeen) return;
            _inboxSeen = sig;
            List<MailView> mails = box.Mails;
            int pages = Mathf.Max(1, (mails.Count + InboxItemsToDisplay - 1) / InboxItemsToDisplay);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, pages - 1);
            for (int i = 0; i < InboxItemsToDisplay; i++)
            {
                int idx = _pageIndex * InboxItemsToDisplay + i;
                UiButton b = _rows[i];
                if (idx >= mails.Count) { SocialUi.Show(b, false); continue; }
                MailView m = mails[idx];
                SocialUi.Show(b, true);
                // InboxFrame_Update (MailFrame.lua:150-235): the first item's icon (not for a GM mail), else the stationery's; read = grey
                string icon = m.Items.Count > 0 && !m.IsGm && m.Items[0].Item != null ? m.Items[0].Item.IconPath : MailArt.StationeryIcon(m.Stationery);
                Texture2D tex = !string.IsNullOrEmpty(icon) ? UiArt.Icon(icon) : null;
                _rowIcon[i].texture = tex;
                _rowIcon[i].color = tex == null ? new Color(0f, 0f, 0f, 0f) : m.Read ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
                _rowSel[i].enabled = m.Id == _openId;
                _rowCod[i].enabled = m.Cod > 0;
                Color sender = m.Read ? new Color(0.75f, 0.75f, 0.75f) : UiArt.Gold, subject = m.Read ? new Color(0.75f, 0.75f, 0.75f) : Color.white;
                SocialUi.SetText(_rowSender[i], SocialUi.Name(m.Sender), sender);
                SocialUi.SetText(_rowSubject[i], m.Subject ?? "", subject);
                Color c;
                SocialUi.SetText(_rowExpire[i], DaysLeft(m.DaysLeft, out c), c);
                _rowTip[i] = m.CanReturn ? SocialUi.G("TIME_UNTIL_RETURNED", "Time until message is returned") : SocialUi.G("TIME_UNTIL_DELETED", "Time until message is deleted");   // :7581 / :7580
            }
            _prev.Interactable = _pageIndex > 0;
            _next.Interactable = _pageIndex < pages - 1;
            SocialUi.SetText(_page, SocialUi.F("PAGE_NUMBER", "Page %d", _pageIndex + 1), UiArt.Gold);   // :5562
            _tooMuch.enabled = box.TotalOnServer > box.Mails.Count;
        }

        /// <summary>The expiry column (MailFrame.lua:195-197): DAYS_ABBR in green from one day, else SecondsToTime (abbreviated, at most 2
        /// units: HOURS_ABBR / MINUTES_ABBR / SECONDS_ABBR, TIME_UNIT_DELIMITER) in red.</summary>
        private static string DaysLeft(float days, out Color c)
        {
            if (days >= 1f) { c = Color.green; return SocialUi.F("DAYS_ABBR", "%d |4Day:Days;", (int)days); }   // GlobalStrings.lua:1925
            c = UiArt.ErrorRed;
            int s = Mathf.Max(0, Mathf.FloorToInt(days * 86400f));
            int h = s / 3600, m = s / 60 % 60;
            string d = SocialUi.G("TIME_UNIT_DELIMITER", " ");
            if (h > 0) return SocialUi.F("HOURS_ABBR", "%d |4Hr:Hr;", h) + (m > 0 ? d + SocialUi.F("MINUTES_ABBR", "%d |4Min:Min;", m) : "");   // :4196 / :4953
            if (m > 0) return SocialUi.F("MINUTES_ABBR", "%d |4Min:Min;", m);
            return SocialUi.F("SECONDS_ABBR", "%d |4Sec:Sec;", s);
        }

        private MailView Find(uint id)
        {
            MailboxView box = Mailbox();
            if (box == null || id == 0) return null;
            for (int i = 0; i < box.Mails.Count; i++) if (box.Mails[i].Id == id) return box.Mails[i];
            return null;
        }

        /// <summary>InboxFrame_OnClick (MailFrame.lua:259-279): a click toggles the opened mail; Shift-click = AutoLootMailItem (money + every
        /// item) when the mail has no C.O.D., then opens it.</summary>
        private void RowClick(int row)
        {
            MailboxView box = Mailbox();
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            int idx = _pageIndex * InboxItemsToDisplay + row;
            if (box == null || soc == null || idx < 0 || idx >= box.Mails.Count) return;
            MailView m = box.Mails[idx];
            if (SocialUi.Shift() && m.Cod == 0 && (m.Items.Count > 0 || m.Money > 0))
            {
                soc.TakeAllFromMail(m.Id);
                ShowOpenMail(m.Id);
                return;
            }
            UiSound.Named("igSpellBookOpen");
            ShowOpenMail(m.Id == _openId ? 0u : m.Id);
        }

        // ================================================================== the opened mail

        private void ShowOpenMail(uint id)
        {
            if (id == _openId && (id == 0) == !_open.gameObject.activeSelf) return;
            _openId = id;
            _openSeen = -1;
            _inboxSeen = -1;
            SocialUi.Show(_open, id != 0);
            if (Ctx.Vr) SocialUi.Show(_inbox, _tab == 1 && id == 0);   // VR (ours): the opened mail takes the inbox's place
            if (id == 0) return;
            MailView m = Find(id);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (m != null && soc != null && !m.Read) soc.ReadMail(id);   // CMSG_MAIL_MARK_AS_READ
        }

        /// <summary>OpenMailFrame_OnHide (MailFrame.lua:336-363): a mail with no money, no items and a copied text is deleted.</summary>
        private void CloseOpenMail()
        {
            if (_openId == 0) { SocialUi.Show(_open, false); return; }
            MailView m = Find(_openId);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (m != null && soc != null && m.Money == 0 && m.Items.Count == 0 && m.Copied) soc.DeleteMail(m.Id);
            ShowOpenMail(0);
        }

        private void RefreshOpen(MailboxView box)
        {
            MailView m = Find(_openId);
            if (m == null) { ShowOpenMail(0); return; }   // taken / deleted / returned: the list no longer has it
            int sig = box.Counter * 131 + m.Items.Count * 7 + m.Money + m.Flags * 3;
            for (int i = 0; i < m.Items.Count; i++)   // lead fix 2026-11-03: attachment names / icons that arrive after the mail opened
            {
                ItemView a = m.Items[i].Item;
                if (a != null) sig = sig * 17 + (a.Name != null ? a.Name.GetHashCode() : 0) + a.Quality * 5 + a.DisplayId;
            }
            if (sig == _openSeen) return;
            _openSeen = sig;
            SocialUi.SetText(_oSender, SocialUi.Name(m.Sender), UiArt.Gold);
            SocialUi.SetText(_oSubject, m.Subject ?? "", UiArt.Gold);
            _oSubject.richText = false;
            SocialUi.SetText(_oBody, m.Body ?? "", UiArt.QuestText);
            string tex = MailArt.StationeryTexture(m.Stationery);
            _oStationeryL.texture = tex != null ? UiArt.Tex(Stationery + tex + "1.blp") : null;
            _oStationeryR.texture = tex != null ? UiArt.Tex(Stationery + tex + "2.blp") : null;
            _oStationeryL.color = _oStationeryL.texture != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            _oStationeryR.color = _oStationeryR.texture != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            // the cells: the money, the letter (hidden when copied or without a body), the items - rows of 7 centred like OpenMail_Update
            // (MailFrame.lua:399-430); the body shrinks to make room above them (ours: WoW resizes OpenMailScrollFrame the same way)
            int n = Mathf.Min(m.Items.Count, AttachmentsMaxReceive);
            bool money = m.Money > 0, letter = !m.Copied && !string.IsNullOrEmpty(m.Body);
            int cells = n + (money ? 1 : 0) + (letter ? 1 : 0);
            int rows = (cells + AttachmentsPerRowReceive - 1) / AttachmentsPerRowReceive;
            float gridTop = 404f - rows * 39f;
            int cell = 0;
            SocialUi.Show(_oMoney, money);
            if (money) PlaceCell(_oMoney, cell++, cells, gridTop);
            SocialUi.Show(_oLetter, letter);
            if (letter)
            {
                PlaceCell(_oLetter, cell++, cells, gridTop);
                string li = MailArt.ItemIcon(MailBodyItem);
                Texture2D lt = !string.IsNullOrEmpty(li) ? UiArt.Icon(li) : null;
                _oLetter.Icon.texture = lt;
                _oLetter.Icon.color = lt != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            }
            for (int i = 0; i < AttachmentsMaxReceive; i++)
            {
                UiButton b = _oItems[i];
                if (i >= n) { SocialUi.Show(b, false); continue; }
                SocialUi.Show(b, true);
                PlaceCell(b, cell++, cells, gridTop);
                ItemView it = m.Items[i].Item;
                Texture2D t = it != null && !string.IsNullOrEmpty(it.IconPath) ? UiArt.Icon(it.IconPath) : null;
                b.Icon.texture = t;
                b.Icon.color = t != null ? Color.white : new Color(0f, 0f, 0f, 0f);
                int count = m.Items[i].Count;
                if (b.Count != null) b.Count.text = count > 1 ? count.ToString() : "";
            }
            _oAttachLabel.enabled = n > 0 || money;
            UiKit.Move(_oAttachLabel.rectTransform, 26f, gridTop - 16f);
            float bodyBottom = cells > 0 ? gridTop - 20f : 354f;
            UiKit.Size(_oBody.rectTransform, 276f, Mathf.Max(40f, bodyBottom - 105f));
            _oReply.Interactable = m.CanReply;
            _oDelete.SetLabel(m.CanReturn ? SocialUi.G("MAIL_RETURN", "Return") : SocialUi.G("DELETE", "Delete"));
        }

        private static void PlaceCell(UiButton b, int cell, int cells, float top)
        {
            int row = cell / AttachmentsPerRowReceive;
            int inRow = Mathf.Min(AttachmentsPerRowReceive, cells - row * AttachmentsPerRowReceive);
            float xstart = (AttachmentsPerRowReceive - inRow) / 2f;   // OpenMail_Update: each row centred
            UiKit.Move(b.Rt, 54f + (xstart + cell % AttachmentsPerRowReceive) * 39f, top + row * 39f);
        }

        private string ItemTip(int i)
        {
            MailView m = Find(_openId);
            if (m == null || i >= m.Items.Count || m.Items[i].Item == null) return null;
            return UiTooltip.Item(m.Items[i].Item, m.Cod > 0 ? SocialUi.G("COD_CONFIRMATION", "Accepting this item will cost:") + " " + UiArt.MoneyText(m.Cod) : null);
        }

        /// <summary>OpenMailAttachment_OnClick (MailFrame.lua:771-786): C.O.D. above the money = COD_ALERT; C.O.D. = COD_CONFIRMATION (its money
        /// frame shows the C.O.D.); else TakeInboxItem.</summary>
        private void TakeItem(int i)
        {
            MailView m = Find(_openId);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (m == null || soc == null || i >= m.Items.Count) return;
            uint id = m.Id, item = m.Items[i].ItemGuidLow;
            if (m.Cod > 0)
            {
                int money = Ctx.P != null ? Ctx.P.Money : 0;
                if (m.Cod > money)
                {
                    SocialUi.Confirm("COD_ALERT", SocialUi.G("COD_INSUFFICIENT_MONEY", "You do not have enough money to pay the C.O.D. charges."), SocialUi.G("CLOSE", "Close"), null, null);   // StaticPopup.lua:765
                    return;
                }
                SocialUi.Confirm("COD_CONFIRMATION", SocialUi.G("COD_CONFIRMATION", "Accepting this item will cost:"), SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"),   // :772
                    () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.TakeMailItem(id, item); }, m.Cod);
                return;
            }
            soc.TakeMailItem(id, item);
        }

        private void TakeMoney()
        {
            MailView m = Find(_openId);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (m != null && soc != null && m.Money > 0) soc.TakeMailMoney(m.Id);
        }

        private void CopyLetter()
        {
            MailView m = Find(_openId);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (m != null && soc != null && !m.Copied) soc.CopyMailText(m.Id);   // CMSG_MAIL_CREATE_TEXT_ITEM
        }

        /// <summary>OpenMail_Delete (MailFrame.lua:731-746): Return for a returnable mail; deletable + items = DELETE_MAIL (the item's name),
        /// + money = DELETE_MONEY (the money frame), otherwise deleted at once.</summary>
        private void DeleteOrReturn()
        {
            MailView m = Find(_openId);
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (m == null || soc == null) return;
            uint id = m.Id;
            Action delete = () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.DeleteMail(id); ShowOpenMail(0); };
            if (m.CanReturn) { soc.ReturnMail(id); ShowOpenMail(0); return; }
            if (m.Items.Count > 0)
            {
                string item = m.Items[0].Item != null && !string.IsNullOrEmpty(m.Items[0].Item.Name) ? m.Items[0].Item.Name : SocialUi.G("UNKNOWN", "Unknown");
                SocialUi.Confirm("DELETE_MAIL", SocialUi.F("DELETE_MAIL_CONFIRMATION", "Deleting this mail will also destroy %s", item),   // :1972
                    SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"), delete);
                return;
            }
            if (m.Money > 0)
            {
                SocialUi.Confirm("DELETE_MONEY", SocialUi.G("DELETE_MONEY_CONFIRMATION", "Deleting this mail will also destroy:"),   // :1973
                    SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"), delete, m.Money);
                return;
            }
            delete();
        }

        /// <summary>OpenMail_Reply (MailFrame.lua:716-729): the Send Mail tab, To = the sender, Subject = MAIL_REPLY_PREFIX + subject unless it has it.</summary>
        private void Reply()
        {
            MailView m = Find(_openId);
            if (m == null || !m.CanReply) return;
            string prefix = SocialUi.G("MAIL_REPLY_PREFIX", "RE:");   // :4870
            string subject = m.Subject ?? "";
            if (!subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) subject = prefix + " " + subject;
            SetTab(2, true);
            _to.text = m.Sender ?? "";
            _subject.text = subject;
            _autoSubject = "";
            SocialUi.Type(_body, SocialUi.G("SENDMAIL", "Send Mail"), null);
        }

        // ================================================================== send mail

        private void SetCod(bool cod)
        {
            _cod = cod;
            if (_radioMoney.Checked != null) _radioMoney.Checked.enabled = !cod;
            if (_radioCod.Checked != null) _radioCod.Checked.enabled = cod;
            _moneyLabel.text = cod ? SocialUi.G("COD_AMOUNT", "Cash on Delivery Amount:") : SocialUi.G("SEND_MONEY", "Send Money");   // :1654 / :6214
            _sendSeen = -1;
        }

        private int Copper()
        {
            return ParseInt(_gold, 0, 99999) * 10000 + ParseInt(_silver, 0, 99) * 100 + ParseInt(_copper, 0, 99);
        }

        private static int ParseInt(TMP_InputField f, int min, int max)
        {
            int v;
            if (f == null || !int.TryParse((f.text ?? "").Trim(), out v)) return 0;
            return Mathf.Clamp(v, min, max);
        }

        private void RefreshSend(MailboxView box)
        {
            // MAIL_SEND_SUCCESS -> SendMailFrame_Reset (MailFrame.lua:946-957)
            if (_sending && box.LastSendOk.HasValue)
            {
                _sending = false;
                if (box.LastSendOk.Value) ResetSend();
            }
            List<ItemView> draft = box.Attachments;
            // an empty subject (or our previous automatic one) becomes the first attachment's name (+ " (n)" for a stack) - SendMailFrame_Update
            string subj = SocialUi.Text(_subject);
            if (draft.Count > 0 && draft[0] != null && !string.IsNullOrEmpty(draft[0].Name) && (subj.Length == 0 || subj == _autoSubject))
            {
                string auto = draft[0].Count > 1 ? draft[0].Name + " (" + draft[0].Count + ")" : draft[0].Name;
                if (auto != subj) { _subject.text = auto; subj = auto; }
                _autoSubject = auto;
            }
            if (_cod && draft.Count == 0) SetCod(false);   // C.O.D. only with items
            int sig = box.Counter * 17 + draft.Count;
            if (sig != _sendSeen)
            {
                _sendSeen = sig;
                for (int i = 0; i < AttachmentsMaxSend; i++)
                {
                    UiButton b = _slots[i];
                    ItemView it = i < draft.Count ? draft[i] : null;
                    Texture2D t = it != null && !string.IsNullOrEmpty(it.IconPath) ? UiArt.Icon(it.IconPath) : null;
                    b.Icon.texture = t;
                    b.Icon.color = t != null ? Color.white : new Color(0f, 0f, 0f, 0f);
                    if (b.Count != null) b.Count.text = it != null && it.Count > 1 ? it.Count.ToString() : "";
                }
            }
            // the postage, red when it is more than the player has
            int postage = box.Postage, money = Ctx.P != null ? Ctx.P.Money : 0;
            bool red = postage > money;
            SocialUi.Show(_postageOk, !red);
            SocialUi.Show(_postageRed, red);
            _postageWhite.Set(postage);
            _postageRedMoney.Set(postage);
            // SendMailFrame_CanSend (MailFrame.lua:959-990): a recipient AND a subject; a C.O.D. at most MAX_COD_AMOUNT gold
            bool codTooMuch = _cod && Copper() > MaxCodGold * 10000;
            string err = codTooMuch ? SocialUi.F("MAIL_COD_ERROR", "C.O.D. must be <= %d", MaxCodGold) : "";   // :4864
            if (_codError.text != err) _codError.text = err;
            _sendButton.Interactable = SocialUi.Text(_to).Length > 0 && subj.Length > 0 && !codTooMuch && !_sending;
        }

        private string DraftTip(int i)
        {
            MailboxView box = Mailbox();
            if (box == null || i >= box.Attachments.Count || box.Attachments[i] == null) return null;
            return UiTooltip.Item(box.Attachments[i], null);
        }

        private void Detach(int i)
        {
            MailboxView box = Mailbox();
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (box == null || soc == null || i >= box.Attachments.Count) return;
            soc.DetachMailItem(i);
        }

        /// <summary>SendMailMailButton_OnClick (MailFrame.lua:789-808): money to send asks SEND_MONEY (the name) first; C.O.D. sends the amount.</summary>
        private void SendClick()
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null || soc.Mailbox == null) return;
            string to = SocialUi.Text(_to), subject = SocialUi.Text(_subject), body = _body.text ?? "";
            int copper = Copper();
            if (to.Length == 0 || subject.Length == 0) return;
            if (!_cod && copper > 0)
            {
                SocialUi.Confirm("SEND_MONEY", SocialUi.F("SEND_MONEY_CONFIRMATION", "Really send %s the following amount?", to),   // :6215
                    SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"), () => Send(to, subject, body, copper, 0), copper);
                return;
            }
            Send(to, subject, body, 0, _cod ? copper : 0);
        }

        private void Send(string to, string subject, string body, int money, int cod)
        {
            IWowSocial soc = SocialUi.Soc(Ctx.S);
            if (soc == null || soc.Mailbox == null) return;
            _sending = true;
            soc.SendMail(to, subject, body, money, cod);
            if (soc.Mailbox.LastSendOk.HasValue && !soc.Mailbox.LastSendOk.Value) _sending = false;   // refused at once (an Error event)
        }

        private void ResetSend()
        {
            _to.text = "";
            _subject.text = "";
            _body.text = "";
            _gold.text = _silver.text = _copper.text = "";
            _autoSubject = "";
            SetCod(false);
            _sending = false;
        }
    }

    /// <summary>
    /// The mail art of the install's DBCs (read once, never copied): Stationery.dbc (ID, ItemID, Texture, Flags - measured 7 rows x 4,
    /// proposal-social.md 4.6) -> the texture of STATIONERY_PATH and the stationery item's icon through Item.dbc DisplayInfoID ->
    /// ItemDisplayInfo.dbc InventoryIcon (the inbox icon of a mail without items).
    /// </summary>
    internal static class MailArt
    {
        const int StationeryFields = 4, StItemId = 1, StTexture = 2;   // Stationery.dbc (measured, SocialMailDbc)
        static bool _tried;
        static DbcTable _stationery, _item, _display;
        static readonly Dictionary<int, string> IconCache = new Dictionary<int, string>();

        private static void Load()
        {
            if (_tried) return;
            WowFiles files = UiArt.Files;
            if (files == null) return;
            _tried = true;
            try
            {
                _stationery = Dbc.Load(files, "Stationery");
                if (_stationery != null && _stationery.FieldCount != StationeryFields) { WowLog.Warn("UI: mail - Stationery.dbc has " + _stationery.FieldCount + " fields, expected " + StationeryFields + " - no stationery art."); _stationery = null; }
                _item = Dbc.Load(files, "Item");
                if (_item != null && _item.FieldCount != DbcFields.Item.FieldCount) _item = null;
                _display = Dbc.Load(files, "ItemDisplayInfo");
                if (_display != null && _display.FieldCount != DbcFields.ItemDisplayInfo.FieldCount) _display = null;
                WowLog.Info("UI: mail art - Stationery.dbc " + (_stationery != null ? _stationery.RecordCount + " rows" : "missing") + ", Item.dbc " + (_item != null ? "read" : "missing")
                            + ", ItemDisplayInfo.dbc " + (_display != null ? "read" : "missing") + ".");
            }
            catch (Exception e) { WowLog.Warn("UI: mail art - the DBCs could not be read: " + e.Message); }
        }

        /// <summary>The Texture of a Stationery.dbc row ("STATIONERYTEST" for 41), null when unknown.</summary>
        public static string StationeryTexture(int id)
        {
            Load();
            if (_stationery == null || id <= 0) return null;
            int r = _stationery.FindRow(id);
            if (r < 0) r = _stationery.FindRow(41);   // the default stationery (MailHandler.cpp: MAIL_STATIONERY_DEFAULT 41)
            return r >= 0 ? _stationery.GetString(r, StTexture) : null;
        }

        /// <summary>The icon of the stationery's item (Interface\Icons\...), null when unknown.</summary>
        public static string StationeryIcon(int id)
        {
            Load();
            if (_stationery == null) return null;
            int r = _stationery.FindRow(id > 0 ? id : 41);
            if (r < 0) r = _stationery.FindRow(41);
            return r >= 0 ? ItemIcon(_stationery.GetInt(r, StItemId)) : null;
        }

        /// <summary>An item entry's inventory icon (Item.dbc DisplayInfoID -> ItemDisplayInfo.dbc InventoryIcon), cached.</summary>
        public static string ItemIcon(int entry)
        {
            Load();
            string icon;
            if (IconCache.TryGetValue(entry, out icon)) return icon;
            icon = null;
            if (_item != null && _display != null && entry > 0)
            {
                int r = _item.FindRow(entry);
                int d = r >= 0 ? _item.GetInt(r, DbcFields.Item.DisplayInfoID) : 0;
                int dr = d > 0 ? _display.FindRow(d) : -1;
                string name = dr >= 0 ? _display.GetString(dr, DbcFields.ItemDisplayInfo.InventoryIcon) : null;
                if (!string.IsNullOrEmpty(name)) icon = @"Interface\Icons\" + name;
            }
            IconCache[entry] = icon;
            return icon;
        }
    }
}
