// iteration 11 - owner uicore (proposal-uicore.md 5.7): Interface\FrameXML\GlobalStrings.lua of the player's own install, parsed once at
// runtime (so a deDE / frFR install shows its own language; nothing is shipped). The parser is the UicGlobalStrings probe's prototype
// (P/harness/Probes/D11/uicore/UicoreProbes.cs: 8999 strings, 0 lines not understood on the user's enUS install). Pure C#: no UnityEngine.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure
{
    public static class UiGlobalStrings
    {
        /// <summary>The path inside the install's MPQs.</summary>
        public const string Path = @"Interface\FrameXML\GlobalStrings.lua";

        private static Dictionary<string, string> _map;

        /// <summary>True once the install's file was parsed.</summary>
        public static bool Loaded { get { return _map != null; } }

        /// <summary>How many strings the install's file gave (0 before Load).</summary>
        public static int Count { get { return _map != null ? _map.Count : 0; } }

        /// <summary>Lines of the file that were neither empty, a comment nor <c>KEY = "value";</c> (0 on the user's enUS install).</summary>
        public static int OddLines { get; private set; }

        /// <summary>Parses the file's bytes (UTF-8, Lua escapes \" \\ \n \t). False (nothing changes) for null / empty data.</summary>
        public static bool Load(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            int odd;
            _map = Parse(data, out odd);
            OddLines = odd;
            return true;
        }

        /// <summary>Forgets the parsed strings (the harness; a new install).</summary>
        public static void Reset() { _map = null; OddLines = 0; }

        /// <summary>The install's text of a GlobalStrings key; the fallback (the caller quotes the enUS text with its line) when the file is
        /// not read yet or has no such key; the key itself when neither exists.</summary>
        public static string G(string key, string fallback = null)
        {
            string v;
            if (key != null && _map != null && _map.TryGetValue(key, out v)) return v;
            return fallback ?? key ?? "";
        }

        /// <summary>True when the install's file has the key.</summary>
        public static bool Has(string key)
        {
            return key != null && _map != null && _map.ContainsKey(key);
        }

        /// <summary>The key's text (G(key)) with the printf conversions filled in order (or by %n$ position) and WoW's
        /// <c>|4singular:plural;</c> chosen by the number before it.</summary>
        public static string Format(string key, params object[] args)
        {
            return FormatText(G(key), args);
        }

        /// <summary>Format with an enUS fallback for the key (used before the file is read).</summary>
        public static string FormatOr(string key, string fallback, params object[] args)
        {
            return FormatText(G(key, fallback), args);
        }

        /// <summary>The printf subset GlobalStrings uses: %s %d %i %f %.Nf %x %c %%, %n$s; then |4a:b; grammar.</summary>
        public static string FormatText(string text, params object[] args)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length + 32);
            int next = 0;
            double lastNumber = double.NaN;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '|' && i + 1 < text.Length && text[i + 1] == '4')
                {
                    int colon = text.IndexOf(':', i + 2), semi = colon >= 0 ? text.IndexOf(';', colon + 1) : -1;
                    if (colon > 0 && semi > colon)
                    {
                        string one = text.Substring(i + 2, colon - i - 2), many = text.Substring(colon + 1, semi - colon - 1);
                        double n = double.IsNaN(lastNumber) ? LastNumberIn(sb) : lastNumber;
                        sb.Append(n == 1.0 ? one : many);
                        i = semi;
                        continue;
                    }
                }
                if (c != '%' || i + 1 >= text.Length) { sb.Append(c); continue; }
                if (text[i + 1] == '%') { sb.Append('%'); i++; continue; }
                // %[n$][flags][width][.precision]conv
                int j = i + 1;
                int pos = -1;
                int digitsStart = j;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                if (j < text.Length && text[j] == '$' && j > digitsStart)
                {
                    pos = int.Parse(text.Substring(digitsStart, j - digitsStart), CultureInfo.InvariantCulture) - 1;
                    j++;
                }
                else j = digitsStart;
                while (j < text.Length && (text[j] == '-' || text[j] == '+' || text[j] == ' ' || text[j] == '0' || text[j] == '#')) j++;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                int precision = -1;
                if (j < text.Length && text[j] == '.')
                {
                    j++;
                    int ps = j;
                    while (j < text.Length && char.IsDigit(text[j])) j++;
                    precision = j > ps ? int.Parse(text.Substring(ps, j - ps), CultureInfo.InvariantCulture) : 0;
                }
                if (j >= text.Length) { sb.Append(text, i, text.Length - i); break; }
                char conv = text[j];
                if ("sdifgxXcu".IndexOf(conv) < 0) { sb.Append(c); continue; }
                int argIndex = pos >= 0 ? pos : next++;
                object a = args != null && argIndex >= 0 && argIndex < args.Length ? args[argIndex] : null;
                sb.Append(Convert(a, conv, precision, ref lastNumber));
                i = j;
            }
            return sb.ToString();
        }

        private static string Convert(object a, char conv, int precision, ref double lastNumber)
        {
            if (a == null) return "";
            double d;
            bool numeric = TryNumber(a, out d);
            switch (conv)
            {
                case 'd':
                case 'i':
                case 'u':
                    if (!numeric) return a.ToString();
                    lastNumber = Math.Truncate(d);
                    return ((long)Math.Truncate(d)).ToString(CultureInfo.InvariantCulture);
                case 'f':
                case 'g':
                    if (!numeric) return a.ToString();
                    lastNumber = d;
                    return d.ToString("F" + (precision >= 0 ? precision : 6), CultureInfo.InvariantCulture);
                case 'x':
                case 'X':
                    if (!numeric) return a.ToString();
                    lastNumber = d;
                    string h = ((long)d).ToString("x", CultureInfo.InvariantCulture);
                    return conv == 'X' ? h.ToUpperInvariant() : h;
                case 'c':
                    return numeric ? ((char)(int)d).ToString() : a.ToString();
                default:
                    if (numeric) lastNumber = d;
                    return Convert(a);
            }
        }

        private static string Convert(object a)
        {
            var f = a as IFormattable;
            return f != null ? f.ToString(null, CultureInfo.InvariantCulture) : a.ToString();
        }

        private static bool TryNumber(object a, out double d)
        {
            d = 0;
            if (a is int) { d = (int)a; return true; }
            if (a is long) { d = (long)a; return true; }
            if (a is float) { d = (float)a; return true; }
            if (a is double) { d = (double)a; return true; }
            if (a is uint) { d = (uint)a; return true; }
            if (a is short) { d = (short)a; return true; }
            if (a is byte) { d = (byte)a; return true; }
            if (a is ulong) { d = (ulong)a; return true; }
            if (a is decimal) { d = (double)(decimal)a; return true; }
            return false;
        }

        /// <summary>The last whole number already written (|4 after a literal number in the text).</summary>
        private static double LastNumberIn(StringBuilder sb)
        {
            int end = sb.Length - 1;
            while (end >= 0 && !char.IsDigit(sb[end])) end--;
            if (end < 0) return double.NaN;
            int start = end;
            while (start > 0 && char.IsDigit(sb[start - 1])) start--;
            double v;
            return double.TryParse(sb.ToString(start, end - start + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        /// <summary>The prototype's parser (UicoreProbes.cs Parse): every <c>KEY = "value";</c> line; the rest counted as odd.</summary>
        public static Dictionary<string, string> Parse(byte[] data, out int odd)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            odd = 0;
            string text = Encoding.UTF8.GetString(data);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            string[] all = text.Split('\n');
            var sb = new StringBuilder();
            foreach (string raw in all)
            {
                string l = raw.TrimEnd('\r', ' ', '\t');
                if (l.Length == 0 || l.StartsWith("--", StringComparison.Ordinal)) continue;
                int eq = l.IndexOf(" = \"", StringComparison.Ordinal);
                bool keyOk = eq > 0;
                for (int i = 0; keyOk && i < eq; i++) { char c = l[i]; keyOk = char.IsLetterOrDigit(c) || c == '_'; }
                if (!keyOk || !l.EndsWith("\";", StringComparison.Ordinal)) { odd++; continue; }
                string body = l.Substring(eq + 4, l.Length - eq - 4 - 2);
                sb.Length = 0;
                for (int i = 0; i < body.Length; i++)
                {
                    char c = body[i];
                    if (c == '\\' && i + 1 < body.Length)
                    {
                        char n = body[++i];
                        sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);   // \" \\ \n \t ; anything else: the character itself
                    }
                    else sb.Append(c);
                }
                map[l.Substring(0, eq)] = sb.ToString();
            }
            return map;
        }
    }
}
