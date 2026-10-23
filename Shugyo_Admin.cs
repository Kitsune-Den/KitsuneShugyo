using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;

// Version handshake, so a client and server on different Shugyo versions
// find out at once instead of practice silently not counting (a 0.1.0 client
// on a 0.2.0 server could not run the server's buffs.xml and nobody could
// tell why).
//
// Same transport as party sharing (Shugyo_Sharing.cs): vanilla console
// packages with a shugyo_* verb, swallowed by the inbox patches.
//
//   1. Server, when a player spawns into the world from joining: sends
//      "shugyo_version <server version>" to that client.
//   2. Client: replies "shugyo_hello <client version>" and, if the two
//      differ, says so on screen and in its log.
//   3. Server: records the reply, and logs a warning on a mismatch.
//
// A client without Shugyo, or older than 1.1.0, never replies: `shugyo
// version` lists it as "no reply", and after 30 s the server logs a warning
// too, since that player's reads and practice never reach it. A server without
// Shugyo never asks.
public static class Shugyo_Version
{
    public const string VersionVerb = "shugyo_version";
    public const string HelloVerb = "shugyo_hello";
    public const string AdminVerb = "shugyo_admin";

    public static string Mine = "unknown";

    // Server: entityId -> the version that client reported.
    private static readonly Dictionary<int, string> clientVersions = new Dictionary<int, string>();

    public static string ClientVersion(int entityId)
    {
        return clientVersions.TryGetValue(entityId, out string v) ? v : null;
    }

    // Asked, not answered yet: entityId -> (client, when asked).
    private const float ReplySeconds = 30f;
    private static readonly Dictionary<int, KeyValuePair<ClientInfo, float>> waiting =
        new Dictionary<int, KeyValuePair<ClientInfo, float>>();

    // Step 1.
    public static void AskClient(ClientInfo client)
    {
        if (client == null) return;
        clientVersions.Remove(client.entityId);
        waiting[client.entityId] = new KeyValuePair<ClientInfo, float>(client, UnityEngine.Time.realtimeSinceStartup);
        client.SendPackage(NetPackageManager.GetPackage<NetPackageConsoleCmdClient>()
            .Setup(VersionVerb + " " + Mine, true));
    }

    // Every game update on the server: a client that hasn't answered in 30 s
    // isn't running Shugyo (Maltaine, 2026-10-23: no practice and no shared
    // reads, and the log said nothing). Logged once; a player who left is skipped.
    public static void CheckReplies(ref ModEvents.SGameUpdateData _data)
    {
        if (waiting.Count == 0) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        List<int> due = null;
        foreach (KeyValuePair<int, KeyValuePair<ClientInfo, float>> entry in waiting)
        {
            if (now - entry.Value.Value >= ReplySeconds) (due ?? (due = new List<int>())).Add(entry.Key);
        }
        if (due == null) return;
        ConnectionManager connections = SingletonMonoBehaviour<ConnectionManager>.Instance;
        foreach (int entityId in due)
        {
            ClientInfo client = waiting[entityId].Key;
            waiting.Remove(entityId);
            if (connections?.Clients?.ForEntityId(entityId) != client) continue;
            Log.Warning("[KitsuneShugyo] " + client.playerName + "'s game didn't answer the version check: Kitsune "
                + "Shugyo isn't running there (not installed, or switched off because KitsuneSharedMagazines or "
                + "KitsuneLearnedSkills is installed too). Their practice and shared reads won't count until it is.");
        }
    }

    // Step 3.
    public static void OnHello(ClientInfo sender, string cmd)
    {
        if (sender == null) return;
        waiting.Remove(sender.entityId);
        string theirs = cmd.Substring(HelloVerb.Length).Trim();
        if (theirs.Length == 0 || theirs.Length > 32) return;
        clientVersions[sender.entityId] = theirs;
        if (theirs != Mine)
        {
            Log.Warning("[KitsuneShugyo] " + sender.playerName + " runs Kitsune Shugyo " + theirs
                + "; this server runs " + Mine + ". Practice may not count for them until they match.");
        }
    }

    // Step 2, and admin requests (see ConsoleCmdShugyo). Returns true when the
    // line was ours.
    public static bool HandleClientLine(World world, string line)
    {
        if (line.StartsWith(VersionVerb + " ", StringComparison.Ordinal))
        {
            string server = line.Substring(VersionVerb.Length).Trim();
            SingletonMonoBehaviour<ConnectionManager>.Instance?.SendToServer(
                NetPackageManager.GetPackage<NetPackageConsoleCmdServer>().Setup(HelloVerb + " " + Mine));
            if (server != Mine)
            {
                string text = "This server runs Kitsune Shugyō " + server + " and you have " + Mine
                    + ". Install the server's version, or your practice may not count.";
                Log.Warning("[KitsuneShugyo] " + text);
                EntityPlayerLocal player = world?.GetPrimaryPlayer();
                if (player != null) GameManager.ShowTooltip(player, text, false, false, 0f);
            }
            return true;
        }
        if (line.StartsWith(AdminVerb + " ", StringComparison.Ordinal))
        {
            ConsoleCmdShugyo.ApplyOnClient(world?.GetPrimaryPlayer(), line.Substring(AdminVerb.Length).Trim());
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(GameManager))]
[HarmonyPatch("PlayerSpawnedInWorld")]
public class Shugyo_VersionAskPatch
{
    public static void Postfix(ClientInfo _cInfo, RespawnType _respawnReason)
    {
        try
        {
            if (!Shugyo_Attributes.Enabled || _cInfo == null) return;
            if (_respawnReason != RespawnType.JoinMultiplayer && _respawnReason != RespawnType.EnterMultiplayer) return;
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
            Shugyo_Version.AskClient(_cInfo);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] version check failed: " + ex);
        }
    }
}

// The admin console command (F1 in game, or the server console / telnet).
//
//   shugyo version                     this server's Shugyo and each player's
//   shugyo status [player]             every meter: rank or level, % to the
//                                      next, fatigue, top practice sources
//   shugyo give <player> <meter> <n>   add practice: attribute meters take
//                                      points (a walker kill is 1), crafting
//                                      skills take levels (0.5 = half a level)
//   shugyo rest <player>               clear that player's fatigue
//
// Status reads the copy of the player the server keeps (meters and levels are
// net synced). Give and rest have to change the player on their own client,
// which owns its progression, so the server sends them a shugyo_admin line
// (the same transport as the version handshake); a listen host or single
// player applies it directly. Admin-only by default; serveradmin.xml can open
// it up like any command.
public class ConsoleCmdShugyo : ConsoleCmdAbstract
{
    public override string[] getCommands()
    {
        return new[] { "shugyo" };
    }

    public override string getDescription()
    {
        return "Kitsune Shugyo: practice meters, sources and fatigue; give practice; version check.";
    }

    public override string getHelp()
    {
        return "Usage:\n"
            + "  shugyo version - this server's Kitsune Shugyo and each player's\n"
            + "  shugyo status [player] - meters, fatigue and top sources (yourself if no player)\n"
            + "  shugyo give <player> <meter> <amount> - add practice (attPerception etc. take points, craftingClubs etc. take levels)\n"
            + "  shugyo rest <player> - clear that player's fatigue\n"
            + "<player> is a name, entity id or platform id.";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        try
        {
            if (!Shugyo_Attributes.Enabled)
            {
                Out("Kitsune Shugyo is not running (see the log for why).");
                return;
            }
            string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "status";
            switch (sub)
            {
            case "version":
                Version();
                break;
            case "status":
                Status(Target(_params.Count > 1 ? _params[1] : null, _senderInfo));
                break;
            case "give":
                if (_params.Count < 4)
                {
                    Out(getHelp());
                    return;
                }
                Give(Target(_params[1], _senderInfo), _params[2], _params[3]);
                break;
            case "rest":
                Rest(Target(_params.Count > 1 ? _params[1] : null, _senderInfo));
                break;
            default:
                Out(getHelp());
                break;
            }
        }
        catch (Exception ex)
        {
            Out("shugyo failed: " + ex.Message);
            Log.Error("[KitsuneShugyo] shugyo command failed: " + ex);
        }
    }

    private static void Out(string line)
    {
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output(line);
    }

    // The named player, or the sender when no name is given.
    private static EntityPlayer Target(string param, CommandSenderInfo sender)
    {
        World world = GameManager.Instance?.World;
        if (world == null) return null;
        if (string.IsNullOrEmpty(param))
        {
            if (sender.RemoteClientInfo != null && world.Players.dict.TryGetValue(sender.RemoteClientInfo.entityId, out EntityPlayer remote))
                return remote;
            EntityPlayer local = world.GetPrimaryPlayer();
            if (local == null) Out("Name a player: the server console has no player of its own.");
            return local;
        }
        ClientInfo client = ConsoleHelper.ParseParamIdOrName(param);
        if (client != null && world.Players.dict.TryGetValue(client.entityId, out EntityPlayer found)) return found;
        EntityPlayer primary = world.GetPrimaryPlayer();
        if (primary != null && (string.Equals(primary.EntityName, param, StringComparison.OrdinalIgnoreCase)
            || primary.entityId.ToString(CultureInfo.InvariantCulture) == param))
            return primary;
        Out("No player '" + param + "' in the game.");
        return null;
    }

    private static void Version()
    {
        Out("Kitsune Shugyo " + Shugyo_Version.Mine + " on this " + (GameManager.IsDedicatedServer ? "server." : "game."));
        World world = GameManager.Instance?.World;
        if (world == null) return;
        foreach (EntityPlayer player in world.Players.list)
        {
            if (player is EntityPlayerLocal)
            {
                Out("  " + player.EntityName + ": " + Shugyo_Version.Mine + " (this game)");
                continue;
            }
            string theirs = Shugyo_Version.ClientVersion(player.entityId);
            Out("  " + player.EntityName + ": " + (theirs == null
                ? "no reply (no Kitsune Shugyo, or older than 1.1.0)"
                : theirs + (theirs == Shugyo_Version.Mine ? "" : "  <- different from the server")));
        }
    }

    private static void Status(EntityPlayer player)
    {
        if (player == null) return;
        Out("Kitsune Shugyo practice for " + player.EntityName + ":");
        Out("Attributes (rank, % to next, fatigue, top sources):");
        foreach (string name in Shugyo_Attributes.Names)
        {
            ProgressionValue value = player.Progression?.GetProgressionValue(name);
            if (value == null) continue;
            float fatigue = Cvar(player, Shugyo_Fatigue.Var(name));
            Out(string.Format(CultureInfo.InvariantCulture, "  {0,-14} rank {1}/{2}  {3,3}%  fatigue {4:0.00}{5}  {6}",
                name, value.Level, value.ProgressionClass.MaxLevel, Percent(player, name, value),
                fatigue, fatigue >= 1f ? " (tired)" : "", Sources(player, name)));
        }
        Out("Crafting skills (level, % to next, top sources):");
        foreach (string name in Shugyo_Practice.SkillNames)
        {
            ProgressionValue value = player.Progression?.GetProgressionValue(name);
            if (value == null) continue;
            Out(string.Format(CultureInfo.InvariantCulture, "  {0,-24} {1,3}/{2,-3} {3,3}%  {4}",
                name, value.Level, value.ProgressionClass.MaxLevel, Percent(player, name, value), Sources(player, name)));
        }
    }

    private static int Percent(EntityPlayer player, string meter, ProgressionValue value)
    {
        if (value.Level >= value.ProgressionClass.MaxLevel) return 100;
        return (int)Math.Floor(Math.Min(Cvar(player, Shugyo_Practice.MeterPrefix + meter), Shugyo_Practice.Threshold) * 100f);
    }

    // "kill 62% hit 30% magazine 8%" from the $shugyo_src_<meter>_* cvars.
    private static string Sources(EntityPlayer player, string meter)
    {
        if (player.Buffs == null) return "";
        string prefix = Shugyo_Practice.SourcePrefix + meter + "_";
        var totals = player.Buffs.EnumerateCustomVars(prefix, true)
            .Where(kv => kv.Value > 0f)
            .Select(kv => new KeyValuePair<string, float>(kv.Key.Substring(prefix.Length), kv.Value))
            .ToList();
        float sum = totals.Sum(kv => kv.Value);
        if (sum <= 0f) return "";
        var text = new StringBuilder();
        foreach (var kv in totals.OrderByDescending(kv => kv.Value).Take(3))
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(kv.Key).Append(' ').Append(Math.Round(kv.Value / sum * 100f).ToString(CultureInfo.InvariantCulture)).Append('%');
        }
        return text.ToString();
    }

    private static float Cvar(EntityPlayer player, string var)
    {
        return player.Buffs != null && player.Buffs.HasCustomVar(var) ? player.Buffs.GetCustomVar(var) : 0f;
    }

    private static void Give(EntityPlayer player, string meter, string amountText)
    {
        if (player == null) return;
        if (!Shugyo_Attributes.IsPracticed(meter) && !Shugyo_Practice.IsSkill(meter))
        {
            Out("'" + meter + "' is not an attribute (attPerception, attStrength, attFortitude, attAgility, attIntellect) or a crafting skill (craftingClubs, ...).");
            return;
        }
        if (!float.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount)
            || amount <= 0f || amount > 1000f)
        {
            Out("The amount must be a number above 0 (attributes: points, crafting skills: levels).");
            return;
        }
        Send(player, "give " + meter + " " + amount.ToString(CultureInfo.InvariantCulture));
        Out("Gave " + player.EntityName + " " + amountText + (Shugyo_Attributes.IsPracticed(meter) ? " practice points of " : " levels of ") + meter + ".");
    }

    private static void Rest(EntityPlayer player)
    {
        if (player == null) return;
        Send(player, "rest");
        Out("Cleared " + player.EntityName + "'s fatigue.");
    }

    // Run on the player's own client: in-process when it's this game's player,
    // otherwise as a shugyo_admin line to their client.
    private static void Send(EntityPlayer player, string request)
    {
        if (player is EntityPlayerLocal local && !local.isEntityRemote)
        {
            ApplyOnClient(local, request);
            return;
        }
        ClientInfo client = SingletonMonoBehaviour<ConnectionManager>.Instance?.Clients?.ForEntityId(player.entityId);
        if (client == null)
        {
            Out(player.EntityName + " has no connection to send it to.");
            return;
        }
        if (Shugyo_Version.ClientVersion(player.entityId) == null)
        {
            Out("Note: " + player.EntityName + " never answered the version check, so their game may not understand this.");
        }
        client.SendPackage(NetPackageManager.GetPackage<NetPackageConsoleCmdClient>()
            .Setup(Shugyo_Version.AdminVerb + " " + request, true));
    }

    // The client side of give and rest.
    public static void ApplyOnClient(EntityPlayerLocal player, string request)
    {
        if (player == null || player.Buffs == null) return;
        string[] parts = request.Split(' ');
        if (parts.Length == 3 && parts[0] == "give"
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) && amount > 0f)
        {
            // Untracked (no source), so admin gifts don't show up as "top sources",
            // and untiring, so a test gift isn't cut to a quarter.
            if (Shugyo_Attributes.IsPracticed(parts[1])) Shugyo_Attributes.Add(player, parts[1], amount, null, false);
            else if (Shugyo_Practice.IsSkill(parts[1])) Shugyo_Practice.Add(player, parts[1], amount, null);
            return;
        }
        if (parts.Length == 1 && parts[0] == "rest")
        {
            foreach (string name in Shugyo_Attributes.Names)
            {
                player.Buffs.SetCustomVar(Shugyo_Fatigue.Var(name), 0f, true, CVarOperation.set);
            }
        }
    }
}
