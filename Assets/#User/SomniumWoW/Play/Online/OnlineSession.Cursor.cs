using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Lead fix (2026-10-03, the user: "wrong icon for vendors", "Wrong icons for when gathering ore"): what the mouse cursor shows over a
    // gameobject - its gameobject_template IconName (SMSG_GAMEOBJECT_QUERY_RESPONSE, QueryHandler.cpp:199 "Icon name to use instead of default
    // icon for go's"), and for gathering nodes / locked chests the skill key of its lock (Lock.dbc: Type[8] @1, Index[8] @9, Skill[8] @17;
    // LOCK_KEY_SKILL = 2; Index = the LockType: 1 PICKLOCK, 2 HERBALISM, 3 MINING, 19 FISHING - SharedDefines.h:2588-2614). Creatures carry
    // their creature_template IconName and rank from the creature query (OnlineSession.Objects.cs). The Game layer picks the art (UiCursor).
    public sealed partial class OnlineSession
    {
        const int CurLockSkillField = 17;

        /// <summary>Fills EntityState.CursorName / GatherLockType / GatherSkillNeeded of a gameobject from what is known (the GO query answer
        /// - names and IconName from Objects.cs, the type data from Quests.cs - and Lock.dbc). Cheap; called when either answer arrives.</summary>
        void CurGoRefresh(EntityState s)
        {
            if (s == null || s.Kind != EntityKind.GameObject) return;
            string icon;
            s.CursorName = _goIcons.TryGetValue(s.Entry, out icon) && !string.IsNullOrEmpty(icon) ? icon : null;
            s.GatherLockType = 0;
            s.GatherSkillNeeded = 0;
            int lockId = QGoLockId(s.Entry, s.GoType);
            if (lockId <= 0) return;
            QLoadDbcs();
            if (_qLock == null) return;
            int r = _qLock.FindRow(lockId);
            if (r < 0) return;
            for (int i = 0; i < 8; i++)
            {
                if (_qLock.GetInt(r, QLockType + i) != QLockKeySkill) continue;
                s.GatherLockType = _qLock.GetInt(r, QLockIndex + i);
                s.GatherSkillNeeded = Math.Max(0, _qLock.GetInt(r, CurLockSkillField + i));
                return;
            }
        }

        /// <summary>The type data of a gameobject entry arrived (Quests.cs OnQGameObjectQuery): the lock is known now.</summary>
        void CurGoDataArrived(int entry)
        {
            foreach (var o in _objects.Values)
                if (o.TypeId == TypeId.GameObject && o.State != null && o.Entry == entry) CurGoRefresh(o.State);
        }
    }
}
