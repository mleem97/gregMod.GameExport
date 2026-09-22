using System;

namespace greg.Mods.GameExport.Core;

// Erkennt zur Laufzeit, ob gregCore vorhanden ist (ohne harte Abhaengigkeit
// zur Laufzeit: reiner Typname-Lookup, kein direkter Typzugriff).
// WICHTIG: Methoden, die gregCore-Typen beruehren, duerfen NUR aufgerufen
// werden, wenn HasCore true ist (sonst JIT-TypeLoad bei fehlender DLL).
public static class GregHost
{
    private const string ProbeType = "gregCore.UI.GregNotificationManager, gregCore";
    private static bool? _hasCore;

    public static bool HasCore
    {
        get
        {
            if (_hasCore == null)
            {
                try { _hasCore = Type.GetType(ProbeType) != null; }
                catch { _hasCore = false; }
            }
            return _hasCore.Value;
        }
    }
}
