using System;
using HarmonyLib;
using UnityEngine;

// Optional: fewer skill points late in the game.
//
// With attributes earned by practice, skill points only buy perks, so a long
// game can pile them up. Settings.xml's `points_after` (a player level; 0, the
// default, turns this off) and `points_every` (default 2) make levels past
// points_after grant points only every points_every-th level: with 100 and 2,
// levels 102, 104, ... grant and 101, 103, ... don't.
//
// Vanilla grants points inside Progression.AddLevelExpRecursive, which calls
// itself once per extra level. The outermost call records the level it
// started at; when it returns, the points vanilla gave for each level that
// shouldn't grant are taken back. Both values come from buffShugyoPace, so the
// server's settings win. (Vanilla's level-up tooltip inside that call still
// shows the point total before the adjustment.)
[HarmonyPatch(typeof(Progression))]
[HarmonyPatch("AddLevelExpRecursive")]
public class Shugyo_SkillPointRatePatch
{
    public const string AfterVar = "$shugyo_points_after";
    public const string EveryVar = "$shugyo_points_every";

    private static int depth;
    private static int startLevel;

    public static void Prefix(Progression __instance)
    {
        if (depth++ == 0 && __instance != null) startLevel = __instance.Level;
    }

    public static void Finalizer(Progression __instance)
    {
        if (--depth != 0) return;
        try
        {
            if (!Shugyo_Attributes.Enabled || __instance == null) return;
            if (!(__instance.parent is EntityPlayerLocal player) || player.isEntityRemote) return;
            int after = (int)Value(player, AfterVar, Shugyo_Settings.PointsAfter);
            int every = Math.Max(1, (int)Value(player, EveryVar, Shugyo_Settings.PointsEvery));
            if (after <= 0 || every <= 1 || __instance.Level <= startLevel) return;

            int takeBack = 0;
            for (int level = Math.Max(startLevel + 1, after + 1); level <= __instance.Level; level++)
            {
                if ((level - after) % every != 0) takeBack += VanillaPointsFor(level);
            }
            if (takeBack > 0)
            {
                __instance.SkillPoints = Math.Max(0, __instance.SkillPoints - takeBack);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] skill point rate failed: " + ex);
        }
    }

    // What vanilla adds at `level` (see AddLevelExpRecursive).
    private static int VanillaPointsFor(int level)
    {
        if (Progression.SkillPointsGainRate <= 0 || level % Progression.SkillPointsGainRate != 0) return 0;
        if (Progression.SkillPointMultiplier == 0f) return Progression.SkillPointsPerLevel;
        return (int)Math.Min(Progression.SkillPointsPerLevel * Mathf.Pow(Progression.SkillPointMultiplier, level),
            2.1474836E+09f);
    }

    private static float Value(EntityPlayerLocal player, string var, float fallback)
    {
        return player.Buffs != null && player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : fallback;
    }
}
