using UnityEngine;

// When Kitsune Shugyo switches itself off because a mod it replaces is
// installed too (KitsuneLearnedSkills, KitsuneSharedMagazines,
// KitsuneSharedReading), the only trace was an error in the log. Players never
// saw it: practice and party sharing just didn't work, with no idea why (Ada
// and Maltaine, 2026-10-23). This tells them on screen and in chat, once,
// a few seconds after they're in a world.
//
// A mod event, not a Harmony patch: a switched-off Shugyo applies none.
// A dedicated server has no local player, so it only ever logs.
public static class Shugyo_ConflictNotice
{
    private const float DelaySeconds = 5f;

    private static string text;
    private static float firstSeen = -1f;
    private static bool shown;

    public static void Show(string otherMod)
    {
        text = "Kitsune Shugyō is switched off: " + otherMod + " is installed too, and Shugyō replaces it. "
            + "Remove the " + otherMod + " folder from your Mods folder and restart the game. "
            + "Until then your practice and shared reads don't count.";
        ModEvents.GameUpdate.RegisterHandler(OnGameUpdate);
    }

    private static void OnGameUpdate(ref ModEvents.SGameUpdateData _data)
    {
        if (shown || text == null) return;
        EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
        if (player == null || player.IsDead())
        {
            firstSeen = -1f;
            return;
        }
        // Wait out the loading screen, so the message isn't shown behind it.
        if (firstSeen < 0f) firstSeen = Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - firstSeen < DelaySeconds) return;

        shown = true;
        GameManager.ShowTooltip(player, text, false, false, 0f);
        XUi xui = LocalPlayerUI.GetUIForPlayer(player)?.xui;
        if (xui != null) XUiC_ChatOutput.AddMessage(xui, EnumGameMessages.PlainTextLocal, "[ff8040]" + text + "[-]");
    }
}
