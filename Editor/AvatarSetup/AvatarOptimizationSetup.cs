using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
public static class AvatarOptimizationSetup
{
    private const string AAOTypeName =
        "Anatawa12.AvatarOptimizer.TraceAndOptimize, com.anatawa12.avatar-optimizer.runtime";

    private const string LACTypeName =
        "dev.limitex.avatar.compressor.TextureCompressor, dev.limitex.avatar-compressor";

    private const string LACEnumTypeName =
        "dev.limitex.avatar.compressor.CompressorPreset, dev.limitex.avatar-compressor";

    private const string LACPresetFieldName = "Preset";

    // RBS-睡眠システム Ver2 / 日本語版のみ
    private const string RBSSuiminJapanesePrefabGuid = "99ff43deda213c145878efbecf96abf1";

    // RBSは手動導入・旧ツール導入・Prefab Unpack済みの場合、
    // PrefabUtility.GetCorrespondingObjectFromSource だけでは検出できない。
    // そのため、既存導入検出ではPrefab一致に加えて、名前ベースの保険も使う。
    private static readonly string[] RBSSuiminNameKeywords =
    {
        "rbs",
        "suimin",
        "sleep",
        "sleeping",
        "睡眠"
    };

    // 【赤夜式】撫で音ギミック / NadeSystemInstaller.cs 由来
    private const string NadeSystemGUID = "491c3f399da5d064d9966982ddf0d191";
    private const string FootSystemGUID = "ca3cfa8587af6cc4f8f7205a0c16e108";
    private const string FootSystemMenuGUID = "f7ce8e50badf67b418b0c9d5b7e73442";
    private const string NadeShadowGUID = "fd1d0e8cc6fc6f646ad9f24b156a31ac";
    private const string DummyLightGUID = "c46c6e537bbb1a140957ad83f15c5afb";
    private const string NadeShadowMenuGUID = "7f6a3a1aa3e98df4e88571c89d365603";
    private const string NadeSphereGUID = "e6971546677df8d449b746136433e2cc";
    private const string NadeSphereMenuGUID = "4912f408e5d621d43a4d48dd368e3c3a";

    private const float DefaultContactRadius = 0.14f;
    private const float MinContactRadius = 0.01f;
    private const float MaxContactRadius = 1.0f;
    private const float DefaultHeadOffsetY = 0.035f;

    public enum LacPresetMode
    {
        HighQuality,
        Quality,
        Balanced,
        Aggressive,
        Maximum
    }

    public enum TargetMode
    {
        SelectedFolder,
        SelectedProjectPrefabs,
        SelectedHierarchyObjects
    }

    public class NadeOptions
    {
        public float ContactRadius = DefaultContactRadius;
        public float HeadOffsetY = DefaultHeadOffsetY;
        public bool InstallNadeShaderForHands = true;
        public bool InstallNadeShaderForFeet = true;
        public bool InstallNadeShaderForHead = false;
        public bool InstallNadeSphere = false;
        public bool InstallFootSystem = false;

        public NadeOptions Clone()
        {
            return (NadeOptions)MemberwiseClone();
        }
    }

    public class RunOptions
    {
        public bool AddAAO;
        public bool AddLAC;
        public bool AddRBS;
        public bool AddNade;
        public LacPresetMode LacPresetMode = LacPresetMode.Quality;
        public bool DryRun;
        public bool ShowConfirm = true;
        public NadeOptions Nade = new NadeOptions();

        public string DisplayName
        {
            get
            {
                List<string> parts = new List<string>();
                if (AddAAO) parts.Add("AAO");
                if (AddLAC) parts.Add("LAC " + LacPresetMode);
                if (AddRBS) parts.Add("RBS睡眠V2");
                if (AddNade) parts.Add("撫で音");
                if (parts.Count == 0) return "何もしない";
                return string.Join(" + ", parts.ToArray());
            }
        }
    }

    public class BatchResult
    {
        public bool DryRun;
        public string TargetLabel;
        public int TargetCount;
        public int AddedAAO;
        public int AddedLAC;
        public int ChangedLACPreset;
        public int AddedRBS;
        public int AddedNade;
        public int ChangedTargets;
        public int Skipped;
        public int Failed;
        public List<string> Details = new List<string>();
        public List<string> Errors = new List<string>();

        public string ToSummary()
        {
            return
                (DryRun ? "Dry Run 完了" : "完了") + "\n" +
                "対象: " + TargetLabel + "\n" +
                "対象数: " + TargetCount + "\n" +
                "AAO追加: " + AddedAAO + "\n" +
                "LAC追加: " + AddedLAC + "\n" +
                "LAC Preset変更: " + ChangedLACPreset + "\n" +
                "RBS睡眠V2追加: " + AddedRBS + "\n" +
                "撫で音追加/再構築: " + AddedNade + "\n" +
                "変更対象数: " + ChangedTargets + "\n" +
                "スキップ: " + Skipped + "\n" +
                "失敗: " + Failed;
        }

        public string ToFullText()
        {
            string text = ToSummary();
            if (Details.Count > 0)
            {
                text += "\n\n--- Details ---\n" + string.Join("\n", Details.ToArray());
            }
            if (Errors.Count > 0)
            {
                text += "\n\n--- Errors ---\n" + string.Join("\n", Errors.ToArray());
            }
            return text;
        }
    }

    // ------------------------------------------------------------
    // Tools menu: 選択フォルダ内のPrefabを処理
    // ------------------------------------------------------------

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch Window", false, 100)]
    public static void OpenWindow()
    {
        AvatarOptimizationSetupWindow.ShowWindow();
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Add AAO Only")]
    public static void Tools_AddAAOOnly()
    {
        RunForBestCurrentSelection(new RunOptions { AddAAO = true });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Add LAC Only (HighQuality)")]
    public static void Tools_AddLACOnly_HighQuality()
    {
        RunForBestCurrentSelection(new RunOptions { AddLAC = true, LacPresetMode = LacPresetMode.HighQuality });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Add LAC Only (Quality)")]
    public static void Tools_AddLACOnly_Quality()
    {
        RunForBestCurrentSelection(new RunOptions { AddLAC = true, LacPresetMode = LacPresetMode.Quality });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Add RBS Suimin V2 Only")]
    public static void Tools_AddRBSOnly()
    {
        RunForBestCurrentSelection(new RunOptions { AddRBS = true });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Add Nade System Only")]
    public static void Tools_AddNadeOnly()
    {
        RunForBestCurrentSelection(new RunOptions { AddNade = true });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Setup AAO + LAC (HighQuality)")]
    public static void Tools_SetupAAOAndLAC_HighQuality()
    {
        RunForBestCurrentSelection(new RunOptions { AddAAO = true, AddLAC = true, LacPresetMode = LacPresetMode.HighQuality });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Setup AAO + LAC (Quality)")]
    public static void Tools_SetupAAOAndLAC_Quality()
    {
        RunForBestCurrentSelection(new RunOptions { AddAAO = true, AddLAC = true, LacPresetMode = LacPresetMode.Quality });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Setup AAO + LAC + RBS + Nade (HighQuality)")]
    public static void Tools_SetupAll_HighQuality()
    {
        RunForBestCurrentSelection(new RunOptions { AddAAO = true, AddLAC = true, AddRBS = true, AddNade = true, LacPresetMode = LacPresetMode.HighQuality });
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Legacy Batch/Setup AAO + LAC + RBS + Nade (Quality)")]
    public static void Tools_SetupAll_Quality()
    {
        RunForBestCurrentSelection(new RunOptions { AddAAO = true, AddLAC = true, AddRBS = true, AddNade = true, LacPresetMode = LacPresetMode.Quality });
    }

    // ------------------------------------------------------------
    // Assets context menu: 選択Prefabを処理
    // ------------------------------------------------------------

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add AAO Only", false, 2000)]
    public static void Context_AddAAOOnly() { RunForProjectSelection(new RunOptions { AddAAO = true }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (HighQuality)", false, 2001)]
    public static void Context_AddLACOnly_HighQuality() { RunForProjectSelection(new RunOptions { AddLAC = true, LacPresetMode = LacPresetMode.HighQuality }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (Quality)", false, 2002)]
    public static void Context_AddLACOnly_Quality() { RunForProjectSelection(new RunOptions { AddLAC = true, LacPresetMode = LacPresetMode.Quality }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add RBS Suimin V2 Only", false, 2003)]
    public static void Context_AddRBSOnly() { RunForProjectSelection(new RunOptions { AddRBS = true }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add Nade System Only", false, 2004)]
    public static void Context_AddNadeOnly() { RunForProjectSelection(new RunOptions { AddNade = true }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (HighQuality)", false, 2010)]
    public static void Context_SetupAAOAndLAC_HighQuality() { RunForProjectSelection(new RunOptions { AddAAO = true, AddLAC = true, LacPresetMode = LacPresetMode.HighQuality }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (Quality)", false, 2011)]
    public static void Context_SetupAAOAndLAC_Quality() { RunForProjectSelection(new RunOptions { AddAAO = true, AddLAC = true, LacPresetMode = LacPresetMode.Quality }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (HighQuality)", false, 2020)]
    public static void Context_SetupAll_HighQuality() { RunForProjectSelection(new RunOptions { AddAAO = true, AddLAC = true, AddRBS = true, AddNade = true, LacPresetMode = LacPresetMode.HighQuality }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (Quality)", false, 2021)]
    public static void Context_SetupAll_Quality() { RunForProjectSelection(new RunOptions { AddAAO = true, AddLAC = true, AddRBS = true, AddNade = true, LacPresetMode = LacPresetMode.Quality }); }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add AAO Only", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (HighQuality)", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (Quality)", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add RBS Suimin V2 Only", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Add Nade System Only", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (HighQuality)", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (Quality)", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (HighQuality)", true)]
    [MenuItem("Assets/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (Quality)", true)]
    public static bool ValidateProjectSelectionMenus()
    {
        return GetSelectedFolderPath() != null || GetSelectedPrefabAssetPaths().Length > 0;
    }

    // ------------------------------------------------------------
    // GameObject context menu: Hierarchy上のPrefab/Avatarを処理
    // ------------------------------------------------------------

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add AAO Only", false, 30)]
    public static void GameObject_AddAAOOnly() { RunForHierarchySelection(new RunOptions { AddAAO = true }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (HighQuality)", false, 31)]
    public static void GameObject_AddLACOnly_HighQuality() { RunForHierarchySelection(new RunOptions { AddLAC = true, LacPresetMode = LacPresetMode.HighQuality }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (Quality)", false, 32)]
    public static void GameObject_AddLACOnly_Quality() { RunForHierarchySelection(new RunOptions { AddLAC = true, LacPresetMode = LacPresetMode.Quality }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add RBS Suimin V2 Only", false, 33)]
    public static void GameObject_AddRBSOnly() { RunForHierarchySelection(new RunOptions { AddRBS = true }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add Nade System Only", false, 34)]
    public static void GameObject_AddNadeOnly() { RunForHierarchySelection(new RunOptions { AddNade = true }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (HighQuality)", false, 40)]
    public static void GameObject_SetupAAOAndLAC_HighQuality() { RunForHierarchySelection(new RunOptions { AddAAO = true, AddLAC = true, LacPresetMode = LacPresetMode.HighQuality }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (Quality)", false, 41)]
    public static void GameObject_SetupAAOAndLAC_Quality() { RunForHierarchySelection(new RunOptions { AddAAO = true, AddLAC = true, LacPresetMode = LacPresetMode.Quality }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (HighQuality)", false, 50)]
    public static void GameObject_SetupAll_HighQuality() { RunForHierarchySelection(new RunOptions { AddAAO = true, AddLAC = true, AddRBS = true, AddNade = true, LacPresetMode = LacPresetMode.HighQuality }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (Quality)", false, 51)]
    public static void GameObject_SetupAll_Quality() { RunForHierarchySelection(new RunOptions { AddAAO = true, AddLAC = true, AddRBS = true, AddNade = true, LacPresetMode = LacPresetMode.Quality }); }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add AAO Only", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (HighQuality)", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only (Quality)", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add RBS Suimin V2 Only", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Add Nade System Only", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (HighQuality)", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC (Quality)", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (HighQuality)", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Avatar Setup/Setup AAO + LAC + RBS + Nade (Quality)", true)]
    public static bool ValidateHierarchySelectionMenus()
    {
        return GetSelectedHierarchyRoots().Length > 0;
    }

    // ------------------------------------------------------------
    // Public runners for window
    // ------------------------------------------------------------

    public static BatchResult RunForFolder(string folderPath, RunOptions options)
    {
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError("有効なフォルダを指定してください。");
            return null;
        }

        string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct()
            .ToArray();

        if (!ConfirmRunIfNeeded(options, "フォルダ", folderPath, prefabPaths.Length, null)) return null;
        return RunForPrefabAssetPaths(prefabPaths, options, folderPath);
    }

    public static BatchResult RunForPrefabAssets(GameObject[] prefabAssets, RunOptions options)
    {
        string[] paths = prefabAssets == null ? new string[0] : prefabAssets
            .Where(go => go != null)
            .Select(AssetDatabase.GetAssetPath)
            .Where(IsPrefabAssetPath)
            .Distinct()
            .ToArray();

        if (paths.Length == 0)
        {
            Debug.LogError("Projectウィンドウ上のPrefab Assetを指定してください。");
            return null;
        }

        if (!ConfirmRunIfNeeded(options, "Project内Prefab", null, paths.Length, paths.Select(Path.GetFileNameWithoutExtension).ToArray())) return null;
        return RunForPrefabAssetPaths(paths, options, "Project内Prefab");
    }

    public static BatchResult RunForHierarchyObjects(GameObject[] objects, RunOptions options)
    {
        GameObject[] roots = NormalizeHierarchyTargets(objects);
        if (roots.Length == 0)
        {
            Debug.LogError("Hierarchy上のAvatar/Prefabインスタンスを選択してください。");
            return null;
        }

        if (!ConfirmRunIfNeeded(options, "Hierarchy選択", null, roots.Length, roots.Select(x => x.name).ToArray())) return null;
        return RunForSceneObjects(roots, options, "Hierarchy選択");
    }

    // ------------------------------------------------------------
    // Selection runners
    // ------------------------------------------------------------

    private static void RunForBestCurrentSelection(RunOptions options)
    {
        string folder = GetSelectedFolderPath();
        if (!string.IsNullOrEmpty(folder))
        {
            ShowResultWindow(RunForFolder(folder, options));
            return;
        }

        string[] prefabPaths = GetSelectedPrefabAssetPaths();
        if (prefabPaths.Length > 0)
        {
            if (!ConfirmRunIfNeeded(options, "Project内Prefab", null, prefabPaths.Length, prefabPaths.Select(Path.GetFileNameWithoutExtension).ToArray())) return;
            ShowResultWindow(RunForPrefabAssetPaths(prefabPaths, options, "Project内Prefab"));
            return;
        }

        GameObject[] hierarchy = GetSelectedHierarchyRoots();
        if (hierarchy.Length > 0)
        {
            if (!ConfirmRunIfNeeded(options, "Hierarchy選択", null, hierarchy.Length, hierarchy.Select(x => x.name).ToArray())) return;
            ShowResultWindow(RunForSceneObjects(hierarchy, options, "Hierarchy選択"));
            return;
        }

        Debug.LogError("対象がありません。Projectのフォルダ/Prefab、またはHierarchy上のAvatarを選択してください。");
    }

    private static void RunForProjectSelection(RunOptions options)
    {
        string folder = GetSelectedFolderPath();
        if (!string.IsNullOrEmpty(folder))
        {
            ShowResultWindow(RunForFolder(folder, options));
            return;
        }

        string[] prefabPaths = GetSelectedPrefabAssetPaths();
        if (prefabPaths.Length == 0)
        {
            Debug.LogError("ProjectウィンドウでフォルダまたはPrefabを選択してください。");
            return;
        }

        if (!ConfirmRunIfNeeded(options, "Project内Prefab", null, prefabPaths.Length, prefabPaths.Select(Path.GetFileNameWithoutExtension).ToArray())) return;
        ShowResultWindow(RunForPrefabAssetPaths(prefabPaths, options, "Project内Prefab"));
    }

    private static void RunForHierarchySelection(RunOptions options)
    {
        GameObject[] hierarchy = GetSelectedHierarchyRoots();
        if (hierarchy.Length == 0)
        {
            Debug.LogError("Hierarchy上のAvatar/Prefabインスタンスを選択してください。");
            return;
        }

        if (!ConfirmRunIfNeeded(options, "Hierarchy選択", null, hierarchy.Length, hierarchy.Select(x => x.name).ToArray())) return;
        ShowResultWindow(RunForSceneObjects(hierarchy, options, "Hierarchy選択"));
    }

    // ------------------------------------------------------------
    // Core process: Prefab Assets
    // ------------------------------------------------------------

    private static BatchResult RunForPrefabAssetPaths(string[] prefabPaths, RunOptions options, string targetLabel)
    {
        BatchResult result = CreateResult(options, targetLabel, prefabPaths.Length);
        if (!ValidateDependencies(options, result))
        {
            LogAndShowResult(result);
            return result;
        }

        Debug.Log("開始: 対象=" + targetLabel + " / Prefab数=" + prefabPaths.Length + " / DryRun=" + options.DryRun + " / 処理=" + options.DisplayName);

        foreach (string path in prefabPaths)
        {
            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null)
                {
                    AddError(result, "Prefabを開けませんでした: " + path);
                    continue;
                }

                string label = path;
                ProcessAvatarRoot(prefabRoot, options, result, label, isSceneObject: false);

                if (!options.DryRun)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                }
            }
            catch (Exception ex)
            {
                AddError(result, "失敗: " + path + "\n" + ex);
            }
            finally
            {
                if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        if (!options.DryRun)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        LogAndShowResult(result);
        return result;
    }

    // ------------------------------------------------------------
    // Core process: Scene Objects
    // ------------------------------------------------------------

    private static BatchResult RunForSceneObjects(GameObject[] roots, RunOptions options, string targetLabel)
    {
        BatchResult result = CreateResult(options, targetLabel, roots.Length);
        if (!ValidateDependencies(options, result))
        {
            LogAndShowResult(result);
            return result;
        }

        Undo.SetCurrentGroupName("VRC Avatar Toolkit Plus - Avatar Setup");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject root in roots)
        {
            try
            {
                if (root == null)
                {
                    AddError(result, "nullの対象をスキップしました。");
                    continue;
                }

                ProcessAvatarRoot(root, options, result, root.name, isSceneObject: true);

                if (!options.DryRun)
                {
                    EditorUtility.SetDirty(root);
                    var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(root);
                    if (prefabRoot != null)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(prefabRoot);
                    }
                }
            }
            catch (Exception ex)
            {
                AddError(result, "失敗: " + root.name + "\n" + ex);
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        LogAndShowResult(result);
        return result;
    }

    private static void ProcessAvatarRoot(GameObject root, RunOptions options, BatchResult result, string label, bool isSceneObject)
    {
        Type aaoType = Type.GetType(AAOTypeName);
        Type lacType = Type.GetType(LACTypeName);
        Type presetEnumType = Type.GetType(LACEnumTypeName);
        GameObject rbsPrefabAsset = options.AddRBS ? LoadPrefabByGuid(RBSSuiminJapanesePrefabGuid, "RBS-睡眠システムVer2") : null;

        bool changed = false;
        string detail = string.Empty;

        if (options.AddAAO)
        {
            if (root.GetComponent(aaoType) == null)
            {
                if (!options.DryRun)
                {
                    if (isSceneObject) Undo.AddComponent(root, aaoType);
                    else root.AddComponent(aaoType);
                }
                result.AddedAAO++;
                changed = true;
                detail += "[AAO追加] ";
            }
        }

        if (options.AddLAC)
        {
            Component lacComponent = root.GetComponent(lacType);
            if (lacComponent == null)
            {
                if (!options.DryRun)
                {
                    lacComponent = isSceneObject ? Undo.AddComponent(root, lacType) : root.AddComponent(lacType);
                }
                result.AddedLAC++;
                changed = true;
                detail += "[LAC追加] ";
            }

            bool presetWillChange = options.DryRun
                ? lacComponent == null || WillChangeLacPreset(lacComponent, lacType, presetEnumType, options.LacPresetMode)
                : TrySetLacPreset(lacComponent, lacType, presetEnumType, options.LacPresetMode);

            if (presetWillChange)
            {
                result.ChangedLACPreset++;
                changed = true;
                detail += "[LAC Preset=" + GetPresetName(options.LacPresetMode) + "] ";
            }
        }

        if (options.AddRBS)
        {
            string rbsDetectionReason;
            if (!HasRBSSuiminInstalled(root, rbsPrefabAsset, out rbsDetectionReason))
            {
                if (!options.DryRun)
                {
                    GameObject rbsInstance = PrefabUtility.InstantiatePrefab(rbsPrefabAsset, root.scene) as GameObject;
                    if (rbsInstance == null) throw new Exception("RBS-睡眠システムVer2のPrefabインスタンス生成に失敗しました。");
                    if (isSceneObject) Undo.RegisterCreatedObjectUndo(rbsInstance, "Add RBS Suimin V2");
                    rbsInstance.transform.SetParent(root.transform, false);
                    EditorUtility.SetDirty(rbsInstance);
                    EditorUtility.SetDirty(rbsInstance.transform);
                }
                result.AddedRBS++;
                changed = true;
                detail += "[RBS睡眠V2追加] ";
            }
            else
            {
                detail += "[RBS既存検出: " + rbsDetectionReason + "] ";
            }
        }

        if (options.AddNade)
        {
            bool willInstall = true; // 元Installerと同じく既存NadeSystemは削除して再構築する
            if (willInstall)
            {
                if (!options.DryRun)
                {
                    InstallNadeSystem(root, options.Nade, isSceneObject);
                }
                result.AddedNade++;
                changed = true;
                detail += "[撫で音追加/再構築] ";
            }
        }

        if (changed)
        {
            result.ChangedTargets++;
            string line = (options.DryRun ? "[DryRun] " : "[変更] ") + label + " " + detail.Trim();
            result.Details.Add(line);
            Debug.Log(line);
        }
        else
        {
            result.Skipped++;
            string line = (options.DryRun ? "[DryRun][スキップ] " : "[スキップ] ") + label;
            result.Details.Add(line);
            Debug.Log(line);
        }
    }

    // ------------------------------------------------------------
    // Nade System install
    // ------------------------------------------------------------

    private static void InstallNadeSystem(GameObject avatarRoot, NadeOptions options, bool isSceneObject)
    {
        VRCAvatarDescriptor avatar = avatarRoot.GetComponent<VRCAvatarDescriptor>();
        if (avatar == null) avatar = avatarRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
        if (avatar == null) throw new Exception("VRCAvatarDescriptorが見つかりません。撫で音はアバターPrefabに対して実行してください: " + avatarRoot.name);

        options = options == null ? new NadeOptions() : options.Clone();
        if (options.ContactRadius < MinContactRadius || options.ContactRadius > MaxContactRadius)
        {
            options.ContactRadius = DefaultContactRadius;
        }

        Transform existingSystem = avatar.transform.Find("NadeSystem");
        if (existingSystem != null)
        {
            if (isSceneObject) Undo.DestroyObjectImmediate(existingSystem.gameObject);
            else UnityEngine.Object.DestroyImmediate(existingSystem.gameObject);
        }

        GameObject nadeSystemPrefab = LoadPrefabByGuid(NadeSystemGUID, "NadeSystem");
        GameObject nadeSystemRoot = PrefabUtility.InstantiatePrefab(nadeSystemPrefab, avatar.transform) as GameObject;
        if (nadeSystemRoot == null) throw new Exception("NadeSystem Prefabの生成に失敗しました。");
        if (isSceneObject) Undo.RegisterCreatedObjectUndo(nadeSystemRoot, "Add NadeSystem");

        NadeSystemObjects nadeObjects = FindNadeSystemObjects(nadeSystemRoot.transform);
        ConfigureNadeComponents(avatar, nadeObjects, options);
        nadeObjects.FootSystem = InstallNadeFootSystem(nadeObjects, nadeSystemRoot.transform, options, isSceneObject);
        InstallNadeShaders(nadeObjects, nadeSystemRoot.transform, options, isSceneObject);
        InstallNadeSphere(nadeObjects, nadeSystemRoot.transform, options, isSceneObject);

        if (nadeObjects.HeadSystem != null)
        {
            nadeObjects.HeadSystem.gameObject.SetActive(false);
        }

        EditorUtility.SetDirty(nadeSystemRoot);
        EditorUtility.SetDirty(avatar.gameObject);
    }

    private static NadeSystemObjects FindNadeSystemObjects(Transform nadeSystemRoot)
    {
        return new NadeSystemObjects
        {
            SystemRoot = nadeSystemRoot,
            RxHeadMain = FindRequiredChild(nadeSystemRoot, "RxHeadMain"),
            HeadSystem = FindRequiredChild(nadeSystemRoot, "HeadSystem"),
            RightHandSystem = FindRequiredChild(nadeSystemRoot, "RightHandSystem"),
            LeftHandSystem = FindRequiredChild(nadeSystemRoot, "LeftHandSystem"),
            ExMenu = FindRequiredChild(nadeSystemRoot, "ExMenu"),
            NadeControlMenu = FindRequiredChild(nadeSystemRoot, "ExMenu/Nade Control"),
            FootSystem = null
        };
    }

    private static void ConfigureNadeComponents(VRCAvatarDescriptor avatar, NadeSystemObjects nadeObjects, NadeOptions options)
    {
        Animator animator = avatar.GetComponent<Animator>();
        Transform headBone = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (headBone == null) throw new Exception("Head bone not found on avatar: " + avatar.gameObject.name);

        Vector3 avatarHeadCenter = avatar.collider_head.position + headBone.position;

        VRCContactReceiver rxHeadMainContact = nadeObjects.RxHeadMain.GetComponent<VRCContactReceiver>();
        if (rxHeadMainContact == null) throw new Exception("RxHeadMain に VRCContactReceiver がありません。");

        rxHeadMainContact.radius = options.ContactRadius;
        nadeObjects.RxHeadMain.transform.position = avatarHeadCenter + new Vector3(0f, options.HeadOffsetY, 0f);
        nadeObjects.HeadSystem.transform.position = avatar.ViewPosition + avatar.transform.position;

        EditorUtility.SetDirty(rxHeadMainContact);
    }

    private static Transform InstallNadeFootSystem(NadeSystemObjects nadeObjects, Transform nadeSystemRoot, NadeOptions options, bool isSceneObject)
    {
        if (!options.InstallFootSystem) return null;

        GameObject footSystemPrefab = LoadPrefabByGuid(FootSystemGUID, "FootSystem");
        GameObject footSystemMenuPrefab = LoadPrefabByGuid(FootSystemMenuGUID, "FootSystemMenu");
        GameObject footSystem = PrefabUtility.InstantiatePrefab(footSystemPrefab, nadeSystemRoot) as GameObject;
        GameObject menu = PrefabUtility.InstantiatePrefab(footSystemMenuPrefab, nadeObjects.NadeControlMenu.transform) as GameObject;
        if (isSceneObject)
        {
            if (footSystem != null) Undo.RegisterCreatedObjectUndo(footSystem, "Add Nade FootSystem");
            if (menu != null) Undo.RegisterCreatedObjectUndo(menu, "Add Nade FootSystem Menu");
        }
        return footSystem != null ? footSystem.transform : null;
    }

    private static void InstallNadeShaders(NadeSystemObjects nadeObjects, Transform nadeSystemRoot, NadeOptions options, bool isSceneObject)
    {
        if (!options.InstallNadeShaderForHands && !options.InstallNadeShaderForHead && !(options.InstallNadeShaderForFeet && options.InstallFootSystem)) return;

        GameObject nadeShadowPrefab = LoadPrefabByGuid(NadeShadowGUID, "NadeShadow");
        if (options.InstallNadeShaderForHands)
        {
            RegisterCreated(PrefabUtility.InstantiatePrefab(nadeShadowPrefab, nadeObjects.RightHandSystem.transform) as GameObject, isSceneObject, "Add NadeShadow RightHand");
            RegisterCreated(PrefabUtility.InstantiatePrefab(nadeShadowPrefab, nadeObjects.LeftHandSystem.transform) as GameObject, isSceneObject, "Add NadeShadow LeftHand");
        }
        if (options.InstallNadeShaderForHead)
        {
            RegisterCreated(PrefabUtility.InstantiatePrefab(nadeShadowPrefab, nadeObjects.HeadSystem.transform) as GameObject, isSceneObject, "Add NadeShadow Head");
        }
        if (options.InstallNadeShaderForFeet && options.InstallFootSystem && nadeObjects.FootSystem != null)
        {
            Transform rightFoot = FindOptionalChild(nadeObjects.FootSystem, "RightFootSystem/RxRightFoot");
            Transform leftFoot = FindOptionalChild(nadeObjects.FootSystem, "LeftFootSystem/RxLeftFoot");
            if (rightFoot != null) RegisterCreated(PrefabUtility.InstantiatePrefab(nadeShadowPrefab, rightFoot) as GameObject, isSceneObject, "Add NadeShadow RightFoot");
            if (leftFoot != null) RegisterCreated(PrefabUtility.InstantiatePrefab(nadeShadowPrefab, leftFoot) as GameObject, isSceneObject, "Add NadeShadow LeftFoot");
        }

        GameObject dummyLightPrefab = LoadPrefabByGuid(DummyLightGUID, "DummyLight");
        RegisterCreated(PrefabUtility.InstantiatePrefab(dummyLightPrefab, nadeSystemRoot) as GameObject, isSceneObject, "Add DummyLight");

        GameObject nadeShadowMenuPrefab = LoadPrefabByGuid(NadeShadowMenuGUID, "NadeShadowMenu");
        RegisterCreated(PrefabUtility.InstantiatePrefab(nadeShadowMenuPrefab, nadeObjects.ExMenu.transform) as GameObject, isSceneObject, "Add NadeShadowMenu");
    }

    private static void InstallNadeSphere(NadeSystemObjects nadeObjects, Transform nadeSystemRoot, NadeOptions options, bool isSceneObject)
    {
        if (!options.InstallNadeSphere) return;

        GameObject nadeSpherePrefab = LoadPrefabByGuid(NadeSphereGUID, "NadeSphere");
        GameObject nadeSphereMenuPrefab = LoadPrefabByGuid(NadeSphereMenuGUID, "NadeSphereMenu");
        RegisterCreated(PrefabUtility.InstantiatePrefab(nadeSpherePrefab, nadeSystemRoot) as GameObject, isSceneObject, "Add NadeSphere");
        RegisterCreated(PrefabUtility.InstantiatePrefab(nadeSphereMenuPrefab, nadeObjects.NadeControlMenu.transform) as GameObject, isSceneObject, "Add NadeSphereMenu");
    }

    private static void RegisterCreated(GameObject go, bool isSceneObject, string undoName)
    {
        if (go != null && isSceneObject) Undo.RegisterCreatedObjectUndo(go, undoName);
    }

    private class NadeSystemObjects
    {
        public Transform SystemRoot;
        public Transform RxHeadMain;
        public Transform HeadSystem;
        public Transform RightHandSystem;
        public Transform LeftHandSystem;
        public Transform ExMenu;
        public Transform NadeControlMenu;
        public Transform FootSystem;
    }

    // ------------------------------------------------------------
    // Dependency and prefab helpers
    // ------------------------------------------------------------

    private static bool ValidateDependencies(RunOptions options, BatchResult result)
    {
        if (options.AddAAO && Type.GetType(AAOTypeName) == null)
        {
            AddError(result, "AAO型が見つかりません: " + AAOTypeName);
            return false;
        }

        if (options.AddLAC && (Type.GetType(LACTypeName) == null || Type.GetType(LACEnumTypeName) == null))
        {
            AddError(result, "LAC型またはLAC preset enum型が見つかりません。\n" + LACTypeName + "\n" + LACEnumTypeName);
            return false;
        }

        if (options.AddRBS && LoadPrefabByGuidOrNull(RBSSuiminJapanesePrefabGuid) == null)
        {
            AddError(result, "RBS-睡眠システムVer2のPrefabが見つかりません。GUID=" + RBSSuiminJapanesePrefabGuid);
            return false;
        }

        if (options.AddNade)
        {
            string[] guids = { NadeSystemGUID, FootSystemGUID, FootSystemMenuGUID, NadeShadowGUID, DummyLightGUID, NadeShadowMenuGUID, NadeSphereGUID, NadeSphereMenuGUID };
            foreach (string guid in guids)
            {
                if (LoadPrefabByGuidOrNull(guid) == null)
                {
                    AddError(result, "撫で音ギミックのPrefabが見つかりません。GUID=" + guid);
                    return false;
                }
            }
        }

        return true;
    }

    private static GameObject LoadPrefabByGuid(string guid, string prefabName)
    {
        GameObject prefab = LoadPrefabByGuidOrNull(guid);
        if (prefab == null)
        {
            throw new FileNotFoundException("Prefab '" + prefabName + "' with GUID '" + guid + "' not found.");
        }
        return prefab;
    }

    private static GameObject LoadPrefabByGuidOrNull(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static bool HasRBSSuiminInstalled(GameObject root, GameObject rbsPrefabAsset, out string reason)
    {
        reason = string.Empty;
        if (root == null) return false;

        string rbsPrefabPath = rbsPrefabAsset != null ? AssetDatabase.GetAssetPath(rbsPrefabAsset) : string.Empty;
        string rbsPrefabRootName = rbsPrefabAsset != null ? rbsPrefabAsset.name : string.Empty;
        string normalizedPrefabRootName = NormalizeNameForCompare(rbsPrefabRootName);

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child == null || child == root.transform) continue;

            GameObject childObject = child.gameObject;

            // 1. 現行ツールで追加したPrefab Instanceを検出する。
            if (rbsPrefabAsset != null)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(childObject);
                if (source == rbsPrefabAsset)
                {
                    reason = "Prefab source一致";
                    return true;
                }

                // Prefab Variant / nested / root解決の差を吸収するため、sourceのAssetPathでも見る。
                if (source != null && !string.IsNullOrEmpty(rbsPrefabPath))
                {
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    if (!string.IsNullOrEmpty(sourcePath) && sourcePath == rbsPrefabPath)
                    {
                        reason = "Prefab path一致";
                        return true;
                    }
                }

                GameObject nearestSource = PrefabUtility.GetCorrespondingObjectFromSource(PrefabUtility.GetNearestPrefabInstanceRoot(childObject));
                if (nearestSource == rbsPrefabAsset)
                {
                    reason = "Prefab instance root一致";
                    return true;
                }
            }

            // 2. 手動導入・旧ツール導入・Unpack済みを検出する。
            //    Prefab接続が切れている場合は名前でしか拾えない。
            string normalizedChildName = NormalizeNameForCompare(childObject.name);

            if (!string.IsNullOrEmpty(normalizedPrefabRootName) && normalizedChildName == normalizedPrefabRootName)
            {
                reason = "Prefab root名一致: " + childObject.name;
                return true;
            }

            if (LooksLikeRBSSuiminObjectName(normalizedChildName))
            {
                reason = "RBS名シグネチャ一致: " + childObject.name;
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeRBSSuiminObjectName(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName)) return false;

        bool hasRbs = normalizedName.Contains("rbs");
        bool hasSleep =
            normalizedName.Contains("suimin") ||
            normalizedName.Contains("sleep") ||
            normalizedName.Contains("sleeping") ||
            normalizedName.Contains("睡眠");

        if (hasRbs && hasSleep) return true;

        // 日本語Prefab名で「RBS」が省略されている派生にも最低限対応する。
        // ただし「睡眠」だけだと誤検出があり得るので、system/ver2系の語も要求する。
        if (normalizedName.Contains("睡眠") &&
            (normalizedName.Contains("system") || normalizedName.Contains("システム") || normalizedName.Contains("ver2") || normalizedName.Contains("v2")))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeNameForCompare(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        string normalized = name.ToLowerInvariant();
        normalized = normalized.Replace("(clone)", string.Empty);
        normalized = normalized.Replace(" ", string.Empty);
        normalized = normalized.Replace("　", string.Empty);
        normalized = normalized.Replace("_", string.Empty);
        normalized = normalized.Replace("-", string.Empty);
        normalized = normalized.Replace("－", string.Empty);
        normalized = normalized.Replace("/", string.Empty);
        normalized = normalized.Replace("\\", string.Empty);
        normalized = normalized.Replace("（", string.Empty).Replace("）", string.Empty);
        normalized = normalized.Replace("(", string.Empty).Replace(")", string.Empty);
        return normalized;
    }

    private static Transform FindRequiredChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null) throw new InvalidOperationException("Required child object '" + name + "' not found in '" + parent.name + "'.");
        return child;
    }

    private static Transform FindOptionalChild(Transform parent, string name)
    {
        return parent != null ? parent.Find(name) : null;
    }

    // ------------------------------------------------------------
    // LAC helpers
    // ------------------------------------------------------------

    private static bool WillChangeLacPreset(Component lacComponent, Type lacType, Type presetEnumType, LacPresetMode presetMode)
    {
        if (lacComponent == null) return true;

        string targetPresetName = GetPresetName(presetMode);
        if (!Enum.IsDefined(presetEnumType, targetPresetName))
        {
            Debug.LogWarning("LAC Preset enumに存在しない値です: " + targetPresetName);
            return false;
        }

        object currentValue;
        if (TryGetLacPresetValue(lacComponent, lacType, out currentValue))
        {
            string currentName = currentValue != null ? currentValue.ToString() : string.Empty;
            return currentName != targetPresetName;
        }

        // Presetフィールド/プロパティを直接読めない場合でも、
        // ApplyPresetが存在するなら変更可能とみなす。
        return HasLacApplyPresetMethod(lacType, presetEnumType);
    }

    private static bool TrySetLacPreset(Component lacComponent, Type lacType, Type presetEnumType, LacPresetMode presetMode)
    {
        if (lacComponent == null) return false;

        string targetPresetName = GetPresetName(presetMode);
        if (!Enum.IsDefined(presetEnumType, targetPresetName))
        {
            Debug.LogWarning("LAC Preset enumに存在しない値です: " + targetPresetName);
            return false;
        }

        object currentValue;
        if (TryGetLacPresetValue(lacComponent, lacType, out currentValue))
        {
            string currentName = currentValue != null ? currentValue.ToString() : string.Empty;
            if (currentName == targetPresetName) return false;
        }

        object targetEnumValue = Enum.Parse(presetEnumType, targetPresetName);

        // LAC本体のEditor UIでは ApplyPreset(...) を呼んでいるため、
        // まずはこちらを優先する。Presetだけ変えると内部設定が更新されない可能性がある。
        MethodInfo applyPreset = lacType.GetMethod(
            "ApplyPreset",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { presetEnumType },
            null
        );

        if (applyPreset != null)
        {
            applyPreset.Invoke(lacComponent, new[] { targetEnumValue });
            EditorUtility.SetDirty(lacComponent);
            return true;
        }

        FieldInfo presetField = lacType.GetField(LACPresetFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (presetField != null)
        {
            presetField.SetValue(lacComponent, targetEnumValue);
            EditorUtility.SetDirty(lacComponent);
            return true;
        }

        PropertyInfo presetProperty = lacType.GetProperty(LACPresetFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (presetProperty != null && presetProperty.CanWrite)
        {
            presetProperty.SetValue(lacComponent, targetEnumValue, null);
            EditorUtility.SetDirty(lacComponent);
            return true;
        }

        Debug.LogWarning("LACのApplyPreset/Presetフィールド/Presetプロパティが見つかりません。");
        return false;
    }

    private static bool TryGetLacPresetValue(Component lacComponent, Type lacType, out object value)
    {
        value = null;
        if (lacComponent == null || lacType == null) return false;

        FieldInfo presetField = lacType.GetField(LACPresetFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (presetField != null)
        {
            value = presetField.GetValue(lacComponent);
            return true;
        }

        PropertyInfo presetProperty = lacType.GetProperty(LACPresetFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (presetProperty != null && presetProperty.CanRead)
        {
            value = presetProperty.GetValue(lacComponent, null);
            return true;
        }

        return false;
    }

    private static bool HasLacApplyPresetMethod(Type lacType, Type presetEnumType)
    {
        if (lacType == null || presetEnumType == null) return false;

        MethodInfo applyPreset = lacType.GetMethod(
            "ApplyPreset",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { presetEnumType },
            null
        );

        return applyPreset != null;
    }

    private static string GetPresetName(LacPresetMode presetMode)
    {
        return presetMode.ToString();
    }

    // ------------------------------------------------------------
    // Selection helpers
    // ------------------------------------------------------------

    private static string GetSelectedFolderPath()
    {
        UnityEngine.Object selectedObject = Selection.activeObject;
        if (selectedObject == null) return null;
        string path = AssetDatabase.GetAssetPath(selectedObject);
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.IsValidFolder(path) ? path : null;
    }

    private static string[] GetSelectedPrefabAssetPaths()
    {
        List<string> paths = new List<string>();
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj == null) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (!IsPrefabAssetPath(path)) continue;
            if (!paths.Contains(path)) paths.Add(path);
        }
        return paths.ToArray();
    }

    private static bool IsPrefabAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (AssetDatabase.IsValidFolder(path)) return false;
        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return false;
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefabAsset == null) return false;
        PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
        return prefabAssetType != PrefabAssetType.NotAPrefab;
    }

    private static GameObject[] GetSelectedHierarchyRoots()
    {
        return NormalizeHierarchyTargets(Selection.gameObjects);
    }

    private static GameObject[] NormalizeHierarchyTargets(GameObject[] selected)
    {
        if (selected == null) return new GameObject[0];

        List<GameObject> roots = new List<GameObject>();
        foreach (GameObject go in selected)
        {
            if (go == null) continue;
            string assetPath = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(assetPath)) continue; // Project assetは除外

            GameObject candidate = go;
            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (prefabRoot != null) candidate = prefabRoot;

            if (candidate.GetComponent<VRCAvatarDescriptor>() == null && candidate.GetComponentInChildren<VRCAvatarDescriptor>(true) == null)
            {
                // Prefabインスタンスではないが、AvatarDescriptorも無いものは対象外
                continue;
            }

            if (!roots.Contains(candidate)) roots.Add(candidate);
        }
        return roots.ToArray();
    }

    private static bool ConfirmRunIfNeeded(RunOptions options, string targetKind, string folderPath, int count, string[] names)
    {
        if (!options.ShowConfirm) return true;

        string targetText;
        if (!string.IsNullOrEmpty(folderPath))
        {
            targetText = "フォルダ: " + folderPath + "\n対象Prefab数: " + count;
        }
        else if (count == 1 && names != null && names.Length == 1)
        {
            targetText = targetKind + ": " + names[0];
        }
        else
        {
            targetText = targetKind + " 数: " + count;
            if (names != null && names.Length > 0 && names.Length <= 5)
            {
                targetText += "\n" + string.Join("\n", names);
            }
        }

        string message =
            "次の処理を実行します。\n\n" +
            "処理: " + options.DisplayName + "\n" +
            "DryRun: " + options.DryRun + "\n\n" +
            targetText + "\n\n" +
            "続行しますか？";

        return EditorUtility.DisplayDialog("VRC Avatar Toolkit Plus - Avatar Setup", message, "実行", "キャンセル");
    }

    // ------------------------------------------------------------
    // Result helpers
    // ------------------------------------------------------------

    private static BatchResult CreateResult(RunOptions options, string targetLabel, int count)
    {
        return new BatchResult { DryRun = options.DryRun, TargetLabel = targetLabel, TargetCount = count };
    }

    private static void AddError(BatchResult result, string message)
    {
        result.Failed++;
        result.Errors.Add(message);
        Debug.LogError(message);
    }

    private static void LogAndShowResult(BatchResult result)
    {
        if (result == null) return;
        Debug.Log(result.ToSummary());
    }

    private static void ShowResultWindow(BatchResult result)
    {
        if (result != null) AvatarOptimizationResultWindow.Show(result);
    }
}

public class AvatarOptimizationSetupWindow : EditorWindow
{
    private AvatarOptimizationSetup.TargetMode _targetMode = AvatarOptimizationSetup.TargetMode.SelectedProjectPrefabs;
    private DefaultAsset _folder;
    private readonly List<GameObject> _prefabAssets = new List<GameObject>();
    private readonly List<GameObject> _hierarchyObjects = new List<GameObject>();

    private bool _addAAO = true;
    private bool _addLAC = true;
    private bool _addRBS = true;
    private bool _addNade = true;
    private bool _dryRun;
    private AvatarOptimizationSetup.LacPresetMode _lacPreset = AvatarOptimizationSetup.LacPresetMode.HighQuality;

    private AvatarOptimizationSetup.NadeOptions _nade = new AvatarOptimizationSetup.NadeOptions();
    private Vector2 _scroll;
    private AvatarOptimizationSetup.BatchResult _lastResult;

    public static void ShowWindow()
    {
        AvatarOptimizationSetupWindow window = GetWindow<AvatarOptimizationSetupWindow>("Avatar Setup");
        window.minSize = new Vector2(520, 620);
        window.PullCurrentSelection();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("VRC Avatar Toolkit Plus / Avatar Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawTargetMode();
        EditorGUILayout.Space();
        DrawInstallOptions();
        EditorGUILayout.Space();
        DrawNadeOptions();
        EditorGUILayout.Space();
        DrawButtons();
        EditorGUILayout.Space();
        DrawLastResult();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTargetMode()
    {
        EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);
        _targetMode = (AvatarOptimizationSetup.TargetMode)EditorGUILayout.EnumPopup("Mode", _targetMode);

        if (GUILayout.Button("現在の選択を取り込む")) PullCurrentSelection();

        if (_targetMode == AvatarOptimizationSetup.TargetMode.SelectedFolder)
        {
            _folder = EditorGUILayout.ObjectField("Folder", _folder, typeof(DefaultAsset), false) as DefaultAsset;
        }
        else if (_targetMode == AvatarOptimizationSetup.TargetMode.SelectedProjectPrefabs)
        {
            DrawObjectList(_prefabAssets, "Project Prefabs", false);
        }
        else
        {
            DrawObjectList(_hierarchyObjects, "Hierarchy Objects", true);
        }
    }

    private void DrawInstallOptions()
    {
        EditorGUILayout.LabelField("導入するもの", EditorStyles.boldLabel);
        _addAAO = EditorGUILayout.ToggleLeft("AAO", _addAAO);
        _addLAC = EditorGUILayout.ToggleLeft("LAC", _addLAC);
        using (new EditorGUI.DisabledScope(!_addLAC))
        {
            _lacPreset = (AvatarOptimizationSetup.LacPresetMode)EditorGUILayout.EnumPopup("LAC Preset", _lacPreset);
        }
        _addRBS = EditorGUILayout.ToggleLeft("RBS-睡眠システムVer2", _addRBS);
        _addNade = EditorGUILayout.ToggleLeft("【赤夜式】撫で音ギミック", _addNade);
        _dryRun = EditorGUILayout.ToggleLeft("DryRun / テストモード", _dryRun);
    }

    private void DrawNadeOptions()
    {
        using (new EditorGUI.DisabledScope(!_addNade))
        {
            EditorGUILayout.LabelField("撫で音オプション", EditorStyles.boldLabel);
            _nade.InstallFootSystem = EditorGUILayout.ToggleLeft("足用システムを導入", _nade.InstallFootSystem);
            _nade.InstallNadeShaderForHands = EditorGUILayout.ToggleLeft("手用の撫で影を導入", _nade.InstallNadeShaderForHands);
            using (new EditorGUI.DisabledScope(!_nade.InstallFootSystem))
            {
                _nade.InstallNadeShaderForFeet = EditorGUILayout.ToggleLeft("足用の撫で影を導入", _nade.InstallNadeShaderForFeet);
            }
            _nade.InstallNadeShaderForHead = EditorGUILayout.ToggleLeft("頭用の撫で影を導入", _nade.InstallNadeShaderForHead);
            _nade.InstallNadeSphere = EditorGUILayout.ToggleLeft("NadeSphereを導入", _nade.InstallNadeSphere);
            _nade.ContactRadius = EditorGUILayout.FloatField("Contact Radius", _nade.ContactRadius);
            _nade.HeadOffsetY = EditorGUILayout.FloatField("Head Offset Y", _nade.HeadOffsetY);
        }
    }

    private void DrawButtons()
    {
        EditorGUILayout.LabelField("実行", EditorStyles.boldLabel);
        if (GUILayout.Button("選択した設定で実行")) RunCurrent();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("AAOのみ")) RunPreset(true, false, false, false, _lacPreset);
        if (GUILayout.Button("LAC HQのみ")) RunPreset(false, true, false, false, AvatarOptimizationSetup.LacPresetMode.HighQuality);
        if (GUILayout.Button("LAC Qualityのみ")) RunPreset(false, true, false, false, AvatarOptimizationSetup.LacPresetMode.Quality);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("RBSのみ")) RunPreset(false, false, true, false, _lacPreset);
        if (GUILayout.Button("撫で音のみ")) RunPreset(false, false, false, true, _lacPreset);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("AAO + LAC HQ")) RunPreset(true, true, false, false, AvatarOptimizationSetup.LacPresetMode.HighQuality);
        if (GUILayout.Button("AAO + LAC Quality")) RunPreset(true, true, false, false, AvatarOptimizationSetup.LacPresetMode.Quality);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全部 HQ")) RunPreset(true, true, true, true, AvatarOptimizationSetup.LacPresetMode.HighQuality);
        if (GUILayout.Button("全部 Quality")) RunPreset(true, true, true, true, AvatarOptimizationSetup.LacPresetMode.Quality);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLastResult()
    {
        if (_lastResult == null) return;
        EditorGUILayout.LabelField("直近の結果", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(_lastResult.ToFullText(), GUILayout.MinHeight(160));
    }

    private void DrawObjectList(List<GameObject> list, string label, bool allowSceneObjects)
    {
        EditorGUILayout.LabelField(label);
        int remove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            list[i] = EditorGUILayout.ObjectField(list[i], typeof(GameObject), allowSceneObjects) as GameObject;
            if (GUILayout.Button("-", GUILayout.Width(24))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) list.RemoveAt(remove);
        if (GUILayout.Button("+")) list.Add(null);
    }

    private void PullCurrentSelection()
    {
        _prefabAssets.Clear();
        _hierarchyObjects.Clear();

        UnityEngine.Object active = Selection.activeObject;
        string activePath = active != null ? AssetDatabase.GetAssetPath(active) : null;
        if (!string.IsNullOrEmpty(activePath) && AssetDatabase.IsValidFolder(activePath))
        {
            _folder = active as DefaultAsset;
            _targetMode = AvatarOptimizationSetup.TargetMode.SelectedFolder;
            Repaint();
            return;
        }

        foreach (UnityEngine.Object obj in Selection.objects)
        {
            GameObject go = obj as GameObject;
            if (go == null) continue;
            string path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                if (!_prefabAssets.Contains(go)) _prefabAssets.Add(go);
            }
        }

        if (_prefabAssets.Count > 0)
        {
            _targetMode = AvatarOptimizationSetup.TargetMode.SelectedProjectPrefabs;
            Repaint();
            return;
        }

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            string path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path)) continue;
            if (!_hierarchyObjects.Contains(go)) _hierarchyObjects.Add(go);
        }

        if (_hierarchyObjects.Count > 0)
        {
            _targetMode = AvatarOptimizationSetup.TargetMode.SelectedHierarchyObjects;
        }

        Repaint();
    }

    private void RunPreset(bool aao, bool lac, bool rbs, bool nade, AvatarOptimizationSetup.LacPresetMode preset)
    {
        _addAAO = aao;
        _addLAC = lac;
        _addRBS = rbs;
        _addNade = nade;
        _lacPreset = preset;
        RunCurrent();
    }

    private void RunCurrent()
    {
        AvatarOptimizationSetup.RunOptions options = new AvatarOptimizationSetup.RunOptions
        {
            AddAAO = _addAAO,
            AddLAC = _addLAC,
            AddRBS = _addRBS,
            AddNade = _addNade,
            LacPresetMode = _lacPreset,
            DryRun = _dryRun,
            ShowConfirm = true,
            Nade = _nade.Clone()
        };

        if (_targetMode == AvatarOptimizationSetup.TargetMode.SelectedFolder)
        {
            string path = _folder != null ? AssetDatabase.GetAssetPath(_folder) : null;
            _lastResult = AvatarOptimizationSetup.RunForFolder(path, options);
        }
        else if (_targetMode == AvatarOptimizationSetup.TargetMode.SelectedProjectPrefabs)
        {
            _lastResult = AvatarOptimizationSetup.RunForPrefabAssets(_prefabAssets.ToArray(), options);
        }
        else
        {
            _lastResult = AvatarOptimizationSetup.RunForHierarchyObjects(_hierarchyObjects.ToArray(), options);
        }

        if (_lastResult != null) AvatarOptimizationResultWindow.Show(_lastResult);
        Repaint();
    }
}

public class AvatarOptimizationResultWindow : EditorWindow
{
    private AvatarOptimizationSetup.BatchResult _result;
    private Vector2 _scroll;

    public static void Show(AvatarOptimizationSetup.BatchResult result)
    {
        AvatarOptimizationResultWindow window = GetWindow<AvatarOptimizationResultWindow>("Avatar Setup Result");
        window._result = result;
        window.minSize = new Vector2(520, 360);
        window.Show();
    }

    private void OnGUI()
    {
        if (_result == null)
        {
            EditorGUILayout.LabelField("結果がありません。");
            return;
        }

        EditorGUILayout.LabelField(_result.DryRun ? "Dry Run Result" : "Result", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_result.ToFullText(), GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}

}
