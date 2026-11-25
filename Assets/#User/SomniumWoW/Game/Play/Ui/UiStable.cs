// iteration 11 - owner pets (proposal-pets.md 4.3 / 6.4): WoW's PetStableFrame as a uicore session window (UiWindowKind.Stable 44): open
// while Player.Stable != null (MSG_LIST_STABLED_PETS online; offline there are no stables). The current pet + the 4 stable slots
// (NUM_PET_STABLE_SLOTS 4 = AzerothCore MAX_PET_STABLES, PetDefines.h:37), "Stable" / "Take" (ours - WoW drags pets between slots),
// "Purchase" with the next slot's price (StableSlotPrices.dbc) and WoW's CONFIRM_BUY_STABLE_SLOT popup. No 3D model view (gap): the
// selected pet's family icon is drawn in the model area instead.
using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiStable : UiWindow
    {
        public const string ArtPrefix = @"Interface\PetStableFrame\UI-PetStable-";   // TopLeft 256x256, TopRight 128x256, BottomLeft / BottomRight (PetStable.xml:102-133)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Stable, Frame = "PetStableFrame", TitleKey = "STABLES", Title = "Stables",   // GlobalStrings.lua:7334
                Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,                     // UIParent.lua:43 (left, pushable 0)
                Wanted = s => s != null && s.Player != null && s.Player.Stable != null,
                NearGuid = s => s != null && s.Player != null && s.Player.Stable != null ? s.Player.Stable.NpcGuid : 0,
                Closed = s => { if (s != null) s.CloseStable(); },
                Owner = "pets", Hidden = false,
                Build = (parent, ctx, close) => new UiStable(parent, ctx, close)
            };
        }

        private readonly UiButton[] _slot = new UiButton[5];       // 0 = current, 1..4 stabled
        private readonly RawImage[] _slotIcon = new RawImage[5];
        private readonly string[] _slotPath = new string[5];
        private readonly StablePetView[] _pets = new StablePetView[5];
        private readonly RawImage _portrait, _bigIcon, _happy;
        private readonly TextMeshProUGUI _info, _slotText, _cost;
        private readonly UiButton _stable, _take, _buy;
        private readonly UiMoney _money;
        private int _selected = -1, _counter = -1;
        private string _portraitPath, _bigPath;
        private static bool _logged;

        public UiStable(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "PetStableFrame", 0f, 0f, W, H);
            UiKit.Blocker(Root);
            UiKit.Frame4(Root, "Art", ArtPrefix + "TopLeft.blp", ArtPrefix + "TopRight.blp", ArtPrefix + "BottomLeft.blp", ArtPrefix + "BottomRight.blp");
            _portrait = Portrait(null);                                     // 60x60 at (7, -6) (PetStable.xml:88-96): the current pet's family icon
            _portrait.enabled = false;
            Title(UiGlobalStrings.G("STABLES", "Stables"), 18f);
            UiKit.CloseButton(Root, 324f, 9f, close);
            // the model view's area (313x223 at (23, -76), PetStable.xml:174-235): the selected pet's family icon + its info (ours)
            _bigIcon = UiKit.Pic(Root, "Pet icon", null, 23f + 156f - 32f, 76f + 50f, 64f, 64f);
            _bigIcon.enabled = false;
            _info = UiKit.Text(Root, "PetStableLevelText", "", 12f, Color.white, TextAlignmentOptions.Top, 23f, 76f + 124f, 313f, 60f);
            _happy = UiKit.Pic(Root, "PetStablePetInfo happiness", UiArt.Tex(UiPetFrame.Happiness), 23f + 156f + 36f, 76f + 50f, 24f, 23f);
            _happy.enabled = false;
            // PetStableCurrentPet BOTTOMLEFT (45, 150); StabledPet1 LEFT = CurrentPet RIGHT + (53, 0), 2..4 +15 each (PetStable.xml), 37x37
            float y = 512f - 150f - 37f;
            UiKit.Text(Root, "CURRENT_PET", UiGlobalStrings.G("CURRENT_PET", "Current Pet:"), 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 36f, y - 16f, 90f, 14f);
            UiKit.Text(Root, "STABLED_PETS", UiGlobalStrings.G("STABLED_PETS", "Stabled Pets:"), 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 45f + 37f + 53f, y - 16f, 200f, 14f);
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                float x = i == 0 ? 45f : 45f + 37f + 53f + (i - 1) * (37f + 15f);
                UiKit.Pic(Root, "Slot " + i + " background", UiArt.Tex(UiArt.EmptySlot), x - 13f, y - 13f, 64f, 64f);   // UI-EmptySlot 64x64 at (-13, 13)
                UiButton b = UiKit.Slot(Root, i == 0 ? "PetStableCurrentPet" : "PetStableStabledPet" + i, x, y, 37f, true);
                b.Checked = UiKit.Pic(b.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 37f, 37f);
                b.Checked.enabled = false;
                b.Click = () => Select(k);
                b.Tip = () => SlotTip(k);
                _slotIcon[i] = b.Icon;
                _slot[i] = b;
            }
            _stable = UiKit.PanelButton(Root, "Stable", 45f, y + 52f, 90f, 22f, () => { if (Ctx.S != null) Ctx.S.StablePet(); });          // ours
            _take = UiKit.PanelButton(Root, "Take", 45f + 96f, y + 52f, 90f, 22f, () => Take());                                          // ours
            _slotText = UiKit.Text(Root, "PetStableSlotText", UiGlobalStrings.G("STABLE_SLOT_TEXT", "Do you wish to purchase another stable slot?"), 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 36f, 420f, 200f, 30f);
            _cost = UiKit.Text(Root, "COSTS_LABEL", UiGlobalStrings.G("COSTS_LABEL", "Cost:"), 10f, UiArt.Gold, TextAlignmentOptions.Left, 36f, 452f, 40f, 14f);
            _money = new UiMoney(Root, 200f, 452f, Color.white);
            _buy = UiKit.PanelButton(Root, UiGlobalStrings.G("PURCHASE", "Purchase"), 240f, 420f, 90f, 22f, AskBuy);
            Root.gameObject.SetActive(false);
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI: Stables - ours: Stable / Take buttons (WoW drags pets between slots), the family icon instead of the 3D model, the info text in the model area");
            }
        }

        private void Select(int k)
        {
            if (_pets[k] == null) { _selected = -1; return; }
            _selected = k;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
        }

        private void Take()
        {
            StablePetView sp = _selected >= 1 ? _pets[_selected] : null;
            if (sp == null || Ctx.S == null) { Ctx.Say("Select a stabled pet first.", UiArt.InfoYellow); return; }   // ours
            Ctx.S.UnstablePet(sp.Number);
        }

        private void AskBuy()
        {
            IWowSession s = Ctx.S;
            StableView st = s != null ? s.Player.Stable : null;
            if (st == null || st.SlotsBought >= 4) return;
            // StaticPopupDialogs["CONFIRM_BUY_STABLE_SLOT"] (StaticPopup.lua:2725-2735): YES / NO, the money of GetNextStableSlotCost
            UiPopup.Show(new UiPopupSpec
            {
                Which = "CONFIRM_BUY_STABLE_SLOT", Text = UiGlobalStrings.G("CONFIRM_BUY_STABLE_SLOT", "Are you sure you wish to purchase a new stable slot for the following amount?"),
                Button1 = UiGlobalStrings.G("YES", "Yes"), Button2 = UiGlobalStrings.G("NO", "No"), Money = st.NextSlotPrice, OnAccept = () => s.BuyStableSlot(),
                KeepOpen = () => s.Player.Stable != null
            });
        }

        private string Info(StablePetView sp)
        {
            // STABLE_PET_INFO_TOOLTIP_TEXT "Level %1$d %2$s|n%3$s" (GS 7336) - the 3rd line (the pet's loyalty / talent text) is not sent by the server: left out
            return sp.Name + "\n" + UiGlobalStrings.G("LEVEL", "Level") + " " + sp.Level + (string.IsNullOrEmpty(sp.FamilyName) ? "" : " " + sp.FamilyName);
        }

        private string SlotTip(int k)
        {
            StablePetView sp = _pets[k];
            StableView st = Ctx.P != null ? Ctx.P.Stable : null;
            if (sp != null) return "<size=14>" + Info(sp).Replace("\n", "</size>\n");
            if (k == 0) return UiGlobalStrings.G("CURRENT_PET", "Current Pet:");
            if (st != null && k > st.SlotsBought) return UiGlobalStrings.G("STABLE_SLOT_TEXT", "Do you wish to purchase another stable slot?");
            return UiGlobalStrings.G("EMPTY_STABLE_SLOT", "Empty Stable Slot");
        }

        public override void Refresh(float now)
        {
            if (!IsOpen) return;
            PlayerState p = Ctx.P;
            StableView st = p != null ? p.Stable : null;
            if (st == null) return;
            if (st.Counter != _counter)
            {
                _counter = st.Counter;
                for (int i = 0; i < 5; i++) _pets[i] = null;
                foreach (StablePetView sp in st.Pets) if (sp.Slot >= 0 && sp.Slot <= 4) _pets[sp.Slot] = sp;
                if (_selected >= 0 && _pets[_selected] == null) _selected = -1;
                if (_selected < 0) _selected = _pets[0] != null ? 0 : -1;
                for (int i = 1; i < 5; i++) _slot[i].Interactable = i <= st.SlotsBought;
                _buy.Interactable = st.SlotsBought < 4 && st.NextSlotPrice > 0;   // PetStable.lua:199-210
                _money.Set(st.NextSlotPrice);
                bool more = st.SlotsBought < 4;
                _slotText.gameObject.SetActive(more);
                _cost.gameObject.SetActive(more);
                _buy.gameObject.SetActive(more);
            }
            for (int i = 0; i < 5; i++)
            {
                StablePetView sp = _pets[i];
                string path = sp != null ? sp.IconPath : null;
                if (!ReferenceEquals(path, _slotPath[i]))
                {
                    Texture2D t = path != null ? UiArt.Icon(path) : null;
                    if (path == null || t != null)
                    {
                        _slotPath[i] = path;
                        _slotIcon[i].texture = t;
                        _slotIcon[i].enabled = t != null;
                        if (t != null) UiKit.Coords(_slotIcon[i], 0f, 1f, 0f, 1f);
                    }
                }
                _slot[i].Checked.enabled = i == _selected && _slot[i].Checked.texture != null;
            }
            StablePetView cur = _pets[0];
            SetPic(_portrait, cur != null ? cur.IconPath : null, ref _portraitPath);
            StablePetView sel = _selected >= 0 ? _pets[_selected] : null;
            SetPic(_bigIcon, sel != null ? sel.IconPath : null, ref _bigPath);
            string info = sel != null ? Info(sel) : "";
            if (_info.text != info) _info.text = info;
            PetInfoView pi = p.PetInfo;
            int h = sel != null && sel.Current && pi != null && pi.IsHunterPet && pi.Number == sel.Number ? pi.Happiness : 0;   // happiness: the current pet only
            _happy.enabled = h >= 1 && h <= 3 && _happy.texture != null;
            if (_happy.enabled) { float l = h == 1 ? 0.375f : h == 2 ? 0.1875f : 0f; UiKit.Coords(_happy, l, l + 0.1875f, 0f, 0.359375f); }
            _stable.Interactable = cur != null && st.SlotsBought > 0;
            _take.Interactable = _selected >= 1;
        }

        private static void SetPic(RawImage img, string path, ref string shown)
        {
            if (ReferenceEquals(path, shown)) return;
            Texture2D t = path != null ? UiArt.Icon(path) : null;
            if (path != null && t == null) return;   // loading
            shown = path;
            img.texture = t;
            img.enabled = t != null;
            if (t != null) UiKit.Coords(img, 0f, 1f, 0f, 1f);
        }
    }
}
