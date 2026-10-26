using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Magazines, perk books and schematics, read and shared with the party. Folded
// in from Kitsune Shared Magazines 0.2.3, with schematics (0.3.0) and modded
// magazines (0.3.1) added later; the network flow is unchanged, the verbs are
// renamed so a stray copy of the old mod can't answer them.
//
// What changed: a magazine no longer adds a flat level. Its AddProgressionLevel
// effect is intercepted and turned into Settings.xml's `magazine` value of
// practice (a quarter of a level by default) on the reader's meter, and on
// every party member's meter. Doing stays the path; finding speeds it up.
//
// Flow (dedicated server, all three hops ride VANILLA packages so no custom
// NetPackage IDs exist to desync a client that lacks the mod):
//
//   1. Reader's client: prefix on MinEventActionAddProgressionLevel.Execute
//      sees a magazine (see below) raise a crafting skill, skips vanilla,
//      adds the practice to the local player, and sends
//      "shugyo_read <skill> <reads>" to the server inside a
//      NetPackageConsoleCmdServer.
//   2. Server: a prefix on NetPackageConsoleCmdServer.ProcessPackage swallows
//      that line before the console sees it (so it is never a real command
//      anyone can type), checks it, and, unless Settings.xml says share="off",
//      sends "shugyo_apply <skill> <reads> <reader name>" to every other party
//      member's client inside a NetPackageConsoleCmdClient.
//   3. Member's client: a prefix on NetPackageConsoleCmdClient.ProcessPackage
//      swallows that line and adds the same practice to the local player.
//
// A member WITHOUT the mod just gets "unknown command" in their F1 console.
// A server without the mod answers the reader the same way. Neither drops
// anyone. Listen-server hosts are handled in-process (see Share/Apply).
//
// <reads> is how many magazines' worth the read was: vanilla turns level="1"
// into the PointsPerMagazine sandbox option, and the Nerd outfit's bonus roll
// is a second effect on the same read. Each member converts reads to practice
// with the magazine value their server baked into the pace buff.
//
// Perk books (the "1/7" series volumes) ride the same three hops with their
// own verbs and are unchanged from Shared Magazines: a book is any item whose
// "Unlocks" names a ProgressionType.Book perk, shared whenever it is read, even
// by someone who already knew it. The series "Complete" bonus is never shared
// as such: each member earns it themselves when the shared volume leaves them
// with every other book in the series unlocked.
//
// Schematics ride the same hops too (from Shared Magazines 0.3.0):
// "shugyo_schem <schematic item>" up, "shugyo_schemapply <schematic item>
// <reader name>" down. A schematic is any item whose "Unlocks" names a
// learnable recipe (all 69 vanilla ones: every gun, melee, armor, drone and
// vehicle mod, plus the recipe schematics). Reading one sets a player cvar
// named after the recipe to 1, and that is all a member gets: the recipe, not
// the reader's XP. Shared whenever it is read, like books; members who already
// know the recipe are skipped on their side.
//
// A magazine is an item tagged "csm" or one whose "Unlocks" names a crafting
// skill, the way every vanilla magazine is set up, so a modded magazine that
// skips the tag is still a magazine (from Shared Magazines 0.3.1). Practice
// from doing never matches: tools don't name a skill in "Unlocks".
//
// Series bonus self-heal: vanilla only checks for the bonus inside a volume's
// own effect group, which is skipped when the reader already knows that
// volume. A player who ends up with all seven unlocked without that check
// passing is stuck without the bonus for good. So the check runs again on the
// reader's client after every book read and when the player enters the game.

// Step 1: the reader's client. Returning false skips vanilla's flat level for
// every target, the reader's server-side copy included; the reader's client
// is authoritative for its own progression and syncs the result.
[HarmonyPatch(typeof(MinEventActionAddProgressionLevel))]
[HarmonyPatch("Execute")]
public class Shugyo_MagazineReadPatch
{
    public static bool Prefix(MinEventActionAddProgressionLevel __instance, MinEventParams _params)
    {
        try
        {
            if (!Shugyo_Share.IsMagazine(_params)) return true;
            string skill = __instance.progressionName;
            if (!Shugyo_Share.IsCraftingSkillName(skill)) return true;

            // level="-1" (max out) is the admin magazine: leave it alone.
            int reads = __instance.level == 1
                ? global::SandboxOptions.SandboxOptionManager.GetInt(global::SandboxOptions.SandboxOptions.PointsPerMagazine)
                : __instance.level;
            if (reads < 1) return true;

            EntityPlayerLocal player = Shugyo_Share.LocalTarget(__instance);
            if (player != null)
            {
                int gained = Shugyo_Practice.Add(player, skill, reads * Shugyo_Practice.MagazineValue(player), "", "magazine");
                GameManager.ShowTooltip(player, Shugyo_Practice.Progress(player, skill, gained), false, false, 0f);
                Shugyo_Attributes.Add(player, Shugyo_Attributes.Intellect, Shugyo_Attributes.Points.Read, "read");
                Shugyo_Share.ReportRead(player, skill, reads);
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] magazine read failed: " + ex);
            return true;
        }
    }
}

// Step 1 for perk books and schematics. Hooked on the read itself (ItemActionEat), not on the
// book's unlock effect: that effect is skipped when the reader already knows
// the book (vanilla's ProgressionLevel == 0 requirement), but the book is still
// read and used up, so it should still teach the party. Members who already
// have it are skipped on their side. A held book finishes in consume(); "Read"
// from the inventory goes through ExecuteInstantAction.
[HarmonyPatch(typeof(ItemActionEat))]
[HarmonyPatch("consume")]
public class Shugyo_BookReadHeldPatch
{
    public static void Prefix(ItemActionData _actionData, out ItemClass __state)
    {
        __state = null;
        try
        {
            if (!(_actionData is ItemActionEat.MyInventoryData data) || !data.bEatingStarted) return;
            if (!(_actionData.invData?.holdingEntity is EntityPlayerLocal player) || player.isEntityRemote) return;
            __state = Shugyo_Share.SharedReadClass(_actionData.invData.itemStack?.itemValue);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] book check failed: " + ex);
        }
    }

    public static void Postfix(ItemActionData _actionData, ItemClass __state)
    {
        if (__state == null) return;
        try
        {
            Shugyo_Share.OnLocalRead(_actionData.invData.holdingEntity as EntityPlayerLocal, __state);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] sharing a book failed: " + ex);
        }
    }
}

[HarmonyPatch(typeof(ItemActionEat))]
[HarmonyPatch("ExecuteInstantAction")]
public class Shugyo_BookReadInstantPatch
{
    public static void Prefix(EntityAlive ent, ItemStack stack, out ItemClass __state)
    {
        __state = null;
        try
        {
            if (!(ent is EntityPlayerLocal player) || player.isEntityRemote) return;
            __state = Shugyo_Share.SharedReadClass(stack?.itemValue);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] book check failed: " + ex);
        }
    }

    public static void Postfix(EntityAlive ent, bool __result, ItemClass __state)
    {
        if (__state == null || !__result) return;
        try
        {
            Shugyo_Share.OnLocalRead(ent as EntityPlayerLocal, __state);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] sharing a book failed: " + ex);
        }
    }
}

// Series bonus self-heal on entering the game, for saves already stuck with
// every volume read and no bonus. A fresh character has no books to check.
[HarmonyPatch(typeof(EntityPlayerLocal))]
[HarmonyPatch("AfterPlayerRespawn")]
public class Shugyo_SeriesBonusLoginPatch
{
    public static void Postfix(EntityPlayerLocal __instance, RespawnType _type)
    {
        if (_type != RespawnType.LoadedGame && _type != RespawnType.JoinMultiplayer) return;
        try
        {
            Shugyo_Share.HealAllSeries(__instance);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] series bonus check failed: " + ex);
        }
    }
}

// Step 2: the server. Runs before ConnectionManager.ServerConsoleCommand, so a
// shugyo_read line never reaches the console, its permission check or its log.
[HarmonyPatch(typeof(NetPackageConsoleCmdServer))]
[HarmonyPatch("ProcessPackage")]
public class Shugyo_ServerInboxPatch
{
    public static bool Prefix(NetPackageConsoleCmdServer __instance, World _world)
    {
        string cmd = __instance.cmd;
        bool isRead = cmd != null && cmd.StartsWith(Shugyo_Share.ReadVerb + " ", StringComparison.Ordinal);
        bool isBook = cmd != null && cmd.StartsWith(Shugyo_Share.BookVerb + " ", StringComparison.Ordinal);
        bool isSchem = cmd != null && cmd.StartsWith(Shugyo_Share.SchematicVerb + " ", StringComparison.Ordinal);
        if (cmd != null && cmd.StartsWith(Shugyo_Version.HelloVerb + " ", StringComparison.Ordinal))
        {
            Shugyo_Version.OnHello(__instance.Sender, cmd);
            return false;
        }
        if (!isRead && !isBook && !isSchem) return true;
        try
        {
            if (_world != null && isRead) Shugyo_Share.OnReadReport(_world, __instance.Sender, cmd);
            if (_world != null && isBook) Shugyo_Share.OnBookReport(_world, __instance.Sender, cmd);
            if (_world != null && isSchem) Shugyo_Share.OnSchematicReport(_world, __instance.Sender, cmd);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] handling a read report failed: " + ex);
        }
        return false;
    }
}

// Step 3: a party member's client.
[HarmonyPatch(typeof(NetPackageConsoleCmdClient))]
[HarmonyPatch("ProcessPackage")]
public class Shugyo_ClientInboxPatch
{
    public static bool Prefix(NetPackageConsoleCmdClient __instance, World _world)
    {
        List<string> lines = __instance.lines;
        if (!__instance.bExecute || lines == null || lines.Count == 0 || lines[0] == null) return true;
        bool isApply = lines[0].StartsWith(Shugyo_Share.ApplyVerb + " ", StringComparison.Ordinal);
        bool isBook = lines[0].StartsWith(Shugyo_Share.BookApplyVerb + " ", StringComparison.Ordinal);
        bool isSchem = lines[0].StartsWith(Shugyo_Share.SchematicApplyVerb + " ", StringComparison.Ordinal);
        if (Shugyo_Version.HandleClientLine(_world, lines[0])) return false;
        if (!isApply && !isBook && !isSchem) return true;
        try
        {
            if (_world != null && isApply) Shugyo_Share.OnApply(_world, lines[0]);
            if (_world != null && isBook) Shugyo_Share.OnBookApply(_world, lines[0]);
            if (_world != null && isSchem) Shugyo_Share.OnSchematicApply(_world, lines[0]);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] applying a shared read failed: " + ex);
        }
        return false;
    }
}

public static class Shugyo_Share
{
    public const string ReadVerb = "shugyo_read";
    public const string ApplyVerb = "shugyo_apply";
    public const string BookVerb = "shugyo_book";
    public const string BookApplyVerb = "shugyo_bookapply";
    public const string SchematicVerb = "shugyo_schem";
    public const string SchematicApplyVerb = "shugyo_schemapply";

    // A magazine read takes about a second (Eat action Delay 1.0), so a faster
    // stream of reports from one player is not real reading.
    private const float MinSecondsBetweenReports = 0.5f;
    private const int MaxReadsPerReport = 10;

    private static readonly FastTags<TagGroup.Global> MagazineTag = FastTags<TagGroup.Global>.Parse("csm");
    private static readonly Dictionary<int, float> lastReportAt = new Dictionary<int, float>();

    // The local player among an action's targets, if any (null on a dedicated
    // server, where every reader is remote).
    public static EntityPlayerLocal LocalTarget(MinEventActionTargetedBase action)
    {
        List<EntityAlive> targets = action.targets;
        if (targets == null) return null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] is EntityPlayerLocal local && !local.isEntityRemote) return local;
        }
        return null;
    }

    // Tagged "csm", or naming a crafting skill in Unlocks the way every vanilla
    // magazine does, so a modded magazine that skips the tag still counts.
    public static bool IsMagazine(MinEventParams _params)
    {
        ItemClass itemClass = _params?.ItemValue?.ItemClass;
        if (itemClass == null) return false;
        if (itemClass.ItemTags.Test_AnySet(MagazineTag)) return true;
        return !string.IsNullOrEmpty(itemClass.Unlocks) && IsCraftingSkillName(itemClass.Unlocks);
    }

    // By class, not by player: a dedicated server runs the read for a remote
    // reader too, and must skip vanilla there as well.
    public static bool IsCraftingSkillName(string progressionName)
    {
        if (string.IsNullOrEmpty(progressionName)) return false;
        Progression.ProgressionClasses.TryGetValue(progressionName, out ProgressionClass cls);
        return cls != null && cls.IsCrafting;
    }

    // Reader side: hand the read to whoever runs the party logic.
    public static void ReportRead(EntityPlayerLocal reader, string progressionName, int reads)
    {
        ConnectionManager connections = SingletonMonoBehaviour<ConnectionManager>.Instance;
        if (connections == null) return;
        if (connections.IsServer)
        {
            // Listen-server host (or single player, where there is no party).
            Share(reader, reader.EntityName, progressionName, reads);
            return;
        }
        connections.SendToServer(NetPackageManager.GetPackage<NetPackageConsoleCmdServer>()
            .Setup(ReadVerb + " " + progressionName + " " + reads));
    }

    // Server side: "shugyo_read <skill> <reads>" from a client. Every report is
    // logged, shared or not (Maltaine, 2026-10-23: a party read nothing in the
    // log, and "never reached the server" looked the same as "nobody to share
    // with"). A report that reaches no one says why; a malformed one is a warning.
    public static void OnReadReport(World world, ClientInfo sender, string cmd)
    {
        if (sender == null) return;
        string[] parts = cmd.Split(' ');
        if (parts.Length != 3 || !int.TryParse(parts[2], out int reads) || reads < 1 || reads > MaxReadsPerReport)
        {
            Log.Warning("[KitsuneShugyo] Ignored a malformed read report from " + sender.playerName + ": " + cmd);
            return;
        }

        EntityPlayer reader = world.GetEntity(sender.entityId) as EntityPlayer;
        if (Shugyo_Practice.CraftingSkill(reader, parts[1]) == null)
        {
            Log.Warning("[KitsuneShugyo] Ignored a read report from " + sender.playerName + ": " + parts[1]
                + " isn't a crafting skill here");
            return;
        }

        if (!TakeReportSlot(sender)) return;
        if (Share(reader, sender.playerName, parts[1], reads) <= 0)
        {
            Log.Out("[KitsuneShugyo] " + sender.playerName + " read a " + parts[1] + " magazine (x" + reads
                + "), not shared: " + NotSharedReason(reader));
        }
    }

    // Why a read reached no one: sharing off, no party, or nobody to give it to.
    private static string NotSharedReason(EntityPlayer reader)
    {
        if (Shugyo_Settings.Share == Shugyo_Settings.ShareMode.Off) return "sharing is off in Settings.xml";
        if (reader?.Party?.MemberList == null || reader.Party.MemberList.Count < 2) return "not in a party";
        return Shugyo_Settings.Share == Shugyo_Settings.ShareMode.Nearby
            ? "no other party member online within " + Shugyo_Settings.Range + " m"
            : "no other party member online";
    }

    // Reading a magazine or a book takes about a second, so a faster stream of
    // reports from one player is not real reading.
    private static bool TakeReportSlot(ClientInfo sender)
    {
        float now = Time.time;
        if (lastReportAt.TryGetValue(sender.entityId, out float last) && now - last < MinSecondsBetweenReports)
        {
            Log.Warning("[KitsuneShugyo] Ignored a read report from " + sender.playerName + " (too fast)");
            return false;
        }
        lastReportAt[sender.entityId] = now;
        return true;
    }

    // Server (or listen host): send the read to every other party member.
    // Returns how many it reached (0 when sharing is off).
    public static int Share(EntityPlayer reader, string readerName, string progressionName, int reads)
    {
        if (Shugyo_Settings.Share == Shugyo_Settings.ShareMode.Off) return 0;
        int shared = SendToParty(reader,
            host => Apply(host, progressionName, reads, readerName),
            ApplyVerb + " " + progressionName + " " + reads + " " + readerName);

        if (shared > 0)
        {
            Log.Out("[KitsuneShugyo] " + readerName + " read a " + progressionName + " magazine (x" + reads
                + "), shared with " + shared + " party member(s)");
        }
        return shared;
    }

    // Every other party member (within `range` metres for share="nearby"): the
    // listen host in-process, everyone else as a console line to their client.
    // Returns how many were reached.
    private static int SendToParty(EntityPlayer reader, Action<EntityPlayerLocal> applyHost, string clientLine)
    {
        Party party = reader?.Party;
        if (party == null || party.MemberList == null) return 0;

        ConnectionManager connections = SingletonMonoBehaviour<ConnectionManager>.Instance;
        int shared = 0;
        for (int i = 0; i < party.MemberList.Count; i++)
        {
            EntityPlayer member = party.MemberList[i];
            if (member == null || member.entityId == reader.entityId) continue;
            // share="nearby" (Phase 3 anti-grind): a party of parked alts
            // doesn't learn from one reader across the map.
            if (Shugyo_Settings.Share == Shugyo_Settings.ShareMode.Nearby
                && member.GetDistance(reader) > Shugyo_Settings.Range) continue;

            if (member is EntityPlayerLocal host)
            {
                applyHost(host);
                shared++;
                continue;
            }

            ClientInfo client = connections?.Clients?.ForEntityId(member.entityId);
            if (client == null) continue;
            client.SendPackage(NetPackageManager.GetPackage<NetPackageConsoleCmdClient>().Setup(clientLine, true));
            shared++;
        }
        return shared;
    }

    // Member side: "shugyo_apply <skill> <reads> <reader name...>".
    public static void OnApply(World world, string line)
    {
        string[] parts = line.Split(new[] { ' ' }, 4);
        if (parts.Length < 3 || !int.TryParse(parts[2], out int reads)) return;
        if (reads < 1 || reads > MaxReadsPerReport) return;
        string readerName = parts.Length == 4 && parts[3].Length > 0 ? parts[3] : "A party member";
        Apply(world.GetPrimaryPlayer(), parts[1], reads, readerName);
    }

    // The same practice the reader got, on this member's meter.
    public static void Apply(EntityPlayerLocal player, string progressionName, int reads, string readerName)
    {
        ProgressionValue value = Shugyo_Practice.CraftingSkill(player, progressionName);
        if (value == null) return;
        int gained = Shugyo_Practice.Add(player, progressionName, reads * Shugyo_Practice.MagazineValue(player), "", "shared");
        GameManager.ShowTooltip(player, readerName + " shared a magazine. "
            + Shugyo_Practice.Progress(player, progressionName, gained), false, false, 0f);
    }

    // ---- Perk books and schematics: the read hooks ----

    // The item class when this item is a perk book or a schematic, else null.
    public static ItemClass SharedReadClass(ItemValue itemValue)
    {
        return BookClass(itemValue) ?? SchematicClass(itemValue);
    }

    // Reader's client, after a book or schematic was read.
    public static void OnLocalRead(EntityPlayerLocal player, ItemClass itemClass)
    {
        if (player == null || itemClass == null) return;
        Shugyo_Attributes.Add(player, Shugyo_Attributes.Intellect, Shugyo_Attributes.Points.Read, "read");
        if (Progression.ProgressionClasses.ContainsKey(itemClass.Unlocks))
        {
            HealSeriesOf(player, itemClass.Unlocks);
            ReportBook(player, itemClass.Unlocks, itemClass.GetItemName());
            return;
        }
        ReportSchematic(player, itemClass.GetItemName());
    }

    // ---- Perk books ----

    // The book's item class when this item is a perk book (its Unlocks names a
    // ProgressionType.Book perk), else null.
    public static ItemClass BookClass(ItemValue itemValue)
    {
        ItemClass itemClass = itemValue?.ItemClass;
        if (itemClass == null || string.IsNullOrEmpty(itemClass.Unlocks)) return null;
        Progression.ProgressionClasses.TryGetValue(itemClass.Unlocks, out ProgressionClass progression);
        return progression != null && progression.IsBook ? itemClass : null;
    }

    public static ProgressionValue Book(EntityAlive player, string perkName)
    {
        if (player == null || player.Progression == null || string.IsNullOrEmpty(perkName)) return null;
        ProgressionValue value = player.Progression.GetProgressionValue(perkName);
        if (value == null || value.ProgressionClass == null || !value.ProgressionClass.IsBook) return null;
        return value;
    }

    public static void ReportBook(EntityPlayerLocal reader, string perkName, string bookItem)
    {
        if (reader == null || bookItem == null) return;
        ConnectionManager connections = SingletonMonoBehaviour<ConnectionManager>.Instance;
        if (connections == null) return;
        if (connections.IsServer)
        {
            ShareBook(reader, reader.EntityName, perkName, bookItem);
            return;
        }
        connections.SendToServer(NetPackageManager.GetPackage<NetPackageConsoleCmdServer>()
            .Setup(BookVerb + " " + perkName + " " + bookItem));
    }

    // Server side: "shugyo_book <perk> <book item>" from a client.
    public static void OnBookReport(World world, ClientInfo sender, string cmd)
    {
        if (sender == null) return;
        string[] parts = cmd.Split(' ');
        if (parts.Length != 3)
        {
            Log.Warning("[KitsuneShugyo] Ignored a malformed book report from " + sender.playerName + ": " + cmd);
            return;
        }

        // The named item must really be the book for that perk, and the
        // reader must really have it now.
        ItemClass book = ItemClass.GetItemClass(parts[2]);
        EntityPlayer reader = world.GetEntity(sender.entityId) as EntityPlayer;
        if (book == null || !string.Equals(book.Unlocks, parts[1], StringComparison.OrdinalIgnoreCase)
            || Book(reader, parts[1]) == null)
        {
            Log.Warning("[KitsuneShugyo] Ignored a book report from " + sender.playerName + ": " + parts[2]
                + " (" + parts[1] + ") isn't a book they have");
            return;
        }

        if (!TakeReportSlot(sender)) return;
        if (ShareBook(reader, sender.playerName, parts[1], parts[2]) <= 0)
        {
            Log.Out("[KitsuneShugyo] " + sender.playerName + " read " + parts[2] + ", not shared: "
                + NotSharedReason(reader));
        }
    }

    // Returns how many party members it reached (0 when sharing is off).
    public static int ShareBook(EntityPlayer reader, string readerName, string perkName, string bookItem)
    {
        if (Shugyo_Settings.Share == Shugyo_Settings.ShareMode.Off) return 0;
        int shared = SendToParty(reader,
            host => ApplyBook(host, perkName, bookItem, readerName),
            BookApplyVerb + " " + perkName + " " + bookItem + " " + readerName);

        if (shared > 0)
        {
            Log.Out("[KitsuneShugyo] " + readerName + " read " + bookItem + ", shared with "
                + shared + " party member(s)");
        }
        return shared;
    }

    // Member side: "shugyo_bookapply <perk> <book item> <reader name...>".
    public static void OnBookApply(World world, string line)
    {
        string[] parts = line.Split(new[] { ' ' }, 4);
        if (parts.Length < 3) return;
        string readerName = parts.Length == 4 && parts[3].Length > 0 ? parts[3] : "A party member";
        ApplyBook(world.GetPrimaryPlayer(), parts[1], parts[2], readerName);
    }

    // Mirrors the book's own effects: unlock its perk if this player hasn't
    // read it, then grant the series bonus if that completes their set.
    public static void ApplyBook(EntityPlayerLocal player, string perkName, string bookItem, string readerName)
    {
        ProgressionValue value = Book(player, perkName);
        if (value == null || value.Level > 0) return;
        value.Level = value.ProgressionClass.MaxLevel;

        ProgressionValue bonus = SeriesBonusIfComplete(player, value.ProgressionClass.Parent);
        if (bonus != null) bonus.Level = bonus.ProgressionClass.MaxLevel;

        player.Progression.bProgressionStatsChanged = true;
        player.bPlayerStatsChanged = true;

        string bookName = ItemClass.GetItemClass(bookItem)?.GetLocalizedItemName();
        if (string.IsNullOrEmpty(bookName)) bookName = bookItem;
        string message = readerName + " shared " + bookName + " with you.";
        if (bonus != null)
        {
            message += " That completes the series!";
            Audio.Manager.PlayInsidePlayerHead("read_skillbook_final", player.entityId);
        }
        GameManager.ShowTooltip(player, message, false, false, 0f);
    }

    // Vanilla grants the series bonus perk (perk...Complete, a book in the same
    // group) once 7 books in the group are unlocked, i.e. all the others.
    // Returns the bonus when this player has just met that, else null.
    // ProgressionClass names are lowercased on load ("perksnipercomplete"),
    // so the suffix match must ignore case.
    private static ProgressionValue SeriesBonusIfComplete(EntityPlayerLocal player, ProgressionClass series)
    {
        if (series == null || series.Children == null) return null;
        ProgressionValue bonus = null;
        for (int i = 0; i < series.Children.Count; i++)
        {
            ProgressionClass child = series.Children[i];
            ProgressionValue childValue = player.Progression.GetProgressionValue(child.Name);
            if (childValue == null) return null;
            if (child.Name.EndsWith("Complete", StringComparison.OrdinalIgnoreCase))
            {
                bonus = childValue;
                continue;
            }
            if (childValue.Level <= 0) return null;
        }
        return bonus != null && bonus.Level <= 0 ? bonus : null;
    }

    // Self-heal after a read: the series of the book perk just read.
    public static void HealSeriesOf(EntityPlayerLocal player, string perkName)
    {
        if (player == null || player.isEntityRemote || string.IsNullOrEmpty(perkName)) return;
        Progression.ProgressionClasses.TryGetValue(perkName, out ProgressionClass book);
        if (book != null && book.IsBook) HealSeries(player, book.Parent);
    }

    // Self-heal on entering the game: every book series.
    public static void HealAllSeries(EntityPlayerLocal player)
    {
        if (player == null || player.isEntityRemote || player.Progression == null) return;
        foreach (ProgressionClass series in Progression.ProgressionClasses.Values)
        {
            if (series.IsBookGroup) HealSeries(player, series);
        }
    }

    // Grants the series bonus when this player has every volume but not the
    // bonus. A no-op whenever vanilla already granted it.
    private static void HealSeries(EntityPlayerLocal player, ProgressionClass series)
    {
        ProgressionValue bonus = SeriesBonusIfComplete(player, series);
        if (bonus == null) return;
        bonus.Level = bonus.ProgressionClass.MaxLevel;
        player.Progression.bProgressionStatsChanged = true;
        player.bPlayerStatsChanged = true;

        Log.Out("[KitsuneShugyo] Granted the missing " + series.Name + " series bonus to "
            + player.EntityName);
        string seriesName = Localization.Get(series.NameKey, false);
        if (string.IsNullOrEmpty(seriesName)) seriesName = series.Name;
        Audio.Manager.PlayInsidePlayerHead("read_skillbook_final", player.entityId);
        GameManager.ShowTooltip(player, seriesName + " series complete!", false, false, 0f);
    }

    // ---- Schematics ----

    // The schematic's item class when its Unlocks names a learnable recipe,
    // else null. Perk books and magazines never match: their Unlocks names a
    // perk or skill, not a recipe.
    public static ItemClass SchematicClass(ItemValue itemValue)
    {
        ItemClass itemClass = itemValue?.ItemClass;
        return SchematicRecipe(itemClass) != null ? itemClass : null;
    }

    public static Recipe SchematicRecipe(ItemClass itemClass)
    {
        if (itemClass == null || string.IsNullOrEmpty(itemClass.Unlocks)) return null;
        if (Progression.ProgressionClasses.ContainsKey(itemClass.Unlocks)) return null;
        Recipe recipe = CraftingManager.GetRecipe(itemClass.Unlocks);
        return recipe != null && recipe.IsLearnable ? recipe : null;
    }

    public static void ReportSchematic(EntityPlayerLocal reader, string schematicItem)
    {
        if (reader == null || schematicItem == null) return;
        ConnectionManager connections = SingletonMonoBehaviour<ConnectionManager>.Instance;
        if (connections == null) return;
        if (connections.IsServer)
        {
            ShareSchematic(reader, reader.EntityName, schematicItem);
            return;
        }
        connections.SendToServer(NetPackageManager.GetPackage<NetPackageConsoleCmdServer>()
            .Setup(SchematicVerb + " " + schematicItem));
    }

    // Server side: "shugyo_schem <schematic item>" from a client. The reader's
    // recipe cvar is set on their client and reaches the server later with
    // their player data, so only the item itself is checked here.
    public static void OnSchematicReport(World world, ClientInfo sender, string cmd)
    {
        if (sender == null) return;
        string[] parts = cmd.Split(' ');
        if (parts.Length != 2)
        {
            Log.Warning("[KitsuneShugyo] Ignored a malformed schematic report from " + sender.playerName + ": " + cmd);
            return;
        }

        EntityPlayer reader = world.GetEntity(sender.entityId) as EntityPlayer;
        if (reader == null || SchematicRecipe(ItemClass.GetItemClass(parts[1])) == null)
        {
            Log.Warning("[KitsuneShugyo] Ignored a schematic report from " + sender.playerName + ": " + parts[1]
                + " doesn't unlock a learnable recipe here");
            return;
        }

        if (!TakeReportSlot(sender)) return;
        if (ShareSchematic(reader, sender.playerName, parts[1]) <= 0)
        {
            Log.Out("[KitsuneShugyo] " + sender.playerName + " read " + parts[1] + ", not shared: "
                + NotSharedReason(reader));
        }
    }

    // Returns how many party members it reached (0 when sharing is off).
    public static int ShareSchematic(EntityPlayer reader, string readerName, string schematicItem)
    {
        if (Shugyo_Settings.Share == Shugyo_Settings.ShareMode.Off) return 0;
        int shared = SendToParty(reader,
            host => ApplySchematic(host, schematicItem, readerName),
            SchematicApplyVerb + " " + schematicItem + " " + readerName);

        if (shared > 0)
        {
            Log.Out("[KitsuneShugyo] " + readerName + " read " + schematicItem + ", shared with "
                + shared + " party member(s)");
        }
        return shared;
    }

    // Member side: "shugyo_schemapply <schematic item> <reader name...>".
    public static void OnSchematicApply(World world, string line)
    {
        string[] parts = line.Split(new[] { ' ' }, 3);
        if (parts.Length < 2) return;
        string readerName = parts.Length == 3 && parts[2].Length > 0 ? parts[2] : "A party member";
        ApplySchematic(world.GetPrimaryPlayer(), parts[1], readerName);
    }

    // Mirrors the schematic's own effect: the recipe's cvar set to 1 on the
    // local player, which saves and syncs with their player data. Players who
    // already know the recipe get nothing.
    public static void ApplySchematic(EntityPlayerLocal player, string schematicItem, string readerName)
    {
        if (player == null) return;
        ItemClass schematic = ItemClass.GetItemClass(schematicItem);
        Recipe recipe = SchematicRecipe(schematic);
        if (recipe == null || player.GetCVar(recipe.GetName()) > 0f) return;
        player.SetCVar(recipe.GetName(), 1f);
        player.bPlayerStatsChanged = true;

        string schematicName = schematic.GetLocalizedItemName();
        if (string.IsNullOrEmpty(schematicName)) schematicName = schematicItem;
        GameManager.ShowTooltip(player, readerName + " shared " + schematicName + " with you.", false, false, 0f);
    }
}
