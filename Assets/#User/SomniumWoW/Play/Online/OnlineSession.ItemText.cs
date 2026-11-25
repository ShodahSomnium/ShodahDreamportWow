// iteration 11 - owner npc (proposal-npc.md 3.8): the ItemTextFrame - readable items (CMSG_READ_ITEM -> SMSG_READ_ITEM_OK -> CMSG_PAGE_TEXT_QUERY
// -> one SMSG_PAGE_TEXT_QUERY_RESPONSE per page of the chain), letters (ITEM_FIELD_FLAG_READABLE: CMSG_ITEM_TEXT_QUERY ->
// SMSG_ITEM_TEXT_QUERY_RESPONSE), books / plaques (game object type 9 TEXT, opened locally from its data0 page: AC GameObject::Use has no
// TEXT case, GameObject.cpp:1514-2066) and page goobers (CMSG_GAMEOBJ_USE -> SMSG_GAMEOBJECT_PAGETEXT, GameObject.cpp:1646-1650).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        // ---- AzerothCore / client values
        const uint ItemFieldFlagReadable = 0x200;       // ITEM_FIELD_FLAG_READABLE (ItemTemplate.h:118 "Opens text page when right clicked")
        const int GoText = 9, GoGoober = 10;            // GameobjectTypes; TEXT data0 pageID / data2 pageMaterial, GOOBER data7 pageId / data9 pageMaterial (GameObjectData.h:154-156, :169-171)
        const string ItxDefaultMaterial = "Parchment";  // ItemTextFrame.lua:57-68: no material = "Parchment"

        // ---- ours (named here, logged once in InitItemText)
        const int ItxMaxPages = 100;                    // a page chain longer than this is cut (a guard against a looping chain)
        const double ItxAnswerSeconds = 5.0;            // SMSG_READ_ITEM_OK / ITEM_TEXT / GAMEOBJECT_PAGETEXT later than this after our request is ignored

        sealed class ItxPage { public string Text; public int Next; }
        ItemTextView _itemText;
        readonly Dictionary<int, ItxPage> _itxPages = new Dictionary<int, ItxPage>();

        int _itxFirstPage, _itxPageIndex;               // the open text's chain start and the shown page (0-based)
        // the request waiting for its answer (an item read / a letter / a goober use)
        ulong _itxWaitGuid; int _itxWaitPage; string _itxWaitTitle, _itxWaitMaterial; double _itxWaitAt = -100;
        bool _itxMaterialTried;
        DbcTable _itxMaterialDbc;

        partial void InitItemText()
        {
            if (_world == null) return;
            _world.On(Op.SMSG_READ_ITEM_OK, OnItxReadOk, this);
            _world.On(Op.SMSG_READ_ITEM_FAILED, OnItxReadFailed, this);
            _world.On(Op.SMSG_PAGE_TEXT_QUERY_RESPONSE, OnItxPage, this);
            _world.On(Op.SMSG_ITEM_TEXT_QUERY_RESPONSE, OnItxItemText, this);
            _world.On(Op.SMSG_GAMEOBJECT_PAGETEXT, OnItxGoPage, this);
            WowLog.Info("Online item text: ours - page chains cut at " + ItxMaxPages + " pages, an answer counts " + ItxAnswerSeconds + " s after our request");
        }

        public ItemTextView CurrentItemText { get { return _itemText; } }

        public void CloseItemText() { ItxClose("closed by the player"); }   // WoW's CloseItemText sends nothing

        void ItxClose(string why)
        {
            if (_itemText == null) return;
            WowLog.Info("Online item text: '" + _itemText.Title + "' closes - " + why);
            _itemText = null;
        }

        /// <summary>+1 next page, -1 previous page (ItemTextFrame's Prev / Next buttons; the server sent the whole chain already).</summary>
        public void ItemTextPage(int delta)
        {
            var v = _itemText;
            if (v == null || delta == 0 || _itxFirstPage <= 0) return;
            var chain = ItxChain(_itxFirstPage);                                           // never empty: the first page is always in it
            int i = Math.Max(0, Math.Min(_itxPageIndex + Math.Sign(delta), chain.Count - 1));
            if (i == _itxPageIndex) return;
            _itxPageIndex = i;
            if (!_itxPages.ContainsKey(chain[i])) ItxAsk(chain[i], v.SourceGuid);          // not arrived (yet): ask for it
            ItxShow();
        }

        // ------------------------------------------------------------------ the requests (Items.cs UseItemAt, quests' QUseGameObject)

        /// <summary>CMSG_READ_ITEM (ReadItem::Read: uint8 bag, uint8 slot; ItemHandler.cpp:548-576): the server answers SMSG_READ_ITEM_OK (uint64
        /// item) when CanUseItem allows it, else SMSG_READ_ITEM_FAILED (uint64 item) + an equip error.</summary>
        void ItxReadItem(int bag, int slot, ulong guid, int pageId, int material, string title)
        {
            _world.Send(Op.CMSG_READ_ITEM, new PacketWriter().U8(bag).U8(slot));
            ItxWait(guid, pageId, ItxMaterialName(material), title);
            WowLog.Info("Online item text: CMSG_READ_ITEM bag " + bag + " slot " + slot + " ('" + title + "', page " + pageId + ")");
        }

        /// <summary>CMSG_ITEM_TEXT_QUERY (ItemHandler.cpp:1461-1482: uint64 item) for an item with ITEM_FIELD_FLAG_READABLE (a letter).</summary>
        void ItxQueryItemText(ulong guid, string title)
        {
            _world.Send(Op.CMSG_ITEM_TEXT_QUERY, new PacketWriter().U64(guid));
            ItxWait(guid, 0, ItxDefaultMaterial, title);
            WowLog.Info("Online item text: CMSG_ITEM_TEXT_QUERY for '" + title + "'");
        }

        void ItxWait(ulong guid, int page, string material, string title)
        {
            _itxWaitGuid = guid; _itxWaitPage = page; _itxWaitMaterial = material; _itxWaitTitle = title; _itxWaitAt = _now;
        }

        bool ItxWaitingFor(ulong guid) { return guid != 0 && guid == _itxWaitGuid && _now - _itxWaitAt <= ItxAnswerSeconds; }

        /// <summary>quests' QUseGameObject case 9 (H3): a book / plaque. AC's GameObject::Use has no TEXT case (GameObject.cpp:2064-2066 logs an
        /// error), so it is opened locally from the object's data0 page with CMSG_PAGE_TEXT_QUERY (QueryHandler.cpp:361-397) and
        /// CMSG_GAMEOBJ_REPORT_USE (uint64; SpellHandler.cpp:350-376: only the achievement criterion) is sent. A goober with a page comes here
        /// from SMSG_GAMEOBJECT_PAGETEXT.</summary>
        partial void NpcReadObjectText(EntityState e)
        {
            if (e == null || _world == null) return;
            uint[] d;
            if (!_qGoData.TryGetValue(e.Entry, out d) || d == null || d.Length < 11)
            {
                WowLog.Info("Online item text: " + (e.Name ?? "?") + " (entry " + e.Entry + ") - its object data is not known yet, nothing to read");
                return;
            }
            int type = (int)d[0];
            int page = type == GoText ? (int)d[1] : type == GoGoober ? (int)d[1 + 7] : 0;
            int material = type == GoText ? (int)d[1 + 2] : type == GoGoober ? (int)d[1 + 9] : 0;
            if (page <= 0) { WowLog.Info("Online item text: " + (e.Name ?? "?") + " (type " + type + ") has no page"); return; }
            if (type == GoText) _world.Send(Op.CMSG_GAMEOBJ_REPORT_USE, new PacketWriter().U64(e.Guid));
            ItxOpen(e.Guid, e.Name ?? "", ItxMaterialName(material), page, null);
            WowLog.Info("Online item text: read " + (e.Name ?? "?") + " (type " + type + ", page " + page + ", material " + _itemText.Material + ")" + (type == GoText ? " + CMSG_GAMEOBJ_REPORT_USE" : ""));
        }

        // ------------------------------------------------------------------ server answers

        void OnItxReadOk(WowPacket p)
        {
            if (p.Remaining < 8) return;
            ulong g = p.U64();
            if (!ItxWaitingFor(g)) { WowLog.Info("Online item text: SMSG_READ_ITEM_OK for " + g.ToString("X16") + " not asked for - ignored"); return; }
            _itxWaitGuid = 0;
            ItxOpen(g, _itxWaitTitle, _itxWaitMaterial, _itxWaitPage, null);
        }

        void OnItxReadFailed(WowPacket p)
        {
            ulong g = p.Remaining >= 8 ? p.U64() : 0;
            if (g == _itxWaitGuid) _itxWaitGuid = 0;
            WowLog.Info("Online item text: SMSG_READ_ITEM_FAILED for " + g.ToString("X16") + " (the reason comes as SMSG_INVENTORY_CHANGE_FAILURE)");
        }

        /// <summary>SMSG_PAGE_TEXT_QUERY_RESPONSE (QueryHandler.cpp:371-393): uint32 pageId, string text, uint32 nextPage ("Item page missing." + 0
        /// for an unknown page). The server sends the whole chain at once.</summary>
        void OnItxPage(WowPacket p)
        {
            if (p.Remaining < 5) return;
            int id = (int)p.U32();
            string text = p.CString();
            int next = p.Remaining >= 4 ? (int)p.U32() : 0;
            _itxPages[id] = new ItxPage { Text = text, Next = next };
            if (_itemText != null && ItxChain(_itxFirstPage).Contains(id)) ItxShow();
        }

        /// <summary>SMSG_ITEM_TEXT_QUERY_RESPONSE (ItemHandler.cpp:1468-1481): uint8 0, uint64 item, string text | uint8 1 (no text).</summary>
        void OnItxItemText(WowPacket p)
        {
            if (p.Remaining < 1) return;
            int none = p.U8();
            if (none != 0 || p.Remaining < 8) { if (_itxWaitPage == 0) _itxWaitGuid = 0; WowLog.Info("Online item text: the item has no text"); return; }
            ulong g = p.U64();
            string text = p.CString();
            if (!ItxWaitingFor(g)) { WowLog.Info("Online item text: SMSG_ITEM_TEXT_QUERY_RESPONSE for " + g.ToString("X16") + " not asked for - ignored"); return; }
            _itxWaitGuid = 0;
            ItxOpen(g, _itxWaitTitle, _itxWaitMaterial, 0, text);
        }

        /// <summary>SMSG_GAMEOBJECT_PAGETEXT (GameObject.cpp:1646-1650: uint64 guid) - a goober with a page answered CMSG_GAMEOBJ_USE.</summary>
        void OnItxGoPage(WowPacket p)
        {
            if (p.Remaining < 8) return;
            ulong g = p.U64();
            var e = Find(g);
            if (e == null) { WowLog.Info("Online item text: SMSG_GAMEOBJECT_PAGETEXT for an unknown object " + g.ToString("X16")); return; }
            NpcReadObjectText(e);
        }

        // ------------------------------------------------------------------ the view

        /// <summary>Opens the ItemTextFrame: a page chain from firstPage (asked when not cached) or one plain text (a letter).</summary>
        void ItxOpen(ulong source, string title, string material, int firstPage, string plainText)
        {
            NpcServiceOpened(source, "item text");
            _itemText = new ItemTextView { SourceGuid = source, Title = title ?? "", Material = material ?? ItxDefaultMaterial, Page = 1, Version = 1 };
            _itxFirstPage = firstPage;
            _itxPageIndex = 0;
            if (firstPage <= 0)
            {
                _itemText.Text = QPersonal(plainText ?? "");
                _itemText.Version++;
                return;
            }
            if (!_itxPages.ContainsKey(firstPage)) ItxAsk(firstPage, source);
            ItxShow();
        }

        void ItxAsk(int page, ulong guid)
        {
            if (page <= 0 || _world == null) return;
            _world.Send(Op.CMSG_PAGE_TEXT_QUERY, new PacketWriter().U32((uint)page).U64(guid));   // `recvData >> pageID; recvData.read_skip<uint64>();`

        }

        /// <summary>The page ids of the chain from first, as far as they are known (cycle- and length-guarded).</summary>
        List<int> ItxChain(int first)
        {
            var chain = new List<int>();
            int id = first;
            while (id > 0 && chain.Count < ItxMaxPages && !chain.Contains(id))
            {
                chain.Add(id);
                ItxPage pg;
                if (!_itxPages.TryGetValue(id, out pg)) break;
                id = pg.Next;
            }
            return chain;
        }

        void ItxShow()
        {
            var v = _itemText;
            if (v == null || _itxFirstPage <= 0) return;
            var chain = ItxChain(_itxFirstPage);
            if (_itxPageIndex >= chain.Count) _itxPageIndex = Math.Max(0, chain.Count - 1);
            int id = chain.Count > 0 ? chain[_itxPageIndex] : _itxFirstPage;
            ItxPage pg;
            bool known = _itxPages.TryGetValue(id, out pg);
            string text = known ? QPersonal(pg.Text ?? "") : null;
            bool next = known && pg.Next != 0 && _itxPageIndex + 1 < ItxMaxPages;
            bool prev = _itxPageIndex > 0;
            if (v.Text == text && v.Page == _itxPageIndex + 1 && v.HasNext == next && v.HasPrevious == prev) return;
            v.Text = text;
            v.Page = _itxPageIndex + 1;
            v.HasNext = next;
            v.HasPrevious = prev;
            v.Version++;
        }

        /// <summary>PageTextMaterial.dbc (ID, Name; measured 1 Parchment, 2 Stone, 3 Marble, 4 Silver, 5 Bronze, 6 Valentine, 7 Illidan).</summary>
        string ItxMaterialName(int material)
        {
            if (material <= 0) return ItxDefaultMaterial;
            if (!_itxMaterialTried && _files != null)
            {
                _itxMaterialTried = true;
                _itxMaterialDbc = Dbc.Load(_files, "PageTextMaterial");
                if (_itxMaterialDbc != null && _itxMaterialDbc.FieldCount < 2) _itxMaterialDbc = null;
                WowLog.Info("Online item text: PageTextMaterial.dbc " + (_itxMaterialDbc != null ? _itxMaterialDbc.RecordCount + " rows, " + _itxMaterialDbc.FieldCount + " fields" : "missing"));
            }
            if (_itxMaterialDbc == null) return ItxDefaultMaterial;
            int r = _itxMaterialDbc.FindRow(material);
            string n = r >= 0 ? _itxMaterialDbc.GetString(r, 1) : null;
            return string.IsNullOrEmpty(n) ? ItxDefaultMaterial : n;
        }
    }
}
