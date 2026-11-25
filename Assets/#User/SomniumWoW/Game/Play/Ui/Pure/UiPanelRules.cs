// iteration 11 - owner uicore: the port of WoW's UI panel manager (proposal-uicore.md 5.4.3) - P/harness/Probes/D11/uicore/UiPanelRulesProto.cs
// copied as is (namespace changed); harness-tested by probe UicPanelRules (P/harness/Probes/I11/uicore). Pure C#: no UnityEngine.
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure
{
    /// <summary>
    /// PROTOTYPE of W/Game/Play/Ui/Pure/UiPanelRules.cs (pure C#, no UnityEngine) - a line-by-line port of WoW 3.3.5a's UI panel manager
    /// (Interface\FrameXML\UIParent.lua of the user's install, dumped to P/harness/out_d11uicore/UIParent.lua):
    /// ShowUIPanel :1962-1975 -> FramePositionDelegate:ShowUIPanel :1327-1513, SetUIPanel :1522-1584, MoveUIPanel :1586-1600,
    /// HideUIPanel :1977-1990 -> FramePositionDelegate:HideUIPanel :1602-1644, UpdateUIPanelPositions :1654-1745, GetUIPanelWidth :2001,
    /// GetMaxUIPanelsWidth :2005-2014, CanShowRightUIPanel :2016-2028, CanShowCenterUIPanel :2030-2042, CanShowUIPanels :2044-2067,
    /// CanOpenPanels :2069-2091, CloseWindows :2121-2157, CloseAllWindows :2170-2182, ToggleFrame :1954-1960; UIParent.xml:6-11 attributes.
    /// Lua locals that go stale inside ShowUIPanel (centerFrame after a forced SetUIPanel("center", nil)) are kept stale on purpose.
    /// Builders: copy as is, change the namespace to SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure; the harness probe UicPanelRules is its test.
    /// </summary>
    public sealed class UiPanelRules
    {
        public const string Left = "left", Center = "center", Right = "right", Doublewide = "doublewide", Fullscreen = "fullscreen";
        public const string AreaLeft = "left", AreaCenter = "center", AreaDoublewide = "doublewide", AreaFull = "full";

        /// <summary>One managed window = one UIPanelWindows entry (+ the frame's own size when the entry has no width).</summary>
        public sealed class Panel
        {
            public int Kind;                 // UiWindowKind (the caller's id)
            public string Name;              // WoW's frame name (logs)
            public string Area;              // "left" | "center" | "doublewide" | "full" | null = not a UI panel (shown where it is)
            public int Pushable;
            public float InfoWidth;          // UIPanelWindows "width" (0 = the entry has none)
            public float FrameWidth = 384f;  // the frame's own width (its XML Size x)
            public float Width { get { return InfoWidth > 0f ? InfoWidth : FrameWidth; } }   // GetUIPanelWidth :2001-2003
            public float XOffset, YOffset;
            public bool WhileDead, AllowOtherPanels;
            public bool Shown;
            public float X, Y;               // TOPLEFT in UIParent units (Y negative = down), set when the manager places it
            public bool Placed;              // X/Y valid (false for "center" area frames: they keep their own CENTER anchor)
            public override string ToString() { return Name; }
        }

        // UIParent.xml:6-11 (the attributes) - DEFAULT_FRAME_WIDTH, TOP_OFFSET, LEFT_OFFSET; CENTER_OFFSET / RIGHT_OFFSET are rewritten.
        public const float DefaultFrameWidth = 384f, TopOffset = -104f, LeftOffset = 0f, RightOffsetBuffer = 80f;
        public float CenterOffset = 384f, RightOffset = 768f;
        /// <summary>UIParent:GetRight() in UIParent units (our canvas width: 768 * aspect / UI scale).</summary>
        public float ScreenWidth = 1365.333f;
        /// <summary>GetMaxUIPanelsWidth's minimap rule: set when a minimap cluster is shown at its default place - lead 2026-10-03: the
        /// desktop's UiMinimap (WowArmUi.RulesInputs copies UiDesktopLayout.MinimapShown / MinimapLeft = UIParent width - 192).</summary>
        public bool MinimapShown; public float MinimapLeft;
        /// <summary>UnitIsDead("player").</summary>
        public bool Dead;

        private Panel _left, _center, _right, _doublewide, _fullscreen;
        private bool _updating;

        /// <summary>What happened, in order: "show X", "hide X", "failed X", "dead X" (ERR_PLAYER_DEAD), "closeallbags", "uiparent hidden/shown".</summary>
        public readonly List<string> Events = new List<string>();
        /// <summary>Panels the manager hid during the calls since the last Clear (the caller clears its open flag, calls the window's Closed).</summary>
        public readonly List<Panel> HiddenByManager = new List<Panel>();
        public void ClearEvents() { Events.Clear(); HiddenByManager.Clear(); }

        public Panel GetUIPanel(string key)
        {
            switch (key)
            {
                case Left: return _left;
                case Center: return _center;
                case Right: return _right;
                case Doublewide: return _doublewide;
                case Fullscreen: return _fullscreen;
                default: return null;
            }
        }

        private void Slot(string key, Panel p)
        {
            switch (key)
            {
                case Left: _left = p; break;
                case Center: _center = p; break;
                case Right: _right = p; break;
                case Doublewide: _doublewide = p; break;
                case Fullscreen: _fullscreen = p; break;
            }
        }

        private void FrameShow(Panel p) { if (!p.Shown) { p.Shown = true; Events.Add("show " + p.Name); } }
        private void FrameHide(Panel p)
        {
            if (!p.Shown) return;
            p.Shown = false; p.Placed = false;
            Events.Add("hide " + p.Name);
            HiddenByManager.Add(p);
        }

        // ------------------------------------------------------------------ the global entry points
        /// <summary>ShowUIPanel(frame, force) :1962-1975.</summary>
        public void ShowUIPanel(Panel frame, bool force = false)
        {
            if (frame == null || frame.Shown) return;
            if (frame.Area == null) { FrameShow(frame); return; }
            DelegateShow(frame, force);
        }

        /// <summary>HideUIPanel(frame, skipSetPoint) :1977-1990.</summary>
        public void HideUIPanel(Panel frame, bool skipSetPoint = false)
        {
            if (frame == null || !frame.Shown) return;
            if (frame.Area == null) { FrameHide(frame); return; }
            DelegateHide(frame, skipSetPoint);
        }

        /// <summary>ToggleFrame :1954-1960.</summary>
        public void Toggle(Panel frame) { if (frame.Shown) HideUIPanel(frame); else ShowUIPanel(frame); }

        // ------------------------------------------------------------------ FramePositionDelegate:ShowUIPanel :1327-1513
        private void DelegateShow(Panel frame, bool force)
        {
            string frameArea = frame.Area;
            if (!CanOpenPanels() && frameArea != AreaCenter && frameArea != AreaFull) { Events.Add("failed " + frame.Name); return; }
            int framePushable = frame.Pushable;

            if (Dead && !frame.WhileDead) { Events.Add("dead " + frame.Name); return; }   // NotWhileDeadError

            // If we have a full-screen frame open, ignore other non-fullscreen open requests
            if (GetUIPanel(Fullscreen) != null && frameArea != AreaFull)
            {
                if (force) SetUIPanel(Fullscreen, null, true);
                else { Events.Add("failed " + frame.Name); return; }
            }

            // If we have a "center" frame open, only listen to other "center" open requests
            Panel centerFrame = GetUIPanel(Center);
            string centerArea = null; int centerPushable = 0;
            if (centerFrame != null)
            {
                if (centerFrame.AllowOtherPanels)
                {
                    HideUIPanel(centerFrame);
                    centerFrame = null;
                }
                else
                {
                    centerArea = centerFrame.Area;
                    if (centerArea == AreaCenter && frameArea != AreaCenter && frameArea != AreaFull)
                    {
                        if (force) SetUIPanel(Center, null, true);
                        else { Events.Add("failed " + frame.Name); return; }
                    }
                    centerPushable = centerFrame.Pushable;
                }
            }

            // Full-screen frames just replace each other
            if (frameArea == AreaFull)
            {
                CloseAllWindows(false);
                SetUIPanel(Fullscreen, frame, false);
                return;
            }

            // Native "center" frames just replace each other, and they take priority over pushed frames
            if (frameArea == AreaCenter)
            {
                CloseWindows(false, null);
                if (!frame.AllowOtherPanels) Events.Add("closeallbags");
                SetUIPanel(Center, frame, true);
                return;
            }

            // Doublewide frames take up the left and center spots
            if (frameArea == AreaDoublewide)
            {
                Panel leftFrame0 = GetUIPanel(Left);
                if (leftFrame0 != null)
                {
                    int leftPushable0 = leftFrame0.Pushable;
                    if (leftPushable0 > 0 && CanShowRightUIPanel(leftFrame0)) MoveUIPanel(Left, Right, true);   // Push left to right
                    else if (centerFrame != null && CanShowRightUIPanel(centerFrame)) MoveUIPanel(Center, Right, true);
                }
                SetUIPanel(Doublewide, frame, false);
                return;
            }

            // If not pushable, close any doublewide frames
            Panel doublewideFrame = GetUIPanel(Doublewide);
            if (doublewideFrame != null)
            {
                if (framePushable == 0)
                {
                    // Set as left (closes doublewide) and slide over the right frame
                    SetUIPanel(Left, frame, true);
                    MoveUIPanel(Right, Center, false);
                }
                else if (CanShowRightUIPanel(frame)) SetUIPanel(Right, frame, false);   // Set as right
                else SetUIPanel(Left, frame, false);
                return;
            }

            // Try to put it on the left
            Panel leftFrame = GetUIPanel(Left);
            if (leftFrame == null) { SetUIPanel(Left, frame, false); return; }
            int leftPushable = leftFrame.Pushable;

            // Two open already
            Panel rightFrame = GetUIPanel(Right);
            if (centerFrame != null && rightFrame == null)
            {
                if (leftPushable == 0 && framePushable == 0) SetUIPanel(Left, frame, false);   // Replace left
                else if ((framePushable > centerPushable || centerArea == AreaCenter) && CanShowRightUIPanel(frame))
                    SetUIPanel(Right, frame, false);   // This one is highest priority, show as right
                else if (framePushable < leftPushable)
                {
                    if (centerArea == AreaCenter)
                    {
                        if (CanShowRightUIPanel(leftFrame)) { MoveUIPanel(Left, Right, true); SetUIPanel(Left, frame, false); }   // Skip center
                        else SetUIPanel(Left, frame, false);   // Replace left
                    }
                    else
                    {
                        if (CanShowUIPanels(frame, leftFrame, centerFrame))
                        {
                            // Shift both
                            MoveUIPanel(Center, Right, true);
                            MoveUIPanel(Left, Center, true);
                            SetUIPanel(Left, frame, false);
                        }
                        else SetUIPanel(Left, frame, false);   // Replace left
                    }
                }
                else if (framePushable <= centerPushable && centerArea != AreaCenter && CanShowUIPanels(leftFrame, frame, centerFrame))
                {
                    // Push center
                    MoveUIPanel(Center, Right, true);
                    SetUIPanel(Center, frame, false);
                }
                else if (framePushable <= centerPushable && centerArea != AreaCenter) SetUIPanel(Left, frame, false);   // Replace left
                else SetUIPanel(Center, frame, false);   // Replace center
                return;
            }

            // If there's only one open...
            if (centerFrame == null)
            {
                // If neither is pushable, replace
                if (leftPushable == 0 && framePushable == 0) { SetUIPanel(Left, frame, false); return; }
                // Highest priority goes to center
                if (leftPushable > framePushable) { MoveUIPanel(Left, Center, true); SetUIPanel(Left, frame, false); }
                else SetUIPanel(Center, frame, false);
                return;
            }

            // Three are shown
            int rightPushable = rightFrame != null ? rightFrame.Pushable : 0;
            if (framePushable > rightPushable)
            {
                // This one is highest priority, slide the other two over
                if (CanShowUIPanels(centerFrame, rightFrame, frame))
                {
                    MoveUIPanel(Center, Left, true);
                    MoveUIPanel(Right, Center, true);
                    SetUIPanel(Right, frame, false);
                }
                else
                {
                    MoveUIPanel(Right, Left, true);
                    SetUIPanel(Center, frame, false);
                }
            }
            else if (framePushable > centerPushable)
            {
                // This one is middle priority, so move the center frame to the left
                MoveUIPanel(Center, Left, true);
                SetUIPanel(Center, frame, false);
            }
            else SetUIPanel(Left, frame, false);
        }

        // ------------------------------------------------------------------ SetUIPanel :1522-1584
        private void SetUIPanel(string key, Panel frame, bool skipSetPoint)
        {
            if (key == Fullscreen)
            {
                Panel oldFrame = _fullscreen;
                _fullscreen = frame;
                if (oldFrame != null) FrameHide(oldFrame);
                if (frame != null) { Events.Add("uiparent hidden"); FrameShow(frame); }
                else Events.Add("uiparent shown");
                return;
            }
            else if (key == Doublewide)
            {
                Panel oldLeft = _left, oldCenter = _center, oldDoubleWide = _doublewide;
                _doublewide = frame;
                _left = null;
                _center = null;
                if (oldDoubleWide != null) FrameHide(oldDoubleWide);
                if (oldLeft != null) FrameHide(oldLeft);
                if (oldCenter != null) FrameHide(oldCenter);
            }
            else if (key != Left && key != Center && key != Right) return;
            else
            {
                Panel oldFrame = GetUIPanel(key);
                Slot(key, frame);
                if (oldFrame != null) FrameHide(oldFrame);
                else if (_doublewide != null && (key == Left || key == Center))
                {
                    FrameHide(_doublewide);
                    _doublewide = null;
                }
            }

            if (!skipSetPoint) UpdateUIPanelPositions(null);

            if (frame != null)
            {
                FrameShow(frame);
                // CloseChildWindows (UIChildWindows) - the caller's child windows close here (not modelled)
            }
        }

        // ------------------------------------------------------------------ MoveUIPanel :1586-1600
        private void MoveUIPanel(string current, string @new, bool skipSetPoint)
        {
            if (current != Left && current != Center && current != Right && @new != Left && @new != Center && @new != Right) return;
            SetUIPanel(@new, null, skipSetPoint);
            Panel cur = GetUIPanel(current);
            if (cur != null)
            {
                Slot(@new, cur);
                Slot(current, null);
                if (!skipSetPoint) UpdateUIPanelPositions(null);
            }
        }

        // ------------------------------------------------------------------ FramePositionDelegate:HideUIPanel :1602-1644
        private void DelegateHide(Panel frame, bool skipSetPoint)
        {
            // If we're hiding the full-screen frame, just hide it
            if (frame == GetUIPanel(Fullscreen)) { SetUIPanel(Fullscreen, null, false); return; }

            // If we're hiding the right frame, just hide it
            if (frame == GetUIPanel(Right)) { SetUIPanel(Right, null, skipSetPoint); return; }
            else if (frame == GetUIPanel(Doublewide))
            {
                // Slide over any right frame (hides the doublewide)
                MoveUIPanel(Right, Left, skipSetPoint);
                return;
            }

            // If we're hiding the center frame, slide over any right frame
            Panel centerFrame = GetUIPanel(Center);
            if (frame == centerFrame) MoveUIPanel(Right, Center, skipSetPoint);
            else if (frame == GetUIPanel(Left))
            {
                // If we're hiding the left frame, move the other frames left, unless the center is a native center frame
                if (centerFrame != null)
                {
                    string area = centerFrame.Area;
                    if (area != null)
                    {
                        if (area == AreaCenter)
                        {
                            MoveUIPanel(Right, Left, skipSetPoint);   // Slide left, skip the center
                            return;
                        }
                        // Slide everything left
                        MoveUIPanel(Center, Left, true);
                        MoveUIPanel(Right, Center, skipSetPoint);
                        return;
                    }
                }
                SetUIPanel(Left, null, skipSetPoint);
            }
            else FrameHide(frame);
        }

        // ------------------------------------------------------------------ UpdateUIPanelPositions :1654-1745
        public void UpdateUIPanelPositions(Panel currentFrame)
        {
            if (_updating) return;
            _updating = true;

            float topOffset = TopOffset, leftOffset = LeftOffset;
            float centerOffset = CenterOffset, rightOffset = RightOffset;

            Panel frame = GetUIPanel(Left);
            if (frame != null)
            {
                Place(frame, leftOffset + frame.XOffset, topOffset + frame.YOffset);
                centerOffset = leftOffset + frame.Width + frame.XOffset;
                CenterOffset = centerOffset;
            }
            else
            {
                frame = GetUIPanel(Doublewide);
                if (frame != null)
                {
                    Place(frame, leftOffset + frame.XOffset, topOffset + frame.YOffset);
                    rightOffset = leftOffset + frame.Width + frame.XOffset;
                    RightOffset = rightOffset;
                }
            }

            frame = GetUIPanel(Center);
            if (frame != null)
            {
                if (CanShowCenterUIPanel(frame))
                {
                    if (frame.Area != AreaCenter) Place(frame, centerOffset + frame.XOffset, topOffset + frame.YOffset);
                    rightOffset = centerOffset + frame.Width + frame.XOffset;
                }
                else
                {
                    if (frame == currentFrame)
                    {
                        frame = GetUIPanel(Left) ?? GetUIPanel(Doublewide);
                        if (frame != null)
                        {
                            DelegateHide(frame, false);
                            _updating = false;
                            UpdateUIPanelPositions(currentFrame);
                            return;
                        }
                    }
                    SetUIPanel(Center, null, true);
                    rightOffset = centerOffset + DefaultFrameWidth;
                }
            }
            else if (GetUIPanel(Doublewide) == null)
            {
                if (GetUIPanel(Left) != null) rightOffset = centerOffset + DefaultFrameWidth;
                else rightOffset = leftOffset + DefaultFrameWidth * 2f;
            }
            RightOffset = rightOffset;

            frame = GetUIPanel(Right);
            if (frame != null)
            {
                if (CanShowRightUIPanel(frame)) Place(frame, rightOffset + frame.XOffset, topOffset + frame.YOffset);
                else
                {
                    if (frame == currentFrame)
                    {
                        frame = GetUIPanel(Center) ?? GetUIPanel(Left) ?? GetUIPanel(Doublewide);
                        if (frame != null)
                        {
                            DelegateHide(frame, false);
                            _updating = false;
                            UpdateUIPanelPositions(currentFrame);
                            return;
                        }
                    }
                    SetUIPanel(Right, null, true);
                }
            }
            _updating = false;
        }

        private static void Place(Panel p, float x, float y) { p.X = x; p.Y = y; p.Placed = true; }

        // ------------------------------------------------------------------ helpers :2001-2091
        public float GetMaxUIPanelsWidth()
        {
            float bufferBoundary = ScreenWidth - RightOffsetBuffer;
            return MinimapShown ? Math.Min(MinimapLeft, bufferBoundary) : bufferBoundary;
        }

        public bool CanShowRightUIPanel(Panel frame)
        {
            float width = frame != null ? frame.Width : DefaultFrameWidth;
            return RightOffset + width < GetMaxUIPanelsWidth();
        }

        public bool CanShowCenterUIPanel(Panel frame)
        {
            float width = frame != null ? frame.Width : DefaultFrameWidth;
            return CenterOffset + width < GetMaxUIPanelsWidth();
        }

        public bool CanShowUIPanels(Panel leftFrame, Panel centerFrame, Panel rightFrame)
        {
            float offset = LeftOffset;
            if (leftFrame != null)
            {
                offset += leftFrame.Width;
                if (centerFrame != null)
                {
                    // UIParent.lua:2051-2056: a non-"center" centre frame counts its UIPanelWindows width or DEFAULT_FRAME_WIDTH (not its frame width)
                    if (centerFrame.Area != AreaCenter) offset += centerFrame.InfoWidth > 0f ? centerFrame.InfoWidth : DefaultFrameWidth;
                    else offset += centerFrame.Width;
                    if (rightFrame != null) offset += rightFrame.Width;
                }
            }
            else if (centerFrame != null) offset = centerFrame.Width;
            return offset < GetMaxUIPanelsWidth();
        }

        public bool CanOpenPanels()
        {
            Panel centerFrame = GetUIPanel(Center);
            if (centerFrame == null) return true;
            if (centerFrame.Area == AreaCenter && !centerFrame.AllowOtherPanels) return false;
            return true;
        }

        // ------------------------------------------------------------------ CloseWindows :2121-2157, CloseAllWindows :2170-2182
        public bool CloseWindows(bool ignoreCenter, Panel frameToIgnore)
        {
            Panel leftFrame = GetUIPanel(Left), centerFrame = GetUIPanel(Center), rightFrame = GetUIPanel(Right);
            Panel doublewideFrame = GetUIPanel(Doublewide), fullScreenFrame = GetUIPanel(Fullscreen);
            bool found = leftFrame != null || centerFrame != null || rightFrame != null || doublewideFrame != null || fullScreenFrame != null;

            if (frameToIgnore == null || frameToIgnore != leftFrame) HideUIPanel(leftFrame, true);
            HideUIPanel(fullScreenFrame, true);
            HideUIPanel(doublewideFrame, true);
            if (frameToIgnore == null || frameToIgnore != centerFrame)
                if (centerFrame != null && (centerFrame.Area != AreaCenter || !ignoreCenter)) HideUIPanel(centerFrame, true);
            if (frameToIgnore == null || frameToIgnore != rightFrame)
                if (rightFrame != null) HideUIPanel(rightFrame, true);
            // CloseSpecialWindows (UISpecialFrames): the caller closes its non-panel windows here and ORs the result
            UpdateUIPanelPositions(null);
            return found;
        }

        public bool CloseAllWindows(bool ignoreCenter)
        {
            // the container frames (bags) are hidden first by the caller; their visibility ORs into the result there
            return CloseWindows(ignoreCenter, null);
        }

        public string Describe()
        {
            return "left " + N(_left) + ", center " + N(_center) + ", right " + N(_right) + ", doublewide " + N(_doublewide)
                + ", full " + N(_fullscreen) + " | CENTER_OFFSET " + CenterOffset + " RIGHT_OFFSET " + RightOffset;
        }

        private static string N(Panel p)
        {
            if (p == null) return "-";
            return p.Placed ? p.Name + "@(" + p.X.ToString("0.#") + "," + p.Y.ToString("0.#") + ")" : p.Name + "@centre";
        }
    }
}
