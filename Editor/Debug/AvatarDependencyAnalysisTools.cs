using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    /// <summary>
    /// VRC Avatar Toolkit Plus 用の依存関係解析ツール。
    /// 既存の AvatarComponentDebugTools とは分離し、同じ AvatarDebugReportWindow に結果を表示します。
    ///
    /// 目的:
    /// - AAO / LAC のようなコンポーネント系依存の型・Assembly・Preset確認
    /// - RBS / 赤夜式撫で音 / LightLimitChanger / 可愛いポーズ のようなPrefab系依存の候補検出
    /// - 選択中アバターに既に導入済みらしい痕跡があるか確認
    /// - VPM / UnityPackage 導入差を吸収するための調査材料を出す
    /// </summary>
    public static class AvatarDependencyAnalysisTools
    {
        private const string WindowTitle = "VRC Avatar Toolkit Plus - 依存関係解析";

        private const string AvatarDescriptorFullName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";

        private static readonly DependencyTypeDefinition[] ComponentDependencies =
        {
            new DependencyTypeDefinition(
                "AAO / Avatar Optimizer",
                new []
                {
                    "Anatawa12.AvatarOptimizer.TraceAndOptimize",
                    "Anatawa12.AvatarOptimizer.MergeSkinnedMesh",
                    "Anatawa12.AvatarOptimizer.RemoveMeshByBlendShape"
                },
                new []
                {
                    "com.anatawa12.avatar-optimizer",
                    "AvatarOptimizer",
                    "anatawa12"
                }
            ),

            new DependencyTypeDefinition(
                "LAC / lilAvatarUtils Avatar Compressor",
                new []
                {
                    "dev.limitex.avatar.compressor.TextureCompressor",
                    "dev.limitex.avatar.compressor.CompressorPreset"
                },
                new []
                {
                    "dev.limitex.avatar-compressor",
                    "avatar-compressor",
                    "TextureCompressor",
                    "limitex"
                }
            ),

            new DependencyTypeDefinition(
                "Modular Avatar",
                new []
                {
                    "nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller",
                    "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator",
                    "nadena.dev.modular_avatar.core.ModularAvatarParameters"
                },
                new []
                {
                    "nadena.dev.modular-avatar",
                    "modular-avatar",
                    "ModularAvatar"
                }
            ),

            new DependencyTypeDefinition(
                "VRC SDK Avatars",
                new []
                {
                    "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor",
                    "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters"
                },
                new []
                {
                    "com.vrchat.avatars",
                    "VRCSDK",
                    "VRChat"
                }
            )
        };

        private static readonly PrefabDependencyDefinition[] PrefabDependencies =
        {
            new PrefabDependencyDefinition(
                "RBS 睡眠システム Ver2",
                new [] { "RBS", "Sleep", "睡眠", "SleepSystem" },
                new [] { "ModularAvatar", "MA", "Menu", "Parameter", "睡眠", "Sleep" }
            ),
            new PrefabDependencyDefinition(
                "赤夜式 撫で音ギミック",
                new [] { "撫で", "なで", "Nade", "Nadenade", "Akaya", "赤夜" },
                new [] { "撫で", "なで", "Nade", "Audio", "Sound", "Contact", "Receiver" }
            ),
            new PrefabDependencyDefinition(
                "LightLimitChanger",
                new [] { "LightLimit", "Light Limit", "LightLimitChanger", "LLC" },
                new [] { "LightLimit", "Light Limit", "Changer", "MA", "Menu", "Parameter" }
            ),
            new PrefabDependencyDefinition(
                "可愛いポーズツール",
                new [] { "可愛い", "かわいい", "Kawaii", "Pose", "ポーズ", "8bit", "足の高さ" },
                new [] { "可愛い", "かわいい", "Kawaii", "Pose", "Sit", "MA", "Menu", "Parameter" }
            )
        };

        [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/選択中アバターを解析")]
        public static void AnalyzeSelectedAvatarDependencies()
        {
            var report = new ReportBuilder("Selected Avatar Dependency Analysis");

            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                report.Warning("GameObjectを選択してください。アバター直下でも、アバター配下の子オブジェクトでも構いません。");
                ShowReport(report);
                return;
            }

            Component descriptor = FindAvatarDescriptor(selected);
            GameObject avatarRoot = descriptor != null ? descriptor.gameObject : selected;

            report.Section("解析対象");
            report.Line($"Selected: {GetPath(selected.transform)}");
            report.Line($"Avatar Root Guess: {GetPath(avatarRoot.transform)}");
            report.Line($"Avatar Descriptor: {(descriptor != null ? "Found" : "Not Found / selected object used as root")}");
            report.Blank();

            AppendProjectPackageHints(report);
            AppendComponentDependencyStatus(report);
            AppendLacPresetStatus(report);
            AppendInstalledComponentStatus(report, avatarRoot);
            AppendInstalledPrefabLikeStatus(report, avatarRoot);
            AppendPrefabCandidateStatus(report, quick: true);

            ShowReport(report);
        }

        [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/Project全体を解析")]
        public static void AnalyzeProjectDependencies()
        {
            var report = new ReportBuilder("Project Dependency Analysis");

            AppendProjectPackageHints(report);
            AppendComponentDependencyStatus(report);
            AppendLacPresetStatus(report);
            AppendPrefabCandidateStatus(report, quick: false);
            AppendAssemblySummary(report);

            ShowReport(report);
        }

        [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/選択中Prefabのシグネチャを表示")]
        public static void AnalyzeSelectedPrefabSignature()
        {
            var report = new ReportBuilder("Selected Prefab Signature");

            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                report.Warning("PrefabまたはPrefabインスタンスを選択してください。");
                ShowReport(report);
                return;
            }

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath = AssetDatabase.GetAssetPath(selected);
            }

            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                report.Warning("選択対象からPrefab Asset Pathを取得できませんでした。Project上のPrefab、またはPrefabインスタンスを選択してください。");
                ShowReport(report);
                return;
            }

            AppendSinglePrefabSignature(report, assetPath, fullDump: true);
            ShowReport(report);
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/このアバターを解析", false, 30)]
        public static void ContextAnalyzeSelectedAvatarDependencies()
        {
            AnalyzeSelectedAvatarDependencies();
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/このPrefabのシグネチャを表示", false, 31)]
        public static void ContextAnalyzeSelectedPrefabSignature()
        {
            AnalyzeSelectedPrefabSignature();
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/このアバターを解析", true)]
        [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/依存関係解析/このPrefabのシグネチャを表示", true)]
        private static bool ValidateContextNeedsGameObject()
        {
            return Selection.activeGameObject != null;
        }

        private static void AppendProjectPackageHints(ReportBuilder report)
        {
            report.Section("VPM / Package Hints");

            AppendTextFileMatches(report, "Packages/manifest.json");
            AppendTextFileMatches(report, "Packages/packages-lock.json");

            string[] packageJsonGuids = AssetDatabase.FindAssets("package t:TextAsset", new[] { "Assets", "Packages" });
            int matchedPackageJsonCount = 0;
            foreach (string guid in packageJsonGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)) continue;

                string text = TryReadAllText(path);
                if (string.IsNullOrEmpty(text)) continue;

                if (ContainsAny(text, CollectAllPackageKeywords()))
                {
                    matchedPackageJsonCount++;
                    report.Line($"package.json: {path}");
                    AppendLikelyPackageNameAndVersion(report, text);
                }
            }

            if (matchedPackageJsonCount == 0)
            {
                report.Line("Assets/Packages配下の関連 package.json は未検出、またはキーワードに一致しませんでした。");
            }

            report.Blank();
        }

        private static void AppendTextFileMatches(ReportBuilder report, string path)
        {
            if (!File.Exists(path))
            {
                report.Line($"{path}: not found");
                return;
            }

            string text = TryReadAllText(path);
            report.Line($"{path}: found");

            foreach (string keyword in CollectAllPackageKeywords())
            {
                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    report.Line($"  Match: {keyword}");
                    AppendJsonLineContaining(report, text, keyword, "    ");
                }
            }
        }

        private static void AppendComponentDependencyStatus(ReportBuilder report)
        {
            report.Section("Component Type Dependency Status");

            foreach (var dependency in ComponentDependencies)
            {
                report.Subsection(dependency.DisplayName);

                bool anyFound = false;
                foreach (string fullName in dependency.TypeFullNames)
                {
                    Type type = FindType(fullName);
                    if (type == null)
                    {
                        report.Line($"[NG] Type Not Found: {fullName}");
                    }
                    else
                    {
                        anyFound = true;
                        AssemblyName asm = type.Assembly.GetName();
                        report.Line($"[OK] {fullName}");
                        report.Line($"     Assembly: {asm.Name} / Version: {asm.Version}");
                        report.Line($"     Location: {SafeAssemblyLocation(type.Assembly)}");
                    }
                }

                report.Line($"Status: {(anyFound ? "Detected" : "Not Detected")}");
                report.Blank();
            }
        }

        private static void AppendLacPresetStatus(ReportBuilder report)
        {
            report.Section("LAC Preset Enum Status");

            Type enumType = FindType("dev.limitex.avatar.compressor.CompressorPreset");
            if (enumType == null)
            {
                report.Warning("LACのCompressorPreset型が見つかりません。LAC未導入、または型名/Assembly名が変わっている可能性があります。");
                report.Blank();
                return;
            }

            report.Line($"Enum Type: {enumType.FullName}");
            report.Line($"Assembly: {enumType.Assembly.GetName().Name}");

            if (!enumType.IsEnum)
            {
                report.Warning("検出したCompressorPresetはEnumではありません。LAC側の構造変更の可能性があります。");
                report.Blank();
                return;
            }

            string[] names = Enum.GetNames(enumType);
            report.Line("Enum Values: " + string.Join(", ", names));

            string[] expected = { "HighQuality", "Quality", "Balanced", "Aggressive", "Maximum" };
            foreach (string preset in expected)
            {
                report.Line($"Preset {preset}: {(Array.IndexOf(names, preset) >= 0 ? "OK" : "Missing")}");
            }

            report.Blank();
        }

        private static void AppendInstalledComponentStatus(ReportBuilder report, GameObject avatarRoot)
        {
            report.Section("Selected Avatar Component Install Status");

            var allComponents = avatarRoot.GetComponentsInChildren<Component>(true);

            foreach (var dependency in ComponentDependencies)
            {
                report.Subsection(dependency.DisplayName);
                int count = 0;

                foreach (var component in allComponents)
                {
                    if (component == null) continue;
                    string fullName = component.GetType().FullName;
                    if (!ContainsString(dependency.TypeFullNames, fullName)) continue;

                    count++;
                    report.Line($"Found: {fullName} / GameObject: {GetPath(component.transform)}");
                }

                if (count == 0)
                {
                    report.Line("Not found in selected avatar hierarchy.");
                }
                else
                {
                    report.Line($"Count: {count}");
                }

                report.Blank();
            }
        }

        private static void AppendInstalledPrefabLikeStatus(ReportBuilder report, GameObject avatarRoot)
        {
            report.Section("Selected Avatar Prefab-like Install Hints");

            Transform[] transforms = avatarRoot.GetComponentsInChildren<Transform>(true);
            Component[] components = avatarRoot.GetComponentsInChildren<Component>(true);

            foreach (var dependency in PrefabDependencies)
            {
                report.Subsection(dependency.DisplayName);

                int nameHits = 0;
                foreach (var t in transforms)
                {
                    if (ContainsAny(t.name, dependency.InstallSignatureKeywords))
                    {
                        nameHits++;
                        report.Line($"Name Hit: {GetPath(t)}");
                    }
                }

                int componentHits = 0;
                foreach (var c in components)
                {
                    if (c == null) continue;
                    string fullName = c.GetType().FullName ?? c.GetType().Name;
                    if (ContainsAny(fullName, dependency.InstallSignatureKeywords))
                    {
                        componentHits++;
                        report.Line($"Component Hit: {fullName} / {GetPath(c.transform)}");
                    }
                }

                report.Line($"Name Hits: {nameHits}, Component Hits: {componentHits}");

                if (nameHits == 0 && componentHits == 0)
                {
                    report.Line("導入済みらしい痕跡は未検出です。ただし名前変更済みPrefabは検出できない場合があります。");
                }

                report.Blank();
            }
        }

        private static void AppendPrefabCandidateStatus(ReportBuilder report, bool quick)
        {
            report.Section("Project Prefab Candidates");

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets", "Packages" });
            report.Line($"Prefab Count: {prefabGuids.Length}");

            int maxSignatureDump = quick ? 3 : 12;

            foreach (var dependency in PrefabDependencies)
            {
                report.Subsection(dependency.DisplayName);

                var candidates = new List<string>();
                foreach (string guid in prefabGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileNameWithoutExtension(path);

                    if (ContainsAny(path, dependency.ProjectSearchKeywords) || ContainsAny(name, dependency.ProjectSearchKeywords))
                    {
                        candidates.Add(path);
                    }
                }

                if (candidates.Count == 0)
                {
                    report.Line("Project上の候補Prefabは未検出です。");
                    report.Blank();
                    continue;
                }

                report.Line($"Candidate Count: {candidates.Count}");
                for (int i = 0; i < candidates.Count; i++)
                {
                    string path = candidates[i];
                    report.Line($"[{i}] {path}");

                    if (i < maxSignatureDump)
                    {
                        AppendSinglePrefabSignature(report, path, fullDump: false);
                    }
                }

                if (candidates.Count > maxSignatureDump)
                {
                    report.Line($"... Signature dump omitted: {candidates.Count - maxSignatureDump} prefab(s)");
                }

                report.Blank();
            }
        }

        private static void AppendSinglePrefabSignature(ReportBuilder report, string assetPath, bool fullDump)
        {
            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                if (prefabRoot == null)
                {
                    report.Warning($"Prefabを開けませんでした: {assetPath}");
                    return;
                }

                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                report.Line($"  Prefab: {prefabRoot.name}");
                report.Line($"  Path: {assetPath}");
                report.Line($"  GUID: {guid}");

                var componentCounts = new SortedDictionary<string, int>();
                var missingPaths = new List<string>();
                foreach (var t in prefabRoot.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null)
                        {
                            missingPaths.Add(GetPath(t));
                            continue;
                        }

                        string key = c.GetType().FullName;
                        componentCounts[key] = componentCounts.ContainsKey(key) ? componentCounts[key] + 1 : 1;
                    }
                }

                report.Line($"  Component Type Count: {componentCounts.Count}");
                int limit = fullDump ? int.MaxValue : 40;
                int n = 0;
                foreach (var pair in componentCounts)
                {
                    if (n >= limit)
                    {
                        report.Line("    ... component list omitted");
                        break;
                    }
                    report.Line($"    {pair.Key}: {pair.Value}");
                    n++;
                }

                if (missingPaths.Count > 0)
                {
                    report.Line($"  Missing Components: {missingPaths.Count}");
                    foreach (string p in missingPaths)
                    {
                        report.Line($"    Missing at: {p}");
                    }
                }

                report.Line("  Child Objects:");
                int childLimit = fullDump ? int.MaxValue : 60;
                int childCount = 0;
                foreach (var t in prefabRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (childCount >= childLimit)
                    {
                        report.Line("    ... child list omitted");
                        break;
                    }
                    report.Line($"    {GetPath(t)}");
                    childCount++;
                }
            }
            catch (Exception ex)
            {
                report.Warning($"Prefab解析中に例外: {assetPath} / {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (prefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private static void AppendAssemblySummary(ReportBuilder report)
        {
            report.Section("Related Assembly Summary");

            var keywords = CollectAllPackageKeywords();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AssemblyName name;
                try { name = assembly.GetName(); }
                catch { continue; }

                string location = SafeAssemblyLocation(assembly);
                string combined = name.Name + " " + location;
                if (!ContainsAny(combined, keywords)) continue;

                report.Line($"Assembly: {name.Name} / Version: {name.Version}");
                report.Line($"Location: {location}");
                report.Blank();
            }
        }

        private static Component FindAvatarDescriptor(GameObject go)
        {
            Transform current = go.transform;
            while (current != null)
            {
                Component descriptor = GetComponentByFullName(current.gameObject, AvatarDescriptorFullName);
                if (descriptor != null) return descriptor;
                current = current.parent;
            }

            foreach (Component component in go.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().FullName == AvatarDescriptorFullName)
                    return component;
            }

            return null;
        }

        private static Component GetComponentByFullName(GameObject go, string fullName)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().FullName == fullName) return c;
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            Type direct = Type.GetType(fullName);
            if (direct != null) return direct;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try { type = assembly.GetType(fullName, throwOnError: false); }
                catch { }
                if (type != null) return type;
            }

            return null;
        }

        private static string[] CollectAllPackageKeywords()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in ComponentDependencies)
            {
                foreach (string k in dependency.PackageKeywords) set.Add(k);
                foreach (string t in dependency.TypeFullNames) set.Add(t);
            }
            foreach (var dependency in PrefabDependencies)
            {
                foreach (string k in dependency.ProjectSearchKeywords) set.Add(k);
            }
            return new List<string>(set).ToArray();
        }

        private static bool ContainsString(string[] values, string target)
        {
            if (values == null || target == null) return false;
            foreach (string value in values)
            {
                if (string.Equals(value, target, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            if (string.IsNullOrEmpty(text) || keywords == null) return false;
            foreach (string keyword in keywords)
            {
                if (string.IsNullOrEmpty(keyword)) continue;
                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string TryReadAllText(string unityPathOrRelativePath)
        {
            try
            {
                string path = unityPathOrRelativePath;
                if (path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
                }
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendLikelyPackageNameAndVersion(ReportBuilder report, string json)
        {
            string name = ExtractJsonStringValue(json, "name");
            string version = ExtractJsonStringValue(json, "version");
            if (!string.IsNullOrEmpty(name)) report.Line($"  name: {name}");
            if (!string.IsNullOrEmpty(version)) report.Line($"  version: {version}");
        }

        private static void AppendJsonLineContaining(ReportBuilder report, string text, string keyword, string indent)
        {
            using (var reader = new StringReader(text))
            {
                string line;
                int shown = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    report.Line(indent + line.Trim());
                    shown++;
                    if (shown >= 3) break;
                }
            }
        }

        private static string ExtractJsonStringValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return string.Empty;

            string marker = "\"" + key + "\"";
            int keyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) return string.Empty;

            int colon = json.IndexOf(':', keyIndex + marker.Length);
            if (colon < 0) return string.Empty;

            int firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0) return string.Empty;

            int secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) return string.Empty;

            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }

        private static string SafeAssemblyLocation(Assembly assembly)
        {
            try
            {
                return string.IsNullOrEmpty(assembly.Location) ? "<dynamic or unknown>" : assembly.Location;
            }
            catch
            {
                return "<unreadable>";
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack.ToArray());
        }

        private static void ShowReport(ReportBuilder report)
        {
            string text = report.ToString();
            Debug.Log(text);
            AvatarDebugReportWindow.Open(text, WindowTitle);
        }

        private sealed class DependencyTypeDefinition
        {
            public readonly string DisplayName;
            public readonly string[] TypeFullNames;
            public readonly string[] PackageKeywords;

            public DependencyTypeDefinition(string displayName, string[] typeFullNames, string[] packageKeywords)
            {
                DisplayName = displayName;
                TypeFullNames = typeFullNames;
                PackageKeywords = packageKeywords;
            }
        }

        private sealed class PrefabDependencyDefinition
        {
            public readonly string DisplayName;
            public readonly string[] ProjectSearchKeywords;
            public readonly string[] InstallSignatureKeywords;

            public PrefabDependencyDefinition(string displayName, string[] projectSearchKeywords, string[] installSignatureKeywords)
            {
                DisplayName = displayName;
                ProjectSearchKeywords = projectSearchKeywords;
                InstallSignatureKeywords = installSignatureKeywords;
            }
        }

        private sealed class ReportBuilder
        {
            private readonly StringBuilder sb = new StringBuilder();

            public ReportBuilder(string title)
            {
                Line("# " + title);
                Line("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Line("Unity: " + Application.unityVersion);
                Line("Project: " + Application.dataPath);
                Blank();
            }

            public void Section(string text)
            {
                Separator();
                Line("## " + text);
            }

            public void Subsection(string text)
            {
                Line("-- " + text + " --");
            }

            public void Line(string text)
            {
                sb.AppendLine(text ?? string.Empty);
            }

            public void Warning(string text)
            {
                sb.AppendLine("[Warning] " + text);
                Debug.LogWarning(text);
            }

            public void Blank()
            {
                sb.AppendLine();
            }

            public void Separator()
            {
                sb.AppendLine("============================================================");
            }

            public override string ToString()
            {
                return sb.ToString();
            }
        }
    }
}
