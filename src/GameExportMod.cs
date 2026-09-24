using MelonLoader;
using UnityEngine;
using greg.Mods.GameExport.Core;

[assembly: MelonInfo(typeof(greg.Mods.GameExport.GameExportMod), "gregMod.GameExport", "1.0.0", "TeamGreg Modding")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace greg.Mods.GameExport;

public class GameExportMod : MelonMod
{
    internal static MelonPreferences_Entry<bool> EnabledEntry;
    internal static MelonPreferences_Entry<string> ExportKeyEntry;
    private static UnityEngine.InputSystem.Key _exportKey = UnityEngine.InputSystem.Key.F12;
    private static bool _exporting;

    public override void OnInitializeMelon()
    {
        var cat = MelonPreferences.CreateCategory("GameExport");
        EnabledEntry = cat.CreateEntry("Enabled", true, "Enabled",
            "Exports the vanilla reference to ~/GameExport/{timestamp}/.");
        ExportKeyEntry = cat.CreateEntry("ExportKey", "F12", "ExportKey",
            "Hotkey to export the vanilla reference.");
        try
        {
            if (System.Enum.TryParse<UnityEngine.InputSystem.Key>(ExportKeyEntry.Value, true, out var k)
                && k != UnityEngine.InputSystem.Key.None)
                _exportKey = k;
            else
                MelonLogger.Warning($"[GameExport] Unknown ExportKey '{ExportKeyEntry.Value}', defaulting to F12.");
        }
        catch { }
        MelonLogger.Msg($"[GameExport] Ready. {_exportKey} = export to ~/GameExport/{{timestamp}}/.");
        if (GregHost.HasCore)
        {
            try { RegisterCoreExtras(); } catch { }
        }
    }

    // Mod contract + key HUD + opener for F1 hub. Call only with gregCore
    // (own method for JIT split without gregCore DLL).
    private static void RegisterCoreExtras()
    {
        try
        {
            gregCore.Core.Mods.GregModRegistry.Register(
                "gregMod.GameExport", "GameExport", "1.0.0",
                new string[] { "gameexport" });
            gregCore.UI.GregHudRegistry.Register("gameexport", _exportKey.ToString(), "Export");
            gregCore.UI.GregMenuRegistry.RegisterOpener("gameexport", () => StartExport());
        }
        catch (System.Exception ex)
        {
            MelonLogger.Warning("[GameExport] Hub registration failed: " + ex.GetBaseException().Message);
        }
    }

    internal static void StartExport()
    {
        if (_exporting) return;
        bool enabled = true;
        try { if (EnabledEntry != null) enabled = EnabledEntry.Value; } catch { }
        if (!enabled) return;
        _exporting = true;
        MelonLogger.Msg("[GameExport] Export starting ...");
        try { MelonCoroutines.Start(GameExporter.Run(() => { _exporting = false; })); }
        catch { _exporting = false; }
    }

    public override void OnUpdate()
    {
        if (_exporting) return;
        try
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                var ctrl = kb[_exportKey];
                if (ctrl != null && ctrl.wasPressedThisFrame)
                    StartExport();
            }
        }
        catch { }
    }
}
