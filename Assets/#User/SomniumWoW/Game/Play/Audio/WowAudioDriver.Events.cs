using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// The sounds the driver plays by itself from the game: weapon swings/impacts (WowPlay.Event melee events of the player and on
    /// the player), quest / level / loot / money / death sounds, race error voice lines (VocalUISounds) and NPC greetings /
    /// farewells / "pissed" lines (NPCSounds) when Session.CurrentDialog opens and closes. Plus the WowUiSound table.
    /// </summary>
    public sealed partial class WowAudioDriver
    {
        // Interface sounds by the names the client's own FrameXML / engine use (SoundEntries.Name; "file:" = a file without an
        // entry). Order = WowUiSound.
        private static readonly string[] UiNames =
        {
            "igMainMenuOption",        // ButtonClick
            "igMainMenuOpen",          // WindowOpen
            "igMainMenuClose",         // WindowClose
            "igSpellBookOpen",         // SpellbookOpen   (SpellBookFrame.lua)
            "igSpellBookClose",        // SpellbookClose
            "igCharacterInfoOpen",     // CharacterOpen   (CharacterFrame.lua)
            "igCharacterInfoClose",    // CharacterClose
            "igBackPackOpen",          // BagOpen         (ContainerFrame.lua)
            "igBackPackClose",         // BagClose
            "igQuestLogOpen",          // QuestLogOpen    (QuestLogFrame.lua)
            "igQuestLogClose",         // QuestLogClose
            "QUESTADDED",              // QuestAccepted   (iQuestActivate.wav)
            "igQuestListComplete",     // QuestCompleted  (iQuestComplete.wav, turn-in)
            "igQuestLogAbandonQuest",  // QuestAbandoned  (StaticPopup.lua)
            @"file:Sound\Interface\iQuestUpdate.wav",   // QuestObjective
            "LEVELUPSOUND",            // LevelUp         (Sound\Spells\LevelUp.wav)
            "LOOTWINDOWCOINSOUND",     // LootCoin
            "PickUpBag",               // LootItem (items without an ItemGroupSounds pickup sound)
            @"file:Sound\Interface\Error.wav",          // Error
            "igCreatureAggroSelect",   // TargetSelect    (TargetFrame.lua: hostile; friendly igCharacterNPCSelect, neutral igCreatureNeutralSelect)
            "INTERFACESOUND_LOSTTARGETUNIT", // TargetDeselect (TargetFrame.lua)
            "igAbilityIconDrop",       // ActionBarDrop
            null                       // Death: the character's own death sound (race / VoiceGender)
        };

        private int[] _uiIds;
        private string[] _uiFiles;
        private int _selectFriendly, _selectNeutral;
        private readonly HashSet<string> _missingUi = new HashSet<string>();

        // dialog state
        private ulong _dialogGuid;
        private int _dialogDisplay;
        private Vector3 _dialogPos;
        // Integrator (iteration 4): the NPC's WoW position, so the farewell is placed in the CURRENT origin frame (a rebase may
        // happen while the dialog is open; a cached Unity position would be ~1 km off then).
        private int _dialogMap;
        private float _dialogX, _dialogY, _dialogZ;
        private ulong _greetGuid;
        private int _greetCount;
        private float _greetWindowStart;

        // error voice lines
        private float _nextVoice;
        private readonly Dictionary<int, float> _voiceNext = new Dictionary<int, float>();
        private int _voiceLines;

        public static IReadOnlyList<string> UiSoundNames { get { return UiNames; } }

        private void OnDataReady()
        {
            _uiIds = new int[UiNames.Length];
            _uiFiles = new string[UiNames.Length];
            int ok = 0;
            var missing = new List<string>();
            for (int i = 0; i < UiNames.Length; i++)
            {
                string n = UiNames[i];
                if (n == null) continue;
                if (n.StartsWith("file:", StringComparison.Ordinal))
                {
                    string f = n.Substring(5);
                    if (_files != null && _files.Exists(f)) { _uiFiles[i] = f; ok++; _clips.Prefetch(f); }
                    else missing.Add(f);
                    continue;
                }
                int id = _data.IdByName(n);
                if (id > 0) { _uiIds[i] = id; ok++; PrefetchEntry(id); }
                else missing.Add(n);
            }
            _selectFriendly = _data.IdByName("igCharacterNPCSelect");
            _selectNeutral = _data.IdByName("igCreatureNeutralSelect");
            WowLog.Info("Audio: interface sounds " + ok + " of " + (UiNames.Length - 1) + " found" + (missing.Count > 0 ? " - missing: " + string.Join(", ", missing.ToArray()) : ""));
            if (WowPlay.Session != null) OnSessionStarted(WowPlay.Session);
        }

        internal void PlayUi(WowUiSound sound)
        {
            if (_data == null || _uiIds == null) { LogEarly(); return; }
            int i = (int)sound;
            if (i < 0 || i >= _uiIds.Length) return;
            if (sound == WowUiSound.Death) { PlayDeath(); return; }
            if (sound == WowUiSound.TargetSelect)
            {
                var s = WowPlay.Session;
                var t = s != null && s.TargetGuid != 0 ? s.Find(s.TargetGuid) : null;
                if (t != null && t.Reaction == Reaction.Friendly && _selectFriendly > 0) { PlayEntry(_selectFriendly, SoundCategory.Ui, Vector3.zero, null, true, 1f); return; }
                if (t != null && t.Reaction == Reaction.Neutral && _selectNeutral > 0) { PlayEntry(_selectNeutral, SoundCategory.Ui, Vector3.zero, null, true, 1f); return; }
            }
            if (_uiIds[i] > 0) PlayEntry(_uiIds[i], SoundCategory.Ui, Vector3.zero, null, true, 1f);
            else if (_uiFiles[i] != null) PlayPath(_uiFiles[i], SoundCategory.Ui, 0.75f, true, Vector3.zero, null, 8f, 45f, 1f);
        }

        internal void PlayNamedUi(string name)
        {
            if (_data == null) { LogEarly(); return; }
            if (string.IsNullOrEmpty(name)) return;
            int id = _data.IdByName(name);
            if (id > 0) { PlayEntry(id, SoundCategory.Ui, Vector3.zero, null, true, 1f); return; }
            if (_missingUi.Add(name) && _missingUi.Count <= 20) WowLog.Warn("Audio: no sound entry named '" + name + "' - logged once");
        }

        // ------------------------------------------------------------------------------------------ session

        private void OnSessionStarted(IWowSession s)
        {
            _dialogGuid = 0;
            _dialogDisplay = 0;
            _greetGuid = 0;
            _voiceNext.Clear();
            if (_data == null || s == null || s.Player == null) return;
            // the player's own step and swing sounds, so the first ones are not late
            int race = s.Player.Race, gender = WowSettings.VoiceGender == 1 ? 1 : 0;
            int foot = _data.RaceFootstepId(race, gender);
            for (int t = 0; t <= WowSoundData.TerrainWater; t++) PrefetchEntry(_data.FootstepSound(foot, t, false));
            PrefetchEntry(_data.FootstepSound(foot, WowSoundData.TerrainDirt, true));
            for (int size = 0; size < 3; size++) { PrefetchEntry(_data.SwingSound(size, false)); PrefetchEntry(_data.SwingSound(size, true)); }
        }

        private void OnSessionEnded()
        {
            _dialogGuid = 0;
            _dialogDisplay = 0;
            StopAllLoops();   // iteration 11 (audio fix): no cast, aura or area a loop sounded for outlives the session
        }

        /// <summary>Per frame while a session runs: NPC greeting on dialog open, farewell on close.</summary>
        private void TickSession(IWowSession s)
        {
            var dlg = s.CurrentDialog;
            ulong g = dlg != null ? dlg.NpcGuid : 0;
            if (g == _dialogGuid) return;
            if (_dialogGuid != 0 && _dialogDisplay > 0)
                PlayEntry(_data.NpcSound(_dialogDisplay, 1), SoundCategory.Voice, WowSpace.ToUnity(_dialogMap, _dialogX, _dialogY, _dialogZ) + Vector3.up * 1.5f, null, false, 1f);
            _dialogGuid = g;
            _dialogDisplay = 0;
            if (g == 0) return;
            var npc = s.Find(g);
            if (npc == null) return;
            _dialogDisplay = npc.DisplayId;
            _dialogMap = s.MapId; _dialogX = npc.Pos.X; _dialogY = npc.Pos.Y; _dialogZ = npc.Pos.Z;
            _dialogPos = WowSpace.ToUnity(s.MapId, npc.Pos.X, npc.Pos.Y, npc.Pos.Z) + Vector3.up * 1.5f;
            // the client turns to the "pissed" line when the same NPC is clicked again and again
            float now = Time.unscaledTime;
            if (g != _greetGuid || now - _greetWindowStart > 12f) { _greetGuid = g; _greetCount = 0; _greetWindowStart = now; }
            _greetCount++;
            int which = _greetCount >= 5 ? 2 : 0;
            int id = _data.NpcSound(npc.DisplayId, which);
            if (id <= 0 && which == 2) id = _data.NpcSound(npc.DisplayId, 0);
            PlayEntry(id, SoundCategory.Voice, _dialogPos, null, false, 1f);
        }

        // ------------------------------------------------------------------------------------------ game events

        private void OnGameEvent(GameEvent e)
        {
            if (_data == null) return;
            var s = WowPlay.Session;
            if (s == null || s.Player == null) return;
            try
            {
                switch (e.Type)
                {
                    case GameEventType.MeleeHit:
                    case GameEventType.MeleeMiss:
                    case GameEventType.MeleeDodge:
                    case GameEventType.MeleeParry:
                    case GameEventType.MeleeBlock:
                        Weapon(s, e);
                        break;
                    case GameEventType.QuestAccepted: PlayUi(WowUiSound.QuestAccepted); break;
                    case GameEventType.QuestProgress:
                    case GameEventType.QuestCompleted: PlayUi(WowUiSound.QuestObjective); break;
                    case GameEventType.QuestRewarded: PlayUi(WowUiSound.QuestCompleted); break;
                    case GameEventType.QuestAbandoned: PlayUi(WowUiSound.QuestAbandoned); break;
                    case GameEventType.LevelUp: PlayUi(WowUiSound.LevelUp); break;
                    case GameEventType.MoneyReceived: PlayUi(WowUiSound.LootCoin); break;
                    case GameEventType.LootReceived:
                        {
                            int id = e.ItemEntry > 0 ? _data.ItemPickupSound(e.ItemEntry) : 0;
                            if (id > 0) PlayEntry(id, SoundCategory.Ui, Vector3.zero, null, true, 1f);
                            else PlayUi(WowUiSound.LootItem);
                            break;
                        }
                    case GameEventType.PlayerDied: PlayDeath(); break;
                    case GameEventType.Error: ErrorVoice(s, e.Text); break;
                }
            }
            catch (Exception ex) { LogThrow("event " + e.Type, ex); }
        }

        private void PlayDeath()
        {
            var s = WowPlay.Session;
            if (s == null || s.Player == null || _data == null) return;
            int id = _data.RaceDeathSound(s.Player.Race, WowSettings.VoiceGender == 1 ? 1 : 0);
            PlayEntry(id, SoundCategory.Voice, Vector3.zero, null, true, 1f);
        }

        /// <summary>The race's voice line for a red error text ("Not enough rage", "Out of range"...), rate-limited like speech.</summary>
        private void ErrorVoice(IWowSession s, string text)
        {
            int en = WowSoundData.VocalEnumForError(text);
            if (en < 0) return;
            float now = Time.unscaledTime;
            float next;
            if (now < _nextVoice || (_voiceNext.TryGetValue(en, out next) && now < next)) return;
            int gender = WowSettings.VoiceGender == 1 ? 1 : 0;
            int id = _data.VocalSound(s.Player.Race, en, gender);
            if (id <= 0) return;
            _nextVoice = now + 1.5f;
            _voiceNext[en] = now + 4f;
            if (_voiceLines++ < 5) WowLog.Info("Audio: error voice '" + text + "' -> VocalUI enum " + en + ", race " + s.Player.Race + (gender == 1 ? " female" : " male") + ", sound " + id);
            PlayEntry(id, SoundCategory.Voice, Vector3.zero, null, true, 1f);
        }

        /// <summary>
        /// Melee sounds of the player's swings and of creatures' swings at the player: the swing (WeaponSwingSounds2 by the weapon's
        /// ItemSubClass.WeaponSwingSize and crit) and on a hit the impact (WeaponImpactSounds by weapon subclass + material, the
        /// target's CreatureImpactType slot, crit); parry = the parry slot, block = the shield slot. Creatures without a weapon keep
        /// only their own attack sounds (Entities) plus the unarmed impact on the player.
        /// </summary>
        private void Weapon(IWowSession s, GameEvent e)
        {
            ulong me = s.Player.Guid;
            bool mine = e.SourceGuid == me, onMe = e.TargetGuid == me;
            if (!mine && !onMe) return;
            if (e.SpellId == 75) return;   // Auto Shot: the session reports ranged shots as Melee* with spell 75 - no swing / melee impact
            int map = s.MapId;
            Transform root = LocalRoot();
            Vector3 playerPos = root != null ? root.position : WowSpace.ToUnity(map, s.Player.Pos.X, s.Player.Pos.Y, s.Player.Pos.Z);
            int subclass;
            bool metal, armed;
            Vector3 swingAt, hitAt;
            int targetSlot;
            if (mine)
            {
                WeaponOf(s.Player.Equipment[(int)EquipSlot.MainHand], out subclass, out metal);
                armed = true;   // fists whoosh too
                swingAt = playerPos + Vector3.up * 1.2f;
                var t = s.Find(e.TargetGuid);
                if (t == null) return;
                hitAt = WowSpace.ToUnity(map, t.Pos.X, t.Pos.Y, t.Pos.Z) + Vector3.up * 1.1f * Mathf.Max(0.5f, t.Scale);
                targetSlot = _data.CreatureImpactSlot(t.DisplayId);
            }
            else
            {
                var c = s.Find(e.SourceGuid);
                if (c == null) return;
                subclass = c.MainHandDisplayId > 0 ? _data.SubclassOfItemDisplay(c.MainHandDisplayId) : -1;
                armed = subclass >= 0;
                if (subclass < 0) subclass = WowSoundData.SubclassUnarmed;
                metal = subclass != 10 && subclass != WowSoundData.SubclassUnarmed && subclass != 2 && subclass != 18;   // staves, fists, bows, crossbows: wood
                swingAt = WowSpace.ToUnity(map, c.Pos.X, c.Pos.Y, c.Pos.Z) + Vector3.up * 1.2f;
                hitAt = playerPos + Vector3.up * 1.1f;
                targetSlot = WowSoundData.SlotFlesh;
            }
            if (armed) PlayEntry(_data.SwingSound(_data.SwingSize(subclass), e.Crit), SoundCategory.Effects, swingAt, null, false, 0.8f);

            int slot = -1;
            if (e.Type == GameEventType.MeleeHit) slot = targetSlot;
            else if (e.Type == GameEventType.MeleeParry) slot = DefenderMetal(s, onMe) ? WowSoundData.SlotParryMetal : WowSoundData.SlotParryWood;
            else if (e.Type == GameEventType.MeleeBlock) slot = DefenderMetal(s, onMe) ? WowSoundData.SlotShieldMetal : WowSoundData.SlotShieldWood;
            if (slot < 0) return;
            int impact = _data.ImpactSound(subclass, metal, slot, e.Crit && e.Type == GameEventType.MeleeHit);
            if (impact <= 0 && slot != WowSoundData.SlotFlesh) impact = _data.ImpactSound(subclass, metal, WowSoundData.SlotFlesh, false);
            PlayEntry(impact, SoundCategory.Effects, hitAt, null, false, armed ? 1f : 0.8f);
        }

        /// <summary>The weapon subclass and material (Item.dbc) of an equipped item; unarmed when none.</summary>
        private void WeaponOf(ItemView item, out int subclass, out bool metal)
        {
            int cls, disp;
            subclass = WowSoundData.SubclassUnarmed;
            metal = false;
            if (item == null || !_data.TryItem(item.Entry, out cls, out subclass, out metal, out disp) || cls != 2)
            {
                subclass = WowSoundData.SubclassUnarmed;
                metal = false;
            }
        }

        /// <summary>Whether the defender's parrying weapon / blocking shield is metal (the player's from Item.dbc; creatures: metal).</summary>
        private bool DefenderMetal(IWowSession s, bool playerDefends)
        {
            if (!playerDefends) return true;
            var off = s.Player.Equipment[(int)EquipSlot.OffHand];
            var main = s.Player.Equipment[(int)EquipSlot.MainHand];
            int cls, sub, disp;
            bool metal;
            var item = off ?? main;
            if (item == null || !_data.TryItem(item.Entry, out cls, out sub, out metal, out disp)) return true;
            return metal;
        }
    }
}
