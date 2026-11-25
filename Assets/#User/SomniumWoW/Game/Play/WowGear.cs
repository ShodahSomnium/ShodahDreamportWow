using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The character's weapons on Somnium avatars (owner: Gear). The front end adds this component to the WoW Game object.
    /// - The local player (GearLocal): the weapons of Session.Player.Equipment (main hand, off hand, ranged) as their WoW
    ///   models, sheathed where WoW hangs them (Item.dbc SheatheType, GearPlace) on the avatar's body (GearBody); VR: grab
    ///   from the sheath with the grip (sticky), put back the same way, a swing that hits a creature starts auto-attack,
    ///   haptics; desktop: the Z key draws/sheathes, and the avatar itself swings/stabs/punches/shoots/casts with the character
    ///   (GearAnimLocal -> GearPoser, posed in LateUpdate after the client animated it; WowSettings.AvatarCombatAnimations).
    /// - Everyone else (GearRemote): their weapons, sheathed or in the hand their record says, and "Name - Level N Class"
    ///   over their head, from their slot in the world's Fusion session (WowNet, "Wow Net Player 0..49" scene objects);
    ///   desktop players' avatars play the motions their record names (VR avatars are left to their own IK).
    /// Order: Update = the local player's input, then the network (read everyone, write our record), then the remote
    /// records; LateUpdate at order 10000 (after the client's VRIK, 9998 per the ETS nameplate note) places every weapon;
    /// Application.onBeforeRender re-places the weapons held in VR from this frame's controller poses. Iteration 4: remote
    /// avatars re-mapped by the floating-origin difference (GearShift 30000 / GearShiftEarly -30000, see GearRemap),
    /// WowParty.Members refilled every LateUpdate, HandNearSheath for the VR radial menu.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class WowGear : MonoBehaviour
    {
        /// <summary>
        /// CONTRACT (iteration 4, lead stub - the Gear owner implements it): true while that hand is close enough to a sheath (or holds
        /// a weapon there) that a grip press belongs to the weapon grab. The VR radial menu opens only when this is false.
        /// </summary>
        /// <summary>Lead 2026-10-10 (immersive bow): the hand holds the bow or an arrow (the strike ring leaves it alone).</summary>
        public static bool HandOnBow(bool rightHand)
        {
            WowGear g = _current;
            if (g == null) return false;
            try { return g._local.BowBusy(rightHand, Vector3.zero, false); }
            catch (Exception) { return false; }
        }

        public static bool HandNearSheath(bool rightHand)
        {
            WowGear g = _current;
            if (g == null) return false;
            if (_nearFrame != Time.frameCount)
            {
                _nearFrame = Time.frameCount;
                try { _nearRight = g._local.NearSheath(true); _nearLeft = g._local.NearSheath(false); }
                catch (Exception e) { _nearRight = _nearLeft = false; g.Fail("HandNearSheath", e); }
            }
            return rightHand ? _nearRight : _nearLeft;
        }

        /// <summary>
        /// For WowOrigin (optional, PLAN4 §2): the origin cell of the nearest drawn remote player on our map within maxMetres
        /// (after re-mapping). Rebasing onto it instead of our own grid cell makes the two frames equal - no render-time offset,
        /// and the client's own avatar culling (AvatarsCullingSystem: full avatar within 15 m, by UNshifted positions) sees the
        /// true distance. False when there is none.
        /// </summary>
        public static bool NearestRemoteOrigin(float maxMetres, out int cellX, out int cellY, out int cellZ)
        {
            cellX = cellY = cellZ = 0;
            WowGear g = _current;
            if (g == null) return false;
            float best = maxMetres;
            bool found = false;
            foreach (GearRemote r in g._remotes.Values)
            {
                WowNet.Remote rec = r.Record;
                if (rec == null || r.Seen != GearSeen.Shown || r.Distance > best) continue;
                WowGearState s = rec.Gear;
                if (!s.OriginSaid || s.OriginMap < 0 || s.OriginMap != WowSpace.OriginMap) continue;
                best = r.Distance;
                cellX = s.CellX; cellY = s.CellY; cellZ = s.CellZ;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Lead fix (2026-10-03, WowOrigin.ShareRange): the origin cell to share - of the Somnium players on our map within maxMetres (true
        /// distance, after re-mapping), the one with the LOWEST Fusion player id, when it is lower than ours (the lowest keeps its own
        /// origin; everyone near it adopts it, so a group converges on one frame). False when we lead or nobody is near.
        /// </summary>
        /// <summary>Lead (2026-10-04, the Armour page's fitting view): the local avatar's Animator (the copy we follow) and the
        /// immersive armour renderers shown on it now (appended). False when there is no local avatar body.</summary>
        public static bool LocalFitting(List<Renderer> armour, out Animator avatar)
        {
            avatar = null;
            WowGear g = _current;
            if (g == null) return false;
            avatar = g._local.Body.Animator;
            if (armour != null) g._local.CollectArmourRenderers(armour);
            return avatar != null;
        }

        public static bool LeaderOrigin(float maxMetres, out int cellX, out int cellY, out int cellZ, out int leaderId)
        {
            cellX = cellY = cellZ = 0;
            leaderId = -1;
            WowGear g = _current;
            if (g == null || g._net == null) return false;
            int best = g._net.LocalId;
            if (best < 0) return false;
            bool found = false;
            foreach (GearRemote r in g._remotes.Values)
            {
                WowNet.Remote rec = r.Record;
                if (rec == null || rec.PlayerId >= best || r.Distance > maxMetres) continue;
                WowGearState s = rec.Gear;
                if (!s.OriginSaid || s.OriginMap < 0 || s.OriginMap != WowSpace.OriginMap) continue;
                best = rec.PlayerId;
                cellX = s.CellX; cellY = s.CellY; cellZ = s.CellZ;
                found = true;
            }
            leaderId = found ? best : -1;
            return found;
        }

        /// <summary>
        /// Access isolation (Game/Access/WowIsolation, whitelisted rule): true while that player (Bridge PhotonId) has a WoW
        /// network record here (WowNet.HasRecord: a claimed slot, in the room or playing). Every player Gear draws has one;
        /// a visitor never claims one. False while Gear is not running (fails closed).
        /// </summary>
        public static bool HasRecord(int photonId)
        {
            WowGear g = _current;
            return g != null && g._net != null && g._net.HasRecord(photonId);
        }

        /// <summary>
        /// CONTRACT (iteration 6, Views - for the World owner to set EntityState.IsSomniumPlayer): true when this WoW character guid is
        /// the online character of another Somnium player in this world (their Gear record, ints 14-16, says it while they are logged in
        /// to a realm). Their avatar stands there, so the entity is not drawn (its click box stays for targeting). Main thread; cheap
        /// (a per-frame set). Character guids are per-realm counters: two realms can share one - prefer the overload with the name.
        /// </summary>
        public static bool IsSomniumCharacter(ulong guid)
        {
            return IsSomniumCharacter(guid, null);
        }

        /// <summary>As IsSomniumCharacter(guid), and when both the name and the record's name hash are known they must match too.</summary>
        public static bool IsSomniumCharacter(ulong guid, string name)
        {
            if (guid == 0) return false;
            WowGear g = _current;
            if (g == null || g._net == null) return false;
            if (_somniumFrame != Time.frameCount)
            {
                _somniumFrame = Time.frameCount;
                SomniumChars.Clear();
                IReadOnlyList<WowNet.Remote> list = g._net.Remotes;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        WowGearState s = list[i].Gear;
                        if (s.Status == WowNetStatus.Playing && s.CharacterGuid != 0) SomniumChars[s.CharacterGuid] = s.CharacterNameHash;
                    }
            }
            int hash;
            if (!SomniumChars.TryGetValue(guid, out hash)) return false;
            return hash == 0 || string.IsNullOrEmpty(name) || hash == WowNetWire.NameHash(name);
        }

        /// <summary>
        /// Iteration 7 (PLAN7 s5.5, for WowEntityView): where the Somnium avatar of this online WoW character is drawn HERE this frame
        /// (our frame, after re-mapping and the stage-B ride correction) - feet = its mount's feet while it rides (seatHeight = metres
        /// up to the rider's root), else the avatar's feet (seatHeight 0). False when no drawn Somnium avatar has this guid (not a
        /// Somnium character, or hidden - see <see cref="SomniumAvatarHidden"/>). Placed in WowGear.LateUpdate: one frame old when read
        /// in the next Update.
        /// </summary>
        public static bool TryRiderAnchor(ulong guid, out Vector3 feet, out float seatHeight)
        {
            float yaw;
            return TryRiderAnchor(guid, null, out feet, out seatHeight, out yaw);
        }

        /// <summary>As TryRiderAnchor(guid, ...), the record's name hash checked when both are known, plus the drawn heading (degrees).</summary>
        public static bool TryRiderAnchor(ulong guid, string name, out Vector3 feet, out float seatHeight, out float yawDeg)
        {
            feet = Vector3.zero;
            seatHeight = 0f;
            yawDeg = 0f;
            GearRemote r = RemoteOf(guid, name);
            if (r == null || r.AnchorState != 1) return false;
            feet = r.AnchorFeet;
            seatHeight = r.AnchorSeatHeight;
            yawDeg = r.AnchorYaw;
            return true;
        }

        /// <summary>Iteration 7: true when this online WoW character is another Somnium player's whose avatar is NOT drawn here
        /// (another map, the room, beyond 1.5 km, no body yet) - its click box and target circle must not show either.</summary>
        public static bool SomniumAvatarHidden(ulong guid, string name)
        {
            GearRemote r = RemoteOf(guid, name);
            return r != null && r.AnchorState == 2;
        }

        /// <summary>
        /// Stage O2 (Sync, for WowSpellVfx): true when this unit is a Somnium player's avatar here - another player's online WoW character
        /// (their record's guid, the name checked against our session's copy when both are known) or a record-driven key of GearCast.
        /// anchor = that avatar's spell-effect slot (SpellVisualDb.Slot) as drawn this frame, or null while the avatar is not drawn here
        /// (another map, the room, far, no body yet): then nothing is drawn for it, never the server's copy. False for everyone else.
        /// </summary>
        public static bool TryAvatarSlot(ulong guid, int slot, out Transform anchor)
        {
            anchor = null;
            GearRemote r = RemoteOfUnit(guid);
            if (r == null) return false;
            anchor = r.Cast.Slot(slot);
            return true;
        }

        /// <summary>vfxanchor fix 2026-10-04 (for WowSpellVfx's one-time slot log): the posed body of a Somnium player's avatar here, or null.</summary>
        public static GearBody AvatarBody(ulong guid)
        {
            GearRemote r = RemoteOfUnit(guid);
            return r != null ? r.Body : null;
        }

        /// <summary>Stage O2 (Sync, for WowSpellVfx): the hand another Somnium player's avatar casts this spell from (their record),
        /// NotSaid when not a Somnium avatar or not said.</summary>
        public static WowCastHand AvatarCastHand(ulong guid, int spellId)
        {
            GearRemote r = RemoteOfUnit(guid);
            return r != null ? r.Cast.HandFor(spellId) : WowCastHand.NotSaid;
        }

        /// <summary>Stage O2 (Sync, for WowSpellVfx): the hand the LOCAL player casts this spell from (VR: the hand that released the
        /// radial menu; desktop: both, instants the right), NotSaid when unknown.</summary>
        public static WowCastHand LocalCastHand(int spellId)
        {
            WowGear g = _current;
            return g != null ? g._local.CastHandFor(spellId) : WowCastHand.NotSaid;
        }

        /// <summary>The remote whose avatar this unit is: a GearCast key (by slot) or an online character guid (name-checked).</summary>
        private static GearRemote RemoteOfUnit(ulong guid)
        {
            if (guid == 0) return null;
            WowGear g = _current;
            if (g == null) return null;
            if (GearCast.IsKey(guid))
            {
                GearRemote k;
                return g._remotes.TryGetValue(GearCast.SlotOfKey(guid), out k) && k.Cast.Key == guid ? k : null;
            }
            // A character guid means something only to an online session (the server's events); offline guids are the session's own
            // (the offline player is guid 1 - the first character of a fresh realm is guid 1 too).
            if (!(WowPlay.Session is SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession)) return null;
            bool any = false;
            foreach (GearRemote r in g._remotes.Values)
            {
                WowNet.Remote rec = r.Record;
                if (rec != null && rec.Gear.CharacterGuid == guid) { any = true; break; }
            }
            if (!any) return null;
            string name = null;
            try
            {
                IWowSession s = WowPlay.Session;
                EntityState e = s != null ? s.Find(guid) : null;
                name = e != null ? e.Name : null;
            }
            catch (Exception) { name = null; }
            return RemoteOf(guid, name);
        }

        private static GearRemote RemoteOf(ulong guid, string name)
        {
            if (guid == 0) return null;
            WowGear g = _current;
            if (g == null) return null;
            foreach (GearRemote r in g._remotes.Values)
            {
                WowNet.Remote rec = r.Record;
                if (rec == null || rec.Gear.Status != WowNetStatus.Playing || rec.Gear.CharacterGuid != guid) continue;
                int hash = rec.Gear.CharacterNameHash;
                if (hash != 0 && !string.IsNullOrEmpty(name) && hash != WowNetWire.NameHash(name)) continue;
                return r;
            }
            return null;
        }

        private static readonly Dictionary<ulong, int> SomniumChars = new Dictionary<ulong, int>();
        private static int _somniumFrame = -1;

        private static WowGear _current;

        /// <summary>13.7: the local player's matched avatar body (WowOrigin's avatar report after a rebase), null before WowGear runs.</summary>
        public static GearBody LocalBody { get { return _current != null ? _current._local.Body : null; } }
        private static bool _ridesSaid;
        private static int _nearFrame = -1;
        private static bool _nearRight, _nearLeft;

        private readonly GearLocal _local = new GearLocal();
        private readonly Dictionary<int, GearRemote> _remotes = new Dictionary<int, GearRemote>();
        private readonly Dictionary<int, int> _remotePlayer = new Dictionary<int, int>();
        private readonly List<int> _stale = new List<int>();
        private WowNet _net;
        private bool _hooked;
        private int _errors;
        private float _nextStats;

        private void OnEnable()
        {
            if (_net == null) _net = new WowNet();
            _current = this;
            if (GetComponent<GearShift>() == null) gameObject.AddComponent<GearShift>();
            if (GetComponent<GearShiftEarly>() == null) gameObject.AddComponent<GearShiftEarly>();
            if (!_hooked)
            {
                WowPlay.Event += OnEvent;
                WowPlay.Ended += OnEnded;
                WowSettings.Changed += ApplySettings;
                Application.onBeforeRender += BeforeRender;
                WowSpace.Rebased += OnRebased;
                _hooked = true;
            }
            // Iteration 11 (avatar): the form / puppet / armour containers live under the WoW Game object (never hidden, WowOrigin shifts it);
            // the avatar rows of Settings are shown now that forms, the WoW full-body animation and immersive armour (helm + shoulders) work.
            _local.GameRoot = transform;
            WowSettings.SetLive("show_forms");
            WowSettings.SetLive("show_weapons");   // lead 2026-10-12
            WowSettings.SetLive("show_ranged");
            WowSettings.SetLive("avatar_combat_style");
            WowSettings.SetLive("immersive_armour");
            WowSettings.SetLive("armour_fit");
            WowSettings.SetLive("mount_seat_height");   // lead 2026-10-04
            // lead 2026-10-03: the Armour page (Armour Adjustment)
            // lead 2026-10-04: the per-piece rows (checks_b, taper, top, bottom) follow the selected piece (UiSettings.ArmourRows)
            foreach (string key in new[] { "armour_piece", "armour_checks_a", "armour_xy", "armour_z_tilt", "armour_size", "armour_buttons" })
                WowSettings.SetLive(key);
            ApplySettings();
            WowLog.Info("Gear: ready (VR grab: grip at a sheath, grip again at the sheath to put back; desktop key Z; swing speed " + GearLocal.SwingSpeed.ToString("0.0") +
                        " m/s; entity layer 16; desktop avatar combat animations " + (GearLocal.AnimationsOn ? "on" : "off") + ")");
            if (!_ridesSaid) { _ridesSaid = true; WowLog.Info(GearRemote.ConstantsLine()); }
        }

        private string _settingsSaid;

        /// <summary>WowSettings.SwingSpeed / Haptics / ShowPlayerPlates / AvatarCombatAnimations -> the gear modules (at start and on Changed).</summary>
        private void ApplySettings()
        {
            GearLocal.SwingSpeed = WowSettings.SwingSpeed;
            GearXr.HapticsOn = WowSettings.Haptics;
            GearRemote.ShowPlates = WowSettings.ShowPlayerPlates;
            GearLocal.AnimationsOn = WowSettings.AvatarCombatAnimations;
            string said = "Gear: settings - swing speed " + GearLocal.SwingSpeed.ToString("0.00") + " m/s, haptics " + (GearXr.HapticsOn ? "on" : "off") +
                          ", player plates " + (GearRemote.ShowPlates ? "on" : "off") + ", desktop avatar combat animations " + (GearLocal.AnimationsOn ? "on" : "off") +
                          // iteration 11 (avatar): read live by GearLocal / GearRemote / Forms / Armour
                          " (style " + (WowSettings.AvatarCombatStyle == 1 ? "WoW full body" : "simple arms") + "), forms on avatars " + (WowSettings.ShowForms ? "on" : "off") +
                          ", immersive armour " + (Game.Armour.ArmourGate.Mode(WowSettings.ImmersiveArmour) == 2 ? "full (experimental), fit " + WowSettings.ArmourFit.ToString("0.00")
                              : WowSettings.ImmersiveArmour >= 1 ? "helm and shoulders, fit " + WowSettings.ArmourFit.ToString("0.00") : "off");
            if (said == _settingsSaid) return;
            _settingsSaid = said;
            WowLog.Info(said);
        }

        private void OnDisable()
        {
            if (_current == this) _current = null;
            WowParty.Members.Clear();
            if (_hooked)
            {
                WowPlay.Event -= OnEvent;
                WowPlay.Ended -= OnEnded;
                WowSettings.Changed -= ApplySettings;
                Application.onBeforeRender -= BeforeRender;
                WowSpace.Rebased -= OnRebased;
                _hooked = false;
            }
        }

        private void OnDestroy()
        {
            _local.Clear();
            foreach (var r in _remotes.Values) r.Destroy();
            _remotes.Clear();
            if (_net != null) _net.Dispose();
        }

        private void Fail(string where, Exception e)
        {
            _errors++;
            if (_errors <= 10 || _errors % 500 == 0) WowLog.Error("Gear: " + where + " threw (" + _errors + "): " + e);
        }

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            try { _local.Tick(s, WowPlay.Files); }
            catch (Exception e) { Fail("local player", e); }

            try
            {
                if (_net == null) _net = new WowNet();
                _net.Tick(_local.State(s, SceneNetworking.IsMasterClient));
            }
            catch (Exception e) { Fail("network", e); }

            try { SyncRemotes(); }
            catch (Exception e) { Fail("remote players", e); }

            if (Time.unscaledTime >= _nextStats)
            {
                _nextStats = Time.unscaledTime + 120f;
                if (_net != null && (_net.SlotCount > 0 || _remotes.Count > 0)) WowLog.Info(_net.StatsLine() + " | " + GearRemap.StatsLine());
                // Iteration 7: how smooth remote riders are drawn (the measurement that tells whether stage B is needed / works).
                int riders = 0;
                foreach (var r in _remotes.Values)
                {
                    if (r.Riding) riders++;
                    string j = r.JitterLine();
                    if (j != null) WowLog.Info(j);
                }
                string ride = GearRemote.RideStatsLine(riders);
                if (ride != null) WowLog.Info(ride);
            }
        }

        private void SyncRemotes()
        {
            IReadOnlyList<WowNet.Remote> list = _net != null ? _net.Remotes : null;
            _stale.Clear();
            foreach (int k in _remotes.Keys) _stale.Add(k);
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    WowNet.Remote r = list[i];
                    GearRemote g;
                    int pid;
                    if (_remotes.TryGetValue(r.Slot, out g) && _remotePlayer.TryGetValue(r.Slot, out pid) && pid != r.PlayerId)
                    {
                        g.Destroy();
                        _remotes.Remove(r.Slot);
                        g = null;
                    }
                    if (g == null)
                    {
                        g = new GearRemote(transform);
                        _remotes[r.Slot] = g;
                        _remotePlayer[r.Slot] = r.PlayerId;
                        WowLog.Info("Gear: drawing player " + r.PlayerId + " '" + r.Name + "' (slot " + r.Slot + ")");
                    }
                    g.Tick(r, WowPlay.Files);
                    _stale.Remove(r.Slot);
                }
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                _remotes[_stale[i]].Destroy();
                _remotes.Remove(_stale[i]);
                _remotePlayer.Remove(_stale[i]);
            }
        }

        private void LateUpdate()
        {
            try { _local.Place(WowPlay.Session); }
            catch (Exception e) { Fail("placing the local weapons", e); }
            // Iteration 11 (combat feel): the local hand / weapon spell effects onto the avatar as just posed (WowSpellVfx runs earlier).
            try { WowSpellVfx.PlaceLocalAnchors(_local.Body); }
            catch (Exception e) { Fail("placing the local spell effect anchors", e); }
            GearLocalView v = LocalView();
            foreach (var r in _remotes.Values)
            {
                try { r.Place(v); }
                catch (Exception e) { Fail("placing a remote player's weapons", e); }
            }
            try { FillParty(v); }
            catch (Exception e) { Fail("filling WowParty", e); }
        }

        /// <summary>This client as GearRemote.Place needs it: in a game or in the room, our origin map, our feet, what never moves.</summary>
        private GearLocalView LocalView()
        {
            IWowSession s = WowPlay.Session;
            var v = new GearLocalView { InWorld = s != null && s.Player != null, Map = WowSpace.OriginMap, GameRoot = transform };
            v.HaveFeet = v.InWorld && _local.Body.Valid;
            v.Feet = _local.Body.Feet;
            Transform root = null;
            try { root = SomniumSpace.Bridge.SomniumBridge.PlayersContainer?.LocalPlayer?.References?.Body?.Root; }
            catch (Exception) { root = null; }
            v.PlayerRoot = root != null ? root.root : _local.Body.PlayerRoot;
            v.AvatarRoot = _local.Body.AvatarRoot;
            // Stage O2: glows on remote avatars face the local player's head (the camera as the fallback).
            Camera cam = Camera.main;
            v.Eye = _local.Body.Valid ? _local.Body.HeadPos : (cam != null ? cam.transform.position : v.Feet + Vector3.up * 1.6f);
            return v;
        }

        /// <summary>WowParty.Members from every counted slot, each frame (the UI reads it).</summary>
        private void FillParty(in GearLocalView v)
        {
            List<WowPartyMember> m = WowParty.Members;
            m.Clear();
            IReadOnlyList<WowNet.Remote> list = _net != null ? _net.Remotes : null;
            if (list == null) return;
            // Stage S1 (party request 2, integrator): the record's WoW character guid - only while we play online too (then both
            // characters are on the same realm and the guid is a unit of our game; an offline game does not know it).
            IWowSession session = WowPlay.Session;
            bool online = session != null && !(session is SomniumSpace.Worlds.SomniumWoW.Play.Offline.OfflineSession);
            for (int i = 0; i < list.Count; i++)
            {
                WowNet.Remote r = list[i];
                WowGearState g = r.Gear;
                GearRemote gr;
                _remotes.TryGetValue(r.Slot, out gr);
                bool inRoom = g.Status != WowNetStatus.Playing || (g.OriginSaid && g.OriginMap < 0);
                // Not said (an older build / no origin there yet): assumed on our map, as its unmapped drawing assumes.
                int map = inRoom ? -1 : g.OriginSaid ? g.OriginMap : (v.InWorld ? v.Map : -1);
                m.Add(new WowPartyMember
                {
                    PlayerId = r.PlayerId,
                    Name = r.Name,
                    Level = g.Level,
                    Class = (WowClass)g.Class,
                    Race = g.Race,
                    HealthPct = g.HealthPercent / 100f,
                    MapId = map,
                    Vr = g.Xr,
                    Distance = gr != null && gr.Record == r ? gr.Distance : float.MaxValue,
                    Guid = online && g.Status == WowNetStatus.Playing ? g.CharacterGuid : 0,
                    SomniumUserId = UserIdOf(r),
                });
            }
        }

        /// <summary>Stage S1: the Somnium user id of a record's player (Bridge ISomniumPlayer.Properties.Id, proven members), null
        /// when the Bridge does not list them yet. Its own try: one odd player must not empty the whole party list.</summary>
        private static string UserIdOf(WowNet.Remote r)
        {
            try
            {
                if (r == null || r.Player == null || r.Player.Properties == null) return null;
                string id = r.Player.Properties.Id;
                return string.IsNullOrEmpty(id) ? null : id;
            }
            catch (Exception) { return null; }
        }

        private void BeforeRender()
        {
            try { _local.BeforeRender(); }
            catch (Exception e) { Fail("before render", e); }
            // Iteration 11 (combat feel): VR - the hand effects follow this frame's controller poses like the held weapons above.
            if (_local.Xr)
            {
                try { WowSpellVfx.PlaceLocalAnchors(_local.Body); }
                catch (Exception e) { Fail("before render (spell effect anchors)", e); }
            }
        }

        /// <summary>WowSpace.Rebased (WowOrigin moved our origin by delta): the local swing trackers and the remote offsets follow.
        /// Our weapons, plates and mounts are placed from the bodies every frame, so nothing else holds a Unity position.</summary>
        private void OnRebased(Vector3 delta)
        {
            _local.OnRebased(delta);
            foreach (var r in _remotes.Values) r.OnLocalRebased(delta);
        }

        private void OnEvent(GameEvent e)
        {
            _local.OnEvent(WowPlay.Session, e);
        }

        private void OnEnded()
        {
            _local.Clear();
        }

        // ---- iteration 11 - owner avatar (wave-0 stubs by w0game, the owner fills them): the public statics other owners call
        // (proposal-avatar.md contract "WowGear public statics"; vrcast's hook 11 TryHeldBlade / NoteCastHand). Filled by the owner.

        /// <summary>The look the local avatar shows now (forms, ghost, stealth...); Own outside a game.</summary>
        public static SomniumSpace.Worlds.SomniumWoW.Play.Rules.AvatarLook LocalLook
        {
            get
            {
                WowGear g = _current;
                return g != null ? g._local.Look : new SomniumSpace.Worlds.SomniumWoW.Play.Rules.AvatarLook { Kind = SomniumSpace.Worlds.SomniumWoW.Play.Rules.AvatarLookKind.Own, Scale = 1f };
            }
        }

        /// <summary>A creature form's attachment point for spell visual slot (SpellVisualDb slots: attachments 17-22 / 34, SlotFallback,
        /// else the model root at 0.6 / 0.95 of its height) while LocalLook is Creature and its model is built. False = the avatar's points.</summary>
        public static bool TryLocalFormSlot(int slot, out Vector3 pos, out Quaternion rot)
        {
            WowGear g = _current;
            if (g != null) return g._local.TryFormSlot(slot, out pos, out rot);
            pos = Vector3.zero; rot = Quaternion.identity; return false;
        }

        /// <summary>Plays a WoW emote animation (AnimationData id, e.g. 69 EmoteDance, 67 EmoteWave) on the local avatar (desktop:
        /// retargeted; a creature look: the creature's own), synced through int 32. False when it cannot play (VR, the setting off).</summary>
        public static bool PlayAvatarEmote(int animId)
        {
            WowGear g = _current;
            return g != null && g._local.Emote(animId);
        }

        /// <summary>VR: the blade capsule (a, b, radius) of the weapon held in that hand (vrcast's strike ring; GearLocal's TestBlade rule).</summary>
        public static bool TryHeldBlade(bool rightHand, out Vector3 a, out Vector3 b, out float radius)
        {
            WowGear g = _current;
            if (g != null) return g._local.TryBlade(rightHand, out a, out b, out radius);
            a = b = Vector3.zero; radius = 0f; return false;
        }

        /// <summary>VR: a strike with that hand counts like a grip release for the cast hand the others see.</summary>
        public static void NoteCastHand(bool rightHand)
        {
            WowGear g = _current;
            if (g != null) g._local.NoteCastHand(rightHand);
        }
    }
}
