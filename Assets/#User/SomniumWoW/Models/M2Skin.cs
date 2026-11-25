using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>A submesh (M2SkinSection, 48 B): one geoset piece of the skin's triangle list.</summary>
    public struct M2Submesh
    {
        public int GeosetId;       // skinSectionId: group * 100 + variant (0 = body, 1..n hair styles, 101.. facial 1, ...)
        public int Level;
        public int VertexStart, VertexCount;
        public int IndexStart, IndexCount;   // into M2Skin.Indices (IndexStart already includes Level << 16)
        public int BoneCount, BoneComboIndex, BoneInfluences, CenterBoneIndex;
        public M2Vec3 Center;
    }

    /// <summary>A texture unit (M2Batch, 24 B): draws one submesh with one material and 1-4 textures.</summary>
    public struct M2Batch
    {
        public int Flags;          // 0x10 static (not animated)...
        public int PriorityPlane;
        public int ShaderId;
        public int SubmeshIndex;
        public int GeosetIndex;
        public int ColorIndex;     // -1 = none
        public int MaterialIndex;
        public int MaterialLayer;
        public int TextureCount;
        public int TextureComboIndex;
        public int TextureCoordComboIndex;
        public int TransparencyComboIndex;
        public int TextureTransformComboIndex;
    }

    /// <summary>A parsed .skin file (3.3.5a: magic SKIN, 48-byte header). Pure and thread-safe.</summary>
    public sealed class M2Skin
    {
        public const int HeaderSize = 48, SubmeshSize = 48, BatchSize = 24;

        public string Path;
        public ushort[] VertexLookup = new ushort[0];   // skin vertex -> M2 vertex
        public ushort[] Indices = new ushort[0];        // triangle list of skin vertex indices
        public M2Submesh[] Submeshes = new M2Submesh[0];
        public M2Batch[] Batches = new M2Batch[0];
        public int BoneCountMax;
        public readonly List<string> Warnings = new List<string>();

        public int TriangleCount { get { return Indices.Length / 3; } }

        /// <summary>Parses a .skin. m2VertexCount (when &gt; 0) validates the vertex lookup. Throws InvalidDataException on a bad header.</summary>
        public static M2Skin Parse(byte[] d, string path, int m2VertexCount)
        {
            if (d == null || d.Length < HeaderSize) throw new System.IO.InvalidDataException("skin: too small");
            if (d[0] != 'S' || d[1] != 'K' || d[2] != 'I' || d[3] != 'N') throw new System.IO.InvalidDataException("skin: magic is not SKIN (" + Encoding.ASCII.GetString(d, 0, 4) + ")");
            var s = new M2Skin { Path = path };
            int n, o;
            if (Arr(d, 4, 2, out n, out o, s))
            {
                s.VertexLookup = new ushort[n];
                Buffer.BlockCopy(d, o, s.VertexLookup, 0, n * 2);
            }
            if (Arr(d, 12, 2, out n, out o, s))
            {
                s.Indices = new ushort[n - n % 3];
                Buffer.BlockCopy(d, o, s.Indices, 0, s.Indices.Length * 2);
            }
            if (Arr(d, 28, SubmeshSize, out n, out o, s))
            {
                s.Submeshes = new M2Submesh[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * SubmeshSize;
                    var sm = new M2Submesh
                    {
                        GeosetId = BitConverter.ToUInt16(d, b), Level = BitConverter.ToUInt16(d, b + 2),
                        VertexStart = BitConverter.ToUInt16(d, b + 4), VertexCount = BitConverter.ToUInt16(d, b + 6),
                        IndexStart = BitConverter.ToUInt16(d, b + 8), IndexCount = BitConverter.ToUInt16(d, b + 10),
                        BoneCount = BitConverter.ToUInt16(d, b + 12), BoneComboIndex = BitConverter.ToUInt16(d, b + 14),
                        BoneInfluences = BitConverter.ToUInt16(d, b + 16), CenterBoneIndex = BitConverter.ToUInt16(d, b + 18),
                        Center = new M2Vec3(BitConverter.ToSingle(d, b + 20), BitConverter.ToSingle(d, b + 24), BitConverter.ToSingle(d, b + 28))
                    };
                    sm.IndexStart += sm.Level << 16;   // >65535 indices: Level carries the high part of the start
                    if (sm.IndexStart + sm.IndexCount > s.Indices.Length || sm.IndexCount % 3 != 0)
                    {
                        s.Warnings.Add("submesh " + i + " indices " + sm.IndexStart + "+" + sm.IndexCount + " exceed " + s.Indices.Length + ": emptied");
                        sm.IndexCount = 0;
                    }
                    s.Submeshes[i] = sm;
                }
            }
            if (Arr(d, 36, BatchSize, out n, out o, s))
            {
                s.Batches = new M2Batch[n];
                for (int i = 0; i < n; i++)
                {
                    int b = o + i * BatchSize;
                    s.Batches[i] = new M2Batch
                    {
                        Flags = d[b], PriorityPlane = (sbyte)d[b + 1], ShaderId = BitConverter.ToUInt16(d, b + 2),
                        SubmeshIndex = BitConverter.ToUInt16(d, b + 4), GeosetIndex = BitConverter.ToUInt16(d, b + 6),
                        ColorIndex = BitConverter.ToInt16(d, b + 8), MaterialIndex = BitConverter.ToUInt16(d, b + 10),
                        MaterialLayer = BitConverter.ToUInt16(d, b + 12), TextureCount = BitConverter.ToUInt16(d, b + 14),
                        TextureComboIndex = BitConverter.ToUInt16(d, b + 16), TextureCoordComboIndex = BitConverter.ToUInt16(d, b + 18),
                        TransparencyComboIndex = BitConverter.ToUInt16(d, b + 20), TextureTransformComboIndex = BitConverter.ToUInt16(d, b + 22)
                    };
                }
            }
            s.BoneCountMax = (int)BitConverter.ToUInt32(d, 44);

            // Validate indices -> lookup -> M2 vertices.
            int nl = s.VertexLookup.Length, badIdx = 0, badLookup = 0;
            for (int i = 0; i < s.Indices.Length; i++) if (s.Indices[i] >= nl) { badIdx++; s.Indices[i] = 0; }
            if (m2VertexCount > 0) for (int i = 0; i < nl; i++) if (s.VertexLookup[i] >= m2VertexCount) { badLookup++; s.VertexLookup[i] = 0; }
            if (badIdx > 0) s.Warnings.Add(badIdx + " indices >= vertex lookup count " + nl);
            if (badLookup > 0) s.Warnings.Add(badLookup + " vertex lookups >= M2 vertex count " + m2VertexCount);
            return s;
        }

        static bool Arr(byte[] d, int o, int elem, out int n, out int off, M2Skin s)
        {
            uint c = BitConverter.ToUInt32(d, o), of = BitConverter.ToUInt32(d, o + 4);
            n = 0; off = 0;
            if (c == 0) return false;
            if ((ulong)of + (ulong)c * (ulong)elem > (ulong)d.Length) { s.Warnings.Add("skin array @" + o + " exceeds the file"); return false; }
            n = (int)c; off = (int)of;
            return true;
        }
    }
}
