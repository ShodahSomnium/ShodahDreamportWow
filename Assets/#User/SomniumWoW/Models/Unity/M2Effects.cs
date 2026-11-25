using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    // M2 particle emitters, ribbons and material (colour / transparency / texture transform) animation on the Unity side.
    // The data comes from the pure reader (Models/M2Emitters.cs, M2MeshData.Describe); M2Factory.Build / BuildStatic call
    // AttachAnimated / AttachStatic; M2EffectsRunner (one hidden object) drives everything at runtime.
    //
    // Emitter -> Unity ParticleSystem mapping (WoW semantics from wowdev "M2 particles" and WebWowViewerCpp
    // CParticleGenerator / CPlaneGenerator / CSphereGenerator / particleEmitter.cpp):
    //   rate (per s, animated track)          -> emission.rateOverTime (x quality), 0 while the EnabledIn track is 0
    //   lifespan +- lifespanVary              -> main.startLifetime (two constants)
    //   speed x (1 +- speedVariation)         -> main.startSpeed (yards -> metres)
    //   direction: cone around the emitter's +Z (bone space), polar = verticalRange, azimuth = horizontalRange
    //                                         -> shape Cone (angle = verticalRange, arc = 2 x horizontalRange), Hemisphere /
    //                                            Sphere when verticalRange reaches 90 / 180 degrees; plane area -> cone base
    //                                            ellipse (areaLength x areaWidth); sphere emitters: radius min..max
    //   gravity (yards/s^2, +Z down)          -> main.gravityModifier (g x 0.9144 / 9.81; negative = rising)
    //   drag                                  -> limitVelocityOverLifetime.drag
    //   colour / alpha / scale part tracks    -> colorOverLifetime gradient, sizeOverLifetime curves (quad = 2 x scale)
    //   flipbook rows x cols + head cell track-> textureSheetAnimation (random texture flag = random frame)
    //   base spin / spin (+- vary, negate)    -> startRotation / rotationOverLifetime
    //   flag 0x10 model space                 -> simulationSpace Local (else World); flag 0x1000 XY quad -> horizontal billboard
    //   blend mode                            -> SomniumWoW/Particle material state (same table as M2 materials)
    // Not reproduced: tails (headOrTail is 0 in every 3.3.5a emitter, measured), twinkle, wind, spline paths, model-spawning
    // emitters, creature colour replacement (ParticleColor.dbc), ground projection.
    public static class M2Effects
    {
        public const string ShaderName = "SomniumWoW/Particle";
        const float Yd = 0.9144f;

        static Shader _shader;
        static bool _shaderWarned;
        static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        // Totals for the log line.
        internal static int StaticInstances, AnimatedInstances, EmittersBuilt, RibbonsBuilt, AnimatedMaterials;

        // ------------------------------------------------------------------ settings

        /// <summary>Emission rate factor per WowSettings.EffectsQuality (0 low, 1 medium, 2 high).</summary>
        internal static float RateScale { get { int q = Quality; return q <= 0 ? 0.35f : q == 1 ? 0.65f : 1f; } }
        /// <summary>Particle cap per emitter per quality.</summary>
        internal static int ParticleCap { get { int q = Quality; return q <= 0 ? 40 : q == 1 ? 120 : 300; } }
        /// <summary>Distance (metres) beyond which world-prop emitters stop, per quality.</summary>
        internal static float WorldDistance { get { int q = Quality; return q <= 0 ? 30f : q == 1 ? 50f : 80f; } }
        internal static int Quality { get { return Mathf.Clamp(WowSettings.EffectsQuality, 0, 2); } }

        static Shader Shader
        {
            get
            {
                if (_shader != null) return _shader;
                _shader = WowShaders.Find(ShaderName);
                if (_shader == null)
                {
                    if (!_shaderWarned) { _shaderWarned = true; WowLog.Warn("M2Effects: shader " + ShaderName + " missing (carrier WowParticle.mat not carried?) - using URP Particles/Unlit, blend modes approximate"); }
                    _shader = WowShaders.Find("Universal Render Pipeline/Particles/Unlit");
                }
                return _shader;
            }
        }

        /// <summary>Particle / ribbon material for a WoW blend mode (shared per texture + blend + fog + uv convention).</summary>
        internal static Material MaterialFor(Texture tex, int blend, bool unfogged, int priorityPlane, bool unityUv)
        {
            string key = (tex != null ? tex.GetInstanceID() : 0) + "|" + blend + "|" + (unfogged ? 1 : 0) + "|" + Mathf.Clamp(priorityPlane, -20, 20) + "|" + (unityUv ? 1 : 0);
            Material m;
            if (_materials.TryGetValue(key, out m) && m != null) return m;
            var sh = Shader;
            if (sh == null) return null;
            m = new Material(sh) { name = "WowParticle " + blend, enableInstancing = true };
            if (tex != null) m.SetTexture("_MainTex", tex);
            // Unity's particle / trail UVs have v = 0 at the bottom of a frame; WoW textures are uploaded as stored (row 0 at
            // v = 0, PLAN rule 15), so flip v: frame 0 of Unity's sheet (top row) = WoW cell 0 (first rows of the image).
            if (unityUv) m.SetTextureScale("_MainTex", new Vector2(1f, -1f));
            if (unityUv) m.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
            BlendMode src = BlendMode.One, dst = BlendMode.Zero;
            float cutoff = -1f, fog = 0f, mod = 0f;
            switch (blend)
            {
                case 0: cutoff = -1f; break;                                                             // Opaque (still in the transparent queue)
                case 1: cutoff = 0.502f; break;                                                          // AlphaKey
                case 2: src = BlendMode.SrcAlpha; dst = BlendMode.OneMinusSrcAlpha; break;               // Alpha
                case 3: src = BlendMode.One; dst = BlendMode.One; fog = 1f; mod = 3f; break;             // NoAlphaAdd
                case 4: src = BlendMode.SrcAlpha; dst = BlendMode.One; fog = 1f; break;                  // Add
                case 5: src = BlendMode.DstColor; dst = BlendMode.Zero; fog = 2f; mod = 1f; break;       // Mod
                case 6: src = BlendMode.DstColor; dst = BlendMode.SrcColor; fog = 3f; mod = 2f; break;   // Mod2x
                case 7: src = BlendMode.One; dst = BlendMode.OneMinusSrcAlpha; fog = 1f; mod = 3f; break; // BlendAdd
                default: src = BlendMode.SrcAlpha; dst = BlendMode.One; fog = 1f; break;
            }
            if (unfogged) fog = 4f;
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_FogMode", fog);
            m.SetFloat("_ModMode", mod);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.Transparent + Mathf.Clamp(priorityPlane, -20, 20) + 1;
            m.SetOverrideTag("RenderType", "Transparent");
            _materials[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ static doodads (rest pose)

        /// <summary>Emitters of a static doodad placed at the rest pose (Unity, local to the doodad root). Main thread.</summary>
        internal sealed class StaticFx
        {
            public M2Model Model;
            public int RestSeq, RestDuration;
            public Vector3[] PartPos; public Quaternion[] PartRot; public float[] PartScale;
            public Vector3[] RibPos; public Quaternion[] RibRot; public float[] RibScale;
            public Material[] PartMat, RibMat;
        }

        /// <summary>Rest-pose placement of every emitter (null when the model has none). mats = M2Pose matrices or null (identity).</summary>
        internal static StaticFx PrepareStatic(WowFiles files, M2Model m, int restSeq, float[] mats)
        {
            if (m == null || !m.HasEmitters) return null;
            var fx = new StaticFx { Model = m, RestSeq = restSeq };
            fx.RestDuration = restSeq >= 0 && restSeq < m.Sequences.Length ? Math.Max(1, m.Sequences[restSeq].Duration) : 1000;
            int np = m.Particles.Length, nr = m.Ribbons.Length;
            fx.PartPos = new Vector3[np]; fx.PartRot = new Quaternion[np]; fx.PartScale = new float[np]; fx.PartMat = new Material[np];
            for (int i = 0; i < np; i++)
            {
                var e = m.Particles[i];
                Place(m, mats, e.Bone, e.Position, out fx.PartPos[i], out fx.PartRot[i], out fx.PartScale[i]);
                fx.PartMat[i] = ParticleMaterial(files, e);
            }
            fx.RibPos = new Vector3[nr]; fx.RibRot = new Quaternion[nr]; fx.RibScale = new float[nr]; fx.RibMat = new Material[nr];
            for (int i = 0; i < nr; i++)
            {
                var r = m.Ribbons[i];
                Place(m, mats, r.Bone, r.Position, out fx.RibPos[i], out fx.RibRot[i], out fx.RibScale[i]);
                fx.RibMat[i] = RibbonMaterial(files, r);
            }
            return fx;
        }

        static void Place(M2Model m, float[] mats, int bone, M2Vec3 p, out Vector3 pos, out Quaternion rot, out float scale)
        {
            if (mats == null || bone < 0 || bone * 16 + 15 >= mats.Length)
            {
                pos = WowSpace.PointToUnity(p.X, p.Y, p.Z);
                rot = Quaternion.Euler(-90f, 0f, 0f);
                scale = 1f;
                return;
            }
            int o = bone * 16;   // column-major: x' = M[0]x + M[4]y + M[8]z + M[12]
            float px = mats[o] * p.X + mats[o + 4] * p.Y + mats[o + 8] * p.Z + mats[o + 12];
            float py = mats[o + 1] * p.X + mats[o + 5] * p.Y + mats[o + 9] * p.Z + mats[o + 13];
            float pz = mats[o + 2] * p.X + mats[o + 6] * p.Y + mats[o + 10] * p.Z + mats[o + 14];
            pos = WowSpace.PointToUnity(px, py, pz);
            Vector3 up = WowSpace.DirToUnity(mats[o + 8], mats[o + 9], mats[o + 10]);        // bone +Z
            Vector3 back = WowSpace.DirToUnity(-mats[o], -mats[o + 1], -mats[o + 2]);         // bone -X
            scale = up.magnitude;
            if (scale < 1e-4f || back.sqrMagnitude < 1e-8f) { rot = Quaternion.Euler(-90f, 0f, 0f); scale = 1f; return; }
            rot = Quaternion.LookRotation(up / scale, back.normalized);
        }

        /// <summary>Instantiates the doodad's emitters under a child "fx" of the static instance.</summary>
        internal static void AttachStatic(GameObject go, StaticFx fx)
        {
            if (go == null || fx == null) return;
            var container = new GameObject("fx");
            container.SetActive(false);
            container.transform.SetParent(go.transform, false);
            var inst = new M2FxInstance { Root = go.transform, Container = container, Model = fx.Model, World = true, RestSeq = fx.RestSeq, RestDuration = fx.RestDuration };
            var list = new List<M2FxEmitter>();
            for (int i = 0; i < fx.Model.Particles.Length; i++)
            {
                var e = fx.Model.Particles[i];
                if (fx.PartMat[i] == null || !Usable(e)) continue;
                var t = new GameObject("p" + i).transform;
                t.SetParent(container.transform, false);
                t.localPosition = fx.PartPos[i]; t.localRotation = fx.PartRot[i]; t.localScale = Vector3.one * fx.PartScale[i];
                list.Add(MakeParticles(t.gameObject, fx.Model, e, fx.PartMat[i], fx.RestSeq, true));
            }
            inst.Emitters = list.ToArray();
            var ribs = new List<M2FxRibbon>();
            for (int i = 0; i < fx.Model.Ribbons.Length; i++)
            {
                if (fx.RibMat[i] == null) continue;
                var t = new GameObject("r" + i).transform;
                t.SetParent(container.transform, false);
                t.localPosition = fx.RibPos[i]; t.localRotation = fx.RibRot[i]; t.localScale = Vector3.one * fx.RibScale[i];
                ribs.Add(MakeRibbon(t.gameObject, fx.Model, fx.Model.Ribbons[i], fx.RibMat[i], fx.RestSeq));
            }
            inst.Ribbons = ribs.ToArray();
            if (inst.Emitters.Length == 0 && inst.Ribbons.Length == 0) { UnityEngine.Object.Destroy(container); return; }
            inst.Active = false;   // the runner activates it when in range and enabled
            StaticInstances++;
            M2EffectsRunner.Register(inst);
        }

        // ------------------------------------------------------------------ animated models

        /// <summary>
        /// Emitters parented to the bones of an animated build, and material animation for its animated batches
        /// (MaterialPropertyBlock per material index on the renderer). Nothing is registered when there is nothing to drive.
        /// </summary>
        internal static void AttachAnimated(GameObject root, M2Model m, Transform[] bones, M2Animator anim, WowFiles files, Renderer renderer, List<M2BatchInfo> batches)
        {
            if (root == null || m == null) return;
            var inst = new M2FxInstance { Root = root.transform, Model = m, Anim = anim, World = false };
            var list = new List<M2FxEmitter>();
            for (int i = 0; i < m.Particles.Length; i++)
            {
                var e = m.Particles[i];
                if (!Usable(e)) continue;
                var mat = ParticleMaterial(files, e);
                if (mat == null) continue;
                var t = new GameObject("fx_p" + i).transform;
                t.gameObject.SetActive(false);
                ParentToBone(t, root.transform, bones, m, e.Bone, e.Position);
                int seq = anim != null ? anim.DataSequence : -1;
                list.Add(MakeParticles(t.gameObject, m, e, mat, seq, false));
            }
            inst.Emitters = list.ToArray();
            var ribs = new List<M2FxRibbon>();
            for (int i = 0; i < m.Ribbons.Length; i++)
            {
                var r = m.Ribbons[i];
                var mat = RibbonMaterial(files, r);
                if (mat == null) continue;
                var t = new GameObject("fx_r" + i).transform;
                t.gameObject.SetActive(false);
                ParentToBone(t, root.transform, bones, m, r.Bone, r.Position);
                ribs.Add(MakeRibbon(t.gameObject, m, r, mat, anim != null ? anim.DataSequence : -1));
            }
            inst.Ribbons = ribs.ToArray();
            var mats = new List<M2FxMaterial>();
            if (renderer != null && batches != null)
                for (int i = 0; i < batches.Count; i++)
                    if (batches[i].Animated) mats.Add(new M2FxMaterial { B = batches[i], Index = i });
            if (mats.Count > 0) { inst.Renderer = renderer; inst.Materials = mats.ToArray(); AnimatedMaterials += mats.Count; }
            if (inst.Emitters.Length == 0 && inst.Ribbons.Length == 0 && inst.Materials == null) return;
            inst.Active = false;
            AnimatedInstances++;
            M2EffectsRunner.Register(inst);
        }

        static void ParentToBone(Transform t, Transform root, Transform[] bones, M2Model m, int bone, M2Vec3 p)
        {
            Vector3 ep = WowSpace.PointToUnity(p.X, p.Y, p.Z);
            if (bones != null && bone >= 0 && bone < bones.Length && bones[bone] != null)
            {
                var pv = m.Bones[bone].Pivot;
                t.SetParent(bones[bone], false);
                t.localPosition = ep - WowSpace.PointToUnity(pv.X, pv.Y, pv.Z);
            }
            else { t.SetParent(root, false); t.localPosition = ep; }
            t.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // local +Z (Unity cone axis) = the bone's WoW +Z
        }

        /// <summary>Emitters we can draw: a texture from the model's texture table (not replaceable), finite values.</summary>
        static bool Usable(M2ParticleEmitter e)
        {
            return !string.IsNullOrEmpty(e.TextureFile) && e.TextureType == 0;
        }

        static Material ParticleMaterial(WowFiles files, M2ParticleEmitter e)
        {
            if (files == null || string.IsNullOrEmpty(e.TextureFile)) return null;
            var tex = WowTextures.Get(files, e.TextureFile, WowTextureOptions.ForM2Flags((int)e.TextureFlags));
            return MaterialFor(tex, e.BlendMode, (e.Flags & M2ParticleEmitter.FlagUnfogged) != 0, e.PriorityPlane, true);
        }

        static Material RibbonMaterial(WowFiles files, M2RibbonEmitter r)
        {
            if (files == null || string.IsNullOrEmpty(r.TextureFile) || r.TextureType != 0) return null;
            var tex = WowTextures.Get(files, r.TextureFile, WowTextureOptions.ForM2Flags((int)r.TextureFlags));
            return MaterialFor(tex, r.BlendMode, (r.MaterialFlags & 0x02) != 0, r.PriorityPlane, true);
        }

        // ------------------------------------------------------------------ particle system setup

        static M2FxEmitter MakeParticles(GameObject go, M2Model m, M2ParticleEmitter e, Material mat, int seq, bool world)
        {
            var fe = new M2FxEmitter { E = e, Go = go };
            fe.Animated = M2Emitters.IsAnimated(e.EmissionRate) || M2Emitters.IsAnimated(e.EnabledIn) || M2Emitters.IsAnimated(e.EmissionSpeed)
                || M2Emitters.IsAnimated(e.Lifespan) || M2Emitters.IsAnimated(e.Gravity);
            var ps = go.AddComponent<ParticleSystem>();
            fe.Ps = ps;
            float life = Mathf.Max(0.05f, M2Emitters.At(e.Lifespan, m, seq, 0, 0, 1f));
            float rate = Mathf.Max(0f, M2Emitters.At(e.EmissionRate, m, seq, 0, 0, 0f));
            fe.MaxLife = Mathf.Max(life + Mathf.Abs(e.LifespanVary), MaxOf(e.Lifespan, life));
            fe.MaxRate = Mathf.Max(rate + Mathf.Abs(e.EmissionRateVary), MaxOf(e.EmissionRate, rate));

            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1f;
            main.prewarm = world;
            main.simulationSpace = e.InModelSpace ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startColor = Color.white;
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            ApplyTracks(fe, m, seq, 0, 0);
            ApplyCap(fe);

            // size: quad = 2 x scale yards (corners at centre +- scale); curves normalised by the largest key
            float smax = 0.001f;
            var sc = e.Scale;
            for (int i = 0; i < sc.Values.Length; i++) smax = Mathf.Max(smax, Mathf.Abs(sc.Values[i].X), Mathf.Abs(sc.Values[i].Y));
            if (sc.Values.Length == 0) smax = 1f;
            float full = 2f * smax * Yd;
            float vary = Mathf.Clamp01(Mathf.Abs(e.ScaleVary.X));
            main.startSize = vary > 0.001f ? new ParticleSystem.MinMaxCurve(full * (1f - vary), full * (1f + vary)) : new ParticleSystem.MinMaxCurve(full);
            if (sc.Values.Length > 1)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                bool sep = false;
                for (int i = 0; i < sc.Values.Length; i++) if (Mathf.Abs(sc.Values[i].X - sc.Values[i].Y) > 1e-4f) sep = true;
                sol.separateAxes = sep;
                var kx = new float[sc.Values.Length]; var ky = new float[sc.Values.Length];
                for (int i = 0; i < kx.Length; i++) { kx[i] = Mathf.Max(0f, sc.Values[i].X) / smax; ky[i] = Mathf.Max(0f, sc.Values[i].Y) / smax; }
                if (sep)
                {
                    sol.x = new ParticleSystem.MinMaxCurve(1f, Curve(sc.Times, kx));
                    sol.y = new ParticleSystem.MinMaxCurve(1f, Curve(sc.Times, ky));
                    sol.z = new ParticleSystem.MinMaxCurve(1f);
                }
                else sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(sc.Times, kx));
            }

            // colour + alpha over life
            if (e.Color.Values.Length > 0 || e.Alpha.Values.Length > 0)
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(Gradient(e));
            }

            // spin
            float bs = e.BaseSpin, bsv = Mathf.Abs(e.BaseSpinVary);
            if (Mathf.Abs(bs) > 1e-4f || bsv > 1e-4f) main.startRotation = new ParticleSystem.MinMaxCurve(bs - bsv, bs + bsv);
            float sp = e.Spin, spv = Mathf.Abs(e.SpinVary);
            if (Mathf.Abs(sp) > 1e-4f || spv > 1e-4f)
            {
                var rol = ps.rotationOverLifetime;
                rol.enabled = true;
                if ((e.Flags & M2ParticleEmitter.FlagNegateSpin) != 0) { float a = Mathf.Abs(sp) + spv; rol.z = new ParticleSystem.MinMaxCurve(-a, a); }
                else rol.z = new ParticleSystem.MinMaxCurve(sp - spv, sp + spv);
            }

            // drag
            if (e.Drag > 1e-4f)
            {
                var lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = new ParticleSystem.MinMaxCurve(100000f);
                lv.drag = new ParticleSystem.MinMaxCurve(Mathf.Min(e.Drag, 50f));
                lv.multiplyDragByParticleSize = false;
                lv.multiplyDragByParticleVelocity = false;
            }

            // flipbook
            int rows = Mathf.Max(1, e.Rows), cols = Mathf.Max(1, e.Cols), cells = rows * cols;
            if (cells > 1)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = cols;
                tsa.numTilesY = rows;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.cycleCount = 1;
                var hc = e.HeadCell;
                if ((e.Flags & M2ParticleEmitter.FlagRandomTexture) != 0 || hc.Values.Length == 0 && (e.Flags & M2ParticleEmitter.FlagRandFlipbookStart) != 0)
                    tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, (cells - 0.5f) / cells);
                else if (hc.Values.Length > 1)
                {
                    var v = new float[hc.Values.Length];
                    for (int i = 0; i < v.Length; i++) v[i] = (Mathf.Clamp(hc.Values[i], 0f, cells - 1) + 0.5f) / cells;
                    tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, Curve(hc.Times, v));
                }
                else
                {
                    float c0 = hc.Values.Length == 1 ? Mathf.Clamp(hc.Values[0], 0f, cells - 1) : 0f;
                    tsa.frameOverTime = new ParticleSystem.MinMaxCurve((c0 + 0.5f) / cells);
                }
            }

            // emission shape
            var sh = ps.shape;
            sh.enabled = true;
            float vr = Mathf.Abs(M2Emitters.At(e.VerticalRange, m, seq, 0, 0, 0f));
            float hr = Mathf.Abs(M2Emitters.At(e.HorizontalRange, m, seq, 0, 0, 0f));
            float al = Mathf.Abs(M2Emitters.At(e.AreaLength, m, seq, 0, 0, 0f)) * Yd;
            float aw = Mathf.Abs(M2Emitters.At(e.AreaWidth, m, seq, 0, 0, 0f)) * Yd;
            float arc = hr > 1e-4f ? Mathf.Min(360f, 2f * hr * Mathf.Rad2Deg) : 360f;
            sh.arc = arc;
            sh.arcMode = ParticleSystemShapeMultiModeValue.Random;
            if (e.IsSphere)
            {
                float rmax = Mathf.Max(al, aw), rmin = Mathf.Min(al, aw);
                sh.shapeType = vr >= 1.6f ? ParticleSystemShapeType.Sphere : vr >= 0.8f ? ParticleSystemShapeType.Hemisphere : ParticleSystemShapeType.Cone;
                if (sh.shapeType == ParticleSystemShapeType.Cone) sh.angle = Mathf.Clamp(vr * Mathf.Rad2Deg, 0f, 89f);
                sh.radius = Mathf.Max(0.001f, rmax);
                sh.radiusThickness = rmax > 1e-4f ? Mathf.Clamp01((rmax - rmin) / rmax) : 1f;
            }
            else
            {
                if (vr >= 1.6f) { sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = Mathf.Max(0.001f, 0.5f * Mathf.Max(al, aw)); }
                else if (vr >= 1.55f) { sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = Mathf.Max(0.001f, 0.5f * Mathf.Max(al, aw)); }
                else
                {
                    sh.shapeType = ParticleSystemShapeType.Cone;
                    sh.angle = Mathf.Clamp(vr * Mathf.Rad2Deg, 0f, 89f);
                    sh.radius = 0.5f;
                    // cone local axes: X = WoW -Y (areaWidth), Y = WoW -X (areaLength)
                    sh.scale = new Vector3(Mathf.Max(0.002f, aw), Mathf.Max(0.002f, al), 1f);
                }
                sh.radiusThickness = 1f;
            }

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = (e.Flags & M2ParticleEmitter.FlagXYQuad) != 0 ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.lightProbeUsage = LightProbeUsage.Off;
            pr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            pr.maxParticleSize = 3f;
            pr.alignment = ParticleSystemRenderSpace.View;
            EmittersBuilt++;
            return fe;
        }

        static float MaxOf(M2Track<float> tr, float fallback)
        {
            float mx = fallback;
            if (tr == null) return mx;
            for (int s = 0; s < tr.Values.Length; s++)
            {
                var v = tr.Values[s];
                if (v == null) continue;
                for (int k = 0; k < v.Length; k++) if (v[k] > mx) mx = v[k];
            }
            return mx;
        }

        /// <summary>Per-sequence values: rate (x quality, EnabledIn), speed, lifetime, gravity. No allocations.</summary>
        internal static void ApplyTracks(M2FxEmitter fe, M2Model m, int seq, int timeMs, long globalMs)
        {
            var e = fe.E;
            var ps = fe.Ps;
            if (ps == null) return;
            var main = ps.main;
            float life = Mathf.Max(0.05f, M2Emitters.At(e.Lifespan, m, seq, timeMs, globalMs, 1f));
            float lv = Mathf.Min(Mathf.Abs(e.LifespanVary), life * 0.95f);
            main.startLifetime = lv > 1e-3f ? new ParticleSystem.MinMaxCurve(life - lv, life + lv) : new ParticleSystem.MinMaxCurve(life);
            float speed = M2Emitters.At(e.EmissionSpeed, m, seq, timeMs, globalMs, 0f) * Yd;
            float sv = Mathf.Clamp01(Mathf.Abs(M2Emitters.At(e.SpeedVariation, m, seq, timeMs, globalMs, 0f)));
            main.startSpeed = sv > 1e-3f ? new ParticleSystem.MinMaxCurve(speed * (1f - sv), speed * (1f + sv)) : new ParticleSystem.MinMaxCurve(speed);
            var g = M2Emitters.At(e.Gravity, m, seq, timeMs, globalMs, new M2Vec3(0f, 0f, 0f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-g.Z * Yd / 9.81f);
            float rate = Mathf.Max(0f, M2Emitters.At(e.EmissionRate, m, seq, timeMs, globalMs, 0f));
            if (!e.EnabledIn.IsEmpty && M2Emitters.At(e.EnabledIn, m, seq, timeMs, globalMs, 1f) < 0.5f) rate = 0f;
            fe.Rate = rate;
            var em = ps.emission;
            em.rateOverTimeMultiplier = rate * RateScale;
        }

        internal static void ApplyCap(M2FxEmitter fe)
        {
            if (fe.Ps == null) return;
            var main = fe.Ps.main;
            int want = Mathf.CeilToInt(fe.MaxRate * RateScale * fe.MaxLife * 1.3f) + 4;
            main.maxParticles = Mathf.Clamp(want, 4, ParticleCap);
            var em = fe.Ps.emission;
            em.rateOverTimeMultiplier = fe.Rate * RateScale;
        }

        static Gradient Gradient(M2ParticleEmitter e)
        {
            var g = new Gradient();
            var c = e.Color; var a = e.Alpha;
            int nc = Mathf.Min(8, c.Values.Length), na = Mathf.Min(8, a.Values.Length);
            var ck = new GradientColorKey[Mathf.Max(1, nc)];
            var ak = new GradientAlphaKey[Mathf.Max(1, na)];
            if (nc == 0) ck[0] = new GradientColorKey(Color.white, 0f);
            for (int i = 0; i < nc; i++)
                ck[i] = new GradientColorKey(new Color(Mathf.Clamp01(c.Values[i].X / 255f), Mathf.Clamp01(c.Values[i].Y / 255f), Mathf.Clamp01(c.Values[i].Z / 255f)), Mathf.Clamp01(c.Times[i]));
            if (na == 0) ak[0] = new GradientAlphaKey(1f, 0f);
            for (int i = 0; i < na; i++) ak[i] = new GradientAlphaKey(Mathf.Clamp01(a.Values[i]), Mathf.Clamp01(a.Times[i]));
            g.SetKeys(ck, ak);
            return g;
        }

        /// <summary>Piecewise-linear AnimationCurve (keys at equal times nudged apart so Unity keeps both).</summary>
        static AnimationCurve Curve(float[] times, float[] values)
        {
            int n = Math.Min(times.Length, values.Length);
            var t = new float[n];
            for (int i = 0; i < n; i++)
            {
                t[i] = Mathf.Clamp01(times[i]);
                if (i > 0 && t[i] <= t[i - 1]) t[i] = t[i - 1] + 1e-4f;
            }
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float inT = i > 0 ? (values[i] - values[i - 1]) / Mathf.Max(1e-5f, t[i] - t[i - 1]) : 0f;
                float outT = i < n - 1 ? (values[i + 1] - values[i]) / Mathf.Max(1e-5f, t[i + 1] - t[i]) : 0f;
                keys[i] = new Keyframe(t[i], values[i], inT, outT);
            }
            return new AnimationCurve(keys);
        }

        // ------------------------------------------------------------------ ribbons (TrailRenderer, camera-facing)

        static M2FxRibbon MakeRibbon(GameObject go, M2Model m, M2RibbonEmitter r, Material mat, int seq)
        {
            var fr = new M2FxRibbon { R = r, Go = go };
            fr.Animated = M2Emitters.IsAnimated(r.Color) || M2Emitters.IsAnimated(r.Alpha) || M2Emitters.IsAnimated(r.HeightAbove)
                || M2Emitters.IsAnimated(r.HeightBelow) || M2Emitters.IsAnimated(r.Visibility);
            var tr = go.AddComponent<TrailRenderer>();
            fr.Trail = tr;
            tr.sharedMaterial = mat;
            tr.time = Mathf.Clamp(r.EdgeLifetime, 0.05f, 5f);
            tr.minVertexDistance = 0.05f;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.numCapVertices = 0;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.lightProbeUsage = LightProbeUsage.Off;
            tr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            tr.autodestruct = false;
            ApplyRibbon(fr, m, seq, 0, 0);
            RibbonsBuilt++;
            return fr;
        }

        internal static void ApplyRibbon(M2FxRibbon fr, M2Model m, int seq, int timeMs, long globalMs)
        {
            var r = fr.R; var tr = fr.Trail;
            if (tr == null) return;
            float above = M2Emitters.At(r.HeightAbove, m, seq, timeMs, globalMs, 0.1f);
            float below = M2Emitters.At(r.HeightBelow, m, seq, timeMs, globalMs, 0.1f);
            tr.widthMultiplier = Mathf.Max(0.01f, (Mathf.Abs(above) + Mathf.Abs(below)) * Yd);
            var c = M2Emitters.At(r.Color, m, seq, timeMs, globalMs, new M2Vec3(1f, 1f, 1f));
            float a = Mathf.Clamp01(M2Emitters.At(r.Alpha, m, seq, timeMs, globalMs, 1f));
            var col = new Color(Mathf.Clamp01(c.X), Mathf.Clamp01(c.Y), Mathf.Clamp01(c.Z), a);
            tr.startColor = col;
            tr.endColor = new Color(col.r, col.g, col.b, 0f);
            bool vis = r.Visibility.IsEmpty || M2Emitters.At(r.Visibility, m, seq, timeMs, globalMs, 1f) >= 0.5f;
            tr.emitting = vis && a > 0.003f;
        }

        // ------------------------------------------------------------------ material animation (pure evaluation + apply)

        static readonly int ColorId = UnityEngine.Shader.PropertyToID("_Color");
        static readonly int StId = UnityEngine.Shader.PropertyToID("_MainTex_ST");
        static readonly int ScrollId = UnityEngine.Shader.PropertyToID("_UvScroll");
        static MaterialPropertyBlock _mpb;

        /// <summary>Colour (rgb only for unlit batches, like M2Materials) x transparency, and the texture transform as _MainTex_ST.</summary>
        internal static void EvalBatch(M2Model m, M2BatchInfo b, int seq, int t, long g, out Color c, out Vector4 st, out bool texAnim)
        {
            float a = 1f, r = b.R, gr = b.G, bl = b.B;
            if (b.TransparencyIndex >= 0 && b.TransparencyIndex < m.Transparencies.Length)
                a *= Mathf.Clamp01(M2Emitters.At(m.Transparencies[b.TransparencyIndex], m, seq, t, g, 1f));
            if (b.ColorIndex >= 0 && b.ColorIndex < m.Colors.Length)
            {
                var mc = m.Colors[b.ColorIndex];
                a *= Mathf.Clamp01(M2Emitters.At(mc.Alpha, m, seq, t, g, 1f));
                if ((b.RenderFlags & 1) != 0)
                {
                    var v = M2Emitters.At(mc.Color, m, seq, t, g, new M2Vec3(1f, 1f, 1f));
                    r = Mathf.Clamp01(v.X); gr = Mathf.Clamp01(v.Y); bl = Mathf.Clamp01(v.Z);
                }
            }
            c = new Color(r, gr, bl, a);
            st = new Vector4(1f, 1f, 0f, 0f);
            texAnim = false;
            if (b.TextureTransformIndex >= 0 && b.TextureTransformIndex < m.TextureTransforms.Length)
            {
                var x = m.TextureTransforms[b.TextureTransformIndex];
                var tr = M2Emitters.At(x.Translation, m, seq, t, g, new M2Vec3(0f, 0f, 0f));
                var sc = M2Emitters.At(x.Scale, m, seq, t, g, new M2Vec3(1f, 1f, 1f));
                // uv' = (uv - 0.5) x scale + 0.5 + translation (pivot at the texture centre); translation wrapped to 0..1
                float tx = tr.X - Mathf.Floor(tr.X), ty = tr.Y - Mathf.Floor(tr.Y);
                st = new Vector4(sc.X, sc.Y, 0.5f - 0.5f * sc.X + tx, 0.5f - 0.5f * sc.Y + ty);
                texAnim = true;
            }
        }

        internal static void ApplyMaterials(M2FxInstance inst, int seq, int t, long g)
        {
            var r = inst.Renderer;
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < inst.Materials.Length; i++)
            {
                var fm = inst.Materials[i];
                Color c; Vector4 st; bool texAnim;
                EvalBatch(inst.Model, fm.B, seq, t, g, out c, out st, out texAnim);
                _mpb.Clear();
                _mpb.SetColor(ColorId, c);
                if (texAnim) { _mpb.SetVector(StId, st); _mpb.SetVector(ScrollId, Vector4.zero); }
                r.SetPropertyBlock(_mpb, fm.Index);
            }
        }

        /// <summary>Static doodads: an animated batch gets its own material (animated once per frame for every instance).</summary>
        internal static void RegisterSharedMaterial(M2Model m, int restSeq, M2BatchInfo b, Material mat)
        {
            if (m == null || b == null || mat == null) return;
            int dur = restSeq >= 0 && restSeq < m.Sequences.Length ? Math.Max(1, m.Sequences[restSeq].Duration) : 1000;
            if (b.TextureTransformIndex >= 0) mat.SetVector(ScrollId, Vector4.zero);
            AnimatedMaterials++;
            M2EffectsRunner.RegisterMaterial(new M2FxSharedMaterial { Model = m, RestSeq = restSeq, Duration = dur, B = b, Mat = mat });
        }

        internal static void ApplyShared(M2FxSharedMaterial sm, long globalMs)
        {
            Color c; Vector4 st; bool texAnim;
            EvalBatch(sm.Model, sm.B, sm.RestSeq, (int)(globalMs % sm.Duration), globalMs, out c, out st, out texAnim);
            sm.Mat.SetColor(ColorId, c);
            if (texAnim) sm.Mat.SetVector(StId, st);
        }

        public static string StatsLine()
        {
            return "M2Effects: " + StaticInstances + " static + " + AnimatedInstances + " animated instances, " + EmittersBuilt + " particle systems, "
                + RibbonsBuilt + " ribbons, " + AnimatedMaterials + " animated materials, " + _materials.Count + " particle materials, quality "
                + Quality + (WowSettings.WorldParticles ? "" : " (world particles off)") + (_shader != null ? ", shader " + _shader.name : "");
        }
    }

    // ---------------------------------------------------------------------- runtime records (driven by M2EffectsRunner)

    internal sealed class M2FxEmitter
    {
        public M2ParticleEmitter E;
        public ParticleSystem Ps;
        public GameObject Go;
        public bool Animated;
        public float Rate, MaxRate, MaxLife;
    }

    internal sealed class M2FxRibbon
    {
        public M2RibbonEmitter R;
        public TrailRenderer Trail;
        public GameObject Go;
        public bool Animated;
    }

    internal sealed class M2FxMaterial
    {
        public M2BatchInfo B;
        public int Index;
    }

    internal sealed class M2FxSharedMaterial
    {
        public M2Model Model;
        public int RestSeq, Duration;
        public M2BatchInfo B;
        public Material Mat;
    }

    internal sealed class M2FxInstance
    {
        public Transform Root;
        public GameObject Container;       // static doodads: every emitter under it (one SetActive culls all)
        public M2Model Model;
        public M2Animator Anim;            // animated builds (time source), null for static doodads
        public bool World;                 // a world prop (WowSettings.WorldParticles applies)
        public int RestSeq, RestDuration;
        public M2FxEmitter[] Emitters = new M2FxEmitter[0];
        public M2FxRibbon[] Ribbons = new M2FxRibbon[0];
        public Renderer Renderer;
        public M2FxMaterial[] Materials;
        public bool Active;
        public float NextCull, NextTracks;
        public int LastSeq = -2;
    }
}
