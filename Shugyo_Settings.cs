using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;

// Learning-speed settings, read from Settings.xml in the mod folder root.
//
// Settings.xml names a preset (SLOW / MEDIUM / FAST) and can override any
// single number. The DLL writes the result into buffShugyoPace while the HOST
// builds buffs.xml:
//
//   WorldStaticData.loadSingleXml runs LoadAndPatchConfig (mod patches), then
//   ApplyConditionalXmlBlocks(Host), then compresses the XML for clients.
//   Prefixing the Host pass means a dedicated server's Settings.xml is baked
//   into the buffs.xml every client receives, so clients' own copies of the
//   file never matter in multiplayer and nobody can speed up their own pace.
//
// Every practice source counts in fractions of a level on one meter per skill
// (see Shugyo_Practice). Settings.xml speaks in "actions per level" because
// that's easier to reason about; the buff stores the reciprocal, the STEP one
// action adds to the meter ($shugyo_step_<key>), so the XML only ever adds.
//
// `attribute` is the one exception to "actions per level": it's practice
// points per rank step (a kill is 1 point), and rank r -> r+1 costs r times
// it. Its step is stored the same way and Shugyo_Attributes turns it back.
//
// `magazine` is different: it's already in levels (0.25 = a quarter of a level
// per read). `share` is read by the server's DLL directly and never reaches
// the buff.
//
// Settings.xml is re-read on every XML load, so in single player a change
// takes effect on the next world load without restarting the game. On a
// dedicated server, restart the server.
public static class Shugyo_Settings
{
    public const string FileName = "Settings.xml";
    public const string DefaultPreset = "SLOW";
    public const float DefaultMagazine = 0.25f;
    public const float MaxMagazine = 10f;

    // Actions-per-level keys, in the order buffShugyoPace seeds their steps;
    // keys are the Settings.xml attribute names.
    private static readonly string[] Keys = { "weapon", "tool", "use", "craft", "station", "vehicle", "wire", "loot", "wreck", "attribute" };

    private static readonly Dictionary<string, float[]> Presets =
        new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
    {
        //                       weapon tool use craft station vehicle wire loot wreck attribute
        { "SLOW",   new float[] { 16f, 80f, 8f, 8f, 16f, 4f, 4f, 30f, 80f, 40f } },
        { "MEDIUM", new float[] { 12f, 60f, 6f, 6f,  8f, 2f, 2f, 20f, 60f, 25f } },
        { "FAST",   new float[] {  4f, 20f, 2f, 2f,  4f, 1f, 1f,  8f, 20f, 10f } },
    };

    public enum ShareMode { Party, Off }

    public static string ModPath;

    public static string Preset { get; private set; } = DefaultPreset;
    private static float[] values = (float[])Presets[DefaultPreset].Clone();

    public static float Magazine { get; private set; } = DefaultMagazine;
    public static ShareMode Share { get; private set; } = ShareMode.Party;

    public static float Weapon => values[0];
    public static float Tool => values[1];
    public static float Use => values[2];
    public static float Craft => values[3];
    public static float Station => values[4];
    public static float Vehicle => values[5];
    public static float Wire => values[6];
    public static float Loot => values[7];
    public static float Wreck => values[8];
    // Practice points per rank step for attributes: rank r -> r+1 costs
    // r x this (see Shugyo_Attributes). Stored as a step like the others.
    public static float Attribute => values[9];

    public static string StepVar(string key) => "$shugyo_step_" + key;
    public const string MagazineVar = "$shugyo_magazine";

    public static void Load()
    {
        Preset = DefaultPreset;
        values = (float[])Presets[DefaultPreset].Clone();
        Magazine = DefaultMagazine;
        Share = ShareMode.Party;
        try
        {
            if (string.IsNullOrEmpty(ModPath)) return;

            string path = Path.Combine(ModPath, FileName);
            if (!File.Exists(path))
            {
                Log.Out("[KitsuneShugyo] No " + FileName + " found; using the " + DefaultPreset + " learning pace.");
                return;
            }

            XElement root = XDocument.Load(path).Root;
            if (root == null) return;

            string preset = ((string)root.Attribute("preset") ?? "").Trim();
            if (preset.Length != 0)
            {
                if (Presets.TryGetValue(preset, out float[] presetValues))
                {
                    Preset = preset.ToUpperInvariant();
                    values = (float[])presetValues.Clone();
                }
                else
                {
                    Log.Warning("[KitsuneShugyo] " + FileName + ": unknown preset '" + preset
                        + "' (use SLOW, MEDIUM or FAST); using " + DefaultPreset + ".");
                }
            }

            // Any number set explicitly overrides the preset's value for that source.
            bool overridden = false;
            for (int i = 0; i < Keys.Length; i++)
            {
                string raw = ((string)root.Attribute(Keys[i]) ?? "").Trim();
                if (raw.Length == 0) continue;
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 1f)
                {
                    values[i] = v;
                    overridden = true;
                }
                else
                {
                    Log.Warning("[KitsuneShugyo] " + FileName + ": " + Keys[i] + "=\"" + raw
                        + "\" is not a number of 1 or more; keeping " + Format(values[i]) + ".");
                }
            }
            if (overridden) Preset += " (custom)";

            string magazine = ((string)root.Attribute("magazine") ?? "").Trim();
            if (magazine.Length != 0)
            {
                if (float.TryParse(magazine, NumberStyles.Float, CultureInfo.InvariantCulture, out float m)
                    && m > 0f && m <= MaxMagazine)
                {
                    Magazine = m;
                }
                else
                {
                    Log.Warning("[KitsuneShugyo] " + FileName + ": magazine=\"" + magazine
                        + "\" is not a number above 0 and up to " + Format(MaxMagazine) + "; keeping "
                        + Format(Magazine) + ".");
                }
            }

            string share = ((string)root.Attribute("share") ?? "").Trim();
            if (share.Length != 0)
            {
                if (string.Equals(share, "party", StringComparison.OrdinalIgnoreCase)) Share = ShareMode.Party;
                else if (string.Equals(share, "off", StringComparison.OrdinalIgnoreCase)) Share = ShareMode.Off;
                else
                {
                    Log.Warning("[KitsuneShugyo] " + FileName + ": share=\"" + share
                        + "\" is not party or off; keeping party.");
                }
            }
        }
        catch (Exception ex)
        {
            Preset = DefaultPreset;
            values = (float[])Presets[DefaultPreset].Clone();
            Magazine = DefaultMagazine;
            Share = ShareMode.Party;
            Log.Warning("[KitsuneShugyo] Failed to read " + FileName + " (" + ex.Message + "); using the "
                + DefaultPreset + " learning pace.");
        }
    }

    // Replace buffShugyoPace's effect groups with one built from the settings.
    public static void ApplyToBuffs(XDocument doc)
    {
        XElement buff = doc?.Root?.Elements("buff")
            .FirstOrDefault(b => (string)b.Attribute("name") == "buffShugyoPace");
        if (buff == null)
        {
            Log.Warning("[KitsuneShugyo] buffShugyoPace not found in buffs.xml; " + FileName + " not applied.");
            return;
        }

        buff.Elements("effect_group").Remove();
        XElement group = new XElement("effect_group");
        for (int i = 0; i < Keys.Length; i++)
        {
            AddBoth(group, StepVar(Keys[i]), Step(values[i]));
        }
        AddBoth(group, MagazineVar, Precise(Magazine));
        buff.Add(group);

        Log.Out("[KitsuneShugyo] Learning pace " + Preset + ": " + string.Join(", ",
            Keys.Select((k, i) => k + " " + Format(values[i])))
            + "; magazine " + Format(Magazine) + " level per read; share " + Share.ToString().ToLowerInvariant());
    }

    // One action's share of a level, at full float precision ("0.###" would
    // turn 1/80 into 0.013 and every 80th hit would fall short).
    public static string Step(float actionsPerLevel)
    {
        return Precise(1f / actionsPerLevel);
    }

    // Plain decimals only: never exponent notation in the XML.
    private static string Precise(float v)
    {
        return v.ToString("0.#########", CultureInfo.InvariantCulture);
    }

    // Both triggers: onSelfBuffStart for new characters, onSelfEnteredGame so
    // existing characters pick up a changed pace on their next login.
    private static void AddBoth(XElement group, string cvar, string value)
    {
        group.Add(ModifyCVar("onSelfBuffStart", cvar, value));
        group.Add(ModifyCVar("onSelfEnteredGame", cvar, value));
    }

    private static XElement ModifyCVar(string trigger, string cvar, string value)
    {
        return new XElement("triggered_effect",
            new XAttribute("trigger", trigger),
            new XAttribute("action", "ModifyCVar"),
            new XAttribute("cvar", cvar),
            new XAttribute("operation", "set"),
            new XAttribute("value", value));
    }

    private static string Format(float v)
    {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

// Host-side pass over each config XML, after every mod's patches are applied
// and before the result is cached for clients (see the header above). The
// original is an iterator, so this prefix runs when the coroutine is created,
// before the conditional blocks are processed. Clients applying configs
// received from a server only ever run the Client evaluator, so they skip this.
[HarmonyPatch(typeof(XmlPatcher))]
[HarmonyPatch("ApplyConditionalXmlBlocks")]
public class Shugyo_BuffsXmlPacePatch
{
    public static void Prefix(string _xmlName, XmlFile _xmlFile, XmlPatcher.EEvaluator _evaluator)
    {
        try
        {
            if (_evaluator != XmlPatcher.EEvaluator.Host || _xmlFile?.XmlDoc == null) return;
            if (!string.Equals(_xmlName, "buffs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_xmlName, "buffs.xml", StringComparison.OrdinalIgnoreCase)) return;

            Shugyo_Settings.Load();
            Shugyo_Settings.ApplyToBuffs(_xmlFile.XmlDoc);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneShugyo] applying " + Shugyo_Settings.FileName + " to buffs.xml failed: " + ex);
        }
    }
}
