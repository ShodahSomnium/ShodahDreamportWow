// Lead 2026-10-12 (the user: "Please add macros in the settings, so players can use them in game."): WoW keeps macros in the client
// (macros-cache.txt), and an action button holds ACTION_BUTTON_MACRO (0x40) + the macro's id (Player.h:220-228, WowActionSlots.TypeMacro) -
// the realm stores the button, never the text. Ours: up to MaxMacros macros saved in WowSettings.MacroData (encoded, one string), edited on
// the Settings window's Macros page (UiMacroPage), run line by line when their button is used (WowActionSlots.MacroRunner): "#" lines skipped
// (#showtooltip), "/" lines through the chat's commands (UiChat.RunLine: /cast /use /target /startattack ... and every chat command), other
// lines said as chat. The icon: the first /cast spell's or /use item's, else the question mark (WoW's #showtooltip look, ours).
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public static class UiMacros
    {
        public const int MaxMacros = 18;                 // MAX_CHARACTER_MACROS (MacroFrame.lua) - ours: one set
        public const int MaxBody = 255;                  // MacroFrameText letters (MacroFrame.xml maxLetters 255)
        public const int MaxName = 16;                   // MacroPopupEditBox letters (MacroFrame.xml)
        public const string QuestionIcon = @"Interface\Icons\INV_Misc_QuestionMark";

        public sealed class Macro { public string Name = "", Body = ""; }

        private static readonly Macro[] Slots = new Macro[MaxMacros];
        private static string _loadedFrom;
        private static int _runs;

        /// <summary>Hooks the sessions' macro buttons (once).</summary>
        public static void Hook()
        {
            WowActionSlots.MacroRunner = Run;
        }

        /// <summary>The macro with this button id (1..MaxMacros), null when none.</summary>
        public static Macro Get(int id)
        {
            Load();
            return id >= 1 && id <= MaxMacros ? Slots[id - 1] : null;
        }

        public static void Set(int id, Macro m)
        {
            Load();
            if (id < 1 || id > MaxMacros) return;
            Slots[id - 1] = m;
            Save();
        }

        /// <summary>The first free id (1..MaxMacros), 0 = full.</summary>
        public static int FreeId()
        {
            Load();
            for (int i = 0; i < MaxMacros; i++) if (Slots[i] == null) return i + 1;
            return 0;
        }

        public static int Packed(int id) { return WowActionSlots.Pack(WowActionSlots.TypeMacro, id); }

        // ------------------------------------------------------------------ storage (WowSettings.MacroData)

        private static void Load()
        {
            string data = WowSettings.MacroData ?? "";
            if (ReferenceEquals(data, _loadedFrom)) return;
            _loadedFrom = data;
            for (int i = 0; i < MaxMacros; i++) Slots[i] = null;
            if (data.Length == 0) return;
            foreach (string rec in data.Split('~'))
            {
                string[] f = rec.Split('|');
                int id;
                if (f.Length < 3 || !int.TryParse(f[0], out id) || id < 1 || id > MaxMacros) continue;
                Slots[id - 1] = new Macro { Name = Unescape(f[1]), Body = Unescape(f[2]) };
            }
        }

        private static void Save()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < MaxMacros; i++)
            {
                if (Slots[i] == null) continue;
                if (sb.Length > 0) sb.Append('~');
                sb.Append(i + 1).Append('|').Append(Escape(Slots[i].Name)).Append('|').Append(Escape(Slots[i].Body));
            }
            WowSettings.MacroData = sb.ToString();
            _loadedFrom = WowSettings.MacroData;
            WowSettings.NotifyChanged();
        }

        /// <summary>Printable ASCII stays (CleanData keeps it) except % | ~ =; the rest as %XX / %uXXXX.</summary>
        private static string Escape(string s)
        {
            var b = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (c >= ' ' && c < (char)127 && c != '%' && c != '|' && c != '~' && c != '=') b.Append(c);
                else if (c < 256) b.Append('%').Append(((int)c).ToString("X2"));
                else b.Append("%u").Append(((int)c).ToString("X4"));
            }
            return b.ToString();
        }

        private static string Unescape(string s)
        {
            var b = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '%') { b.Append(c); continue; }
                int v;
                if (i + 5 < s.Length && s[i + 1] == 'u' && int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out v)) { b.Append((char)v); i += 5; continue; }
                if (i + 2 < s.Length && int.TryParse(s.Substring(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out v)) { b.Append((char)v); i += 2; continue; }
            }
            return b.ToString();
        }

        // ------------------------------------------------------------------ running

        /// <summary>WowActionSlots.MacroRunner: runs macro id's lines (true = handled, even when empty).</summary>
        public static bool Run(int id, ulong targetGuid)
        {
            Macro m = Get(id);
            if (m == null) { Say("That macro is empty."); return true; }   // ours
            if (_runs++ < 20) WowLog.Info("Macros: running '" + m.Name + "' (" + id + ") (logged 20 times).");
            foreach (string raw in (m.Body ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                try { UiChat.RunLine(line); }
                catch (Exception e) { WowLog.Warn("Macros: line '" + line + "' threw " + e.Message); }
            }
            return true;
        }

        /// <summary>The icon: the first /cast spell's (spellbook) or /use item's (bags), else the question mark.</summary>
        public static string IconOf(PlayerState p, Macro m)
        {
            if (m == null) return QuestionIcon;
            foreach (string raw in (m.Body ?? "").Split('\n'))
            {
                string line = raw.Trim();
                string cmd, arg;
                if (!SplitCommand(line, out cmd, out arg)) continue;
                if (cmd == "/CAST" || cmd == "/USE")
                {
                    bool self;
                    string what = StripOptions(arg, out self);
                    AbilityView a = FindSpell(p, what);
                    if (a != null && !string.IsNullOrEmpty(a.IconPath)) return a.IconPath;
                    ItemView it = FindItem(p, what);
                    if (it != null && !string.IsNullOrEmpty(it.IconPath)) return it.IconPath;
                }
            }
            return QuestionIcon;
        }

        public static string Tooltip(Macro m)
        {
            if (m == null) return "<size=14><color=#ffffff>Macro</color></size>\n<color=#808080>Empty - write it on the Settings window's Macros page.</color>";
            return "<size=14><color=#ffffff>" + m.Name + "</color></size>\n<color=#ffd100>" + (m.Body ?? "").Replace("<", "&lt;") + "</color>";
        }

        // ------------------------------------------------------------------ helpers (also UiChat's /cast /use)

        public static bool SplitCommand(string line, out string cmd, out string arg)
        {
            cmd = arg = "";
            if (string.IsNullOrEmpty(line) || line[0] != '/') return false;
            int sp = line.IndexOf(' ');
            cmd = (sp < 0 ? line : line.Substring(0, sp)).ToUpperInvariant();
            arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            return true;
        }

        /// <summary>"[@player] Flash Heal; Heal" -> "Flash Heal" (the first clause; ours - no conditions are tested); self = @player / target=player.</summary>
        public static string StripOptions(string arg, out bool self)
        {
            self = false;
            string a = (arg ?? "").Trim();
            int semi = a.IndexOf(';');
            if (semi >= 0) a = a.Substring(0, semi).Trim();
            while (a.StartsWith("["))
            {
                int close = a.IndexOf(']');
                if (close < 0) break;
                string opts = a.Substring(1, close - 1).ToLowerInvariant();
                if (opts.Contains("@player") || opts.Contains("target=player")) self = true;
                a = a.Substring(close + 1).Trim();
            }
            return a;
        }

        /// <summary>A spellbook spell by name; "Name(Rank n)" picks that rank, else the highest rank known.</summary>
        public static AbilityView FindSpell(PlayerState p, string what)
        {
            if (p == null || p.Spellbook == null || string.IsNullOrEmpty(what)) return null;
            string name = what, rank = null;
            int open = what.IndexOf('(');
            if (open > 0 && what.EndsWith(")")) { name = what.Substring(0, open).Trim(); rank = what.Substring(open + 1, what.Length - open - 2).Trim(); }
            AbilityView best = null;
            int bestRank = -1;
            foreach (AbilityView a in p.Spellbook)
            {
                if (a == null || !string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (rank != null) { if (string.Equals(a.Rank ?? "", rank, StringComparison.OrdinalIgnoreCase)) return a; continue; }
                int r = RankNumber(a.Rank);
                if (r > bestRank) { bestRank = r; best = a; }
            }
            return best;
        }

        private static int RankNumber(string rank)
        {
            if (string.IsNullOrEmpty(rank)) return 0;
            int n = 0;
            foreach (char c in rank) if (c >= '0' && c <= '9') n = n * 10 + (c - '0');
            return n;
        }

        /// <summary>An item in the bags by name (its bag index, -1 = none).</summary>
        public static ItemView FindItem(PlayerState p, string what)
        {
            int i = FindItemIndex(p, what);
            return i >= 0 ? p.Bags[i] : null;
        }

        public static int FindItemIndex(PlayerState p, string what)
        {
            if (p == null || p.Bags == null || string.IsNullOrEmpty(what)) return -1;
            for (int i = 0; i < p.Bags.Count; i++)
                if (p.Bags[i] != null && string.Equals(p.Bags[i].Name, what, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static void Say(string text)
        {
            if (UiActionCursor.Say != null) UiActionCursor.Say(text, UiArt.InfoYellow);
        }
    }
}
