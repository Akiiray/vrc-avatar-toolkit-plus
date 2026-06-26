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

    private enum KawaiiPoseDialogPolicy
    {
        Auto,
        AlwaysShow,
        AlwaysSkip
    }

    private static readonly GUIContent[] TargetModeLabels =
    {
        new GUIContent("Hierarchyで選択中のアバター"),
        new GUIContent("Projectで選択中のPrefab"),
        new GUIContent("Projectで選択中フォルダ内のPrefab"),
        new GUIContent("Project内の全Prefab"),
    };

    private sealed class SetupTarget
    {
        public bool IsPrefabAsset;
        public string PrefabAssetPath;
        public GameObject SceneObject;
        public string Label;
    }

    private Vector2 scroll;
    private Vector2 dryRunSummaryScroll;
    private string log = "";
    private string detailedLog = "";
    private readonly List<string> lastDryRunSummary = new List<string>();

    private TargetMode targetMode = TargetMode.SelectedHierarchyAvatars;
    private string selectedFolderPath = "";
    private bool requireAvatarDescriptor = true;
    private bool toolStatusChecked = false;
    private string avatarInstallStatusTargetKey = "";
    private List<GameObject> hierarchyAvatarSlots = new List<GameObject>();
    private List<SetupTarget> cachedTargets = new List<SetupTarget>();
    private string cachedTargetKey = "";
    private bool targetCacheDirty = true;
    private bool lastTargetScanCanceled = false;
    private readonly Dictionary<string, string> kawaiiPrefabPathCache = new Dictionary<string, string>();

    private bool addAAO = true;
    private bool addLAC = true;
    private LacPresetMode lacPreset = LacPresetMode.HighQuality;
    private bool addRBS = true;
    private bool addNadeSystem = true;
    private bool addLightLimitChanger = true;
    private bool addKawaiiNormal = false;
    private bool addKawaii8bitNoFoot = true;
    private KawaiiPoseInstallMode allInstallKawaiiMode = KawaiiPoseInstallMode.EightBitNoFoot;
    private KawaiiPoseDialogPolicy kawaiiPoseDialogPolicy = KawaiiPoseDialogPolicy.Auto;

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
    private static readonly string[] RBSPrefabNames =
    {
        "RBS_Suimin(日本語)",
        "RBS_Suimin",
        "RBS_Suimin-Menu",
        "RBS_Suimin-Menu-ja_JP",
    };
    private static readonly string[] RBSInstalledNameKeywords =
    {
        "RBS_Suimin",
    };

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/導入ウィンドウ", false, 0)]
    public static void Open()
    {
        ShowAvatarSetupWindow();
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Avatar Setup/Window", false, 1)]
    public static void OpenWindowAlias()
    {
        ShowAvatarSetupWindow();
    }

    private static AkiirayAvatarSetupTool ShowAvatarSetupWindow()
    {
        var window = GetWindow<AkiirayAvatarSetupTool>("VRC Avatar Toolkit Plus - Avatar Setup");
        window.minSize = new Vector2(520, 620);
        window.InitializeHierarchySlotsFromSelectionIfNeeded();
        window.Show();
        window.Focus();
        return window;
    }

    private void OnEnable()
    {
        InitializeHierarchySlotsFromSelectionIfNeeded();
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add AAO Only")]
    public static void MenuAddAAOOnly() { OpenAndRunPreset(addAAO: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add LAC Only")]
    public static void MenuAddLACOnly() { OpenAndRunPreset(addLAC: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add RBS Suimin V2 Only")]
    public static void MenuAddRBSOnly() { OpenAndRunPreset(addRBS: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add Nade System Only")]
    public static void MenuAddNadeOnly() { OpenAndRunPreset(addNadeSystem: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add LightLimitChanger Only")]
    public static void MenuAddLightLimitChangerOnly() { OpenAndRunPreset(addLightLimitChanger: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add Kawaii Pose Only/可愛いポーズ")]
    public static void MenuAddKawaiiNormalOnly() { OpenAndRunPreset(addKawaiiNormal: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Add Kawaii Pose Only/可愛いポーズ(8bit・足の高さなし)")]
    public static void MenuAddKawaii8bitOnly() { OpenAndRunPreset(addKawaii8bitNoFoot: true); }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/クイック導入/Setup All")]
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
        var window = ShowAvatarSetupWindow();
        window.addAAO = addAAO;
        window.addLAC = addLAC;
        window.addRBS = addRBS;
        window.addNadeSystem = addNadeSystem;
        window.addLightLimitChanger = addLightLimitChanger;
        window.addKawaiiNormal = addKawaiiNormal;
        window.addKawaii8bitNoFoot = addKawaii8bitNoFoot;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("VRC Avatar Toolkit Plus - Avatar Setup", EditorStyles.boldLabel);

        var previewTargets = DrawTargetArea();
        DrawDependencyStatusCards();
        DrawInstallOptionsAndDryRun(previewTargets);
        DrawDetailedLog();
    }

    private List<SetupTarget> DrawTargetArea()
    {
        List<SetupTarget> previewTargets;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("対象選択・操作", EditorStyles.boldLabel);
            DrawTargetModePopup();
            EditorGUI.BeginChangeCheck();
            requireAvatarDescriptor = EditorGUILayout.ToggleLeft("VRCAvatarDescriptorがあるPrefab/Hierarchyだけ対象", requireAvatarDescriptor);
            if (EditorGUI.EndChangeCheck())
                InvalidateTargetCache(clearTargets: targetMode == TargetMode.SelectedProjectFolderPrefabs || targetMode == TargetMode.AllProjectPrefabs);

            if (targetMode == TargetMode.SelectedProjectFolderPrefabs)
            {
                EditorGUILayout.LabelField("対象フォルダ:", string.IsNullOrEmpty(selectedFolderPath) ? "<未指定>" : selectedFolderPath);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("選択中フォルダを対象にする"))
                    {
                        selectedFolderPath = GetSelectedFolderPath() ?? "";
                        InvalidateTargetCache(clearTargets: true);
                    }
                    if (GUILayout.Button("対象Prefabを走査"))
                        RefreshTargetCache();
                    if (GUILayout.Button("対象フォルダをクリア"))
                    {
                        selectedFolderPath = "";
                        InvalidateTargetCache(clearTargets: true);
                    }
                }
            }
            else if (targetMode == TargetMode.AllProjectPrefabs)
            {
                if (GUILayout.Button("対象Prefabを走査"))
                    RefreshTargetCache();
            }

            if (targetMode == TargetMode.SelectedHierarchyAvatars)
                DrawHierarchyAvatarSlots();

            previewTargets = GetCachedTargets();
            DrawDetectedTargets(previewTargets);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("導入状態チェック", GUILayout.Height(28)))
                {
                    toolStatusChecked = true;
                    detailedLog = Run(false, true);
                    log = BuildConciseLog(detailedLog);
                }

                if (GUILayout.Button("すべて導入を選択", GUILayout.Height(28)))
                    SelectAllInstallOptions();

                if (GUILayout.Button("すべて解除", GUILayout.Height(28)))
                    ClearInstallOptions();
            }
        }

        return previewTargets;
    }

    private void DrawDependencyStatusCards()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("ツール導入状態", EditorStyles.boldLabel);
            if (!toolStatusChecked)
                EditorGUILayout.HelpBox("導入状態チェックを実行すると、各ツールの導入状態をカードに表示します。", MessageType.Info);

            var cards = new[]
            {
                new { Name = "AAO", Status = GetTypeToolDependencyState(AAOTypeName) },
                new { Name = "LAC", Status = GetTypeToolDependencyState(LACTypeName) },
                new { Name = "RBS", Status = GetPrefabToolDependencyState(RBSPrefabNames) },
                new { Name = "赤夜式 撫で音", Status = GetTypeToolDependencyState(NadeSettingsTypeName) },
                new { Name = "LightLimitChanger", Status = GetLightLimitChangerDependencyState() },
                new { Name = "可愛いポーズ", Status = GetTypeToolDependencyState(KawaiiComponentTypeName) },
            };

            int columns = position.width >= 760f ? 3 : 2;
            for (int i = 0; i < cards.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns; c++)
                    {
                        int index = i + c;
                        if (index >= cards.Length)
                        {
                            GUILayout.FlexibleSpace();
                            continue;
                        }

                        DrawDependencyStatusCard(cards[index].Name, cards[index].Status);
                    }
                }
            }
        }
    }

    private void DrawDependencyStatusCard(string toolName, string status)
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.MinWidth(150), GUILayout.ExpandWidth(true)))
        {
            EditorGUILayout.LabelField(toolName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(SanitizeStatusForUi(status), EditorStyles.wordWrappedLabel);
        }
    }

    private string SanitizeStatusForUi(string status)
    {
        if (string.IsNullOrEmpty(status))
            return "未判定";

        return status.Replace("Type not found", "ツール未導入")
            .Replace("型が見つかりません", "ツール未導入")
            .Replace("○ ", "")
            .Replace("× ", "");
    }

    private void DrawInstallOptionsAndDryRun(List<SetupTarget> previewTargets)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(300), GUILayout.ExpandWidth(true)))
                    DrawInstallOptions(previewTargets);

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(220f, position.width * 0.36f))))
                    DrawDryRunSummary();
            }

            bool hasTargets = (previewTargets != null && previewTargets.Count > 0) || CanRefreshTargetsForRun();
            if (!hasTargets)
                EditorGUILayout.HelpBox("導入対象のアバターが未選択です。ボタンの赤いアイコンは、実行対象がないため処理できない状態を示します。", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawRunButton("導入テスト（DryRun）", hasTargets, 34))
                {
                    toolStatusChecked = true;
                    avatarInstallStatusTargetKey = GetSingleAvatarInstallStatusTargetKey(previewTargets);
                    detailedLog = Run(false, false);
                    log = BuildConciseLog(detailedLog);
                    UpdateLastDryRunSummary(detailedLog);
                }

                if (DrawRunButton("導入実行", hasTargets, 34))
                {
                    toolStatusChecked = true;
                    avatarInstallStatusTargetKey = GetSingleAvatarInstallStatusTargetKey(previewTargets);
                    detailedLog = Run(true, false);
                    log = BuildConciseLog(detailedLog);
                }
            }
        }
    }

    private void DrawInstallOptions(List<SetupTarget> previewTargets)
    {
        EditorGUILayout.LabelField("個別導入設定", EditorStyles.boldLabel);
        DrawSingleAvatarInstallStatus(previewTargets);
        allInstallKawaiiMode = (KawaiiPoseInstallMode)EditorGUILayout.EnumPopup("すべて導入時の可愛いポーズ", allInstallKawaiiMode);
        kawaiiPoseDialogPolicy = (KawaiiPoseDialogPolicy)EditorGUILayout.EnumPopup(new GUIContent("可愛いポーズ導入オプション", "Auto: 単体導入では表示、一括導入ではスキップ / AlwaysShow: 常に公式確認を表示 / AlwaysSkip: 常に公式確認をスキップ"), kawaiiPoseDialogPolicy);
        EditorGUILayout.HelpBox(GetKawaiiPoseDialogPolicyDescription(), MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope())
            {
                addAAO = EditorGUILayout.ToggleLeft("AAO / TraceAndOptimize", addAAO);
                addLAC = EditorGUILayout.ToggleLeft("LAC / TextureCompressor", addLAC);
                using (new EditorGUI.DisabledScope(!addLAC))
                    lacPreset = (LacPresetMode)EditorGUILayout.EnumPopup("LAC Preset", lacPreset);
                addRBS = EditorGUILayout.ToggleLeft("RBS 睡眠システム Ver2", addRBS);
                addNadeSystem = EditorGUILayout.ToggleLeft("赤夜式 撫で音ギミック", addNadeSystem);
            }

            using (new EditorGUILayout.VerticalScope())
            {
                addLightLimitChanger = EditorGUILayout.ToggleLeft("LightLimitChanger", addLightLimitChanger);
                addKawaiiNormal = EditorGUILayout.ToggleLeft("可愛いポーズ", addKawaiiNormal);
                addKawaii8bitNoFoot = EditorGUILayout.ToggleLeft("可愛いポーズ(8bit・足の高さなし)", addKawaii8bitNoFoot);
            }
        }
    }

    private string GetKawaiiPoseDialogPolicyDescription()
    {
        switch (kawaiiPoseDialogPolicy)
        {
            case KawaiiPoseDialogPolicy.AlwaysShow:
                return "AlwaysShow: 常に公式確認を表示";
            case KawaiiPoseDialogPolicy.AlwaysSkip:
                return "AlwaysSkip: 常に公式確認をスキップ。スキップ時は公式のプレビルド確認およびアバター個別プリセット適用を行わず、Prefabのみ追加します。";
            default:
                return "Auto: 単体導入では表示、一括導入ではスキップ。スキップ時は公式のプレビルド確認およびアバター個別プリセット適用を行わず、Prefabのみ追加します。";
        }
    }

    private void DrawDryRunSummary()
    {
        EditorGUILayout.LabelField("前回のDryRun結果", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box", GUILayout.MinHeight(135)))
        {
            if (lastDryRunSummary.Count == 0)
            {
                EditorGUILayout.LabelField("まだDryRunは実行されていません", EditorStyles.wordWrappedLabel);
                return;
            }

            dryRunSummaryScroll = EditorGUILayout.BeginScrollView(dryRunSummaryScroll, GUILayout.MinHeight(110));
            foreach (var line in lastDryRunSummary)
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawDetailedLog()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("詳細ログ", EditorStyles.boldLabel);
                if (GUILayout.Button("クリップボードコピー", GUILayout.Width(150)))
                    EditorGUIUtility.systemCopyBuffer = string.IsNullOrEmpty(detailedLog) ? log : detailedLog;
                if (GUILayout.Button("別ウィンドウで表示", GUILayout.Width(150)))
                    AvatarSetupLogWindow.ShowLog(string.IsNullOrEmpty(detailedLog) ? log : detailedLog);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    private bool DrawRunButton(string label, bool hasTargets, int height)
    {
        if (hasTargets)
            return GUILayout.Button(label, GUILayout.Height(height));

        var errorIcon = EditorGUIUtility.IconContent("console.erroricon").image;
        var content = new GUIContent(" " + label, errorIcon, "導入対象のアバターが未選択です");
        return GUILayout.Button(content, GUILayout.Height(height));
    }

    private bool CanRefreshTargetsForRun()
    {
        if (targetMode == TargetMode.SelectedProjectFolderPrefabs)
            return !string.IsNullOrEmpty(selectedFolderPath) && AssetDatabase.IsValidFolder(selectedFolderPath);
        if (targetMode == TargetMode.AllProjectPrefabs)
            return true;
        return false;
    }

    private void DrawDependencyOverview()
    {
        EditorGUILayout.LabelField("AAO", GetTypeToolDependencyState(AAOTypeName));
        EditorGUILayout.LabelField("LAC", GetTypeToolDependencyState(LACTypeName));
        EditorGUILayout.LabelField("RBS", GetPrefabToolDependencyState(RBSPrefabNames));
        EditorGUILayout.LabelField("赤夜式 撫で音", GetTypeToolDependencyState(NadeSettingsTypeName));
        EditorGUILayout.LabelField("LightLimitChanger", GetLightLimitChangerDependencyState());
        EditorGUILayout.LabelField("可愛いポーズ", GetTypeToolDependencyState(KawaiiComponentTypeName));
    }

    private void DrawTargetModePopup()
    {
        EditorGUI.BeginChangeCheck();
        targetMode = (TargetMode)EditorGUILayout.Popup(new GUIContent("対象モード"), (int)targetMode, TargetModeLabels);
        if (EditorGUI.EndChangeCheck())
            InvalidateTargetCache(clearTargets: true);
    }

    private void DrawHierarchyAvatarSlots()
    {
        EnsureHierarchyAvatarSlots();
        if (hierarchyAvatarSlots.Count == 0)
            hierarchyAvatarSlots.Add(null);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("検出対象（Hierarchyのアバター）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("枠にHierarchy上のアバター、またはその子オブジェクトを入れてください。VRCAvatarDescriptorが見つかる親アバターを導入対象として扱います。", MessageType.Info);

        for (int i = 0; i < hierarchyAvatarSlots.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                EditorGUILayout.LabelField((i + 1) + "体目", EditorStyles.boldLabel, GUILayout.Width(48));
                EditorGUI.BeginChangeCheck();
                hierarchyAvatarSlots[i] = (GameObject)EditorGUILayout.ObjectField(hierarchyAvatarSlots[i], typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
                    InvalidateTargetCache();

                using (new EditorGUI.DisabledScope(hierarchyAvatarSlots.Count <= 1))
                {
                    if (GUILayout.Button("−", GUILayout.Width(28)))
                    {
                        hierarchyAvatarSlots.RemoveAt(i);
                        InvalidateTargetCache();
                        GUI.FocusControl(null);
                        break;
                    }
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("＋ 対象枠を追加"))
            {
                hierarchyAvatarSlots.Add(null);
                InvalidateTargetCache();
            }
            if (GUILayout.Button("選択中のHierarchyを追加"))
            {
                AddSelectedHierarchyAvatarsToSlots();
                InvalidateTargetCache();
            }
        }
    }

    private void DrawDetectedTargets(List<SetupTarget> previewTargets)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("検出された導入対象", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("対象数", previewTargets.Count.ToString());

        if (previewTargets.Count == 0)
        {
            EditorGUILayout.HelpBox("導入対象はまだ検出されていません。対象モードに合わせてHierarchyのアバター、ProjectのPrefab、またはProjectフォルダを選択してください。", MessageType.Warning);
            return;
        }

        foreach (var t in previewTargets.Take(5))
            EditorGUILayout.LabelField("● " + t.Label);
        if (previewTargets.Count > 5)
            EditorGUILayout.LabelField("...他 " + (previewTargets.Count - 5) + " 件");
    }

    private void DrawSingleAvatarInstallStatus(List<SetupTarget> previewTargets)
    {
        var statusTargetKey = GetSingleAvatarInstallStatusTargetKey(previewTargets);
        if (string.IsNullOrEmpty(statusTargetKey) || statusTargetKey != avatarInstallStatusTargetKey)
            return;

        var avatarRoot = GetAvatarRootForInstallStatus(previewTargets[0]);
        if (avatarRoot == null)
            return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("選択アバターの導入状態", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("AAO", GetAvatarComponentInstallState(avatarRoot, AAOTypeName));
        EditorGUILayout.LabelField("LAC", GetAvatarComponentInstallState(avatarRoot, LACTypeName));
        EditorGUILayout.LabelField("RBS", GetRbsAvatarInstallState(avatarRoot));
        EditorGUILayout.LabelField("赤夜式 撫で音", GetAvatarComponentInstallState(avatarRoot, NadeSettingsTypeName));
        EditorGUILayout.LabelField("LightLimitChanger", GetLightLimitChangerAvatarInstallState(avatarRoot));
        EditorGUILayout.LabelField("可愛いポーズ", GetAvatarKawaiiInstallState(avatarRoot, "可愛いポーズ"));
        EditorGUILayout.LabelField("可愛いポーズ(8bit・足の高さなし)", GetAvatarKawaiiInstallState(avatarRoot, "可愛いポーズ(8bit・足の高さなし)"));
        EditorGUILayout.Space(4);
    }

    private string GetSingleAvatarInstallStatusTargetKey(List<SetupTarget> previewTargets)
    {
        if (previewTargets == null || previewTargets.Count != 1)
            return "";
        if (targetMode != TargetMode.SelectedHierarchyAvatars && targetMode != TargetMode.SelectedProjectPrefabAssets)
            return "";

        var target = previewTargets[0];
        if (target.IsPrefabAsset)
            return "prefab:" + target.PrefabAssetPath;
        return target.SceneObject != null ? "hierarchy:" + target.SceneObject.GetInstanceID() : "";
    }

    private GameObject GetAvatarRootForInstallStatus(SetupTarget target)
    {
        if (target == null)
            return null;

        if (target.IsPrefabAsset)
        {
            var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(target.PrefabAssetPath);
            return FindAvatarRootFlexible(prefabRoot);
        }

        return FindAvatarRootFlexible(target.SceneObject);
    }

    private string GetAvatarComponentInstallState(GameObject avatarRoot, string typeName)
    {
        var type = FindType(typeName);
        if (type == null)
            return FormatToolNotInstalledStatus();

        var count = avatarRoot.GetComponentsInChildren(type, true).Length;
        return FormatAvatarInstallStatus(count > 0) + " / 数: " + count;
    }

    private string GetAvatarKawaiiInstallState(GameObject avatarRoot, string objectName)
    {
        var type = FindType(KawaiiComponentTypeName);
        if (type == null)
            return FormatToolNotInstalledStatus();

        var found = avatarRoot.GetComponentsInChildren(type, true)
            .OfType<Component>()
            .Any(c => c.gameObject.name == objectName);
        return FormatAvatarInstallStatus(found);
    }

    private string GetRbsAvatarInstallState(GameObject avatarRoot)
    {
        if (!IsRbsToolAvailable())
            return FormatToolNotInstalledStatus();

        return FormatAvatarInstallStatus(HasRbsInstalled(avatarRoot));
    }

    private string GetLightLimitChangerAvatarInstallState(GameObject avatarRoot)
    {
        var variant = GetLightLimitChangerToolVariant();
        if (variant == LightLimitChangerVariant.NotInstalled)
            return FormatToolNotInstalledStatus();

        return FormatAvatarInstallStatus(HasLightLimitChanger(avatarRoot)) + FormatLightLimitChangerVariantSuffix(variant);
    }

    private string FormatAvatarInstallStatus(bool installed)
    {
        return installed ? "○ 導入済" : "× 未導入";
    }

    private string FormatToolNotInstalledStatus()
    {
        return "× ツール未導入";
    }

    private static bool HasRbsInstalled(GameObject avatarRoot)
    {
        if (avatarRoot == null) return false;
        return avatarRoot.GetComponentsInChildren<Transform>(true)
            .Any(t => RBSPrefabNames.Contains(t.name) || RBSInstalledNameKeywords.Any(keyword => t.name.Contains(keyword)));
    }

    private void InitializeHierarchySlotsFromSelectionIfNeeded()
    {
        EnsureHierarchyAvatarSlots();
        if (hierarchyAvatarSlots.Count > 0)
            return;

        AddSelectedHierarchyAvatarsToSlots();
        if (hierarchyAvatarSlots.Count == 0)
            hierarchyAvatarSlots.Add(null);
    }

    private void EnsureHierarchyAvatarSlots()
    {
        if (hierarchyAvatarSlots == null)
            hierarchyAvatarSlots = new List<GameObject>();
    }

    private void AddSelectedHierarchyAvatarsToSlots()
    {
        EnsureHierarchyAvatarSlots();

        var selectedRoots = Selection.gameObjects
            .Where(x => x != null && !EditorUtility.IsPersistent(x))
            .Select(FindAvatarRootFlexible)
            .Where(x => x != null)
            .Distinct()
            .ToArray();

        foreach (var root in selectedRoots)
        {
            if (hierarchyAvatarSlots.Contains(root))
                continue;

            var emptyIndex = hierarchyAvatarSlots.FindIndex(x => x == null);
            if (emptyIndex >= 0)
                hierarchyAvatarSlots[emptyIndex] = root;
            else
                hierarchyAvatarSlots.Add(root);
        }
    }

    private string GetTypeToolDependencyState(string typeName)
    {
        return FormatToolDependencyStatus(FindType(typeName) != null);
    }

    private string GetPrefabToolDependencyState(string[] prefabNames)
    {
        return FormatToolDependencyStatus(FindPrefabByNames(prefabNames) != null);
    }

    private string FormatToolDependencyStatus(bool installed)
    {
        if (!toolStatusChecked)
            return "未判定";
        return installed ? "○ 導入済み" : FormatToolNotInstalledStatus();
    }

    private string GetLightLimitChangerDependencyState()
    {
        if (!toolStatusChecked)
            return "未判定";

        var variant = GetLightLimitChangerToolVariant();
        if (variant == LightLimitChangerVariant.NotInstalled)
            return FormatToolNotInstalledStatus();

        return "○ 導入済み" + FormatLightLimitChangerVariantSuffix(variant);
    }

    private void UpdateLastDryRunSummary(string detail)
    {
        lastDryRunSummary.Clear();

        if (string.IsNullOrEmpty(detail))
            return;

        var labels = new[]
        {
            "AAO",
            "LAC",
            "RBS",
            "赤夜式 撫で音",
            "LightLimitChanger",
            "可愛いポーズ",
            "可愛いポーズ(8bit・足の高さなし)",
        };

        foreach (var label in labels)
        {
            var summary = FindDryRunSummaryForLabel(detail, label);
            if (!string.IsNullOrEmpty(summary))
                lastDryRunSummary.Add(label + ": " + summary);
        }

        if (lastDryRunSummary.Count == 0)
            lastDryRunSummary.Add("DryRunは完了しました（詳細は詳細ログを確認してください）");
    }

    private string FindDryRunSummaryForLabel(string detail, string label)
    {
        var lines = detail.Split(new[] { '\n' }, StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith(label + ":", StringComparison.Ordinal))
                continue;

            if (line.Contains("[DRY]"))
                return "追加予定";
            if (line.Contains("[SKIP] ツール未導入") || line.Contains("Tool not installed"))
                return "ツール未導入";
            if (line.Contains("導入未選択"))
                return "導入未選択";
            if (line.Contains("Already installed") || line.Contains("[INFO] Already installed"))
                return "導入済みのためスキップ";
            if (line.Contains("Not Installed"))
                return "未導入";
            if (line.Contains("Installed"))
                return "導入済み";
        }

        if (label == "LightLimitChanger")
        {
            var variant = GetLightLimitChangerToolVariant();
            if (variant == LightLimitChangerVariant.V1)
                return "導入済み(V1)";
            if (variant == LightLimitChangerVariant.V2)
                return "導入済み(V2)";
        }

        return "";
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
            else if (line.Contains("[OK]") || line.Contains("[SKIP]") || line.Contains("[DRY]") || line.Contains("[INFO]") || line.Contains("[WARN]") || line.Contains("[ERROR]"))
            {
                sb.AppendLine(ToJapaneseResultLine(line));
            }
            else if (line.StartsWith("LightLimitChanger Variant: ", StringComparison.Ordinal))
            {
                sb.AppendLine("LightLimitChanger判定: " + ToJapaneseLlcVariant(line.Substring(27)));
            }
            else if (line.Contains(": Installed") || line.Contains(": Not Installed") || line.Contains(": Type not found") || line.Contains(": Tool not installed"))
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
        return line.Replace("Tool not installed", "ツール未導入")
            .Replace("Type not found", "ツール未導入")
            .Replace("Not Installed", "未導入")
            .Replace("Installed", "導入済")
            .Replace("Count", "数")
            .Replace("Component 数", "コンポーネント数")
            .Replace("Name Hit 数", "名前一致数");
    }

    private string ToJapaneseResultLine(string line)
    {
        string prefix = null;
        if (line.Contains("[OK]")) prefix = "成功";
        else if (line.Contains("[SKIP]")) prefix = "スキップ";
        else if (line.Contains("[DRY]")) prefix = "テスト";
        else if (line.Contains("[INFO]")) prefix = "情報";
        else if (line.Contains("[WARN]")) prefix = "注意";
        else if (line.Contains("[ERROR]")) prefix = "エラー";

        var translated = line.Replace("[OK]", "")
            .Replace("[SKIP]", "")
            .Replace("[DRY]", "")
            .Replace("[INFO]", "")
            .Replace("[WARN]", "")
            .Replace("[ERROR]", "")
            .Replace("Already installed on avatar root", "アバター直下に既に導入済み")
            .Replace("Already installed", "既に導入済み")
            .Replace("Type not found", "ツール未導入")
            .Replace("Prefab not found", "Prefabが見つかりません")
            .Replace("Official AddPrefab(string) not found", "公式AddPrefab(string)が見つかりません")
            .Replace("Add component", "コンポーネント追加")
            .Replace("Added component", "コンポーネントを追加しました")
            .Replace("Instantiate prefab", "Prefabを追加予定")
            .Replace("Added prefab fallback", "Prefabフォールバックで追加しました")
            .Replace("Called", "呼び出しました")
            .Replace("Call", "呼び出し予定")
            .Replace("Saved prefab", "Prefabを保存しました")
            .Replace("DryRun can add it normally", "DryRunでは正常に追加できそうです")
            .Replace("Not installed on avatar", "アバターに未導入")
            .Trim();

        while (translated.Contains("  "))
            translated = translated.Replace("  ", " ");

        return string.IsNullOrEmpty(prefix) ? translated : prefix + ": " + translated;
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
        kawaiiPrefabPathCache.Clear();
        var targets = RefreshTargetCache();

        sb.AppendLine("# Akiiray Avatar Setup Report");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Unity: " + Application.unityVersion);
        sb.AppendLine("Mode: " + (checkOnly ? "Check Only" : (apply ? "Apply" : "Dry Run")));
        sb.AppendLine("TargetMode: " + targetMode);
        sb.AppendLine("TargetCount: " + targets.Count);
        if (lastTargetScanCanceled)
            sb.AppendLine("対象Prefab走査をキャンセルしました。");
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
            RunForTarget(sb, target, apply, checkOnly, targets.Count);
            sb.AppendLine();
        }

        if (apply)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        return sb.ToString();
    }

    private void RunForTarget(StringBuilder sb, SetupTarget target, bool apply, bool checkOnly, int totalTargetCount)
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

                    RunForAvatarRoot(sb, avatarRoot, apply: true, checkOnly: checkOnly, isPrefabAsset: true, totalTargetCount: totalTargetCount);
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

                RunForAvatarRoot(sb, avatarRoot, apply: false, checkOnly: checkOnly, isPrefabAsset: true, totalTargetCount: totalTargetCount);
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

            RunForAvatarRoot(sb, avatarRoot, apply, checkOnly, isPrefabAsset: false, totalTargetCount: totalTargetCount);
        }
    }

    private void RunForAvatarRoot(StringBuilder sb, GameObject avatarRoot, bool apply, bool checkOnly, bool isPrefabAsset, int totalTargetCount)
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
            RunInstallOrSkip(sb, avatarRoot, "AAO", addAAO, IsTypeAvailable(AAOTypeName), () => InstallComponent(sb, avatarRoot, "AAO", AAOTypeName, apply, null));
            RunInstallOrSkip(sb, avatarRoot, "LAC", addLAC, IsTypeAvailable(LACTypeName), () => InstallLac(sb, avatarRoot, apply));
            RunInstallOrSkip(sb, avatarRoot, "RBS", addRBS, IsRbsToolAvailable(), () => InstallPrefabByName(sb, avatarRoot, "RBS", RBSPrefabNames, apply));
            RunInstallOrSkip(sb, avatarRoot, "赤夜式 撫で音", addNadeSystem, IsTypeAvailable(NadeSettingsTypeName), () => InstallPrefabByName(sb, avatarRoot, "赤夜式 撫で音", new[] { "NadeSystem" }, apply));
            RunInstallOrSkip(sb, avatarRoot, "LightLimitChanger", addLightLimitChanger, IsLightLimitChangerToolAvailable(), () => InstallLightLimitChangerOfficial(sb, avatarRoot, apply));
            RunInstallOrSkip(sb, avatarRoot, "可愛いポーズ", addKawaiiNormal, IsTypeAvailable(KawaiiComponentTypeName), () => InstallKawaiiOfficial(sb, avatarRoot, "可愛いポーズ", apply, ShouldSkipKawaiiOfficialDialog(totalTargetCount)));
            RunInstallOrSkip(sb, avatarRoot, "可愛いポーズ(8bit・足の高さなし)", addKawaii8bitNoFoot, IsTypeAvailable(KawaiiComponentTypeName), () => InstallKawaiiOfficial(sb, avatarRoot, "可愛いポーズ(8bit・足の高さなし)", apply, ShouldSkipKawaiiOfficialDialog(totalTargetCount)));

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

    private void RunInstallOrSkip(StringBuilder sb, GameObject avatarRoot, string label, bool selected, bool toolAvailable, Action installAction)
    {
        if (!toolAvailable)
        {
            sb.AppendLine(label + ": [SKIP] ツール未導入");
            return;
        }

        if (!selected)
        {
            sb.AppendLine(label + ": [SKIP] 導入未選択");
            return;
        }

        installAction();
    }

    private bool IsTypeAvailable(string typeName)
    {
        return FindType(typeName) != null;
    }

    private enum LightLimitChangerVariant
    {
        NotInstalled,
        V1,
        V2,
        PrefabOnly
    }

    private bool IsRbsToolAvailable()
    {
        return FindPrefabByNames(RBSPrefabNames) != null;
    }

    private bool IsLightLimitChangerToolAvailable()
    {
        return GetLightLimitChangerToolVariant() != LightLimitChangerVariant.NotInstalled;
    }

    private LightLimitChangerVariant GetLightLimitChangerToolVariant()
    {
        if (FindType(LLCV2ComponentTypeName) != null)
            return LightLimitChangerVariant.V2;
        if (FindType(LLCV1SettingsTypeName) != null)
            return LightLimitChangerVariant.V1;
        if (FindPrefabByNames(LLCPrefabNames) != null)
            return LightLimitChangerVariant.PrefabOnly;
        return LightLimitChangerVariant.NotInstalled;
    }

    private string FormatLightLimitChangerVariantSuffix(LightLimitChangerVariant variant)
    {
        if (variant == LightLimitChangerVariant.V2) return "（V2）";
        if (variant == LightLimitChangerVariant.V1) return "（V1）";
        if (variant == LightLimitChangerVariant.PrefabOnly) return "（Prefabのみ）";
        return "";
    }

    private void InvalidateTargetCache(bool clearTargets = false)
    {
        targetCacheDirty = true;
        if (clearTargets)
        {
            cachedTargets.Clear();
            cachedTargetKey = "";
        }
    }

    private List<SetupTarget> GetCachedTargets()
    {
        var key = BuildTargetCacheKey();
        if (targetCacheDirty || cachedTargetKey != key)
        {
            if (targetMode == TargetMode.SelectedHierarchyAvatars || targetMode == TargetMode.SelectedProjectPrefabAssets)
                return RefreshTargetCache();

            return new List<SetupTarget>();
        }

        return cachedTargets ?? (cachedTargets = new List<SetupTarget>());
    }

    private List<SetupTarget> RefreshTargetCache()
    {
        lastTargetScanCanceled = false;
        cachedTargets = BuildTargets();
        cachedTargetKey = BuildTargetCacheKey();
        targetCacheDirty = false;
        return cachedTargets;
    }

    private string BuildTargetCacheKey()
    {
        var sb = new StringBuilder();
        sb.Append(targetMode).Append('|').Append(requireAvatarDescriptor).Append('|').Append(selectedFolderPath);
        if (targetMode == TargetMode.SelectedHierarchyAvatars)
        {
            EnsureHierarchyAvatarSlots();
            foreach (var go in hierarchyAvatarSlots)
                sb.Append('|').Append(go != null ? go.GetInstanceID().ToString() : "null");
        }
        else if (targetMode == TargetMode.SelectedProjectPrefabAssets)
        {
            foreach (var path in GetSelectedPrefabAssetPaths())
                sb.Append('|').Append(path);
        }
        return sb.ToString();
    }

    private List<SetupTarget> BuildTargets()
    {
        var list = new List<SetupTarget>();

        if (targetMode == TargetMode.SelectedHierarchyAvatars)
        {
            EnsureHierarchyAvatarSlots();
            if (hierarchyAvatarSlots.Count == 0)
                hierarchyAvatarSlots.Add(null);

            foreach (var go in hierarchyAvatarSlots.Where(x => x != null && !EditorUtility.IsPersistent(x)).Distinct())
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
            var folder = selectedFolderPath;
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                AddPrefabTargetsWithProgress(list, FindPrefabPathsInFolder(folder), "対象Prefab走査", folder);
            }
        }
        else if (targetMode == TargetMode.AllProjectPrefabs)
        {
            AddPrefabTargetsWithProgress(list, FindAllProjectPrefabPaths(), "対象Prefab走査", "Project内の全Prefab");
        }

        return list;
    }

    private void AddPrefabTargetsWithProgress(List<SetupTarget> list, string[] paths, string title, string info)
    {
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths.Length > 25 && EditorUtility.DisplayCancelableProgressBar(title, info + "\n" + paths[i], (float)i / paths.Length))
                {
                    lastTargetScanCanceled = true;
                    detailedLog = "対象Prefab走査をキャンセルしました。\n";
                    log = detailedLog;
                    break;
                }

                AddPrefabTargetIfValid(list, paths[i]);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
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
        AppendPrefabCandidateLine(sb, "RBS Prefab", RBSPrefabNames);
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
        sb.AppendLine(label + ": " + (type != null ? "OK / " + type.Assembly.GetName().Name : "NG / Tool not installed"));
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
        if (v2ComponentType != null)
            variant = "V2 detected";
        else if (v1SettingsType != null)
            variant = "V1 detected";
        else if (prefab != null)
            variant = "unknown / prefab fallback only";
        else
            variant = "unknown / not detected";
        sb.AppendLine("LightLimitChanger Variant: " + variant);
    }

    private void AppendResolvedTypeLine(StringBuilder sb, string label, Type type)
    {
        sb.AppendLine(label + ": " + (type != null ? "OK / " + type.Assembly.GetName().Name : "NG / Tool not installed"));
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
            sb.AppendLine(label + ": Tool not installed");
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
            sb.AppendLine("LAC: Tool not installed");
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
            .Where(t => RBSPrefabNames.Contains(t.name) || RBSInstalledNameKeywords.Any(keyword => t.name.Contains(keyword)))
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
            sb.AppendLine("可愛いポーズツール: Tool not installed");
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
            sb.AppendLine(label + ": [SKIP] ツール未導入");
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
            sb.AppendLine("LAC: [SKIP] ツール未導入");
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

        if (!apply && IsLightLimitChangerToolAvailable())
            sb.AppendLine("LightLimitChanger: [INFO] Not installed on avatar / DryRun can add it normally");

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

    private static bool HasLightLimitChanger(GameObject avatarRoot)
    {
        if (avatarRoot == null) return false;

        foreach (var typeName in LLCInstalledTypeNames)
        {
            var type = FindType(typeName);
            if (type != null && avatarRoot.GetComponentsInChildren(type, true).Length > 0)
                return true;
        }

        return avatarRoot.GetComponentsInChildren<Transform>(true)
            .Any(t => t.name == LLCInstalledObjectName || LLCPrefabNames.Contains(t.name));
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

    private bool ShouldSkipKawaiiOfficialDialog(int totalTargetCount)
    {
        if (kawaiiPoseDialogPolicy == KawaiiPoseDialogPolicy.AlwaysShow)
            return false;
        if (kawaiiPoseDialogPolicy == KawaiiPoseDialogPolicy.AlwaysSkip)
            return true;

        return targetMode == TargetMode.SelectedProjectFolderPrefabs
            || targetMode == TargetMode.AllProjectPrefabs
            || totalTargetCount >= 2;
    }

    private void InstallKawaiiOfficial(StringBuilder sb, GameObject avatarRoot, string prefabName, bool apply, bool skipOfficialDialogs)
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

        if (skipOfficialDialogs)
        {
            InstallKawaiiSilent(sb, avatarRoot, prefabName, apply);
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

    private void InstallKawaiiSilent(StringBuilder sb, GameObject avatarRoot, string prefabName, bool apply)
    {
        var prefab = FindKawaiiPrefabByExactNameCached(prefabName);
        if (prefab == null)
        {
            sb.AppendLine(prefabName + ": [SKIP] Prefab not found for silent install");
            return;
        }

        var prefabPath = AssetDatabase.GetAssetPath(prefab);
        if (!apply)
        {
            sb.AppendLine(prefabName + ": [DRY] サイレント導入予定。公式のプレビルド確認とアバター個別プリセット適用確認はスキップします。Prefab: " + prefabPath);
            return;
        }

        var instanceObj = PrefabUtility.InstantiatePrefab(prefab, avatarRoot.transform) as GameObject;
        if (instanceObj == null)
        {
            sb.AppendLine(prefabName + ": [ERROR] Silent install prefab failed: InstantiatePrefab returned null");
            return;
        }

        Undo.RegisterCreatedObjectUndo(instanceObj, "Add " + prefabName);
        EditorUtility.SetDirty(instanceObj);
        sb.AppendLine(prefabName + ": [OK] サイレント導入しました。公式のプレビルド確認とアバター個別プリセット適用確認はスキップしました。Prefab: " + prefabPath);
    }

    private GameObject FindPrefabByExactName(string prefabName)
    {
        var guids = AssetDatabase.FindAssets("t:Prefab " + prefabName);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.name == prefabName)
                return prefab;
        }

        return null;
    }

    private GameObject FindKawaiiPrefabByExactNameCached(string prefabName)
    {
        if (!kawaiiPrefabPathCache.TryGetValue(prefabName, out var path))
        {
            var prefab = FindPrefabByExactName(prefabName);
            path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : "";
            kawaiiPrefabPathCache[prefabName] = path;
        }

        return string.IsNullOrEmpty(path)
            ? null
            : AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
