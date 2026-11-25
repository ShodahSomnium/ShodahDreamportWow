// iteration 11 - owner talents (new file; lifted from the designer's prototype P/harness/Probes/D11/talents/TalentProto.cs, which built
// every one of the 33 trees and every pet family view without a "blocked" message: proposal-talents.md 12.2)
// The branch and arrow layout of WoW's talent trees: a line-by-line port of FrameXML TalentFrameBase.lua (the user's install, dumped to
// P/harness/out_d11talents/): TalentFrame_Update's branch part (:236-312), TalentFrame_SetPrereqs (:374-387), TalentFrame_DrawLines (:390-520),
// texture coordinates TALENT_BRANCH_TEXTURECOORDS / TALENT_ARROW_TEXTURECOORDS (:20-82). Pure C# (harness-testable).
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>One 32x32 piece: TOPLEFT (X, Y) in scroll-child units, Y DOWN (= the Lua's yOffset negated); L, R, T, B = the Lua's
    /// SetTexCoord(left, right, top, bottom) as they are (L &gt; R = mirrored) -> UiKit.Pic(parent, name, tex, q.L, q.R, q.T, q.B, q.X, q.Y, 32, 32).
    /// Arrow = UI-TalentArrows on top of the buttons, else UI-TalentBranches under them.</summary>
    public struct TalentQuad { public bool Arrow; public float L, R, T, B, X, Y; }

    public static class TalentBranches
    {
        public const int MaxTiers = 15, Columns = 4, ButtonSize = 32, MaxBranchTextures = 30, MaxArrowTextures = 30;   // TalentFrameBase.lua:5-13
        public const float OffsetX = 35f, OffsetY = 20f;    // INITIAL_TALENT_OFFSET_X / _Y (TalentFrameBase.lua:15-16)
        public const float ButtonStep = 63f;                 // SetTalentButtonLocation (TalentFrameBase.lua:534-538): Col * 63 + 35, Row * 63 + 20

        sealed class Node { public bool Has; public int Up, Down, Left, Right, LeftArrow, RightArrow, TopArrow; }

        // TALENT_BRANCH_TEXTURECOORDS / TALENT_ARROW_TEXTURECOORDS (TalentFrameBase.lua:20-82): [1] = met, [-1] = not met
        static readonly float[][] Up = { new[] { 0.12890625f, 0.25390625f, 0f, 0.484375f }, new[] { 0.12890625f, 0.25390625f, 0.515625f, 1.0f } };
        static readonly float[][] Down = { new[] { 0f, 0.125f, 0f, 0.484375f }, new[] { 0f, 0.125f, 0.515625f, 1.0f } };
        static readonly float[][] Left = { new[] { 0.2578125f, 0.3828125f, 0f, 0.5f }, new[] { 0.2578125f, 0.3828125f, 0.5f, 1.0f } };
        static readonly float[][] Right = Left;
        static readonly float[][] TopRight = { new[] { 0.515625f, 0.640625f, 0f, 0.5f }, new[] { 0.515625f, 0.640625f, 0.5f, 1.0f } };
        static readonly float[][] TopLeft = { new[] { 0.640625f, 0.515625f, 0f, 0.5f }, new[] { 0.640625f, 0.515625f, 0.5f, 1.0f } };
        static readonly float[][] BottomRight = { new[] { 0.38671875f, 0.51171875f, 0f, 0.5f }, new[] { 0.38671875f, 0.51171875f, 0.5f, 1.0f } };
        static readonly float[][] BottomLeft = { new[] { 0.51171875f, 0.38671875f, 0f, 0.5f }, new[] { 0.51171875f, 0.38671875f, 0.5f, 1.0f } };
        static readonly float[][] TDown = { new[] { 0.64453125f, 0.76953125f, 0f, 0.5f }, new[] { 0.64453125f, 0.76953125f, 0.5f, 1.0f } };
        static readonly float[][] TUp = { new[] { 0.7734375f, 0.8984375f, 0f, 0.5f }, new[] { 0.7734375f, 0.8984375f, 0.5f, 1.0f } };
        static readonly float[][] ArrowTop = { new[] { 0f, 0.5f, 0f, 0.5f }, new[] { 0f, 0.5f, 0.5f, 1.0f } };
        static readonly float[][] ArrowRight = { new[] { 1.0f, 0.5f, 0f, 0.5f }, new[] { 1.0f, 0.5f, 0.5f, 1.0f } };
        static readonly float[][] ArrowLeft = { new[] { 0.5f, 1.0f, 0f, 0.5f }, new[] { 0.5f, 1.0f, 0.5f, 1.0f } };

        static float[] Pick(float[][] set, int met) { return met > 0 ? set[0] : set[1]; }

        /// <summary>The button's TOPLEFT in scroll-child units (y down): SetTalentButtonLocation (TalentFrameBase.lua:534-538).</summary>
        public static float ButtonX(TalentInfo t) { return t.Col * ButtonStep + OffsetX; }
        public static float ButtonY(TalentInfo t) { return t.Row * ButtonStep + OffsetY; }

        /// <summary>requirementsMet(t) = TalentFrame_SetPrereqs's value for talent t: its tier unlocked AND not force-desaturated (no free points /
        /// inactive spec, rank 0) AND its prerequisite at the needed rank (preview ranks while previewing). problems receives the Lua's message()
        /// texts ("blocked" layouts, &gt; 30 textures) - none on the 33 trees (measured).</summary>
        public static void Build(TalentTabInfo tab, Func<TalentInfo, bool> requirementsMet, List<TalentQuad> quads, List<string> problems)
        {
            if (tab == null || quads == null) return;
            var a = new Node[MaxTiers + 2, Columns + 2];
            for (int i = 0; i < MaxTiers + 2; i++) for (int j = 0; j < Columns + 2; j++) a[i, j] = new Node();
            // buttons in index order (Row, Col): mark the node, then SetPrereqs (one prerequisite per talent in 3.3.5a's Talent.dbc)
            foreach (var t in tab.Talents)
            {
                int tier = t.Row + 1, col = t.Col + 1;
                if (tier < 1 || tier > MaxTiers || col < 1 || col > Columns) { if (problems != null) problems.Add("talent " + t.Id + " outside the grid"); continue; }
                a[tier, col].Has = true;
                if (t.PrereqTalent <= 0) continue;
                TalentInfo pre = null;
                foreach (var x in tab.Talents) if (x.Id == t.PrereqTalent) { pre = x; break; }
                if (pre == null) { if (problems != null) problems.Add("prereq " + t.PrereqTalent + " of " + t.Id + " not in this tab"); continue; }
                DrawLines(a, tier, col, pre.Row + 1, pre.Col + 1, requirementsMet == null || requirementsMet(t) ? 1 : -1, problems);
            }
            // TalentFrame_Update :240-307
            bool ignoreUp = false;
            int branches = 0, arrows = 0;
            for (int i = 1; i <= MaxTiers; i++)
                for (int j = 1; j <= Columns; j++)
                {
                    var node = a[i, j];
                    float x = (j - 1) * ButtonStep + OffsetX + 2, y = -((i - 1) * ButtonStep) - OffsetY - 2;
                    if (node.Has)
                    {
                        if (node.Up != 0) { if (!ignoreUp) Add(quads, false, Pick(Up, node.Up), x, y + ButtonSize, ref branches); else ignoreUp = false; }
                        if (node.Down != 0) Add(quads, false, Pick(Down, node.Down), x, y - ButtonSize + 1, ref branches);
                        if (node.Left != 0) Add(quads, false, Pick(Left, node.Left), x - ButtonSize, y, ref branches);
                        if (node.Right != 0)
                        {
                            var temp = a[i, j + 1];
                            if (temp.Left != 0 && temp.Down < 0) Add(quads, false, Pick(Right, temp.Down), x + ButtonSize, y, ref branches);
                            else Add(quads, false, Pick(Right, node.Right), x + ButtonSize + 1, y, ref branches);
                        }
                        if (node.RightArrow != 0) Add(quads, true, Pick(ArrowRight, node.RightArrow), x + ButtonSize / 2 + 5, y, ref arrows);
                        if (node.LeftArrow != 0) Add(quads, true, Pick(ArrowLeft, node.LeftArrow), x - ButtonSize / 2 - 5, y, ref arrows);
                        if (node.TopArrow != 0) Add(quads, true, Pick(ArrowTop, node.TopArrow), x, y + ButtonSize / 2 + 5, ref arrows);
                    }
                    else
                    {
                        if (node.Up != 0 && node.Left != 0 && node.Right != 0) Add(quads, false, Pick(TUp, node.Up), x, y, ref branches);
                        else if (node.Down != 0 && node.Left != 0 && node.Right != 0) Add(quads, false, Pick(TDown, node.Down), x, y, ref branches);
                        else if (node.Left != 0 && node.Down != 0) { Add(quads, false, Pick(TopRight, node.Left), x, y, ref branches); Add(quads, false, Pick(Down, node.Down), x, y - 32, ref branches); }
                        else if (node.Left != 0 && node.Up != 0) Add(quads, false, Pick(BottomRight, node.Left), x, y, ref branches);
                        else if (node.Left != 0 && node.Right != 0) { Add(quads, false, Pick(Right, node.Right), x + ButtonSize, y, ref branches); Add(quads, false, Pick(Left, node.Left), x + 1, y, ref branches); }
                        else if (node.Right != 0 && node.Down != 0) { Add(quads, false, Pick(TopLeft, node.Right), x, y, ref branches); Add(quads, false, Pick(Down, node.Down), x, y - 32, ref branches); }
                        else if (node.Right != 0 && node.Up != 0) Add(quads, false, Pick(BottomLeft, node.Right), x, y, ref branches);
                        else if (node.Up != 0 && node.Down != 0) { Add(quads, false, Pick(Up, node.Up), x, y, ref branches); Add(quads, false, Pick(Down, node.Down), x, y - 32, ref branches); ignoreUp = true; }
                    }
                }
            if (problems != null && branches > MaxBranchTextures) problems.Add("Not enough branch textures (" + branches + ")");
            if (problems != null && arrows > MaxArrowTextures) problems.Add("Not enough arrow textures (" + arrows + ")");
        }

        static void Add(List<TalentQuad> quads, bool arrow, float[] tc, float x, float y, ref int count)
        {
            count++;
            quads.Add(new TalentQuad { Arrow = arrow, L = tc[0], R = tc[1], T = tc[2], B = tc[3], X = x, Y = -y });
        }

        /// <summary>TalentFrame_DrawLines (TalentFrameBase.lua:390-520), arrays 1-based like the Lua.</summary>
        static void DrawLines(Node[,] a, int buttonTier, int buttonColumn, int tier, int column, int met, List<string> problems)
        {
            if (buttonColumn == column)
            {
                if (buttonTier - tier > 1)
                    for (int i = tier + 1; i <= buttonTier - 1; i++)
                        if (a[i, buttonColumn].Has) { if (problems != null) problems.Add("Error this layout is blocked vertically (tier " + buttonTier + " col " + buttonColumn + ")"); return; }
                for (int i = tier; i <= buttonTier - 1; i++)
                {
                    a[i, buttonColumn].Down = met;
                    if (i + 1 <= buttonTier - 1) a[i + 1, buttonColumn].Up = met;
                }
                a[buttonTier, buttonColumn].TopArrow = met;
                return;
            }
            if (buttonTier == tier)
            {
                int l = Math.Min(buttonColumn, column), r = Math.Max(buttonColumn, column);
                if (r - l > 1)
                    for (int i = l + 1; i <= r - 1; i++)
                        if (a[tier, i].Has) { if (problems != null) problems.Add("there's a blocker " + tier + " " + i); return; }
                for (int i = l; i <= r - 1; i++) { a[tier, i].Right = met; a[tier, i + 1].Left = met; }
                if (buttonColumn < column) a[buttonTier, buttonColumn].RightArrow = met; else a[buttonTier, buttonColumn].LeftArrow = met;
                return;
            }
            int left = Math.Min(buttonColumn, column), right = Math.Max(buttonColumn, column);
            if (left == column) left = left + 1; else right = right - 1;
            bool blocked = false;
            for (int i = left; i <= right; i++) if (a[tier, i].Has) blocked = true;
            left = Math.Min(buttonColumn, column); right = Math.Max(buttonColumn, column);
            if (!blocked)
            {
                a[tier, buttonColumn].Down = met;
                a[buttonTier, buttonColumn].Up = met;
                for (int i = tier; i <= buttonTier - 1; i++) { a[i, buttonColumn].Down = met; a[i + 1, buttonColumn].Up = met; }
                for (int i = left; i <= right - 1; i++) { a[tier, i].Right = met; a[tier, i + 1].Left = met; }
                a[buttonTier, buttonColumn].TopArrow = met;
                return;
            }
            if (left == buttonColumn) left = left + 1; else right = right - 1;
            for (int i = left; i <= right; i++)
                if (a[buttonTier, i].Has) { if (problems != null) problems.Add("Error, this layout is undrawable (tier " + buttonTier + " col " + i + ")"); return; }
            left = Math.Min(buttonColumn, column); right = Math.Max(buttonColumn, column);
            for (int i = tier; i <= buttonTier - 1; i++) { a[i, column].Up = met; a[i + 1, column].Down = met; }
            if (buttonColumn < column) a[buttonTier, buttonColumn].RightArrow = met; else a[buttonTier, buttonColumn].LeftArrow = met;
        }
    }
}
