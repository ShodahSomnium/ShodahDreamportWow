// iteration 11 - owner npcui (proposal-npc.md 6.1 / H7 / H10; moved here from UiWindows.cs by w0game, filled by npcui)
// The Bags window (UiWindowKind.Bags, registry built-in, Apart): every open WoW ContainerFrame - the backpack, the four bags, the keyring
// and (with the bank open) the seven bank bags - stacked as updateContainerFrameAnchors does (ContainerFrame.lua:547-611). The window is
// open while any container is; B / F12 open the backpack (SetOpen), F8..F11 / the bag bar toggle one bag (ToggleBag), Shift+B all
// (OpenAllBags). VR (ours): the containers stand in the arm window (the lid) in columns from its bottom right, scaled to fit.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    // =====================================================================================================================
    /// <summary>
    /// WoW's container frames as one window. Desktop: the column(s) grow up and left from the anchor the layout gives (the bottom-right
    /// holder, CONTAINER_OFFSET_Y 70 + uicore's bar lift), a new column when the screen height is used up, scaled down to CONTAINER_SCALE
    /// 0.75 when the columns would reach the bank frame. VR: the same stacking inside the arm window, scaled down until it fits (ours).
    /// </summary>
    public sealed class UiBags : UiWindow
    {
        public const int Slots = 16;                       // the backpack (ContainerFrame.lua BACKPACK_HEIGHT layout, 4 x 4)
        public const float ContainerScale = 0.75f;         // CONTAINER_SCALE (ContainerFrame.lua:13)
        public const float VisibleSpacing = 3f;            // VISIBLE_CONTAINER_SPACING (ContainerFrame.lua:10)
        public const float VrMinScale = 0.5f;              // ours: the smallest VR container scale before columns overflow the arm window
        public const float BankRightInset = 25f;           // leftLimit = BankFrame:GetRight() - 25 (ContainerFrame.lua:553)

        private static readonly List<UiBags> Instances = new List<UiBags>();
        private static readonly Vector3[] Corners = new Vector3[4];
        private static bool _logged;

        /// <summary>The bags window of the layout in use (desktop or VR - each layout builds its own).</summary>
        public static UiBags Current
        {
            get
            {
                UiBags any = null;
                for (int i = Instances.Count - 1; i >= 0; i--)
                {
                    UiBags b = Instances[i];
                    if (b == null || b.Root == null) { Instances.RemoveAt(i); continue; }
                    if (any == null) any = b;
                    if (b.Ctx != null && b._isVr == b.Ctx.Vr) return b;
                }
                return any;
            }
        }

        private readonly Dictionary<int, UiContainerFrame> _frames = new Dictionary<int, UiContainerFrame>();
        private readonly List<int> _order = new List<int>();          // open containers in opening order (ContainerFrame1.bags)
        private readonly List<int> _serviceOpened = new List<int>();  // what ServiceOpen opened (closed again with the service)
        private readonly UiSurface _surface;
        private readonly bool _pointable, _over, _isVr;
        private readonly ContainerView _legacy = new ContainerView { Bag = InvPos.Bag0, SlotBase = InvPos.PackStart, Size = Slots, Slots = new ItemView[Slots] };
        private int _serviceDepth;
        private int _arrangeSig;

        /// <summary>Lead fix 2026-10-10 (the user, a screenshot: "the bags open ON TOP of the character screen, make them open up on the
        /// right"): VR - the rect the containers stack in (bottom-right first) instead of the whole arm window; null = the arm window.</summary>
        public RectTransform VrArea;

        /// <summary>Lead 2026-10-11 (the user: "in VR mode, there is no way to unequip bags"): VR - units kept free at the area's bottom for
        /// the bag bar (UiBagBar) under the containers.</summary>
        public float VrBottomInset;

        public UiBags(Transform parent, UiContext ctx, float x, float y, System.Action close)
        {
            Ctx = ctx;
            W = UiContainerFrame.Width;
            H = UiContainerFrame.BackpackHeight;
            _surface = UiKit.Surface;
            _pointable = UiKit.Pointable;
            _over = UiKit.Over;
            _isVr = _surface != null && _surface.IsWorld;
            Root = UiKit.Rect(parent, "Bags", x, y, W, H);
            FrameOf(0);   // the backpack is always there
            Root.gameObject.SetActive(false);
            Instances.Add(this);
            UiNpcPopups.Hook(ctx, parent);
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI bags (npcui): WoW's container frames - backpack, 4 bags, keyring, 7 bank bags (ContainerFrame.lua); ours: VR opens every bag " +
                            "at once and stacks them in the arm window scaled to fit (>= " + VrMinScale + "), the backpack's free-slot count, the hint lines.");
            }
        }

        // ------------------------------------------------------------------ the API other windows / keys use

        /// <summary>Any container open.</summary>
        public bool AnyContainerOpen { get { return IsOpen && _order.Count > 0; } }

        /// <summary>Container id (0 backpack, 1..4 bags, -2 keyring, 5..11 bank bags) is shown.</summary>
        public bool IsBagShown(int id) { return IsOpen && _order.Contains(id); }

        /// <summary>F8..F11 / the bag bar / a container's close button: open or close container id (ToggleBag, ContainerFrame.lua).</summary>
        public void ToggleBag(int id)
        {
            if (IsBagShown(id)) CloseId(id, true);
            else if (!OpenId(id, true)) return;
            SyncWindow();
        }

        /// <summary>Shift+B / Shift-click a bag button: OpenAllBags (ContainerFrame.lua:794-829) - all shown already = all closed.</summary>
        public void OpenAllBags()
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            int total = 1, open = 0;
            for (int id = 1; id <= 4; id++) if (Usable(p, id)) total++;
            for (int k = 0; k < _order.Count; k++) if (_order[k] != UiContainers.KeyringId) open++;
            bool wasOpen = IsOpen;
            CloseAllIds();
            if (!(wasOpen && open >= total)) OpenAllIds(p);
            SyncWindow();
        }

        /// <summary>A merchant / bank / mailbox opens (true) or closes (false) the backpack with it (MerchantFrame_OnShow BACKPACK_WAS_OPEN =
        /// OpenBackpack(), MerchantFrame.lua:39-40; OnHide CloseBackpack only if it was closed, :51-55). VR: every bag (ours).</summary>
        public void ServiceOpen(bool open)
        {
            PlayerState p = Ctx.P;
            if (open)
            {
                if (_serviceDepth++ > 0 || p == null) return;
                _serviceOpened.Clear();
                if (_isVr)
                {
                    for (int id = 0; id <= 4; id++) if (!IsBagShown(id) && Usable(p, id) && OpenId(id, false)) _serviceOpened.Add(id);
                }
                else if (!IsBagShown(0) && OpenId(0, true)) _serviceOpened.Add(0);
                SyncWindow();
            }
            else
            {
                if (_serviceDepth <= 0) return;
                if (--_serviceDepth > 0) return;
                for (int i = 0; i < _serviceOpened.Count; i++) if (IsBagShown(_serviceOpened[i])) CloseId(_serviceOpened[i], true);
                _serviceOpened.Clear();
                for (int id = UiContainers.BankBagFirstId; id <= UiContainers.BankBagLastId; id++) if (IsBagShown(id) && (Ctx.S == null || Ctx.S.BankerGuid == 0)) CloseId(id, false);
                SyncWindow();
            }
        }

        // ------------------------------------------------------------------ the window

        protected override void OnOpen()
        {
            // B / F12 / the palm: OpenBackpack; VR has no bag bar and no F-keys, so it opens every bag (ours).
            PlayerState p = Ctx.P;
            if (_order.Count == 0)
            {
                if (_isVr && p != null) OpenAllIds(p);
                else OpenId(0, false);
            }
            _arrangeSig = 0;
        }

        protected override void OnClose()
        {
            CloseAllIds();   // CloseAllBags (ContainerFrame.lua:831-836)
        }

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            for (int k = _order.Count - 1; k >= 0; k--)
            {
                int id = _order[k];
                if (!Usable(p, id)) CloseId(id, false);   // a bag taken off / the bank closed (BAG_CLOSED)
            }
            if (_order.Count == 0) { SyncWindow(); return; }
            bool pickUps = UiContainers.SlotsPickUp(Ctx);
            for (int k = 0; k < _order.Count; k++)
            {
                UiContainerFrame f = FrameOf(_order[k]);
                f.Refresh(ContainerOf(p, _order[k]), pickUps);
            }
            Arrange();
        }

        // ------------------------------------------------------------------ containers

        /// <summary>The container view of an id (0 backpack - the old flat list when the session has no containers, 1..4 bags, -2
        /// keyring, 5..11 bank bags); null = none.</summary>
        public ContainerView ContainerOf(PlayerState p, int id)
        {
            if (p == null) return null;
            if (id == 0)
            {
                if (p.Containers != null && p.Containers.Count > 0) return p.Containers[0];
                for (int i = 0; i < Slots; i++) _legacy.Slots[i] = p.Bags != null && i < p.Bags.Count ? p.Bags[i] : null;
                return _legacy;
            }
            if (id >= 1 && id <= 4) return p.Containers != null && id < p.Containers.Count ? p.Containers[id] : null;
            if (id == UiContainers.KeyringId) return p.Keyring;
            if (id >= UiContainers.BankBagFirstId && id <= UiContainers.BankBagLastId)
            {
                int i = id - UiContainers.BankBagFirstId + 1;
                return p.Bank != null && i < p.Bank.Count ? p.Bank[i] : null;
            }
            return null;
        }

        private bool Usable(PlayerState p, int id)
        {
            ContainerView c = ContainerOf(p, id);
            if (c == null || c.Size <= 0) return false;
            if (id >= UiContainers.BankBagFirstId) return !c.Locked && Ctx.S != null && Ctx.S.BankerGuid != 0;
            return true;
        }

        private UiContainerFrame FrameOf(int id)
        {
            UiContainerFrame f;
            if (_frames.TryGetValue(id, out f)) return f;
            using (new UiBuildScope(_surface, _pointable, _over))
                f = new UiContainerFrame(Root, Ctx, id, ToggleBag, _surface, _pointable, _over);
            f.Show(false);
            _frames[id] = f;
            return f;
        }

        private bool OpenId(int id, bool sound)
        {
            PlayerState p = Ctx.P;
            if (_order.Contains(id) || !Usable(p, id)) return false;
            bool windowOpen = IsOpen && _order.Count > 0;
            UiContainerFrame f = FrameOf(id);
            f.Refresh(ContainerOf(p, id), UiContainers.SlotsPickUp(Ctx));
            f.Show(true);
            _order.Add(id);
            _arrangeSig = 0;
            // ContainerFrame_OnShow sounds (ContainerFrame.lua:159-163); the first container of the window plays the window's sound (WowArmUi)
            if (sound && windowOpen) UiSound.Named(id == UiContainers.KeyringId ? "KeyRingOpen" : "igBackPackOpen");
            return true;
        }

        private void CloseId(int id, bool sound)
        {
            int k = _order.IndexOf(id);
            if (k < 0) return;
            _order.RemoveAt(k);
            UiContainerFrame f;
            if (_frames.TryGetValue(id, out f)) f.Show(false);
            _arrangeSig = 0;
            // ContainerFrame_OnHide sounds (ContainerFrame.lua:140-145); the last one closes the window, which plays its own
            if (sound && _order.Count > 0) UiSound.Named(id == UiContainers.KeyringId ? "KeyRingClose" : "igBackPackClose");
        }

        private void CloseAllIds()
        {
            for (int k = _order.Count - 1; k >= 0; k--)
            {
                UiContainerFrame f;
                if (_frames.TryGetValue(_order[k], out f)) f.Show(false);
            }
            _order.Clear();
            _arrangeSig = 0;
        }

        private void OpenAllIds(PlayerState p)
        {
            OpenId(0, false);
            for (int id = 1; id <= 4; id++) OpenId(id, false);
            if (Ctx.S != null && Ctx.S.BankerGuid != 0)
                for (int id = UiContainers.BankBagFirstId; id <= UiContainers.BankBagLastId; id++) OpenId(id, false);   // BankFrame:IsShown() (:820-828)
        }

        /// <summary>Keeps the window controller (WowArmUi: the VR arm window, sounds, Escape) in step with the open containers.</summary>
        private void SyncWindow()
        {
            bool any = _order.Count > 0;
            bool ctl = Ctx.IsOpen != null ? Ctx.IsOpen(UiWindowKind.Bags) : IsOpen;
            if (any != ctl && Ctx.Toggle != null) Ctx.Toggle(UiWindowKind.Bags);
            if (any != IsOpen)
            {
                Root.gameObject.SetActive(any);
                if (!any) CloseAllIds();
            }
        }

        // ------------------------------------------------------------------ stacking (updateContainerFrameAnchors, ContainerFrame.lua:547-611)

        private void Arrange()
        {
            float aL = -100000f, aT = -100000f, aR = W, aB = H;
            RectTransform area = _isVr && VrArea != null ? VrArea : _surface != null ? _surface.Content : Root.parent as RectTransform;
            if (area != null)
            {
                area.GetWorldCorners(Corners);   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
                Vector3 tl = Root.InverseTransformPoint(Corners[1]), br = Root.InverseTransformPoint(Corners[3]);
                aL = tl.x; aT = -tl.y; aR = br.x; aB = -br.y;
            }
            float right, bottom, left = aL, top = aT, minScale;
            if (_isVr) { right = aR; bottom = aB - VrBottomInset; minScale = VrMinScale; }
            else
            {
                right = W;           // the anchor's right edge and bottom (CONTAINER_OFFSET_X / _Y + uicore's bar offsets move the holder)
                bottom = H;
                minScale = ContainerScale;
                RectTransform bank = UiBank.ShownRoot;
                if (bank != null && bank.gameObject.activeInHierarchy)
                {
                    bank.GetWorldCorners(Corners);
                    left = Mathf.Max(left, Root.InverseTransformPoint(Corners[2]).x - BankRightInset);
                }
            }
            float avail = bottom - top;
            int sig = 17;
            unchecked
            {
                for (int k = 0; k < _order.Count; k++) { UiContainerFrame f = _frames[_order[k]]; sig = sig * 31 + _order[k] * 7 + (int)(f.H * 4f) + (int)f.W; }
                sig = sig * 31 + (int)(avail * 4f);
                sig = sig * 31 + (int)(left * 4f);
                sig = sig * 31 + (int)(right * 4f);
                sig = sig * 31 + (int)(VrBottomInset * 4f);
            }
            if (sig == _arrangeSig) return;
            _arrangeSig = sig;
            // pass 1: shrink until the columns fit right..left (WoW's leftMostPoint: one column always fits)
            float scale = 1f;
            while (scale > minScale)
            {
                int cols = Columns(avail / scale);
                float leftMost = cols > 1 ? right - cols * UiContainerFrame.Width * scale : right;
                if (leftMost >= left) break;
                scale -= 0.01f;
            }
            if (scale < minScale) scale = minScale;
            // pass 2: place
            float free = avail / scale;
            int column = 0;
            float stackBottom = bottom;
            for (int k = 0; k < _order.Count; k++)
            {
                UiContainerFrame f = _frames[_order[k]];
                if (k > 0 && free < f.H)
                {
                    column++;
                    free = avail / scale;
                    stackBottom = bottom;
                }
                float fRight = right - column * UiContainerFrame.Width * scale;
                float fTop = stackBottom - f.H * scale;
                UiKit.Move(f.Root, fRight - f.W * scale, fTop);
                f.Root.localScale = new Vector3(scale, scale, 1f);
                stackBottom = fTop;   // CONTAINER_SPACING 0: the next frame sits on this one's top
                free -= f.H + VisibleSpacing;
            }
        }

        private int Columns(float avail)
        {
            int cols = 1;
            float free = avail;
            for (int k = 0; k < _order.Count; k++)
            {
                float h = _frames[_order[k]].H;
                if (k > 0 && free < h) { cols++; free = avail; }
                free -= h + VisibleSpacing;
            }
            return cols;
        }
    }
}
