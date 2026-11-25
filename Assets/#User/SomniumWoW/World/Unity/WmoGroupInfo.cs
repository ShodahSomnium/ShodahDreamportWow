using UnityEngine;

// Iteration 7 (Body, PLAN7 s3.6): its own namespace because `SomniumSpace.Worlds.SomniumWoW.World.WmoGroupInfo` is already the
// MOGI struct of World/WmoFile.cs. Use it as `SomniumSpace.Worlds.SomniumWoW.World.Tags.WmoGroupInfo` (or a using alias) from code
// that also imports ...World, where the bare name means the struct.
namespace SomniumSpace.Worlds.SomniumWoW.World.Tags
{
    /// <summary>
    /// The ids of one built WMO group (PLAN7 s3.6), on every "Group NNN ..." GameObject WmoFactory.Build makes (the one carrying
    /// the group's MeshCollider): what MountPlace needs for AzerothCore's indoor rule - the WMOAreaTable key (WmoId = the root's
    /// MOHD id, NameSet = the MODF placement's NameSet, GroupId = the MOGP UniqueId) and the MOGP flags (0x8 exterior,
    /// 0x2000 interior). Data only; no Update.
    /// </summary>
    public sealed class WmoGroupInfo : MonoBehaviour
    {
        public int WmoId, NameSet, GroupId;
        public uint MogpFlags;
        public string GroupName, RootPath;
    }
}
