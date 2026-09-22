using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace greg.Mods.GameExport.Core;

// VanillaReferenz-Export nach ~/GameExport/{timestamp}/.
// Struktur folgt gregsPorter.md: Assembly -> Namespace -> Type -> Member
// und Scene -> GameObject -> Component (+ Shop, Input, Audio, Abhaengigkeiten).
// Alles gedeckelt + try/catch pro Einheit (obfuscation-/stripping-sicher).
// Laeuft als Coroutine (Slices pro Frame, kein Dauer-Hitch).
public static class GameExporter
{
    private const int MaxTypes = 1500;
    private const int MaxMembers = 120;
    private const int MaxSceneNodes = 3000;
    private const int MaxSceneDepth = 8;
    private const int MaxMaterials = 400;
    private const int SliceTypes = 60;
    private const int SliceNodes = 400;

    public static IEnumerator Run(Action onDone)
    {
        string dir = null;
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            dir = Path.Combine(home, "GameExport", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            MelonLogger.Error("[GameExport] Zielordner fehlgeschlagen: " + ex.GetBaseException().Message);
            try { onDone?.Invoke(); } catch { }
            yield break;
        }

        MelonLogger.Msg("[GameExport] Ziel: " + dir);
        yield return null;
        yield return WriteSection(dir, "README.md", WriteReadme);
        yield return WriteSection(dir, "assemblies.md", WriteAssemblies);
        yield return WriteSection(dir, "scene.md", WriteScene);
        yield return WriteSection(dir, "shop-catalog.md", WriteShop);
        yield return WriteSection(dir, "input.md", WriteInput);
        yield return WriteSection(dir, "audio.md", WriteAudio);
        yield return WriteSection(dir, "materials.md", WriteMaterials);
        yield return WriteSection(dir, "dependencies.mmd", WriteDependencies);
        MelonLogger.Msg("[GameExport] Fertig: " + dir);
        try { onDone?.Invoke(); } catch { }
    }

    private static IEnumerator WriteSection(string dir, string file, Func<StringBuilder, IEnumerator> writer)
    {
        var sb = new StringBuilder(1 << 16);
        IEnumerator it = null;
        try { it = writer(sb); } catch { }
        if (it != null)
        {
            while (true)
            {
                bool more = false;
                try { more = it.MoveNext(); } catch { break; }
                if (!more) break;
                yield return null;
            }
        }
        try { File.WriteAllText(Path.Combine(dir, file), sb.ToString()); }
        catch (Exception ex) { MelonLogger.Warning($"[GameExport] {file} schreiben fehlgeschlagen: {ex.Message}"); }
    }

    private static void H(StringBuilder sb, string title)
    {
        sb.Append("# ").Append(title).Append("\n\n");
    }

    private static string Safe(string s, int max = 160)
    {
        if (string.IsNullOrEmpty(s)) return "-";
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        return s.Length > max ? s.Substring(0, max) : s;
    }

    // ---- README ----
    private static IEnumerator WriteReadme(StringBuilder sb)
    {
        H(sb, "VanillaReferenz");
        sb.Append($"Export: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");
        try { sb.Append($"Spiel: {Application.productName} {Application.version} (Unity {Application.unityVersion})\n\n"); } catch { }
        try
        {
            var scene = SceneManager.GetActiveScene();
            sb.Append($"Aktive Szene: {Safe(scene.name)} (Build-Index {scene.buildIndex})\n\n");
        }
        catch { }
        sb.Append("## Dateien\n\n");
        sb.Append("- assemblies.md — Assembly-CSharp: Namespace -> Typ -> Member\n");
        sb.Append("- scene.md — Szenen-Hierarchie: GameObject -> Transform -> Components\n");
        sb.Append("- shop-catalog.md — Shop-Items (ID/Name/Preis/Typ)\n");
        sb.Append("- input.md — InputDevices + PlayerInput-Maps\n");
        sb.Append("- audio.md — AudioManager-Clips + Quellen\n");
        sb.Append("- materials.md — Renderer/Material/Shader/Textur-Inventar\n");
        sb.Append("- dependencies.mmd — Mermaid: Mods/Plugins -> Assembly-Referenzen\n");
        yield break;
    }

    // ---- Assemblies ----
    private static IEnumerator WriteAssemblies(StringBuilder sb)
    {
        H(sb, "Assemblies (Assembly-CSharp)");
        Assembly target = null;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (a != null && a.GetName().Name == "Assembly-CSharp") { target = a; break; } } catch { }
            }
        }
        catch { }
        if (target == null) { sb.Append("Assembly-CSharp nicht gefunden.\n"); yield break; }
        Type[] types;
        try { types = target.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        catch { sb.Append("Typen nicht lesbar.\n"); yield break; }
        var byNs = new SortedDictionary<string, List<Type>>(StringComparer.Ordinal);
        int total = 0;
        foreach (var t in types)
        {
            if (t == null || total >= MaxTypes) break;
            try
            {
                string ns = t.Namespace ?? "(global)";
                if (!byNs.TryGetValue(ns, out var list)) byNs[ns] = list = new List<Type>();
                list.Add(t);
                total++;
            }
            catch { }
            if (total % SliceTypes == 0) yield return null;
        }
        sb.Append($"Typen: {total} (gedeckelt)\n\n");
        foreach (var kv in byNs)
        {
            sb.Append($"## {Safe(kv.Key)}\n\n");
            foreach (var t in kv.Value)
            {
                try
                {
                    sb.Append($"### {Safe(t.FullName)}");
                    try { if (t.BaseType != null) sb.Append($" : {Safe(t.BaseType.FullName)}"); } catch { }
                    sb.Append("\n");
                    AppendMembers(sb, "Fields", Get(() => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), f => f.Name + " : " + f.FieldType.Name));
                    AppendMembers(sb, "Properties", Get(() => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), p => p.Name));
                    AppendMembers(sb, "Methods", Get(() => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly), m => m.Name + "(" + m.GetParameters().Length + ")"));
                    AppendMembers(sb, "Events", Get(() => t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), e => e.Name));
                    sb.Append("\n");
                }
                catch { }
            }
            yield return null;
        }
    }

    private static List<string> Get<T>(Func<T[]> fetch, Func<T, string> fmt)
    {
        var out_ = new List<string>();
        try
        {
            var arr = fetch();
            if (arr == null) return out_;
            foreach (var m in arr)
            {
                if (out_.Count >= MaxMembers) break;
                try { out_.Add(fmt(m)); } catch { }
            }
        }
        catch { }
        return out_;
    }

    private static void AppendMembers(StringBuilder sb, string title, List<string> items)
    {
        if (items.Count == 0) return;
        sb.Append($"- {title} ({items.Count}): ");
        sb.Append(string.Join(", ", items.ToArray()));
        sb.Append("\n");
    }

    // ---- Szene ----
    private static IEnumerator WriteScene(StringBuilder sb)
    {
        H(sb, "Szene");
        GameObject[] roots = null;
        string sceneName = "?";
        try
        {
            var scene = SceneManager.GetActiveScene();
            sceneName = scene.name;
            roots = scene.GetRootGameObjects();
        }
        catch (Exception ex)
        {
            sb.Append("Szene nicht lesbar: " + Safe(ex.Message) + "\n");
            yield break;
        }
        sb.Append($"Szene: {Safe(sceneName)}, Root-Objekte: {(roots != null ? roots.Length : 0)}\n\n");
        int count = 0;
        if (roots != null)
        {
            foreach (var r in roots)
            {
                count = DumpNode(sb, r, 0, count);
                if (count % SliceNodes == 0) yield return null;
                if (count >= MaxSceneNodes) { sb.Append("\n...(gedeckelt)\n"); break; }
            }
        }
        sb.Append($"\nKnoten gesamt: {count}\n");
        yield break;
    }

    private static int DumpNode(StringBuilder sb, GameObject go, int depth, int count)
    {
        if (go == null || depth > MaxSceneDepth || count >= MaxSceneNodes) return count;
        string name = "?";
        bool active = false;
        try { name = go.name; active = go.activeSelf; } catch { return count; }
        sb.Append(new string(' ', depth * 2));
        sb.Append($"- [{(active ? "x" : " ")}] {Safe(name)}");
        try
        {
            var comps = go.GetComponents<Component>();
            if (comps != null)
            {
                var names = new List<string>();
                foreach (var c in comps)
                {
                    try { if (c != null) names.Add(c.GetType().Name); } catch { }
                    if (names.Count >= 12) break;
                }
                sb.Append("  <" + string.Join(", ", names.ToArray()) + ">");
            }
        }
        catch { }
        sb.Append("\n");
        count++;
        Transform t = null;
        try { t = go.transform; } catch { return count; }
        if (t == null) return count;
        int kids = 0;
        try { kids = t.childCount; } catch { return count; }
        for (int i = 0; i < kids && count < MaxSceneNodes; i++)
        {
            GameObject child = null;
            try
            {
                var ct = t.GetChild(i);
                if (ct != null) child = ct.gameObject;
            }
            catch { }
            count = DumpNode(sb, child, depth + 1, count);
        }
        return count;
    }

    // ---- Shop ----
    private static IEnumerator WriteShop(StringBuilder sb)
    {
        H(sb, "Shop-Katalog");
        global::Il2Cpp.ComputerShop[] shops = null;
        try { shops = UnityEngine.Object.FindObjectsOfType<global::Il2Cpp.ComputerShop>(); }
        catch (Exception ex) { sb.Append("Fehler: " + Safe(ex.Message) + "\n"); yield break; }
        {
            if (shops == null || shops.Length == 0) { sb.Append("Kein ComputerShop in Szene.\n"); yield break; }
            foreach (var shop in shops)
            {
                if (shop == null) continue;
                string shopName = "?";
                try { shopName = shop.gameObject != null ? shop.gameObject.name : "?"; } catch { }
                sb.Append($"## Shop: {Safe(shopName)}\n\n");
                sb.Append("| ID | Name | Preis | Typ |\n|---|---|---|---|\n");
                object itemsObj = null;
                try { itemsObj = shop.shopItems; } catch { itemsObj = null; }
                if (itemsObj == null) { sb.Append("(keine Items)\n"); continue; }
                var items = (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<global::Il2Cpp.ShopItem>)itemsObj;
                int n = 0;
                try { n = items.Length; } catch { }
                for (int i = 0; i < n && i < 500; i++)
                {
                    try
                    {
                        var it = items[i];
                        if (it == null || it.shopItemSO == null) continue;
                        var so = it.shopItemSO;
                        int id = 0; string nm = "?"; int price = 0; string ty = "?";
                        try { id = so.itemID; } catch { }
                        try { nm = so.itemName; } catch { }
                        try { price = so.price; } catch { }
                        try { ty = so.itemType.ToString(); } catch { }
                        sb.Append($"| {id} | {Safe(nm, 60)} | {price} | {Safe(ty, 30)} |\n");
                    }
                    catch { }
                    if (i % 100 == 0) yield return null;
                }
                sb.Append("\n");
            }
        }
        yield break;
    }

    // ---- Input ----
    private static IEnumerator WriteInput(StringBuilder sb)
    {
        H(sb, "Input");
        try
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var ms = UnityEngine.InputSystem.Mouse.current;
            var gp = UnityEngine.InputSystem.Gamepad.current;
            sb.Append($"Devices: Keyboard={(kb != null ? "ja" : "nein")}, Mouse={(ms != null ? "ja" : "nein")}, Gamepad={(gp != null ? "ja" : "nein")}\n\n");
        }
        catch { }
        try
        {
            var pis = UnityEngine.Object.FindObjectsOfType<UnityEngine.InputSystem.PlayerInput>();
            sb.Append($"PlayerInput-Komponenten: {(pis != null ? pis.Length : 0)}\n\n");
            if (pis != null)
            {
                int shown = 0;
                foreach (var pi in pis)
                {
                    if (pi == null || shown >= 10) break;
                    try
                    {
                        string go = pi.gameObject != null ? pi.gameObject.name : "?";
                        sb.Append($"- {Safe(go)}: Aktionen=");
                        try
                        {
                            var asset = pi.actions;
                            if (asset != null)
                            {
                                var names = new List<string>();
                                foreach (var map in asset.actionMaps)
                                {
                                    try { if (map != null) names.Add(map.name); } catch { }
                                }
                                sb.Append(string.Join("/", names.ToArray()));
                            }
                            else sb.Append("-");
                        }
                        catch { sb.Append("?"); }
                        sb.Append("\n");
                        shown++;
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex) { sb.Append("Fehler: " + Safe(ex.Message) + "\n"); }
        yield break;
    }

    // ---- Audio ----
    private static IEnumerator WriteAudio(StringBuilder sb)
    {
        H(sb, "Audio");
        try
        {
            var mgr = Il2Cpp.AudioManager.instance;
            if (mgr == null) { sb.Append("AudioManager.instance = null.\n"); yield break; }
            try
            {
                var src = mgr.musicAudioSource;
                sb.Append($"musicAudioSource: {(src != null ? "vorhanden" : "null")}\n");
                if (src != null)
                {
                    try { sb.Append($"  volume={src.volume}, loop={src.loop}, clip={(src.clip != null ? src.clip.name : "-")}\n"); } catch { }
                }
            }
            catch { }
            foreach (var f in new[] { "calmMusic", "iddleMusic", "fastMusic" })
            {
                try
                {
                    AudioClip clip = null;
                    if (f == "calmMusic") clip = mgr.calmMusic;
                    else if (f == "iddleMusic") clip = mgr.iddleMusic;
                    else clip = mgr.fastMusic;
                    sb.Append($"- {f}: {(clip != null ? Safe(clip.name) + $" ({clip.length:0.0}s, {clip.channels}ch, {clip.frequency}Hz)" : "-")}\n");
                }
                catch { sb.Append($"- {f}: ?\n"); }
            }
        }
        catch (Exception ex) { sb.Append("Fehler: " + Safe(ex.Message) + "\n"); }
        yield break;
    }

    // ---- Materials ----
    private static IEnumerator WriteMaterials(StringBuilder sb)
    {
        H(sb, "Materials/Texturen/Meshes (Szene, gedeckelt)");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int renderers = 0, mats = 0;
        Renderer[] all = null;
        try { all = UnityEngine.Object.FindObjectsOfType<Renderer>(); } catch { all = null; }
        if (all != null)
        {
            var lines = new List<string>();
            foreach (var r in all)
            {
                if (r == null || renderers >= MaxMaterials) break;
                renderers++;
                try
                {
                    string go = r.gameObject != null ? r.gameObject.name : "?";
                    var mf = r.GetComponent<MeshFilter>();
                    string mesh = "-";
                    try
                    {
                        if (mf != null && mf.sharedMesh != null)
                            mesh = $"{mf.sharedMesh.name} ({mf.sharedMesh.vertexCount}v/{mf.sharedMesh.triangles.Length / 3}t)";
                    }
                    catch { }
                    var mlist = new List<string>();
                    try
                    {
                        foreach (var m in r.sharedMaterials)
                        {
                            try
                            {
                                if (m == null) continue;
                                string sh = "?";
                                try { if (m.shader != null) sh = m.shader.name; } catch { }
                                mlist.Add($"{m.name}[{sh}]");
                            }
                            catch { }
                            if (mlist.Count >= 4) break;
                        }
                    }
                    catch { }
                    string key = go + "|" + string.Join(";", mlist.ToArray());
                    if (seen.Add(key))
                        lines.Add($"- {Safe(go, 60)} :: {Safe(mesh, 60)} :: {Safe(string.Join(" + ", mlist.ToArray()), 120)}");
                }
                catch { }
                if (renderers % SliceNodes == 0) yield return null;
            }
            sb.Append($"Renderer: {renderers}, eindeutige Combos: {lines.Count}\n\n");
            foreach (var l in lines) sb.Append(l).Append("\n");
            mats = lines.Count;
        }
        yield break;
    }

    // ---- Dependencies (Mermaid) ----
    private static IEnumerator WriteDependencies(StringBuilder sb)
    {
        sb.Append("graph TD\n");
        int n = 0;
        var melons = new List<MelonBase>();
        try { foreach (var m in MelonBase.RegisteredMelons) { if (m != null) melons.Add(m); } } catch { }
        {
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in melons)
            {
                string name = "?";
                string ver = "";
                try { name = m.Info != null ? m.Info.Name ?? "?" : "?"; } catch { }
                try { ver = m.Info != null ? m.Info.Version ?? "" : ""; } catch { }
                string id = "M" + n++;
                string key = "";
                try { key = m.GetType().AssemblyQualifiedName ?? name; } catch { key = name; }
                ids[key] = id;
                string kind = "Mod";
                try { if (m is MelonPlugin) kind = "Plugin"; } catch { }
                sb.Append($"  {id}[\"{Safe(name, 40)} {Safe(ver, 12)}\\n({kind})\"]\n");
                yield return null;
            }
            foreach (var m in melons)
            {
                string from = "?";
                try { from = m.GetType().AssemblyQualifiedName ?? ""; } catch { }
                if (!ids.TryGetValue(from, out string fromId)) continue;
                System.Reflection.Assembly asm = null;
                try { asm = m.GetType().Assembly; } catch { continue; }
                if (asm == null) continue;
                System.Reflection.AssemblyName[] refs = null;
                try { refs = asm.GetReferencedAssemblies(); } catch { continue; }
                if (refs == null) continue;
                foreach (var r in refs)
                {
                    string rn = "";
                    try { rn = r.Name ?? ""; } catch { continue; }
                    if (!rn.StartsWith("greg", StringComparison.OrdinalIgnoreCase)
                        && !rn.StartsWith("Melon", StringComparison.OrdinalIgnoreCase)
                        && rn != "0Harmony") continue;
                    string toId = "EXT_" + SanId(rn);
                    sb.Append($"  {fromId} --> {toId}[\"{Safe(rn, 40)}\"]\n");
                }
            }
        }
        yield break;
    }

    private static string SanId(string s)
    {
        if (string.IsNullOrEmpty(s)) return "X";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
