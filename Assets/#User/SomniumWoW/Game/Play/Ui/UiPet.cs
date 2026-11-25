// Iteration 3 (pet frame, pet bar, totem frame); iteration 11 - owner pets (proposal-pets.md s6.4): the pet frame's happiness and PET menu
// (Rename / Abandon / Dismiss), the 10-slot pet bar of Player.ControlBar (pet / charm; both sessions fill it), the vehicle / possess bar
// (UiVehicleBar), the totem frame from Player.Totems (right-click / a second VR tap = destroy) and the shaman multicast row (UiMultiCastBar).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>The player's own pet (or charmed unit) read from the session every frame.</summary>
    public static class UiSummons
    {
        public const ulong PlayerGuid = 1;

        /// <summary>The unit of the pet frame: a charmed unit's bar (WoW's "pet" unit is the charm then), else our pet (Player.PetGuid).</summary>
        public static EntityState Pet(IWowSession s)
        {
            if (s == null || s.Player == null) return null;
            ControlBarView v = s.Player.ControlBar;
            if (v != null && v.Kind == ControlBarKind.Charm && v.UnitGuid != 0) { EntityState c = s.Find(v.UnitGuid); if (c != null) return c; }
            ulong g = s.Player.PetGuid;
            EntityState e = g != 0 ? s.Find(g) : null;
            return e != null && e.Kind != EntityKind.Player && e.Kind != EntityKind.GameObject ? e : null;
        }

        /// <summary>A control-bar button's tooltip (name, rank, description; autocast; why it is unusable).</summary>
        public static string Tip(ControlSlotView v, string extra)
        {
            if (v == null || v.Kind == ControlSlotKind.Empty) return null;
            string t = v.Tooltip ?? v.Name ?? "";
            int nl = t.IndexOf('\n');
            string head = "<size=14>" + (nl >= 0 ? t.Substring(0, nl) : t) + "</size>" + (nl >= 0 ? "\n<color=#ffd100>" + t.Substring(nl + 1) + "</color>" : "");
            if (v.Autocastable) head += v.Autocast ? "\n<color=#00ff00>Autocast on</color>" : "\n<color=#9d9d9d>Autocast off</color>";   // ours
            if (v.Passive) head += "\n<color=#9d9d9d>Passive</color>";
            if (!v.Usable && !string.IsNullOrEmpty(v.UnusableReason)) head += "\n<color=#ff2020>" + v.UnusableReason + "</color>";
            return head + (string.IsNullOrEmpty(extra) ? "" : "\n" + extra);
        }
    }

    /// <summary>One control-bar button: icon, checked (the active command / reaction / casting spell), the UI-AutoCastableOverlay ring
    /// (pulsing while autocast is on - ours; WoW's AutoCastShine sparkles are Lua-animated) and a cooldown sweep.</summary>
    internal sealed class UiControlButton
    {
        public UiButton B;
        public RawImage Auto;
        public Image Wipe;
        private string _path;
        private bool _loaded;

        public UiControlButton(Transform parent, string name, float x, float y, float size)
        {
            B = UiKit.Slot(parent, name, x, y, size, true);
            B.Checked = UiKit.Pic(B.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, size, size);
            B.Checked.enabled = false;
            float o = size * 58f / 30f;
            Auto = UiKit.Pic(B.transform, "Autocastable", UiArt.Tex(UiPetBar.AutoCastable), (size - o) * 0.5f, (size - o) * 0.5f, o, o);
            Auto.enabled = false;
            var wr = UiKit.Rect(B.transform, "Cooldown", 0f, 0f, size, size);
            Wipe = wr.gameObject.AddComponent<Image>();
            Wipe.sprite = UiArt.WhiteSprite;
            Wipe.type = Image.Type.Filled;
            Wipe.fillMethod = Image.FillMethod.Radial360;
            Wipe.fillOrigin = (int)Image.Origin360.Top;
            Wipe.fillClockwise = false;
            Wipe.color = new Color(0f, 0f, 0f, 0.6f);
            Wipe.raycastTarget = false;
            Wipe.fillAmount = 0f;
            if (UiKit.Over) Wipe.material = UiArt.OverUi;
        }

        public void Show(ControlSlotView v, float now)
        {
            bool has = v != null && v.Kind != ControlSlotKind.Empty;
            string path = has ? v.IconPath : null;
            if (!ReferenceEquals(path, _path)) { _path = path; _loaded = false; B.Icon.enabled = false; }
            if (has && !_loaded)
            {
                Texture2D t = UiArt.Icon(path);
                if (t != null) { B.Icon.texture = t; B.Icon.enabled = true; UiKit.Coords(B.Icon, 0f, 1f, 0f, 1f); _loaded = true; }
            }
            if (!has) { B.Icon.enabled = false; B.Checked.enabled = false; Auto.enabled = false; Wipe.fillAmount = 0f; return; }
            bool grey = !v.Usable || v.Passive;
            B.Icon.color = grey ? new Color(0.4f, 0.4f, 0.4f) : Color.white;              // ActionButton.lua: unusable = 0.4
            B.Checked.enabled = v.Active && B.Checked.texture != null;
            Auto.enabled = v.Autocastable && Auto.texture != null;
            if (Auto.enabled) Auto.color = v.Autocast ? new Color(1f, 1f, 1f, 0.65f + 0.35f * Mathf.Sin(now * 4f)) : new Color(1f, 1f, 1f, 0.35f);
            Wipe.fillAmount = v.CooldownRemaining > 0f && v.CooldownTotal > 0f ? Mathf.Clamp01(v.CooldownRemaining / v.CooldownTotal) : 0f;
        }
    }

    /// <summary>
    /// PetFrame (PetFrame.xml): 128x53, UI-SmallTargetingFrame 128x64 at (0, -2), portrait 37x37 at (7, -6) (TemporaryPortrait-Pet: no
    /// 3D portrait render), health 69x8 at (47, -22), mana 69x8 at (47, -29), PetName GameFontNormalSmall BOTTOMLEFT (52, 33).
    /// Click = target the pet; right click (desktop) / the small menu button (VR, ours) = WoW's PET menu (UnitPopup.lua:139, rules 795-809).
    /// PetFrameHappiness 24x23 LEFT = the frame's RIGHT + (-7, -4) (PetFrame.xml:240-250), UI-PetHappiness texcoords PetFrame.lua:152-157.
    /// </summary>
    public sealed class UiPetFrame
    {
        public const string Frame = @"Interface\TargetingFrame\UI-SmallTargetingFrame.blp";
        public const string PortraitPet = @"Interface\CharacterFrame\TemporaryPortrait-Pet.blp";
        public const string Happiness = @"Interface\PetPaperDollFrame\UI-PetHappiness.blp";
        private static readonly float[] HappyLeft = { 0.375f, 0.1875f, 0f };      // 1 unhappy, 2 content, 3 happy (PetFrame.lua:152-157)

        public RectTransform Root;
        private readonly UiContext _ctx;
        private readonly UiBar _hp, _mp;
        private readonly RawImage _portrait, _happy;
        private readonly TextMeshProUGUI _name, _hpText, _mpText;
        private ulong _guid;
        private int _h = -1, _hm = -1, _m = -1, _mm = -1, _happiness = -1;
        private bool _dead;
        private string _nameShown;
        private static readonly List<UiPetFrame> Frames = new List<UiPetFrame>();   // desktop + VR builds: the menu opens on the shown one

        public bool Shown { get { return Root.gameObject.activeSelf; } }

        public UiPetFrame(Transform parent, UiContext ctx, float x, float y)
        {
            _ctx = ctx;
            Root = UiKit.Rect(parent, "Pet Frame", x, y, 128f, 53f);
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.5f), 47f, 22f, 69f, 15f);
            _portrait = UiKit.Pic(Root, "Portrait", UiArt.Tex(PortraitPet), 7f, 6f, 37f, 37f);
            _hp = UiBar.Make(Root, "Health", 47f, 22f, 69f, 8f, UiArt.HealthGreen, false);
            _mp = UiBar.Make(Root, "Mana", 47f, 29f, 69f, 8f, UiArt.PowerColor(PowerType.Mana), false);
            UiKit.Pic(Root, "Frame", UiArt.Tex(Frame), 0f, 2f, 128f, 64f);
            _name = UiKit.Text(Root, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 52f, 6f, 74f, 14f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _hpText = UiKit.Text(Root, "Health Text", "", 7.5f, Color.white, TextAlignmentOptions.Center, 47f, 21f, 69f, 10f);
            _mpText = UiKit.Text(Root, "Mana Text", "", 7.5f, Color.white, TextAlignmentOptions.Center, 47f, 28f, 69f, 10f);
            UiButton b = UiKit.Button(Root, "Pet", 4f, 4f, 116f, 42f);
            b.Click = () =>
            {
                if (ctx.S != null && _guid != 0) ctx.S.SetTarget(_guid);   // WowArmUi plays the target sound on the change
            };
            b.RightClick = () => ShowMenu();
            b.Tip = Tip;
            _happy = UiKit.Pic(Root, "Happiness", UiArt.Tex(Happiness), 121f, 19f, 24f, 23f);
            _happy.enabled = false;
            UiButton hb = UiKit.Button(Root, "Happiness tip", 121f, 19f, 24f, 23f);
            hb.Tip = () => _happiness >= 1 && _happiness <= 3 ? UiGlobalStrings.G("PET_HAPPINESS" + _happiness, _happiness == 1 ? "Unhappy" : _happiness == 2 ? "Content" : "Happy") : null;
            if (ctx.Vr)
            {
                // VR has no right click: a small menu button at the frame's top-right opens the same menu (ours, 16x16).
                UiButton mb = UiKit.ArtButton(Root, "Pet menu", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.ButtonHilightSquare, 104f, 0f, 16f, () => ShowMenu());
                mb.Tip = () => "Pet menu";   // ours
            }
            Frames.RemoveAll(f => f.Root == null);   // frames of a closed session (Unity-destroyed)
            Frames.Add(this);
            UiGroup.PetMenu = g => { foreach (UiPetFrame f in Frames) if (f.Root != null && f.Root.gameObject.activeInHierarchy) { f.ShowMenu(); return; } };   // interact's hook: our pet's unit menu = the PET menu
            Root.gameObject.SetActive(false);
        }

        private string Tip()
        {
            IWowSession s = _ctx.S;
            EntityState e = UiSummons.Pet(s);
            if (e == null) return null;
            PetInfoView pi = s.Player.PetInfo != null && s.Player.PetInfo.Guid == e.Guid ? s.Player.PetInfo : null;
            string fam = pi != null && !string.IsNullOrEmpty(pi.FamilyName) ? " " + pi.FamilyName : " pet";
            string t = "<size=14>" + e.Name + "</size>\nLevel " + e.Level + fam + "\n" + e.Health + " / " + e.MaxHealth + " health"
                + (e.PowerMax > 0 ? ", " + e.PowerValue + " / " + e.PowerMax + " " + (e.Power == PowerType.Focus ? "focus" : e.Power == PowerType.Energy ? "energy" : "mana") : "");
            if (pi != null && pi.IsHunterPet && pi.XpNext > 0) t += "\n" + UiGlobalStrings.G("XP", "XP") + " " + pi.Xp + " / " + pi.XpNext;   // ours (WoW shows it on the pet paperdoll)
            return t + "\n<color=#00ff00>" + (_ctx.Vr ? "Tap to target your pet; the small arrow opens its menu." : "Click to target your pet, right-click for its menu.") + "</color>";
        }

        /// <summary>WoW's PET unit menu (UnitPopup.lua:139 + rules 795-809): Rename only if abandonable AND renameable, Abandon only if
        /// abandonable (a hunter pet), Dismiss only if NOT abandonable (warlock / DK / mage pets, guardians, charmed units).</summary>
        public void ShowMenu()
        {
            IWowSession s = _ctx.S;
            EntityState e = UiSummons.Pet(s);
            if (s == null || e == null) return;
            PetInfoView pi = s.Player.PetInfo != null && s.Player.PetInfo.Guid == e.Guid ? s.Player.PetInfo : null;
            bool hunter = pi != null && pi.IsHunterPet;
            var labels = new List<string>();
            var acts = new List<Action>();
            if (hunter && pi.CanRename) { labels.Add(UiGlobalStrings.G("PET_RENAME", "Rename")); acts.Add(() => AskRename(s)); }
            if (hunter) { labels.Add(UiGlobalStrings.G("PET_ABANDON", "Abandon")); acts.Add(() => AskAbandon(s)); }
            else { labels.Add(UiGlobalStrings.G("PET_DISMISS", "Dismiss")); acts.Add(() => s.DismissPet()); }
            labels.Add(UiGlobalStrings.G("CANCEL", "Cancel")); acts.Add(null);
            SocialMenu.Open(Root, new Vector2(40f, 50f), e.Name, labels.ToArray(), acts.ToArray());
            WowLog.Info("UI: PET menu for " + e.Name + " (" + (hunter ? "hunter pet" : "not abandonable") + ")");
        }

        private static void AskRename(IWowSession s)
        {
            // StaticPopupDialogs["RENAME_PET"] (StaticPopup.lua:2118-2130: ACCEPT / CANCEL, edit box, maxLetters 12) then "PETRENAMECONFIRM" (1110-1116)
            UiPopup.Show(new UiPopupSpec
            {
                Which = "RENAME_PET", Text = UiGlobalStrings.G("PET_RENAME_LABEL", "Enter desired name of pet:"), Button1 = UiGlobalStrings.G("ACCEPT", "Accept"),
                Button2 = UiGlobalStrings.G("CANCEL", "Cancel"), HasEditBox = true, MaxLetters = 12, EnterClicksFirstButton = true,
                OnAcceptText = name =>
                {
                    if (string.IsNullOrEmpty(name)) return;
                    UiPopup.Show(new UiPopupSpec
                    {
                        Which = "PETRENAMECONFIRM", Text = UiGlobalStrings.G("PET_RENAME_CONFIRMATION", "Name your pet '%s'?").Replace("%s", name),
                        Button1 = UiGlobalStrings.G("YES", "Yes"), Button2 = UiGlobalStrings.G("NO", "No"), OnAccept = () => s.RenamePet(name),
                        KeepOpen = () => s.Player.PetInfo != null
                    });
                },
                KeepOpen = () => s.Player.PetInfo != null
            });
        }

        private static void AskAbandon(IWowSession s)
        {
            // StaticPopupDialogs["ABANDON_PET"] (StaticPopup.lua:1671-1681): OKAY / CANCEL
            UiPopup.Show(new UiPopupSpec
            {
                Which = "ABANDON_PET", Text = UiGlobalStrings.G("ABANDON_PET", "Are you sure you want to permanently abandon your pet?"),
                Button1 = UiGlobalStrings.G("OKAY", "Okay"), Button2 = UiGlobalStrings.G("CANCEL", "Cancel"), OnAccept = () => s.AbandonPet(), ShowAlert = true,
                KeepOpen = () => s.Player.PetInfo != null
            });
        }

        /// <summary>Returns true while a pet is shown.</summary>
        public bool Refresh(IWowSession s)
        {
            EntityState e = UiSummons.Pet(s);
            bool show = e != null;
            if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            if (!show) { _guid = 0; return false; }
            if (e.Guid != _guid) { _guid = e.Guid; _h = _hm = _m = _mm = -1; _nameShown = null; _happiness = -1; }
            if (!ReferenceEquals(e.Name, _nameShown)) { _nameShown = e.Name; _name.text = e.Name ?? ""; }
            if (e.Health != _h || e.MaxHealth != _hm)
            {
                _h = e.Health; _hm = e.MaxHealth;
                _hp.Set(_hm > 0 ? (float)_h / _hm : 0f);
                _hpText.SetText("{0} / {1}", _h, _hm);
            }
            if (e.PowerValue != _m || e.PowerMax != _mm)
            {
                _m = e.PowerValue; _mm = e.PowerMax;
                _mp.Root.gameObject.SetActive(_mm > 0);
                _mp.SetColor(UiArt.PowerColor(e.Power == PowerType.None ? PowerType.Mana : e.Power));
                _mp.Set(_mm > 0 ? (float)_m / _mm : 0f);
                _mpText.SetText(_mm > 0 ? "{0} / {1}" : "", _m, _mm);
            }
            if (e.Dead != _dead) { _dead = e.Dead; _portrait.color = e.Dead ? new Color(0.35f, 0.35f, 0.35f) : Color.white; }
            PetInfoView pi = s.Player.PetInfo;
            int h = pi != null && pi.Guid == e.Guid && pi.IsHunterPet ? pi.Happiness : 0;   // shown only for a hunter pet (PetFrame.lua:147-150)
            if (h != _happiness)
            {
                _happiness = h;
                _happy.enabled = h >= 1 && h <= 3 && _happy.texture != null;
                if (_happy.enabled) UiKit.Coords(_happy, HappyLeft[h - 1], HappyLeft[h - 1] + 0.1875f, 0f, 0.359375f);
            }
            return true;
        }
    }

    /// <summary>
    /// PetActionBarFrame (PetActionBarFrame.xml / .lua): NUM_PET_ACTION_SLOTS 10, 30x30 PetActionButtons from BOTTOMLEFT (36, 2), each next
    /// LEFT = previous RIGHT + 8 except button 7 = +7 (xml:118-205), on the UI-PetBar art (desktop). The bar is Player.ControlBar for a pet
    /// or a charmed unit (the server's saved layout online; AzerothCore's default layout offline). Left click = use (lua:287-292
    /// CastPetAction), right click on a spell = autocast on / off (TogglePetAutocast), VR: hold = autocast; Ctrl+1..0 (BONUSACTIONBUTTON).
    /// The vehicle / possess bar (UiVehicleBar) and the shaman multicast row (UiMultiCastBar) are built next to it (same parent).
    /// </summary>
    public sealed class UiPetBar
    {
        public const string Art = @"Interface\PetActionBar\UI-PetBar.blp";
        public const string AutoCastable = @"Interface\Buttons\UI-AutoCastableOverlay.blp";
        public const int Slots = 10;

        public RectTransform Root;
        private readonly UiContext _ctx;
        private readonly UiControlButton[] _b = new UiControlButton[Slots];
        private readonly UiVehicleBar _vehicle;
        private readonly UiMultiCastBar _multi;
        private readonly bool _art;
        private readonly float _x, _y;
        private int _counter = -1;
        private ControlBarView _bar;

        /// <summary>Ctrl+1..0 (vrcast's key loop, hook H9a): pet bar slot i (0-based) while a pet / charm bar shows.</summary>
        public static void PressKey(IWowSession s, int i)
        {
            ControlBarView v = s != null && s.Player != null ? s.Player.ControlBar : null;
            if (v == null || i < 0 || i >= Slots || (v.Kind != ControlBarKind.Pet && v.Kind != ControlBarKind.Charm)) return;
            UiPetRows.Use(s, i, 0);
        }

        public UiPetBar(Transform parent, UiContext ctx, float x, float y, float size, float step, bool art)
        {
            _ctx = ctx;
            _art = art;
            _x = x; _y = y;
            float w = art ? 509f : Slots * step + 4f;
            Root = UiKit.Rect(parent, "Pet Bar", x, y, w, art ? 43f : size + 4f);
            float by = 2f, bx = 0f;
            if (art)
            {
                Texture2D t = UiArt.Tex(Art);
                UiKit.Pic(Root, "Art 0", t, 0f, 1f, 0.015625f, 0.359375f, 0f, 0f, 256f, 44f);
                UiKit.Pic(Root, "Art 1", t, 0f, 0.71875f, 0.375f, 0.71875f, 256f, 0f, 184f, 44f);
                by = 43f - 2f - size;
                bx = 36f;
            }
            for (int i = 0; i < Slots; i++)
            {
                int k = i;
                float sx = art ? bx + i * (size + 8f) - (i >= 6 ? 1f : 0f) : i * step;   // button 7 is 7 apart (PetActionBarFrame.xml:166)
                var cb = new UiControlButton(Root, "Pet action " + (i + 1), sx, by, size);
                cb.B.Click = () => Click(k);
                cb.B.RightClick = () => Autocast(k);
                cb.B.LongPress = () => Autocast(k);
                cb.B.Tip = () => Tip(k);
                if (i >= 3 && i <= 6)
                    UiKit.DropTarget(cb.B, p => p != null && ReferenceEquals(p.Source, UiPetBook.DragTag) && p.SpellId != 0,
                        p => { if (_ctx.S != null) _ctx.S.SetPetBarSlot(k, p.SpellId); UiPickup.Clear("placed on the pet bar"); },
                        p => "Put it on the pet bar");   // ours
                _b[i] = cb;
            }
            _vehicle = new UiVehicleBar(parent, ctx, art, art ? 68f : x, art ? 59f : y, size);
            _multi = new UiMultiCastBar(parent, ctx, art ? 30f : x, art ? 21f : y, size);
            Root.gameObject.SetActive(false);
        }

        private ControlSlotView SlotAt(int k) { return _bar != null && k >= 0 && k < Slots ? _bar.Slots[k] : null; }

        private void Click(int k)
        {
            IWowSession s = _ctx.S;
            if (s == null) return;
            int picked = UiPetBook.PickedSpell;
            if (picked != 0 && k >= 3 && k <= 6) { s.SetPetBarSlot(k, picked); UiPetBook.ClearPick(); return; }
            UiPetRows.Use(s, k, 0);
        }

        private void Autocast(int k)
        {
            ControlSlotView v = SlotAt(k);
            if (_ctx.S == null || v == null || v.Kind != ControlSlotKind.Spell || !v.Autocastable) return;
            _ctx.S.SetPetAutocast(v.Action, !v.Autocast);
        }

        private string Tip(int k)
        {
            ControlSlotView v = SlotAt(k);
            if (v == null) return null;
            string hint = v.Kind == ControlSlotKind.Spell && v.Autocastable ? (_ctx.Vr ? "<color=#00ff00>Hold to toggle autocast.</color>" : "<color=#00ff00>Right-click to toggle autocast.</color>") : null;   // ours
            return UiSummons.Tip(v, hint);
        }

        /// <summary>Returns true while shown (a pet / charm bar exists).</summary>
        public bool Refresh(IWowSession s)
        {
            float now = Time.unscaledTime;
            PlayerState p = s != null ? s.Player : null;
            ControlBarView v = p != null ? p.ControlBar : null;
            bool show = v != null && (v.Kind == ControlBarKind.Pet || v.Kind == ControlBarKind.Charm);
            bool vehicleShown = _vehicle.Refresh(s, now);
            if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            _multi.Refresh(s, now, show || vehicleShown, _art ? (show ? 560f : 30f) : _x);
            if (!show) { _bar = null; return false; }
            _bar = v;
            if (v.Counter != _counter)
            {
                _counter = v.Counter;
                WowLog.Info("UI: pet bar " + v.Kind + " for 0x" + v.UnitGuid.ToString("X") + " (" + v.Spells.Count + " book spells)");
            }
            for (int i = 0; i < Slots; i++) _b[i].Show(v.Slots[i], now);
            return true;
        }
    }

    /// <summary>
    /// The vehicle / possess bar. WoW's VehicleMenuBar (a vehicle seat with VEHICLE_SEAT_FLAG_CAN_CAST, VehicleView.HasVehicleUi):
    /// VEHICLE_MAX_ACTIONBUTTONS 6 (VehicleMenuBar.lua:6), LeaveButton 50x47 (UI-Vehicles-Button-Exit-Up / -Down, texcoords 0.140625-0.859375,
    /// lua:40-50), PitchUp / PitchDown 38x36 (lua:16-38) only for aim-adjustable vehicles. Otherwise (a possessed unit, a controlling seat
    /// without the vehicle UI) the bonus-bar look: 10 buttons + WoW's PossessBar Cancel (BonusActionBarFrame.lua:237-296; CANCEL, GS 1338).
    /// A passenger seat: only Leave + the seat arrows (ours). The skin art of VehicleMenuBar.lua is not drawn (a dark panel, ours).
    /// </summary>
    public sealed class UiVehicleBar
    {
        public const string ExitUp = @"Interface\Vehicles\UI-Vehicles-Button-Exit-Up.blp", ExitDown = @"Interface\Vehicles\UI-Vehicles-Button-Exit-Down.blp";
        public const string PitchUpUp = @"Interface\Vehicles\UI-Vehicles-Button-Pitch-Up.blp", PitchUpDown = @"Interface\Vehicles\UI-Vehicles-Button-Pitch-Down.blp";
        public const string PitchDownUp = @"Interface\Vehicles\UI-Vehicles-Button-PitchDown-Up.blp", PitchDownDown = @"Interface\Vehicles\UI-Vehicles-Button-PitchDown-Down.blp";
        private const float PitchStep = 0.25f;   // ours: one click = PitchSpeed x 0.25 s

        /// <summary>True while the bar shows (uicore's UiLayouts may hide the desktop main bar meanwhile - WoW does).</summary>
        public static bool ShownNow { get; private set; }

        public RectTransform Root;
        private readonly UiContext _ctx;
        private readonly UiControlButton[] _b = new UiControlButton[10];
        private readonly UiButton _leave, _pitchUp, _pitchDown, _prevSeat, _nextSeat;
        private readonly UiBar _hp;
        private readonly TextMeshProUGUI _name;
        private readonly bool _art;
        private readonly float _x, _y, _size;
        private ControlBarView _bar;
        private int _look = -1;   // 0 vehicle UI (6), 1 bonus look (10), 2 passenger (none)
        private static bool _loggedSkin;

        /// <summary>A digit key i (0-based) for the vehicle / possess bar: true = used (vehicle look i &lt; 6, bonus look i &lt; 10;
        /// ActionButton.lua:15-27).</summary>
        public static bool TryPressKey(IWowSession s, int i)
        {
            PlayerState p = s != null ? s.Player : null;
            ControlBarView v = p != null ? p.ControlBar : null;
            if (v == null || (v.Kind != ControlBarKind.Vehicle && v.Kind != ControlBarKind.Possess)) return false;
            bool vehicleUi = v.Kind == ControlBarKind.Vehicle && p.Vehicle != null && p.Vehicle.HasVehicleUi;
            if (i < 0 || i >= (vehicleUi ? 6 : 10)) return false;
            UiPetRows.Use(s, i, 0);
            return true;
        }

        public UiVehicleBar(Transform parent, UiContext ctx, bool art, float x, float y, float size)
        {
            _ctx = ctx;
            _art = art;
            _x = x; _y = y; _size = size;
            float step = size + 6f;
            float w = art ? 888f : 12f * step + 60f;
            Root = UiKit.Rect(parent, "Vehicle Bar", x, y, w, art ? 53f : size + 26f);
            UiKit.Solid(Root, "Panel", new Color(0.05f, 0.04f, 0.03f, 0.85f), 0f, 0f, w, art ? 53f : size + 26f);   // ours (the skin art is not drawn)
            _name = UiKit.Text(Root, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 4f, -16f, 200f, 14f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _hp = UiBar.Make(Root, "Health", 206f, -12f, 120f, 8f, UiArt.HealthGreen, true);
            float bx = art ? 200f : 4f, by = art ? (53f - size) * 0.5f : 2f;
            for (int i = 0; i < 10; i++)
            {
                int k = i;
                var cb = new UiControlButton(Root, "Vehicle action " + (i + 1), bx + i * step, by, size);
                cb.B.Click = () => { if (_ctx.S != null) UiPetRows.Use(_ctx.S, k, 0); };
                cb.B.Tip = () => _bar != null ? UiSummons.Tip(_bar.Slots[k], null) : null;
                _b[i] = cb;
            }
            float lx = art ? 888f - 148f - 50f : bx + 10f * step + 4f;
            _leave = UiKit.ArtButton(Root, "Leave", ExitUp, ExitDown, ExitUp, UiArt.ButtonHilightSquare, lx, art ? 53f - 18f - 47f : 0f, art ? 47f : size + 4f, () => { if (_ctx.S != null) _ctx.S.LeaveVehicle(); });
            UiKit.Coords(_leave.Art, 0.140625f, 0.859375f, 0.140625f, 0.859375f);                 // VehicleMenuBar.lua:40-50
            if (_leave.Pushed != null) UiKit.Coords(_leave.Pushed, 0.140625f, 0.859375f, 0.140625f, 0.859375f);
            _leave.Tip = () => _bar != null && _bar.Kind == ControlBarKind.Possess ? UiGlobalStrings.G("CANCEL", "Cancel") : UiGlobalStrings.G("LEAVE_VEHICLE", "Leave Vehicle");
            float px = art ? 146f : lx + size + 8f;
            _pitchUp = UiKit.ArtButton(Root, "Pitch up", PitchUpUp, PitchUpDown, PitchUpUp, UiArt.ButtonHilightSquare, px, art ? 53f - 41f - 36f : 0f, art ? 36f : size * 0.5f, () => Pitch(1f));
            _pitchDown = UiKit.ArtButton(Root, "Pitch down", PitchDownUp, PitchDownDown, PitchDownUp, UiArt.ButtonHilightSquare, px, art ? 53f - 3f - 36f : size * 0.5f, art ? 36f : size * 0.5f, () => Pitch(-1f));
            _pitchUp.Tip = () => "Aim up";       // ours
            _pitchDown.Tip = () => "Aim down";   // ours
            float sx = art ? 888f - 90f : px + size;
            _prevSeat = UiKit.ArtButton(Root, "Previous seat", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.ButtonHilightSquare, sx, art ? 11f : 0f, art ? 32f : size * 0.5f, () => { if (_ctx.S != null) _ctx.S.ChangeSeat(-1); });
            _nextSeat = UiKit.ArtButton(Root, "Next seat", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.ButtonHilightSquare, sx + (art ? 34f : size * 0.5f), art ? 11f : 0f, art ? 32f : size * 0.5f, () => { if (_ctx.S != null) _ctx.S.ChangeSeat(-2); });
            _prevSeat.Tip = () => "Previous seat";   // ours
            _nextSeat.Tip = () => "Next seat";       // ours
            Root.gameObject.SetActive(false);
        }

        private void Pitch(float dir)
        {
            IWowSession s = _ctx.S;
            VehicleView v = s != null && s.Player != null ? s.Player.Vehicle : null;
            if (v == null) return;
            EntityState e = s.Find(v.Guid);
            float o = e != null ? e.Pos.O : s.Player.Pos.O;
            float speed = v.PitchSpeed > 0f ? v.PitchSpeed : 1f;
            s.SetVehicleAim(o, Mathf.Clamp(v.AimPitch + dir * speed * PitchStep, v.PitchMin, v.PitchMax));
        }

        /// <summary>Returns true while shown (in a vehicle / possessing, or a vehicle / possess bar exists).</summary>
        public bool Refresh(IWowSession s, float now)
        {
            PlayerState p = s != null ? s.Player : null;
            ControlBarView v = p != null ? p.ControlBar : null;
            bool bar = v != null && (v.Kind == ControlBarKind.Vehicle || v.Kind == ControlBarKind.Possess);
            VehicleView veh = p != null ? p.Vehicle : null;
            bool show = bar || veh != null;
            ShownNow = show;
            if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            if (!show) { _bar = null; _look = -1; return false; }
            _bar = bar ? v : null;
            int look = !bar ? 2 : v.Kind == ControlBarKind.Vehicle && veh != null && veh.HasVehicleUi ? 0 : 1;
            if (look != _look)
            {
                _look = look;
                for (int i = 0; i < 10; i++) _b[i].B.gameObject.SetActive(look == 0 ? i < 6 : look == 1);
                if (!_loggedSkin) { _loggedSkin = true; WowLog.Info("UI: vehicle bar - VehicleMenuBar's skin art is not drawn (a dark panel, ours); seat arrows instead of the seat indicator (gap)"); }
                WowLog.Info("UI: vehicle bar look " + (look == 0 ? "VehicleMenuBar (6 buttons)" : look == 1 ? "bonus bar (10 buttons + Cancel)" : "passenger (Leave / seats)"));
            }
            if (_art) UiKit.Move(Root, _x, _y + UiBars.PetBarLift);   // stays where the main bar is while the pet bar anchor is lifted
            ulong unit = bar ? v.UnitGuid : veh.Guid;
            EntityState e = s.Find(unit);
            string name = veh != null && !string.IsNullOrEmpty(veh.Name) ? veh.Name : e != null ? e.Name : "";
            if (_name.text != name) _name.text = name;
            _hp.Root.gameObject.SetActive(e != null && e.MaxHealth > 0);
            if (e != null && e.MaxHealth > 0) _hp.Set((float)e.Health / e.MaxHealth);
            if (bar) for (int i = 0; i < 10; i++) if (_b[i].B.gameObject.activeSelf) _b[i].Show(v.Slots[i], now);
            bool canLeave = veh == null || veh.CanLeave || (bar && v.Kind == ControlBarKind.Possess);
            if (_leave.gameObject.activeSelf != canLeave) _leave.gameObject.SetActive(canLeave);
            bool pitch = veh != null && veh.AimAdjustable;
            if (_pitchUp.gameObject.activeSelf != pitch) { _pitchUp.gameObject.SetActive(pitch); _pitchDown.gameObject.SetActive(pitch); }
            bool seats = veh != null && veh.CanSwitchSeat;
            if (_prevSeat.gameObject.activeSelf != seats) { _prevSeat.gameObject.SetActive(seats); _nextSeat.gameObject.SetActive(seats); }
            return true;
        }
    }

    /// <summary>
    /// MultiCastActionBarFrame (MultiCastActionBarFrame.xml / .lua; 3.2+ shaman totem bar), shown while Call of the Elements / Ancestors /
    /// Spirits (TOTEM_MULTI_CAST_SUMMON_SPELLS 66842 / 66843 / 66844, Constants.lua:438-446) is known: the page's call button, the four
    /// element buttons in TOTEM_PRIORITIES order (earth, fire, water, air) showing action button 132 + (page - 1) * 4 + element (0 fire .. 3 air;
    /// MultiCastActionBarFrame.lua:311-324, AzerothCore SpellEffects.cpp:6530-6567), Totemic Recall 36936 and the page arrow. The small arrow
    /// above an element lists that element's known totems (SPELL_ATTR7_CAN_BE_MULTI_CAST 0x20, the SUMMON effect's SummonProperties slot) +
    /// "No Totem" (MULTI_CAST_TOOLTIP_NO_TOTEM) - a plain list instead of WoW's flyout art (ours).
    /// </summary>
    public sealed class UiMultiCastBar
    {
        public static readonly int[] SummonSpells = { 66842, 66843, 66844 };
        public const int RecallSpell = 36936;
        private static readonly int[] Order = { 1, 0, 2, 3 };   // TOTEM_PRIORITIES: earth, fire, water, air (Constants.lua:431-436) as elements 0 fire .. 3 air
        private static readonly string[] ElementNames = { "Fire", "Earth", "Water", "Air" };   // ours (the flyout titles)

        public RectTransform Root;
        private readonly UiContext _ctx;
        private readonly UiButton _call, _recall, _page;
        private readonly UiButton[] _el = new UiButton[4];
        private readonly UiButton[] _pick = new UiButton[4];
        private readonly string[] _path = new string[6];
        private readonly bool[] _loaded = new bool[6];
        private readonly float _y;
        private int _bookCount = -1;
        private readonly List<int>[] _byElement = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        private static DbcTable _props;

        public UiMultiCastBar(Transform parent, UiContext ctx, float x, float y, float size)
        {
            _ctx = ctx;
            _y = y;
            Root = UiKit.Rect(parent, "MultiCastActionBarFrame", x, y, 230f, 38f);
            _call = UiKit.Slot(Root, "MultiCastSummonSpellButton", 0f, 6f, 30f, true);
            _call.Click = () => Cast(CurrentSummon());
            _call.Tip = () => SpellTip(CurrentSummon());
            for (int k = 0; k < 4; k++)
            {
                int el = Order[k];
                _el[k] = UiKit.Slot(Root, "MultiCastActionButton" + (k + 1), 36f + k * 34f, 6f, 30f, true);
                _el[k].Click = () => Cast(ActionSpell(el));
                _el[k].Tip = () => ActionSpell(el) != 0 ? SpellTip(ActionSpell(el)) : UiGlobalStrings.G("MULTI_CAST_TOOLTIP_NO_TOTEM", "No Totem");
                _pick[k] = UiKit.ArtButton(Root, "Totem choice " + (k + 1), UiArt.ScrollUpUp, UiArt.ScrollUpUp, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 43f + k * 34f, -10f, 16f, () => Choose(el));
                _pick[k].Tip = () => "Choose the " + ElementNames[el] + " totem of this page";   // ours
            }
            _recall = UiKit.Slot(Root, "MultiCastRecallSpellButton", 36f + 4f * 34f, 6f, 30f, true);
            _recall.Click = () => Cast(RecallSpell);
            _recall.Tip = () => SpellTip(RecallSpell);
            _page = UiKit.ArtButton(Root, "Page", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.ButtonHilightSquare, 36f + 5f * 34f, 8f, 24f, NextPage);
            _page.Tip = () => "Next totem page";   // ours
            Root.gameObject.SetActive(false);
        }

        private bool Knows(int spell) { return _ctx.P != null && UiContext.Find(_ctx.P, spell) != null; }

        private int CurrentSummon()
        {
            PlayerState p = _ctx.P;
            int page = p != null ? Mathf.Clamp(p.TotemBarPage, 1, 3) : 1;
            return SummonSpells[page - 1];
        }

        private int ActionSpell(int element)
        {
            PlayerState p = _ctx.P;
            if (p == null || p.ActionButtons == null) return 0;
            int slot = 132 + (Mathf.Clamp(p.TotemBarPage, 1, 3) - 1) * 4 + element;
            return slot < p.ActionButtons.Length ? WowActionSlots.SpellOf(p.ActionButtons[slot]) : 0;
        }

        private void Cast(int spell)
        {
            if (spell != 0 && _ctx.S != null) _ctx.S.CastSpell(spell, 0);
        }

        private string SpellTip(int spell)
        {
            AbilityView v = _ctx.P != null ? UiContext.Find(_ctx.P, spell) : null;
            return v != null ? "<size=14>" + v.Name + "</size>" + (string.IsNullOrEmpty(v.Description) ? "" : "\n<color=#ffd100>" + v.Description + "</color>") : null;
        }

        private void NextPage()
        {
            IWowSession s = _ctx.S;
            if (s == null) return;
            int page = Mathf.Clamp(s.Player.TotemBarPage, 1, 3);
            for (int k = 1; k <= 3; k++)
            {
                int next = (page - 1 + k) % 3 + 1;
                if (Knows(SummonSpells[next - 1])) { s.SetTotemBarPage(next); return; }
            }
        }

        private void Choose(int element)
        {
            IWowSession s = _ctx.S;
            if (s == null) return;
            BuildLists();
            List<int> list = _byElement[element];
            int slot = 132 + (Mathf.Clamp(s.Player.TotemBarPage, 1, 3) - 1) * 4 + element;
            var labels = new List<string>();
            var acts = new List<Action>();
            foreach (int id in list)
            {
                int sp = id;
                AbilityView v = UiContext.Find(s.Player, sp);
                labels.Add(v != null ? v.Name : sp.ToString());
                acts.Add(() => s.SetActionBarSlot(slot, sp));
            }
            labels.Add(UiGlobalStrings.G("MULTI_CAST_TOOLTIP_NO_TOTEM", "No Totem"));
            acts.Add(() => s.SetActionBarSlot(slot, 0));
            SocialMenu.Open(Root, new Vector2(36f + System.Array.IndexOf(Order, element) * 34f, -10f), ElementNames[element], labels.ToArray(), acts.ToArray());
        }

        /// <summary>The known totems per element (SPELL_ATTR7_CAN_BE_MULTI_CAST, SharedDefines.h:634; SummonProperties.dbc field 4 slot 1..4).</summary>
        private void BuildLists()
        {
            PlayerState p = _ctx.P;
            if (p == null || p.Spellbook.Count == _bookCount) return;
            _bookCount = p.Spellbook.Count;
            for (int i = 0; i < 4; i++) _byElement[i].Clear();
            SpellDb db = UiArt.Files != null ? SpellDb.Get(UiArt.Files) : null;
            if (db == null) return;
            if (_props == null) _props = Dbc.Load(UiArt.Files, "SummonProperties");
            if (_props == null) return;
            foreach (AbilityView a in p.Spellbook)
            {
                SpellInfo s = a != null ? db.Find(a.SpellId) : null;
                if (s == null || (s.Attributes[7] & 0x20u) == 0) continue;
                for (int e = 0; e < 3; e++)
                {
                    SpellEffectInfo fx = s.Effects[e];
                    if (fx == null || fx.Effect != SpellEffect.Summon || fx.MiscValueB == 0) continue;
                    int r = _props.FindRow(fx.MiscValueB);
                    int slot = r >= 0 ? _props.GetInt(r, 4) : 0;
                    if (slot >= 1 && slot <= 4) { _byElement[slot - 1].Add(s.Id); break; }
                }
            }
        }

        private void Icon(UiButton b, int idx, int spell)
        {
            AbilityView v = spell != 0 && _ctx.P != null ? UiContext.Find(_ctx.P, spell) : null;
            string path = v != null ? v.IconPath : null;
            if (!ReferenceEquals(path, _path[idx])) { _path[idx] = path; _loaded[idx] = false; b.Icon.enabled = false; }
            if (path == null || _loaded[idx]) return;
            Texture2D t = UiArt.Icon(path);
            if (t == null) return;
            b.Icon.texture = t; b.Icon.color = Color.white; b.Icon.enabled = true; UiKit.Coords(b.Icon, 0f, 1f, 0f, 1f); _loaded[idx] = true;   // lead fix 2026-10-10: UiKit.Slot's icon starts clear
        }

        public void Refresh(IWowSession s, float now, bool barShown, float x)
        {
            PlayerState p = s != null ? s.Player : null;
            bool show = p != null && (Knows(66842) || Knows(66843) || Knows(66844)) && !(_ctx.Vr && barShown);   // VR: the strip shows one bar at a time (ours)
            if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            if (!show) return;
            if (!Knows(CurrentSummon())) NextPage();
            UiKit.Move(Root, x, _y);
            Icon(_call, 0, CurrentSummon());
            for (int k = 0; k < 4; k++) Icon(_el[k], 1 + k, ActionSpell(Order[k]));
            Icon(_recall, 5, Knows(RecallSpell) ? RecallSpell : 0);
            if (_recall.gameObject.activeSelf != Knows(RecallSpell)) _recall.gameObject.SetActive(Knows(RecallSpell));
        }
    }

    /// <summary>
    /// TotemFrame (TotemFrame.xml / .lua): up to four 37x37 TotemButtons 33 apart, packed from the left in TOTEM_PRIORITIES order
    /// (earth, fire, water, air - Constants.lua:431), each UI-Minimap-Background 34x34 + the 22x22 icon with a reversed cooldown wipe +
    /// TotemBorder 38x38, the time left under it (SecondsToTimeAbbrev: "%d m" from 60 s up, rounded up, else "%d s"). Iteration 11: the
    /// totems come from Player.Totems (online SMSG_TOTEM_CREATED with the server's real durations; offline the session's own), right-click =
    /// destroy (TotemFrame.lua:97-103 DestroyTotem), VR: a second tap within 2 s = destroy (ours).
    /// </summary>
    public sealed class UiTotemBar
    {
        public const string Background = @"Interface\Minimap\UI-Minimap-Background.blp";
        public const string Border = @"Interface\CharacterFrame\TotemBorder.blp";
        private static readonly int[] Priority = { 1, 0, 2, 3 };   // elements earth, fire, water, air
        private const float VrDestroyTap = 2f;                      // ours

        public RectTransform Root;
        private readonly UiContext _ctx;
        private readonly UiButton[] _b = new UiButton[4];
        private readonly RawImage[] _icon = new RawImage[4];
        private readonly Image[] _wipe = new Image[4];
        private readonly TextMeshProUGUI[] _time = new TextMeshProUGUI[4];
        private readonly string[] _path = new string[4];
        private readonly int[] _secs = new int[4];
        private readonly TotemView[] _shown = new TotemView[4];
        private readonly float[] _tapAt = { -10f, -10f, -10f, -10f };

        public UiTotemBar(Transform parent, UiContext ctx, float x, float y)
        {
            _ctx = ctx;
            Root = UiKit.Rect(parent, "Totem Frame", x, y, 4f * 33f + 4f, 50f);
            Texture2D bg = UiArt.Tex(Background), border = UiArt.Tex(Border);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(Root, "Totem " + (i + 1), i * 33f, 0f, 37f, 37f);
                UiKit.Pic(b.transform, "Background", bg, 1.5f, 1.5f, 34f, 34f);
                _icon[i] = UiKit.Pic(b.transform, "Icon", null, 7.5f, 7.5f, 22f, 22f);
                _icon[i].enabled = false;
                var wr = UiKit.Rect(b.transform, "Cooldown", 7.5f, 7.5f, 22f, 22f);
                _wipe[i] = wr.gameObject.AddComponent<Image>();
                _wipe[i].sprite = UiArt.WhiteSprite;
                _wipe[i].type = Image.Type.Filled;
                _wipe[i].fillMethod = Image.FillMethod.Radial360;
                _wipe[i].fillOrigin = (int)Image.Origin360.Top;
                _wipe[i].fillClockwise = true;
                _wipe[i].color = new Color(0f, 0f, 0f, 0.6f);
                _wipe[i].raycastTarget = false;
                if (UiKit.Over) _wipe[i].material = UiArt.OverUi;
                UiKit.Pic(b.transform, "Border", border, -0.5f, -0.5f, 38f, 38f);
                _time[i] = UiKit.Text(b.transform, "Duration", "", 10f, UiArt.Gold, TextAlignmentOptions.Top, -4f, 37f, 45f, 12f);
                _time[i].textWrappingMode = TextWrappingModes.NoWrap;
                b.Tip = () => Tip(k);
                b.RightClick = () => Destroy(k);
                b.Click = () =>
                {
                    if (!_ctx.Vr) return;
                    float now = Time.unscaledTime;
                    if (now - _tapAt[k] <= VrDestroyTap) { _tapAt[k] = -10f; Destroy(k); }
                    else { _tapAt[k] = now; _ctx.Say("Tap the totem again to destroy it.", UiArt.InfoYellow); }   // ours
                };
                b.gameObject.SetActive(false);
                _b[i] = b;
                _secs[i] = -1;
            }
            Root.gameObject.SetActive(false);
        }

        private void Destroy(int i)
        {
            TotemView t = _shown[i];
            if (t == null || _ctx.S == null) return;
            _ctx.S.DestroyTotem(t.Slot);
        }

        private string Tip(int i)
        {
            TotemView t = _shown[i];
            if (t == null) return null;
            return "<size=14>" + t.Name + "</size>\n" + Mathf.CeilToInt(Mathf.Max(0f, t.Remaining)) + " sec remaining\n<color=#00ff00>"
                + (_ctx.Vr ? "Tap twice to destroy it." : "Right-click to destroy it.") + "</color>";   // ours
        }

        /// <summary>Returns true while at least one of the player's totems stands.</summary>
        public bool Refresh(IWowSession s)
        {
            PlayerState p = s != null ? s.Player : null;
            int n = 0;
            if (p != null && p.Totems != null)
                for (int k = 0; k < Priority.Length; k++)
                {
                    TotemView t = Priority[k] < p.Totems.Length ? p.Totems[Priority[k]] : null;
                    if (t != null && t.Remaining > 0f) Put(n++, t);
                }
            for (int i = n; i < 4; i++)
            {
                if (_shown[i] == null) continue;
                _shown[i] = null;
                _b[i].gameObject.SetActive(false);
            }
            bool any = n > 0;
            if (Root.gameObject.activeSelf != any) Root.gameObject.SetActive(any);
            return any;
        }

        private void Put(int i, TotemView t)
        {
            UiButton b = _b[i];
            if (!ReferenceEquals(_shown[i], t))
            {
                _shown[i] = t;
                _path[i] = null;
                _icon[i].enabled = false;
                _secs[i] = -1;
                b.gameObject.SetActive(true);
            }
            if (_path[i] == null && !string.IsNullOrEmpty(t.IconPath))
            {
                Texture2D tex = UiArt.Icon(t.IconPath);
                if (tex != null)
                {
                    _path[i] = t.IconPath;
                    _icon[i].texture = tex;
                    _icon[i].color = Color.white;
                    _icon[i].enabled = true;
                }
            }
            float left = Mathf.Max(0f, t.Remaining);
            _wipe[i].fillAmount = t.Duration > 0f ? Mathf.Clamp01(1f - left / t.Duration) : 0f;
            int secs = Mathf.FloorToInt(left);
            if (secs != _secs[i])
            {
                _secs[i] = secs;
                if (left >= 60f) _time[i].SetText("{0} m", Mathf.CeilToInt(left / 60f));
                else _time[i].SetText("{0} s", secs);
            }
        }
    }
}
