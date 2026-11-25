using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Game;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// One hidden object that drives every M2 effect instance (M2Effects): distance culling (every 0.25 s, staggered),
    /// animated emitter / ribbon tracks (every 0.1 s), material animation (every frame), WowSettings changes
    /// (EffectsQuality, WorldParticles). Created on the first registration; no per-frame allocations.
    /// </summary>
    public sealed class M2EffectsRunner : MonoBehaviour
    {
        static M2EffectsRunner _instance;
        static readonly List<M2FxInstance> _items = new List<M2FxInstance>();
        static readonly List<M2FxSharedMaterial> _shared = new List<M2FxSharedMaterial>();
        static bool _settingsDirty;
        static float _logAt = -1f;
        static int _lastLogged = -1;
        static bool _vfxEnsured;

        public static int Count { get { return _items.Count; } }

        internal static void Register(M2FxInstance inst)
        {
            Ensure();
            inst.NextCull = 0f;
            _items.Add(inst);
            _logAt = Time.realtimeSinceStartup + 5f;
        }

        internal static void RegisterMaterial(M2FxSharedMaterial sm)
        {
            Ensure();
            _shared.Add(sm);
        }

        static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("WoW M2 Effects");
            go.hideFlags = HideFlags.DontSave;
            _instance = go.AddComponent<M2EffectsRunner>();
        }

        void OnEnable() { WowSettings.Changed += OnSettings; WowSpace.Rebased += OnRebased; }
        void OnDisable() { WowSettings.Changed -= OnSettings; WowSpace.Rebased -= OnRebased; }

        /// <summary>
        /// Floating origin (WowSpace.Rebased): the models were moved by WowOrigin; their world-simulated particles and ribbon
        /// trails are not moved by a transform shift, so they are shifted here.
        /// </summary>
        static void OnRebased(Vector3 d)
        {
            int n = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Root == null) continue;
                for (int k = 0; k < it.Emitters.Length; k++)
                    if (it.Emitters[k].Ps != null) { WowOrigin.ShiftParticles(it.Emitters[k].Ps, d); n++; }
                for (int k = 0; k < it.Ribbons.Length; k++)
                    if (it.Ribbons[k].Trail != null) { WowOrigin.ShiftTrail(it.Ribbons[k].Trail, d); n++; }
            }
            if (n > 0) WowLog.Info("M2 effects: " + n + " emitters/ribbons shifted with the origin");
        }
        static void OnSettings() { _settingsDirty = true; }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            float now = Time.time;
            if (!_vfxEnsured && WowPlay.Session != null) { _vfxEnsured = true; WowSpellVfx.Ensure(); }   // spell visuals run whenever a session runs
            long gms = (long)(now * 1000f);

            for (int i = _shared.Count - 1; i >= 0; i--)
            {
                var sm = _shared[i];
                if (sm.Mat == null) { _shared[i] = _shared[_shared.Count - 1]; _shared.RemoveAt(_shared.Count - 1); continue; }
                M2Effects.ApplyShared(sm, gms);
            }

            bool dirty = _settingsDirty;
            _settingsDirty = false;
            var cam = M2Animator.FindCamera();
            bool haveCam = cam != null;
            Vector3 cp = haveCam ? cam.transform.position : Vector3.zero;
            float worldD = M2Effects.WorldDistance, animD = worldD * 1.5f;
            bool worldOn = WowSettings.WorldParticles;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it.Root == null) { _items[i] = _items[_items.Count - 1]; _items.RemoveAt(_items.Count - 1); continue; }
                if (dirty)
                {
                    it.NextCull = 0f;
                    for (int k = 0; k < it.Emitters.Length; k++) M2Effects.ApplyCap(it.Emitters[k]);
                }
                bool hasParticles = it.Emitters.Length > 0 || it.Ribbons.Length > 0;
                if (now >= it.NextCull)
                {
                    it.NextCull = now + 0.2f + (i % 7) * 0.01f;
                    bool want = true;
                    if (it.World && !worldOn) want = false;
                    else if (haveCam && (it.Root.position - cp).sqrMagnitude > (it.World ? worldD * worldD : animD * animD)) want = false;
                    if (want != it.Active) SetActive(it, want);
                }

                int seq, t; long g;
                if (it.Anim != null) { seq = it.Anim.DataSequence; t = it.Anim.TimeMs; g = it.Anim.GlobalMs; }
                else { seq = it.RestSeq; t = (int)(gms % (it.RestDuration > 0 ? it.RestDuration : 1000)); g = gms; }

                if (it.Materials != null && it.Renderer != null) M2Effects.ApplyMaterials(it, seq, t, g);
                if (!it.Active || !hasParticles) continue;
                if (now >= it.NextTracks || seq != it.LastSeq)
                {
                    it.NextTracks = now + 0.1f;
                    it.LastSeq = seq;
                    for (int k = 0; k < it.Emitters.Length; k++)
                    {
                        var fe = it.Emitters[k];
                        if (fe.Animated) M2Effects.ApplyTracks(fe, it.Model, seq, t, g);
                    }
                    for (int k = 0; k < it.Ribbons.Length; k++)
                    {
                        var fr = it.Ribbons[k];
                        if (fr.Animated) M2Effects.ApplyRibbon(fr, it.Model, seq, t, g);
                    }
                }
            }

            if (_logAt > 0f && Time.realtimeSinceStartup >= _logAt)
            {
                _logAt = -1f;
                if (_items.Count != _lastLogged) { _lastLogged = _items.Count; WowLog.Info(M2Effects.StatsLine() + ", " + _items.Count + " live"); }
            }
        }

        static void SetActive(M2FxInstance it, bool on)
        {
            it.Active = on;
            if (on) ClearTrails(it);
            if (it.Container != null) { it.Container.SetActive(on); return; }
            for (int k = 0; k < it.Emitters.Length; k++) if (it.Emitters[k].Go != null) it.Emitters[k].Go.SetActive(on);
            for (int k = 0; k < it.Ribbons.Length; k++) if (it.Ribbons[k].Go != null) it.Ribbons[k].Go.SetActive(on);
        }

        static void ClearTrails(M2FxInstance it)
        {
            for (int k = 0; k < it.Ribbons.Length; k++) if (it.Ribbons[k].Trail != null) it.Ribbons[k].Trail.Clear();
        }
    }
}
