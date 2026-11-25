using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>A stack of items in the bags (or one equipped item).</summary>
    public sealed class ItemStack
    {
        public int Entry;
        public int Count = 1;
        public int Slot = -1;              // iteration 11 (npc H11): backpack slot 0..15 (record key "bagpos"), -1 = the first free
        public ItemStack() { }
        public ItemStack(int entry, int count) { Entry = entry; Count = count; }
    }

    /// <summary>A quest in the log: state Active or Complete, with the objective counters.</summary>
    public sealed class QuestRecord
    {
        public int Id;
        public QuestState State = QuestState.Active;
        public int[] Counts = new int[4];
    }

    /// <summary>
    /// The saved character (pure C#). The offline session reads it on start and writes it back through OfflineSession.Snapshot();
    /// the front end stores Serialize() in the Bridge LocalStorage. Text format (version 1, our own, line based, UTF-16 safe):
    /// <code>
    /// SOMNIUMWOW-CHARACTER 1
    /// id=3f9a0c1d2b4e
    /// name=Somni%20User            (percent-escaped: %, =, newline, CR, space-at-ends)
    /// class=1 race=4 level=3 xp=120 money=45 map=1 ...   (one key=value per line)
    /// pos=10311.30;832.40;1326.40;5.60
    /// spells=78,2457,6673
    /// bar=6603,78,6673,0,0,0,0,0,0,0,0,0
    /// bags=2070x4,117x1              (entry x count, 16 slots)
    /// equip=15:25,17:2504            (EquipSlot:entry)
    /// quest=28:2:3,0,0,0             (id:state:counts)
    /// rewarded=456,457
    /// check=1A2B3C4D                 (FNV-1a over every line above, detects damaged text)
    /// </code>
    /// Unknown keys are ignored (newer writers stay readable); missing optional keys take defaults.
    /// </summary>
    public sealed class CharacterRecord
    {
        public const int FormatVersion = 1;
        public const string Header = "SOMNIUMWOW-CHARACTER";
        public const int BagSlots = 16;

        public string Id = "";
        public string Name = "";
        public WowClass Class;
        public int Race = 4;
        public int Level = 1;
        public int Xp;
        public int Money;
        public int MapId = 1;
        public WowPos Pos;
        public bool HasPos;                // false = use the zone's start
        public int Health = -1;            // -1 = full
        public int Power = -1;             // class power (mana/rage/energy); -1 = full (rage: 0)
        public List<int> Spells = new List<int>();
        public int[] ActionBar = new int[12];
        public int[] ActionButtons = new int[WowActionSlots.Count];   // iteration 11 (vrcast): packed, all 144 (key "slots"); ActionBar stays the 12 spells of page 1 (key "bar")
        public bool HasSlots;              // iteration 11 (vrcast): the record carried "slots" (else ActionButtons 0..11 come from "bar")
        public int FormBarsFilled;         // iteration 11 (vrcast): bit b-1 = offline form bar b was filled once from the main bar (key "formbars")
        public List<ItemStack> Bags = new List<ItemStack>();
        public int[] Equipment = new int[19];   // item entry per EquipSlot, 0 = empty
        public List<QuestRecord> Quests = new List<QuestRecord>();
        public List<int> Rewarded = new List<int>();
        public long CreatedUnix;
        public long PlayedSeconds;
        public long SavedUnix;
        /// <summary>Iteration 3: the zone had no ClassStart items for this class - the session equips the CharStartOutfit.dbc items once.</summary>
        public bool StartKitPending;
        /// <summary>Iteration 3: the creature entry of the summoned pet (Warlock imp 416 / voidwalker 1860), re-summoned on the next visit; 0 = none.</summary>
        public int PetEntry;
        /// <summary>Iteration 4: TaxiNodes.dbc ids this character discovered (old saves: empty; the session adds the race's initial nodes).</summary>
        public List<int> KnownTaxiNodes = new List<int>();
        /// <summary>Iteration 11 (talents): offline talents, pairs (Talent.dbc id, rank count); key "talents" (older worlds ignore it).</summary>
        public List<int> Talents = new List<int>();

        /// <summary>Alliance or Horde, from the race.</summary>
        public WowFaction Faction { get { return Rules.WowRaces.FactionOf(Race); } }

        /// <summary>
        /// A fresh level-1 Night Elf of this class for a zone: the ClassStart items (equippable ones equipped by their ItemDef
        /// InventoryType, the rest in the bags), the ClassStart spells, the zone's start position. The offline session adds the
        /// class kit spells of the level (read from the game files) when it starts.
        /// </summary>
        public static CharacterRecord CreateNew(WowClass cls, string name, ZoneContent zone)
        {
            return CreateNew(cls, Rules.WowRaces.NightElf, name, zone);
        }

        /// <summary>
        /// Iteration 3: a fresh level-1 character of this race and class in the race's starting zone (WowZones.ForRace). When that zone is
        /// not written yet the record still gets the zone's map and no position (the session then uses whatever zone it is given).
        /// Check WowRaces.CanBe(files, race, cls) before offering the class; this method does not refuse a combination.
        /// </summary>
        public static CharacterRecord CreateNew(WowClass cls, int race, string name)
        {
            var zone = WowZones.ForRace(race);
            var c = CreateNew(cls, race, name, zone);
            if (zone == null) { var info = WowZones.InfoForRace(race); if (info != null) c.MapId = info.MapId; }
            return c;
        }

        /// <summary>A fresh level-1 character of this race and class for a zone (see the 3-argument overloads).</summary>
        public static CharacterRecord CreateNew(WowClass cls, int race, string name, ZoneContent zone)
        {
            var c = new CharacterRecord();
            c.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            c.Name = string.IsNullOrEmpty(name) ? "Adventurer" : name.Trim();
            c.Class = cls;
            c.Race = Rules.WowRaces.IsPlayable(race) ? race : Rules.WowRaces.NightElf;
            c.Level = 1;
            c.CreatedUnix = NowUnix();
            c.StartKitPending = true;
            if (zone != null)
            {
                c.MapId = zone.MapId;
                c.Pos = zone.PlayerStart;
                c.HasPos = true;
                ClassStart start = null;
                foreach (var cs in zone.ClassStarts) if (cs != null && cs.Class == cls && cs.Race == c.Race) { start = cs; break; }
                if (start == null) foreach (var cs in zone.ClassStarts) if (cs != null && cs.Class == cls && cs.Race == 0) { start = cs; break; }
                if (start == null) foreach (var cs in zone.ClassStarts) if (cs != null && cs.Class == cls) { start = cs; break; }
                if (start != null)
                {
                    if (start.StartItems != null && start.StartItems.Length > 0) c.StartKitPending = false;
                    if (start.StartItems != null)
                    {
                        foreach (int entry in start.StartItems)
                        {
                            ItemDef def = null;
                            foreach (var d in zone.Items) if (d != null && d.Entry == entry) { def = d; break; }
                            int slot = def != null ? EquipSlotFor(def.InventoryType, c.Equipment) : -1;
                            if (slot >= 0 && c.Equipment[slot] == 0) c.Equipment[slot] = entry;
                            else c.AddToBags(entry, 1, 20);
                        }
                    }
                    if (start.StartSpells != null)
                    {
                        int bar = 0;
                        foreach (int s in start.StartSpells)
                        {
                            if (s <= 0 || c.Spells.Contains(s)) continue;
                            c.Spells.Add(s);
                            if (bar < 12) { c.ActionBar[bar] = s; c.ActionButtons[bar] = WowActionSlots.Spell(s); bar++; }   // iteration 11 (vrcast)
                        }
                        c.HasSlots = true;
                    }
                }
            }
            return c;
        }

        /// <summary>
        /// The equipment slot for an Item.dbc InventoryType (-1 = not equippable). Fingers/trinkets take the first free of the pair.
        /// </summary>
        public static int EquipSlotFor(int inventoryType, int[] equipped)
        {
            switch (inventoryType)
            {
                case 1: return (int)EquipSlot.Head;
                case 2: return (int)EquipSlot.Neck;
                case 3: return (int)EquipSlot.Shoulders;
                case 4: return (int)EquipSlot.Shirt;
                case 5: case 20: return (int)EquipSlot.Chest;
                case 6: return (int)EquipSlot.Waist;
                case 7: return (int)EquipSlot.Legs;
                case 8: return (int)EquipSlot.Feet;
                case 9: return (int)EquipSlot.Wrists;
                case 10: return (int)EquipSlot.Hands;
                case 11: return equipped != null && equipped[(int)EquipSlot.Finger1] != 0 && equipped[(int)EquipSlot.Finger2] == 0 ? (int)EquipSlot.Finger2 : (int)EquipSlot.Finger1;
                case 12: return equipped != null && equipped[(int)EquipSlot.Trinket1] != 0 && equipped[(int)EquipSlot.Trinket2] == 0 ? (int)EquipSlot.Trinket2 : (int)EquipSlot.Trinket1;
                case 13: case 17: case 21: return (int)EquipSlot.MainHand;
                case 14: case 22: case 23: return (int)EquipSlot.OffHand;
                case 15: case 25: case 26: case 28: return (int)EquipSlot.Ranged;
                case 16: return (int)EquipSlot.Back;
                case 19: return (int)EquipSlot.Tabard;
            }
            return -1;
        }

        /// <summary>Adds items to the bags (stacking up to maxStack). Returns how many did not fit.</summary>
        public int AddToBags(int entry, int count, int maxStack)
        {
            if (maxStack < 1) maxStack = 1;
            foreach (var s in Bags)
            {
                if (count <= 0) break;
                if (s.Entry != entry || s.Count >= maxStack) continue;
                int add = Math.Min(count, maxStack - s.Count);
                s.Count += add; count -= add;
            }
            while (count > 0 && Bags.Count < BagSlots)
            {
                int add = Math.Min(count, maxStack);
                Bags.Add(new ItemStack(entry, add) { Slot = FirstFreeBagSlot() });   // iteration 11 (npc H11): the first FREE slot index
                count -= add;
            }
            return count;
        }

        /// <summary>Iteration 11 (npc H11): the lowest backpack slot 0..BagSlots-1 no stack holds (stacks with Slot -1 are not counted), -1 = full.</summary>
        public int FirstFreeBagSlot()
        {
            for (int k = 0; k < BagSlots; k++)
            {
                bool used = false;
                foreach (var s in Bags) if (s.Slot == k) { used = true; break; }
                if (!used) return k;
            }
            return -1;
        }

        /// <summary>How many of an item are in the bags.</summary>
        public int CountInBags(int entry)
        {
            int n = 0;
            foreach (var s in Bags) if (s.Entry == entry) n += s.Count;
            return n;
        }

        /// <summary>Removes up to count items from the bags; returns how many were removed.</summary>
        public int RemoveFromBags(int entry, int count)
        {
            int removed = 0;
            for (int i = Bags.Count - 1; i >= 0 && removed < count; i--)
            {
                if (Bags[i].Entry != entry) continue;
                int take = Math.Min(count - removed, Bags[i].Count);
                Bags[i].Count -= take; removed += take;
                if (Bags[i].Count <= 0) Bags.RemoveAt(i);
            }
            return removed;
        }

        public QuestRecord FindQuest(int id)
        {
            foreach (var q in Quests) if (q.Id == id) return q;
            return null;
        }

        public static long NowUnix() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        /// <summary>A short line for character lists: "Name - Level 3 Warrior".</summary>
        public override string ToString() { return Name + " - Level " + Level + " " + Class; }

        // ------------------------------------------------------------------ text format

        public string Serialize()
        {
            var sb = new StringBuilder(512);
            sb.Append(Header).Append(' ').Append(FormatVersion).Append('\n');
            Kv(sb, "id", Escape(Id));
            Kv(sb, "name", Escape(Name));
            Kv(sb, "class", ((int)Class).ToString(CultureInfo.InvariantCulture));
            Kv(sb, "race", I(Race));
            Kv(sb, "level", I(Level));
            Kv(sb, "xp", I(Xp));
            Kv(sb, "money", I(Money));
            Kv(sb, "map", I(MapId));
            if (HasPos) Kv(sb, "pos", F(Pos.X) + ";" + F(Pos.Y) + ";" + F(Pos.Z) + ";" + F(Pos.O));
            Kv(sb, "health", I(Health));
            Kv(sb, "power", I(Power));
            Kv(sb, "spells", JoinInts(Spells));
            Kv(sb, "bar", JoinInts(ActionBar));
            // iteration 11 (vrcast): every non-empty button as index:packed (packed as unsigned decimal - item values are negative ints)
            var slots = new StringBuilder();
            for (int i = 0; i < ActionButtons.Length; i++) { if (ActionButtons[i] == 0) continue; if (slots.Length > 0) slots.Append(','); slots.Append(I(i)).Append(':').Append(unchecked((uint)ActionButtons[i]).ToString(CultureInfo.InvariantCulture)); }
            if (slots.Length > 0) Kv(sb, "slots", slots.ToString());
            if (FormBarsFilled != 0) Kv(sb, "formbars", I(FormBarsFilled));
            var bags = new StringBuilder();
            foreach (var s in Bags) { if (bags.Length > 0) bags.Append(','); bags.Append(I(s.Entry)).Append('x').Append(I(s.Count)); }
            Kv(sb, "bags", bags.ToString());
            // iteration 11 (npc H11): the backpack slot of each stack, same order as "bags"
            if (Bags.Count > 0) { var pos = new List<int>(); foreach (var s in Bags) pos.Add(s.Slot); Kv(sb, "bagpos", JoinInts(pos)); }
            var eq = new StringBuilder();
            for (int i = 0; i < Equipment.Length; i++) { if (Equipment[i] == 0) continue; if (eq.Length > 0) eq.Append(','); eq.Append(I(i)).Append(':').Append(I(Equipment[i])); }
            Kv(sb, "equip", eq.ToString());
            foreach (var q in Quests) Kv(sb, "quest", I(q.Id) + ":" + I((int)q.State) + ":" + JoinInts(q.Counts));
            Kv(sb, "rewarded", JoinInts(Rewarded));
            if (StartKitPending) Kv(sb, "kit", "1");
            if (PetEntry > 0) Kv(sb, "pet", I(PetEntry));
            if (KnownTaxiNodes.Count > 0) Kv(sb, "taxi", JoinInts(KnownTaxiNodes));
            if (Talents.Count > 0) Kv(sb, "talents", JoinInts(Talents));   // iteration 11 (talents hook 9)
            Kv(sb, "created", CreatedUnix.ToString(CultureInfo.InvariantCulture));
            Kv(sb, "played", PlayedSeconds.ToString(CultureInfo.InvariantCulture));
            Kv(sb, "saved", NowUnix().ToString(CultureInfo.InvariantCulture));
            string body = sb.ToString();
            return body + "check=" + Fnv(body).ToString("X8", CultureInfo.InvariantCulture) + "\n";
        }

        /// <summary>
        /// Reads Serialize() output. Returns null with a reason for text that is not a character, from a newer format, damaged
        /// (checksum mismatch or truncated) or missing the essentials (name, a supported class); older/partial fields take defaults.
        /// </summary>
        public static CharacterRecord Parse(string text, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(text)) { reason = "empty text"; return null; }
            try
            {
                string norm = text.Replace("\r\n", "\n").Replace('\r', '\n');
                var lines = norm.Split('\n');
                int li = 0;
                while (li < lines.Length && lines[li].Trim().Length == 0) li++;
                if (li >= lines.Length) { reason = "empty text"; return null; }
                string head = lines[li].Trim();
                if (!head.StartsWith(Header, StringComparison.Ordinal)) { reason = "not a Somnium WoW character (header '" + Trunc(head, 30) + "')"; return null; }
                int version;
                if (!int.TryParse(head.Substring(Header.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out version) || version < 1)
                { reason = "unreadable format version in '" + Trunc(head, 40) + "'"; return null; }
                if (version > FormatVersion) { reason = "saved by a newer version of the world (format " + version + ", this world reads up to " + FormatVersion + ")"; return null; }

                // Checksum: over the text before the "check=" line, exactly as written.
                int checkAt = norm.LastIndexOf("\ncheck=", StringComparison.Ordinal);
                if (checkAt >= 0)
                {
                    string body = norm.Substring(0, checkAt + 1);
                    int bodyStart = body.IndexOf(Header, StringComparison.Ordinal);
                    if (bodyStart > 0) body = body.Substring(bodyStart);
                    string hex = norm.Substring(checkAt + 7).Trim();
                    int nl = hex.IndexOf('\n');
                    if (nl >= 0) hex = hex.Substring(0, nl).Trim();
                    uint stored;
                    if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out stored) || stored != Fnv(body))
                    { reason = "damaged (checksum mismatch)"; return null; }
                }
                else { reason = "damaged (truncated: no check line)"; return null; }

                var c = new CharacterRecord();
                bool gotName = false, gotClass = false;
                List<int> bagPos = null;                                                // iteration 11 (npc H11): the "bagpos" line, if any
                for (int i = li + 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length == 0) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "id": c.Id = Unescape(val); break;
                        case "name": c.Name = Unescape(val); gotName = c.Name.Length > 0; break;
                        case "class": { int v; if (TryI(val, out v)) { c.Class = (WowClass)v; gotClass = true; } break; }
                        case "race": TryI(val, out c.Race); break;
                        case "level": TryI(val, out c.Level); break;
                        case "xp": TryI(val, out c.Xp); break;
                        case "money": TryI(val, out c.Money); break;
                        case "map": TryI(val, out c.MapId); break;
                        case "pos":
                        {
                            var p = val.Split(';');
                            float x, y, z, o;
                            if (p.Length >= 4 && TryF(p[0], out x) && TryF(p[1], out y) && TryF(p[2], out z) && TryF(p[3], out o))
                            { c.Pos = new WowPos(x, y, z, o); c.HasPos = true; }
                            break;
                        }
                        case "health": TryI(val, out c.Health); break;
                        case "power": TryI(val, out c.Power); break;
                        case "spells": c.Spells = SplitInts(val); break;
                        case "bar":
                        {
                            var b = SplitInts(val);
                            for (int k = 0; k < 12 && k < b.Count; k++) c.ActionBar[k] = b[k];
                            break;
                        }
                        case "bags":
                            foreach (var part in val.Split(','))
                            {
                                int x = part.IndexOf('x');
                                int e, n;
                                if (x > 0 && TryI(part.Substring(0, x), out e) && TryI(part.Substring(x + 1), out n) && e > 0 && n > 0 && c.Bags.Count < BagSlots)
                                    c.Bags.Add(new ItemStack(e, n));
                            }
                            break;
                        case "equip":
                            foreach (var part in val.Split(','))
                            {
                                int k = part.IndexOf(':');
                                int s, e;
                                if (k > 0 && TryI(part.Substring(0, k), out s) && TryI(part.Substring(k + 1), out e) && s >= 0 && s < 19) c.Equipment[s] = e;
                            }
                            break;
                        case "quest":
                        {
                            var p = val.Split(':');
                            int id, st;
                            if (p.Length >= 2 && TryI(p[0], out id) && TryI(p[1], out st) && (st == (int)QuestState.Active || st == (int)QuestState.Complete))
                            {
                                var q = new QuestRecord { Id = id, State = (QuestState)st };
                                if (p.Length >= 3) { var cn = SplitInts(p[2]); for (int k = 0; k < 4 && k < cn.Count; k++) q.Counts[k] = cn[k]; }
                                if (c.FindQuest(id) == null) c.Quests.Add(q);
                            }
                            break;
                        }
                        case "rewarded": c.Rewarded = SplitInts(val); break;
                        case "kit": c.StartKitPending = val.Trim() == "1"; break;
                        case "pet": TryI(val, out c.PetEntry); break;
                        case "taxi": c.KnownTaxiNodes = SplitInts(val); break;
                        // iteration 11 (vrcast): index:packed pairs (unsigned decimal)
                        case "slots":
                            foreach (var part in val.Split(','))
                            {
                                int c2 = part.IndexOf(':');
                                int i2; uint v2;
                                if (c2 > 0 && TryI(part.Substring(0, c2), out i2) && i2 >= 0 && i2 < WowActionSlots.Count &&
                                    uint.TryParse(part.Substring(c2 + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v2))
                                { c.ActionButtons[i2] = unchecked((int)v2); c.HasSlots = true; }
                            }
                            break;
                        case "formbars": TryI(val, out c.FormBarsFilled); break;
                        case "bagpos": bagPos = SplitInts(val); break;                  // iteration 11 (npc H11), applied after the loop
                        case "talents": c.Talents = SplitInts(val); break;             // iteration 11 (talents hook 9)
                        case "created": long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out c.CreatedUnix); break;
                        case "played": long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out c.PlayedSeconds); break;
                        case "saved": long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out c.SavedUnix); break;
                    }
                }
                // iteration 11 (vrcast): an older record has no "slots" - its 12 "bar" spells are buttons 0..11
                if (!c.HasSlots) for (int k = 0; k < 12; k++) c.ActionButtons[k] = WowActionSlots.Spell(c.ActionBar[k]);
                // iteration 11 (npc H11): backpack slots - the saved ones when valid and unique, the rest (or all, without "bagpos") the first free
                if (bagPos != null && bagPos.Count == c.Bags.Count)
                    for (int k = 0; k < c.Bags.Count; k++)
                    {
                        int sl = bagPos[k];
                        bool dup = false;
                        for (int j = 0; j < k; j++) if (c.Bags[j].Slot == sl) { dup = true; break; }
                        if (sl >= 0 && sl < BagSlots && !dup) c.Bags[k].Slot = sl;
                    }
                foreach (var st in c.Bags) if (st.Slot < 0) st.Slot = c.FirstFreeBagSlot();
                if (!gotName) { reason = "no character name"; return null; }
                if (!gotClass || !Rules.WowFormulas.IsSupportedClass(c.Class)) { reason = "unsupported class " + (int)c.Class; return null; }
                if (c.Level < 1) c.Level = 1;
                if (c.Level > Rules.WowFormulas.MaxLevel) c.Level = Rules.WowFormulas.MaxLevel;
                if (c.Xp < 0) c.Xp = 0;
                if (c.Money < 0) c.Money = 0;
                if (!Rules.WowRaces.IsPlayable(c.Race)) c.Race = Rules.WowRaces.NightElf;   // old saves (no race key) were Night Elves
                if (string.IsNullOrEmpty(c.Id)) c.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                return c;
            }
            catch (Exception e)
            {
                reason = "unreadable: " + e.Message;
                return null;
            }
        }

        /// <summary>Several characters in one text (records separated by blank lines).</summary>
        public static string SerializeMany(IEnumerable<CharacterRecord> records)
        {
            var sb = new StringBuilder();
            foreach (var r in records) { if (r == null) continue; sb.Append(r.Serialize()).Append('\n'); }
            return sb.ToString();
        }

        /// <summary>Reads SerializeMany output; damaged records are skipped and their reasons added to problems (may be null).</summary>
        public static List<CharacterRecord> ParseMany(string text, List<string> problems)
        {
            var list = new List<CharacterRecord>();
            if (string.IsNullOrEmpty(text)) return list;
            string norm = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int at = norm.IndexOf(Header, StringComparison.Ordinal);
            while (at >= 0)
            {
                int next = norm.IndexOf("\n" + Header, at + Header.Length, StringComparison.Ordinal);
                string chunk = next < 0 ? norm.Substring(at) : norm.Substring(at, next + 1 - at);
                string why;
                var r = Parse(chunk, out why);
                if (r != null) list.Add(r);
                else if (problems != null) problems.Add(why);
                at = next < 0 ? -1 : next + 1;
            }
            return list;
        }

        // ------------------------------------------------------------------ helpers

        private static void Kv(StringBuilder sb, string k, string v) { sb.Append(k).Append('=').Append(v).Append('\n'); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        private static bool TryI(string s, out int v) { return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v); }
        private static bool TryF(string s, out float v) { return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v); }
        private static string Trunc(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "..."; }

        private static string JoinInts(IEnumerable<int> v)
        {
            var sb = new StringBuilder();
            foreach (int x in v) { if (sb.Length > 0) sb.Append(','); sb.Append(I(x)); }
            return sb.ToString();
        }

        private static List<int> SplitInts(string s)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var p in s.Split(',')) { int v; if (TryI(p, out v)) list.Add(v); }
            return list;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool edge = (i == 0 || i == s.Length - 1) && c == ' ';
                if (c == '%' || c == '=' || c == '\n' || c == '\r' || c < 32 || edge) sb.Append('%').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int code;
                if (s[i] == '%' && i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                { sb.Append((char)code); i += 4; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>FNV-1a 32 over the UTF-16 code units.</summary>
        private static uint Fnv(string s)
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }
    }
}
