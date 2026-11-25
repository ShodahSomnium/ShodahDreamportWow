// Lead 2026-10-10 (the user: "lets add immersive bow for hunters ... grab their bow from grabbing anywhere behind their back with their
// left or right hand, hold their bow, grab and HOLD an arrow from anywhere behind their head, place it on the bow while still holding,
// then they can pull it back and ... as you pull back the bow, your spells will be revealed in front of you, like a line you will scroll
// 6 different spell slots, to cast a spell, let go of the arrow while on the spell you want. The actual line of spells moves down the
// further you pull back, so the selected spell is always shown in the same place, add vibration with every spell you scroll past, also
// add some sort of 'buffer' ... when putting the arrow on the bow, it automatically goes to the middle of the bow string, you can also do
// UP or DOWN once, to use a different order of spells ... I would like the bow string to be able to move").
// The string: WoW draws a bow's string itself - probe BowString (harness, Probes/Lead12): 62 of 69 bow models have two vertex-less bones
// whose parents animate in BowPull (AnimationData 160), with no vertices between them in 56; their limbs bend in BowPull (160, ~1 s) and
// spring back in BowRelease (161). So: a line from one string-end bone through the nock to the other, BowPull scrubbed by the draw,
// BowRelease on the shot. The draw / nock numbers follow the Skyrim world's VR bow (SkyGear.Vr.cs: MaxDraw 0.62 m, NockRadius 0.12,
// SlipDistance 0.22, tension ticks every 0.06 s, release pulses 0.8 / 0.6) - ours here.
using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// VR immersive bow (WowSettings.ImmersiveBow): a bow in either hand (grabbed from the back, GrabFor) makes the other hand the string
    /// hand. Its grip behind the back takes an arrow (held while the grip is held; let go = gone); near the string (BowNockRadius of the
    /// rest point .. grip) the arrow nocks at the string's middle and the six buttons of the string hand's half of the bar show as a column
    /// beside the bow (VrRadialMenu Column). Pulling back along the bow's shoot axis selects slot 1 after BowFirstSlot, one slot per
    /// BowPerSlot, BowBuffer of a slot sticky at each edge, a pulse per slot; the column scrolls so the selected slot stays on the
    /// selection frame. Before the first slot the hand up / down (BowRowFlick along the limbs) switches once to the bar above / below.
    /// Letting go on a slot casts it (CastSpell, or UseActionButton for items / macros; an auto-repeat shot already running is left on)
    /// and the arrow flies; before the first slot it is put away. Sideways off the axis by BowSlip un-nocks. The string is drawn on our
    /// own bow whenever it is shown (held or on the back, VR and desktop). Values ours unless noted.
    /// </summary>
    public sealed partial class GearLocal
    {
        public const float BowNockRadius = 0.14f, BowSlip = 0.25f, BowBrace = 0.12f, BowMaxDraw = 0.62f;
        public const float BowFirstSlot = 0.06f, BowPerSlot = 0.08f, BowBuffer = 0.2f, BowRowFlick = 0.07f, BowRowRearm = 0.03f;
        public const float BowMenuSide = 0.11f, ArrowSpeed = 45f, ArrowFlight = 0.35f, StringWidth = 0.003f;
        /// <summary>One of the six ammo models in the 3.3.5 files (probe BowBones: Item\ObjectComponents\AMMO\, ArrowFlight_01 first).</summary>
        public const string ArrowModel = @"Item\ObjectComponents\AMMO\ArrowFlight_01.m2";
        private static readonly Color StringColour = new Color(0.86f, 0.82f, 0.72f, 1f);

        private int _arrowHand = -1;          // 1 right / 0 left holds an arrow, -1 none
        private bool _nocked, _bowRowArmed = true, _bowLiveSaid, _arrowTried;
        private float _draw, _bowU, _bowTick, _flyT = -1f;
        private int _bowSel = -1, _bowRowOff;
        private VrRadialMenu _bowMenu;
        private readonly VrRows _bowRows = new VrRows();
        private LineRenderer _bowString;
        private GameObject _arrowGo;
        private float _arrowTail;             // the arrow model's tail (local z; its head points along +z = WoW's +x)
        private GearItem _stringFor;
        private Transform _strA, _strB;
        private M2Animator _bowAnim;
        private int _bowPullDur;
        private Vector3 _restLocal, _shootLocal = Vector3.down, _endALocal, _endBLocal;
        private Vector3 _flyPos, _flyDir;

        /// <summary>The hand holding our bow (1 right, 0 left, -1 none).</summary>
        private int BowHand()
        {
            GearItem it = ItemOf(17);
            if (it == null || !it.IsBow || it.Go == null) return -1;
            return _right == 17 ? 1 : _left == 17 ? 0 : -1;
        }

        private bool BowOn(PlayerState p)
        {
            return _xr && WowSettings.ImmersiveBow && !_hidden && p != null && !p.Dead && !p.Mounted && BowHand() >= 0;
        }

        /// <summary>The other hand holds the bow: this hand's grip behind the back takes an arrow (GrabFor leaves it alone).</summary>
        private bool ArrowHand(bool right)
        {
            IWowSession s = WowPlay.Session;
            return BowOn(s != null ? s.Player : null) && BowHand() == (right ? 0 : 1);
        }

        /// <summary>For NearSheath / WowGear.HandOnBow: the bow hand, the hand holding an arrow, and the string hand behind the back.</summary>
        public bool BowBusy(bool right, Vector3 hp, bool useZone)
        {
            IWowSession s = WowPlay.Session;
            if (!BowOn(s != null ? s.Player : null)) return false;
            int bh = BowHand();
            if (bh == (right ? 1 : 0)) return true;
            if (_arrowHand == (right ? 1 : 0)) return true;
            return useZone && BehindBack(hp);
        }

        // ------------------------------------------------------------------ Update (GearLocal.Tick, after StepVr)

        private void StepBow(IWowSession s, PlayerState p)
        {
            if (_xr && !_bowLiveSaid) { _bowLiveSaid = true; WowSettings.SetLive("immersive_bow"); }
            int bh = BowOn(p) ? BowHand() : -1;
            if (bh < 0) { if (_arrowHand >= 0) DropArrow("the bow left the hand"); RelaxBow(); return; }
            bool bowRight = bh == 1, strRight = !bowRight;
            GearItem bow = ItemOf(17);
            FindString(bow);
            Vector3 sp; Quaternion sr;
            if (!Body.HandPose(strRight, out sp, out sr)) return;
            Vector3 grip = bow.T.position, rest = bow.T.TransformPoint(_restLocal), shoot = bow.T.TransformDirection(_shootLocal).normalized;
            if (_arrowHand < 0)
            {
                if (GearXr.GripPressed(strRight) && (strRight ? _right : _left) == 0 && BehindBack(sp)) TakeArrow(strRight);
                RelaxBow();
                return;
            }
            if (!GearXr.GripHeld(strRight)) { Loose(s, p, strRight); return; }
            if (!_nocked)
            {
                if (GearPlace.DistanceToSegment(sp, rest - shoot * 0.10f, grip) >= BowNockRadius * Body.Scale) { RelaxBow(); return; }
                _nocked = true;
                _draw = 0f;
                _bowSel = -1;
                _bowU = 0f;
                _bowRowOff = 0;
                _bowRowArmed = true;
                GearXr.Pulse(strRight, 0.45f, 0.04f);
                GearXr.Pulse(bowRight, 0.3f, 0.04f);
                WowLog.Info("Gear: VR arrow nocked (" + (strRight ? "right" : "left") + " hand on the string) - pull back to choose a shot, up / down first for another bar.");
            }
            Vector3 limb = LimbAxis(bow, shoot);
            Vector3 off = sp - rest;
            Vector3 lateral = off - Vector3.Dot(off, shoot) * shoot;
            float up = Vector3.Dot(lateral, limb);
            float sideways = (lateral - up * limb).magnitude;
            if (sideways > BowSlip * Body.Scale)
            {
                _nocked = false;
                _draw = 0f;
                _bowSel = -1;
                RelaxBow();
                WowLog.Info("Gear: VR arrow slipped off the string (" + sideways.ToString("0.00") + " m to the side).");
                return;
            }
            _draw = Mathf.Clamp(Vector3.Dot(rest - sp, shoot), 0f, BowMaxDraw);
            // up / down once, before the first slot: the bar above / below (VrRows: up = the higher row, as the Gear Select bar)
            if (_draw < BowFirstSlot)
            {
                if (_bowRowArmed && Mathf.Abs(up) > BowRowFlick)
                {
                    int want = Mathf.Clamp(_bowRowOff + (up > 0f ? 1 : -1), -1, 1);
                    if (want != _bowRowOff && _bowRows.Exists(want))
                    {
                        _bowRowOff = want;
                        GearXr.Pulse(strRight, 0.45f, 0.05f);
                        GearXr.Pulse(bowRight, 0.25f, 0.05f);
                        WowLog.Info("Gear: VR bow row -> " + _bowRows.NameOf(want) + ".");
                    }
                    _bowRowArmed = false;
                }
                else if (!_bowRowArmed && Mathf.Abs(up) < BowRowRearm) _bowRowArmed = true;
            }
            // the slot under the draw, BowBuffer of a slot sticky at each edge (and a centimetre below the first)
            float u = (_draw - BowFirstSlot) / BowPerSlot;
            int sel = _bowSel;
            if (_draw < BowFirstSlot - (sel >= 0 ? 0.01f : 0f)) sel = -1;
            else
            {
                if (sel < 0) sel = 0;
                while (sel < VrRadialMenu.Count - 1 && u > sel + 0.5f + BowBuffer) sel++;
                while (sel > 0 && u < sel - 0.5f - BowBuffer) sel--;
            }
            if (sel != _bowSel)
            {
                _bowSel = sel;
                GearXr.Pulse(strRight, 0.45f, 0.025f);
                GearXr.Pulse(bowRight, 0.2f, 0.025f);
                _bowTick = 0.06f;
            }
            else
            {
                _bowTick -= Time.deltaTime;
                if (_bowTick <= 0f && _draw > 0.02f)
                {
                    _bowTick = 0.06f;
                    float k = _draw / BowMaxDraw;
                    GearXr.Pulse(strRight, 0.03f + 0.12f * k, 0.03f);
                    GearXr.Pulse(bowRight, 0.02f + 0.08f * k, 0.03f);
                }
            }
            PullBow(_draw / BowMaxDraw);
        }

        /// <summary>The limbs' direction across the shoot axis, toward world up (the string line, else the bow's long axis).</summary>
        private Vector3 LimbAxis(GearItem bow, Vector3 shoot)
        {
            Vector3 l = bow.T.TransformPoint(_endBLocal) - bow.T.TransformPoint(_endALocal);
            l = Vector3.ProjectOnPlane(l, shoot);
            if (l.sqrMagnitude < 1e-6f) l = Vector3.ProjectOnPlane(bow.T.forward, shoot);
            l.Normalize();
            return Vector3.Dot(l, Vector3.up) < 0f ? -l : l;
        }

        private void TakeArrow(bool right)
        {
            EnsureArrow();
            _arrowHand = right ? 1 : 0;
            _nocked = false;
            _draw = 0f;
            _bowSel = -1;
            _flyT = -1f;
            GearXr.Pulse(right, 0.35f, 0.04f);
            WowLog.Info("Gear: VR " + (right ? "right" : "left") + " hand took an arrow from behind the back - hold the grip, bring it to the string.");
        }

        private void DropArrow(string why)
        {
            if (_arrowHand < 0) return;
            _arrowHand = -1;
            _nocked = false;
            _draw = 0f;
            _bowSel = -1;
            _bowRowOff = 0;
            WowLog.Info("Gear: VR arrow put away (" + why + ").");
        }

        /// <summary>The grip let go of the arrow: on a slot = the shot (the button cast, the arrow flies, BowRelease); else put away.</summary>
        private void Loose(IWowSession s, PlayerState p, bool strRight)
        {
            int sel = _nocked ? _bowSel : -1, row = _bowRowOff;
            GearItem bow = ItemOf(17);
            if (sel < 0) { GearXr.Pulse(strRight, 0.1f, 0.03f); DropArrow(_nocked ? "let go before the first slot" : "let go before nocking"); RelaxBow(); return; }
            Vector3 rest = bow.T.TransformPoint(_restLocal), shoot = bow.T.TransformDirection(_shootLocal).normalized;
            _flyDir = shoot;
            _flyPos = rest - shoot * _draw;
            _flyT = 0f;
            GearXr.Pulse(strRight, 0.8f, 0.06f);
            GearXr.Pulse(!strRight, 0.6f, 0.08f);
            if (_bowAnim != null) { _bowAnim.Speed = 1f; if (_bowAnim.Has(161)) _bowAnim.Play(161, false); else _bowAnim.Play(0, true); }   // BowRelease
            _arrowHand = -1;
            _nocked = false;
            _draw = 0f;
            _bowSel = -1;
            _bowRowOff = 0;
            Shoot(s, p, row, sel, strRight);
        }

        private void Shoot(IWowSession s, PlayerState p, int row, int sel, bool strRight)
        {
            int slot = WowActionSlots.RowSlot(row, strRight, sel, UiBars.MainPage);
            string where = WowActionSlots.Valid(slot) ? WowActionSlots.BarName(slot) + " button " + WowActionSlots.ButtonOf(slot) : "slot " + slot;
            if (!WowActionSlots.Valid(slot) || p.ActionButtons == null || slot >= p.ActionButtons.Length) return;
            int packed = p.ActionButtons[slot];
            try
            {
                switch (WowActionSlots.KindOf(packed))
                {
                    case ActionKind.Empty:
                        WowLog.Info("Gear: VR bow shot on an empty " + where + " - nothing cast.");
                        return;
                    case ActionKind.Spell:
                    {
                        int spell = WowActionSlots.SpellOf(packed);
                        SpellInfo info = null;
                        try { info = _files != null ? SpellDb.Get(_files).Find(spell) : null; } catch (Exception) { info = null; }
                        if (info != null && info.AutoRepeat && p.AutoShooting)
                        {
                            WowLog.Info("Gear: VR bow shot on " + where + " (" + (info.Name ?? "spell " + spell) + ") - already repeating, left on.");
                            return;
                        }
                        s.CastSpell(spell, 0);
                        WowLog.Info("Gear: VR bow shot -> " + (info != null && info.Name != null ? info.Name : "spell " + spell) + " (" + where + ").");
                        return;
                    }
                    default:
                        s.UseActionButton(slot, 0);
                        WowLog.Info("Gear: VR bow shot -> " + UiActions.NameOf(p, packed) + " (" + where + ").");
                        return;
                }
            }
            catch (Exception e) { WowLog.Warn("Gear: VR bow shot on " + where + " threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ the bow's own string and limbs

        /// <summary>Once per bow model: its two string-end bones (vertex-less bones whose parents animate in BowPull; the farthest pair),
        /// BowPull's length, and in the relaxed pose the rest point (the string's point nearest the grip) and the shoot direction - kept in
        /// the bow's own space, so the bent limbs never move the draw's zero.</summary>
        private void FindString(GearItem bow)
        {
            if (bow == null || bow.Go == null || ReferenceEquals(bow, _stringFor)) return;
            _stringFor = bow;
            _strA = _strB = null;
            _bowPullDur = 0;
            _bowAnim = bow.Go.GetComponentInChildren<M2Animator>(true);
            M2Model m = _bowAnim != null ? _bowAnim.Model : null;
            int ea = -1, eb = -1;
            if (m != null && _bowAnim.Bones != null)
            {
                int si = Array.FindIndex(m.Sequences, q => q.AnimId == 160);
                if (si >= 0) _bowPullDur = m.Sequences[si].Duration;
                var used = new bool[m.Bones.Length];
                for (int v = 0; v < m.VertexCount; v++)
                    for (int k = 0; k < 4; k++)
                        if (m.BoneWeights[v * 4 + k] > 0 && m.BoneIndices[v * 4 + k] < used.Length) used[m.BoneIndices[v * 4 + k]] = true;
                float best = 0f;
                for (int a = 0; a < m.Bones.Length; a++)
                {
                    if (!IsStringEnd(m, a, used, si)) continue;
                    for (int b = a + 1; b < m.Bones.Length; b++)
                    {
                        if (!IsStringEnd(m, b, used, si)) continue;
                        M2Vec3 pa = m.Bones[a].Pivot, pb = m.Bones[b].Pivot;
                        float d = (pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y) + (pa.Z - pb.Z) * (pa.Z - pb.Z);
                        if (d > best) { best = d; ea = a; eb = b; }
                    }
                }
                if (ea >= 0 && ea < _bowAnim.Bones.Length && eb < _bowAnim.Bones.Length) { _strA = _bowAnim.Bones[ea]; _strB = _bowAnim.Bones[eb]; }
            }
            if (_bowAnim != null) { _bowAnim.Speed = 1f; _bowAnim.Play(0, true); }   // the relaxed pose to measure
            Transform t = bow.T;
            GearGeometry g = bow.Geo;
            bool measured = false;
            if (_strA != null && _strB != null)
            {
                Vector3 a = _strA.position, b = _strB.position, root = t.position, ab = b - a;
                float k = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(root - a, ab) / ab.sqrMagnitude) : 0.5f;
                Vector3 rest = a + ab * k;
                if ((root - rest).magnitude > 0.02f)
                {
                    _restLocal = t.InverseTransformPoint(rest);
                    _shootLocal = t.InverseTransformDirection((root - rest).normalized);
                    _endALocal = t.InverseTransformPoint(a);
                    _endBLocal = t.InverseTransformPoint(b);
                    measured = true;
                }
            }
            if (!measured)
            {
                // GearItem: the limbs run along +z, the string side toward +y - the string BowBrace behind the grip, its ends at the tips
                _strA = _strB = null;
                float zl = g != null ? g.Min.z * 0.95f : -0.6f, zh = g != null ? g.Max.z * 0.95f : 0.6f;
                _restLocal = new Vector3(0f, BowBrace, 0f);
                _shootLocal = Vector3.down;
                _endALocal = new Vector3(0f, BowBrace * 0.5f, zl);
                _endBLocal = new Vector3(0f, BowBrace * 0.5f, zh);
            }
            WowLog.Info("Gear: bow string " + (measured ? "between bones " + ea + " / " + eb + " (" + (t.TransformPoint(_endALocal) - t.TransformPoint(_endBLocal)).magnitude.ToString("0.00") + " m), rest "
                + (t.position - t.TransformPoint(_restLocal)).magnitude.ToString("0.00") + " m behind the grip" : "not found on the model - drawn " + BowBrace + " m behind the grip (ours)")
                + "; BowPull " + (_bowPullDur > 0 ? _bowPullDur + " ms" : "none") + ".");
        }

        private static bool IsStringEnd(M2Model m, int b, bool[] used, int pullSeq)
        {
            if (used[b] || pullSeq < 0) return false;
            int pa = m.Bones[b].Parent;
            if (pa < 0 || pa >= m.Bones.Length) return false;
            M2Track<M2Quat> r = m.Bones[pa].Rotation;
            return r != null && pullSeq < r.Times.Length && r.Times[pullSeq] != null && r.Times[pullSeq].Length > 1;
        }

        /// <summary>The limbs bent to frac of a full draw (BowPull held at that time).</summary>
        private void PullBow(float frac)
        {
            if (_bowAnim == null || _bowPullDur <= 0) return;
            _bowAnim.Scrub(160, Mathf.Clamp01(frac) * (_bowPullDur - 1));
        }

        /// <summary>Back to the relaxed bow (a BowRelease still playing finishes by itself).</summary>
        private void RelaxBow()
        {
            if (_bowAnim == null || _bowAnim.PlayingAnimationId != 160) return;
            _bowAnim.Speed = 1f;
            _bowAnim.Play(0, true);
        }

        // ------------------------------------------------------------------ LateUpdate / before render: string, arrow, column

        /// <summary>After the weapons are placed (Place, BeforeRender): the string on our shown bow, the arrow, the column.</summary>
        private void PlaceBow(IWowSession s, bool refresh)
        {
            GearItem bow = ItemOf(17);
            bool shown = bow != null && bow.IsBow && bow.Go != null && bow.Go.activeInHierarchy;
            if (shown) FindString(bow);
            PlayerState p = s != null ? s.Player : null;
            int bh = shown && BowOn(p) ? BowHand() : -1;
            bool strRight = bh == 0;
            Vector3 sp = Vector3.zero; Quaternion sr = Quaternion.identity;
            bool hand = bh >= 0 && Body.HandPose(strRight, out sp, out sr);
            Vector3 rest = Vector3.zero, shoot = Vector3.forward, nock = Vector3.zero;
            if (shown)
            {
                Transform t = bow.T;
                rest = t.TransformPoint(_restLocal);
                shoot = t.TransformDirection(_shootLocal).normalized;
                float draw = hand && _nocked ? Mathf.Clamp(Vector3.Dot(rest - sp, shoot), 0f, BowMaxDraw) : 0f;
                nock = rest - shoot * draw;
                Vector3 a = _strA != null ? _strA.position : t.TransformPoint(_endALocal);
                Vector3 b = _strB != null ? _strB.position : t.TransformPoint(_endBLocal);
                LineRenderer line = EnsureString();
                if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
                if (_nocked && hand)
                {
                    line.positionCount = 3;
                    line.SetPosition(0, a);
                    line.SetPosition(1, nock);
                    line.SetPosition(2, b);
                }
                else
                {
                    line.positionCount = 2;
                    line.SetPosition(0, a);
                    line.SetPosition(1, b);
                }
            }
            else if (_bowString != null && _bowString.gameObject.activeSelf) _bowString.gameObject.SetActive(false);

            // the arrow: in the hand, on the string, or flying
            bool arrow = hand && _arrowHand == (strRight ? 1 : 0);
            if (arrow && EnsureArrow())
            {
                Vector3 dir = _nocked ? shoot : (sr * Vector3.forward);
                Vector3 tail = _nocked ? nock : sp - dir * 0.04f;
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
                _arrowGo.transform.SetPositionAndRotation(tail - rot * new Vector3(0f, 0f, _arrowTail), rot);
                if (!_arrowGo.activeSelf) _arrowGo.SetActive(true);
            }
            else if (_flyT >= 0f && _arrowGo != null)
            {
                if (refresh)
                {
                    _flyT += Time.deltaTime;
                    _flyPos += _flyDir * ArrowSpeed * Time.deltaTime;
                }
                Quaternion rot = Quaternion.LookRotation(_flyDir, Vector3.up);
                _arrowGo.transform.SetPositionAndRotation(_flyPos - rot * new Vector3(0f, 0f, _arrowTail), rot);
                if (!_arrowGo.activeSelf) _arrowGo.SetActive(true);
                if (_flyT > ArrowFlight) { _flyT = -1f; _arrowGo.SetActive(false); }
            }
            else if (_arrowGo != null && _arrowGo.activeSelf) _arrowGo.SetActive(false);

            // the column beside the bow while nocked: the side away from the string hand, the selection frame at the arrow's height
            bool column = hand && _nocked && p != null;
            if (!column) { if (_bowMenu != null) _bowMenu.Show(false); return; }
            if (_bowMenu == null || _bowMenu.Right != strRight)
            {
                if (_bowMenu != null) _bowMenu.Destroy();
                _bowMenu = new VrRadialMenu(strRight, false, true);
            }
            Camera cam = M2Animator.FindCamera();
            Vector3 eye = cam != null ? cam.transform.position : Body.HeadPos;
            Vector3 grip = bow.T.position;
            Vector3 look = grip - eye; look.y = 0f;
            Vector3 viewRight = look.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, look.normalized) : Body.Right;
            Vector3 at = grip + (strRight ? -viewRight : viewRight) * BowMenuSide;
            if (refresh) _bowU = Mathf.Lerp(_bowU, Mathf.Max(0, _bowSel), 1f - Mathf.Exp(-18f * Time.deltaTime));
            _bowMenu.Show(true);
            _bowMenu.PlaceColumn(at, eye, LimbAxis(bow, shoot), _bowU);
            if (refresh)
            {
                _bowRows.MaxRow = WowActionSlots.MaxRow;
                _bowMenu.Refresh(s, p, _bowRows, _bowRowOff, _bowSel, false, Time.unscaledTime);
                UiArt.PumpIcons(2);
            }
        }

        private LineRenderer EnsureString()
        {
            if (_bowString != null) return _bowString;
            var go = new GameObject("WoW bow string (local player)");
            if (GameRoot != null) go.transform.SetParent(GameRoot, false);
            _bowString = go.AddComponent<LineRenderer>();
            _bowString.useWorldSpace = true;
            _bowString.positionCount = 2;
            _bowString.alignment = LineAlignment.View;
            _bowString.textureMode = LineTextureMode.Stretch;
            _bowString.numCapVertices = 0;
            _bowString.numCornerVertices = 0;
            _bowString.shadowCastingMode = ShadowCastingMode.Off;
            _bowString.receiveShadows = false;
            _bowString.lightProbeUsage = LightProbeUsage.Off;
            _bowString.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _bowString.sharedMaterial = M2Effects.MaterialFor(VrArt.Beam, 2, true, 4, false);   // VrLine's: alpha blend, soft edges
            _bowString.widthMultiplier = StringWidth;
            _bowString.startColor = StringColour;
            _bowString.endColor = StringColour;
            return _bowString;
        }

        /// <summary>The held / flying arrow: WoW's ArrowFlight model (else a plain shaft); its tail found from the mesh's bounds.</summary>
        private bool EnsureArrow()
        {
            if (_arrowGo != null) return true;
            if (_arrowTried) return false;
            _arrowTried = true;
            try
            {
                if (_files != null && _files.Exists(ArrowModel)) _arrowGo = M2Factory.Build(_files, ArrowModel, new M2BuildOptions { Name = "WoW VR arrow (local player)" });
            }
            catch (Exception e) { _arrowGo = null; WowLog.Warn("Gear: the arrow model threw " + e.Message + " - a plain shaft instead."); }
            if (_arrowGo != null)
            {
                float zMin = float.MaxValue, zMax = float.MinValue;
                foreach (Renderer r in _arrowGo.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    var smr = r as SkinnedMeshRenderer;
                    var mf = r.GetComponent<MeshFilter>();
                    Mesh mesh = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    Bounds mb = mesh.bounds;
                    for (int c = 0; c < 2; c++)
                    {
                        Vector3 pt = _arrowGo.transform.InverseTransformPoint(r.transform.TransformPoint(mb.center + new Vector3(0f, 0f, c == 0 ? -mb.extents.z : mb.extents.z)));
                        zMin = Mathf.Min(zMin, pt.z);
                        zMax = Mathf.Max(zMax, pt.z);
                    }
                    if (smr != null) smr.updateWhenOffscreen = true;
                }
                _arrowTail = zMin < zMax ? zMin : 0f;
                WowLog.Info("Gear: VR arrow = " + ArrowModel + " (" + (zMax - zMin).ToString("0.00") + " m, tail at z " + _arrowTail.ToString("0.00") + ").");
            }
            else
            {
                _arrowGo = new GameObject("WoW VR arrow (local player, plain)");
                var lr = _arrowGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.SetPosition(0, Vector3.zero);
                lr.SetPosition(1, new Vector3(0f, 0f, 0.7f));
                lr.widthMultiplier = 0.008f;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.sharedMaterial = M2Effects.MaterialFor(VrArt.Beam, 2, true, 4, false);
                lr.startColor = lr.endColor = new Color(0.45f, 0.32f, 0.18f, 1f);
                _arrowTail = 0f;
            }
            if (GameRoot != null) _arrowGo.transform.SetParent(GameRoot, true);
            _arrowGo.SetActive(false);
            return true;
        }

        /// <summary>Clear(): the bow's things of this game gone.</summary>
        private void ClearBow()
        {
            if (_bowMenu != null) { _bowMenu.Destroy(); _bowMenu = null; }
            if (_bowString != null) { UnityEngine.Object.Destroy(_bowString.gameObject); _bowString = null; }
            if (_arrowGo != null) { UnityEngine.Object.Destroy(_arrowGo); _arrowGo = null; }
            _arrowTried = false;
            _arrowHand = -1;
            _nocked = false;
            _draw = 0f;
            _bowSel = -1;
            _flyT = -1f;
            _stringFor = null;
            _strA = _strB = null;
            _bowAnim = null;
        }
    }
}
