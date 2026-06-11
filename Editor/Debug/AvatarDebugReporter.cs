using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public class AvatarDebugReporter : EditorWindow
{
    private Vector2 scroll;
    private string report = "";

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/Avatar Debug Reporter")]
    public static void Open()
    {
        GetWindow<AvatarDebugReporter>("Avatar Debug Reporter");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Avatar Debug Reporter", EditorStyles.boldLabel);

        if (GUILayout.Button("選択中アバターを解析"))
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                report = "GameObjectが選択されていません。";
            }
            else
            {
                report = GenerateReport(selected);
            }
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("クリップボードへコピー"))
        {
            EditorGUIUtility.systemCopyBuffer = report;
        }

        if (GUILayout.Button("Consoleへ出力"))
        {
            Debug.Log(report);
        }

        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private static string GenerateReport(GameObject selected)
    {
        var sb = new StringBuilder();

        var avatarRoot = FindAvatarRoot(selected);

        sb.AppendLine("# Avatar Debug Report");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Unity: " + Application.unityVersion);
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## Target");
        sb.AppendLine("Selected: " + GetPath(selected.transform));
        sb.AppendLine("Avatar Root: " + (avatarRoot != null ? GetPath(avatarRoot.transform) : "<not found>"));
        sb.AppendLine();

        if (avatarRoot == null)
        {
            sb.AppendLine("VRCAvatarDescriptorを持つ親が見つかりません。");
            return sb.ToString();
        }

        sb.AppendLine("============================================================");
        sb.AppendLine("## Dependency Summary");
        AppendPackageSummary(sb);
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## Component Type Status");
        AppendTypeStatus(sb, "AAO / Avatar Optimizer", new[]
        {
            "Anatawa12.AvatarOptimizer.TraceAndOptimize",
            "Anatawa12.AvatarOptimizer.MergeSkinnedMesh",
            "Anatawa12.AvatarOptimizer.RemoveMeshByBlendShape",
        });

        AppendTypeStatus(sb, "LAC / Avatar Compressor", new[]
        {
            "dev.limitex.avatar.compressor.TextureCompressor",
            "dev.limitex.avatar.compressor.CompressorPreset",
        });

        AppendTypeStatus(sb, "Modular Avatar", new[]
        {
            "nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller",
            "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator",
            "nadena.dev.modular_avatar.core.ModularAvatarParameters",
        });

        AppendTypeStatus(sb, "LightLimitChanger", new[]
        {
            "io.github.azukimochi.LightLimitChangerComponent",
        });

        AppendTypeStatus(sb, "可愛いポーズツール", new[]
        {
            "jp.unisakistudio.kawaiiposing.KawaiiPosing",
        });

        AppendTypeStatus(sb, "赤夜式 撫で音ギミック", new[]
        {
            "RedNightWorks.NadeSystem.NadeSystemSettings",
        });

        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## Installed Summary");
        AppendInstalledSummary(sb, avatarRoot);
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## KawaiiPosing Detail");
        AppendComponentPathList(sb, avatarRoot, "jp.unisakistudio.kawaiiposing.KawaiiPosing");
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## LightLimitChanger Detail");
        AppendComponentPathList(sb, avatarRoot, "io.github.azukimochi.LightLimitChangerComponent");
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## NadeSystem Detail");
        AppendComponentPathList(sb, avatarRoot, "RedNightWorks.NadeSystem.NadeSystemSettings");
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## Direct Children");
        foreach (Transform child in avatarRoot.transform)
        {
            sb.AppendLine("- " + child.name);
        }

        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## Hierarchy Component Summary");
        AppendHierarchyComponentSummary(sb, avatarRoot);

        return sb.ToString();
    }

    private static void AppendInstalledSummary(StringBuilder sb, GameObject avatarRoot)
    {
        var aaoType = FindType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
        var lacType = FindType("dev.limitex.avatar.compressor.TextureCompressor");
        var kawaiiType = FindType("jp.unisakistudio.kawaiiposing.KawaiiPosing");
        var llcType = FindType("io.github.azukimochi.LightLimitChangerComponent");
        var nadeType = FindType("RedNightWorks.NadeSystem.NadeSystemSettings");

        AppendSimpleInstalled(sb, "AAO", avatarRoot, aaoType);
        AppendLacInstalled(sb, avatarRoot, lacType);
        AppendPrefabNameInstalled(sb, "RBS", avatarRoot, new[]
        {
            "RBS_Suimin",
            "RBS_Suimin(日本語)",
            "RBS_Suimin-Menu",
            "RBS_Suimin-Menu-ja_JP"
        });

        AppendSimpleInstalled(sb, "赤夜式 撫で音", avatarRoot, nadeType);
        AppendSimpleInstalled(sb, "LightLimitChanger", avatarRoot, llcType);
        AppendKawaiiInstalled(sb, avatarRoot, kawaiiType);
    }

    private static void AppendSimpleInstalled(StringBuilder sb, string label, GameObject root, Type type)
    {
        if (type == null)
        {
            sb.AppendLine(label + ": Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(type, true);
        sb.AppendLine(label + ": " + (comps.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + comps.Length);

        foreach (var comp in comps)
        {
            var c = comp as Component;
            if (c != null)
                sb.AppendLine("  - " + GetPath(c.transform));
        }
    }

    private static void AppendLacInstalled(StringBuilder sb, GameObject root, Type lacType)
    {
        if (lacType == null)
        {
            sb.AppendLine("LAC: Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(lacType, true);
        sb.AppendLine("LAC: " + (comps.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + comps.Length);

        foreach (var comp in comps)
        {
            var c = comp as Component;
            if (c == null) continue;

            sb.AppendLine("  - " + GetPath(c.transform));

            var presetField = lacType.GetField("Preset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (presetField != null)
            {
                object value = presetField.GetValue(comp);
                sb.AppendLine("    Preset: " + (value != null ? value.ToString() : "<null>"));
            }
        }

        var enumType = FindType("dev.limitex.avatar.compressor.CompressorPreset");
        if (enumType != null && enumType.IsEnum)
        {
            sb.AppendLine("  Available Presets: " + string.Join(", ", Enum.GetNames(enumType)));
        }
    }

    private static void AppendKawaiiInstalled(StringBuilder sb, GameObject root, Type kawaiiType)
    {
        if (kawaiiType == null)
        {
            sb.AppendLine("可愛いポーズツール: Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(kawaiiType, true);
        sb.AppendLine("可愛いポーズツール: " + (comps.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + comps.Length);

        bool normal = false;
        bool eightBitNoFoot = false;

        foreach (var comp in comps)
        {
            var c = comp as Component;
            if (c == null) continue;

            string path = GetPath(c.transform);
            string name = c.gameObject.name;

            if (name == "可愛いポーズ") normal = true;
            if (name == "可愛いポーズ(8bit・足の高さなし)") eightBitNoFoot = true;

            sb.AppendLine("  - " + path);
        }

        sb.AppendLine("  Variant 判定:");
        sb.AppendLine("    可愛いポーズ: " + (normal ? "Found" : "Not Found"));
        sb.AppendLine("    可愛いポーズ(8bit・足の高さなし): " + (eightBitNoFoot ? "Found" : "Not Found"));
    }

    private static void AppendPrefabNameInstalled(StringBuilder sb, string label, GameObject root, string[] names)
    {
        var hits = new List<Transform>();

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (var n in names)
            {
                if (t.name == n || t.name.Contains(n))
                {
                    hits.Add(t);
                    break;
                }
            }
        }

        sb.AppendLine(label + ": " + (hits.Count > 0 ? "Installed" : "Not Installed") + " / Count: " + hits.Count);

        foreach (var h in hits.Distinct())
        {
            sb.AppendLine("  - " + GetPath(h));
        }
    }

    private static void AppendComponentPathList(StringBuilder sb, GameObject root, string fullTypeName)
    {
        var type = FindType(fullTypeName);

        if (type == null)
        {
            sb.AppendLine(fullTypeName + ": Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(type, true);
        sb.AppendLine(fullTypeName + ": " + comps.Length);

        foreach (var comp in comps)
        {
            var c = comp as Component;
            if (c != null)
                sb.AppendLine("- " + GetPath(c.transform));
        }
    }

    private static void AppendTypeStatus(StringBuilder sb, string label, string[] typeNames)
    {
        sb.AppendLine("-- " + label + " --");

        bool any = false;

        foreach (var typeName in typeNames)
        {
            var type = FindType(typeName);
            if (type == null)
            {
                sb.AppendLine("[NG] " + typeName);
                continue;
            }

            any = true;
            sb.AppendLine("[OK] " + type.FullName);
            sb.AppendLine("     Assembly: " + type.Assembly.GetName().Name);
        }

        sb.AppendLine("Status: " + (any ? "Detected" : "Not Detected"));
        sb.AppendLine();
    }

    private static void AppendPackageSummary(StringBuilder sb)
    {
        string[] packageNames =
        {
            "com.anatawa12.avatar-optimizer",
            "dev.limitex.avatar-compressor",
            "nadena.dev.modular-avatar",
            "nadena.dev.ndmf",
            "io.github.azukimochi.light-limit-changer",
            "jp.unisakistudio.kawaiiposing",
            "jp.unisakistudio.posingsystem",
            "com.vrchat.avatars",
            "com.vrchat.base"
        };

        foreach (var packageName in packageNames)
        {
            string version = TryReadPackageVersion(packageName);
            sb.AppendLine(packageName + ": " + (string.IsNullOrEmpty(version) ? "<not found>" : version));
        }
    }

    private static string TryReadPackageVersion(string packageName)
    {
        string packageJsonPath = "Packages/" + packageName + "/package.json";
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>(packageJsonPath);

        if (json == null)
            return null;

        string text = json.text;
        string marker = "\"version\"";
        int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return "<version unknown>";

        int colon = text.IndexOf(':', index);
        if (colon < 0) return "<version unknown>";

        int firstQuote = text.IndexOf('"', colon + 1);
        int secondQuote = text.IndexOf('"', firstQuote + 1);

        if (firstQuote < 0 || secondQuote < 0)
            return "<version unknown>";

        return text.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    private static void AppendHierarchyComponentSummary(StringBuilder sb, GameObject root)
    {
        var dict = new SortedDictionary<string, int>();

        foreach (var comp in root.GetComponentsInChildren<Component>(true))
        {
            if (comp == null) continue;

            string name = comp.GetType().FullName;
            if (!dict.ContainsKey(name)) dict[name] = 0;
            dict[name]++;
        }

        sb.AppendLine("GameObject Count: " + root.GetComponentsInChildren<Transform>(true).Length);
        sb.AppendLine("Component Type Count: " + dict.Count);

        foreach (var kv in dict)
        {
            sb.AppendLine(kv.Key + ": " + kv.Value);
        }
    }

    private static GameObject FindAvatarRoot(GameObject selected)
    {
        var t = selected.transform;

        while (t != null)
        {
            if (t.GetComponent("VRCAvatarDescriptor") != null)
                return t.gameObject;

            t = t.parent;
        }

        return selected;
    }

    private static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = null;

            try
            {
                type = asm.GetType(fullName);
            }
            catch
            {
                // ignored
            }

            if (type != null)
                return type;
        }

        return null;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "<null>";

        var names = new Stack<string>();
        while (t != null)
        {
            names.Push(t.name);
            t = t.parent;
        }

        return string.Join("/", names);
    }
}