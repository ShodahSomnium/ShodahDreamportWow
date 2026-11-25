using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    // Pure WMO v17 (WotLK 3.3.5a, build 12340) reader: the root file and its group files (<name>_NNN.wmo).
    // No UnityEngine here - the offline harness runs it on the real game files.
    //
    // Layouts: wowdev.wiki/WMO (fetched 2026-09-25) and checked on the real files by the Buildings probes
    // (P/harness/Probes/Buildings/BuildingsProbes.cs; numbers in P/reports/buildings.md):
    //   - chunk ids are stored byte-reversed ("MVER" is the bytes 'R','E','V','M'); chunk walks end exactly at the file end.
    //   - MOHD 64 B, MOMT 64 B, MOGI 32 B, MODS 32 B, MODD 40 B, MFOG 48 B, MOGP header 0x44 B, MOPY 2 B, MOBA 24 B, MLIQ header 30 B.
    //   - MOVT/MONR/MODD positions are (x, y, z) in the WMO's own z-up frame (the wiki's "(X, Z, -Y)" remark is about a
    //     y-up viewer, not the file) - measured: see the report (doodads stand on floors, bounding boxes match MOVT).
    //   - MODD: size it by its chunk (MOHD.nDoodadDefs is larger than MODD/40 in 423 of 1985 roots - Data's WmoCounts probe).
    //   - MOCV/MOTV: a group carries 0-2 of each; the first MOCV is the lighting colour when group flag 0x4 is set, the
    //     blend-alpha set otherwise / second (flag 0x1000000); the second MOTV exists with flag 0x2000000.

    /// <summary>One MOMT material (64 bytes).</summary>
    public sealed class WmoMaterial
    {
        public const uint F_UNLIT = 0x1, F_UNFOGGED = 0x2, F_UNCULLED = 0x4, F_EXTLIGHT = 0x8, F_SIDN = 0x10, F_WINDOW = 0x20,
            F_CLAMP_S = 0x40, F_CLAMP_T = 0x80;

        public uint Flags;
        /// <summary>CMapObj shader index: 0 Diffuse, 1 Specular, 2 Metal, 3 Env, 4 Opaque, 5 EnvMetal, 6 TwoLayerDiffuse (3.3.5a uses 0-6).</summary>
        public int Shader;
        /// <summary>EGxBlend: 0 Opaque, 1 AlphaKey, 2 Alpha, 3 Add, 4 Mod, 5 Mod2x, 6 ModAdd, 7 InvSrcAlphaAdd, 8 InvSrcAlphaOpaque, 9 SrcAlphaOpaque, 10 NoAlphaAdd.</summary>
        public int BlendMode;
        public int Texture1Offset, Texture2Offset, Texture3Offset;
        /// <summary>Texture paths from MOTX ("" when unused).</summary>
        public string Texture1 = "", Texture2 = "", Texture3 = "";
        /// <summary>CImVector colours as stored: bytes B, G, R, A (packed little-endian into a uint: 0xAARRGGBB).</summary>
        public uint SidnColor, DiffColor, Color2;
        public uint GroundType, Flags2;

        public bool Unlit { get { return (Flags & F_UNLIT) != 0; } }
        public bool Unfogged { get { return (Flags & F_UNFOGGED) != 0; } }
        public bool TwoSided { get { return (Flags & F_UNCULLED) != 0; } }
        public bool ExteriorLight { get { return (Flags & F_EXTLIGHT) != 0; } }
        public bool Sidn { get { return (Flags & F_SIDN) != 0; } }
        public bool Window { get { return (Flags & F_WINDOW) != 0; } }
        public bool ClampS { get { return (Flags & F_CLAMP_S) != 0; } }
        public bool ClampT { get { return (Flags & F_CLAMP_T) != 0; } }
    }

    /// <summary>MOGI: one group's info in the root (32 bytes).</summary>
    public struct WmoGroupInfo
    {
        public uint Flags;
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public int NameOffset;
        public string Name;
    }

    /// <summary>MODS doodad set (32 bytes).</summary>
    public struct WmoDoodadSet
    {
        public string Name;
        public int Start;
        public int Count;
    }

    /// <summary>MODD doodad placement (40 bytes), in the WMO's own frame (yards, z up).</summary>
    public struct WmoDoodadDef
    {
        /// <summary>Offset into MODN (low 24 bits of the first word).</summary>
        public int NameOffset;
        /// <summary>High 8 bits of the first word (bit 0 acceptProjTex, ...).</summary>
        public int Flags;
        public string Name;
        public float X, Y, Z;
        /// <summary>Rotation quaternion (x, y, z, w) in the WMO frame.</summary>
        public float QX, QY, QZ, QW;
        public float Scale;
        /// <summary>CImVector B, G, R, A (A != 255: a MOLT light index per the wiki).</summary>
        public uint Color;
    }

    /// <summary>MFOG entry (48 bytes).</summary>
    public struct WmoFog
    {
        public uint Flags;
        public float X, Y, Z;
        public float SmallRadius, LargeRadius;
        public float End0, StartScalar0;
        public uint Color0;
        public float End1, StartScalar1;
        public uint Color1;
    }

    /// <summary>MOPT portal (20 bytes): its polygon is MOPV[StartVertex .. +VertexCount); plane NX*x + NY*y + NZ*z + D = 0 (WMO frame).</summary>
    public struct WmoPortal
    {
        public int StartVertex, VertexCount;
        public float NX, NY, NZ, D;
    }

    /// <summary>MOPR (8 bytes): one of a group's portals (the group's MOGP PortalStart/PortalCount range) and the group on the
    /// other side. Side (+1/-1) = the side of the portal plane the referencing group is on.</summary>
    public struct WmoPortalRef
    {
        public int Portal, Group, Side;
    }

    /// <summary>MOBA render batch (24 bytes).</summary>
    public struct WmoBatch
    {
        public int StartIndex;   // into MOVI
        public int IndexCount;
        public int MinIndex;     // first vertex
        public int MaxIndex;     // last vertex (inclusive)
        public int Flags;
        public int Material;     // into MOMT
    }

    /// <summary>MLIQ liquid of a group: a (XVerts x YVerts) height grid over (XTiles x YTiles) tiles.</summary>
    public sealed class WmoLiquid
    {
        /// <summary>Tile edge in yards: a map chunk (533.33 / 16) split in 8.</summary>
        public const float TileSize = 533.3333f / 16f / 8f;
        public int XVerts, YVerts, XTiles, YTiles;
        public float CornerX, CornerY, CornerZ;
        public int MaterialId;
        /// <summary>Vertex heights (yards, WMO frame), [y * XVerts + x].</summary>
        public float[] Heights;
        /// <summary>The first 4 bytes of each vertex (water: depth, flow0, flow1, filler; magma: s, t as int16).</summary>
        public byte[] VertexBytes;
        /// <summary>Tile flags, [y * XTiles + x]: low 4 bits legacy liquid type (0xF = no liquid), 0x40 fishable, 0x80 shared.</summary>
        public byte[] Tiles;

        public bool TileExists(int x, int y) { return (Tiles[y * XTiles + x] & 0x0F) != 0x0F; }
    }

    /// <summary>A WMO root file.</summary>
    public sealed class WmoRoot
    {
        public const ushort MOHD_UNIFIED_RENDER = 0x2, MOHD_LIQUID_DBC_ID = 0x4, MOHD_NO_FIX_VERTEX_ALPHA = 0x8;

        public string Path;
        public int Version;
        public int NTextures, NGroups, NPortals, NLights, NDoodadNames, NDoodadDefs, NDoodadSets;
        /// <summary>MOHD ambColor as stored (bytes B, G, R, A).</summary>
        public uint AmbientColor;
        public int WmoId;
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public ushort Flags;
        public WmoMaterial[] Materials = new WmoMaterial[0];
        public WmoGroupInfo[] Groups = new WmoGroupInfo[0];
        public WmoDoodadSet[] DoodadSets = new WmoDoodadSet[0];
        public WmoDoodadDef[] DoodadDefs = new WmoDoodadDef[0];
        public WmoFog[] Fogs = new WmoFog[0];
        /// <summary>MOPT portals and MOPR portal references (iteration 11, lod11: WmoGroupVisibility).</summary>
        public WmoPortal[] Portals = new WmoPortal[0];
        public WmoPortalRef[] PortalRefs = new WmoPortalRef[0];
        /// <summary>MOPV portal vertices, (x, y, z) per vertex, WMO frame, yards; MOPT StartVertex / VertexCount index it
        /// (iteration 12, portal culling: World/WmoPortalCuller.cs).</summary>
        public float[] PortalVertices = new float[0];
        /// <summary>MOSB skybox model ("" if none).</summary>
        public string Skybox = "";
        public byte[] Motx = new byte[0];
        public byte[] Modn = new byte[0];

        public bool UnifiedRender { get { return (Flags & MOHD_UNIFIED_RENDER) != 0; } }
        public bool LiquidTypeIsDbcId { get { return (Flags & MOHD_LIQUID_DBC_ID) != 0; } }
        public bool NoFixVertexAlpha { get { return (Flags & MOHD_NO_FIX_VERTEX_ALPHA) != 0; } }

        /// <summary>"World\wmo\X\Y.wmo" -> "World\wmo\X\Y_007.wmo".</summary>
        public static string GroupPath(string rootPath, int groupIndex)
        {
            string p = rootPath;
            if (p.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 4);
            return p + "_" + groupIndex.ToString("000") + ".wmo";
        }

        /// <summary>Parses root bytes. Throws InvalidDataException when the file is not a v17 WMO root.</summary>
        public static WmoRoot Parse(byte[] d, string path)
        {
            if (d == null) throw new InvalidDataException("WMO " + path + ": no data");
            var r = new WmoRoot { Path = path };
            bool haveHeader = false;
            int modsCount = -1;
            foreach (var c in WmoChunks.Walk(d, path))
            {
                int o = c.Offset, s = c.Size;
                switch (c.Id)
                {
                    case "MVER": r.Version = WmoChunks.I32(d, o); break;
                    case "MOHD":
                        if (s < 64) throw new InvalidDataException("WMO " + path + ": MOHD " + s + " B");
                        r.NTextures = WmoChunks.I32(d, o); r.NGroups = WmoChunks.I32(d, o + 4); r.NPortals = WmoChunks.I32(d, o + 8);
                        r.NLights = WmoChunks.I32(d, o + 12); r.NDoodadNames = WmoChunks.I32(d, o + 16); r.NDoodadDefs = WmoChunks.I32(d, o + 20);
                        r.NDoodadSets = WmoChunks.I32(d, o + 24); r.AmbientColor = WmoChunks.U32(d, o + 28); r.WmoId = WmoChunks.I32(d, o + 32);
                        r.MinX = WmoChunks.F(d, o + 36); r.MinY = WmoChunks.F(d, o + 40); r.MinZ = WmoChunks.F(d, o + 44);
                        r.MaxX = WmoChunks.F(d, o + 48); r.MaxY = WmoChunks.F(d, o + 52); r.MaxZ = WmoChunks.F(d, o + 56);
                        r.Flags = BitConverter.ToUInt16(d, o + 60);
                        haveHeader = true;
                        break;
                    case "MOTX": r.Motx = WmoChunks.Copy(d, o, s); break;
                    case "MOMT":
                    {
                        int n = s / 64;
                        r.Materials = new WmoMaterial[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 64;
                            r.Materials[i] = new WmoMaterial
                            {
                                Flags = WmoChunks.U32(d, b), Shader = WmoChunks.I32(d, b + 4), BlendMode = WmoChunks.I32(d, b + 8),
                                Texture1Offset = WmoChunks.I32(d, b + 12), SidnColor = WmoChunks.U32(d, b + 16),
                                Texture2Offset = WmoChunks.I32(d, b + 24), DiffColor = WmoChunks.U32(d, b + 28),
                                GroundType = WmoChunks.U32(d, b + 32), Texture3Offset = WmoChunks.I32(d, b + 36),
                                Color2 = WmoChunks.U32(d, b + 40), Flags2 = WmoChunks.U32(d, b + 44),
                            };
                        }
                        break;
                    }
                    case "MOGI":
                    {
                        int n = s / 32;
                        r.Groups = new WmoGroupInfo[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 32;
                            r.Groups[i] = new WmoGroupInfo
                            {
                                Flags = WmoChunks.U32(d, b),
                                MinX = WmoChunks.F(d, b + 4), MinY = WmoChunks.F(d, b + 8), MinZ = WmoChunks.F(d, b + 12),
                                MaxX = WmoChunks.F(d, b + 16), MaxY = WmoChunks.F(d, b + 20), MaxZ = WmoChunks.F(d, b + 24),
                                NameOffset = WmoChunks.I32(d, b + 28),
                            };
                        }
                        break;
                    }
                    case "MOGN": r._mogn = WmoChunks.Copy(d, o, s); break;
                    case "MOSB": r.Skybox = WmoChunks.CString(d, o, o + s); break;
                    case "MOPT":
                    {
                        int n = s / 20;
                        r.Portals = new WmoPortal[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 20;
                            r.Portals[i] = new WmoPortal
                            {
                                StartVertex = BitConverter.ToUInt16(d, b), VertexCount = BitConverter.ToUInt16(d, b + 2),
                                NX = WmoChunks.F(d, b + 4), NY = WmoChunks.F(d, b + 8), NZ = WmoChunks.F(d, b + 12), D = WmoChunks.F(d, b + 16),
                            };
                        }
                        break;
                    }
                    case "MOPV":
                    {
                        var pv = new float[s / 12 * 3];
                        Buffer.BlockCopy(d, o, pv, 0, pv.Length * 4);
                        r.PortalVertices = pv;
                        break;
                    }
                    case "MOPR":
                    {
                        int n = s / 8;
                        r.PortalRefs = new WmoPortalRef[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 8;
                            r.PortalRefs[i] = new WmoPortalRef { Portal = BitConverter.ToUInt16(d, b), Group = BitConverter.ToUInt16(d, b + 2), Side = BitConverter.ToInt16(d, b + 4) };
                        }
                        break;
                    }
                    case "MODS":
                    {
                        int n = s / 32;
                        modsCount = n;
                        r.DoodadSets = new WmoDoodadSet[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 32;
                            r.DoodadSets[i] = new WmoDoodadSet { Name = WmoChunks.CString(d, b, b + 20), Start = WmoChunks.I32(d, b + 20), Count = WmoChunks.I32(d, b + 24) };
                        }
                        break;
                    }
                    case "MODN": r.Modn = WmoChunks.Copy(d, o, s); break;
                    case "MODD":
                    {
                        int n = s / 40;   // by the chunk, not MOHD.nDoodadDefs (see the header comment)
                        r.DoodadDefs = new WmoDoodadDef[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 40;
                            uint w = WmoChunks.U32(d, b);
                            r.DoodadDefs[i] = new WmoDoodadDef
                            {
                                NameOffset = (int)(w & 0xFFFFFF), Flags = (int)(w >> 24),
                                X = WmoChunks.F(d, b + 4), Y = WmoChunks.F(d, b + 8), Z = WmoChunks.F(d, b + 12),
                                QX = WmoChunks.F(d, b + 16), QY = WmoChunks.F(d, b + 20), QZ = WmoChunks.F(d, b + 24), QW = WmoChunks.F(d, b + 28),
                                Scale = WmoChunks.F(d, b + 32), Color = WmoChunks.U32(d, b + 36),
                            };
                        }
                        break;
                    }
                    case "MFOG":
                    {
                        int n = s / 48;
                        r.Fogs = new WmoFog[n];
                        for (int i = 0; i < n; i++)
                        {
                            int b = o + i * 48;
                            r.Fogs[i] = new WmoFog
                            {
                                Flags = WmoChunks.U32(d, b), X = WmoChunks.F(d, b + 4), Y = WmoChunks.F(d, b + 8), Z = WmoChunks.F(d, b + 12),
                                SmallRadius = WmoChunks.F(d, b + 16), LargeRadius = WmoChunks.F(d, b + 20),
                                End0 = WmoChunks.F(d, b + 24), StartScalar0 = WmoChunks.F(d, b + 28), Color0 = WmoChunks.U32(d, b + 32),
                                End1 = WmoChunks.F(d, b + 36), StartScalar1 = WmoChunks.F(d, b + 40), Color1 = WmoChunks.U32(d, b + 44),
                            };
                        }
                        break;
                    }
                }
            }
            if (!haveHeader) throw new InvalidDataException("WMO " + path + ": no MOHD (not a root file)");
            if (r.Version != 17) throw new InvalidDataException("WMO " + path + ": version " + r.Version + " (expected 17)");

            foreach (var m in r.Materials)
            {
                m.Texture1 = r.TextureName(m.Texture1Offset);
                m.Texture2 = r.TextureName(m.Texture2Offset);
                m.Texture3 = r.TextureName(m.Texture3Offset);
            }
            for (int i = 0; i < r.Groups.Length; i++)
                r.Groups[i].Name = r._mogn != null && r.Groups[i].NameOffset >= 0 ? WmoChunks.CString(r._mogn, r.Groups[i].NameOffset, r._mogn.Length) : "";
            for (int i = 0; i < r.DoodadDefs.Length; i++)
                r.DoodadDefs[i].Name = r.DoodadName(r.DoodadDefs[i].NameOffset);
            return r;
        }

        private byte[] _mogn;

        /// <summary>A MOTX string by offset ("" when out of range or empty).</summary>
        public string TextureName(int offset)
        {
            if (offset < 0 || offset >= Motx.Length) return "";
            return WmoChunks.CString(Motx, offset, Motx.Length);
        }

        /// <summary>A MODN string by offset ("" when out of range).</summary>
        public string DoodadName(int offset)
        {
            if (offset < 0 || offset >= Modn.Length) return "";
            return WmoChunks.CString(Modn, offset, Modn.Length);
        }

        /// <summary>A MOGN string by offset ("" when out of range).</summary>
        public string GroupName(int offset)
        {
            if (_mogn == null || offset < 0 || offset >= _mogn.Length) return "";
            return WmoChunks.CString(_mogn, offset, _mogn.Length);
        }

        /// <summary>Indices into DoodadDefs for set 0 plus the requested set (no duplicates, in range).</summary>
        public List<int> DoodadsForSet(int set)
        {
            var list = new List<int>();
            var seen = new HashSet<int>();
            AddSet(0, list, seen);
            if (set > 0 && set < DoodadSets.Length) AddSet(set, list, seen);
            return list;
        }

        private void AddSet(int set, List<int> list, HashSet<int> seen)
        {
            if (set < 0 || set >= DoodadSets.Length) return;
            var s = DoodadSets[set];
            for (int i = s.Start; i < s.Start + s.Count; i++)
                if (i >= 0 && i < DoodadDefs.Length && seen.Add(i)) list.Add(i);
        }
    }

    /// <summary>A WMO group file (MOGP and its sub-chunks).</summary>
    public sealed class WmoGroup
    {
        public const uint G_BSP = 0x1, G_LIGHTMAP = 0x2, G_MOCV = 0x4, G_EXTERIOR = 0x8, G_EXTERIOR_LIT = 0x40, G_UNREACHABLE = 0x80,
            G_LIGHTS = 0x200, G_DOODADS = 0x800, G_LIQUID = 0x1000, G_INTERIOR = 0x2000, G_ALWAYSDRAW = 0x10000,
            G_OCEAN = 0x80000, G_CVERTS2 = 0x1000000, G_TVERTS2 = 0x2000000;

        public const byte P_TRANSITION = 0x01, P_NOCAMCOLLIDE = 0x02, P_DETAIL = 0x04, P_COLLISION = 0x08, P_HINT = 0x10,
            P_RENDER = 0x20, P_CULL_OBJECTS = 0x40, P_COLLIDE_HIT = 0x80;

        public string Path;
        public int Version;
        public int NameOffset, DescriptiveNameOffset;
        public uint Flags;
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public int PortalStart, PortalCount;
        public int TransBatchCount, IntBatchCount, ExtBatchCount;
        public byte[] FogIds = new byte[4];
        public int GroupLiquid;
        public int UniqueId;
        public uint Flags2;

        /// <summary>MOPY per triangle: flags and material (0xFF = collision-only).</summary>
        public byte[] PolyFlags = new byte[0];
        public byte[] PolyMaterials = new byte[0];
        /// <summary>MOVI.</summary>
        public ushort[] Indices = new ushort[0];
        /// <summary>MOVT (x, y, z) per vertex, WMO frame, yards.</summary>
        public float[] Positions = new float[0];
        /// <summary>MONR (x, y, z) per vertex.</summary>
        public float[] Normals = new float[0];
        /// <summary>First and second MOTV (u, v) per vertex (Uv1 null when absent).</summary>
        public float[] Uv0, Uv1;
        /// <summary>MOCV lighting colours (B, G, R, A per vertex), null when the group has none.</summary>
        public byte[] Colors;
        /// <summary>Second-set MOCV (only its alpha is used: two-layer blend), null when absent.</summary>
        public byte[] Colors2;
        public WmoBatch[] Batches = new WmoBatch[0];
        public WmoLiquid Liquid;
        /// <summary>Doodad refs (MODR) into the root's MODD.</summary>
        public ushort[] DoodadRefs = new ushort[0];

        public int VertexCount { get { return Positions.Length / 3; } }
        public int TriangleCount { get { return Indices.Length / 3; } }
        public bool IsExterior { get { return (Flags & G_EXTERIOR) != 0; } }
        public bool IsInterior { get { return (Flags & G_INTERIOR) != 0; } }
        /// <summary>The unified path's exterior branch test (EXTERIOR | EXTERIOR_LIT).</summary>
        public bool ExteriorLit { get { return (Flags & (G_EXTERIOR | G_EXTERIOR_LIT)) != 0; } }

        /// <summary>Collidable per the client's SMOPoly::isCollidable: F_COLLISION || (F_RENDER && !F_DETAIL).</summary>
        public static bool IsCollidable(byte polyFlags)
        {
            return (polyFlags & P_COLLISION) != 0 || ((polyFlags & P_RENDER) != 0 && (polyFlags & P_DETAIL) == 0);
        }

        /// <summary>Parses group bytes. Throws InvalidDataException when it is not a v17 group file.</summary>
        public static WmoGroup Parse(byte[] d, string path)
        {
            if (d == null) throw new InvalidDataException("WMO group " + path + ": no data");
            var g = new WmoGroup { Path = path };
            bool haveMogp = false;
            foreach (var c in WmoChunks.Walk(d, path))
            {
                if (c.Id == "MVER") { g.Version = WmoChunks.I32(d, c.Offset); continue; }
                if (c.Id != "MOGP") continue;
                haveMogp = true;
                int o = c.Offset;
                if (c.Size < 0x44) throw new InvalidDataException("WMO group " + path + ": MOGP " + c.Size + " B");
                g.NameOffset = WmoChunks.I32(d, o); g.DescriptiveNameOffset = WmoChunks.I32(d, o + 4); g.Flags = WmoChunks.U32(d, o + 8);
                g.MinX = WmoChunks.F(d, o + 12); g.MinY = WmoChunks.F(d, o + 16); g.MinZ = WmoChunks.F(d, o + 20);
                g.MaxX = WmoChunks.F(d, o + 24); g.MaxY = WmoChunks.F(d, o + 28); g.MaxZ = WmoChunks.F(d, o + 32);
                g.PortalStart = BitConverter.ToUInt16(d, o + 36); g.PortalCount = BitConverter.ToUInt16(d, o + 38);
                g.TransBatchCount = BitConverter.ToUInt16(d, o + 40); g.IntBatchCount = BitConverter.ToUInt16(d, o + 42);
                g.ExtBatchCount = BitConverter.ToUInt16(d, o + 44);
                Buffer.BlockCopy(d, o + 48, g.FogIds, 0, 4);
                g.GroupLiquid = WmoChunks.I32(d, o + 52); g.UniqueId = WmoChunks.I32(d, o + 56); g.Flags2 = WmoChunks.U32(d, o + 60);

                int motv = 0, mocv = 0;
                bool firstMocvIsLight = (g.Flags & G_MOCV) != 0;
                foreach (var s in WmoChunks.Walk(d, o + 0x44, o + c.Size, path))
                {
                    int so = s.Offset, sz = s.Size;
                    switch (s.Id)
                    {
                        case "MOPY":
                        {
                            int n = sz / 2;
                            g.PolyFlags = new byte[n];
                            g.PolyMaterials = new byte[n];
                            for (int i = 0; i < n; i++) { g.PolyFlags[i] = d[so + i * 2]; g.PolyMaterials[i] = d[so + i * 2 + 1]; }
                            break;
                        }
                        case "MOVI":
                            g.Indices = new ushort[sz / 2];
                            Buffer.BlockCopy(d, so, g.Indices, 0, g.Indices.Length * 2);
                            break;
                        case "MOVT":
                            g.Positions = new float[sz / 4];
                            Buffer.BlockCopy(d, so, g.Positions, 0, g.Positions.Length * 4);
                            break;
                        case "MONR":
                            g.Normals = new float[sz / 4];
                            Buffer.BlockCopy(d, so, g.Normals, 0, g.Normals.Length * 4);
                            break;
                        case "MOTV":
                        {
                            var uv = new float[sz / 4];
                            Buffer.BlockCopy(d, so, uv, 0, uv.Length * 4);
                            if (motv == 0) g.Uv0 = uv; else if (motv == 1) g.Uv1 = uv;
                            motv++;
                            break;
                        }
                        case "MOCV":
                        {
                            var cv = WmoChunks.Copy(d, so, sz);
                            if (mocv == 0 && firstMocvIsLight) g.Colors = cv;
                            else if (g.Colors2 == null) g.Colors2 = cv;
                            mocv++;
                            break;
                        }
                        case "MOBA":
                        {
                            int n = sz / 24;
                            g.Batches = new WmoBatch[n];
                            for (int i = 0; i < n; i++)
                            {
                                int b = so + i * 24;
                                g.Batches[i] = new WmoBatch
                                {
                                    StartIndex = WmoChunks.I32(d, b + 12), IndexCount = BitConverter.ToUInt16(d, b + 16),
                                    MinIndex = BitConverter.ToUInt16(d, b + 18), MaxIndex = BitConverter.ToUInt16(d, b + 20),
                                    Flags = d[b + 22], Material = d[b + 23],
                                };
                            }
                            break;
                        }
                        case "MODR":
                            g.DoodadRefs = new ushort[sz / 2];
                            Buffer.BlockCopy(d, so, g.DoodadRefs, 0, g.DoodadRefs.Length * 2);
                            break;
                        case "MLIQ":
                            g.Liquid = ParseLiquid(d, so, sz, path);
                            break;
                    }
                }
            }
            if (!haveMogp) throw new InvalidDataException("WMO group " + path + ": no MOGP");
            return g;
        }

        private static WmoLiquid ParseLiquid(byte[] d, int o, int size, string path)
        {
            if (size < 30) return null;
            var l = new WmoLiquid
            {
                XVerts = WmoChunks.I32(d, o), YVerts = WmoChunks.I32(d, o + 4), XTiles = WmoChunks.I32(d, o + 8), YTiles = WmoChunks.I32(d, o + 12),
                CornerX = WmoChunks.F(d, o + 16), CornerY = WmoChunks.F(d, o + 20), CornerZ = WmoChunks.F(d, o + 24),
                MaterialId = BitConverter.ToUInt16(d, o + 28),
            };
            long nv = (long)l.XVerts * l.YVerts, nt = (long)l.XTiles * l.YTiles;
            if (l.XVerts < 0 || l.YVerts < 0 || l.XTiles < 0 || l.YTiles < 0 || 30 + nv * 8 + nt > size)
                throw new InvalidDataException("WMO group " + path + ": MLIQ " + l.XVerts + "x" + l.YVerts + " verts / " + l.XTiles + "x" + l.YTiles + " tiles does not fit " + size + " B");
            l.Heights = new float[nv];
            l.VertexBytes = new byte[nv * 4];
            int p = o + 30;
            for (int i = 0; i < nv; i++, p += 8)
            {
                Buffer.BlockCopy(d, p, l.VertexBytes, i * 4, 4);
                l.Heights[i] = BitConverter.ToSingle(d, p + 4);
            }
            l.Tiles = WmoChunks.Copy(d, p, (int)nt);
            return l;
        }
    }

    /// <summary>Chunk walking and little-endian reads shared by the WMO reader.</summary>
    public static class WmoChunks
    {
        public struct Chunk
        {
            public string Id;
            public int Offset;   // payload start
            public int Size;     // payload size
        }

        public static IEnumerable<Chunk> Walk(byte[] d, string path) { return Walk(d, 0, d.Length, path); }

        /// <summary>Walks chunks in [start, end). Throws when a chunk runs past the end (a truncated or foreign file).</summary>
        public static IEnumerable<Chunk> Walk(byte[] d, int start, int end, string path)
        {
            int p = start;
            while (p + 8 <= end)
            {
                string id = new string(new[] { (char)d[p + 3], (char)d[p + 2], (char)d[p + 1], (char)d[p] });
                int size = BitConverter.ToInt32(d, p + 4);
                if (size < 0 || p + 8L + size > end)
                    throw new InvalidDataException("WMO " + path + ": chunk " + id + " at " + p + " size " + size + " runs past " + end);
                yield return new Chunk { Id = id, Offset = p + 8, Size = size };
                p += 8 + size;
            }
        }

        public static int I32(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        public static uint U32(byte[] d, int o) { return BitConverter.ToUInt32(d, o); }
        public static float F(byte[] d, int o) { return BitConverter.ToSingle(d, o); }

        public static byte[] Copy(byte[] d, int o, int n)
        {
            var b = new byte[Math.Max(0, n)];
            if (n > 0) Buffer.BlockCopy(d, o, b, 0, n);
            return b;
        }

        /// <summary>Zero-terminated string starting at o (stops at end). Windows-1252 bytes map 1:1 for the ASCII paths used.</summary>
        public static string CString(byte[] d, int o, int end)
        {
            int e = o;
            while (e < end && e < d.Length && d[e] != 0) e++;
            if (e <= o) return "";
            var sb = new StringBuilder(e - o);
            for (int i = o; i < e; i++) sb.Append((char)d[i]);
            return sb.ToString();
        }
    }

    /// <summary>Loads a WMO root and all its groups from the game files.</summary>
    public sealed class WmoModel
    {
        public WmoRoot Root;
        /// <summary>One entry per MOGI group; null where the group file is missing or broken (see GroupErrors).</summary>
        public WmoGroup[] Groups;
        public string[] GroupErrors;
        public long ParseMs;

        /// <summary>Reads + parses. Throws FileNotFoundException when the root is missing, InvalidDataException when broken.
        /// Missing/broken group files do not throw (their slot is null, the reason in GroupErrors).</summary>
        public static WmoModel Load(WowFiles files, string rootPath)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bytes = files.Read(rootPath);
            if (bytes == null) throw new FileNotFoundException("WMO not in the game files: " + rootPath);
            var m = new WmoModel { Root = WmoRoot.Parse(bytes, rootPath) };
            int n = Math.Max(m.Root.NGroups, m.Root.Groups.Length);
            m.Groups = new WmoGroup[n];
            m.GroupErrors = new string[n];
            for (int i = 0; i < n; i++)
            {
                string gp = WmoRoot.GroupPath(rootPath, i);
                try
                {
                    var gb = files.Read(gp);
                    if (gb == null) { m.GroupErrors[i] = "missing " + gp; continue; }
                    m.Groups[i] = WmoGroup.Parse(gb, gp);
                }
                catch (Exception e) { m.GroupErrors[i] = e.Message; }
            }
            m.ParseMs = sw.ElapsedMilliseconds;
            return m;
        }
    }
}
