using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public class AkiirayAvatarSetupTool : EditorWindow
{
    private enum LacPresetMode
    {
        HighQuality,
        Quality,
        Balanced,
        Aggressive,
        Maximum
    }

    private enum KawaiiPoseInstallMode
    {
        None,
        Normal,
        EightBitNoFoot,
        Both
    }

    private enum TargetMode
    {
        SelectedHierarchyAvatars,
        SelectedProjectPrefabAssets,
        SelectedProjectFolderPrefabs,
        AllProjectPrefabs
    }

    private sealed class SetupTarget
    {
        public bool IsPrefabAsset;
        public string PrefabAssetPath;
        public GameObject SceneObject;
        public string Label;
    }

    private Vector2 scroll;
    private string log = "";
    private string detailedLog = "";

    private TargetMode targetMode = TargetMode.SelectedHierarchyAvatars;
    private string selectedFolderPath = "";
    private bool requireAvatarDescriptor = true;

    private bool addAAO = true;
    private bool addLAC = true;
    private LacPresetMode lacPreset = LacPresetMode.HighQuality;
    private bool addRBS = true;
    private bool addNadeSystem = true;
    private bool addLightLimitChanger = true;
    private bool addKawaiiNormal = false;
    private bool addKawaii8bitNoFoot = true;
    private KawaiiPoseInstallMode allInstallKawaiiMode = KawaiiPoseInstallMode.EightBitNoFoot;

    private const string AAOTypeName = "Anatawa12.AvatarOptimizer.TraceAndOptimize";
    private const string LACTypeName = "dev.limitex.avatar.compressor.TextureCompressor";
    private const string LACPresetEnumTypeName = "dev.limitex.avatar.compressor.CompressorPreset";
    private const string LLCV2ComponentTypeName = "io.github.azukimochi.LightLimitChangerComponent";
    private const string LLCV1SettingsTypeName = "io.github.azukimochi.LightLimitChangerSettings";
    private const string LLCV2ContextMenuTypeName = "io.github.azukimochi.LightLimitChangerContextMenu";
    private const string LLCV1InstallerTypeName = "io.github.azukimochi.LightLimitChanger";
    private const string LLCInstalledObjectName = "Light Limit Changer";
    private static readonly string[] LLCInstalledTypeNames =
    {
        LLCV2ComponentTypeName,
        LLCV1SettingsTypeName,
    };
    private static readonly string[] LLCPrefabNames =
    {
        LLCInstalledObjectName,
        "LightLimitChanger",
    };
    private static readonly string[] LLCPrefabPaths =
    {
        "Packages/io.github.azukimochi.light-limit-changer/Light Limit Changer.prefab",
        "Assets/LightLimitChanger/Light Limit Changer.prefab",
    };
    private const string KawaiiComponentTypeName = "jp.unisakistudio.kawaiiposing.KawaiiPosing";
    private const string PosingSystemMenuItemsTypeName = "jp.unisakistudio.posingsystemeditor.PosingSystemMenuItems";
    private const string NadeSettingsTypeName = "RedNightWorks.NadeSystem.NadeSystemSettings";

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Window", false, 0)]
    public static void Open()
    {
        GetWindow<AkiirayAvatarSetupTool>("VRC Avatar Toolkit Plus - Avatar Setup");
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add AAO Only")]
    public static void MenuAddAAOOnly() { OpenAndRunPreset(addAAO: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add LAC Only")]
    public static void MenuAddLACOnly() { OpenAndRunPreset(addLAC: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add RBS Suimin V2 Only")]
    public static void MenuAddRBSOnly() { OpenAndRunPreset(addRBS: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add Nade System Only")]
    public static void MenuAddNadeOnly() { OpenAndRunPreset(addNadeSystem: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add LightLimitChanger Only")]
    public static void MenuAddLightLimitChangerOnly() { OpenAndRunPreset(addLightLimitChanger: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add Kawaii Pose Only/可愛いポーズ")]
    public static void MenuAddKawaiiNormalOnly() { OpenAndRunPreset(addKawaiiNormal: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Add Kawaii Pose Only/可愛いポーズ(8bit・足の高さなし)")]
    public static void MenuAddKawaii8bitOnly() { OpenAndRunPreset(addKawaii8bitNoFoot: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Setup All")]
    public static void MenuSetupAll() { OpenAndRunPreset(addAAO: true, addLAC: true, addRBS: true, addNadeSystem: true, addLightLimitChanger: true, addKawaii8bitNoFoot: true); }

    private static void OpenAndRunPreset(
        bool addAAO = false,
        bool addLAC = false,
        bool addRBS = false,
        bool addNadeSystem = false,
        bool addLightLimitChanger = false,
        bool addKawaiiNormal = false,
        bool addKawaii8bitNoFoot = false)
    {
        var window = GetWindow<AkiirayAvatarSetupTool>("VRC Avatar Toolkit Plus - Avatar Setup");
        window.addAAO = addAAO;
        window.addLAC = addLAC;
        window.addRBS = addRBS;
        window.addNadeSystem = addNadeSystem;
        window.addLightLimitChanger = addLightLimitChanger;
        window.addKawaiiNormal = addKawaiiNormal;
        window.addKawaii8bitNoFoot = addKawaii8bitNoFoot;
        window.Focus();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("VRC Avatar Toolkit Plus - Avatar Setup", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("ツール導入確認", EditorStyles.boldLabel);
            DrawDependencyOverview();

            if (GUILayout.Button("依存関係・導入状態チェック", GUILayout.Height(28)))
            {
                detailedLog = Run(false, true);
                log = BuildConciseLog(detailedLog);
            }
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);
            targetMode = (TargetMode)EditorGUILayout.EnumPopup("対象モード", targetMode);
            requireAvatarDescriptor = EditorGUILayout.ToggleLeft("VRCAvatarDescriptorがあるPrefab/Hierarchyだけ対象", requireAvatarDescriptor);

            if (targetMode == TargetMode.SelectedProjectFolderPrefabs)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.TextField("対象フォルダ", string.IsNullOrEmpty(selectedFolderPath) ? "<未指定: Projectで選択中フォルダを使用>" : selectedFolderPath);
                    if (GUILayout.Button("選択フォルダを使用", GUILayout.Width(130)))
                    {
                        selectedFolderPath = GetSelectedFolderPath() ?? "";
                    }
                }
            }

            var previewTargets = BuildTargets();
            EditorGUILayout.LabelField("検出対象数", previewTargets.Count.ToString());
            foreach (var t in previewTargets.Take(5))
                EditorGUILayout.LabelField("- " + t.Label);
            if (previewTargets.Count > 5)
                EditorGUILayout.LabelField("...他 " + (previewTargets.Count - 5) + " 件");
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("導入プリセット", EditorStyles.boldLabel);
            allInstallKawaiiMode = (KawaiiPoseInstallMode)EditorGUILayout.EnumPopup("すべて導入時の可愛いポーズ", allInstallKawaiiMode);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("すべて導入を選択"))
                {
                    SelectAllInstallOptions();
                }

                if (GUILayout.Button("すべて解除"))
                {
                    ClearInstallOptions();
                }
            }
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("個別導入", EditorStyles.boldLabel);
            addAAO = EditorGUILayout.ToggleLeft("AAO / TraceAndOptimize（" + GetSimpleDependencyState(AAOTypeName) + "）", addAAO);
            addLAC = EditorGUILayout.ToggleLeft("LAC / TextureCompressor（" + GetSimpleDependencyState(LACTypeName) + "）", addLAC);
            using (new EditorGUI.DisabledScope(!addLAC))
            {
                lacPreset = (LacPresetMode)EditorGUILayout.EnumPopup("LAC Preset", lacPreset);
            }
            addRBS = EditorGUILayout.ToggleLeft("RBS 睡眠システム Ver2（" + GetPrefabDependencyState(new[] { "RBS_Suimin(日本語)", "RBS_Suimin" }) + "）", addRBS);
            addNadeSystem = EditorGUILayout.ToggleLeft("赤夜式 撫で音ギミック（" + GetSimpleDependencyState(NadeSettingsTypeName) + "）", addNadeSystem);
            addLightLimitChanger = EditorGUILayout.ToggleLeft("LightLimitChanger（" + GetLightLimitChangerDependencyState() + "）", addLightLimitChanger);
            addKawaiiNormal = EditorGUILayout.ToggleLeft("可愛いポーズ（" + GetSimpleDependencyState(KawaiiComponentTypeName) + "）", addKawaiiNormal);
            addKawaii8bitNoFoot = EditorGUILayout.ToggleLeft("可愛いポーズ(8bit・足の高さなし)（" + GetSimpleDependencyState(KawaiiComponentTypeName) + "）", addKawaii8bitNoFoot);
        }

        if (GUILayout.Button("導入テスト（DryRun）", GUILayout.Height(36)))
        {
            detailedLog = Run(false, false);
            log = BuildConciseLog(detailedLog);
        }

        if (GUILayout.Button("導入実行", GUILayout.Height(36)))
        {
            detailedLog = Run(true, false);
            log = BuildConciseLog(detailedLog);
        }

        if (GUILayout.Button("詳細ログを表示", GUILayout.Height(36)))
        {
            AvatarSetupLogWindow.ShowLog(string.IsNullOrEmpty(detailedLog) ? log : detailedLog);
        }

        EditorGUILayout.LabelField("実行結果", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void DrawDependencyOverview()
    {
        EditorGUILayout.LabelField("AAO", GetSimpleDependencyState(AAOTypeName));
        EditorGUILayout.LabelField("LAC", GetSimpleDependencyState(LACTypeName));
        EditorGUILayout.LabelField("RBS", GetPrefabDependencyState(new[] { "RBS_Suimin(日本語)", "RBS_Suimin" }));
        EditorGUILayout.LabelField("赤夜式 撫で音", GetSimpleDependencyState(NadeSettingsTypeName));
        EditorGUILayout.LabelField("LightLimitChanger", GetLightLimitChangerDependencyState());
        EditorGUILayout.LabelField("可愛いポーズ", GetSimpleDependencyState(KawaiiComponentTypeName));
    }

    private string GetSimpleDependencyState(string typeName)
    {
        return FindType(typeName) != null ? "導入済" : "未導入/未判定";
    }

    private string GetPrefabDependencyState(string[] prefabNames)
    {
        return FindPrefabByNames(prefabNames) != null ? "導入済" : "未導入/未判定";
    }

    private string GetLightLimitChangerDependencyState()
    {
        if (FindLightLimitChangerV2SetupMethod() != null)
            return "導入済（V2）";
        if (FindLightLimitChangerV1ApplyMethod() != null)
            return "導入済（V1）";
        if (FindPrefabByNames(LLCPrefabNames) != null)
            return "導入済（Prefabのみ/未判定）";
        return "未導入/未判定";
    }

    private string BuildConciseLog(string detail)
    {
        if (string.IsNullOrEmpty(detail))
            return "実行結果はまだありません。";

        var sb = new StringBuilder();
        foreach (var rawLine in detail.Split(new[] { '\n' }, StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("Mode: ", StringComparison.Ordinal))
            {
                sb.AppendLine("動作: " + ToJapaneseMode(line.Substring(6)));
            }
            else if (line.StartsWith("TargetCount: ", StringComparison.Ordinal))
            {
                sb.AppendLine("対象数: " + line.Substring(13));
            }
            else if (line.StartsWith("## Target: ", StringComparison.Ordinal))
            {
                sb.AppendLine();
                sb.AppendLine("対象: " + line.Substring(11));
            }
            else if (line.Contains("[OK]") || line.Contains("[SKIP]") || line.Contains("[DRY]") || line.Contains("[WARN]") || line.Contains("[ERROR]"))
            {
                sb.AppendLine(ToJapaneseResultLine(line));
            }
            else if (line.StartsWith("LightLimitChanger Variant: ", StringComparison.Ordinal))
            {
                sb.AppendLine("LightLimitChanger判定: " + ToJapaneseLlcVariant(line.Substring(27)));
            }
            else if (line.Contains(": Installed") || line.Contains(": Not Installed") || line.Contains(": Type not found"))
            {
                sb.AppendLine(ToJapaneseStatusLine(line));
            }
        }

        return sb.Length > 0 ? sb.ToString() : detail;
    }

    private string ToJapaneseMode(string mode)
    {
        if (mode == "Check Only") return "導入状態チェック";
        if (mode == "Apply") return "導入実行";
        if (mode == "Dry Run") return "導入テスト（DryRun）";
        return mode;
    }

    private string ToJapaneseLlcVariant(string variant)
    {
        if (variant.StartsWith("V2 detected", StringComparison.Ordinal)) return "V2を検出";
        if (variant.StartsWith("V1 detected", StringComparison.Ordinal)) return "V1を検出";
        if (variant.StartsWith("unknown", StringComparison.Ordinal)) return "未判定";
        return variant;
    }

    private string ToJapaneseStatusLine(string line)
    {
        return line.Replace("Not Installed", "未導入")
            .Replace("Installed", "導入済")
            .Replace("Type not found", "型が見つかりません")
            .Replace("Count", "数")
            .Replace("Component 数", "コンポーネント数")
            .Replace("Name Hit 数", "名前一致数");
    }

    private string ToJapaneseResultLine(string line)
    {
        return line.Replace("[OK]", "[成功]")
            .Replace("[SKIP]", "[スキップ]")
            .Replace("[DRY]", "[テスト]")
            .Replace("[WARN]", "[注意]")
            .Replace("[ERROR]", "[エラー]")
            .Replace("Already installed on avatar root", "アバター直下に既に導入済み")
            .Replace("Already installed", "既に導入済み")
            .Replace("Type not found", "型が見つかりません")
            .Replace("Prefab not found", "Prefabが見つかりません")
            .Replace("Add component", "コンポーネント追加")
            .Replace("Added component", "コンポーネントを追加しました")
            .Replace("Instantiate prefab", "Prefabを追加予定")
            .Replace("Added prefab fallback", "Prefabフォールバックで追加しました")
            .Replace("Called", "呼び出しました")
            .Replace("Call", "呼び出し予定")
            .Replace("Saved prefab", "Prefabを保存しました");
    }

    private void SelectAllInstallOptions()
    {
        addAAO = true;
        addLAC = true;
        addRBS = true;
        addNadeSystem = true;
        addLightLimitChanger = true;
        ApplyKawaiiModeToToggles(allInstallKawaiiMode);
    }

    private void ClearInstallOptions()
    {
        addAAO = false;
        addLAC = false;
        addRBS = false;
        addNadeSystem = false;
        addLightLimitChanger = false;
        addKawaiiNormal = false;
        addKawaii8bitNoFoot = false;
    }

    private void ApplyKawaiiModeToToggles(KawaiiPoseInstallMode mode)
    {
        addKawaiiNormal = mode == KawaiiPoseInstallMode.Normal || mode == KawaiiPoseInstallMode.Both;
        addKawaii8bitNoFoot = mode == KawaiiPoseInstallMode.EightBitNoFoot || mode == KawaiiPoseInstallMode.Both;
    }

    private string Run(bool apply, bool checkOnly)
    {
        var sb = new StringBuilder();
        var targets = BuildTargets();

        sb.AppendLine("# Akiiray Avatar Setup Report");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Unity: " + Application.unityVersion);
        sb.AppendLine("Mode: " + (checkOnly ? "Check Only" : (apply ? "Apply" : "Dry Run")));
        sb.AppendLine("TargetMode: " + targetMode);
        sb.AppendLine("TargetCount: " + targets.Count);
        sb.AppendLine();

        sb.AppendLine("============================================================");
        sb.AppendLine("## Dependency Status");
        AppendDependencyStatus(sb);
        sb.AppendLine();

        if (targets.Count == 0)
        {
            sb.AppendLine("対象がありません。Hierarchyのアバター、ProjectのPrefab、またはProjectフォルダを選択してください。");
            return sb.ToString();
        }

        foreach (var target in targets)
        {
            sb.AppendLine("============================================================");
            sb.AppendLine("## Target: " + target.Label);
            RunForTarget(sb, target, apply, checkOnly);
            sb.AppendLine();
        }

        if (apply)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        return sb.ToString();
    }

    private void RunForTarget(StringBuilder sb, SetupTarget target, bool apply, bool checkOnly)
    {
        if (target.IsPrefabAsset)
        {
            if (apply)
            {
                var loadedRoot = PrefabUtility.LoadPrefabContents(target.PrefabAssetPath);
                try
                {
                    var avatarRoot = FindAvatarRootFlexible(loadedRoot);
                    if (avatarRoot == null)
                    {
                        sb.AppendLine("[SKIP] VRCAvatarDescriptorが見つかりません。");
                        return;
                    }

                    RunForAvatarRoot(sb, avatarRoot, apply: true, checkOnly: checkOnly, isPrefabAsset: true);
                    PrefabUtility.SaveAsPrefabAsset(loadedRoot, target.PrefabAssetPath);
                    sb.AppendLine("[OK] Saved prefab: " + target.PrefabAssetPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(loadedRoot);
                }
            }
            else
            {
                var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(target.PrefabAssetPath);
                var avatarRoot = FindAvatarRootFlexible(prefabRoot);
                if (avatarRoot == null)
                {
                    sb.AppendLine("[SKIP] VRCAvatarDescriptorが見つかりません。");
                    return;
                }

                RunForAvatarRoot(sb, avatarRoot, apply: false, checkOnly: checkOnly, isPrefabAsset: true);
            }
        }
        else
        {
            var avatarRoot = FindAvatarRootFlexible(target.SceneObject);
            if (avatarRoot == null)
            {
                sb.AppendLine("[SKIP] VRCAvatarDescriptorが見つかりません。");
                return;
            }

            RunForAvatarRoot(sb, avatarRoot, apply, checkOnly, isPrefabAsset: false);
        }
    }

    private void RunForAvatarRoot(StringBuilder sb, GameObject avatarRoot, bool apply, bool checkOnly, bool isPrefabAsset)
    {
        sb.AppendLine("Avatar Root: " + GetPath(avatarRoot.transform));
        sb.AppendLine("Asset Mode: " + (isPrefabAsset ? "Project Prefab" : "Hierarchy"));
        sb.AppendLine();

        sb.AppendLine("-- Installed Before --");
        AppendInstallStatus(sb, avatarRoot);
        sb.AppendLine();

        if (checkOnly)
            return;

        int undoGroup = -1;
        if (!isPrefabAsset)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Akiiray Avatar Setup");
            undoGroup = Undo.GetCurrentGroup();
        }

        try
        {
            sb.AppendLine("-- Actions --");
            if (addAAO) InstallComponent(sb, avatarRoot, "AAO", AAOTypeName, apply, null);
            if (addLAC) InstallLac(sb, avatarRoot, apply);
            if (addRBS) InstallPrefabByName(sb, avatarRoot, "RBS", new[] { "RBS_Suimin(日本語)", "RBS_Suimin" }, apply);
            if (addNadeSystem) InstallPrefabByName(sb, avatarRoot, "赤夜式 撫で音", new[] { "NadeSystem" }, apply);
            if (addLightLimitChanger) InstallLightLimitChangerOfficial(sb, avatarRoot, apply);
            if (addKawaiiNormal) InstallKawaiiOfficial(sb, avatarRoot, "可愛いポーズ", apply);
            if (addKawaii8bitNoFoot) InstallKawaiiOfficial(sb, avatarRoot, "可愛いポーズ(8bit・足の高さなし)", apply);

            if (apply)
            {
                EditorUtility.SetDirty(avatarRoot);
                if (!isPrefabAsset && undoGroup >= 0)
                    Undo.CollapseUndoOperations(undoGroup);
            }
            else
            {
                if (!isPrefabAsset && undoGroup >= 0)
                    Undo.RevertAllDownToGroup(undoGroup);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("[ERROR] " + ex.GetType().Name + ": " + ex.Message);
            if (!apply && !isPrefabAsset && undoGroup >= 0)
                Undo.RevertAllDownToGroup(undoGroup);
        }

        sb.AppendLine();
        sb.AppendLine("-- Installed After --");
        AppendInstallStatus(sb, avatarRoot);
    }

    private List<SetupTarget> BuildTargets()
    {
        var list = new List<SetupTarget>();

        if (targetMode == TargetMode.SelectedHierarchyAvatars)
        {
            foreach (var go in Selection.gameObjects.Where(x => x != null && !EditorUtility.IsPersistent(x)).Distinct())
            {
                var avatarRoot = FindAvatarRootFlexible(go);
                if (requireAvatarDescriptor && avatarRoot == null) continue;
                var root = avatarRoot != null ? avatarRoot : go;
                if (list.Any(x => x.SceneObject == root)) continue;
                list.Add(new SetupTarget
                {
                    IsPrefabAsset = false,
                    SceneObject = root,
                    Label = GetPath(root.transform)
                });
            }
        }
        else if (targetMode == TargetMode.SelectedProjectPrefabAssets)
        {
            foreach (var path in GetSelectedPrefabAssetPaths())
                AddPrefabTargetIfValid(list, path);
        }
        else if (targetMode == TargetMode.SelectedProjectFolderPrefabs)
        {
            var folder = string.IsNullOrEmpty(selectedFolderPath) ? GetSelectedFolderPath() : selectedFolderPath;
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                foreach (var path in FindPrefabPathsInFolder(folder))
                    AddPrefabTargetIfValid(list, path);
            }
        }
        else if (targetMode == TargetMode.AllProjectPrefabs)
        {
            foreach (var path in FindAllProjectPrefabPaths())
                AddPrefabTargetIfValid(list, path);
        }

        return list;
    }

    private void AddPrefabTargetIfValid(List<SetupTarget> list, string path)
    {
        if (string.IsNullOrEmpty(path) || list.Any(x => x.PrefabAssetPath == path)) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return;
        if (requireAvatarDescriptor && FindAvatarRootFlexible(prefab) == null) return;
        list.Add(new SetupTarget
        {
            IsPrefabAsset = true,
            PrefabAssetPath = path,
            Label = path
        });
    }

    private void AppendDependencyStatus(StringBuilder sb)
    {
        AppendTypeLine(sb, "AAO", AAOTypeName);
        AppendTypeLine(sb, "LAC", LACTypeName);
        AppendTypeLine(sb, "LAC Preset Enum", LACPresetEnumTypeName);
        AppendLightLimitChangerDependencyStatus(sb);
        AppendTypeLine(sb, "LightLimitChanger V2 Component", LLCV2ComponentTypeName);
        AppendTypeLine(sb, "LightLimitChanger V1 Settings", LLCV1SettingsTypeName);
        AppendTypeLine(sb, "LightLimitChanger V2 ContextMenu", LLCV2ContextMenuTypeName);
        AppendTypeLine(sb, "LightLimitChanger V1 Installer", LLCV1InstallerTypeName);
        AppendPrefabCandidateLine(sb, "LightLimitChanger Prefab", LLCPrefabNames);
        AppendTypeLine(sb, "KawaiiPosing Component", KawaiiComponentTypeName);
        AppendTypeLine(sb, "PosingSystem Official AddPrefab", PosingSystemMenuItemsTypeName);
        AppendTypeLine(sb, "RedNight NadeSystem", NadeSettingsTypeName);

        sb.AppendLine();
        AppendPackageVersion(sb, "com.anatawa12.avatar-optimizer");
        AppendPackageVersion(sb, "dev.limitex.avatar-compressor");
        AppendPackageVersion(sb, "io.github.azukimochi.light-limit-changer");
        AppendPackageVersion(sb, "jp.unisakistudio.kawaiiposing");
        AppendPackageVersion(sb, "jp.unisakistudio.posingsystem");
        AppendPackageVersion(sb, "nadena.dev.modular-avatar");
    }

    private void AppendTypeLine(StringBuilder sb, string label, string typeName)
    {
        var type = FindType(typeName);
        sb.AppendLine(label + ": " + (type != null ? "OK / " + type.Assembly.GetName().Name : "NG / Type not found"));
    }

    private void AppendLightLimitChangerDependencyStatus(StringBuilder sb)
    {
        var v2ComponentType = FindType(LLCV2ComponentTypeName);
        var v1SettingsType = FindType(LLCV1SettingsTypeName);
        var v2SetupType = FindType(LLCV2ContextMenuTypeName);
        var v1InstallerType = FindType(LLCV1InstallerTypeName);
        var v2SetupMethod = FindLightLimitChangerV2SetupMethod();
        var v1ApplyMethod = FindLightLimitChangerV1ApplyMethod();
        var prefab = FindPrefabByNames(LLCPrefabNames);

        AppendResolvedTypeLine(sb, "LightLimitChanger V2 Component", v2ComponentType);
        AppendResolvedTypeLine(sb, "LightLimitChanger V1 Settings", v1SettingsType);
        AppendResolvedTypeLine(sb, "LightLimitChanger V2 ContextMenu", v2SetupType);
        AppendResolvedTypeLine(sb, "LightLimitChanger V1 Installer", v1InstallerType);
        sb.AppendLine("LightLimitChanger V2 Setup(): " + (v2SetupMethod != null ? "OK" : "NG / Method not found"));
        sb.AppendLine("LightLimitChanger V1 ApplytoAvatar(MenuCommand): " + (v1ApplyMethod != null ? "OK" : "NG / Method not found"));
        sb.AppendLine("LightLimitChanger Prefab: " + (prefab != null ? "OK / " + AssetDatabase.GetAssetPath(prefab) : "NG / Prefab not found"));

        string variant;
        if (v2ComponentType != null || v2SetupMethod != null)
            variant = "V2 detected";
        else if (v1SettingsType != null || v1ApplyMethod != null)
            variant = "V1 detected";
        else if (prefab != null)
            variant = "unknown / prefab fallback only";
        else
            variant = "unknown / not detected";
        sb.AppendLine("LightLimitChanger Variant: " + variant);
    }

    private void AppendResolvedTypeLine(StringBuilder sb, string label, Type type)
    {
        sb.AppendLine(label + ": " + (type != null ? "OK / " + type.Assembly.GetName().Name : "NG / Type not found"));
    }

    private void AppendPackageVersion(StringBuilder sb, string packageName)
    {
        var version = TryReadPackageVersion(packageName);
        sb.AppendLine(packageName + ": " + (string.IsNullOrEmpty(version) ? "<not found>" : version));
    }

    private void AppendPrefabCandidateLine(StringBuilder sb, string label, string[] prefabNames)
    {
        var prefab = FindPrefabByNames(prefabNames);
        sb.AppendLine(label + ": " + (prefab != null ? "OK / " + AssetDatabase.GetAssetPath(prefab) : "NG / Prefab not found"));
    }

    private void AppendInstallStatus(StringBuilder sb, GameObject avatarRoot)
    {
        AppendComponentStatus(sb, avatarRoot, "AAO", AAOTypeName);
        AppendLacStatus(sb, avatarRoot);
        AppendRbsStatus(sb, avatarRoot);
        AppendComponentStatus(sb, avatarRoot, "赤夜式 撫で音", NadeSettingsTypeName);
        AppendLightLimitChangerStatus(sb, avatarRoot);
        AppendKawaiiStatus(sb, avatarRoot);
    }

    private void AppendComponentStatus(StringBuilder sb, GameObject root, string label, string typeName)
    {
        var type = FindType(typeName);
        if (type == null)
        {
            sb.AppendLine(label + ": Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(type, true).OfType<Component>().ToArray();
        sb.AppendLine(label + ": " + (comps.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + comps.Length);
        foreach (var c in comps)
            sb.AppendLine("  - " + GetPath(c.transform));
    }

    private void AppendLacStatus(StringBuilder sb, GameObject root)
    {
        var type = FindType(LACTypeName);
        if (type == null)
        {
            sb.AppendLine("LAC: Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(type, true).OfType<Component>().ToArray();
        sb.AppendLine("LAC: " + (comps.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + comps.Length);
        foreach (var c in comps)
        {
            sb.AppendLine("  - " + GetPath(c.transform));
            var presetField = type.GetField("Preset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (presetField != null)
            {
                var value = presetField.GetValue(c);
                sb.AppendLine("    Preset: " + (value != null ? value.ToString() : "<null>"));
            }
        }
    }

    private void AppendRbsStatus(StringBuilder sb, GameObject root)
    {
        var hits = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "RBS_Suimin" || t.name == "RBS_Suimin(日本語)" || t.name.Contains("RBS_Suimin-Menu"))
            .Distinct()
            .ToArray();

        sb.AppendLine("RBS: " + (hits.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + hits.Length);
        foreach (var h in hits)
            sb.AppendLine("  - " + GetPath(h));
    }

    private void AppendLightLimitChangerStatus(StringBuilder sb, GameObject root)
    {
        var comps = GetComponentsInChildrenByTypeNames(root, LLCInstalledTypeNames);
        var nameHits = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == LLCInstalledObjectName)
            .Distinct()
            .ToArray();

        bool installed = comps.Count > 0 || nameHits.Length > 0;
        sb.AppendLine("LightLimitChanger: " + (installed ? "Installed" : "Not Installed") + " / Component Count: " + comps.Count + " / Name Hit Count: " + nameHits.Length);

        foreach (var c in comps)
            sb.AppendLine("  - Component: " + c.GetType().FullName + " / " + GetPath(c.transform));
        foreach (var h in nameHits)
            sb.AppendLine("  - Name: " + GetPath(h));
    }

    private List<Component> GetComponentsInChildrenByTypeNames(GameObject root, string[] typeNames)
    {
        var result = new List<Component>();
        foreach (var typeName in typeNames)
        {
            var type = FindType(typeName);
            if (type == null) continue;
            result.AddRange(root.GetComponentsInChildren(type, true).OfType<Component>());
        }
        return result;
    }

    private void AppendKawaiiStatus(StringBuilder sb, GameObject root)
    {
        var type = FindType(KawaiiComponentTypeName);
        if (type == null)
        {
            sb.AppendLine("可愛いポーズツール: Type not found");
            return;
        }

        var comps = root.GetComponentsInChildren(type, true).OfType<Component>().ToArray();
        bool normal = comps.Any(c => c.gameObject.name == "可愛いポーズ");
        bool eight = comps.Any(c => c.gameObject.name == "可愛いポーズ(8bit・足の高さなし)");

        sb.AppendLine("可愛いポーズツール: " + (comps.Length > 0 ? "Installed" : "Not Installed") + " / Count: " + comps.Length);
        foreach (var c in comps)
            sb.AppendLine("  - " + GetPath(c.transform));
        sb.AppendLine("  可愛いポーズ: " + (normal ? "Found" : "Not Found"));
        sb.AppendLine("  可愛いポーズ(8bit・足の高さなし): " + (eight ? "Found" : "Not Found"));
    }

    private void InstallComponent(StringBuilder sb, GameObject avatarRoot, string label, string typeName, bool apply, Action<Component> configure)
    {
        var type = FindType(typeName);
        if (type == null)
        {
            sb.AppendLine(label + ": [SKIP] Type not found: " + typeName);
            return;
        }

        var exists = avatarRoot.GetComponents(type).Length > 0;
        if (exists)
        {
            sb.AppendLine(label + ": [SKIP] Already installed on avatar root");
            return;
        }

        if (!apply)
        {
            sb.AppendLine(label + ": [DRY] Add component " + typeName + " to " + avatarRoot.name);
            return;
        }

        Undo.RecordObject(avatarRoot, "Add " + label);
        var comp = Undo.AddComponent(avatarRoot, type);
        configure?.Invoke(comp);
        EditorUtility.SetDirty(avatarRoot);
        sb.AppendLine(label + ": [OK] Added component " + typeName);
    }

    private void InstallLac(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        var type = FindType(LACTypeName);
        if (type == null)
        {
            sb.AppendLine("LAC: [SKIP] Type not found: " + LACTypeName);
            return;
        }

        var existing = avatarRoot.GetComponents(type).OfType<Component>().FirstOrDefault();
        if (existing != null)
        {
            sb.AppendLine("LAC: [INFO] Already installed on avatar root");
            SetLacPreset(sb, existing, type, apply);
            return;
        }

        if (!apply)
        {
            sb.AppendLine("LAC: [DRY] Add component and set Preset=" + lacPreset);
            return;
        }

        Undo.RecordObject(avatarRoot, "Add LAC");
        var comp = Undo.AddComponent(avatarRoot, type);
        SetLacPreset(sb, comp, type, true);
        EditorUtility.SetDirty(avatarRoot);
        sb.AppendLine("LAC: [OK] Added component");
    }

    private void SetLacPreset(StringBuilder sb, Component comp, Type lacType, bool apply)
    {
        var enumType = FindType(LACPresetEnumTypeName);
        var presetField = lacType.GetField("Preset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (enumType == null || !enumType.IsEnum || presetField == null)
        {
            sb.AppendLine("LAC: [WARN] Preset enum or field not found");
            return;
        }

        var presetName = lacPreset.ToString();
        if (!Enum.GetNames(enumType).Contains(presetName))
        {
            sb.AppendLine("LAC: [WARN] Preset not available in this version: " + presetName);
            return;
        }

        if (!apply)
        {
            sb.AppendLine("LAC: [DRY] Set Preset=" + presetName);
            return;
        }

        Undo.RecordObject(comp, "Set LAC Preset");
        presetField.SetValue(comp, Enum.Parse(enumType, presetName));
        EditorUtility.SetDirty(comp);
        sb.AppendLine("LAC: [OK] Preset=" + presetName);
    }

    private void InstallLightLimitChangerOfficial(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        if (HasLightLimitChanger(avatarRoot))
        {
            sb.AppendLine("LightLimitChanger: [SKIP] Already installed");
            return;
        }

        var setupMethod = FindLightLimitChangerV2SetupMethod();
        if (setupMethod != null)
        {
            if (!apply)
            {
                sb.AppendLine("LightLimitChanger: [DRY] Call official Setup() with avatar selected (V2 style)");
                return;
            }

            if (TryInvokeLightLimitChangerSetup(sb, avatarRoot, setupMethod, null, "V2 Setup()"))
                return;
        }

        var applyMethod = FindLightLimitChangerV1ApplyMethod();
        if (applyMethod != null)
        {
            if (!apply)
            {
                sb.AppendLine("LightLimitChanger: [DRY] Call ApplytoAvatar(MenuCommand) with avatar selected (V1 style)");
                return;
            }

            if (TryInvokeLightLimitChangerSetup(sb, avatarRoot, applyMethod, new object[] { new MenuCommand(avatarRoot) }, "V1 ApplytoAvatar(MenuCommand)"))
                return;
        }

        InstallLightLimitChangerPrefabFallback(sb, avatarRoot, apply);
    }

    private bool TryInvokeLightLimitChangerSetup(StringBuilder sb, GameObject avatarRoot, MethodInfo method, object[] parameters, string label)
    {
        try
        {
            InvokeWithSelection(avatarRoot, () => method.Invoke(null, parameters));
        }
        catch (Exception ex)
        {
            sb.AppendLine("LightLimitChanger: [WARN] " + label + " failed: " + GetInvocationErrorMessage(ex));
            sb.AppendLine("LightLimitChanger: [INFO] Try next install method.");
            return false;
        }

        if (HasLightLimitChanger(avatarRoot))
        {
            sb.AppendLine("LightLimitChanger: [OK] Called " + label);
            return true;
        }

        sb.AppendLine("LightLimitChanger: [WARN] " + label + " completed but install marker was not detected on avatar.");
        sb.AppendLine("LightLimitChanger: [INFO] Try next install method.");
        return false;
    }

    private string GetInvocationErrorMessage(Exception ex)
    {
        if (ex is TargetInvocationException && ex.InnerException != null)
            return ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
        return ex.GetType().Name + ": " + ex.Message;
    }

    private MethodInfo FindLightLimitChangerV2SetupMethod()
    {
        var setupType = FindType(LLCV2ContextMenuTypeName);
        return setupType?.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
    }

    private MethodInfo FindLightLimitChangerV1ApplyMethod()
    {
        var installerType = FindType(LLCV1InstallerTypeName);
        if (installerType == null) return null;

        return installerType.GetMethod("ApplytoAvatar", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(MenuCommand) }, null)
            ?? installerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "ApplytoAvatar"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(MenuCommand)));
    }

    private static bool HasLightLimitChanger(GameObject avatarRoot)
    {
        foreach (var typeName in LLCInstalledTypeNames)
        {
            var type = FindType(typeName);
            if (type == null) continue;

            if (avatarRoot.GetComponentsInChildren(type, true).Length > 0)
                return true;
        }

        return avatarRoot.transform
            .GetComponentsInChildren<Transform>(true)
            .Any(t => t.name == LLCInstalledObjectName);
    }

    private void InstallLightLimitChangerPrefabFallback(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        var prefab = FindPrefabByNames(LLCPrefabNames);
        if (prefab == null)
        {
            sb.AppendLine("LightLimitChanger: [SKIP] V2 Setup(), V1 ApplytoAvatar(MenuCommand), and prefab were not found. Tried prefabs: " + string.Join(", ", LLCPrefabNames));
            return;
        }

        if (!PrefabHasAnyComponentType(prefab, LLCInstalledTypeNames))
        {
            sb.AppendLine("LightLimitChanger: [WARN] Prefab found but known V1/V2 component types were not detected: " + AssetDatabase.GetAssetPath(prefab));
        }

        if (!apply)
        {
            sb.AppendLine("LightLimitChanger: [DRY] Instantiate prefab " + AssetDatabase.GetAssetPath(prefab) + " under " + avatarRoot.name + " (V1 fallback)");
            return;
        }

        var instanceObj = PrefabUtility.InstantiatePrefab(prefab, avatarRoot.transform) as GameObject;
        if (instanceObj == null)
        {
            sb.AppendLine("LightLimitChanger: [ERROR] InstantiatePrefab returned null");
            return;
        }

        Undo.RegisterCreatedObjectUndo(instanceObj, "Add LightLimitChanger");
        EditorUtility.SetDirty(instanceObj);
        sb.AppendLine("LightLimitChanger: [OK] Added prefab fallback " + GetPath(instanceObj.transform));
    }

    private bool PrefabHasAnyComponentType(GameObject prefab, string[] typeNames)
    {
        if (prefab == null) return false;
        foreach (var typeName in typeNames)
        {
            var type = FindType(typeName);
            if (type != null && prefab.GetComponentInChildren(type, true) != null)
                return true;
        }
        return false;
    }

    private void InstallKawaiiOfficial(StringBuilder sb, GameObject avatarRoot, string prefabName, bool apply)
    {
        var componentType = FindType(KawaiiComponentTypeName);
        if (componentType == null)
        {
            sb.AppendLine(prefabName + ": [SKIP] KawaiiPosing type not found");
            return;
        }

        bool exists = avatarRoot.GetComponentsInChildren(componentType, true)
            .OfType<Component>()
            .Any(c => c.gameObject.name == prefabName);

        if (exists)
        {
            sb.AppendLine(prefabName + ": [SKIP] Already installed");
            return;
        }

        var menuType = FindType(PosingSystemMenuItemsTypeName);
        var addPrefab = menuType?.GetMethod("AddPrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (addPrefab == null)
        {
            sb.AppendLine(prefabName + ": [SKIP] Official AddPrefab(string) not found");
            return;
        }

        if (!apply)
        {
            sb.AppendLine(prefabName + ": [DRY] Call official AddPrefab(\"" + prefabName + "\")");
            return;
        }

        InvokeWithSelection(avatarRoot, () => addPrefab.Invoke(null, new object[] { prefabName }));
        sb.AppendLine(prefabName + ": [OK] Called official AddPrefab");
    }

    private void InstallPrefabByName(StringBuilder sb, GameObject avatarRoot, string label, string[] prefabNames, bool apply)
    {
        foreach (var name in prefabNames)
        {
            if (avatarRoot.GetComponentsInChildren<Transform>(true).Any(t => t.name == name))
            {
                sb.AppendLine(label + ": [SKIP] Already installed: " + name);
                return;
            }
        }

        var prefab = FindPrefabByNames(prefabNames);
        if (prefab == null)
        {
            sb.AppendLine(label + ": [SKIP] Prefab not found. Tried: " + string.Join(", ", prefabNames));
            return;
        }

        if (!apply)
        {
            sb.AppendLine(label + ": [DRY] Instantiate prefab " + AssetDatabase.GetAssetPath(prefab) + " under " + avatarRoot.name);
            return;
        }

        var instanceObj = PrefabUtility.InstantiatePrefab(prefab, avatarRoot.transform) as GameObject;
        if (instanceObj == null)
        {
            sb.AppendLine(label + ": [ERROR] InstantiatePrefab returned null");
            return;
        }

        Undo.RegisterCreatedObjectUndo(instanceObj, "Add " + label);
        EditorUtility.SetDirty(instanceObj);
        sb.AppendLine(label + ": [OK] Added " + GetPath(instanceObj.transform));
    }

    private GameObject FindPrefabByNames(string[] names)
    {
        foreach (var path in LLCPrefabPaths)
        {
            var explicitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (explicitPrefab != null && names.Contains(explicitPrefab.name))
                return explicitPrefab;
        }

        foreach (var name in names)
        {
            var guids = AssetDatabase.FindAssets("t:prefab " + name);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                if (prefab.name == name || names.Contains(prefab.name))
                    return prefab;
            }
        }

        return null;
    }

    private void InvokeWithSelection(GameObject avatarRoot, Action action)
    {
        var oldActive = Selection.activeObject;
        var oldObjects = Selection.objects;

        try
        {
            Selection.activeObject = avatarRoot;
            Selection.objects = new UnityEngine.Object[] { avatarRoot };
            action();
        }
        finally
        {
            Selection.activeObject = oldActive;
            Selection.objects = oldObjects;
        }
    }

    private static GameObject FindAvatarRootFlexible(GameObject selected)
    {
        if (selected == null) return null;

        var t = selected.transform;
        while (t != null)
        {
            if (t.GetComponent("VRCAvatarDescriptor") != null)
                return t.gameObject;
            t = t.parent;
        }

        var descriptorType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
        if (descriptorType != null)
        {
            var child = selected.GetComponentInChildren(descriptorType, true) as Component;
            if (child != null) return child.gameObject;
        }

        return null;
    }

    private static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            catch
            {
                // ignored
            }
        }
        return null;
    }

    private static string TryReadPackageVersion(string packageName)
    {
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/" + packageName + "/package.json");
        if (json == null) return null;

        var text = json.text;
        var marker = "\"version\"";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return "<version unknown>";

        var colon = text.IndexOf(':', index);
        var firstQuote = text.IndexOf('"', colon + 1);
        var secondQuote = text.IndexOf('"', firstQuote + 1);
        if (colon < 0 || firstQuote < 0 || secondQuote < 0) return "<version unknown>";

        return text.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    private static string GetSelectedFolderPath()
    {
        foreach (var obj in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                return path;
        }
        return null;
    }

    private static string[] GetSelectedPrefabAssetPaths()
    {
        return Selection.objects
            .Select(AssetDatabase.GetAssetPath)
            .Where(IsPrefabAssetPath)
            .Distinct()
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] FindPrefabPathsInFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            return Array.Empty<string>();

        return AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsPrefabAssetPath)
            .Distinct()
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] FindAllProjectPrefabPaths()
    {
        return AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsPrefabAssetPath)
            .Distinct()
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPrefabAssetPath(string path)
    {
        return !string.IsNullOrEmpty(path)
            && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            && AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(GameObject);
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


public class AvatarSetupLogWindow : EditorWindow
{
    private Vector2 scroll;
    private string log = "";

    public static void ShowLog(string text)
    {
        var window = GetWindow<AvatarSetupLogWindow>("Avatar Setup 詳細ログ");
        window.log = string.IsNullOrEmpty(text) ? "詳細ログはまだありません。" : text;
        window.Focus();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Avatar Setup 詳細ログ", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("ログをコピー"))
            {
                EditorGUIUtility.systemCopyBuffer = log;
            }

            if (GUILayout.Button("Consoleへ出力"))
            {
                Debug.Log(log);
            }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}
