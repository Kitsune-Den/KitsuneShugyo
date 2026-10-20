using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.Scripting;

// Phase 3 anti-grind: blocks you placed yourself don't teach.
//
// The loop: place a block, hit it with a pickaxe until it breaks, pick it up,
// place it again, all for Strength. 7DTD keeps no "player placed" flag on
// ordinary blocks (only on tile entities like chests), so this remembers the
// positions the local player places blocks at, for the game session, and the
// attribute block-hit lines (ShugyoPractice skip_placed="true") skip hits on
// them. Upgrading a block you placed still counts: that's building, and it
// costs materials.
//
// The hit position isn't in the onSelfDamagedBlock event, so a prefix on
// ItemActionAttack.Hit (which fires that event) notes the block being hit
// for the duration of the call.
public static class Shugyo_PlacedBlocks
{
    // Enough for a big build session; past it the oldest are forgotten.
    private const int Capacity = 8192;

    private static readonly HashSet<Vector3i> placed = new HashSet<Vector3i>();
    private static readonly Queue<Vector3i> order = new Queue<Vector3i>();

    private static Vector3i? currentHit;

    public static void Remember(Vector3i pos)
    {
        if (!placed.Add(pos)) return;
        order.Enqueue(pos);
        while (order.Count > Capacity)
        {
            placed.Remove(order.Dequeue());
        }
    }

    // True while the hit being processed is on a block the player placed.
    public static bool HittingPlaced()
    {
        return currentHit.HasValue && placed.Contains(currentHit.Value);
    }

    public static void BeginHit(Vector3i pos)
    {
        currentHit = pos;
    }

    public static void EndHit()
    {
        currentHit = null;
    }
}

[HarmonyPatch(typeof(ItemActionAttack))]
[HarmonyPatch("Hit")]
public class Shugyo_BlockHitPositionPatch
{
    public static void Prefix(WorldRayHitInfo hitInfo)
    {
        if (hitInfo != null && hitInfo.bHitValid) Shugyo_PlacedBlocks.BeginHit(hitInfo.hit.blockPos);
    }

    public static void Finalizer()
    {
        Shugyo_PlacedBlocks.EndHit();
    }
}

// <triggered_effect trigger="onSelfPlaceBlock" action="ShugyoPlaced, KitsuneShugyo"/>
// Vanilla sets the event's Position to the placed block before firing it.
[Preserve]
public class MinEventActionShugyoPlaced : MinEventActionTargetedBase
{
    public override void Execute(MinEventParams _params)
    {
        if (targets == null || _params == null || !Shugyo_Attributes.Enabled) return;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is EntityPlayerLocal player && !player.isEntityRemote)
                {
                    Shugyo_PlacedBlocks.Remember(World.worldToBlockPos(_params.Position));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] placed-block note failed: " + ex);
        }
    }
}
