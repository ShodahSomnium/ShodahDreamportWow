using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>Numbers a tooltip may reference ($AP, weapon damage...). Fill what you know; zeros are fine.</summary>
    public sealed class SpellTextContext
    {
        public int CasterLevel = 1;
        public float AttackPower, RangedAttackPower, SpellPower;
        public float MainWeaponMin = 1, MainWeaponMax = 2, MainWeaponSpeed = 2f;
        public float RangedWeaponMin, RangedWeaponMax, RangedWeaponSpeed = 2f;
        public string HomeName;   // iteration 11 (npc H6): the hearthstone's home ($z); null / empty = "your home"
    }

    /// <summary>
    /// Resolves the $-tokens of Spell.dbc descriptions/tooltips into plain text (the client's own format, read from the files):
    /// $s1 $m1 $M1 $o1 $t1 $d $a1 $h $n $u $x1 $b1 $e1 $q (with optional spell-id prefix: $7922d), $/10;s2 and $*5;s1 (scale),
    /// ${expression}[.decimals] with $AP $RAP $SP $MWB $mwb $MWS $PL, $&lt;var&gt; from SpellDescriptionVariables, $lsingular:plural;
    /// $gmale:female;, $?sID[yes][no] / $?aID / chains (the offline game takes the "no" branch: no talents/glyphs). Unknown tokens
    /// are dropped. Pure C#; never throws (falls back to the raw text).
    /// </summary>
    public static class SpellText
    {
        public static string Resolve(SpellDb db, SpellInfo spell, string text, SpellTextContext ctx)
        {
            if (string.IsNullOrEmpty(text) || spell == null) return text ?? "";
            try
            {
                var r = new Resolver(db, spell, ctx ?? new SpellTextContext());
                return r.Run(text, 0);
            }
            catch (Exception)
            {
                return text;
            }
        }

        private sealed class Resolver
        {
            private readonly SpellDb _db;
            private readonly SpellInfo _spell;
            private readonly SpellTextContext _ctx;
            private Dictionary<string, string> _vars;
            private double _lastNumber = 2;

            public Resolver(SpellDb db, SpellInfo spell, SpellTextContext ctx) { _db = db; _spell = spell; _ctx = ctx; }

            public string Run(string text, int depth)
            {
                if (depth > 6) return "";
                var sb = new StringBuilder(text.Length + 16);
                int i = 0;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (c != '$') { sb.Append(c); i++; continue; }
                    int start = i;
                    string piece = Token(text, ref i, depth);
                    if (piece == null) { sb.Append(text, start, Math.Min(i, text.Length) - start); continue; }
                    sb.Append(piece);
                }
                return sb.ToString();
            }

            // Parses one token starting at text[i] == '$', advances i, returns its text (never null; "" when unknown).
            private string Token(string text, ref int i, int depth)
            {
                i++;   // '$'
                if (i >= text.Length) return "";
                char c = text[i];
                if (c == '{')
                {
                    int end = Matching(text, i, '{', '}');
                    string expr = text.Substring(i + 1, Math.Max(0, end - i - 1));
                    i = end + 1;
                    int decimals = 0;
                    if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1])) { decimals = text[i + 1] - '0'; i += 2; }
                    double v = Eval(expr, depth);
                    _lastNumber = v;
                    return Format(v, decimals);
                }
                if (c == '<')
                {
                    int end = text.IndexOf('>', i);
                    if (end < 0) { i = text.Length; return ""; }
                    string name = text.Substring(i + 1, end - i - 1);
                    i = end + 1;
                    double v = Eval(VarExpr(name), depth + 1);
                    _lastNumber = v;
                    return Format(v, 0);
                }
                if (c == '?')
                {
                    // $?sID[yes][no], $?!aID[..][..], chains: $?s1[a]?s2[b][c]. Offline: every condition is false.
                    i++;
                    while (true)
                    {
                        while (i < text.Length && text[i] != '[') i++;
                        if (i >= text.Length) return "";
                        int end = Matching(text, i, '[', ']');
                        i = end + 1;   // skip the "yes" branch
                        if (i < text.Length && text[i] == '?') { i++; continue; }
                        if (i < text.Length && text[i] == '[')
                        {
                            int e2 = Matching(text, i, '[', ']');
                            string no = text.Substring(i + 1, Math.Max(0, e2 - i - 1));
                            i = e2 + 1;
                            return Run(no, depth + 1);
                        }
                        return "";
                    }
                }
                if (c == 'l' || c == 'L' || c == 'g' || c == 'G')
                {
                    // $lsingular:plural;  $gmale:female;
                    int colon = text.IndexOf(':', i);
                    int semi = text.IndexOf(';', i);
                    if (colon < 0 || semi < 0 || colon > semi) { i++; return ""; }
                    string a = text.Substring(i + 1, colon - i - 1), b = text.Substring(colon + 1, semi - colon - 1);
                    i = semi + 1;
                    if (c == 'g' || c == 'G') return a;
                    return Math.Abs(_lastNumber - 1) < 1e-6 ? a : b;
                }
                if (c == '/' || c == '*')
                {
                    // $/10;s2  $*5;s1  (optionally with a spell id: $/10;2687s1)
                    int semi = text.IndexOf(';', i);
                    if (semi < 0) { i++; return ""; }
                    double k;
                    double.TryParse(text.Substring(i + 1, semi - i - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out k);
                    i = semi + 1;
                    double v;
                    if (!SimpleValue(text, ref i, out v, true)) return "";
                    v = c == '/' ? (k != 0 ? v / k : v) : v * k;
                    _lastNumber = v;
                    return Format(v, 0);
                }
                // Simple token: [spellId]letter[index]
                int save = i;
                string s = SimpleText(text, ref i);
                if (s == null) { i = save; return "$"; }
                return s;
            }

            private string VarExpr(string name)
            {
                if (_vars == null)
                {
                    _vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string defs = _db != null ? _db.DescriptionVariables(_spell.DescriptionVariablesId) : "";
                    foreach (var raw in defs.Split('\n', '\r'))
                    {
                        var line = raw.Trim();
                        if (line.Length < 3 || line[0] != '$') continue;
                        int eq = line.IndexOf('=');
                        if (eq < 2) continue;
                        _vars[line.Substring(1, eq - 1).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
                string expr;
                if (_vars.TryGetValue(name, out expr))
                {
                    // Definitions look like "${$m1*5}" or "$?s123[..][..]": strip one ${ } wrapper, resolve conditionals.
                    if (expr.StartsWith("${") && expr.EndsWith("}")) return expr.Substring(2, expr.Length - 3);
                    return Run(expr, 3);
                }
                // Common client variables without a definition row.
                switch (name.ToLowerInvariant())
                {
                    case "mult": return "1";
                    case "percent": return "100";
                    case "min": return MinOf(_spell, 0).ToString(CultureInfo.InvariantCulture);
                    case "max": return MaxOf(_spell, 0).ToString(CultureInfo.InvariantCulture);
                    case "total": return (MinOf(_spell, 0) * Ticks(_spell, 0)).ToString(CultureInfo.InvariantCulture);
                }
                return "0";
            }

            // Reads [spellId]letter[digit] at text[i], returns the display text or null when no letter follows.
            private string SimpleText(string text, ref int i)
            {
                int idStart = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                int refId = 0;
                if (i > idStart) int.TryParse(text.Substring(idStart, i - idStart), out refId);
                if (i >= text.Length || !char.IsLetter(text[i])) return refId > 0 ? "" : null;
                char letter = text[i++];
                int index = 1;
                if (i < text.Length && text[i] >= '1' && text[i] <= '3') { index = text[i] - '0'; i++; }
                var sp = refId > 0 && _db != null ? _db.Find(refId) : _spell;
                if (sp == null) return "";
                int e = index - 1;
                switch (letter)
                {
                    case 's':
                    {
                        int lo = MinOf(sp, e), hi = MaxOf(sp, e);
                        _lastNumber = Math.Abs(lo);
                        if (lo == hi) return Math.Abs(lo).ToString(CultureInfo.InvariantCulture);
                        return Math.Abs(lo) + " to " + Math.Abs(hi);
                    }
                    case 'S': case 'm': { int v = Math.Abs(MinOf(sp, e)); _lastNumber = v; return v.ToString(CultureInfo.InvariantCulture); }
                    case 'M': { int v = Math.Abs(MaxOf(sp, e)); _lastNumber = v; return v.ToString(CultureInfo.InvariantCulture); }
                    case 'o': { int v = Math.Abs(MinOf(sp, e)) * Ticks(sp, e); _lastNumber = v; return v.ToString(CultureInfo.InvariantCulture); }
                    case 't': { var x = Eff(sp, e); double v = x != null ? x.Amplitude / 1000.0 : 0; _lastNumber = v; return Format(v, v == Math.Floor(v) ? 0 : 1); }
                    case 'd': case 'D': return DurationText(sp.DurationAt(_ctx.CasterLevel));
                    case 'a': case 'A': { var x = Eff(sp, e); double v = x != null ? x.Radius : 0; _lastNumber = v; return Format(v, 0); }
                    case 'h': case 'H': _lastNumber = sp.ProcChance; return sp.ProcChance.ToString(CultureInfo.InvariantCulture);
                    case 'n': case 'N': _lastNumber = sp.ProcCharges; return sp.ProcCharges.ToString(CultureInfo.InvariantCulture);
                    case 'u': case 'U': _lastNumber = sp.StackAmount; return sp.StackAmount.ToString(CultureInfo.InvariantCulture);
                    case 'x': case 'X': { var x = Eff(sp, e); int v = x != null ? x.ChainTargets : 0; _lastNumber = v; return v.ToString(CultureInfo.InvariantCulture); }
                    case 'b': case 'B': { var x = Eff(sp, e); double v = x != null ? x.PointsPerComboPoint : 0; _lastNumber = v; return Format(v, 0); }
                    case 'e': case 'E': { var x = Eff(sp, e); double v = x != null ? x.ValueMultiplier : 0; _lastNumber = v; return Format(v, 1); }
                    case 'q': case 'Q': { var x = Eff(sp, e); int v = x != null ? x.MiscValue : 0; _lastNumber = v; return v.ToString(CultureInfo.InvariantCulture); }
                    case 'r': case 'R': _lastNumber = sp.RangeMax; return Format(sp.RangeMax, 0);
                    case 'i': case 'I': _lastNumber = sp.MaxAffectedTargets; return sp.MaxAffectedTargets.ToString(CultureInfo.InvariantCulture);
                    case 'v': case 'V': _lastNumber = sp.MaxTargetLevel; return sp.MaxTargetLevel.ToString(CultureInfo.InvariantCulture);
                    case 'z': return !string.IsNullOrEmpty(_ctx.HomeName) ? _ctx.HomeName : "your home";   // iteration 11 (npc H6)
                }
                return "";
            }

            // A numeric simple token ($s1, $m1, $2687s1...) at text[i] (after '$' when requireDollar is false).
            private bool SimpleValue(string text, ref int i, out double v, bool allowNoDollar)
            {
                v = 0;
                if (i < text.Length && text[i] == '$') i++;
                int idStart = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                int refId = 0;
                if (i > idStart) int.TryParse(text.Substring(idStart, i - idStart), out refId);
                if (i >= text.Length || !char.IsLetter(text[i])) return false;
                char letter = text[i++];
                int index = 1;
                if (i < text.Length && text[i] >= '1' && text[i] <= '3') { index = text[i] - '0'; i++; }
                var sp = refId > 0 && _db != null ? _db.Find(refId) : _spell;
                if (sp == null) return true;
                v = NumberOf(sp, letter, index - 1);
                return true;
            }

            private double NumberOf(SpellInfo sp, char letter, int e)
            {
                var x = Eff(sp, e);
                switch (letter)
                {
                    case 's': case 'm': case 'S': return Math.Abs(MinOf(sp, e));
                    case 'M': return Math.Abs(MaxOf(sp, e));
                    case 'o': return Math.Abs(MinOf(sp, e)) * Ticks(sp, e);
                    case 't': return x != null ? x.Amplitude / 1000.0 : 0;
                    case 'd': case 'D': { int d = sp.DurationAt(_ctx.CasterLevel); return d > 0 ? d / 1000.0 : 0; }
                    case 'a': return x != null ? x.Radius : 0;
                    case 'b': return x != null ? x.PointsPerComboPoint : 0;
                    case 'e': return x != null ? x.ValueMultiplier : 0;
                    case 'h': return sp.ProcChance;
                    case 'n': return sp.ProcCharges;
                    case 'u': return sp.StackAmount;
                    case 'x': return x != null ? x.ChainTargets : 0;
                    case 'q': return x != null ? x.MiscValue : 0;
                    case 'r': return sp.RangeMax;
                }
                return 0;
            }

            // ------------------------------------------------ expression evaluator (numbers, + - * / ( ), $tokens, functions)

            private string _expr;
            private int _p;
            private int _depth;

            private double Eval(string expr, int depth)
            {
                string saveExpr = _expr; int saveP = _p, saveDepth = _depth;
                _expr = expr; _p = 0; _depth = depth;
                double v;
                try { v = ParseSum(); }
                catch (Exception) { v = 0; }
                _expr = saveExpr; _p = saveP; _depth = saveDepth;
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
                return v;
            }

            private void Ws() { while (_p < _expr.Length && char.IsWhiteSpace(_expr[_p])) _p++; }

            private double ParseSum()
            {
                double v = ParseProduct();
                while (true)
                {
                    Ws();
                    if (_p >= _expr.Length) return v;
                    char c = _expr[_p];
                    if (c == '+') { _p++; v += ParseProduct(); }
                    else if (c == '-') { _p++; v -= ParseProduct(); }
                    else return v;
                }
            }

            private double ParseProduct()
            {
                double v = ParseUnary();
                while (true)
                {
                    Ws();
                    if (_p >= _expr.Length) return v;
                    char c = _expr[_p];
                    if (c == '*') { _p++; v *= ParseUnary(); }
                    else if (c == '/') { _p++; double d = ParseUnary(); v = d != 0 ? v / d : 0; }
                    else return v;
                }
            }

            private double ParseUnary()
            {
                Ws();
                if (_p < _expr.Length && _expr[_p] == '-') { _p++; return -ParseUnary(); }
                if (_p < _expr.Length && _expr[_p] == '+') { _p++; return ParseUnary(); }
                return ParseAtom();
            }

            private double ParseAtom()
            {
                Ws();
                if (_p >= _expr.Length) return 0;
                char c = _expr[_p];
                if (c == '(')
                {
                    _p++;
                    double v = ParseSum();
                    Ws();
                    if (_p < _expr.Length && _expr[_p] == ')') _p++;
                    return v;
                }
                if (char.IsDigit(c) || c == '.')
                {
                    int s = _p;
                    while (_p < _expr.Length && (char.IsDigit(_expr[_p]) || _expr[_p] == '.')) _p++;
                    double v;
                    double.TryParse(_expr.Substring(s, _p - s), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                    return v;
                }
                if (c == '$')
                {
                    _p++;
                    if (_p < _expr.Length && _expr[_p] == '<')
                    {
                        int end = _expr.IndexOf('>', _p);
                        if (end < 0) { _p = _expr.Length; return 0; }
                        string name = _expr.Substring(_p + 1, end - _p - 1);
                        _p = end + 1;
                        return _depth > 6 ? 0 : Eval(VarExpr(name), _depth + 1);
                    }
                    if (_p < _expr.Length && _expr[_p] == '{')
                    {
                        int end = Matching(_expr, _p, '{', '}');
                        string inner = _expr.Substring(_p + 1, Math.Max(0, end - _p - 1));
                        _p = end + 1;
                        return Eval(inner, _depth + 1);
                    }
                    if (_p < _expr.Length && _expr[_p] == '?')
                    {
                        // conditional inside an expression: take the last [..] branch
                        string rest = _expr.Substring(_p - 1);
                        int k = 0;
                        string txt = Token(rest, ref k, _depth + 1);
                        _p = _p - 1 + k;
                        double v;
                        double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                        return v;
                    }
                    // named values
                    int s = _p;
                    while (_p < _expr.Length && char.IsLetter(_expr[_p])) _p++;
                    string word = _expr.Substring(s, _p - s);
                    switch (word)
                    {
                        case "AP": return _ctx.AttackPower;
                        case "RAP": return _ctx.RangedAttackPower;
                        case "SP": case "SPS": case "SPH": case "SPN": case "SPF": case "SPA": case "SPH_": return _ctx.SpellPower;
                        case "MWB": return _ctx.MainWeaponMax;
                        case "mwb": return _ctx.MainWeaponMin;
                        case "MWS": return _ctx.MainWeaponSpeed;
                        case "mws": return _ctx.MainWeaponSpeed;
                        case "RWB": return _ctx.RangedWeaponMax;
                        case "rwb": return _ctx.RangedWeaponMin;
                        case "RWS": return _ctx.RangedWeaponSpeed;
                        case "PL": case "pl": return _ctx.CasterLevel;
                        case "floor": return Math.Floor(Args1());
                        case "ceil": return Math.Ceiling(Args1());
                        case "abs": return Math.Abs(Args1());
                        case "max": { var a = Args(); return a.Count >= 2 ? Math.Max(a[0], a[1]) : (a.Count == 1 ? a[0] : 0); }
                        case "min": { var a = Args(); return a.Count >= 2 ? Math.Min(a[0], a[1]) : (a.Count == 1 ? a[0] : 0); }
                        case "cond": { var a = Args(); return a.Count >= 3 ? (a[0] != 0 ? a[1] : a[2]) : 0; }
                        case "gt": { var a = Args(); return a.Count >= 2 && a[0] > a[1] ? 1 : 0; }
                        case "lt": { var a = Args(); return a.Count >= 2 && a[0] < a[1] ? 1 : 0; }
                        case "eq": { var a = Args(); return a.Count >= 2 && a[0] == a[1] ? 1 : 0; }
                    }
                    // simple token with optional spell id: $m1, $123s1 (the word may be empty when digits come first)
                    _p = s;
                    double v2;
                    int pp = _p;
                    if (SimpleValue(_expr, ref pp, out v2, true)) { _p = pp; return v2; }
                    _p = s + Math.Max(1, word.Length);
                    return 0;
                }
                if (char.IsLetter(c))
                {
                    // bare function names (rare): skip
                    while (_p < _expr.Length && char.IsLetter(_expr[_p])) _p++;
                    return 0;
                }
                _p++;
                return 0;
            }

            private double Args1() { var a = Args(); return a.Count > 0 ? a[0] : 0; }

            private List<double> Args()
            {
                var list = new List<double>();
                Ws();
                if (_p >= _expr.Length || _expr[_p] != '(') return list;
                _p++;
                while (_p < _expr.Length)
                {
                    list.Add(ParseSum());
                    Ws();
                    if (_p < _expr.Length && _expr[_p] == ',') { _p++; continue; }
                    if (_p < _expr.Length && _expr[_p] == ')') { _p++; break; }
                    break;
                }
                return list;
            }

            private int MinOf(SpellInfo sp, int e)
            {
                var x = Eff(sp, e);
                if (x == null) return 0;
                return x.MinValue + LevelBonus(sp, x);
            }

            private int MaxOf(SpellInfo sp, int e)
            {
                var x = Eff(sp, e);
                if (x == null) return 0;
                return x.MaxValue + LevelBonus(sp, x);
            }

            private int LevelBonus(SpellInfo sp, SpellEffectInfo x)
            {
                if (x.PointsPerLevel == 0) return 0;
                int level = _ctx.CasterLevel;
                if (sp.MaxLevel > 0 && level > sp.MaxLevel) level = sp.MaxLevel;
                else if (level < sp.BaseLevel) level = sp.BaseLevel;
                return (int)((level - sp.SpellLevel) * x.PointsPerLevel);
            }

            private static int Ticks(SpellInfo sp, int e)
            {
                var x = Eff(sp, e);
                if (x == null || x.Amplitude <= 0) return 1;
                int d = sp.DurationMs > 0 ? sp.DurationMs : 0;
                return Math.Max(1, d / x.Amplitude);
            }

            private static SpellEffectInfo Eff(SpellInfo sp, int e) { return e >= 0 && e < 3 ? sp.Effects[e] : null; }
        }

        private static int Matching(string s, int open, char o, char c)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == o) depth++;
                else if (s[i] == c) { depth--; if (depth == 0) return i; }
            }
            return s.Length;
        }

        private static string Format(double v, int decimals)
        {
            if (decimals <= 0) return ((long)Math.Round(Math.Abs(v) < 1e-9 ? 0 : v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>"15 sec", "2 min", "1.5 min", "until cancelled".</summary>
        public static string DurationText(int ms)
        {
            if (ms < 0) return "until cancelled";
            if (ms == 0) return "0 sec";
            double s = ms / 1000.0;
            if (s < 60) return Format(s, s == Math.Floor(s) ? 0 : 1) + " sec";
            double m = s / 60.0;
            if (m < 60) return Format(m, m == Math.Floor(m) ? 0 : 1) + " min";
            double h = m / 60.0;
            return Format(h, h == Math.Floor(h) ? 0 : 1) + " hrs";
        }
    }
}
