// iteration 11 - duelsnd (the user: "missing sounds, like starting a duel"): a spawned duel flag plays its Spawn animation and the
// flag's own sound. Facts (harness P/harness/Probes/DuelSnd, the user's install):
// - AzerothCore sends GAMEOBJECT_TYPE_DUEL_ARBITER (16) with UPDATETYPE_CREATE_OBJECT2 (Object.cpp:209-218) -> EntityState.Spawned;
// - the Duel Flag (spell 7266's GO) is GameObjectDisplayInfo 787 "World\Generic\PassiveDoodads\DuelingFlag\DuelingFlag.mdx":
//   Sound[6] = SoundEntries 3486 "DuelingFlagSpawn" (DuelingFlagSpawn.wav), Sound[7] = 3487 "DuelingFlagDeSpawn" (DsGoSounds);
// - DuelingFlag.m2 has 3 sequences: 145 "Spawn" (3333 ms), 0 "Stand", 157 "Despawn", and ONE event '$GC0' at 667 ms of Spawn (DsM2);
// - the fishing bobber shows the pairing '$GCn' -> Sound[6 + n]: G_FishingBobber.m2 '$GC0' at 200 ms of 153 "Custom0", display 668
//   Sound[6] = 3355 "Fishing Hooked" (the trainer owner's TrGoSound already plays Sound[6 + n] for custom animation n).
// So: Spawn once, then Stand; Sound[6 + n] at each '$GCn' time of the Spawn sequence. The Despawn sequence has no event and
// Sound[7] is never named by one - not played (no source tells when the client plays it). Ours: only duel flags (type 16) for now.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    internal sealed partial class WowEntityView
    {
        private const int AnimSpawn = 145;            // AnimationData 145 "Spawn" (harness DsIds)
        private const int GoTypeDuelArbiter = 16;     // GAMEOBJECT_TYPE_DUEL_ARBITER (AC SharedDefines.h:1580)

        // '$GC0'..'$GC3' times (ms) in each model's Spawn sequence, read once per model (null entry = none)
        private static readonly Dictionary<string, int[][]> SpawnEvents = new Dictionary<string, int[][]>(StringComparer.OrdinalIgnoreCase);
        private static int _spawnLogs;

        private string _goModelPath;
        private bool _spawnChecked;
        private float _spawnStart = -1f, _spawnEnd = -1f;
        private int[][] _spawnTimes;
        private bool[] _spawnPlayed;

        private void SpawnAnimate(EntityState s, float now)
        {
            if (!_spawnChecked)
            {
                _spawnChecked = true;
                if (!s.Spawned || s.GoType != GoTypeDuelArbiter || Anim == null || !Anim.Has(AnimSpawn)) return;
                Anim.HoldAtEnd = false;
                Anim.Speed = 1f;
                Anim.Play(AnimSpawn, false);
                _spawnStart = now;
                _spawnEnd = now + DurationOf(AnimSpawn);
                _spawnTimes = SpawnEventTimes(_goModelPath, Anim.Model);
                _spawnPlayed = new bool[4];
                if (_spawnLogs++ < 5)
                    WowLog.Info("Entities: " + (s.Name ?? "duel flag") + " spawned (CREATE_OBJECT2) - Spawn animation, GameObjectDisplayInfo " + s.DisplayId
                                + " Sound[6 + n] at its $GCn events (" + Describe(_spawnTimes) + ")");
            }
            if (_spawnStart < 0f) return;
            float ms = (now - _spawnStart) * 1000f;
            for (int n = 0; n < 4 && _spawnTimes != null; n++)
            {
                int[] t = _spawnTimes[n];
                if (t == null || t.Length == 0 || _spawnPlayed[n] || ms < t[0]) continue;
                _spawnPlayed[n] = true;
                TrGoSound(s.DisplayId, 6 + n);
            }
            if (now >= _spawnEnd)
            {
                _spawnStart = -1f;
                if (Anim.Has(AnimStand)) Anim.Play(AnimStand, true);
            }
        }

        private static int[][] SpawnEventTimes(string path, M2Model model)
        {
            if (string.IsNullOrEmpty(path) || model == null) return null;
            int[][] times;
            if (SpawnEvents.TryGetValue(path, out times)) return times;
            times = null;
            try
            {
                int seq = model.FindSequence(AnimSpawn);
                WowFiles files = WowPlay.Files;
                byte[] b = files != null && seq >= 0 ? files.Read(path) : null;
                if (b != null)
                {
                    M2Events ev = M2Events.Read(b, path, files.Read);
                    times = new int[4][];
                    for (int n = 0; n < 4; n++) times[n] = ev.Times("$GC" + n, seq);
                }
            }
            catch (Exception e) { WowLog.Warn("Entities: the events of " + path + " could not be read: " + e.Message); }
            SpawnEvents[path] = times;
            return times;
        }

        private static string Describe(int[][] times)
        {
            if (times == null) return "no events read";
            var parts = new List<string>();
            for (int n = 0; n < times.Length; n++) if (times[n] != null && times[n].Length > 0) parts.Add("$GC" + n + " @" + times[n][0] + " ms");
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no $GC events";
        }
    }
}
