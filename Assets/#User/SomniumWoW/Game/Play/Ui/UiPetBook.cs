// iteration 11 - owner pets (proposal-pets.md 6.4): WoW's SpellBookFrame pet tab ("Pet" / "Demon") as its own registry window (UiWindowKind
// PetBook 17): the controlled unit's spellbook (Player.ControlBar.Spells - online the server's list for a permanent pet, offline the imp /
// voidwalker's). Layout = UiSpellbook's (SpellBookFrame.xml: 12 buttons, (34, 85) + column 157 / row 50). Left click = cast (a place spell
// opens spellfx's ground circle), right click = autocast (SpellBookFrame.lua:347-348), click the name = pick it then click pet bar button
// 4..7 (or drag the icon onto it, desktop) = put it there (CMSG_PET_SET_ACTION). Opened by the spellbook's Pet / Demon tab and the palm.
using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Pets;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiPetBook : UiWindow
    {
        /// <summary>The cursor payload's Source of a pet-book drag (only pet bar buttons 4..7 accept it).</summary>
        public static readonly object DragTag = new object();
        /// <summary>The spell picked by a click on its name (0 none): the next click on pet bar button 4..7 places it.</summary>
        public static int PickedSpell { get; private set; }
        public static void ClearPick() { PickedSpell = 0; }

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.PetBook, Frame = "SpellBookFrame", TitleKey = "PET_TYPE_PET", Title = "Pet",   // GlobalStrings.lua:5651
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, Binding = "TOGGLEPETBOOK",   // UIParent.lua:22; no key: Shift+I is the Somnium client key (proposal-uicore 5.4.8)
                Palm = true, Owner = "pets", Hidden = false,
                Build = (parent, ctx, close) => new UiPetBook(parent, ctx, close)
            };
        }

        private readonly Action _close;
        private readonly UiButton[] _icon = new UiButton[12];
        private readonly UiButton[] _nameBtn = new UiButton[12];
        private readonly TextMeshProUGUI[] _name = new TextMeshProUGUI[12];
        private readonly TextMeshProUGUI[] _rank = new TextMeshProUGUI[12];
        private readonly UiControlButton[] _slot = new UiControlButton[12];
        private readonly TextMeshProUGUI _title, _page;
        private readonly UiButton _prev, _next;
        private int _pageIndex, _shownSig = -1;
        private ControlBarView _bar;

        public UiPetBook(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "SpellBookFrame (pet)", 0f, 0f, W, H);
            UiKit.Blocker(Root);
            UiKit.Frame4(Root, "Art", UiArt.SpellbookPanel + "TopLeft.blp", UiArt.SpellbookPanel + "TopRight.blp", UiArt.SpellbookPanel + "BotLeft.blp", UiArt.SpellbookPanel + "BotRight.blp");
            Portrait(UiArt.Tex(UiArt.SpellbookIcon));
            _title = Title(UiGlobalStrings.G("PET_TYPE_PET", "Pet"), 18f);
            UiKit.CloseButton(Root, 324f, 9f, close);
            for (int k = 0; k < 12; k++)
            {
                int i = k;
                float bx = 34f + (k / 6) * 157f, by = 85f + (k % 6) * 50f;
                _slot[k] = new UiControlButton(Root, "Pet spell " + (k + 1), bx, by, 37f);
                _icon[k] = _slot[k].B;
                _icon[k].Click = () => Cast(i);
                _icon[k].RightClick = () => Autocast(i);
                _icon[k].LongPress = () => Autocast(i);
                _icon[k].Tip = () => Tip(i);
                UiKit.Draggable(_icon[k], () => Payload(i), false);
                _nameBtn[k] = UiKit.Button(Root, "Pet spell " + (k + 1) + " name", bx + 39f, by, 112f, 37f);
                _nameBtn[k].Hilite = UiKit.Pic(_nameBtn[k].transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 4f, 112f, 30f);
                _nameBtn[k].Hilite.enabled = false;
                _nameBtn[k].Click = () => Pick(i);
                _nameBtn[k].Tip = () => Tip(i);
                _name[k] = UiKit.Text(Root, "Pet spell " + (k + 1) + " text", "", 11f, UiArt.Gold, TextAlignmentOptions.TopLeft, bx + 41f, by + 1f, 108f, 24f);
                _rank[k] = UiKit.Text(Root, "Pet spell " + (k + 1) + " rank", "", 10f, new Color(0.30f, 0.18f, 0f), TextAlignmentOptions.TopLeft, bx + 41f, by + 23f, 108f, 13f, null, false);
                _icon[k].gameObject.SetActive(false);
                _nameBtn[k].gameObject.SetActive(false);
            }
            _page = UiKit.Text(Root, "Page", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 127f, 402f, 102f, 15f);
            _prev = UiKit.ArtButton(Root, "Prev page", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.ButtonHilightSquare, 34f, 391f, 32f, () => { _pageIndex--; _shownSig = -1; UiSound.Named("igAbiliityPageTurn"); });
            _next = UiKit.ArtButton(Root, "Next page", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.ButtonHilightSquare, 298f, 391f, 32f, () => { _pageIndex++; _shownSig = -1; UiSound.Named("igAbiliityPageTurn"); });
            Root.gameObject.SetActive(false);
        }

        private ControlSlotView At(int i)
        {
            int idx = _pageIndex * 12 + i;
            return _bar != null && idx >= 0 && idx < _bar.Spells.Count ? _bar.Spells[idx] : null;
        }

        private void Cast(int i)
        {
            ControlSlotView v = At(i);
            IWowSession s = Ctx.S;
            if (v == null || s == null || v.Passive) return;
            if (v.NeedsPlace) { UiPetRows.BeginPlace(s, v, ControlBarBuilder.SlotOf(_bar, v.Action), v.Action); return; }
            s.UsePetSpell(v.Action, 0);
        }

        private void Autocast(int i)
        {
            ControlSlotView v = At(i);
            if (v == null || Ctx.S == null || !v.Autocastable) return;
            Ctx.S.SetPetAutocast(v.Action, !v.Autocast);
        }

        private void Pick(int i)
        {
            ControlSlotView v = At(i);
            if (v == null || v.Passive) return;
            PickedSpell = v.Action;
            Ctx.Say("Click pet bar button 4, 5, 6 or 7 to place " + v.Name + ".", UiArt.InfoYellow);   // ours
        }

        private UiPickupPayload Payload(int i)
        {
            ControlSlotView v = At(i);
            if (v == null || v.Passive) return null;
            return new UiPickupPayload { Kind = UiPickupKind.Spell, Packed = 0, SpellId = v.Action, Name = v.Name, IconPath = v.IconPath, Source = DragTag };   // Packed 0: no player action bar takes it
        }

        private string Tip(int i)
        {
            ControlSlotView v = At(i);
            if (v == null) return null;
            string hint = Ctx.Vr ? "Tap to cast, hold to toggle autocast, tap the name to put it on the pet bar." : "Click to cast, right-click to toggle autocast; drag it (or click its name) onto pet bar button 4-7.";   // ours
            return UiSummons.Tip(v, "<color=#00ff00>" + hint + "</color>");
        }

        public override void Refresh(float now)
        {
            if (!IsOpen) return;
            PlayerState p = Ctx.P;
            _bar = p != null ? p.ControlBar : null;
            int count = _bar != null ? _bar.Spells.Count : 0;
            if (count == 0)
            {
                Ctx.Say("Your pet has no spells.", UiArt.InfoYellow);   // ours
                if (_close != null) _close();
                return;
            }
            int pages = Mathf.Max(1, (count + 11) / 12);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, pages - 1);
            int sig = _bar.Counter * 7919 + count * 31 + _pageIndex;
            if (sig != _shownSig)
            {
                _shownSig = sig;
                EntityState unit = Ctx.S != null ? Ctx.S.Find(_bar.UnitGuid) : null;
                bool demon = unit != null && unit.CreatureType == 3;   // CreatureType.dbc 3 Demon -> PET_TYPE_DEMON (GS 5650); ours: the token is inside Wow.exe
                _title.text = demon ? UiGlobalStrings.G("PET_TYPE_DEMON", "Demon") : UiGlobalStrings.G("PET_TYPE_PET", "Pet");
                SpellDb db = UiArt.Files != null ? SpellDb.Get(UiArt.Files) : null;
                for (int k = 0; k < 12; k++)
                {
                    ControlSlotView v = At(k);
                    bool on = v != null;
                    _icon[k].gameObject.SetActive(on);
                    _nameBtn[k].gameObject.SetActive(on);
                    _name[k].text = on ? v.Name : "";
                    SpellInfo si = on && db != null ? db.Find(v.Action) : null;
                    _rank[k].text = si != null ? (v.Passive ? "Passive" : si.Rank ?? "") : "";
                }
                _page.text = UiGlobalStrings.G("PAGE_NUMBER", "Page %d").Replace("%d", (_pageIndex + 1).ToString());   // GlobalStrings.lua:5562
                _prev.Interactable = _pageIndex > 0;
                _next.Interactable = _pageIndex < pages - 1;
            }
            for (int k = 0; k < 12; k++) if (_icon[k].gameObject.activeSelf) _slot[k].Show(At(k), now);
        }
    }
}
