using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Social;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 11 (owner social, proposal-social.md 3.7 / 4.8 / 6.3.4): the mailbox - inbox, read, take money / items (+ WoW's Shift-click
    // "take everything"), return, delete, permanent copy, the send draft (attachments from the bags) and Send, the new-mail state.
    // AzerothCore master (P/acsrc11/social): MailHandler.cpp, Mail.h, Player.cpp:2955-2984, SharedDefines.h:3754-3780.
    // Every mail opcode carries the mailbox guid and the server re-checks it (CanOpenMailBox, MailHandler.cpp:39-63: in interaction range,
    // 10 yd for a mailbox gameobject) - the mailbox closes when the player walks out of reach or dies, like WoW's MAIL_CLOSED.
    // Ours (logged once in InitMail): MSG_QUERY_NEXT_MAIL_TIME at login, every MailPollEvery and after SMSG_RECEIVED_MAIL; the chat line
    // HAVE_MAIL when new mail arrives (WoW only lights the icon); TakeAllFromMail sends one request after the previous answer.
    public sealed partial class OnlineSession
    {
        const double MailPollEvery = 60.0;               // ours: s between two MSG_QUERY_NEXT_MAIL_TIME
        const double MailTakeTimeout = 5.0;              // ours: s a TakeAll step waits for its SMSG_SEND_MAIL_RESULT before it stops
        const double MailSenderWait = 3.0;               // ours: s creature / gameobject sender names wait for their query
        const int MailSubjectMax = 64;                   // SendMailSubjectEditBox (proposal 6.3.4)
        const int MailBodyMax = 500;                     // SendMailBodyEditBox (proposal 1.1 / 6.3.4)
        const int MailStationeryDefault = 41;            // MAIL_STATIONERY_DEFAULT (Mail.h:58)
        const int MailMaxItems = 12;                     // MAX_MAIL_ITEMS (Mail.h:33)
        const int MailGoType = 19;                       // GAMEOBJECT_TYPE_MAILBOX
        const uint UnitNpcFlagMailbox = 0x04000000;      // UNIT_NPC_FLAG_MAILBOX (UnitDefines.h:348) - no creature carries it in AzerothCore's data
        // DBC layouts (measured, SocialMailDbc / SocialMailSenders): MailTemplate 35 fields = ID, Subject_lang 1, Body_lang 18;
        // AuctionHouse = ID, faction, deposit, cut, Name_lang 4
        const int MailTemplateSubject = 1, MailTemplateBody = 18, AuctionHouseName = 4;

        // WoW's texts (GlobalStrings.lua of the 3.3.5a enUS install, line numbers)
        const string ErrMailSent = "Mail sent.";                                                       // ERR_MAIL_SENT (3242)
        const string ErrMailToSelf = "You can't send mail to yourself.";                               // ERR_MAIL_TO_SELF (3245)
        const string ErrNotEnoughMoneyMail = "You don't have enough money.";                           // ERR_NOT_ENOUGH_MONEY (3307)
        const string ErrMailTargetNotFound = "Cannot find mail recipient.";                            // ERR_MAIL_TARGET_NOT_FOUND (3243)
        const string ErrPlayerWrongFaction = "Target is unfriendly.";                                  // ERR_PLAYER_WRONG_FACTION (3396) - ours by meaning
        const string ErrMailDatabaseError = "Internal mail database error.";                           // ERR_MAIL_DATABASE_ERROR (3236)
        const string ErrMailReachedCap = "You have reached the in-game cap of unique mail recipients"; // ERR_MAIL_REACHED_CAP (3241)
        const string ErrMailWrappedCod = "You can't send wrapped items C.O.D.";                         // ERR_MAIL_WRAPPED_COD (3246)
        const string ErrMailTooManyAttachments = "A mail included too many attachments.";               // ERR_MAIL_TOO_MANY_ATTACHMENTS (3244)
        const string ErrMailInvalidAttachment = "A mail attachment was invalid.";                       // ERR_MAIL_INVALID_ATTACHMENT (3237)
        const string ErrMailAttachmentExpired = "That item has expired.";                               // ERR_MAIL_ATTACHMENT_EXPIRED (3232)
        const string ErrMailAttachmentSlot = "You cannot attach more than 12 items to mail.";           // ERR_MAIL_INVALID_ATTACHMENT_SLOT (3238)
        const string HaveMail = "You have unread mail";                                                 // HAVE_MAIL (4002)

        struct MailAttach { public ulong Guid; public byte Bag, Slot; }

        MailboxView _mailbox;
        readonly List<MailAttach> _mailAttach = new List<MailAttach>();
        readonly Queue<long> _mailTakeQueue = new Queue<long>();                       // -1 = the money, else an item's low guid
        readonly List<ulong> _mailSenders = new List<ulong>();
        readonly List<int> _mailEntries = new List<int>(), _mailTypes = new List<int>();
        uint _mailTakeMail;
        bool _mailTakeBusy, _mailArrived, _mailOpenedSent, _mailNamesPending, _mailDbcTried, _mailListLogged;
        double _mailPollAt, _mailTakeAt, _mailNamesUntil;
        int _mailFromGen, _mailLists, _mailResults;
        DbcTable _mailTemplates, _mailAuctionHouses;

        MailboxView IWowSocial.Mailbox { get { return _mailbox; } }

        partial void InitMail()
        {
            _world.On(Op.SMSG_MAIL_LIST_RESULT, MailOnList, this);
            _world.On(Op.SMSG_SEND_MAIL_RESULT, MailOnResult, this);
            _world.On(Op.SMSG_RECEIVED_MAIL, MailOnReceived, this);
            _world.On(Op.MSG_QUERY_NEXT_MAIL_TIME, MailOnNextTime, this);
            RegisterNpcService(UnitNpcFlagMailbox, g => { OpenMailbox(g); return true; }, "mailbox");   // npc H4 (custom mailbox creatures)
            WowLog.Info("Online mail: ours - MSG_QUERY_NEXT_MAIL_TIME at login, every " + MailPollEvery + " s and after new mail; the line \"" + HaveMail
                        + "\" when mail arrives; Take All one request after the previous answer (" + MailTakeTimeout + " s timeout); the mailbox "
                        + "closes out of reach (10 yd, CanOpenMailBox MailHandler.cpp:39-63)");
        }

        void MailTick()
        {
            if (_world == null) return;
            if (_now >= _mailPollAt)
            {
                _mailPollAt = _now + MailPollEvery;
                _world.Send(Op.MSG_QUERY_NEXT_MAIL_TIME);                                // MailHandler.cpp:891-938 (empty)
            }
            if (_mailbox == null) return;
            OnlineObject o;
            bool here = _objects.TryGetValue(_mailbox.MailboxGuid, out o) && o.State != null && !_player.Dead
                        && QInReach(o, o.State, o.TypeId == TypeId.GameObject ? QGoInteractionDistance(MailGoType) : QInteractionDistance);
            if (!here)
            {
                WowLog.Info("Online mail: mailbox out of reach / gone / dead - closed (WoW's MAIL_CLOSED)");
                CloseMailbox();
                return;
            }
            MailCheckDraft();
            if (_mailNamesPending) MailFillSenders();
            if (_mailTakeBusy && _now - _mailTakeAt > MailTakeTimeout)
            {
                _mailTakeBusy = false;
                _mailTakeQueue.Clear();
                WowLog.Warn("Online mail: Take All - no answer in " + MailTakeTimeout + " s, stopped");
            }
        }

        MailView MailFind(uint id)
        {
            if (_mailbox == null) return null;
            foreach (var m in _mailbox.Mails) if (m.Id == id) return m;
            return null;
        }

        void MailDirty()
        {
            if (_mailbox != null) _mailbox.Counter++;
            SocChanged(SocialPart.Mail);
        }

        /// <summary>Mail texts: '|' -> '/' (a subject / body with "| |" is dropped by the server, MailHandler.cpp:78-82), line breaks kept,
        /// other control characters as spaces, cut to max.</summary>
        static string MailClean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '|') sb.Append('/');
                else if (c == '\r') continue;
                else if (c < ' ' && c != '\n' && c != '\t') sb.Append(' ');
                else sb.Append(c);
            }
            string r = sb.ToString();
            return r.Length > max ? r.Substring(0, max) : r;
        }

        // ------------------------------------------------------------------ open / close / list

        public void OpenMailbox(ulong mailboxGuid)
        {
            if (_world == null || mailboxGuid == 0) return;
            _mailbox = new MailboxView { MailboxGuid = mailboxGuid, Waiting = true };
            _mailAttach.Clear();
            _mailTakeQueue.Clear();
            _mailTakeBusy = false;
            _mailOpenedSent = false;
            _world.Send(Op.CMSG_GET_MAIL_LIST, new PacketWriter().U64(mailboxGuid));         // MailHandler.cpp:681-692
            _world.Send(Op.MSG_QUERY_NEXT_MAIL_TIME);
            _mailPollAt = _now + MailPollEvery;
            MailDirty();
            WowLog.Info("Online mail: mailbox opened");
        }

        public void CloseMailbox()
        {
            if (_mailbox == null) return;
            _mailbox = null;
            _mailAttach.Clear();
            _mailTakeQueue.Clear();
            _mailTakeBusy = false;
            SocChanged(SocialPart.Mail);
        }

        public void RefreshMailbox()
        {
            if (_world == null || _mailbox == null) return;
            _mailbox.Waiting = true;
            _world.Send(Op.CMSG_GET_MAIL_LIST, new PacketWriter().U64(_mailbox.MailboxGuid));
        }

        void MailOnList(WowPacket p)
        {
            if (_mailbox == null)
            {
                SocLogOnce("mailclosed", "a mail list arrived with no mailbox open (Mail.PushInboxOnDelivery) - ignored");
                return;
            }
            SocialRead.MailList(p, _mailbox);
            _mailLists++;
            if (!_mailOpenedSent)
            {
                _mailOpenedSent = true;
                NpcServiceOpened(_mailbox.MailboxGuid, "mailbox");       // npc H4: closes a mailbox creature's gossip (harmless for a gameobject)
            }
            MailTemplatesLoad();
            foreach (var m in _mailbox.Mails)
            {
                if (m.TemplateId > 0 && _mailTemplates != null && (string.IsNullOrEmpty(m.Subject) || string.IsNullOrEmpty(m.Body)))
                {
                    int r = _mailTemplates.FindRow(m.TemplateId);
                    if (r >= 0)
                    {
                        if (string.IsNullOrEmpty(m.Subject)) m.Subject = _mailTemplates.GetLocalizedString(r, MailTemplateSubject);
                        if (string.IsNullOrEmpty(m.Body)) m.Body = _mailTemplates.GetLocalizedString(r, MailTemplateBody);
                    }
                }
                foreach (var it in m.Items)
                {
                    var inst = new ItemTipInstance { RandomPropertyId = it.RandomPropertyId, SuffixFactor = it.SuffixFactor, Durability = it.Durability, MaxDurability = it.MaxDurability };
                    Array.Copy(it.Enchant, inst.Enchant, 7);
                    it.Item = ItemInfoViewOf(it.Entry, it.Count, inst);                       // item tooltip fix: the attachment's own roll + enchantments
                }
            }
            _mailNamesUntil = _now + MailSenderWait;
            MailFillSenders();
            MailDirty();
            if (!_mailListLogged)
            {
                _mailListLogged = true;
                WowLog.Info("Online mail: inbox - " + _mailbox.Mails.Count + " mail(s) shown of " + _mailbox.TotalOnServer + " (first list; later ones not logged)");
            }
        }

        void MailTemplatesLoad()
        {
            if (_mailDbcTried) return;
            _mailDbcTried = true;
            try
            {
                _mailTemplates = _files != null ? Dbc.Load(_files, "MailTemplate") : null;
                _mailAuctionHouses = _files != null ? Dbc.Load(_files, "AuctionHouse") : null;
            }
            catch (Exception e) { WowLog.Warn("Online mail: MailTemplate / AuctionHouse.dbc unreadable (" + e.Message + ")"); }
        }

        string MailSenderName(int type, int entry, out bool wait)
        {
            wait = false;
            switch (type)
            {
                case 2:                                                                         // MAIL_AUCTION: the AuctionHouse.dbc id
                    MailTemplatesLoad();
                    int r = _mailAuctionHouses != null ? _mailAuctionHouses.FindRow(entry) : -1;
                    string ah = r >= 0 ? _mailAuctionHouses.GetLocalizedString(r, AuctionHouseName) : null;
                    return string.IsNullOrEmpty(ah) ? SocUnknown : ah;
                case 3:                                                                         // MAIL_CREATURE
                    CreatureInfo ci;
                    if (_creatureInfo.TryGetValue(entry, out ci)) return ci.Missing || string.IsNullOrEmpty(ci.Name) ? SocUnknown : ci.Name;
                    if (_world != null && entry > 0 && _creatureAsked.Add(entry))
                    {
                        _world.Send(Op.CMSG_CREATURE_QUERY, new PacketWriter().U32((uint)entry).U64(0));   // uint32 entry, ObjectGuid (unused for the lookup)
                        _queriesSent++;
                    }
                    wait = true;
                    return null;
                case 4:                                                                         // MAIL_GAMEOBJECT
                    string gn;
                    if (_goNames.TryGetValue(entry, out gn)) return string.IsNullOrEmpty(gn) ? SocUnknown : gn;
                    if (_world != null && entry > 0 && _goAsked.Add(entry))
                    {
                        _world.Send(Op.CMSG_GAMEOBJECT_QUERY, new PacketWriter().U32((uint)entry).U64(0));
                        _queriesSent++;
                    }
                    wait = true;
                    return null;
                default:
                    return SocUnknown;                                                          // MAIL_CALENDAR and unknown types
            }
        }

        void MailFillSenders()
        {
            if (_mailbox == null) { _mailNamesPending = false; return; }
            bool pending = false, changed = false, late = _now >= _mailNamesUntil;
            foreach (var m in _mailbox.Mails)
            {
                if (m.Sender != null) continue;
                string n;
                bool wait;
                if (m.MessageType == 0)
                {
                    n = ChatSender(m.SenderGuid);
                    wait = n == null;
                    if (wait) SocAskName(m.SenderGuid);
                }
                else n = MailSenderName(m.MessageType, m.SenderEntry, out wait);
                if (n == null && late) n = SocUnknown;
                if (n != null) { m.Sender = n; changed = true; }
                else if (wait) pending = true;
            }
            _mailNamesPending = pending;
            if (changed) MailDirty();
        }

        // ------------------------------------------------------------------ inbox actions

        public void ReadMail(uint mailId)
        {
            MailView m = MailFind(mailId);
            if (m == null || _world == null || m.Read) return;
            _world.Send(Op.CMSG_MAIL_MARK_AS_READ, new PacketWriter().U64(_mailbox.MailboxGuid).U32(mailId));          // MailHandler.cpp:383-403 (no answer)
            m.Flags |= 1;                                                                                             // MAIL_CHECK_MASK_READ
            MailDirty();
        }

        public void TakeMailMoney(uint mailId)
        {
            if (_world == null || _mailbox == null) return;
            _world.Send(Op.CMSG_MAIL_TAKE_MONEY, new PacketWriter().U64(_mailbox.MailboxGuid).U32(mailId));           // MailHandler.cpp:636-679
        }

        public void TakeMailItem(uint mailId, uint itemGuidLow)
        {
            if (_world == null || _mailbox == null) return;
            _world.Send(Op.CMSG_MAIL_TAKE_ITEM, new PacketWriter().U64(_mailbox.MailboxGuid).U32(mailId).U32(itemGuidLow));   // MailHandler.cpp:517-634
        }

        /// <summary>WoW's Shift-click (AutoLootMailItem, MailFrame.lua:274-279): the money, then every item - each request after the previous
        /// answer. Never for a C.O.D. mail (WoW opens it instead).</summary>
        public void TakeAllFromMail(uint mailId)
        {
            MailView m = MailFind(mailId);
            if (m == null || _world == null) return;
            if (m.Cod > 0) { WowLog.Info("Online mail: Take All refused for a C.O.D. mail (WoW opens it instead)"); return; }
            _mailTakeQueue.Clear();
            _mailTakeMail = mailId;
            if (m.Money > 0) _mailTakeQueue.Enqueue(-1);
            foreach (var it in m.Items) _mailTakeQueue.Enqueue(it.ItemGuidLow);
            MailTakeNext();
        }

        void MailTakeNext()
        {
            if (_mailTakeQueue.Count == 0 || _mailbox == null)
            {
                bool was = _mailTakeBusy;
                _mailTakeBusy = false;
                if (was) RefreshMailbox();
                return;
            }
            long next = _mailTakeQueue.Dequeue();
            if (next < 0) TakeMailMoney(_mailTakeMail);
            else TakeMailItem(_mailTakeMail, (uint)next);
            _mailTakeBusy = true;
            _mailTakeAt = _now;
        }

        public void ReturnMail(uint mailId)
        {
            MailView m = MailFind(mailId);
            if (_world == null || _mailbox == null) return;
            // CMSG_MAIL_RETURN_TO_SENDER: uint64 mailbox; uint32 id; uint64 sender (unused, MailHandler.cpp:436-515)
            _world.Send(Op.CMSG_MAIL_RETURN_TO_SENDER, new PacketWriter().U64(_mailbox.MailboxGuid).U32(mailId).U64(m != null ? m.SenderGuid : 0UL));
        }

        public void DeleteMail(uint mailId)
        {
            MailView m = MailFind(mailId);
            if (_world == null || _mailbox == null) return;
            // CMSG_MAIL_DELETE: uint64 mailbox; uint32 id; uint32 templateId (MailHandler.cpp:406-434; a C.O.D. mail cannot be deleted)
            _world.Send(Op.CMSG_MAIL_DELETE, new PacketWriter().U64(_mailbox.MailboxGuid).U32(mailId).U32(m != null ? (uint)m.TemplateId : 0u));
        }

        public void CopyMailText(uint mailId)
        {
            if (_world == null || _mailbox == null) return;
            _world.Send(Op.CMSG_MAIL_CREATE_TEXT_ITEM, new PacketWriter().U64(_mailbox.MailboxGuid).U32(mailId));      // MailHandler.cpp:824-888
        }

        // ------------------------------------------------------------------ the send draft

        /// <summary>The bag item at WoW's inventory position (bag 255 + slot 23..38 the backpack, 19..22 + slot a bag) joins the draft.
        /// Refused like the server's first per-item checks (MailHandler.cpp:249-259): a non-empty bag, a soulbound item.</summary>
        public bool AttachMailItem(int bag, int slot)
        {
            if (_mailbox == null) { SocLogOnce("attachclosed", "AttachMailItem with no mailbox open - ignored"); return false; }
            ulong g = 0;
            foreach (var bp in _bagPos) if (bp.Bag == bag && bp.Slot == slot) { g = bp.Guid; break; }
            if (g == 0) { SocLogOnce("attachnone", "AttachMailItem(" + bag + ", " + slot + ") - no item there"); return false; }
            foreach (var a in _mailAttach) if (a.Guid == g) return false;                // already in the draft
            if (_mailAttach.Count >= MailMaxItems) { SocText(ErrMailAttachmentSlot, true); return false; }
            OnlineObject o;
            if (_objects.TryGetValue(g, out o))
            {
                if (o.TypeId == TypeId.Container)
                {
                    int n = Math.Min(36, Math.Max(0, o.I32(UF.CONTAINER_FIELD_NUM_SLOTS)));
                    for (int i = 0; i < n; i++)
                        if (o.U64(UF.CONTAINER_FIELD_SLOT_1 + i * 2) != 0) { SocText(InventoryResultText(31, 0), true); return false; }   // EQUIP_ERR_CAN_ONLY_DO_WITH_EMPTY_BAGS
                }
                if ((o.U32(UF.ITEM_FIELD_FLAGS) & ItemFieldFlagSoulbound) != 0) { SocText(InventoryResultText(72, 0), true); return false; }   // EQUIP_ERR_MAIL_BOUND_ITEM
            }
            OwnedView ov;
            ItemView v = _ownedViews.TryGetValue(g, out ov) ? ov.View : null;
            _mailAttach.Add(new MailAttach { Guid = g, Bag = (byte)bag, Slot = (byte)slot });
            _mailbox.Attachments.Add(v ?? new ItemView { Name = SocUnknown, Bag = bag, Slot = slot });
            MailDirty();
            return true;
        }

        public void DetachMailItem(int attachmentIndex)
        {
            if (_mailbox == null || attachmentIndex < 0 || attachmentIndex >= _mailAttach.Count) return;
            _mailAttach.RemoveAt(attachmentIndex);
            if (attachmentIndex < _mailbox.Attachments.Count) _mailbox.Attachments.RemoveAt(attachmentIndex);
            MailDirty();
        }

        /// <summary>Drops draft items that left their bag position (moved, used, sold).</summary>
        void MailCheckDraft()
        {
            if (_mailAttach.Count == 0) return;
            bool changed = false;
            for (int i = _mailAttach.Count - 1; i >= 0; i--)
            {
                MailAttach a = _mailAttach[i];
                bool there = false;
                foreach (var bp in _bagPos) if (bp.Guid == a.Guid && bp.Bag == a.Bag && bp.Slot == a.Slot) { there = true; break; }
                if (there) continue;
                _mailAttach.RemoveAt(i);
                if (i < _mailbox.Attachments.Count) _mailbox.Attachments.RemoveAt(i);
                changed = true;
            }
            if (changed) MailDirty();
        }

        /// <summary>CMSG_SEND_MAIL (MailHandler.cpp:65-120): uint64 mailbox; CString to; CString subject; CString body; uint32 stationery 41;
        /// uint32 0; uint8 n; n x (uint8 slot, uint64 item); uint32 money; uint32 cod; uint64 0; uint8 0.</summary>
        public void SendMail(string to, string subject, string body, int money, int cod)
        {
            if (_world == null || _mailbox == null) return;
            string t = SocClean(to, SocNameMax).Trim();
            if (t.Length == 0) return;
            var w = new PacketWriter().U64(_mailbox.MailboxGuid).CString(t).CString(MailClean(subject, MailSubjectMax)).CString(MailClean(body, MailBodyMax))
                .U32(MailStationeryDefault).U32(0).U8(_mailAttach.Count);
            for (int i = 0; i < _mailAttach.Count; i++) w.U8(i).U64(_mailAttach[i].Guid);
            w.U32((uint)Math.Max(0, money)).U32((uint)Math.Max(0, cod)).U64(0).U8(0);
            _world.Send(Op.CMSG_SEND_MAIL, w);
            _mailbox.LastSendOk = null;
            WowLog.Info("Online mail: mail sent to the server (" + _mailAttach.Count + " item(s), money " + Math.Max(0, money) + ", C.O.D. " + Math.Max(0, cod) + ")");
        }

        // ------------------------------------------------------------------ answers

        static string MailResultText(int result)
        {
            switch (result)
            {
                case 2: return ErrMailToSelf;
                case 3: return ErrNotEnoughMoneyMail;
                case 4: return ErrMailTargetNotFound;
                case 5: return ErrPlayerWrongFaction;
                case 6: return ErrMailDatabaseError;
                case 15: return ErrMailReachedCap;
                case 16: return ErrMailWrappedCod;
                case 18: return ErrMailTooManyAttachments;
                case 19: return ErrMailInvalidAttachment;
                case 21: return ErrMailAttachmentExpired;
                default: return null;                                                        // 14 trial, 17 suspended: no 3.3.5 text
            }
        }

        void MailOnResult(WowPacket p)
        {
            uint id, itemLow;
            int action, result, equipError, count;
            SocialRead.MailResult(p, out id, out action, out result, out equipError, out itemLow, out count);
            _mailResults++;
            bool takeStep = _mailTakeBusy && id == _mailTakeMail && (action == 1 || action == 2);
            if (result == 0)
            {
                MailView m = MailFind(id);
                switch (action)
                {
                    case 0:                                                                  // MAIL_SEND
                        SocText(ErrMailSent);
                        _mailAttach.Clear();
                        if (_mailbox != null) { _mailbox.Attachments.Clear(); _mailbox.LastSendOk = true; }
                        break;
                    case 1: if (m != null) m.Money = 0; break;                              // MONEY_TAKEN (the money line comes from the money update)
                    case 2: if (m != null) { m.Items.RemoveAll(x => x.ItemGuidLow == itemLow); m.Cod = 0; } break;   // ITEM_TAKEN
                    case 3:
                    case 4: if (_mailbox != null) _mailbox.Mails.RemoveAll(x => x.Id == id); break;   // RETURNED / DELETED
                    case 5: if (m != null) m.Flags |= 4; break;                              // MADE_PERMANENT: MAIL_CHECK_MASK_COPIED
                }
                if (takeStep) MailTakeNext();
                else if (action != 0) RefreshMailbox();
            }
            else
            {
                string text = result == 1 ? InventoryResultText(equipError, 0) : MailResultText(result);
                if (text != null) SocText(text, true);
                else SocLogOnce("mailresult" + result, "SMSG_SEND_MAIL_RESULT action " + action + " result " + result + " - no 3.3.5 text");
                if (action == 0 && _mailbox != null) _mailbox.LastSendOk = false;
                if (takeStep) { _mailTakeQueue.Clear(); _mailTakeBusy = false; }
            }
            MailDirty();
        }

        /// <summary>SMSG_RECEIVED_MAIL (Player.cpp:2971-2984): uint32 0 - new mail was delivered.</summary>
        void MailOnReceived(WowPacket p)
        {
            _mailArrived = true;
            _world.Send(Op.MSG_QUERY_NEXT_MAIL_TIME);
            _mailPollAt = _now + MailPollEvery;
            if (_mailbox != null) RefreshMailbox();
        }

        void MailOnNextTime(WowPacket p)
        {
            bool unread = SocialRead.NextMailTime(p, _mailSenders, _mailEntries, _mailTypes);
            bool arrived = _mailArrived;
            _mailArrived = false;
            _social.HasUnreadMail = unread;
            _social.UnreadMailFrom.Clear();
            int gen = ++_mailFromGen;
            for (int i = 0; i < _mailSenders.Count; i++)
            {
                if (_mailTypes[i] == 0 && _mailSenders[i] != 0)
                    SocWaitName(_mailSenders[i], n =>
                    {
                        if (gen != _mailFromGen || _social.UnreadMailFrom.Contains(n)) return;
                        _social.UnreadMailFrom.Add(n);
                        SocChanged(SocialPart.Mail);
                    });
                else
                {
                    bool wait;
                    string n = MailSenderName(_mailTypes[i], _mailEntries[i], out wait) ?? SocUnknown;
                    if (!_social.UnreadMailFrom.Contains(n)) _social.UnreadMailFrom.Add(n);
                }
            }
            SocChanged(SocialPart.Mail);
            if (arrived && unread)
            {
                _events.Enqueue(new GameEvent { Type = GameEventType.NewMail, Text = HaveMail });
                SocText(HaveMail);
            }
        }
    }
}
