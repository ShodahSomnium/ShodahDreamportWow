using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    // Pure C# M2 (MD20) reader for the 3.3.5a client (version 264, measured on the real files; 260-264 accepted).
    // Layout sources: wowdev wiki "M2" (header offsets, M2Track Wrath+ layout, M2CompBone, M2Sequence, M2Vertex,
    // M2Texture/M2Material/M2Attachment), cross-checked with WoW Model Viewer 0.7.0.5 and noggit3 (3.3.5 tools), then
    // verified structurally on the real files by the harness probe Models (every array must fit, every index in range).
    // Everything here is in WoW model space (yards, X forward, Y left, Z up). Unity code converts through WowSpace.

    /// <summary>A 3-float vector in WoW space.</summary>
    public struct M2Vec3
    {
        public float X, Y, Z;
        public M2Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public override string ToString() { return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ", " + Z.ToString("0.###") + ")"; }
    }

    /// <summary>A quaternion in WoW space (x, y, z, w as stored).</summary>
    public struct M2Quat
    {
        public float X, Y, Z, W;
        public M2Quat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static M2Quat Identity { get { return new M2Quat(0f, 0f, 0f, 1f); } }
    }

    /// <summary>
    /// An animated value (M2Track, Wrath+ layout: per-sequence arrays of timestamps and values). Times[s]/Values[s] belong
    /// to sequence s; a track on a global sequence uses index 0 and the global loop length. Hermite/Bezier tracks keep only
    /// the key values (the tangents are dropped; they are sampled linearly).
    /// </summary>
    public sealed class M2Track<T>
    {
        public int Interpolation;   // 0 none (step), 1 linear, 2 bezier, 3 hermite
        public int GlobalSequence = -1;
        public int[][] Times = new int[0][];
        public T[][] Values = new T[0][];

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < Values.Length; i++) if (Values[i] != null && Values[i].Length > 0) return false;
                return true;
            }
        }

        /// <summary>Key arrays for a sequence index (the global array when the track is on a global sequence). Null when none.</summary>
        public bool TryGetKeys(int sequence, out int[] times, out T[] values)
        {
            int i = GlobalSequence >= 0 ? 0 : sequence;
            if (i >= 0 && i < Values.Length && Values[i] != null && Values[i].Length > 0 && Times[i] != null && Times[i].Length > 0)
            {
                times = Times[i]; values = Values[i];
                return true;
            }
            times = null; values = null;
            return false;
        }

        /// <summary>The first key of the first sequence that has keys (the "first key is fine for now" value), or fallback.</summary>
        public T FirstValue(T fallback)
        {
            for (int i = 0; i < Values.Length; i++) if (Values[i] != null && Values[i].Length > 0) return Values[i][0];
            return fallback;
        }
    }

    public sealed class M2Sequence
    {
        public int AnimId, SubId;          // AnimationData.dbc id, variation index
        public int Duration;               // ms
        public float MoveSpeed;
        public uint Flags;                 // 0x20 = data is inside the .m2 (else in <Model><AnimId:D4>-<SubId:D2>.anim), 0x40 = alias
        public int Frequency;
        public int VariationNext, AliasNext;
        public int BlendTime;              // iteration 7 (lead contract): ms, u32 at +28 of the 64-byte sequence record (PLAN7 s4.3); Body fills it
        public bool Embedded { get { return (Flags & 0x20) != 0; } }
        public bool IsAlias { get { return (Flags & 0x40) != 0; } }
        /// <summary>True when the keys of this sequence were loaded (embedded or its .anim file was read).</summary>
        public bool DataLoaded;
    }

    public sealed class M2Bone
    {
        public int KeyBoneId;
        public uint Flags;                 // 0x8 spherical billboard, 0x10/0x20/0x40 cylindrical billboards, 0x200 transformed
        public int Parent;
        public int SubmeshId;
        public M2Track<M2Vec3> Translation;
        public M2Track<M2Quat> Rotation;
        public M2Track<M2Vec3> Scale;
        public M2Vec3 Pivot;
        public bool IsBillboard { get { return (Flags & 0x78) != 0; } }
    }

    public sealed class M2TextureDef
    {
        public int Type;                   // 0 = file name; 1 body skin, 2 object skin/cape, 6 hair, 8 fur, 11-13 monster skins
        public uint Flags;                 // 1 wrap U, 2 wrap V
        public string FileName;
    }

    public struct M2Material
    {
        public int Flags;                  // 1 unlit, 2 unfogged, 4 two-sided, 8 billboard, 0x10 no depth write
        public int BlendMode;              // 0 Opaque 1 AlphaKey 2 Alpha 3 NoAlphaAdd 4 Add 5 Mod 6 Mod2x
    }

    public sealed class M2Color
    {
        public M2Track<M2Vec3> Color;
        public M2Track<float> Alpha;
    }

    public sealed class M2TextureTransform
    {
        public M2Track<M2Vec3> Translation;
        public M2Track<M2Quat> Rotation;
        public M2Track<M2Vec3> Scale;
    }

    public sealed class M2Attachment
    {
        public int Id;                     // attachment id (0 shield/left wrist, 1 right palm, 2 left palm ... see the Models report)
        public int Bone;
        public M2Vec3 Position;            // model space (bind pose)
    }

    /// <summary>A parsed M2 file. Parse is pure and thread-safe (no shared state).</summary>
    public sealed class M2Model
    {
        public const int SequenceSize = 64, BoneSize = 88, VertexSize = 48, AttachmentSize = 40, TextureSize = 16,
                         ColorSize = 40, TransparencySize = 20, TextureTransformSize = 60, TrackSize = 20;

        public string Path;
        public uint Version;
        public string Name;
        public uint GlobalFlags;
        public int[] GlobalSequences = new int[0];
        public M2Sequence[] Sequences = new M2Sequence[0];
        public short[] SequenceLookup = new short[0];
        public M2Bone[] Bones = new M2Bone[0];
        public short[] KeyBoneLookup = new short[0];

        public int VertexCount;
        public float[] Positions = new float[0];   // 3 per vertex
        public byte[] BoneWeights = new byte[0];   // 4 per vertex
        public byte[] BoneIndices = new byte[0];   // 4 per vertex (direct bone indices)
        public float[] Normals = new float[0];     // 3 per vertex
        public float[] Uv0 = new float[0];         // 2 per vertex
        public float[] Uv1 = new float[0];         // 2 per vertex
        public int SkinProfileCount;

        public M2Color[] Colors = new M2Color[0];
        public M2TextureDef[] Textures = new M2TextureDef[0];
        public M2Track<float>[] Transparencies = new M2Track<float>[0];
        public M2TextureTransform[] TextureTransforms = new M2TextureTransform[0];
        public short[] ReplaceableTextureLookup = new short[0];
        public M2Material[] Materials = new M2Material[0];
        public short[] BoneCombos = new short[0];
        public short[] TextureCombos = new short[0];
        public short[] TextureCoordCombos = new short[0];
        public short[] TransparencyCombos = new short[0];
        public short[] TextureTransformCombos = new short[0];

        public M2Vec3 BoundsMin, BoundsMax;
        public float BoundsRadius;
        public ushort[] CollisionIndices = new ushort[0];
        public float[] CollisionPositions = new float[0];   // 3 per vertex
        public float[] CollisionNormals = new float[0];     // 3 per face

        public M2Attachment[] Attachments = new M2Attachment[0];
        public short[] AttachmentLookup = new short[0];

        /// <summary>Particle emitters (M2ParticleOld, 476 B each in v264) and ribbon emitters (M2Ribbon, 176 B). See M2Emitters.</summary>
        public M2ParticleEmitter[] Particles = new M2ParticleEmitter[0];
        public M2RibbonEmitter[] Ribbons = new M2RibbonEmitter[0];
        public bool HasEmitters { get { return Particles.Length > 0 || Ribbons.Length > 0; } }

        /// <summary>External .anim files: how many sequences needed one, how many were read, how many were missing.</summary>
        public int ExternalAnimsNeeded, ExternalAnimsLoaded, ExternalAnimsMissing;
        /// <summary>Non-fatal problems (out-of-range arrays are dropped and noted here).</summary>
        public readonly List<string> Warnings = new List<string>();

        public bool HasCollision { get { return CollisionIndices.Length >= 3 && CollisionPositions.Length >= 9; } }

        // --------------------------------------------------------------------------------------------- paths

        /// <summary>"x.mdx"/"x.mdl" -> "x.m2" (DBCs and old names use the legacy extension; the client reads the .m2).</summary>
        public static string NormalizeModelPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Trim().Replace('/', '\\');
            if (path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 4) + ".m2";
            else if (!path.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) && path.IndexOf('.', Math.Max(0, path.LastIndexOf('\\'))) < 0)
                path += ".m2";
            return path;
        }

        static string Base(string m2Path)
        {
            m2Path = NormalizeModelPath(m2Path);
            return m2Path.Substring(0, m2Path.Length - 3);
        }

        /// <summary>"Dir\Model.m2", 0 -> "Dir\Model00.skin".</summary>
        public static string SkinPath(string m2Path, int profile) { return Base(m2Path) + profile.ToString("D2") + ".skin"; }

        /// <summary>"Dir\Model.m2", 60, 0 -> "Dir\Model0060-00.anim".</summary>
        public static string AnimPath(string m2Path, int animId, int subId) { return Base(m2Path) + animId.ToString("D4") + "-" + subId.ToString("D2") + ".anim"; }

        // --------------------------------------------------------------------------------------------- lookups

        /// <summary>Index of the sequence with this AnimationData id (variation 0 preferred), or -1.</summary>
        public int FindSequence(int animId)
        {
            int any = -1;
            for (int i = 0; i < Sequences.Length; i++)
            {
                if (Sequences[i].AnimId != animId) continue;
                if (Sequences[i].SubId == 0) return i;
                if (any < 0) any = i;
            }
            return any;
        }

        /// <summary>Follows alias sequences (flag 0x40 -> AliasNext) to the sequence that holds the keys.</summary>
        public int ResolveAlias(int seq)
        {
            for (int guard = 0; guard < 8 && seq >= 0 && seq < Sequences.Length && Sequences[seq].IsAlias; guard++)
                seq = Sequences[seq].AliasNext;
            return seq >= 0 && seq < Sequences.Length ? seq : -1;
        }

        /// <summary>Index into Attachments for an attachment id via the lookup table, or -1.</summary>
        public int FindAttachment(int attachmentId)
        {
            if (attachmentId >= 0 && attachmentId < AttachmentLookup.Length)
            {
                int i = AttachmentLookup[attachmentId];
                if (i >= 0 && i < Attachments.Length) return i;
            }
            for (int i = 0; i < Attachments.Length; i++) if (Attachments[i].Id == attachmentId) return i;
            return -1;
        }

        // --------------------------------------------------------------------------------------------- parse

        /// <summary>
        /// Parses an M2. readFile (may be null) reads other game files (the external .anim files of sequences whose keys
        /// are not embedded); a missing .anim only leaves that sequence without keys. Throws InvalidDataException when the
        /// file is not an MD20 v260-264 model.
        /// </summary>
        public static M2Model Parse(byte[] data, string path, Func<string, byte[]> readFile)
        {
            if (data == null || data.Length < 0x130) throw new System.IO.InvalidDataException("M2: too small (" + (data == null ? 0 : data.Length) + " B)");
            if (data[0] != 'M' || data[1] != 'D' || data[2] != '2' || data[3] != '0')
                throw new System.IO.InvalidDataException("M2: magic is not MD20 (" + Encoding.ASCII.GetString(data, 0, 4) + ")");
            var m = new M2Model();
            m.Path = NormalizeModelPath(path);
            var r = new M2Reader(data, m);
            m.Version = r.U32(0x04);
            if (m.Version < 260 || m.Version > 264) throw new System.IO.InvalidDataException("M2: version " + m.Version + " (expected 260-264, WotLK)");

            int n, o;
            if (r.Arr(0x08, 1, out n, out o) && n > 0) m.Name = r.CString(o, n);
            m.GlobalFlags = r.U32(0x10);

            if (r.Arr(0x14, 4, out n, out o)) { m.GlobalSequences = new int[n]; for (int i = 0; i < n; i++) m.GlobalSequences[i] = (int)r.U32(o + i * 4); }

            if (r.Arr(0x1C, SequenceSize, out n, out o))
            {
                m.Sequences = new M2Sequence[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * SequenceSize;
                    m.Sequences[i] = new M2Sequence
                    {
                        AnimId = r.U16(b), SubId = r.U16(b + 2), Duration = (int)r.U32(b + 4), MoveSpeed = r.F32(b + 8),
                        Flags = r.U32(b + 12), Frequency = r.I16(b + 16), VariationNext = r.I16(b + 60), AliasNext = r.U16(b + 62),
                        // Iteration 7 (Body): blend time, u32 at +28 (measured on the mount models: 150 default, horse JumpStart 50,
                        // gryphon Fly 250, wind rider Run 350, proto-drake JumpLandRun 0; the high 16 bits are always 0 -
                        // P/harness/Probes/Mount7/feel/blend.txt). Clamped to 0..10000 ms so a stray word can never freeze a blend.
                        BlendTime = (int)System.Math.Min(10000u, r.U32(b + 28))
                    };
                }
            }
            m.SequenceLookup = r.I16Array(0x24);

            // Key sources per sequence: the .m2 itself or the sequence's external .anim file.
            var sources = new byte[m.Sequences.Length][];
            for (int s = 0; s < m.Sequences.Length; s++)
            {
                var seq = m.Sequences[s];
                if (seq.IsAlias) continue;
                if (seq.Embedded) { sources[s] = data; seq.DataLoaded = true; continue; }
                m.ExternalAnimsNeeded++;
                byte[] anim = null;
                if (readFile != null) { try { anim = readFile(AnimPath(m.Path, seq.AnimId, seq.SubId)); } catch (Exception) { anim = null; } }
                if (anim != null) { sources[s] = anim; seq.DataLoaded = true; m.ExternalAnimsLoaded++; }
                else m.ExternalAnimsMissing++;
            }
            r.Sources = sources;

            if (r.Arr(0x2C, BoneSize, out n, out o))
            {
                m.Bones = new M2Bone[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * BoneSize;
                    m.Bones[i] = new M2Bone
                    {
                        KeyBoneId = r.I32(b), Flags = r.U32(b + 4), Parent = r.I16(b + 8), SubmeshId = r.U16(b + 10),
                        Translation = r.TrackVec3(b + 16), Rotation = r.TrackCompQuat(b + 36), Scale = r.TrackVec3(b + 56),
                        Pivot = r.Vec3(b + 76)
                    };
                    if (m.Bones[i].Parent >= n || m.Bones[i].Parent == i) { m.Warnings.Add("bone " + i + " parent " + m.Bones[i].Parent + " out of range"); m.Bones[i].Parent = -1; }
                }
            }
            m.KeyBoneLookup = r.I16Array(0x34);

            if (r.Arr(0x3C, VertexSize, out n, out o))
            {
                m.VertexCount = n;
                m.Positions = new float[n * 3]; m.Normals = new float[n * 3];
                m.BoneWeights = new byte[n * 4]; m.BoneIndices = new byte[n * 4];
                m.Uv0 = new float[n * 2]; m.Uv1 = new float[n * 2];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * VertexSize;
                    m.Positions[i * 3] = r.F32(b); m.Positions[i * 3 + 1] = r.F32(b + 4); m.Positions[i * 3 + 2] = r.F32(b + 8);
                    Buffer.BlockCopy(data, b + 12, m.BoneWeights, i * 4, 4);
                    Buffer.BlockCopy(data, b + 16, m.BoneIndices, i * 4, 4);
                    m.Normals[i * 3] = r.F32(b + 20); m.Normals[i * 3 + 1] = r.F32(b + 24); m.Normals[i * 3 + 2] = r.F32(b + 28);
                    m.Uv0[i * 2] = r.F32(b + 32); m.Uv0[i * 2 + 1] = r.F32(b + 36);
                    m.Uv1[i * 2] = r.F32(b + 40); m.Uv1[i * 2 + 1] = r.F32(b + 44);
                }
                int nb = m.Bones.Length, bad = 0;
                for (int i = 0; i < n * 4; i++)
                    if (m.BoneIndices[i] >= nb && (nb > 0 || m.BoneWeights[i] != 0)) { m.BoneIndices[i] = 0; if (m.BoneWeights[i] != 0) bad++; }
                if (bad > 0) m.Warnings.Add(bad + " weighted vertex bone indices >= bone count " + nb);
            }
            m.SkinProfileCount = (int)r.U32(0x44);

            if (r.Arr(0x48, ColorSize, out n, out o))
            {
                m.Colors = new M2Color[n];
                for (int i = 0; i < n; i++) m.Colors[i] = new M2Color { Color = r.TrackVec3(o + i * ColorSize), Alpha = r.TrackFixed16(o + i * ColorSize + 20) };
            }
            if (r.Arr(0x50, TextureSize, out n, out o))
            {
                m.Textures = new M2TextureDef[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * TextureSize;
                    var t = new M2TextureDef { Type = (int)r.U32(b), Flags = r.U32(b + 4) };
                    int fn, fo;
                    if (r.Arr(b + 8, 1, out fn, out fo) && fn > 0) t.FileName = r.CString(fo, fn);
                    m.Textures[i] = t;
                }
            }
            if (r.Arr(0x58, TransparencySize, out n, out o))
            {
                m.Transparencies = new M2Track<float>[n];
                for (int i = 0; i < n; i++) m.Transparencies[i] = r.TrackFixed16(o + i * TransparencySize);
            }
            if (r.Arr(0x60, TextureTransformSize, out n, out o))
            {
                m.TextureTransforms = new M2TextureTransform[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * TextureTransformSize;
                    m.TextureTransforms[i] = new M2TextureTransform { Translation = r.TrackVec3(b), Rotation = r.TrackQuat(b + 20), Scale = r.TrackVec3(b + 40) };
                }
            }
            m.ReplaceableTextureLookup = r.I16Array(0x68);
            if (r.Arr(0x70, 4, out n, out o))
            {
                m.Materials = new M2Material[n];
                for (int i = 0; i < n; i++) m.Materials[i] = new M2Material { Flags = r.U16(o + i * 4), BlendMode = r.U16(o + i * 4 + 2) };
            }
            m.BoneCombos = r.I16Array(0x78);
            m.TextureCombos = r.I16Array(0x80);
            m.TextureCoordCombos = r.I16Array(0x88);
            m.TransparencyCombos = r.I16Array(0x90);
            m.TextureTransformCombos = r.I16Array(0x98);

            m.BoundsMin = r.Vec3(0xA0); m.BoundsMax = r.Vec3(0xAC); m.BoundsRadius = r.F32(0xB8);

            if (r.Arr(0xD8, 2, out n, out o)) { m.CollisionIndices = new ushort[n]; for (int i = 0; i < n; i++) m.CollisionIndices[i] = r.U16(o + i * 2); }
            m.CollisionPositions = r.F32Array(0xE0, 3);
            m.CollisionNormals = r.F32Array(0xE8, 3);
            int ncv = m.CollisionPositions.Length / 3;
            for (int i = 0; i < m.CollisionIndices.Length; i++)
                if (m.CollisionIndices[i] >= ncv) { m.Warnings.Add("collision index " + m.CollisionIndices[i] + " >= " + ncv + ": collision dropped"); m.CollisionIndices = new ushort[0]; break; }
            if (m.CollisionIndices.Length % 3 != 0) m.CollisionIndices = new ushort[0];

            if (r.Arr(0xF0, AttachmentSize, out n, out o))
            {
                m.Attachments = new M2Attachment[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * AttachmentSize;
                    m.Attachments[i] = new M2Attachment { Id = (int)r.U32(b), Bone = r.U16(b + 4), Position = r.Vec3(b + 8) };
                    if (m.Attachments[i].Bone >= m.Bones.Length) m.Attachments[i].Bone = -1;
                }
            }
            m.AttachmentLookup = r.I16Array(0xF8);

            // Particle emitters (0x128) and ribbon emitters (0x120): Models/M2Emitters.cs (iteration 3, VFX).
            try { M2Emitters.Read(r, m); }
            catch (Exception e) { m.Warnings.Add("emitters unreadable: " + e.Message); m.Particles = new M2ParticleEmitter[0]; m.Ribbons = new M2RibbonEmitter[0]; }
            return m;
        }
    }

    /// <summary>Bounds-checked little-endian reads over an M2 (and its .anim sources). Out-of-range arrays read as empty.</summary>
    internal sealed class M2Reader
    {
        readonly byte[] _d;
        readonly M2Model _m;
        public byte[][] Sources;   // per sequence: bytes holding that sequence's keys (null = none)

        public M2Reader(byte[] data, M2Model m) { _d = data; _m = m; }

        public uint U32(int o) { return BitConverter.ToUInt32(_d, o); }
        public int I32(int o) { return BitConverter.ToInt32(_d, o); }
        public ushort U16(int o) { return BitConverter.ToUInt16(_d, o); }
        public short I16(int o) { return BitConverter.ToInt16(_d, o); }
        public float F32(int o) { return BitConverter.ToSingle(_d, o); }
        public M2Vec3 Vec3(int o) { return new M2Vec3(F32(o), F32(o + 4), F32(o + 8)); }

        public string CString(int o, int n)
        {
            int end = o;
            while (end < o + n && _d[end] != 0) end++;
            return Encoding.ASCII.GetString(_d, o, end - o);
        }

        /// <summary>Reads {count, offset} at o and checks count*elemSize fits the file. False (and a warning) when it does not.</summary>
        public bool Arr(int o, int elemSize, out int count, out int offset)
        {
            count = 0; offset = 0;
            if (o < 0 || o + 8 > _d.Length) return false;
            uint c = BitConverter.ToUInt32(_d, o), off = BitConverter.ToUInt32(_d, o + 4);
            if (c == 0) return true;
            if ((ulong)off + (ulong)c * (ulong)elemSize > (ulong)_d.Length)
            {
                _m.Warnings.Add("array @0x" + o.ToString("X") + " count " + c + " x " + elemSize + " at " + off + " exceeds the file (" + _d.Length + " B)");
                return false;
            }
            count = (int)c; offset = (int)off;
            return true;
        }

        public short[] I16Array(int o)
        {
            int n, off;
            if (!Arr(o, 2, out n, out off) || n == 0) return new short[0];
            var a = new short[n];
            for (int i = 0; i < n; i++) a[i] = I16(off + i * 2);
            return a;
        }

        public float[] F32Array(int o, int floatsPerElem)
        {
            int n, off;
            if (!Arr(o, 4 * floatsPerElem, out n, out off) || n == 0) return new float[0];
            var a = new float[n * floatsPerElem];
            Buffer.BlockCopy(_d, off, a, 0, a.Length * 4);
            return a;
        }

        // ------------------------------------------------------------------------------------ tracks

        delegate T ValueReader<T>(byte[] src, int o);

        M2Track<T> Track<T>(int o, int valueSize, ValueReader<T> read)
        {
            var t = new M2Track<T> { Interpolation = U16(o), GlobalSequence = I16(o + 2) };
            int nt, ot, nv, ov;
            bool okT = Arr(o + 4, 8, out nt, out ot), okV = Arr(o + 12, 8, out nv, out ov);
            if (!okT || !okV) return t;
            int n = Math.Min(nt, nv);
            if (nt != nv) _m.Warnings.Add("track @0x" + o.ToString("X") + " times " + nt + " != values " + nv);
            int stride = (t.Interpolation == 2 || t.Interpolation == 3) ? 3 : 1;   // bezier/hermite: value, in, out
            t.Times = new int[n][];
            t.Values = new T[n][];
            bool global = t.GlobalSequence >= 0;
            for (int s = 0; s < n; s++)
            {
                // Global-sequence tracks and sequences without their own source read from the .m2 itself.
                byte[] src = _d;
                if (!global && Sources != null && s < Sources.Length) src = Sources[s];
                if (src == null) continue;   // external .anim not loaded (or an alias sequence)
                uint ct = BitConverter.ToUInt32(_d, ot + s * 8), oft = BitConverter.ToUInt32(_d, ot + s * 8 + 4);
                uint cv = BitConverter.ToUInt32(_d, ov + s * 8), ofv = BitConverter.ToUInt32(_d, ov + s * 8 + 4);
                if (ct == 0 || cv == 0) continue;
                int keys = (int)Math.Min(ct, cv);   // bezier/hermite: each value element is (value, in, out)
                if ((ulong)oft + ct * 4UL > (ulong)src.Length || (ulong)ofv + (ulong)cv * (ulong)(valueSize * stride) > (ulong)src.Length)
                {
                    _m.Warnings.Add("track @0x" + o.ToString("X") + " seq " + s + " keys outside their source (" + src.Length + " B)");
                    continue;
                }
                var times = new int[keys];
                var vals = new T[keys];
                for (int k = 0; k < keys; k++)
                {
                    times[k] = (int)BitConverter.ToUInt32(src, (int)oft + k * 4);
                    vals[k] = read(src, (int)ofv + k * stride * valueSize);
                }
                t.Times[s] = times;
                t.Values[s] = vals;
            }
            return t;
        }

        static M2Vec3 ReadVec3(byte[] s, int o) { return new M2Vec3(BitConverter.ToSingle(s, o), BitConverter.ToSingle(s, o + 4), BitConverter.ToSingle(s, o + 8)); }
        static M2Quat ReadQuat(byte[] s, int o) { return new M2Quat(BitConverter.ToSingle(s, o), BitConverter.ToSingle(s, o + 4), BitConverter.ToSingle(s, o + 8), BitConverter.ToSingle(s, o + 12)); }
        static float ReadFixed16(byte[] s, int o) { return BitConverter.ToInt16(s, o) / 32767f; }

        /// <summary>M2CompQuat: 4 x int16 (x, y, z, w); value = (v &lt; 0 ? v + 32768 : v - 32767) / 32767 (wowdev), then normalised.</summary>
        static M2Quat ReadCompQuat(byte[] s, int o)
        {
            float x = Dec(BitConverter.ToInt16(s, o)), y = Dec(BitConverter.ToInt16(s, o + 2)),
                  z = Dec(BitConverter.ToInt16(s, o + 4)), w = Dec(BitConverter.ToInt16(s, o + 6));
            float len = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-6f) return M2Quat.Identity;
            return new M2Quat(x / len, y / len, z / len, w / len);
        }

        static float Dec(short v) { return (v < 0 ? v + 32768 : v - 32767) / 32767f; }

        /// <summary>A track with a caller-supplied value reader (valueSize bytes per key), e.g. uint16/uint8/compressed gravity.</summary>
        public M2Track<T> TrackOf<T>(int o, int valueSize, Func<byte[], int, T> read) { return Track<T>(o, valueSize, (s, off) => read(s, off)); }
        public int Length { get { return _d.Length; } }
        public byte[] Bytes { get { return _d; } }

        public M2Track<M2Vec3> TrackVec3(int o) { return Track<M2Vec3>(o, 12, ReadVec3); }
        public M2Track<M2Quat> TrackQuat(int o) { return Track<M2Quat>(o, 16, ReadQuat); }
        public M2Track<M2Quat> TrackCompQuat(int o) { return Track<M2Quat>(o, 8, ReadCompQuat); }
        public M2Track<float> TrackFixed16(int o) { return Track<float>(o, 2, ReadFixed16); }
    }
}
