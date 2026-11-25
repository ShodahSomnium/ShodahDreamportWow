using System;
using System.Globalization;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>The appearance bytes of CMSG_CHAR_CREATE (race/gender specific).</summary>
    public struct WowAppearance
    {
        public int Skin, Face, HairStyle, HairColor, FacialHair;
        public string Report;

        public override string ToString()
        {
            return "skin " + Skin + ", face " + Face + ", hair " + HairStyle + " colour " + HairColor + ", facial hair " + FacialHair;
        }
    }

    /// <summary>
    /// Pure helpers of the online front end (no UnityEngine):
    /// - CleanName: the Somnium nickname as a WoW character name. AzerothCore ObjectMgr::CheckPlayerName (fetched raw,
    ///   P/acsrc/ObjectMgr.cpp): "if (wname.size() > MAX_PLAYER_NAME) return CHAR_NAME_TOO_LONG" (12), minimum
    ///   CONFIG_MIN_PLAYER_NAME (default 2), "Check for three consecutive letters ... wname[i] == wname[i - 1] && wname[i] ==
    ///   wname[i - 2]"; normalizePlayerName lower-cases it and upper-cases the first letter and refuses spaces. We keep
    ///   ASCII letters only (the task's rule), cut to 12, drop the third of three equal letters, capitalise.
    /// - DefaultAppearance: CharSections.dbc (section 0 skin, 1 face, 3 hair; CharacterComposer's section numbers) and
    ///   CharacterFacialHairStyles.dbc rows that exist for the race and gender, lowest index first, preferring rows whose
    ///   Flags has bit 0x1 (its meaning is not verified here; without such a row the lowest existing index is used either
    ///   way). AzerothCore does not check them (Player::Create:
    ///   "should check that skin, face, hair* are valid via DBC per race/class"), so this is only about a correct look.
    /// - ParseServer: "host", "host:port", "[v6]:port".
    /// </summary>
    public static class WowOnlineNames
    {
        public const int MaxName = 12, MinName = 2;

        /// <summary>Letters only, 2-12, first capital, the rest lower case, no three equal letters in a row; "" when fewer than 2 letters remain.</summary>
        public static string CleanName(string nick)
        {
            if (string.IsNullOrEmpty(nick)) return "";
            var b = new StringBuilder(MaxName);
            foreach (char ch in nick)
            {
                char c = char.ToLowerInvariant(ch);
                if (c < 'a' || c > 'z') continue;
                int n = b.Length;
                if (n >= 2 && b[n - 1] == c && b[n - 2] == c) continue;
                b.Append(c);
                if (b.Length >= MaxName) break;
            }
            if (b.Length < MinName) return "";
            b[0] = char.ToUpperInvariant(b[0]);
            return b.ToString();
        }

        /// <summary>Why a name would be refused, in player words, or null when it passes our rules (the server has the last word).</summary>
        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Enter a name.";
            if (name.Length < MinName) return "The name is too short (at least 2 letters).";
            if (name.Length > MaxName) return "The name is too long (at most 12 letters).";
            string low = name.ToLowerInvariant();
            for (int i = 0; i < low.Length; i++)
            {
                if (low[i] < 'a' || low[i] > 'z') return "Use letters only (A-Z).";
                if (i >= 2 && low[i] == low[i - 1] && low[i] == low[i - 2]) return "No letter three times in a row.";
            }
            return null;
        }

        /// <summary>What the server stores (normalizePlayerName): first letter upper, the rest lower.</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string low = name.Trim().ToLowerInvariant();
            return low.Length == 0 ? "" : char.ToUpperInvariant(low[0]) + low.Substring(1);
        }

        /// <summary>"host" / "host:port" / "[::1]:port" -> host and port (defaultPort when missing). False when empty or the port is bad.</summary>
        public static bool ParseServer(string text, int defaultPort, out string host, out int port)
        {
            host = "";
            port = defaultPort;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return false;
            string portText = null;

            if (t.StartsWith("[", StringComparison.Ordinal))
            {
                int close = t.IndexOf(']');
                if (close < 0) return false;
                host = t.Substring(1, close - 1);
                if (close + 1 < t.Length)
                {
                    if (t[close + 1] != ':') return false;
                    portText = t.Substring(close + 2);
                }
            }
            else
            {
                int colon = t.LastIndexOf(':');
                if (colon >= 0 && t.IndexOf(':') == colon)
                {
                    host = t.Substring(0, colon);
                    portText = t.Substring(colon + 1);
                }
                else
                {
                    host = t;   // no port, or a bare IPv6 address
                }
            }

            host = host.Trim();
            if (host.Length == 0 || host.IndexOf(' ') >= 0) return false;

            if (portText != null)
            {
                int p;
                if (!int.TryParse(portText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out p) || p < 1 || p > 65535) return false;
                port = p;
            }

            return true;
        }

        /// <summary>The server field's text for a host and port (the port only when it is not the default 3724).</summary>
        public static string FormatServer(string host, int port)
        {
            if (string.IsNullOrEmpty(host)) return "";
            string h = host.IndexOf(':') >= 0 ? "[" + host + "]" : host;
            return port > 0 && port != 3724 ? h + ":" + port.ToString(CultureInfo.InvariantCulture) : h;
        }

        /// <summary>RealmType (AC Realm.h: REALM_TYPE_NORMAL 0, PVP 1, NORMAL2 4, RP 6, RPPVP 8, FFA_PVP 16 custom).</summary>
        public static string RealmTypeName(int type)
        {
            switch (type)
            {
                case 1: return "PvP";
                case 6: return "RP";
                case 8: return "RP-PvP";
                case 16: return "Free-for-all PvP";
                default: return "Normal";
            }
        }

        /// <summary>The first existing CharSections / CharacterFacialHairStyles values for race and gender (see the class summary).</summary>
        public static WowAppearance DefaultAppearance(WowFiles files, int race, int gender)
        {
            var a = new WowAppearance();
            DbcTable cs = null, fh = null;
            try { cs = Dbc.Load(files, "CharSections"); } catch (Exception) { cs = null; }
            try { fh = Dbc.Load(files, "CharacterFacialHairStyles"); } catch (Exception) { fh = null; }

            if (cs == null)
            {
                a.Report = "CharSections.dbc not readable - all 0";
                return a;
            }

            // Skin: section 0, variation 0, lowest colour.
            a.Skin = Lowest(cs, race, gender, 0, 0, true);
            if (a.Skin < 0) a.Skin = Lowest(cs, race, gender, 0, -1, true);
            // Face: section 1, lowest variation that exists for that skin colour.
            a.Face = LowestVariation(cs, race, gender, 1, Math.Max(0, a.Skin));
            // Hair: section 3, lowest variation, then its lowest colour.
            a.HairStyle = LowestVariation(cs, race, gender, 3, -1);
            a.HairColor = a.HairStyle >= 0 ? Lowest(cs, race, gender, 3, a.HairStyle, true) : -1;
            a.FacialHair = LowestFacial(fh, race, gender);

            string missing = (a.Skin < 0 ? " skin" : "") + (a.Face < 0 ? " face" : "") + (a.HairStyle < 0 ? " hair" : "") + (a.HairColor < 0 ? " hair colour" : "") + (a.FacialHair < 0 ? " facial hair" : "");
            if (a.Skin < 0) a.Skin = 0;
            if (a.Face < 0) a.Face = 0;
            if (a.HairStyle < 0) a.HairStyle = 0;
            if (a.HairColor < 0) a.HairColor = 0;
            if (a.FacialHair < 0) a.FacialHair = 0;
            a.Report = "CharSections race " + race + " gender " + gender + ": " + a + (missing.Length > 0 ? " (no row for" + missing + " - 0 used)" : "");
            return a;
        }

        /// <summary>Lowest ColorIndex of a section row with that variation (-1 = any variation); rows with flag 0x1 first.</summary>
        private static int Lowest(DbcTable cs, int race, int gender, int section, int variation, bool colour)
        {
            int best = -1, bestAny = -1;
            for (int r = 0; r < cs.RecordCount; r++)
            {
                if (cs.GetInt(r, DbcFields.CharSections.RaceID) != race || cs.GetInt(r, DbcFields.CharSections.SexID) != gender) continue;
                if (cs.GetInt(r, DbcFields.CharSections.BaseSection) != section) continue;
                if (variation >= 0 && cs.GetInt(r, DbcFields.CharSections.VariationIndex) != variation) continue;
                int v = colour ? cs.GetInt(r, DbcFields.CharSections.ColorIndex) : cs.GetInt(r, DbcFields.CharSections.VariationIndex);
                if (v < 0) continue;
                bool playable = (cs.GetInt(r, DbcFields.CharSections.Flags) & 1) != 0;
                if (playable && (best < 0 || v < best)) best = v;
                if (bestAny < 0 || v < bestAny) bestAny = v;
            }
            return best >= 0 ? best : bestAny;
        }

        /// <summary>Lowest VariationIndex of a section (restricted to one ColorIndex when color >= 0); rows with flag 0x1 first.</summary>
        private static int LowestVariation(DbcTable cs, int race, int gender, int section, int color)
        {
            int best = -1, bestAny = -1;
            for (int r = 0; r < cs.RecordCount; r++)
            {
                if (cs.GetInt(r, DbcFields.CharSections.RaceID) != race || cs.GetInt(r, DbcFields.CharSections.SexID) != gender) continue;
                if (cs.GetInt(r, DbcFields.CharSections.BaseSection) != section) continue;
                if (color >= 0 && cs.GetInt(r, DbcFields.CharSections.ColorIndex) != color) continue;
                int v = cs.GetInt(r, DbcFields.CharSections.VariationIndex);
                if (v < 0) continue;
                bool playable = (cs.GetInt(r, DbcFields.CharSections.Flags) & 1) != 0;
                if (playable && (best < 0 || v < best)) best = v;
                if (bestAny < 0 || v < bestAny) bestAny = v;
            }
            if (best < 0 && bestAny < 0 && color >= 0) return LowestVariation(cs, race, gender, section, -1);
            return best >= 0 ? best : bestAny;
        }

        private static int LowestFacial(DbcTable fh, int race, int gender)
        {
            if (fh == null) return -1;
            int best = -1;
            for (int r = 0; r < fh.RecordCount; r++)
            {
                if (fh.GetInt(r, DbcFields.CharacterFacialHairStyles.RaceID) != race || fh.GetInt(r, DbcFields.CharacterFacialHairStyles.SexID) != gender) continue;
                int v = fh.GetInt(r, DbcFields.CharacterFacialHairStyles.VariationID);
                if (v >= 0 && (best < 0 || v < best)) best = v;
            }
            return best;
        }
    }
}
