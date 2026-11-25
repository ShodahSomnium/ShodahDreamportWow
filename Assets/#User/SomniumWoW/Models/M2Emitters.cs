using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    // Pure C# particle / ribbon emitter reader for 3.3.5a M2 (v264). Layout: wowdev wiki "M2" (M2ParticleOld with the
    // Wrath+ fields, M2Ribbon), cross-checked with WebWowViewerCpp (Deamon87, wowViewerLib/src/engine/managers/particles:
    // CParticleGenerator, CPlaneGenerator, CSphereGenerator, particleEmitter.cpp, animationManager.cpp calcParticleEmitters),
    // and MEASURED on the real files by the harness probe VfxEmitters (see the VFX report: array sizes, index ranges, value
    // census). Everything is in WoW model space (yards, X forward, Y left, Z up); the Unity side converts through WowSpace.

    /// <summary>A 2-float vector (particle scale x/y).</summary>
    public struct M2Vec2
    {
        public float X, Y;
        public M2Vec2(float x, float y) { X = x; Y = y; }
    }

    /// <summary>
    /// A particle "fake animation block" (Wrath FBlock / M2PartTrack): keys over the particle's life. Times are fixed16
    /// fractions of the lifespan (stored 0..32767, kept here as 0..1), always inside the .m2 itself.
    /// </summary>
    public sealed class M2PartTrack<T>
    {
        public float[] Times = new float[0];
        public T[] Values = new T[0];
        public bool IsEmpty { get { return Values.Length == 0; } }
    }

    /// <summary>One particle emitter (M2ParticleOld, 476 bytes in v264; offsets in comments are within the record).</summary>
    public sealed class M2ParticleEmitter
    {
        public const int Size = 476;

        // Flags (wowdev "Particle Flags"; WebWowViewerCpp particleEmitter.cpp tests them the same way)
        public const uint FlagShaded = 0x1, FlagSort = 0x2, FlagVelocityOrient = 0x4, FlagUnfogged = 0x8,
                          FlagModelSpace = 0x10,      // particles stay in the emitter's (bone) space; clear = released into world space
                          FlagInheritScale = 0x20, FlagNegateSpin = 0x200, FlagClampTail = 0x400, FlagXYQuad = 0x1000,
                          FlagProjectGround = 0x2000, FlagRandomTexture = 0x10000, FlagUnscaledSizeVary = 0x80000,
                          FlagRandFlipbookStart = 0x200000, FlagCompressedGravity = 0x800000, FlagLodIgnoreDistance = 0x4000000;

        public int Index;
        public int Id;                      // 0x00 (usually -1)
        public uint Flags;                  // 0x04
        public M2Vec3 Position;             // 0x08, relative to the bone's pivot frame (model space at bind pose)
        public int Bone;                    // 0x14
        public int Texture;                 // 0x16 index into M2Model.Textures
        public string ModelFile;            // 0x18 the emitter spawns models (not drawn by us)
        public string ChildEmittersFile;    // 0x20 alias for another model's emitters (not drawn by us)
        public int BlendMode;               // 0x28 same table as M2 materials (WebWowViewer ParticleBlendingModeToEGxBlendEnum)
        public int EmitterType;             // 0x29 1 plane, 2 sphere, 3 spline, 4 bone
        public int ColorIndex;              // 0x2A ParticleColor.dbc (11..13 = creature colour replacement)
        public int ParticleType;            // 0x2C
        public int HeadOrTail;              // 0x2D 0 head (billboard), 1 tail (stretched along velocity), 2 both
        public int PriorityPlane;           // 0x2E
        public int Rows = 1, Cols = 1;      // 0x30, 0x32 texture flipbook
        public M2Track<float> EmissionSpeed, SpeedVariation, VerticalRange, HorizontalRange;   // 0x34 0x48 0x5C 0x70
        public M2Track<M2Vec3> Gravity;     // 0x84 as a vector: plain = (0, 0, -g); FlagCompressedGravity = the decompressed vector
        public M2Track<float> Lifespan;     // 0x98
        public float LifespanVary;          // 0xAC
        public M2Track<float> EmissionRate; // 0xB0
        public float EmissionRateVary;      // 0xC4
        public M2Track<float> AreaLength;   // 0xC8 plane: size along X; sphere: minimum radius (generator emissionAreaX)
        public M2Track<float> AreaWidth;    // 0xDC plane: size along Y; sphere: maximum radius (generator emissionAreaY)
        public M2Track<float> ZSource;      // 0xF0 > 0: velocity points away from (0, 0, zSource)
        public M2PartTrack<M2Vec3> Color;   // 0x104 RGB 0..255
        public M2PartTrack<float> Alpha;    // 0x114 0..1 (fixed16)
        public M2PartTrack<M2Vec2> Scale;   // 0x124 yards (half extent of the quad: corners at centre +- scale)
        public M2Vec2 ScaleVary;            // 0x134
        public M2PartTrack<float> HeadCell; // 0x13C flipbook cell over life
        public M2PartTrack<float> TailCell; // 0x14C
        public float TailLength;            // 0x15C seconds of velocity
        public float TwinkleSpeed, TwinklePercent, TwinkleMin, TwinkleMax;   // 0x160 0x164 0x168 0x16C
        public float InheritVelocityScale;  // 0x170
        public float Drag;                  // 0x174 velocity *= 1 - drag * dt
        public float BaseSpin, BaseSpinVary, Spin, SpinVary;   // 0x178.. radians, radians per second
        public M2Vec3 TumbleMin, TumbleMax; // 0x188
        public M2Vec3 Wind;                 // 0x1A0 added to the velocity per second
        public float WindTime;              // 0x1AC
        public float FollowSpeed1, FollowScale1, FollowSpeed2, FollowScale2;   // 0x1B0..
        public M2Vec3[] SplinePoints = new M2Vec3[0];   // 0x1C0
        public M2Track<float> EnabledIn;    // 0x1C8 uint8 (0 = off in this sequence)

        // Resolved texture (from M2Model.Textures[Texture])
        public int TextureType = -1;
        public string TextureFile;
        public uint TextureFlags;

        public bool InModelSpace { get { return (Flags & FlagModelSpace) != 0; } }
        public bool IsSphere { get { return EmitterType == 2; } }
    }

    /// <summary>One ribbon emitter (M2Ribbon, 176 bytes in v264).</summary>
    public sealed class M2RibbonEmitter
    {
        public const int Size = 176;

        public int Index;
        public int Id;                      // 0x00
        public int Bone;                    // 0x04
        public M2Vec3 Position;             // 0x08
        public int[] TextureIndices = new int[0];    // 0x14
        public int[] MaterialIndices = new int[0];   // 0x1C into M2Model.Materials (blend mode, flags)
        public M2Track<M2Vec3> Color;       // 0x24 0..1
        public M2Track<float> Alpha;        // 0x38 fixed16
        public M2Track<float> HeightAbove;  // 0x4C yards along the bone's +Z
        public M2Track<float> HeightBelow;  // 0x60
        public float EdgesPerSecond;        // 0x74
        public float EdgeLifetime;          // 0x78 seconds
        public float Gravity;               // 0x7C
        public int Rows = 1, Cols = 1;      // 0x80, 0x82
        public M2Track<float> TexSlot;      // 0x84 uint16
        public M2Track<float> Visibility;   // 0x98 uint8
        public int PriorityPlane;           // 0xAC

        public string TextureFile;
        public int TextureType = -1;
        public uint TextureFlags;
        public int BlendMode;
        public int MaterialFlags;
    }

    /// <summary>Reads the emitters of a parsed model (called by M2Model.Parse). Bad records are dropped with a warning.</summary>
    public static class M2Emitters
    {
        internal static void Read(M2Reader r, M2Model m)
        {
            int n, o;
            var parts = new List<M2ParticleEmitter>();
            if (r.Arr(0x128, M2ParticleEmitter.Size, out n, out o))
            {
                for (int i = 0; i < n; i++)
                {
                    var p = ReadParticle(r, m, o + i * M2ParticleEmitter.Size, i);
                    if (p != null) parts.Add(p);
                }
            }
            m.Particles = parts.ToArray();

            var ribs = new List<M2RibbonEmitter>();
            if (r.Arr(0x120, M2RibbonEmitter.Size, out n, out o))
            {
                for (int i = 0; i < n; i++)
                {
                    var rb = ReadRibbon(r, m, o + i * M2RibbonEmitter.Size, i);
                    if (rb != null) ribs.Add(rb);
                }
            }
            m.Ribbons = ribs.ToArray();
        }

        static float F32(byte[] s, int o) { return BitConverter.ToSingle(s, o); }
        static float U16f(byte[] s, int o) { return BitConverter.ToUInt16(s, o); }
        static float U8f(byte[] s, int o) { return s[o]; }

        /// <summary>CompressedParticleGravity (int8 x, int8 y, int16 z), decompressed as wowdev / WebWowViewer do.</summary>
        static M2Vec3 CompressedGravity(byte[] s, int o)
        {
            float dx = (sbyte)s[o] / 128f, dy = (sbyte)s[o + 1] / 128f;
            float zz = 1f - (dx * dx + dy * dy);
            float z = zz > 0f ? (float)Math.Sqrt(zz) : 0f;
            float mag = BitConverter.ToInt16(s, o + 2) * 0.04238648f;
            if (mag < 0f) { z = -z; mag = -mag; }
            return new M2Vec3(dx * mag, dy * mag, z * mag);
        }

        static M2Vec3 PlainGravity(byte[] s, int o) { return new M2Vec3(0f, 0f, -BitConverter.ToSingle(s, o)); }

        static M2ParticleEmitter ReadParticle(M2Reader r, M2Model m, int b, int index)
        {
            var d = r.Bytes;
            var p = new M2ParticleEmitter { Index = index };
            p.Id = r.I32(b);
            p.Flags = r.U32(b + 0x04);
            p.Position = r.Vec3(b + 0x08);
            p.Bone = r.U16(b + 0x14);
            p.Texture = r.U16(b + 0x16);
            int sn, so;
            if (r.Arr(b + 0x18, 1, out sn, out so) && sn > 0) p.ModelFile = r.CString(so, sn);
            if (r.Arr(b + 0x20, 1, out sn, out so) && sn > 0) p.ChildEmittersFile = r.CString(so, sn);
            p.BlendMode = d[b + 0x28];
            p.EmitterType = d[b + 0x29];
            p.ColorIndex = r.U16(b + 0x2A);
            p.ParticleType = d[b + 0x2C];
            p.HeadOrTail = d[b + 0x2D];
            p.PriorityPlane = r.I16(b + 0x2E);
            p.Rows = Math.Max(1, (int)r.U16(b + 0x30));
            p.Cols = Math.Max(1, (int)r.U16(b + 0x32));
            p.EmissionSpeed = r.TrackOf<float>(b + 0x34, 4, F32);
            p.SpeedVariation = r.TrackOf<float>(b + 0x48, 4, F32);
            p.VerticalRange = r.TrackOf<float>(b + 0x5C, 4, F32);
            p.HorizontalRange = r.TrackOf<float>(b + 0x70, 4, F32);
            bool compressed = (p.Flags & M2ParticleEmitter.FlagCompressedGravity) != 0;
            p.Gravity = compressed ? r.TrackOf<M2Vec3>(b + 0x84, 4, CompressedGravity) : r.TrackOf<M2Vec3>(b + 0x84, 4, PlainGravity);
            p.Lifespan = r.TrackOf<float>(b + 0x98, 4, F32);
            p.LifespanVary = r.F32(b + 0xAC);
            p.EmissionRate = r.TrackOf<float>(b + 0xB0, 4, F32);
            p.EmissionRateVary = r.F32(b + 0xC4);
            p.AreaLength = r.TrackOf<float>(b + 0xC8, 4, F32);
            p.AreaWidth = r.TrackOf<float>(b + 0xDC, 4, F32);
            p.ZSource = r.TrackOf<float>(b + 0xF0, 4, F32);
            p.Color = Part<M2Vec3>(r, m, b + 0x104, 12, (s, o) => new M2Vec3(F32(s, o), F32(s, o + 4), F32(s, o + 8)));
            p.Alpha = Part<float>(r, m, b + 0x114, 2, (s, o) => BitConverter.ToInt16(s, o) / 32767f);
            p.Scale = Part<M2Vec2>(r, m, b + 0x124, 8, (s, o) => new M2Vec2(F32(s, o), F32(s, o + 4)));
            p.ScaleVary = new M2Vec2(r.F32(b + 0x134), r.F32(b + 0x138));
            p.HeadCell = Part<float>(r, m, b + 0x13C, 2, U16f);
            p.TailCell = Part<float>(r, m, b + 0x14C, 2, U16f);
            p.TailLength = r.F32(b + 0x15C);
            p.TwinkleSpeed = r.F32(b + 0x160);
            p.TwinklePercent = r.F32(b + 0x164);
            p.TwinkleMin = r.F32(b + 0x168);
            p.TwinkleMax = r.F32(b + 0x16C);
            p.InheritVelocityScale = r.F32(b + 0x170);
            p.Drag = r.F32(b + 0x174);
            p.BaseSpin = r.F32(b + 0x178);
            p.BaseSpinVary = r.F32(b + 0x17C);
            p.Spin = r.F32(b + 0x180);
            p.SpinVary = r.F32(b + 0x184);
            p.TumbleMin = r.Vec3(b + 0x188);
            p.TumbleMax = r.Vec3(b + 0x194);
            p.Wind = r.Vec3(b + 0x1A0);
            p.WindTime = r.F32(b + 0x1AC);
            p.FollowSpeed1 = r.F32(b + 0x1B0);
            p.FollowScale1 = r.F32(b + 0x1B4);
            p.FollowSpeed2 = r.F32(b + 0x1B8);
            p.FollowScale2 = r.F32(b + 0x1BC);
            if (r.Arr(b + 0x1C0, 12, out sn, out so) && sn > 0)
            {
                p.SplinePoints = new M2Vec3[sn];
                for (int i = 0; i < sn; i++) p.SplinePoints[i] = r.Vec3(so + i * 12);
            }
            p.EnabledIn = r.TrackOf<float>(b + 0x1C8, 1, U8f);

            if (p.Bone >= m.Bones.Length) { m.Warnings.Add("particle " + index + " bone " + p.Bone + " >= " + m.Bones.Length + ": dropped"); return null; }
            if (p.Texture < m.Textures.Length)
            {
                var t = m.Textures[p.Texture];
                p.TextureType = t.Type; p.TextureFile = t.FileName; p.TextureFlags = t.Flags;
            }
            else m.Warnings.Add("particle " + index + " texture " + p.Texture + " >= " + m.Textures.Length);
            return p;
        }

        static M2RibbonEmitter ReadRibbon(M2Reader r, M2Model m, int b, int index)
        {
            var rb = new M2RibbonEmitter { Index = index };
            rb.Id = r.I32(b);
            rb.Bone = r.I32(b + 0x04);
            rb.Position = r.Vec3(b + 0x08);
            rb.TextureIndices = U16Array(r, b + 0x14);
            rb.MaterialIndices = U16Array(r, b + 0x1C);
            rb.Color = r.TrackVec3(b + 0x24);
            rb.Alpha = r.TrackFixed16(b + 0x38);
            rb.HeightAbove = r.TrackOf<float>(b + 0x4C, 4, F32);
            rb.HeightBelow = r.TrackOf<float>(b + 0x60, 4, F32);
            rb.EdgesPerSecond = r.F32(b + 0x74);
            rb.EdgeLifetime = r.F32(b + 0x78);
            rb.Gravity = r.F32(b + 0x7C);
            rb.Rows = Math.Max(1, (int)r.U16(b + 0x80));
            rb.Cols = Math.Max(1, (int)r.U16(b + 0x82));
            rb.TexSlot = r.TrackOf<float>(b + 0x84, 2, U16f);
            rb.Visibility = r.TrackOf<float>(b + 0x98, 1, U8f);
            rb.PriorityPlane = r.I16(b + 0xAC);
            if (rb.Bone < 0 || rb.Bone >= m.Bones.Length) { m.Warnings.Add("ribbon " + index + " bone " + rb.Bone + " out of range: dropped"); return null; }
            if (rb.TextureIndices.Length > 0 && rb.TextureIndices[0] < m.Textures.Length)
            {
                var t = m.Textures[rb.TextureIndices[0]];
                rb.TextureType = t.Type; rb.TextureFile = t.FileName; rb.TextureFlags = t.Flags;
            }
            if (rb.MaterialIndices.Length > 0 && rb.MaterialIndices[0] < m.Materials.Length)
            {
                rb.BlendMode = m.Materials[rb.MaterialIndices[0]].BlendMode;
                rb.MaterialFlags = m.Materials[rb.MaterialIndices[0]].Flags;
            }
            return rb;
        }

        static int[] U16Array(M2Reader r, int o)
        {
            int n, off;
            if (!r.Arr(o, 2, out n, out off) || n == 0) return new int[0];
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = r.U16(off + i * 2);
            return a;
        }

        /// <summary>FBlock: {M2Array&lt;fixed16&gt; times, M2Array&lt;T&gt; values} (16 bytes), keys inside the .m2.</summary>
        static M2PartTrack<T> Part<T>(M2Reader r, M2Model m, int o, int valueSize, Func<byte[], int, T> read)
        {
            var t = new M2PartTrack<T>();
            int nt, ot, nv, ov;
            if (!r.Arr(o, 2, out nt, out ot) || !r.Arr(o + 8, valueSize, out nv, out ov)) return t;
            int k = Math.Min(nt, nv);
            if (k <= 0) return t;
            var d = r.Bytes;
            t.Times = new float[k];
            t.Values = new T[k];
            for (int i = 0; i < k; i++)
            {
                t.Times[i] = BitConverter.ToInt16(d, ot + i * 2) / 32767f;
                t.Values[i] = read(d, ov + i * valueSize);
            }
            return t;
        }

        // ------------------------------------------------------------------ evaluation helpers (pure)

        /// <summary>Linear interpolation of a float part track at life fraction t (0..1); fallback when empty.</summary>
        public static float Eval(M2PartTrack<float> tr, float t, float fallback)
        {
            if (tr == null || tr.Values.Length == 0) return fallback;
            int k = FindPart(tr.Times, t);
            if (k >= tr.Values.Length - 1 || t <= tr.Times[k]) return tr.Values[Math.Min(k, tr.Values.Length - 1)];
            float span = tr.Times[k + 1] - tr.Times[k];
            float f = span > 1e-6f ? (t - tr.Times[k]) / span : 0f;
            return tr.Values[k] + (tr.Values[k + 1] - tr.Values[k]) * f;
        }

        public static M2Vec3 Eval(M2PartTrack<M2Vec3> tr, float t, M2Vec3 fallback)
        {
            if (tr == null || tr.Values.Length == 0) return fallback;
            int k = FindPart(tr.Times, t);
            if (k >= tr.Values.Length - 1 || t <= tr.Times[k]) return tr.Values[Math.Min(k, tr.Values.Length - 1)];
            float span = tr.Times[k + 1] - tr.Times[k];
            float f = span > 1e-6f ? (t - tr.Times[k]) / span : 0f;
            M2Vec3 a = tr.Values[k], c = tr.Values[k + 1];
            return new M2Vec3(a.X + (c.X - a.X) * f, a.Y + (c.Y - a.Y) * f, a.Z + (c.Z - a.Z) * f);
        }

        public static M2Vec2 Eval(M2PartTrack<M2Vec2> tr, float t, M2Vec2 fallback)
        {
            if (tr == null || tr.Values.Length == 0) return fallback;
            int k = FindPart(tr.Times, t);
            if (k >= tr.Values.Length - 1 || t <= tr.Times[k]) return tr.Values[Math.Min(k, tr.Values.Length - 1)];
            float span = tr.Times[k + 1] - tr.Times[k];
            float f = span > 1e-6f ? (t - tr.Times[k]) / span : 0f;
            M2Vec2 a = tr.Values[k], c = tr.Values[k + 1];
            return new M2Vec2(a.X + (c.X - a.X) * f, a.Y + (c.Y - a.Y) * f);
        }

        static int FindPart(float[] times, float t)
        {
            int k = 0;
            while (k + 1 < times.Length && times[k + 1] <= t) k++;
            return k;
        }

        /// <summary>Value of a track in a sequence (the first key when the sequence has none; fallback when the track is empty).</summary>
        public static float At(M2Track<float> tr, M2Model m, int seq, int timeMs, long globalMs, float fallback)
        {
            float v;
            if (tr != null && M2Sampler.Sample(tr, seq, timeMs, m.GlobalSequences, globalMs, out v)) return v;
            return tr != null ? tr.FirstValue(fallback) : fallback;
        }

        public static M2Vec3 At(M2Track<M2Vec3> tr, M2Model m, int seq, int timeMs, long globalMs, M2Vec3 fallback)
        {
            M2Vec3 v;
            if (tr != null && M2Sampler.Sample(tr, seq, timeMs, m.GlobalSequences, globalMs, out v)) return v;
            return tr != null ? tr.FirstValue(fallback) : fallback;
        }

        /// <summary>True when a track changes over time (more than one distinct key in any sequence or a global sequence).</summary>
        public static bool IsAnimated<T>(M2Track<T> tr)
        {
            if (tr == null) return false;
            for (int s = 0; s < tr.Values.Length; s++)
            {
                var v = tr.Values[s];
                if (v == null || v.Length < 2) continue;
                for (int k = 1; k < v.Length; k++) if (!EqualityComparer<T>.Default.Equals(v[k], v[0])) return true;
            }
            // Different constant values in different sequences also count.
            bool have = false; T first = default(T);
            for (int s = 0; s < tr.Values.Length; s++)
            {
                var v = tr.Values[s];
                if (v == null || v.Length == 0) continue;
                if (!have) { first = v[0]; have = true; }
                else if (!EqualityComparer<T>.Default.Equals(v[0], first)) return true;
            }
            return false;
        }
    }
}
