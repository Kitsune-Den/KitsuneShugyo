using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine.Scripting;

// The practice meter: ONE CVar per crafting skill, $shugyo_<skill>, counting
// in fractions of a level. Every source adds to it:
//
//   - Config/buffs.xml (kills, tool hits, food and medical use) calls the
//     ShugyoSkill action below, which adds one action of that source.
//   - This DLL (crafting, workstations, vehicle repairs, wiring, looting) adds
//     through Add(), which levels the skill the same way.
//   - Magazines add Settings.xml's `magazine` value per read (0.25 = a
//     quarter of a level), shared with the party (see Shugyo_Sharing).
//
// So a skill fed by several sources fills one bar: 40 salvage hits and two
// magazines are one level at SLOW (40/80 + 2 x 0.25).
//
// Floats: 80 additions of 1/80 can land a hair under 1.0, so a level is due
// at Threshold (0.999), the same number the XML compares against.
public static class Shugyo_Practice
{
    public const string MeterPrefix = "$shugyo_";
    public const float Threshold = 0.999f;

    // The vanilla spelling of every crafting skill. The game lowercases
    // ProgressionClass names on load, but CVar names are case-sensitive and
    // the XML uses these spellings, so every meter name goes through here.
    private static readonly Dictionary<string, string> Canonical = BuildCanonical(
        "craftingHarvestingTools", "craftingRepairTools", "craftingSalvageTools", "craftingKnuckles",
        "craftingBlades", "craftingClubs", "craftingSledgehammers", "craftingBows", "craftingSpears",
        "craftingHandguns", "craftingShotguns", "craftingRifles", "craftingMachineGuns", "craftingExplosives",
        "craftingRobotics", "craftingArmor", "craftingBackpack", "craftingMedical", "craftingFood",
        "craftingSeeds", "craftingElectrician", "craftingTraps", "craftingWorkstations", "craftingVehicles");

    private static Dictionary<string, string> BuildCanonical(params string[] names)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names) map[name] = name;
        return map;
    }

    // Every crafting skill, in vanilla spelling (for the shugyo command).
    public static IEnumerable<string> SkillNames => Canonical.Values;

    public static bool IsSkill(string progressionName)
    {
        return progressionName != null && Canonical.ContainsKey(progressionName);
    }

    public static string SkillName(string progressionName)
    {
        return Canonical.TryGetValue(progressionName, out string name) ? name : progressionName;
    }

    public static string MeterVar(string progressionName)
    {
        return MeterPrefix + SkillName(progressionName);
    }

    public static ProgressionValue CraftingSkill(EntityAlive player, string progressionName)
    {
        if (player == null || player.Progression == null || string.IsNullOrEmpty(progressionName)) return null;
        ProgressionValue value = player.Progression.GetProgressionValue(progressionName);
        if (value == null || value.ProgressionClass == null || !value.ProgressionClass.IsCrafting) return null;
        return value;
    }

    // `count` actions of the source `key` (a Settings.xml name: craft,
    // station, vehicle, wire, loot). The step comes from the cvar the pace
    // buff seeds, so a dedicated server's settings win; the default is only a
    // fallback for the moment before that buff applies.
    // The key is also the source the practice is recorded under (Track).
    // `levelUpMessage` replaces the default line shown when the skill levels.
    public static int AddActions(EntityPlayerLocal player, string progressionName, int count, string key,
        float defaultActionsPerLevel, string levelUpMessage = null)
    {
        float step = Step(player, key, defaultActionsPerLevel);
        if (string.IsNullOrEmpty(levelUpMessage))
        {
            return Add(player, progressionName, step * Math.Max(1, count), null, key);
        }
        int gained = Add(player, progressionName, step * Math.Max(1, count), "", key);
        if (gained > 0) GameManager.ShowTooltip(player, levelUpMessage, false, false, 0f);
        return gained;
    }

    // Where practice came from, for the Practice tab's "top sources":
    // $shugyo_src_<meter>_<source> totals what each source has added to that
    // meter (levels for crafting skills, practice points for attributes) since
    // 0.5.0. Sources are short names: weapon, tool, wreck, use, craft,
    // station, vehicle, wire, loot, magazine, shared for crafting skills;
    // kill, hit, upgrade, hurt, heal, sneak, stealth, headshot, loot, craft,
    // wire, vehicle, read, trade for attributes.
    public const string SourcePrefix = "$shugyo_src_";

    public static void Track(EntityPlayerLocal player, string meter, string source, float amount)
    {
        if (player?.Buffs == null || string.IsNullOrEmpty(source) || amount <= 0f) return;
        string var = SourcePrefix + meter + "_" + source;
        float total = player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : 0f;
        player.Buffs.SetCustomVar(var, total + amount, true, CVarOperation.set);
    }

    public static float Step(EntityPlayerLocal player, string key, float defaultActionsPerLevel)
    {
        string var = Shugyo_Settings.StepVar(key);
        if (player?.Buffs != null && player.Buffs.HasCustomVar(var))
        {
            float step = player.Buffs.GetCustomVar(var);
            if (step > 0f) return step;
        }
        return 1f / Math.Max(1f, defaultActionsPerLevel);
    }

    // What one magazine read is worth, in levels, on this player's server.
    public static float MagazineValue(EntityPlayerLocal player)
    {
        if (player?.Buffs != null && player.Buffs.HasCustomVar(Shugyo_Settings.MagazineVar))
        {
            float value = player.Buffs.GetCustomVar(Shugyo_Settings.MagazineVar);
            if (value > 0f) return value;
        }
        return Shugyo_Settings.Magazine;
    }

    // Adds `levels` to the skill's meter and levels it while the meter is
    // full. Returns the levels gained. `tooltip` replaces the default
    // level-up line; pass "" for silence. `source` is recorded (Track).
    public static int Add(EntityPlayerLocal player, string progressionName, float levels, string tooltip,
        string source = null)
    {
        ProgressionValue value = CraftingSkill(player, progressionName);
        if (value == null || player.Buffs == null || levels <= 0f) return 0;

        ProgressionClass cls = value.ProgressionClass;
        string var = MeterVar(progressionName);

        // Nothing left to learn: keep the meter empty rather than letting it
        // grow forever.
        if (value.Level >= cls.MaxLevel)
        {
            player.Buffs.SetCustomVar(var, 0f, true, CVarOperation.set);
            return 0;
        }

        Track(player, SkillName(progressionName), source, levels);
        float meter = (player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : 0f) + levels;
        int oldLevel = value.Level;
        while (meter >= Threshold && value.Level < cls.MaxLevel)
        {
            value.Level += 1;
            meter -= 1f;
        }
        if (meter < 0f || value.Level >= cls.MaxLevel) meter = 0f;
        player.Buffs.SetCustomVar(var, meter, true, CVarOperation.set);

        int gained = value.Level - oldLevel;
        if (gained > 0) Announce(player, value, oldLevel);

        if (tooltip == null)
        {
            if (gained > 0) GameManager.ShowTooltip(player, "Your " + DisplayName(cls) + " knowledge grows.", false, false, 0f);
        }
        else if (tooltip.Length != 0)
        {
            GameManager.ShowTooltip(player, tooltip, false, false, 0f);
        }
        return gained;
    }

    // "Vehicles: 75% to level 6", or "Vehicles: level 6!" when a read levelled.
    public static string Progress(EntityPlayerLocal player, string progressionName, int gained)
    {
        ProgressionValue value = CraftingSkill(player, progressionName);
        if (value == null) return "";
        string name = DisplayName(value.ProgressionClass);
        if (value.Level >= value.ProgressionClass.MaxLevel) return name + " is mastered.";
        if (gained > 0) return name + " rises to level " + value.Level + "!";
        float meter = player.Buffs.HasCustomVar(MeterVar(progressionName)) ? player.Buffs.GetCustomVar(MeterVar(progressionName)) : 0f;
        int percent = (int)Math.Floor(Math.Min(meter, Threshold) * 100f);
        return name + ": " + percent.ToString(CultureInfo.InvariantCulture) + "% to level " + (value.Level + 1);
    }

    public static string DisplayName(ProgressionClass cls)
    {
        string name = Localization.Get(cls.NameKey, false);
        return string.IsNullOrEmpty(name) ? cls.Name : name;
    }

    // What vanilla's AddProgressionLevel does for the local player, so a DLL
    // level-up shows the same skill notification and recipe-unlock lines.
    private static void Announce(EntityPlayerLocal player, ProgressionValue value, int oldLevel)
    {
        player.Progression.bProgressionStatsChanged = true;
        player.bPlayerStatsChanged = true;
        if (XUiM_Recipes.CraftingProgression)
        {
            player.PlayerUI?.xui?.CollectedItemList?.AddCraftingSkillNotification(value);
            value.ProgressionClass.HandleCheckCrafting(player, oldLevel, value.Level);
        }
    }
}

// <triggered_effect trigger="onSelfKilledOther" action="ShugyoSkill, KitsuneShugyo"
//                   skill="craftingClubs" step="weapon" message="Your club crafting knowledge grows.">
//     <requirement name="HoldingItemHasTags" tags="clubSkill"/>
// </triggered_effect>
// The XML half of crafting-skill practice: one action of the Settings.xml
// source `step`, added through Add() like the DLL's own sources. Before 0.5.0
// this was four XML lines per source ending in vanilla's AddProgressionLevel,
// which multiplies a +1 by the PointsPerMagazine sandbox option, so a server
// with that above 1 levelled every skill that many times per full meter.
[Preserve]
public class MinEventActionShugyoSkill : MinEventActionTargetedBase
{
    private string skill;
    private string step;
    private string message;

    public override bool CanExecute(MinEventTypes _eventType, MinEventParams _params)
    {
        return base.CanExecute(_eventType, _params) && skill != null && step != null;
    }

    public override void Execute(MinEventParams _params)
    {
        if (targets == null || !Shugyo_Attributes.Enabled) return;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is EntityPlayerLocal player && !player.isEntityRemote)
                {
                    Shugyo_Practice.AddActions(player, skill, 1, step, Shugyo_Settings.ActionsPerLevel(step), message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] skill practice failed: " + ex);
        }
    }

    public override bool ParseXmlAttribute(XAttribute _attribute)
    {
        bool flag = base.ParseXmlAttribute(_attribute);
        if (!flag)
        {
            switch (_attribute.Name.LocalName)
            {
            case "skill":
                skill = _attribute.Value;
                break;
            case "step":
                step = _attribute.Value;
                break;
            case "message":
                message = _attribute.Value;
                break;
            }
        }
        return flag;
    }
}

// One-time carry-over from Kitsune Learned Skills. KLS kept a separate
// counter per XML line ($kls_clubs, $kls_salvage, ...) and one per skill for
// its DLL sources ($kls_craft_<skill>), each measured against its own pace.
// Each counter becomes counter / pace levels on the new meter, then is
// removed. Levels themselves are ordinary progression and carry over as is.
[HarmonyPatch(typeof(EntityPlayerLocal))]
[HarmonyPatch("AfterPlayerRespawn")]
public class Shugyo_MigrationPatch
{
    public const string FlagVar = "$shugyo_migrated";

    // KLS XML counter -> (skill, KLS pace cvar, KLS SLOW pace).
    private static readonly (string counter, string skill, string paceVar, float pace)[] XmlCounters =
    {
        ("$kls_clubs", "craftingClubs", "$kls_pace_weapon", 16f),
        ("$kls_blades", "craftingBlades", "$kls_pace_weapon", 16f),
        ("$kls_spears", "craftingSpears", "$kls_pace_weapon", 16f),
        ("$kls_sledges", "craftingSledgehammers", "$kls_pace_weapon", 16f),
        ("$kls_knuckles", "craftingKnuckles", "$kls_pace_weapon", 16f),
        ("$kls_bows", "craftingBows", "$kls_pace_weapon", 16f),
        ("$kls_handguns", "craftingHandguns", "$kls_pace_weapon", 16f),
        ("$kls_shotguns", "craftingShotguns", "$kls_pace_weapon", 16f),
        ("$kls_rifles", "craftingRifles", "$kls_pace_weapon", 16f),
        ("$kls_machineguns", "craftingMachineGuns", "$kls_pace_weapon", 16f),
        ("$kls_explosives", "craftingExplosives", "$kls_pace_weapon", 16f),
        ("$kls_harvest", "craftingHarvestingTools", "$kls_pace_tool", 80f),
        ("$kls_salvage", "craftingSalvageTools", "$kls_pace_tool", 80f),
        ("$kls_wreck", "craftingVehicles", "$kls_pace_wreck", 80f),
        ("$kls_repair", "craftingRepairTools", "$kls_pace_tool", 80f),
        ("$kls_food", "craftingFood", "$kls_pace_use", 8f),
        ("$kls_medical", "craftingMedical", "$kls_pace_use", 8f),
    };

    private static readonly string[] OldPaceVars =
    {
        "$kls_pace_weapon", "$kls_pace_tool", "$kls_pace_use", "$kls_pace_craft", "$kls_pace_station",
        "$kls_pace_vehicle", "$kls_pace_wire", "$kls_pace_loot", "$kls_pace_wreck",
    };

    public static void Postfix(EntityPlayerLocal __instance, RespawnType _type)
    {
        if (_type != RespawnType.LoadedGame && _type != RespawnType.JoinMultiplayer) return;
        try
        {
            Migrate(__instance);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] carrying over Learned Skills progress failed: " + ex);
        }
    }

    private static void Migrate(EntityPlayerLocal player)
    {
        EntityBuffs buffs = player?.Buffs;
        if (buffs == null || player.isEntityRemote || buffs.HasCustomVar(FlagVar)) return;

        int carried = 0;
        foreach (var entry in XmlCounters)
        {
            if (Carry(player, entry.counter, entry.skill, Pace(buffs, entry.paceVar, entry.pace))) carried++;
        }

        float craftPace = Pace(buffs, "$kls_pace_craft", 8f);
        var dllCounters = new List<KeyValuePair<string, float>>(buffs.EnumerateCustomVars("$kls_craft_", true));
        foreach (var counter in dllCounters)
        {
            string skill = counter.Key.Substring("$kls_craft_".Length);
            if (Carry(player, counter.Key, skill, craftPace)) carried++;
        }

        foreach (string pace in OldPaceVars)
        {
            if (buffs.HasCustomVar(pace)) buffs.RemoveCustomVar(pace);
        }

        buffs.SetCustomVar(FlagVar, 1f, true, CVarOperation.set);
        if (carried > 0)
        {
            Log.Out("[KitsuneShugyo] Carried over Learned Skills progress on " + carried + " skill counter(s) for "
                + player.EntityName);
        }
    }

    private static float Pace(EntityBuffs buffs, string paceVar, float fallback)
    {
        if (buffs.HasCustomVar(paceVar))
        {
            float pace = buffs.GetCustomVar(paceVar);
            if (pace >= 1f) return pace;
        }
        return fallback;
    }

    private static bool Carry(EntityPlayerLocal player, string counterVar, string skill, float pace)
    {
        EntityBuffs buffs = player.Buffs;
        if (!buffs.HasCustomVar(counterVar)) return false;
        float count = buffs.GetCustomVar(counterVar);
        buffs.RemoveCustomVar(counterVar);
        if (count <= 0f) return false;
        Shugyo_Practice.Add(player, skill, count / pace, "");
        return true;
    }
}
