using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

// Phase 2: attributes from practice. Skill points buy perks only, unless
// Settings.xml sets buy_attributes="true" (see Shugyo_AttributeBuyPatch).
//
// Every attribute has ONE meter, $shugyo_<attribute>, counting in fractions
// of a rank, like the crafting meters. Sources add PRACTICE POINTS (a kill
// with gear tagged for that attribute is 1 point; the full list is Points
// below). Going from rank r to r+1 costs r x `attribute` points, where
// `attribute` is the Settings.xml base: on SLOW (40), Perception 1 -> 2 is 40
// kills with a rifle, 9 -> 10 is 360. Points carry over a rank boundary.
//
// Vanilla does the rest. Perk ranks are gated on attribute level, and
// AddProgressionLevel-style level changes skip the point cost and
// level_requirements, so a rank earned here unlocks perks the moment it lands.
// The rank's passives (carry weight, headshot damage, ...) are re-read every
// frame, so they apply at once too.
//
// Sources:
//   - Config/buffs.xml calls the ShugyoPractice action below (kills and block
//     hits with tagged gear, block upgrades, damage taken, recovering from
//     injuries and infection).
//   - This DLL adds directly for what the XML can't see: sneak-attack hits
//     and sneaking near unaware enemies, headshot kills, fresh loot
//     containers, crafting, wiring, vehicle repairs, reading and trading.
//
// Phase 3 anti-grind: every point first goes through Shugyo_Fatigue (a tired
// attribute learns at a quarter), kills are weighted by the victim's XP and
// crafts by the recipe's craft XP (KillWeight, CraftWeight).
//
// Everything runs on the player's own client, the only place a player's
// progression can change (the server takes the client's word for it, as it
// does for vanilla's Buy button).
public static class Shugyo_Attributes
{
    // False when the mod refused to start (an old mod is still installed), so
    // the XML action does nothing either.
    public static bool Enabled;

    public const string Perception = "attPerception";
    public const string Strength = "attStrength";
    public const string Fortitude = "attFortitude";
    public const string Agility = "attAgility";
    public const string Intellect = "attIntellect";

    // Practice points per action, relative to a kill (1). Not in Settings.xml:
    // `attribute` scales them all, and these set how sources compare.
    public static class Points
    {
        public const float Kill = 1f;          // kill holding gear tagged for the attribute (x victim weight)
        public const float BlockHit = 0.1f;    // block hit holding that gear
        public const float Upgrade = 0.5f;     // block upgrade (Strength)
        public const float DamageTaken = 0.5f; // hit by a zombie, animal or bandit (Fortitude)
        public const float Recovery = 10f;     // healed a sprain, break or infection (Fortitude)
        public const float SneakHit = 2f;      // sneak-attack hit (Agility)
        public const float Headshot = 1f;      // headshot kill, any weapon (Perception; x victim weight)
        public const float Sneaking = 0.05f;   // per second crouch-moving near an unaware enemy (Agility)
        public const float Loot = 1f;          // fresh loot container (Perception)
        public const float Craft = 1f;         // completed craft (Intellect; x recipe weight, see CraftWeight)
        public const float Wire = 1f;          // wire connection (Intellect)
        public const float Repair = 2f;        // vehicle repair (Intellect)
        public const float Read = 2f;          // magazine or perk book you read yourself (Intellect)
        public const float Trade = 1f;         // a purchase or sale at a trader (Intellect)
    }

    // Phase 3 anti-grind weights.
    //
    // A kill is weighted by the victim's vanilla XP, as vanilla's own XP is:
    // a walker (500 XP) is 1, a chicken (130) a quarter, a feral or a bear
    // up to 2. Chicken pens and spawner farms pay little.
    public const float KillBaseXp = 500f;
    public const float MinKillWeight = 0.25f;
    public const float MaxKillWeight = 2f;

    // A craft is weighted by the recipe's vanilla craft XP (craft_exp_gain,
    // or 3 per ingredient unit by default): a stone axe (about 18) is 0.6,
    // a forged-steel tool is 2. Crafting cheap things on repeat teaches little.
    public const float CraftBaseXp = 30f;
    public const float MinCraftWeight = 0.1f;
    public const float MaxCraftWeight = 2f;
    public const float MaxCraftPerCompletion = 5f;

    public static float KillWeight(EntityAlive victim)
    {
        if (victim == null || victim.ExperienceValue <= 0) return 1f;
        return Clamp(victim.ExperienceValue / KillBaseXp, MinKillWeight, MaxKillWeight);
    }

    public static float CraftWeight(Recipe recipe)
    {
        if (recipe == null) return 1f;
        return Clamp(recipe.craftExpGain / CraftBaseXp, MinCraftWeight, MaxCraftWeight);
    }

    // Intellect for `units` crafts of one recipe, capped per completion so a
    // big batch isn't a windfall.
    public static void AddCraft(EntityPlayerLocal player, Recipe recipe, int units)
    {
        float points = Points.Craft * CraftWeight(recipe) * Math.Max(1, units);
        Add(player, Intellect, Math.Min(points, MaxCraftPerCompletion), "craft");
    }

    private static float Clamp(float v, float min, float max)
    {
        return v < min ? min : (v > max ? max : v);
    }

    private static readonly Dictionary<string, string> Canonical = BuildCanonical(
        Perception, Strength, Fortitude, Agility, Intellect);

    // What a rank-up says: "Your Perception sharpens: rank 5."
    private static readonly Dictionary<string, string> Verbs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { Perception, "sharpens" },
        { Strength, "grows" },
        { Fortitude, "hardens" },
        { Agility, "quickens" },
        { Intellect, "deepens" },
    };

    private static Dictionary<string, string> BuildCanonical(params string[] names)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names) map[name] = name;
        return map;
    }

    // The five attributes, in vanilla spelling.
    public static IEnumerable<string> Names => Canonical.Values;

    // Skill points can buy attribute ranks too (Settings.xml buy_attributes,
    // the server's copy, baked into buffShugyoPace).
    public static bool CanBuy(EntityAlive player)
    {
        return player?.Buffs != null && player.Buffs.GetCustomVar(Shugyo_Settings.BuyAttributesVar) >= 0.5f;
    }

    public static bool IsPracticed(string progressionName)
    {
        return progressionName != null && Canonical.ContainsKey(progressionName);
    }

    public static string MeterVar(string attribute)
    {
        return Shugyo_Practice.MeterPrefix + (Canonical.TryGetValue(attribute, out string name) ? name : attribute);
    }

    public static ProgressionValue Attribute(EntityAlive player, string attribute)
    {
        if (player == null || player.Progression == null || !IsPracticed(attribute)) return null;
        ProgressionValue value = player.Progression.GetProgressionValue(attribute);
        if (value == null || value.ProgressionClass == null || !value.ProgressionClass.IsAttribute) return null;
        return value;
    }

    // Practice points per rank step on this player's server (Settings.xml's
    // `attribute`), read back from the step the pace buff seeds.
    public static float Base(EntityPlayerLocal player)
    {
        return 1f / Shugyo_Practice.Step(player, "attribute", Shugyo_Settings.Attribute);
    }

    // Adds `points` of practice to the attribute and ranks it up while the
    // meter is full. Returns the ranks gained. `source` is recorded (see
    // Shugyo_Practice.Track) after fatigue, so it's what actually counted.
    public static int Add(EntityPlayerLocal player, string attribute, float points, string source, bool tire = true)
    {
        if (!Enabled || player == null || player.isEntityRemote || player.Buffs == null || points <= 0f) return 0;
        ProgressionValue value = Attribute(player, attribute);
        if (value == null) return 0;

        ProgressionClass cls = value.ProgressionClass;
        string var = MeterVar(attribute);
        if (value.Level >= cls.MaxLevel)
        {
            player.Buffs.SetCustomVar(var, 0f, true, CVarOperation.set);
            return 0;
        }

        if (tire) points = Shugyo_Fatigue.Apply(player, attribute, points);
        Shugyo_Practice.Track(player, Canonical.TryGetValue(attribute, out string meterName) ? meterName : attribute,
            source, points);
        float basePoints = Base(player);
        float meter = player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : 0f;
        int oldLevel = value.Level;
        while (points > 0f && value.Level < cls.MaxLevel)
        {
            float need = basePoints * Math.Max(1, value.Level);
            meter += points / need;
            points = 0f;
            if (meter >= Shugyo_Practice.Threshold)
            {
                // Whatever overflows this rank carries into the next, which
                // costs more, so it is converted back to points first.
                points = Math.Max(0f, meter - 1f) * need;
                meter = 0f;
                value.Level += 1;
            }
        }
        if (value.Level >= cls.MaxLevel) meter = 0f;
        player.Buffs.SetCustomVar(var, meter, true, CVarOperation.set);

        int gained = value.Level - oldLevel;
        if (gained > 0) Announce(player, value);
        return gained;
    }

    private static void Announce(EntityPlayerLocal player, ProgressionValue value)
    {
        player.Progression.bProgressionStatsChanged = true;
        player.bPlayerStatsChanged = true;
        player.PlayerUI?.xui?.Recipes?.RefreshTrackedRecipe();

        string verb = Verbs.TryGetValue(value.Name, out string v) ? v : "grows";
        string line = "Your " + Shugyo_Practice.DisplayName(value.ProgressionClass) + " " + verb + ": rank "
            + value.Level.ToString(CultureInfo.InvariantCulture) + ".";
        GameManager.ShowTooltip(player, line, false, false, 0f);
        Audio.Manager.PlayInsidePlayerHead("ui_skill_purchase");
    }
}

// <triggered_effect trigger="onSelfKilledOther" action="ShugyoPractice, KitsuneShugyo"
//                   attribute="attStrength" points="1" scale="victim"/>
// The XML half of attribute practice. Its own action (not ModifyCVar) because
// each rank costs more than the last, which a fixed XML step can't express.
// scale="victim" weights the points by the killed entity's XP (KillWeight);
// on onSelfKilledOther the event's Other is the victim. skip_placed="true"
// ignores hits on blocks the player placed this session (Shugyo_PlacedBlocks).
[Preserve]
public class MinEventActionShugyoPractice : MinEventActionTargetedBase
{
    private string attribute;
    private float points = 1f;
    private bool scaleByVictim;
    private bool skipPlaced;
    private string source;

    public override bool CanExecute(MinEventTypes _eventType, MinEventParams _params)
    {
        return base.CanExecute(_eventType, _params) && attribute != null && points > 0f;
    }

    public override void Execute(MinEventParams _params)
    {
        if (targets == null) return;
        if (skipPlaced && Shugyo_PlacedBlocks.HittingPlaced()) return;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is EntityPlayerLocal player && !player.isEntityRemote)
                {
                    float weight = scaleByVictim ? Shugyo_Attributes.KillWeight(_params?.Other) : 1f;
                    Shugyo_Attributes.Add(player, attribute, points * weight, source);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] attribute practice failed: " + ex);
        }
    }

    public override bool ParseXmlAttribute(XAttribute _attribute)
    {
        bool flag = base.ParseXmlAttribute(_attribute);
        if (!flag)
        {
            switch (_attribute.Name.LocalName)
            {
            case "attribute":
                attribute = _attribute.Value;
                break;
            case "points":
                points = StringParsers.ParseFloat(_attribute.Value);
                break;
            case "scale":
                scaleByVictim = string.Equals(_attribute.Value, "victim", StringComparison.OrdinalIgnoreCase);
                break;
            case "source":
                source = _attribute.Value;
                break;
            case "skip_placed":
                skipPlaced = StringParsers.ParseBool(_attribute.Value);
                break;
            }
        }
        return flag;
    }
}

// Attribute ranks are earned, not bought. The Buy button on the attribute
// screen is the only place vanilla sells them (XUiC_SkillAttributeLevel); an
// unmeetable level_requirements would also cap the rank you have, since 3.3
// clamps an attribute's effective level to what its requirements allow.
//
// With buy_attributes="true" in Settings.xml (the server's, read back from
// $shugyo_buy_attributes) the button works as in vanilla, and practice keeps
// counting too. A bought rank keeps the practice already earned: the meter is
// a fraction of the rank it was filling, so it is converted back to points
// and re-spread over the dearer rank after the one just bought.
[HarmonyPatch(typeof(XUiC_SkillAttributeLevel))]
[HarmonyPatch("btnBuy_OnPress")]
public class Shugyo_AttributeBuyPatch
{
    public static bool Prefix(XUiC_SkillAttributeLevel __instance, out int __state)
    {
        __state = -1;
        if (!Shugyo_Attributes.Enabled) return true;
        try
        {
            EntityPlayerLocal player = __instance?.xui?.playerUI?.entityPlayer;
            if (Shugyo_Attributes.CanBuy(player))
            {
                ProgressionValue value = __instance.CurrentSkill;
                if (value != null && Shugyo_Attributes.IsPracticed(value.Name)) __state = value.Level;
                return true;
            }
            if (player != null)
            {
                GameManager.ShowTooltip(player, "Attributes are earned by practice, not bought. Skill points go to perks.",
                    false, false, 0f);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] attribute buy block failed: " + ex);
        }
        return false;
    }

    public static void Postfix(XUiC_SkillAttributeLevel __instance, int __state)
    {
        if (__state < 0) return;
        try
        {
            EntityPlayerLocal player = __instance?.xui?.playerUI?.entityPlayer;
            ProgressionValue value = __instance.CurrentSkill;
            if (player?.Buffs == null || value == null || value.Level <= __state) return;
            string var = Shugyo_Attributes.MeterVar(value.Name);
            float meter = player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : 0f;
            meter = value.Level >= value.ProgressionClass.MaxLevel
                ? 0f
                : meter * Math.Max(1, __state) / Math.Max(1, value.Level);
            player.Buffs.SetCustomVar(var, meter, true, CVarOperation.set);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] attribute buy meter failed: " + ex);
        }
    }
}

// Grandpa's Forgetting Elixir (and any other ResetProgression: twitch and
// admin resets) resets PERKS only. Vanilla refunds every attribute rank above
// 1 as if it had been bought; with ranks earned by practice that would hand
// out points nobody spent. So attributes are set to 1 for the duration of the
// call, which makes vanilla skip them, and put back afterwards. Their meters
// are cvars and are never touched.
[HarmonyPatch(typeof(Progression))]
[HarmonyPatch("ResetProgression")]
public class Shugyo_ResetKeepsAttributesPatch
{
    public static void Prefix(Progression __instance, out List<KeyValuePair<ProgressionValue, int>> __state)
    {
        __state = null;
        if (!Shugyo_Attributes.Enabled || __instance?.ProgressionValueQuickList == null) return;
        foreach (ProgressionValue value in __instance.ProgressionValueQuickList)
        {
            if (value?.ProgressionClass == null || !value.ProgressionClass.IsAttribute) continue;
            if (!Shugyo_Attributes.IsPracticed(value.Name) || value.Level <= 1) continue;
            if (__state == null) __state = new List<KeyValuePair<ProgressionValue, int>>();
            __state.Add(new KeyValuePair<ProgressionValue, int>(value, value.Level));
            value.Level = 1;
        }
    }

    // A finalizer, so the ranks come back even if vanilla throws.
    public static void Finalizer(List<KeyValuePair<ProgressionValue, int>> __state)
    {
        if (__state == null) return;
        foreach (var kept in __state)
        {
            kept.Key.Level = kept.Value;
        }
    }
}

// Agility: a sneak-attack hit. The game tells the attacker's client with this
// call (the "Sneak damage x2" banner) when the victim's damage response comes
// back, so it fires once per sneak hit, for the local player only.
[HarmonyPatch(typeof(EntityPlayerLocal))]
[HarmonyPatch("NotifySneakDamage")]
public class Shugyo_SneakHitPatch
{
    public static void Postfix(EntityPlayerLocal __instance)
    {
        try
        {
            Shugyo_Attributes.Add(__instance, Shugyo_Attributes.Agility, Shugyo_Attributes.Points.SneakHit, "sneak");
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] sneak practice failed: " + ex);
        }
    }
}

// Perception: a headshot kill, with any weapon (aiming is Perception), on top
// of whatever the kill teaches the weapon's own attribute, so a rifle
// headshot kill counts double, as the design has it. The kill event can't see
// where the killing hit landed, but the victim's damage response does: it is
// processed on the attacker's client (the same call that shows the sneak
// banner) with Fatal and the body part hit. Each victim counts once.
[HarmonyPatch(typeof(EntityAlive))]
[HarmonyPatch("ProcessDamageResponseLocal")]
public class Shugyo_HeadshotKillPatch
{
    private static readonly HashSet<int> credited = new HashSet<int>();

    public static void Postfix(EntityAlive __instance, DamageResponse _dmResponse)
    {
        try
        {
            if (!Shugyo_Attributes.Enabled || __instance == null || !_dmResponse.Fatal) return;
            if ((_dmResponse.HitBodyPart & EnumBodyPartHit.Head) == 0) return;
            if (!(__instance is EntityEnemy)) return;
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || player.entityId != _dmResponse.Source.getEntityId()) return;
            if (credited.Count > 1024) credited.Clear();
            if (!credited.Add(__instance.entityId)) return;
            Shugyo_Attributes.Add(player, Shugyo_Attributes.Perception,
                Shugyo_Attributes.Points.Headshot * Shugyo_Attributes.KillWeight(__instance), "headshot");
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] headshot practice failed: " + ex);
        }
    }
}

// Agility: crouch-moving within a few metres of an enemy that hasn't noticed
// you (zombies' alert flag is replicated to clients; sleepers count). Checked
// once a second from buffShugyoLearn's tick (ShugyoRest), so a minute of
// real sneaking is 3 points, about three kills. Standing still doesn't count,
// and fatigue covers circling a caged zombie.
public static class Shugyo_Stealth
{
    public const float Radius = 8f;
    private const float MinMove = 0.3f; // metres since the last tick

    private static Vector3 lastPos;
    private static int lastPlayer = -1;
    private static readonly List<Entity> nearby = new List<Entity>();

    public static void Tick(EntityPlayerLocal player)
    {
        if (player == null || player.isEntityRemote || player.IsDead()) return;
        Vector3 pos = player.position;
        bool moved = player.entityId == lastPlayer && (pos - lastPos).magnitude >= MinMove;
        lastPos = pos;
        lastPlayer = player.entityId;
        if (!moved || !player.IsCrouching) return;

        World world = GameManager.Instance?.World;
        if (world == null) return;
        nearby.Clear();
        world.GetEntitiesInBounds(typeof(EntityEnemy), new Bounds(pos, Vector3.one * (Radius * 2f)), nearby);
        for (int i = 0; i < nearby.Count; i++)
        {
            if (nearby[i] is EntityEnemy enemy && !enemy.IsDead() && !enemy.IsAlert
                && (enemy.position - pos).sqrMagnitude <= Radius * Radius)
            {
                Shugyo_Attributes.Add(player, Shugyo_Attributes.Agility, Shugyo_Attributes.Points.Sneaking, "stealth");
                return;
            }
        }
    }
}

// Intellect: buying or selling at a trader or vending machine. Vanilla reports
// each completed transaction to the quest system from the trade UI on the
// trading player's client; it counts once per transaction, not per item.
[HarmonyPatch(typeof(QuestEventManager))]
[HarmonyPatch("BoughtItems")]
public class Shugyo_TradeBuyPatch
{
    public static void Postfix()
    {
        Shugyo_TradePractice.Add();
    }
}

[HarmonyPatch(typeof(QuestEventManager))]
[HarmonyPatch("SoldItems")]
public class Shugyo_TradeSellPatch
{
    public static void Postfix()
    {
        Shugyo_TradePractice.Add();
    }
}

public static class Shugyo_TradePractice
{
    public static void Add()
    {
        try
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            Shugyo_Attributes.Add(player, Shugyo_Attributes.Intellect, Shugyo_Attributes.Points.Trade, "trade");
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] trade practice failed: " + ex);
        }
    }
}
