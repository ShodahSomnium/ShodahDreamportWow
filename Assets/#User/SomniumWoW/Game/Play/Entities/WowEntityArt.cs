using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TMPro;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>What an NPC / a player holds, classified from the ItemDisplayInfo model file name (e.g. Sword_2H_Claymore_A_01)
    /// and, for where it hangs when sheathed, Item.dbc (SheatheType / InventoryType of the first item row with this display).</summary>
    internal sealed class WowWeaponInfo
    {
        public int DisplayId;
        public string Error;          // null when the model resolves
        public string ModelName;      // file name without the folder
        public bool Shield;           // Item\ObjectComponents\Shield -> attachment 0
        public bool TwoHand;          // "_2H_" in the name -> Attack2H / Ready2H
        public bool Bow;              // "Bow_" (not a crossbow): WoW holds bows in the LEFT hand -> attachment 2
        public int SheatheType = -1;  // Item.dbc SheatheType (-1 = no Item.dbc row shows this display)
        public int InventoryType = -1;// Item.dbc InventoryType (15 bow, 25 thrown, 26 ranged right = gun/crossbow/wand, 28 relic)
    }

    /// <summary>A gameobject's model from GameObjectDisplayInfo.dbc (ModelName, GeoBoxMin/Max).</summary>
    internal sealed class WowGoInfo
    {
        public int DisplayId;
        public string ModelPath;      // .m2 (from .mdx) or .wmo
        public bool Wmo;
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;   // GeoBox, WoW model space (yards); all 0 = none
        public string Error;
        /// <summary>Iteration 11 (trainer U11 a): GameObjectDisplayInfo Sound[10] (fields 2-11), SoundEntries ids, 0 = none - e.g. the
        /// fishing bobber's Sound[6] for its custom animation (the trainer's one-shot plays Sound[6 + n]).</summary>
        public int[] Sounds = new int[10];
    }

    /// <summary>A player character prepared on the worker: the composed look (CharacterComposer) and the parsed race model.</summary>
    internal sealed class WowCharJob
    {
        public CharacterLook Look;    // ItemDisplayIds is a private copy
        public float Priority;        // distance squared (main thread writes it; nearest first)
        public volatile bool Done;
        public int TexSeq;            // the prepared-texture sequence number its textures end at
        public CharacterComposer.Result Res;
        public M2Model Model;
        public string Name;
        public string Error;
    }

    /// <summary>
    /// The art the entity views share, all read from the player's game files at runtime (paths verified in the Entities report):
    /// WoW's selection circle (Wow.exe names Textures\UnitSelectTexture.blp), the nameplate border/glow (Wow.exe names
    /// Interface\Tooltips\Nameplate-Border / -Glow), the status bar fill, the loot sparkle star (LootFX.m2's particle texture)
    /// and the quest marker models (Wow.exe names Interface\Buttons\TalkToMe*.mdx). Iteration 6 (online: entities keep arriving
    /// for the whole session): ONE worker thread lives as long as the session and takes jobs nearest-first - creature displays,
    /// held items, gameobject displays (M2 or WMO) and player characters (the body texture composed off the main thread) - so
    /// the main thread only uploads (a small budget per frame) and builds. Owned by WowEntityViews for one session.
    /// </summary>
    internal sealed class WowEntityArt
    {
        public const string SelectCirclePath = @"Textures\UnitSelectTexture.blp";
        public const string PlateBorderPath = @"Interface\Tooltips\Nameplate-Border.blp";
        public const string PlateGlowPath = @"Interface\Tooltips\Nameplate-Glow.blp";
        public const string BarFillPath = @"Interface\TargetingFrame\UI-StatusBar.blp";
        public const string SparklePath = @"SPELLS\STAR5A.BLP";
        public const string MarkerAvailable = @"Interface\Buttons\TalkToMe.m2";              // yellow "!"
        public const string MarkerIncomplete = @"Interface\Buttons\TalkToMeQuestion_Grey.m2"; // grey "?"
        public const string MarkerComplete = @"Interface\Buttons\TalkToMeQuestionMark.m2";    // yellow "?"

        private const int KindDisplay = 1, KindWeapon = 2, KindGo = 3, KindChar = 4;
        private const int MaxWarnings = 40;

        private sealed class Pic
        {
            public string Path;
            public int W, H;
            public byte[] Rgba;
            public string Error;
        }

        private sealed class Job
        {
            public int Kind, Id;
            public long Key;
            public float Priority;
            public WowCharJob Char;
        }

        private readonly WowFiles _files;
        private readonly object _lock = new object();
        private readonly Queue<WowPreparedTexture> _prepared = new Queue<WowPreparedTexture>();
        private readonly Queue<Pic> _pics = new Queue<Pic>();
        private readonly Dictionary<int, WowWeaponInfo> _weapons = new Dictionary<int, WowWeaponInfo>();
        private readonly Dictionary<int, WowGoInfo> _gos = new Dictionary<int, WowGoInfo>();
        private readonly Dictionary<int, bool> _characterDisplays = new Dictionary<int, bool>();   // display -> its model is a Character\ race model
        private readonly List<Job> _queue = new List<Job>();
        private readonly Dictionary<long, Job> _queued = new Dictionary<long, Job>();
        private readonly Dictionary<long, int> _doneSeq = new Dictionary<long, int>();             // finished job -> its last texture's sequence number
        private readonly HashSet<string> _seen = new HashSet<string>();                           // worker only: texture keys already prepared
        private readonly object _itemLock = new object();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private readonly Stopwatch _pump = new Stopwatch();
        private Dictionary<int, int> _itemRows;    // ItemDisplayInfo id -> Item.dbc row (the first item showing it)
        private DbcTable _itemDbc;
        private Thread _thread;
        private volatile bool _cancel;
        private volatile bool _uiDone;
        private int _enqSeq, _upSeq;               // prepared textures enqueued / uploaded (FIFO: the n-th enqueued is the n-th uploaded)
        private int _uiSeq = int.MaxValue;         // the sequence number the UI art ends at
        private int _models, _failedModels, _textures, _displays, _weaponCount, _gameObjects, _characters, _uploaded, _warnings;
        private long _workerMs;
        private bool _reported, _adopted;

        public Texture2D SelectCircle, PlateBorder, PlateGlow, BarFill, Sparkle;

        public WowEntityArt(WowFiles files)
        {
            _files = files;
        }

        /// <summary>The shared UI art (circle, plates, sparkle, markers) is prepared and uploaded (or 20 s passed: never wait forever).</summary>
        public bool Ready
        {
            get
            {
                if (_files == null || _since.Elapsed.TotalSeconds > 20.0) return true;
                if (!_uiDone) return false;
                lock (_lock) return _upSeq >= _uiSeq && _pics.Count == 0;
            }
        }

        /// <summary>Jobs waiting for the worker (for the log).</summary>
        public int Pending { get { lock (_lock) return _queue.Count; } }

        /// <summary>Starts the worker (UI art first) and queues these creature display ids and held-item display ids. Main thread.</summary>
        public void Start(int[] displayIds, int[] weaponIds)
        {
            if (_files == null) { _uiDone = true; return; }
            if (displayIds != null) foreach (int id in displayIds) WantDisplay(id, float.MaxValue);
            if (weaponIds != null) foreach (int id in weaponIds) WantWeapon(id, float.MaxValue);
            if (_thread != null) return;
            _thread = new Thread(Work);
            _thread.IsBackground = true;
            _thread.Name = "SomniumWoW entity art";
            _thread.Start();
        }

        /// <summary>Stops the worker and destroys the textures this class made itself (cached WowTextures stay).</summary>
        public void Dispose()
        {
            _cancel = true;
            lock (_lock) Monitor.PulseAll(_lock);
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            if (_textMaterial != null) { UnityEngine.Object.Destroy(_textMaterial); _textMaterial = null; }
        }

        // ------------------------------------------------------------------ requests (main thread)

        /// <summary>True when the creature display is prepared and its textures uploaded (build now without a hitch); else queues it
        /// (priority = distance squared: the nearest first; a later, nearer request moves it up).</summary>
        public bool WantDisplay(int displayId, float priority) { return Want(KindDisplay, displayId, priority); }

        /// <summary>As WantDisplay, for a held item (ItemDisplayInfo id): classification, Item.dbc sheath data, model and textures.</summary>
        public bool WantWeapon(int displayId, float priority) { return Want(KindWeapon, displayId, priority); }

        /// <summary>As WantDisplay, for a gameobject display (GameObjectDisplayInfo id).</summary>
        public bool WantGameObject(int displayId, float priority) { return Want(KindGo, displayId, priority); }

        private bool Want(int kind, int id, float priority)
        {
            if (id <= 0 || _files == null) return true;
            long key = ((long)kind << 32) | (uint)id;
            lock (_lock)
            {
                int seq;
                if (_doneSeq.TryGetValue(key, out seq)) return _upSeq >= seq;
                Job j;
                if (_queued.TryGetValue(key, out j)) { if (priority < j.Priority) j.Priority = priority; return false; }
                j = new Job { Kind = kind, Id = id, Key = key, Priority = priority };
                _queued[key] = j;
                _queue.Add(j);
                Monitor.Pulse(_lock);
            }
            return false;
        }

        /// <summary>Queues a player character (look copied). Poll ReadyToBuild; the main thread then builds it.</summary>
        public WowCharJob WantCharacter(CharacterLook look, float priority)
        {
            if (look.ItemDisplayIds != null) look.ItemDisplayIds = (int[])look.ItemDisplayIds.Clone();
            var cj = new WowCharJob { Look = look, Priority = priority };
            if (_files == null) { cj.Done = true; cj.Error = "no game files"; return cj; }
            lock (_lock)
            {
                _queue.Add(new Job { Kind = KindChar, Char = cj, Priority = priority });
                Monitor.Pulse(_lock);
            }
            return cj;
        }

        /// <summary>Moves a queued character job up or down (distance squared).</summary>
        public void Reprioritise(WowCharJob cj, float priority)
        {
            if (cj == null || cj.Done) return;
            lock (_lock)
            {
                cj.Priority = priority;
                for (int i = 0; i < _queue.Count; i++) if (ReferenceEquals(_queue[i].Char, cj)) { _queue[i].Priority = priority; break; }
            }
        }

        /// <summary>The character job finished and every texture it prepared is uploaded.</summary>
        public bool ReadyToBuild(WowCharJob cj)
        {
            if (cj == null || !cj.Done) return false;
            lock (_lock) return _upSeq >= cj.TexSeq;
        }

        /// <summary>The gameobject display resolved by the worker, or null while not done.</summary>
        public WowGoInfo GoInfo(int displayId)
        {
            WowGoInfo g;
            lock (_lock) return _gos.TryGetValue(displayId, out g) ? g : null;
        }

        /// <summary>True when the worker found this creature display's model to be a player race model (Character\...); false when
        /// it is a creature (a shapeshift form, a polymorph) - known = false while not resolved yet.</summary>
        public bool IsCharacterDisplay(int displayId, out bool known)
        {
            bool c;
            lock (_lock) { known = _characterDisplays.TryGetValue(displayId, out c); }
            return known && c;
        }

        // ------------------------------------------------------------------ worker thread

        private void Work()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrepareUi(SelectCirclePath, false);
                PrepareUi(PlateBorderPath, false);
                PrepareUi(BarFillPath, false);
                PrepareUi(PlateGlowPath, true);
                PrepareUi(SparklePath, true);
                PrepareModel(MarkerAvailable, null, null);
                PrepareModel(MarkerIncomplete, null, null);
                PrepareModel(MarkerComplete, null, null);
            }
            catch (Exception e) { WowLog.Warn("Entities: UI art failed: " + e.Message); }
            lock (_lock) _uiSeq = _enqSeq;
            _uiDone = true;
            _workerMs = sw.ElapsedMilliseconds;

            while (!_cancel)
            {
                Job j = null;
                lock (_lock)
                {
                    if (_queue.Count == 0) { Monitor.Wait(_lock, 500); continue; }
                    int best = 0;
                    for (int i = 1; i < _queue.Count; i++) if (_queue[i].Priority < _queue[best].Priority) best = i;
                    j = _queue[best];
                    _queue[best] = _queue[_queue.Count - 1];
                    _queue.RemoveAt(_queue.Count - 1);
                }
                long t0 = sw.ElapsedMilliseconds;
                try { Run(j); }
                catch (Exception e)
                {
                    if (j.Char != null) j.Char.Error = e.Message;
                    Warn("Entities: art job " + j.Kind + "/" + j.Id + " threw: " + e.Message);
                }
                _workerMs += sw.ElapsedMilliseconds - t0;
                lock (_lock)
                {
                    if (j.Char != null) j.Char.TexSeq = _enqSeq;
                    else { _queued.Remove(j.Key); _doneSeq[j.Key] = _enqSeq; }
                }
                if (j.Char != null) j.Char.Done = true;
            }
        }

        private void Warn(string line)
        {
            if (Interlocked.Increment(ref _warnings) <= MaxWarnings) WowLog.Warn(line);
        }

        private void Run(Job j)
        {
            switch (j.Kind)
            {
                case KindDisplay:
                {
                    var cd = WowModelDb.ResolveCreature(_files, j.Id);
                    if (cd.Error != null) { Warn("Entities: display " + j.Id + ": " + cd.Error); return; }
                    bool character = cd.ModelPath != null && cd.ModelPath.StartsWith(@"Character\", StringComparison.OrdinalIgnoreCase);
                    lock (_lock) _characterDisplays[j.Id] = character;
                    _displays++;
                    // NPC gear fix (iteration 11): a humanoid NPC's helm / shoulder models and cape texture are read here too.
                    var gear = NpcGear.Resolve(_files, cd);
                    PrepareModel(cd.ModelPath, cd, gear != null ? gear.CapeTexture : null);
                    if (gear != null)
                    {
                        if (gear.HelmModel != null) PrepareModel(gear.HelmModel, null, gear.HelmTexture);
                        if (gear.LeftShoulderModel != null) PrepareModel(gear.LeftShoulderModel, null, gear.LeftShoulderTexture);
                        if (gear.RightShoulderModel != null) PrepareModel(gear.RightShoulderModel, null, gear.RightShoulderTexture);
                    }
                    return;
                }
                case KindWeapon:
                {
                    var w = Classify(j.Id);
                    lock (_lock) _weapons[j.Id] = w;
                    if (w.Error != null) { Warn("Entities: held item display " + j.Id + ": " + w.Error); return; }
                    _weaponCount++;
                    var item = WowModelDb.ResolveItem(_files, j.Id, w.Bow);
                    if (item.Error == null) PrepareModel(item.ModelPath, null, item.TexturePath);
                    return;
                }
                case KindGo:
                {
                    var g = ResolveGameObject(j.Id);
                    lock (_lock) _gos[j.Id] = g;
                    if (g.Error != null) { Warn("Entities: gameobject display " + j.Id + ": " + g.Error); return; }
                    _gameObjects++;
                    if (g.Wmo) WmoFactory.Prepare(_files, g.ModelPath);
                    else PrepareModel(g.ModelPath, null, null);
                    return;
                }
                case KindChar:
                    PrepareCharacter(j.Char);
                    return;
            }
        }

        /// <summary>GameObjectDisplayInfo.dbc row -> model path (.mdx names -> .m2, M2Model.NormalizeModelPath) and GeoBox.</summary>
        private WowGoInfo ResolveGameObject(int id)
        {
            var g = new WowGoInfo { DisplayId = id };
            var t = Dbc.Load(_files, "GameObjectDisplayInfo");
            if (t == null) { g.Error = "GameObjectDisplayInfo.dbc missing"; return g; }
            int r = t.FindRow(id);
            if (r < 0) { g.Error = "not in GameObjectDisplayInfo.dbc"; return g; }
            string name = t.GetString(r, DbcFields.GameObjectDisplayInfo.ModelName);
            if (string.IsNullOrEmpty(name)) { g.Error = "no model name"; return g; }
            g.MinX = t.GetFloat(r, DbcFields.GameObjectDisplayInfo.GeoBoxMin);
            g.MinY = t.GetFloat(r, DbcFields.GameObjectDisplayInfo.GeoBoxMin + 1);
            g.MinZ = t.GetFloat(r, DbcFields.GameObjectDisplayInfo.GeoBoxMin + 2);
            g.MaxX = t.GetFloat(r, DbcFields.GameObjectDisplayInfo.GeoBoxMax);
            g.MaxY = t.GetFloat(r, DbcFields.GameObjectDisplayInfo.GeoBoxMax + 1);
            g.MaxZ = t.GetFloat(r, DbcFields.GameObjectDisplayInfo.GeoBoxMax + 2);
            for (int i = 0; i < 10; i++) g.Sounds[i] = t.GetInt(r, DbcFields.GameObjectDisplayInfo.Sound + i);   // iteration 11 (trainer U11 a)
            name = name.Trim().Replace('/', '\\');
            if (name.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)) { g.Wmo = true; g.ModelPath = name; }
            else g.ModelPath = M2Model.NormalizeModelPath(name);
            if (!_files.Exists(g.ModelPath)) g.Error = g.ModelPath + " not in the game files";
            return g;
        }

        /// <summary>Composes the look (body texture blit, geosets) and parses the race model, decoding the textures its build uses.</summary>
        private void PrepareCharacter(WowCharJob cj)
        {
            var l = cj.Look;
            var res = CharacterComposer.Compose(_files, l.Race, l.Gender, l.Skin, l.Face, l.HairStyle, l.HairColor, l.FacialHair, l.ItemDisplayIds, true);
            if (string.IsNullOrEmpty(res.ModelPath)) { cj.Error = "race " + l.Race + " has no model in ChrRaces.dbc"; return; }
            M2Model m; M2Skin s; string err;
            if (!M2Factory.TryGetParsed(_files, res.ModelPath, out m, out s, out err)) { cj.Error = res.ModelPath + ": " + err; _failedModels++; return; }
            CharacterComposer.ApplyEyeGlow(res, m, s);
            cj.Res = res;
            cj.Model = m;
            cj.Name = "Character " + WowModelDb.RaceFileName(_files, l.Race) + (l.Gender == 1 ? "Female" : "Male");
            _characters++;
            _models++;
            foreach (var t in m.Textures)
            {
                if (_cancel) return;
                string p = t.Type == 0 ? t.FileName : t.Type == 2 ? res.CapeTexture : t.Type == 6 ? res.HairTexture : t.Type == 8 ? res.FurTexture : null;
                PrepareTexture(p, (int)t.Flags);
            }
        }

        private void PrepareUi(string path, bool additive)
        {
            if (!additive)
            {
                var o = WowTextureOptions.Ui;
                if (WowTextures.IsCached(path, o) || !_seen.Add(WowTextures.KeyOf(path, o))) return;
                var p = WowTextures.Prepare(_files, path, o);
                lock (_lock) { _prepared.Enqueue(p); _enqSeq++; }
                _textures++;
                return;
            }

            // Additive art (black background, no alpha): alpha = brightest channel, colour normalised - the same
            // conversion the Front's WowUi uses for its additive highlight, so it can be drawn with plain alpha blending.
            var pic = new Pic { Path = path };
            try
            {
                var bytes = _files.Read(path);
                if (bytes == null) pic.Error = "not in the game files";
                else
                {
                    var img = BlpImage.Decode(bytes);
                    var px = img.GetRgba32(0);
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        int m = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                        if (m == 0) { px[i + 3] = 0; continue; }
                        px[i] = (byte)(px[i] * 255 / m);
                        px[i + 1] = (byte)(px[i + 1] * 255 / m);
                        px[i + 2] = (byte)(px[i + 2] * 255 / m);
                        px[i + 3] = (byte)m;
                    }
                    pic.W = img.Width;
                    pic.H = img.Height;
                    pic.Rgba = px;
                }
            }
            catch (Exception e) { pic.Error = e.Message; }
            lock (_lock) _pics.Enqueue(pic);
            _textures++;
        }

        /// <summary>Parses the model (M2Factory's cache) and decodes the textures its build will ask for, with the same options.</summary>
        private void PrepareModel(string path, CreatureDisplay cd, string itemTexture)
        {
            M2Model m;
            M2Skin s;
            string err;
            if (!M2Factory.TryGetParsed(_files, path, out m, out s, out err)) { _failedModels++; return; }
            _models++;
            foreach (var t in m.Textures)
            {
                if (_cancel) return;
                string p = null;
                if (t.Type == 0) p = t.FileName;
                else if (cd != null && t.Type >= 11 && t.Type <= 13) p = cd.Skins[t.Type - 11];
                else if (cd != null && t.Type == 1 && !string.IsNullOrEmpty(cd.BakedTexture)) p = cd.BakedTexture;
                else if (itemTexture != null && t.Type == 2) p = itemTexture;
                PrepareTexture(p, (int)t.Flags);
            }
        }

        private void PrepareTexture(string p, int m2Flags)
        {
            if (string.IsNullOrEmpty(p)) return;
            var o = WowTextureOptions.ForM2Flags(m2Flags);
            if (WowTextures.IsCached(p, o) || !_seen.Add(WowTextures.KeyOf(p, o))) return;
            if (!_files.Exists(p)) return;
            var prep = WowTextures.Prepare(_files, p, o);
            lock (_lock) { _prepared.Enqueue(prep); _enqSeq++; }
            _textures++;
        }

        private WowWeaponInfo Classify(int id)
        {
            var w = new WowWeaponInfo { DisplayId = id };
            try
            {
                var r = WowModelDb.ResolveItem(_files, id, false);
                if (r.Error != null) { w.Error = r.Error; return w; }
                string name = r.ModelPath;
                int slash = name.LastIndexOf('\\');
                if (slash >= 0) name = name.Substring(slash + 1);
                w.ModelName = name;
                w.Shield = string.Equals(r.Folder, "Shield", StringComparison.OrdinalIgnoreCase);
                w.TwoHand = name.IndexOf("_2H_", StringComparison.OrdinalIgnoreCase) >= 0;
                w.Bow = name.StartsWith("Bow_", StringComparison.OrdinalIgnoreCase) && name.IndexOf("Crossbow", StringComparison.OrdinalIgnoreCase) < 0;
                ItemRowOf(id, w);
            }
            catch (Exception e) { w.Error = e.Message; }
            return w;
        }

        /// <summary>Item.dbc (8 fields, DbcFields.Item): the first row whose DisplayInfoID is this display gives SheatheType and
        /// InventoryType. The server sends item ENTRIES (PLAYER_VISIBLE_ITEM_n_ENTRYID); the World owner maps them to displays
        /// through the same Item.dbc, so the row found here is that item or one sharing its model. Any thread.</summary>
        private void ItemRowOf(int displayId, WowWeaponInfo w)
        {
            lock (_itemLock)
            {
                if (_itemRows == null)
                {
                    _itemRows = new Dictionary<int, int>();
                    _itemDbc = Dbc.Load(_files, "Item");
                    if (_itemDbc != null && _itemDbc.FieldCount == DbcFields.Item.FieldCount)
                        for (int r = 0; r < _itemDbc.RecordCount; r++)
                        {
                            int d = _itemDbc.GetInt(r, DbcFields.Item.DisplayInfoID);
                            if (d > 0 && !_itemRows.ContainsKey(d)) _itemRows[d] = r;
                        }
                }
                int row;
                if (_itemDbc == null || !_itemRows.TryGetValue(displayId, out row)) return;
                w.SheatheType = _itemDbc.GetInt(row, DbcFields.Item.SheatheType);
                w.InventoryType = _itemDbc.GetInt(row, DbcFields.Item.InventoryType);
            }
        }

        // ------------------------------------------------------------------ main thread

        /// <summary>The classification of a held item (worker result, else resolved now).</summary>
        public WowWeaponInfo Weapon(int displayId)
        {
            WowWeaponInfo w;
            lock (_lock) if (_weapons.TryGetValue(displayId, out w)) return w;
            w = Classify(displayId);
            lock (_lock) _weapons[displayId] = w;
            return w;
        }

        /// <summary>Uploads prepared textures for up to budgetMs. Main thread, once a frame.</summary>
        public void Pump(float budgetMs)
        {
            _pump.Reset();
            _pump.Start();
            while (_pump.Elapsed.TotalMilliseconds < budgetMs)
            {
                WowPreparedTexture p = null;
                Pic pic = null;
                lock (_lock)
                {
                    if (_pics.Count > 0) pic = _pics.Dequeue();
                    else if (_prepared.Count > 0) p = _prepared.Peek();
                }
                if (p == null && pic == null) break;
                if (p != null)
                {
                    var tex = WowTextures.Create(p);
                    lock (_lock) { _prepared.Dequeue(); _upSeq++; }
                    _uploaded++;
                    if (p.Error == null) AssignUi(p.Path, tex);
                    else if (IsUiPath(p.Path)) WowLog.Warn("Entities: " + p.Path + ": " + p.Error);
                }
                else
                {
                    if (pic.Error != null) { WowLog.Warn("Entities: " + pic.Path + ": " + pic.Error); continue; }
                    var tex = WowTextures.FromRgba32(pic.W, pic.H, pic.Rgba, WowTextureOptions.Ui, System.IO.Path.GetFileNameWithoutExtension(pic.Path) + " (alpha)", true);
                    if (tex != null) { _owned.Add(tex); _uploaded++; AssignUi(pic.Path, tex); }
                }
            }
            _pump.Stop();

            if (_uiDone && !_adopted)
            {
                // A second session: the UI textures are in WowTextures' cache already (the worker skipped them) - take them from there.
                _adopted = true;
                Texture2D t;
                if (SelectCircle == null && WowTextures.TryGetCached(SelectCirclePath, WowTextureOptions.Ui, out t)) SelectCircle = t;
                if (PlateBorder == null && WowTextures.TryGetCached(PlateBorderPath, WowTextureOptions.Ui, out t)) PlateBorder = t;
                if (BarFill == null && WowTextures.TryGetCached(BarFillPath, WowTextureOptions.Ui, out t)) BarFill = t;
            }

            if (!_reported && Ready)
            {
                _reported = true;
                WowLog.Info("Entities: art ready after " + _since.ElapsedMilliseconds + " ms (worker " + _workerMs + " ms): " + _displays + " displays, "
                    + _weaponCount + " held items, " + _models + " models parsed (" + _failedModels + " failed), " + _textures + " textures prepared, "
                    + _uploaded + " uploaded; circle " + Have(SelectCircle) + ", plate " + Have(PlateBorder) + "/" + Have(PlateGlow) + "/" + Have(BarFill)
                    + ", sparkle " + Have(Sparkle) + (_uiDone ? "" : " (worker still running: gave up waiting)") + "; the worker keeps taking new entities nearest-first");
            }
        }

        /// <summary>One line for the periodic log.</summary>
        public string StatsLine()
        {
            int pending, queuedTex;
            lock (_lock) { pending = _queue.Count; queuedTex = _prepared.Count; }
            return "art worker " + _workerMs + " ms: " + _displays + " displays, " + _characters + " characters, " + _gameObjects + " gameobject displays, "
                + _weaponCount + " held items, " + _models + " models (" + _failedModels + " failed), " + _textures + " textures (" + _uploaded + " uploaded), "
                + pending + " jobs + " + queuedTex + " textures waiting";
        }

        private static string Have(Texture t) { return t != null ? "ok" : "missing"; }

        private static bool IsUiPath(string path)
        {
            return string.Equals(path, SelectCirclePath, StringComparison.OrdinalIgnoreCase) || string.Equals(path, PlateBorderPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, BarFillPath, StringComparison.OrdinalIgnoreCase);
        }

        private void AssignUi(string path, Texture2D tex)
        {
            if (string.Equals(path, SelectCirclePath, StringComparison.OrdinalIgnoreCase)) SelectCircle = tex;
            else if (string.Equals(path, PlateBorderPath, StringComparison.OrdinalIgnoreCase)) PlateBorder = tex;
            else if (string.Equals(path, BarFillPath, StringComparison.OrdinalIgnoreCase)) BarFill = tex;
            else if (string.Equals(path, PlateGlowPath, StringComparison.OrdinalIgnoreCase)) PlateGlow = tex;
            else if (string.Equals(path, SparklePath, StringComparison.OrdinalIgnoreCase)) Sparkle = tex;
        }

        private Material _textMaterial;

        /// <summary>
        /// One shared outlined material for every name and combat number (WoW draws unit names and damage with a dark outline):
        /// a copy of the font's own material with TMP's outline on, so the texts do not each instantiate a material.
        /// </summary>
        public Material TextMaterial
        {
            get
            {
                if (_textMaterial != null) return _textMaterial;
                var font = WowUi.Font;
                if (font == null || font.material == null) return null;
                try
                {
                    _textMaterial = new Material(font.material);
                    _textMaterial.name = "WoW entity text (outline)";
                    _textMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                    _textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
                    _textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 1f));
                }
                catch (Exception e)
                {
                    WowLog.Warn("Entities: outline text material failed (" + e.Message + "), plain text");
                    _textMaterial = null;
                }
                return _textMaterial;
            }
        }
    }
}
