using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

// Phase 3 anti-grind: practice fatigue, one general brake on loops.
//
// Every practice point an attribute gets also tires it. $shugyo_fatigue_<attribute>
// counts in fractions of Settings.xml's `fatigue` (practice points): at 1 or
// more the attribute is tired and practice counts at a quarter (TiredRate). It
// drains with in-game time, `rest` hours from fully tired to fresh, so a long
// session still pays and a zombie farm or a place-and-break loop pays much less.
//
//   SLOW Strength 3 -> 4 is 120 points. With fatigue 60 and rest 6 h, about
//   60 club kills inside an in-game afternoon tire it; every kill after that
//   is worth a quarter until it rests.
//
// The value is a plain 0..n cvar on purpose: KitsuneUI's Practice add-on reads
// it (>= 1 shows "tired"), and so can any XML requirement.
//
// Draining runs on buffShugyoLearn's onSelfBuffUpdate through the ShugyoRest
// action below, measured in world time ($shugyo_rest_t, in-game hours), so
// time that passes while you're away from the attribute, or logged out on a
// server that keeps running, counts as rest.
public static class Shugyo_Fatigue
{
    public const float TiredRate = 0.25f;
    public const string ClockVar = "$shugyo_rest_t";
    private const float MinStepHours = 0.05f;

    private static readonly string[] Attributes =
    {
        Shugyo_Attributes.Perception, Shugyo_Attributes.Strength, Shugyo_Attributes.Fortitude,
        Shugyo_Attributes.Agility, Shugyo_Attributes.Intellect,
    };

    // "You're getting diminishing returns" shows once per attribute per game
    // session, so a long grind gets one quiet line, not a stream.
    private static readonly HashSet<string> hinted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string Var(string attribute)
    {
        return "$shugyo_fatigue_" + attribute;
    }

    // Practice points before an attribute tires on this player's server, and
    // in-game hours of rest from tired to fresh. 0 points = fatigue is off.
    public static float Points(EntityPlayerLocal player)
    {
        return ServerValue(player, Shugyo_Settings.FatigueVar, Shugyo_Settings.Fatigue, 0f);
    }

    public static float RestHours(EntityPlayerLocal player)
    {
        return ServerValue(player, Shugyo_Settings.RestVar, Shugyo_Settings.Rest, 0.1f);
    }

    private static float ServerValue(EntityPlayerLocal player, string var, float fallback, float min)
    {
        if (player?.Buffs != null && player.Buffs.HasCustomVar(var))
        {
            return Math.Max(min, player.Buffs.GetCustomVar(var));
        }
        return fallback;
    }

    // Tires the attribute by `points` and returns what they're worth now:
    // full while fresh, TiredRate while tired. Fatigue counts the raw points,
    // so grinding on while tired keeps it tired.
    public static float Apply(EntityPlayerLocal player, string attribute, float points)
    {
        float limit = Points(player);
        if (limit <= 0f || player?.Buffs == null) return points;

        Rest(player);
        string var = Var(attribute);
        float fatigue = player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : 0f;
        bool tired = fatigue >= 1f;
        fatigue += points / limit;
        player.Buffs.SetCustomVar(var, fatigue, true, CVarOperation.set);

        if (!tired && fatigue >= 1f && hinted.Add(attribute))
        {
            ProgressionValue value = Shugyo_Attributes.Attribute(player, attribute);
            string name = value != null ? Shugyo_Practice.DisplayName(value.ProgressionClass) : attribute;
            GameManager.ShowTooltip(player, "You're getting diminishing returns on " + name
                + ". Take a break or try something else.", false, false, 0f);
        }
        return tired ? points * TiredRate : points;
    }

    // Drains every attribute's fatigue by the in-game time since the last call.
    public static void Rest(EntityPlayerLocal player)
    {
        World world = GameManager.Instance?.World;
        if (world == null || player?.Buffs == null || player.isEntityRemote) return;

        float now = world.worldTime / 1000f;
        EntityBuffs buffs = player.Buffs;
        float last = buffs.HasCustomVar(ClockVar) ? buffs.GetCustomVar(ClockVar) : now;
        float hours = now - last;
        if (hours < 0f || !buffs.HasCustomVar(ClockVar))
        {
            // First call, or the world clock went backwards (admin settime):
            // just restart the clock.
            buffs.SetCustomVar(ClockVar, now, true, CVarOperation.set);
            return;
        }
        // The buff ticks every second; settle in steps of a few in-game
        // minutes so the cvars aren't re-sent every tick.
        if (hours < MinStepHours) return;

        float drain = hours / RestHours(player);
        foreach (string attribute in Attributes)
        {
            string var = Var(attribute);
            if (!buffs.HasCustomVar(var)) continue;
            float fatigue = buffs.GetCustomVar(var);
            if (fatigue <= 0f) continue;
            buffs.SetCustomVar(var, Math.Max(0f, fatigue - drain), true, CVarOperation.set);
        }
        buffs.SetCustomVar(ClockVar, now, true, CVarOperation.set);
    }
}

// <triggered_effect trigger="onSelfBuffUpdate" action="ShugyoRest, KitsuneShugyo"/>
// The once-a-second tick: drains fatigue, and checks sneaking (Shugyo_Stealth).
[Preserve]
public class MinEventActionShugyoRest : MinEventActionTargetedBase
{
    public override void Execute(MinEventParams _params)
    {
        if (targets == null || !Shugyo_Attributes.Enabled) return;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is EntityPlayerLocal player && !player.isEntityRemote)
                {
                    Shugyo_Fatigue.Rest(player);
                    Shugyo_Stealth.Tick(player);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] fatigue rest failed: " + ex);
        }
    }
}
