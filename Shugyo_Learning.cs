using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

// Kitsune Shugyo: the learn-by-doing DLL half.
//
// The XML half (Config/buffs.xml) covers learn-by-USING for 17 sources
// (kills with weapons, block damage with tools, wreck salvage, consuming
// food/medical). This DLL covers everything the XML can't reach. Every source
// feeds the skill's practice meter (see Shugyo_Practice); the numbers below
// are Settings.xml's actions per level.
//
//   1. Learn by crafting (all skills): each completed craft reads the
//      crafted item's (or block's) UnlockedBy property and adds progress to
//      that skill. `craft` crafted items = +1 level.
//      Two entry points, mutually exclusive by vanilla design:
//        - EntityPlayerLocal.GiveExp(CraftCompleteData): workstation queue
//          output (TileEntityWorkstation always routes through its
//          CraftCompleteList, even with the window open).
//        - XUiC_RecipeStack.giveExp: the backpack crafting queue.
//
//   2. Workstation skill: every workstation-queue completion ALSO credits
//      craftingWorkstations. Campfires count, which is the bootstrap: the
//      campfire is craftable at level 1, and cooking on it teaches you your
//      way to the forge (unlocks at 4). `station` completions = +1.
//
//   3. Vehicle skill: XUiM_Vehicle.RepairVehicle (the repair button in the
//      vehicle UI, client-side, consumes a repair kit) credits
//      craftingVehicles on success. `vehicle` repairs = +1.
//
//   4. Electrician skill: TileEntityPowered.SetParentWithWireTool runs on
//      the wiring player's client once per completed wire connection.
//      `wire` connections = +1.
//
//   5. Gateway crafts (always-unlocked recipes hard-mapped to a skill,
//      because their line's first UnlockedBy craftable sits above level 1):
//        spikes + barbed fence -> craftingTraps   (dart traps unlock at 25)
//        farm plots            -> craftingSeeds   (first seeds unlock at 2)
//
//   6. Backpack skill: XUiC_LootWindowGroup.OpenLooting runs on the looting
//      player's client; _firstTimeTouched is true only for the first open of
//      a fresh, non-player container, so each container counts once and
//      re-opening your own storage can't be farmed. Not the
//      onSelfOpenLootContainer XML trigger: it fires TWICE per fresh
//      container (server LootContainerOpened + client lock response).
//      `loot` containers = +1.
//
// The same hooks also feed attribute practice (Shugyo_Attributes): crafting,
// wiring and vehicle repairs teach Intellect, fresh containers Perception.

public class KitsuneShugyoInit : IModApi
{
    private static bool inited;

    // Kitsune Shugyo replaces these. Running one alongside it would level
    // every skill twice or share every read twice (SharedReading, the
    // predecessor of SharedMagazines, re-fires each read on every member).
    private static readonly string[] Replaces = { "KitsuneLearnedSkills", "KitsuneSharedMagazines", "KitsuneSharedReading" };

    public void InitMod(Mod _modInstance)
    {
        if (inited) return;
        inited = true;

        foreach (string other in Replaces)
        {
            if (ModManager.ModLoaded(other))
            {
                Log.Error("[KitsuneShugyo] " + other + " is installed. Kitsune Shugyo replaces it: remove the "
                    + other + " folder from Mods on the server and every client. Kitsune Shugyo is NOT running.");
                return;
            }
        }

        Shugyo_Attributes.Enabled = true;
        Shugyo_Version.Mine = _modInstance.VersionString ?? "unknown";
        Shugyo_Settings.ModPath = _modInstance.Path;
        Shugyo_Settings.Load();
        Log.Out("[KitsuneShugyo] Applying Harmony patches (learning pace " + Shugyo_Settings.Preset
            + ", magazine " + Shugyo_Settings.Magazine.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " level per read, share " + Shugyo_Settings.ShareText() + "; set in Settings.xml)");
        new Harmony(GetType().ToString()).PatchAll(Assembly.GetExecutingAssembly());
    }
}

// Workstation queue output. TileEntityWorkstation.HandleTick always funnels
// completed crafts into CraftCompleteList, delivered here when the crafter
// accesses the station. Postfix so vanilla XP handling is untouched.
[HarmonyPatch(typeof(EntityPlayerLocal))]
[HarmonyPatch("GiveExp")]
[HarmonyPatch(new Type[] { typeof(CraftCompleteData) })]
public class Shugyo_GiveExpCraftPatch
{
    public static void Postfix(EntityPlayerLocal __instance, CraftCompleteData data)
    {
        try
        {
            if (__instance == null || data == null) return;
            Shugyo_Craft.OnCraftComplete(__instance, data.CraftedItemStack, data.RecipeName);
            // This path only ever fires for workstation crafts: +1 completion
            // event (RecipeUsedCount, constructed as 1 per completion) toward
            // craftingWorkstations regardless of what was crafted.
            int events = (data.RecipeUsedCount > 0) ? data.RecipeUsedCount : 1;
            Shugyo_Practice.AddActions(__instance, "craftingWorkstations", events,
                "station", Shugyo_Settings.Station);
            Shugyo_Attributes.AddCraft(__instance, CraftingManager.GetRecipe(data.RecipeName), events);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] craft-to-learn (workstation) failed: " + ex);
        }
    }
}

// Backpack crafting queue. Runs once per completed recipe execution on the
// crafting player's client.
[HarmonyPatch(typeof(XUiC_RecipeStack))]
[HarmonyPatch("giveExp")]
public class Shugyo_RecipeStackCraftPatch
{
    public static void Postfix(XUiC_RecipeStack __instance)
    {
        try
        {
            if (__instance == null) return;
            Recipe recipe = __instance.GetRecipe();
            if (recipe == null) return;
            EntityPlayerLocal player = __instance.xui?.playerUI?.entityPlayer;
            if (player == null) return;

            ItemValue itemValue = new ItemValue(recipe.itemValueType);
            ItemStack crafted = new ItemStack(itemValue, recipe.count);
            Shugyo_Craft.OnCraftComplete(player, crafted, recipe.GetName());
            Shugyo_Attributes.AddCraft(player, recipe, 1);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] craft-to-learn (backpack) failed: " + ex);
        }
    }
}

// Vehicle repair button (vehicle UI, consumes a repair kit). Client-side,
// __result is true when a repair actually happened.
[HarmonyPatch(typeof(XUiM_Vehicle))]
[HarmonyPatch("RepairVehicle")]
public class Shugyo_VehicleRepairPatch
{
    public static void Postfix(bool __result, XUi _xui)
    {
        try
        {
            if (!__result) return;
            EntityPlayerLocal player = _xui?.playerUI?.entityPlayer;
            if (player == null) return;
            Shugyo_Practice.AddActions(player, "craftingVehicles", 1, "vehicle", Shugyo_Settings.Vehicle);
            Shugyo_Attributes.Add(player, Shugyo_Attributes.Intellect, Shugyo_Attributes.Points.Repair, "vehicle");
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] repair-to-learn failed: " + ex);
        }
    }
}

// Completed wire connection. ItemActionConnectPower calls this on the wiring
// player's client; on dedicated servers it may also run server-side where
// there is no primary player — the identity check filters that out.
// A pair already wired this session counts once (see `wired`).
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("SetParentWithWireTool")]
public class Shugyo_WireToolPatch
{
    // Phase 3 anti-grind: each parent -> child pair teaches once per game
    // session, so unplugging and reconnecting the same wire is no loop.
    private static readonly HashSet<string> wired = new HashSet<string>();

    public static void Postfix(TileEntityPowered __instance, IPowered newParentTE, int wiringEntityID)
    {
        try
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || player.entityId != wiringEntityID) return;
            if (__instance != null && newParentTE is TileEntity parent
                && !wired.Add(parent.ToWorldPos() + ">" + __instance.ToWorldPos())) return;
            Shugyo_Practice.AddActions(player, "craftingElectrician", 1, "wire", Shugyo_Settings.Wire);
            Shugyo_Attributes.Add(player, Shugyo_Attributes.Intellect, Shugyo_Attributes.Points.Wire, "wire");
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] wire-to-learn failed: " + ex);
        }
    }
}

// First open of a fresh loot container (see header item 6). Client-side;
// _firstTimeTouched is the server's "never opened, not player-owned" flag.
[HarmonyPatch(typeof(XUiC_LootWindowGroup))]
[HarmonyPatch("OpenLooting")]
public class Shugyo_LootOpenPatch
{
    public static void Postfix(XUiC_LootWindowGroup __instance, TEFeatureStorage _te, bool _firstTimeTouched)
    {
        try
        {
            if (!_firstTimeTouched || _te == null || _te.ItemGrid == null || _te.ItemGrid.PlayerOwned) return;
            EntityPlayerLocal player = __instance?.xui?.playerUI?.entityPlayer;
            if (player == null) return;
            Shugyo_Practice.AddActions(player, "craftingBackpack", 1, "loot", Shugyo_Settings.Loot);
            Shugyo_Attributes.Add(player, Shugyo_Attributes.Perception, Shugyo_Attributes.Points.Loot, "loot");
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] loot-to-learn failed: " + ex);
        }
    }
}

public static class Shugyo_Craft
{
    // Always-unlocked recipes whose skill line has no UnlockedBy craftable at
    // level 1 — hard-mapped so the line can bootstrap. Keyed by recipe name.
    private static readonly Dictionary<string, string> GatewayRecipes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "trapSpikesWoodDmg0",        "craftingTraps" },
        { "trapSpikesIronDmg0",        "craftingTraps" },
        { "barbedFence",               "craftingTraps" },
        { "farmPlotBlockVariantHelper", "craftingSeeds" },
    };

    // Shared craft handler for both queue paths: UnlockedBy award + gateway map.
    public static void OnCraftComplete(EntityPlayerLocal player, ItemStack crafted, string recipeName)
    {
        if (player == null || crafted == null || crafted.IsEmpty()) return;

        ItemValue itemValue = crafted.itemValue;
        if (itemValue == null) return;

        int count = (crafted.count <= 0) ? 1 : crafted.count;

        if (!string.IsNullOrEmpty(recipeName) && GatewayRecipes.TryGetValue(recipeName, out string gatewaySkill))
        {
            Shugyo_Practice.AddActions(player, gatewaySkill, count, "craft", Shugyo_Settings.Craft);
        }

        DynamicProperties props = GetUnlockProperties(itemValue);
        if (props == null) return;

        string unlockedBy = props.GetString("UnlockedBy");
        if (string.IsNullOrEmpty(unlockedBy)) return;

        string[] skills = unlockedBy.Split(',');
        for (int i = 0; i < skills.Length; i++)
        {
            string skill = skills[i].Trim();
            if (skill.Length != 0)
            {
                Shugyo_Practice.AddActions(player, skill, count, "craft", Shugyo_Settings.Craft);
            }
        }
    }

    // UnlockedBy usually lives on the ItemClass; seeds and traps exist only
    // as BLOCKS (plantedCotton1, dartTrap, autoTurret) and carry it on the
    // block instead, so fall back to the block properties.
    private static DynamicProperties GetUnlockProperties(ItemValue itemValue)
    {
        ItemClass itemClass = itemValue.ItemClass;
        if (itemClass != null && itemClass.Properties != null && itemClass.Properties.Contains("UnlockedBy"))
        {
            return itemClass.Properties;
        }
        try
        {
            int type = itemValue.type;
            if (type > 0 && Block.list != null && type < Block.list.Length)
            {
                Block block = Block.list[type];
                if (block != null && block.Properties != null && block.Properties.Contains("UnlockedBy"))
                {
                    return block.Properties;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }
}
