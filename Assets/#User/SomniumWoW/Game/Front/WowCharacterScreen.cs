using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The room panel's Play tab: the saved characters (WoW's race and class icons, the Somnium nickname in the class colour,
    /// "Level N Race Class", the starting zone) with Play and Delete (confirmed in a dialog), and "New character" ->
    /// FACTION (Alliance / Horde: the crests of UI-CharacterCreate-Factions, the names and FACTION_INFO texts from the glue
    /// strings) -> RACE (the faction's races with WoW's race icons (UI-CharacterCreate-Races + RACE_ICON_TCOORDS), the
    /// RACE_INFO description, the racial abilities with their spell icons (Rules + Spell.dbc) and the starting zone) ->
    /// CLASS (only the classes the race can be, CharBaseInfo.dbc via Rules: WoW's class icons, colours, role lines and
    /// description) -> Create. The Somnium avatar stays the character: the race gives racial abilities, stats and the zone.
    /// Built into WowRoom.PlayRoot; the creation screens also use the time row's space (WowRoom.SetPlayTall).
    /// </summary>
    public sealed class WowCharacterScreen
    {
        public enum View { Waiting, List, Faction, Race, Class }

        public event Action<CharacterRecord> PlayPressed;

        /// <summary>Create pressed: the class and the ChrRaces id.</summary>
        public event Action<WowClass, int> CreatePressed;

        public event Action<CharacterRecord> DeleteConfirmed;

        /// <summary>Online (iteration 6): Back on the faction screen - WowGame shows the server's characters again.</summary>
        public event Action OnlineBack;

        private bool _online;

        /// <summary>
        /// Online mode (iteration 6): the list / waiting views draw nothing (WowOnlineScreen owns the Play tab then) and the
        /// faction, race and class screens create for the server: no roster limit, every race (the server has every zone).
        /// </summary>
        public void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online;
            if (_view != View.Waiting) _view = View.List;
            Rebuild();
        }

        public bool Online { get { return _online; } }

        // CharacterCreate.lua:15 FACTION_BACKDROP_COLOR_TABLE (border r,g,b then background r,g,b) - the screen's faction tint.
        private static readonly Color AllianceBorder = new Color(0.5f, 0.5f, 0.5f), AllianceBack = new Color(0.09f, 0.09f, 0.19f, 0.85f);
        private static readonly Color HordeBorder = new Color(0.5f, 0.2f, 0.2f), HordeBack = new Color(0.19f, 0.05f, 0.05f, 0.85f);

        private WowRoom _room;
        private WowFiles _files;
        private List<WowClassEntry> _classes = new List<WowClassEntry>();
        private List<WowRaceEntry> _races = new List<WowRaceEntry>();
        private List<WowFactionEntry> _factions = new List<WowFactionEntry>();
        private readonly List<CharacterRecord> _chars = new List<CharacterRecord>();
        private string _waitText = "Reading your characters...";
        private string _note = "";
        private bool _readOnly;
        private string _name = WowSaves.FallbackName;
        private View _view = View.Waiting;
        private WowFaction _faction = WowFaction.Alliance;
        private int _race = 1;
        private WowClass _selected = WowClass.Warrior;
        private GameObject _content;
        private GameObject _dialog;

        public View Current { get { return _view; } }

        public void SetClasses(List<WowClassEntry> classes)
        {
            _classes = classes ?? new List<WowClassEntry>();
        }

        /// <summary>The race and faction data (WowRaceInfo.Load) and the files (map names, spell icons).</summary>
        public void SetRaces(List<WowRaceEntry> races, List<WowFactionEntry> factions, WowFiles files)
        {
            _races = races ?? new List<WowRaceEntry>();
            _factions = factions ?? new List<WowFactionEntry>();
            _files = files;
        }

        /// <summary>Hooks into the room's Play tab (call after every WowRoom.BuildPanel) and draws the current view.</summary>
        public void Attach(WowRoom room)
        {
            _room = room;
            _content = null;
            _dialog = null;
            Rebuild();
        }

        public void ShowWaiting(string text)
        {
            if (_view == View.Waiting && _waitText == (text ?? "") && _content != null) return;
            _waitText = text ?? "";
            _view = View.Waiting;
            Rebuild();
        }

        /// <summary>The roster to list; name = the Somnium nickname shown on the creation screens.</summary>
        public void SetRoster(IReadOnlyList<CharacterRecord> chars, string name, bool readOnly, string note)
        {
            _chars.Clear();
            if (chars != null) foreach (CharacterRecord c in chars) _chars.Add(c);
            _name = string.IsNullOrEmpty(name) ? WowSaves.FallbackName : name;
            _readOnly = readOnly;
            _note = note ?? "";
            if (_view == View.Waiting) _view = View.List;
            Rebuild();
        }

        public void ShowList()
        {
            _view = View.List;
            Rebuild();
        }

        /// <summary>"New character": the faction screen.</summary>
        public void ShowCreate()
        {
            _view = View.Faction;
            WowLog.Info("Character create: faction screen for " + _name + " (" + _races.Count + " races, " + _classes.Count + " classes; " + _chars.Count + " of " + WowRoster.MaxCharacters + " characters).");
            Rebuild();
        }

        // ------------------------------------------------------------------ drawing

        private void Rebuild()
        {
            CloseDialog();

            if (_content != null)
            {
                UnityEngine.Object.Destroy(_content);
                _content = null;
            }

            if (_room == null || _room.PlayRoot == null)
            {
                return;
            }

            if (_online && (_view == View.List || _view == View.Waiting))
            {
                _room.SetPlayTall(false);
                return;   // WowOnlineScreen draws the Play tab
            }

            bool tall = _view == View.Faction || _view == View.Race || _view == View.Class;
            _room.SetPlayTall(tall);
            RectTransform root = WowUi.Place(_room.PlayRoot, "Characters " + _view, 0f, 0f, WowRoom.PanelWidth, tall ? WowRoom.TallHeight : WowRoom.ContentHeight);
            _content = root.gameObject;

            switch (_view)
            {
                case View.Waiting:
                    WowUi.Text(root, "Waiting", _waitText, 24f, WowUi.GlueGold, TextAlignmentOptions.Center, 60f, 110f, WowRoom.PanelWidth - 120f, 90f);
                    break;
                case View.List:
                    BuildList(root);
                    break;
                case View.Faction:
                    BuildFaction(root);
                    break;
                case View.Race:
                    BuildRace(root);
                    break;
                case View.Class:
                    BuildClass(root);
                    break;
            }
        }

        private bool CanCreate { get { return (_online || (!_readOnly && _chars.Count < WowRoster.MaxCharacters)) && _classes.Count > 0 && _races.Count > 0; } }

        private void BuildList(RectTransform root)
        {
            WowUi.Text(root, "Title", "Choose a character", 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 420f, 36f);
            WowUi.Text(root, "Count", _chars.Count + " of " + WowRoster.MaxCharacters, 18f, WowUi.Grey, TextAlignmentOptions.Right, 560f, 10f, 210f, 26f);
            WowGlueButton add = WowUi.Button(root, "New character", 790f, -4f, 260f, 44f, 21f, () => ShowCreate());
            add.Interactable = CanCreate;

            if (_chars.Count == 0)
            {
                WowUi.Text(root, "Empty", "No character yet.\nChoose \"New character\" to pick your faction, race and class - you play as your Somnium avatar, named " + _name + ".", 22f, Color.white, TextAlignmentOptions.Center, 90f, 100f, WowRoom.PanelWidth - 180f, 110f);
            }

            Texture2D classAtlas = WowUi.Tex(WowClassInfo.IconAtlas);
            Texture2D raceAtlas = WowUi.Tex(WowRaceInfo.RaceAtlas);

            for (int i = 0; i < _chars.Count && i < WowRoster.MaxCharacters; i++)
            {
                CharacterRecord c = _chars[i];
                WowClassEntry e = WowClassInfo.Find(_classes, c.Class);
                WowRaceEntry re = WowRaceInfo.Find(_races, c.Race);
                int col = i / 5, row = i % 5;
                RectTransform r = WowUi.Place(root, "Character " + i, 50f + col * 510f, 46f + row * 56f, 490f, 52f);
                var bg = r.gameObject.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.35f);
                bg.raycastTarget = false;

                if (re != null) WowUi.Picture(r, "Race Icon", raceAtlas, RaceRect(re), 4f, 6f, 40f, 40f);
                if (e != null) WowUi.Picture(r, "Class Icon", classAtlas, IconRect(e), 48f, 6f, 40f, 40f);

                Color cc = e != null ? new Color(e.R, e.G, e.B) : WowUi.GlueGold;
                string raceName = re != null ? re.Name : "";
                string zone = re != null ? re.ZoneName : "";
                // Heights leave room for TMP's line height: an Ellipsis text whose line does not fit shows nothing (lab f3_flow_back).
                TextMeshProUGUI nm = WowUi.Text(r, "Name", c.Name, 17f, cc, TextAlignmentOptions.Left, 96f, -3f, 220f, 24f);
                nm.enableAutoSizing = true;
                nm.fontSizeMin = 11f;
                nm.fontSizeMax = 17f;
                TextMeshProUGUI lvl = WowUi.Text(r, "Level", "Level " + c.Level + " " + (raceName.Length > 0 ? raceName + " " : "") + (e != null ? e.Name : c.Class.ToString()), 14f, Color.white, TextAlignmentOptions.Left, 96f, 16f, 220f, 20f);
                lvl.enableAutoSizing = true;
                lvl.fontSizeMin = 10f;
                lvl.fontSizeMax = 14f;
                TextMeshProUGUI zn = WowUi.Text(r, "Zone", zone, 13f, WowUi.Grey, TextAlignmentOptions.Left, 96f, 33f, 220f, 19f);
                zn.enableAutoSizing = true;
                zn.fontSizeMin = 10f;
                zn.fontSizeMax = 13f;
                CharacterRecord pick = c;
                WowUi.Button(r, "Play", 320f, 5f, 90f, 42f, 20f, () => { if (PlayPressed != null) PlayPressed(pick); });
                WowGlueButton del = WowUi.Button(r, "Delete", 412f, 5f, 74f, 42f, 15f, () => ConfirmDelete(pick));
                del.Interactable = !_readOnly;
            }

            if (_note.Length > 0)
            {
                WowUi.Text(root, "Note", _note, 17f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 46f + 5f * 56f, WowRoom.PanelWidth - 100f, 24f);
            }
        }

        // ------------------------------------------------------------------ faction

        private void BuildFaction(RectTransform root)
        {
            WowUi.Text(root, "Title", "Choose your faction", 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 520f, 36f);
            WowUi.Button(root, "Back", 850f, -4f, 200f, 44f, 21f, () =>
            {
                WowLog.Info("Character create: back to the character list" + (_online ? " (online)." : "."));
                if (_online) { _view = View.List; Rebuild(); var h = OnlineBack; if (h != null) h(); }
                else ShowList();
            });
            Texture2D crests = WowUi.Tex(WowRaceInfo.FactionAtlas);

            for (int i = 0; i < _factions.Count && i < 2; i++)
            {
                WowFactionEntry f = _factions[i];
                float x = 50f + i * 510f;
                bool horde = f.Faction == WowFaction.Horde;
                RectTransform p = WowUi.Place(root, "Faction " + f.Faction, x, 46f, 490f, 362f);
                var bg = p.gameObject.AddComponent<Image>();
                bg.color = horde ? HordeBack : AllianceBack;
                bg.raycastTarget = false;
                var outline = p.gameObject.AddComponent<Outline>();
                outline.effectColor = horde ? HordeBorder : AllianceBorder;
                outline.effectDistance = new Vector2(2f, -2f);

                WowFaction pick = f.Faction;
                WowIconButton.Create(p, f.Name, crests, new Rect(f.IconLeft, f.IconTop, f.IconRight - f.IconLeft, f.IconBottom - f.IconTop), (490f - 104f) / 2f, 14f, 104f, () => PickFaction(pick));
                WowUi.Text(p, "Name", f.Name, 30f, WowUi.GlueGold, TextAlignmentOptions.Center, 20f, 124f, 450f, 38f);
                TextMeshProUGUI d = WowUi.Text(p, "Description", f.Description, 16f, new Color(0.93f, 0.9f, 0.82f), TextAlignmentOptions.Top, 24f, 166f, 442f, 110f);
                d.enableAutoSizing = true;
                d.fontSizeMin = 11f;
                d.fontSizeMax = 16f;

                var names = new List<string>();
                foreach (int race in f.Races)
                {
                    WowRaceEntry re = WowRaceInfo.Find(_races, race);
                    if (re != null) names.Add(re.Name);
                }

                TextMeshProUGUI rn = WowUi.Text(p, "Races", string.Join(", ", names.ToArray()), 17f, Color.white, TextAlignmentOptions.Center, 20f, 280f, 450f, 24f);
                rn.enableAutoSizing = true;
                rn.fontSizeMin = 12f;
                rn.fontSizeMax = 17f;
                WowGlueButton choose = WowUi.Button(p, "Choose " + f.Name, 125f, 310f, 240f, 44f, 20f, () => PickFaction(pick));
                choose.Interactable = CanCreate;
            }

            if (_factions.Count == 0)
            {
                WowUi.Text(root, "No Factions", "The race data could not be read from the game files (see the log).", 20f, WowUi.Grey, TextAlignmentOptions.Center, 60f, 170f, WowRoom.PanelWidth - 120f, 40f);
            }
        }

        private void PickFaction(WowFaction f)
        {
            if (!CanCreate) return;
            bool changed = f != _faction || WowRaceInfo.Find(_races, _race) == null || WowRaceInfo.Find(_races, _race).Faction != f;
            _faction = f;

            if (changed)
            {
                foreach (WowRaceEntry r in _races)
                {
                    if (r.Faction == f) { _race = r.Race; break; }
                }
            }

            WowLog.Info("Character create: faction " + f + " chosen.");
            _view = View.Race;
            Rebuild();
        }

        // ------------------------------------------------------------------ race

        private void BuildRace(RectTransform root)
        {
            WowFactionEntry fe = WowRaceInfo.FindFaction(_factions, _faction);
            WowUi.Text(root, "Title", "Choose your race" + (fe != null ? " - " + fe.Name : ""), 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 560f, 36f);
            WowUi.Button(root, "Back", 640f, -4f, 190f, 44f, 21f, () => { WowLog.Info("Character create: back to the faction screen."); _view = View.Faction; Rebuild(); });
            WowRaceEntry sel = WowRaceInfo.Find(_races, _race);
            WowGlueButton next = WowUi.Button(root, "Next", 850f, -4f, 200f, 44f, 22f, () =>
            {
                WowRaceEntry s = WowRaceInfo.Find(_races, _race);
                if (s == null) return;
                if (!s.Classes.Contains(_selected) && s.Classes.Count > 0) _selected = s.Classes.Contains(WowClass.Warrior) ? WowClass.Warrior : s.Classes[0];
                WowLog.Info("Character create: race " + s.Name + " chosen (" + s.Classes.Count + " classes, zone " + s.ZoneName + ").");
                _view = View.Class;
                Rebuild();
            });
            next.Interactable = sel != null && sel.Classes.Count > 0;

            Texture2D atlas = WowUi.Tex(WowRaceInfo.RaceAtlas);
            const float Size = 64f, Step = 122f;
            int n = 0;

            foreach (WowRaceEntry r in _races)
            {
                if (r.Faction != _faction) continue;
                float x = 56f + n * Step;
                int race = r.Race;
                WowIconButton b = WowIconButton.Create(root, r.Name, atlas, RaceRect(r), x, 50f, Size, () => SelectRace(race));
                b.Selected = r.Race == _race;
                WowUi.Text(root, "Race Name " + r.Name, r.Name, 17f, r.Race == _race ? WowUi.GlueGold : Color.white, TextAlignmentOptions.Center, x + Size / 2f - 58f, 118f, 116f, 24f);
                n++;
            }

            if (sel == null)
            {
                WowUi.Text(root, "No Race", "The race data could not be read from the game files (see the log).", 20f, WowUi.Grey, TextAlignmentOptions.Center, 60f, 170f, WowRoom.PanelWidth - 120f, 40f);
                return;
            }

            // The starting zone (top right).
            WowUi.Text(root, "Zone Title", "Starting zone", 18f, WowUi.GlueGold, TextAlignmentOptions.Left, 690f, 52f, 360f, 24f);
            WowUi.Text(root, "Zone", sel.ZoneName, 26f, Color.white, TextAlignmentOptions.Left, 690f, 76f, 360f, 34f);
            string map = MapName(sel.ZoneMapId);
            WowUi.Text(root, "Zone Map", sel.ZoneReady || _online ? map : (map.Length > 0 ? map + " - " : "") + "not ready in this version", 16f, sel.ZoneReady || _online ? WowUi.Grey : new Color(1f, 0.35f, 0.3f), TextAlignmentOptions.Left, 690f, 110f, 360f, 24f);

            // Description (left) and racial abilities (right).
            WowUi.Text(root, "Race", sel.Name, 28f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 150f, 460f, 36f);
            TextMeshProUGUI d = WowUi.Text(root, "Description", sel.Description, 16f, new Color(0.93f, 0.9f, 0.82f), TextAlignmentOptions.TopLeft, 56f, 190f, 470f, 220f);
            d.enableAutoSizing = true;
            d.fontSizeMin = 11f;
            d.fontSizeMax = 16f;

            WowUi.Text(root, "Racials Title", "Racial abilities", 20f, WowUi.GlueGold, TextAlignmentOptions.Left, 560f, 152f, 490f, 28f);
            List<WowRacialSpell> racials = sel.RacialsFor(_selected);

            if (racials.Count == 0)
            {
                TextMeshProUGUI al = WowUi.Text(root, "Ability Lines", string.Join("\n", sel.AbilityLines.ToArray()), 16f, Color.white, TextAlignmentOptions.TopLeft, 560f, 184f, 490f, 224f);
                al.enableAutoSizing = true;
                al.fontSizeMin = 11f;
                al.fontSizeMax = 16f;
                return;
            }

            int rows = racials.Count <= 6 ? racials.Count : (racials.Count + 1) / 2;
            float rowH = Mathf.Min(38f, 224f / Mathf.Max(1, rows));
            float colW = racials.Count <= 6 ? 490f : 245f;

            for (int i = 0; i < racials.Count && i < 12; i++)
            {
                WowRacialSpell s = racials[i];
                int col = i / rows, row = i % rows;
                float x = 560f + col * colW, y = 184f + row * rowH;
                Texture2D icon = IconTex(s.IconPath);
                RawImage img = WowUi.Picture(root, "Racial Icon " + s.Id, icon, new Rect(0f, 0f, 1f, 1f), x, y + 1f, rowH - 4f, rowH - 4f);
                if (icon == null) img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                TextMeshProUGUI t = WowUi.Text(root, "Racial " + s.Id, s.Name + (s.Passive ? "  (passive)" : ""), 17f, s.Passive ? WowUi.Grey : Color.white, TextAlignmentOptions.Left, x + rowH + 2f, y, colW - rowH - 6f, rowH - 2f);
                t.enableAutoSizing = true;
                t.fontSizeMin = 11f;
                t.fontSizeMax = 17f;
            }
        }

        private void SelectRace(int race)
        {
            if (_race == race) return;
            _race = race;
            WowRaceEntry r = WowRaceInfo.Find(_races, race);
            WowLog.Info("Character create: " + (r != null ? r.Name : race.ToString()) + " selected.");
            Rebuild();
        }

        // ------------------------------------------------------------------ class

        private void BuildClass(RectTransform root)
        {
            WowRaceEntry race = WowRaceInfo.Find(_races, _race);
            WowUi.Text(root, "Title", "Choose a class" + (race != null ? " - " + race.Name : ""), 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 540f, 36f);
            WowUi.Button(root, "Back", 610f, -4f, 200f, 44f, 21f, () => { WowLog.Info("Character create: back to the race screen."); _view = View.Race; Rebuild(); });

            var offered = new List<WowClassEntry>();
            if (race != null) foreach (WowClass c in race.Classes) { WowClassEntry e = WowClassInfo.Find(_classes, c); if (e != null) offered.Add(e); }
            if (offered.Count > 0 && offered.Find(x => x.Class == _selected) == null) _selected = offered[0].Class;
            WowClassEntry s = WowClassInfo.Find(offered, _selected);

            WowGlueButton create = WowUi.Button(root, "Create", 830f, -4f, 220f, 44f, 22f, () =>
            {
                WowClassEntry sel = WowClassInfo.Find(offered, _selected);
                if (sel == null || race == null) return;
                WowLog.Info("Character create: create " + race.Name + " " + sel.Name + " for " + _name + " (zone " + race.ZoneName + ").");
                if (CreatePressed != null) CreatePressed(_selected, race.Race);
            });
            create.Interactable = CanCreate && s != null && race != null && (race.ZoneReady || _online);

            Texture2D atlas = WowUi.Tex(WowClassInfo.IconAtlas);
            const float Size = 64f, Step = 174f;
            float x0 = (WowRoom.PanelWidth - ((offered.Count - 1) * Step + Size)) / 2f;

            for (int i = 0; i < offered.Count; i++)
            {
                WowClassEntry e = offered[i];
                float x = x0 + i * Step;
                WowClass cls = e.Class;
                WowIconButton b = WowIconButton.Create(root, e.Name, atlas, IconRect(e), x, 50f, Size, () => SelectClass(cls));
                b.Selected = cls == _selected;
                WowUi.Text(root, "Class Name " + e.Name, e.Name, 20f, new Color(e.R, e.G, e.B), TextAlignmentOptions.Center, x + Size / 2f - 85f, 118f, 170f, 26f);
            }

            if (s == null || race == null)
            {
                WowUi.Text(root, "No Classes", "The class data could not be read from the game files (see the log).", 20f, WowUi.Grey, TextAlignmentOptions.Center, 60f, 170f, WowRoom.PanelWidth - 120f, 40f);
                return;
            }

            WowUi.Text(root, "Selected", s.Name, 30f, new Color(s.R, s.G, s.B), TextAlignmentOptions.Left, 56f, 152f, 330f, 40f);
            string who = _online ? race.Name + " " + s.Name + ", starts in " + race.ZoneName + " - a name comes next" : _name + " - " + race.Name + " " + s.Name + ", starts in " + race.ZoneName + (race.ZoneReady ? "" : " (not ready in this version)");
            TextMeshProUGUI w = WowUi.Text(root, "Who", who, 17f, race.ZoneReady || _online ? WowUi.Grey : new Color(1f, 0.35f, 0.3f), TextAlignmentOptions.Right, 390f, 160f, 660f, 26f);
            w.enableAutoSizing = true;
            w.fontSizeMin = 12f;
            w.fontSizeMax = 17f;
            TextMeshProUGUI roles = WowUi.Text(root, "Roles", string.Join("\n", s.InfoLines.ToArray()), 17f, Color.white, TextAlignmentOptions.TopLeft, 56f, 198f, 400f, 210f);
            roles.enableAutoSizing = true;
            roles.fontSizeMin = 12f;
            roles.fontSizeMax = 17f;
            TextMeshProUGUI flavor = WowUi.Text(root, "Description", s.Description, 16f, new Color(0.93f, 0.9f, 0.82f), TextAlignmentOptions.TopLeft, 470f, 198f, 590f, 210f);
            flavor.enableAutoSizing = true;
            flavor.fontSizeMin = 11f;
            flavor.fontSizeMax = 17f;
        }

        private void SelectClass(WowClass cls)
        {
            if (_selected == cls) return;
            _selected = cls;
            WowClassEntry e = WowClassInfo.Find(_classes, cls);
            WowLog.Info("Character create: " + (e != null ? e.Name : cls.ToString()) + " selected.");
            Rebuild();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>WoW texcoords (left, right, top, bottom) -> WowUi's (x = left, y = top, width, height).</summary>
        private static Rect IconRect(WowClassEntry e)
        {
            return new Rect(e.IconLeft, e.IconTop, e.IconRight - e.IconLeft, e.IconBottom - e.IconTop);
        }

        /// <summary>The race's face: the female one when the race voice setting is female (the avatar has no WoW sex).</summary>
        private static Rect RaceRect(WowRaceEntry e)
        {
            if (WowSettings.VoiceGender == 1) return new Rect(e.FemaleLeft, e.FemaleTop, e.FemaleRight - e.FemaleLeft, e.FemaleBottom - e.FemaleTop);
            return new Rect(e.IconLeft, e.IconTop, e.IconRight - e.IconLeft, e.IconBottom - e.IconTop);
        }

        private static Texture2D IconTex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return WowUi.Tex(path.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? path : path + ".blp");
        }

        private string MapName(int mapId)
        {
            if (_files == null || mapId < 0) return "";
            try { return WowLocations.MapName(_files, mapId) ?? ""; }
            catch (Exception) { return ""; }
        }

        // ------------------------------------------------------------------ delete confirmation

        private void ConfirmDelete(CharacterRecord c)
        {
            if (_room == null || _room.Panel == null || c == null) return;
            CloseDialog();
            WowClassEntry e = WowClassInfo.Find(_classes, c.Class);
            WowRaceEntry re = WowRaceInfo.Find(_races, c.Race);
            string what = c.Name + ", level " + c.Level + " " + (re != null ? re.Name + " " : "") + (e != null ? e.Name : c.Class.ToString());
            WowLog.Info("Characters: delete asked for " + what + " (" + c.Id + ").");

            Transform panel = _room.Panel.transform;
            RectTransform blocker = WowUi.Place(panel, "Delete Dialog", 0f, 0f, WowRoom.PanelWidth, WowRoom.PanelHeight);
            _dialog = blocker.gameObject;
            var dim = _dialog.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true;

            const float W = 640f, H = 240f;
            float x = (WowRoom.PanelWidth - W) / 2f, y = 330f;
            WowUi.Backdrop(blocker, x, y, W, H, 32f);
            WowUi.Text(blocker, "Question", "Delete " + what + "?\nThis cannot be undone.", 23f, Color.white, TextAlignmentOptions.Center, x + 40f, y + 36f, W - 80f, 90f);
            CharacterRecord target = c;
            WowUi.Button(blocker, "Delete", x + 80f, y + 150f, 220f, 48f, 22f, () =>
            {
                CloseDialog();
                WowLog.Info("Characters: delete confirmed for " + target.Name + " (" + target.Id + ").");
                if (DeleteConfirmed != null) DeleteConfirmed(target);
            });
            WowUi.Button(blocker, "Cancel", x + W - 300f, y + 150f, 220f, 48f, 22f, () => { WowLog.Info("Characters: delete cancelled."); CloseDialog(); });
        }

        public void CloseDialog()
        {
            if (_dialog != null)
            {
                UnityEngine.Object.Destroy(_dialog);
                _dialog = null;
            }
        }
    }
}
