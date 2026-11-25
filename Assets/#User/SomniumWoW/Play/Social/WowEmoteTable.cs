using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Social
{
    /// <summary>
    /// Iteration 11 (owner social, proposal-social.md 4.5 / 6.4): the text emotes of the player's own install, read at runtime - nothing is
    /// copied into the project. The slash commands come from Interface\FrameXML\GlobalStrings.lua (`EMOTE&lt;i&gt;_CMD&lt;j&gt; = "/wave";`,
    /// 644 strings in the 3.3.5a enUS install) and Interface\FrameXML\ChatFrame.lua (`EMOTE&lt;i&gt;_TOKEN = "WAVE";`, ChatFrame.lua:363-612;
    /// ChatFrame.lua:4080-4102 maps a typed command to its token); the token is EmotesText.dbc's Name. The lines are EmotesTextData.dbc
    /// texts, the animation Emotes.dbc's AnimID (AnimationData id).
    /// DBC layouts measured on the install (harness probe D11/social SocialEmotes):
    /// EmotesText = ID 0, Name 1, EmoteID 2 (Emotes.dbc id), EmoteText[16] 3..18 (EmotesTextData ids);
    /// EmotesTextData = ID 0, Text_lang 1 (locstring); Emotes = ID 0, EmoteSlashCommand 1, AnimID 2, EmoteFlags 3, EmoteSpecProc 4,
    /// EmoteSpecProcParam 5, EventSoundID 6.
    /// The 16 text slots (measured, e.g. WAVE 101): 0 others see (sender, target), 1 the target sees (sender), 2 the sender sees (target),
    /// 4 others see without a target (sender), 6 the sender sees without a target; +8 = the female variant of 0 / 1 / 4 (slots 8, 9, 12)
    /// when the SENDER is female and the slot is filled. A missing text = no line (SIT has none: only the stand state).
    /// Pure C# (no UnityEngine): the harness probes it on the real files.
    /// </summary>
    public sealed class WowEmoteTable
    {
        const int EtId = 0, EtName = 1, EtEmote = 2, EtText0 = 3, EtTextSlots = 16;
        const int EtdText = 1;
        const int EmAnim = 2, EmSpecProc = 4, EmSpecParam = 5;
        const int SpecProcStandState = 1;                  // EmoteSpecProc 1: SpecProcParam is a stand state (KNEEL 8, SIT 1, SLEEP 3 - measured)
        const int SlotOthersTarget = 0, SlotTargetSees = 1, SlotSenderTarget = 2, SlotOthersAlone = 4, SlotSenderAlone = 6, FemaleOffset = 8;

        sealed class Row { public int Id, EmoteId; public string Token; public readonly int[] Text = new int[EtTextSlots]; }

        static readonly Regex CmdRx = new Regex("^EMOTE(\\d+)_CMD(\\d+)\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline);
        static readonly Regex TokRx = new Regex("^EMOTE(\\d+)_TOKEN\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline);

        readonly Dictionary<string, int> _byCommand = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // "/wave" -> EmotesText id
        readonly Dictionary<int, string> _commandOf = new Dictionary<int, string>();                                      // EmotesText id -> first "/command"
        readonly Dictionary<int, Row> _rows = new Dictionary<int, Row>();
        DbcTable _textData, _emotes;

        /// <summary>Command strings read, distinct commands, commands whose token has no EmotesText row (2 on the 3.3.5a enUS install).</summary>
        public int Commands, Unresolved;
        /// <summary>Null when the files were read; else why the table is empty (every lookup then answers "not an emote").</summary>
        public string Problem;
        public int Count { get { return _byCommand.Count; } }

        static readonly object Gate = new object();
        static WowFiles _cachedFiles;
        static WowEmoteTable _cached;

        /// <summary>The table of these files (read once per WowFiles; never null).</summary>
        public static WowEmoteTable Get(WowFiles files)
        {
            lock (Gate)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                var t = Load(files);
                _cachedFiles = files;
                _cached = t;
                return t;
            }
        }

        public static WowEmoteTable Load(WowFiles files)
        {
            var t = new WowEmoteTable();
            try
            {
                if (files == null) { t.Problem = "no game files"; return t; }
                byte[] gs = files.Read(@"Interface\FrameXML\GlobalStrings.lua");
                byte[] cf = files.Read(@"Interface\FrameXML\ChatFrame.lua");
                DbcTable et = Dbc.Load(files, "EmotesText");
                t._textData = Dbc.Load(files, "EmotesTextData");
                t._emotes = Dbc.Load(files, "Emotes");
                if (gs == null || cf == null || et == null) { t.Problem = "GlobalStrings.lua / ChatFrame.lua / EmotesText.dbc missing"; return t; }
                if (et.FieldCount < EtText0 + EtTextSlots) { t.Problem = "EmotesText.dbc has " + et.FieldCount + " fields (" + (EtText0 + EtTextSlots) + " expected)"; return t; }
                var idByToken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int r = 0; r < et.RecordCount; r++)
                {
                    var row = new Row { Id = et.GetInt(r, EtId), Token = et.GetString(r, EtName), EmoteId = et.GetInt(r, EtEmote) };
                    for (int i = 0; i < EtTextSlots; i++) row.Text[i] = et.GetInt(r, EtText0 + i);
                    t._rows[row.Id] = row;
                    if (!string.IsNullOrEmpty(row.Token) && !idByToken.ContainsKey(row.Token)) idByToken[row.Token] = row.Id;
                }
                var tokenByIndex = new Dictionary<int, string>();
                foreach (Match m in TokRx.Matches(Encoding.UTF8.GetString(cf).Replace("\r", "")))
                    tokenByIndex[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
                foreach (Match m in CmdRx.Matches(Encoding.UTF8.GetString(gs).Replace("\r", "")))
                {
                    t.Commands++;
                    string token;
                    int id;
                    if (!tokenByIndex.TryGetValue(int.Parse(m.Groups[1].Value), out token) || !idByToken.TryGetValue(token, out id)) { t.Unresolved++; continue; }
                    string cmd = m.Groups[3].Value.Trim();
                    if (cmd.Length == 0) continue;
                    if (!t._byCommand.ContainsKey(cmd)) t._byCommand[cmd] = id;   // the first index wins, like ChatFrame.lua's loop order
                    if (!t._commandOf.ContainsKey(id)) t._commandOf[id] = cmd;
                }
            }
            catch (Exception e) { t.Problem = e.GetType().Name + ": " + e.Message; }
            return t;
        }

        /// <summary>The EmotesText id of a typed command ("/wave", "wave", any case), 0 = not an emote command of the install.</summary>
        public int Find(string command)
        {
            if (string.IsNullOrEmpty(command)) return 0;
            string c = command.Trim();
            if (c.Length == 0) return 0;
            if (c[0] != '/') c = "/" + c;
            int id;
            return _byCommand.TryGetValue(c, out id) ? id : 0;
        }

        /// <summary>The first slash command of an emote ("/wave"), null when the install has none (the VR quick-emote labels).</summary>
        public string CommandOf(int textEmoteId)
        {
            string c;
            return _commandOf.TryGetValue(textEmoteId, out c) ? c : null;
        }

        public string TokenOf(int textEmoteId)
        {
            Row r;
            return _rows.TryGetValue(textEmoteId, out r) ? r.Token : null;
        }

        /// <summary>EmotesText.EmoteID (Emotes.dbc id), 0 = none.</summary>
        public int EmoteOf(int textEmoteId)
        {
            Row r;
            return _rows.TryGetValue(textEmoteId, out r) ? r.EmoteId : 0;
        }

        /// <summary>The AnimationData id the emote plays (its Emotes.dbc AnimID), 0 = none.</summary>
        public int AnimOf(int textEmoteId) { return AnimOfEmote(EmoteOf(textEmoteId)); }

        /// <summary>Emotes.dbc AnimID of an Emotes.dbc id (SMSG_EMOTE carries that id), 0 = none.</summary>
        public int AnimOfEmote(int emotesId)
        {
            if (emotesId <= 0 || _emotes == null) return 0;
            int r = _emotes.FindRow(emotesId);
            return r < 0 || _emotes.FieldCount <= EmAnim ? 0 : _emotes.GetInt(r, EmAnim);
        }

        /// <summary>The stand state the emote sets (Emotes.dbc EmoteSpecProc 1 -> SpecProcParam: KNEEL 8, SIT 1, SLEEP 3), -1 = none.</summary>
        public int StandStateOf(int textEmoteId)
        {
            int e = EmoteOf(textEmoteId);
            if (e <= 0 || _emotes == null || _emotes.FieldCount <= EmSpecParam) return -1;
            int r = _emotes.FindRow(e);
            if (r < 0 || _emotes.GetInt(r, EmSpecProc) != SpecProcStandState) return -1;
            return _emotes.GetInt(r, EmSpecParam);
        }

        /// <summary>
        /// The chat line as THIS player reads it (null = the emote has no text for the case): sender == me -> slot 2 (with a target) / 6;
        /// target == me -> slot 1; else slot 0 (with a target) / 4; the female slot (+8) for a female sender when filled. With a target but no
        /// text for it the no-target slot is used (ours).
        /// </summary>
        public string Line(int textEmoteId, string sender, bool senderFemale, string target, string me)
        {
            Row r;
            if (!_rows.TryGetValue(textEmoteId, out r)) return null;
            bool hasTarget = !string.IsNullOrEmpty(target);
            bool mine = !string.IsNullOrEmpty(me) && string.Equals(sender, me, StringComparison.OrdinalIgnoreCase);
            if (mine)
            {
                if (hasTarget)
                {
                    string t2 = Text(r, SlotSenderTarget, false);
                    if (t2 != null) return Fill(t2, target, null);
                }
                return Text(r, SlotSenderAlone, false);
            }
            if (hasTarget && !string.IsNullOrEmpty(me) && string.Equals(target, me, StringComparison.OrdinalIgnoreCase))
            {
                string t1 = Text(r, SlotTargetSees, senderFemale);
                if (t1 != null) return Fill(t1, sender, null);
            }
            else if (hasTarget)
            {
                string t0 = Text(r, SlotOthersTarget, senderFemale);
                if (t0 != null) return Fill(t0, sender, target);
            }
            string t4 = Text(r, SlotOthersAlone, senderFemale);
            return t4 != null ? Fill(t4, sender, null) : null;
        }

        string Text(Row r, int slot, bool female)
        {
            int id = 0;
            if (female && slot + FemaleOffset < EtTextSlots) id = r.Text[slot + FemaleOffset];
            if (id == 0) id = r.Text[slot];
            if (id == 0 || _textData == null) return null;
            int row = _textData.FindRow(id);
            if (row < 0) return null;
            string s = _textData.GetLocalizedString(row, EtdText);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>Lua's format with "%s" (in order) and the positional "%1$s" / "%2$s".</summary>
        static string Fill(string text, string a, string b)
        {
            if (text.IndexOf('%') < 0) return text;
            a = a ?? "";
            b = b ?? "";
            text = text.Replace("%1$s", a).Replace("%2$s", b);
            int i = text.IndexOf("%s", StringComparison.Ordinal);
            if (i < 0) return text;
            text = text.Substring(0, i) + a + text.Substring(i + 2);
            int j = text.IndexOf("%s", i + a.Length, StringComparison.Ordinal);
            if (j >= 0) text = text.Substring(0, j) + b + text.Substring(j + 2);
            return text;
        }
    }
}
