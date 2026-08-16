using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor.Presets;

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

    private enum KawaiiPoseInstallBehavior
    {
        AutoApplyPresetSkipPrebuild,
        OfficialWithDialogs,
        PrefabOnly
    }

    private static readonly GUIContent[] TargetModeLabels =
    {
        new GUIContent("Hierarchyで選択中のアバター"),
        new GUIContent("Projectで選択中のPrefab"),
        new GUIContent("Projectで選択中フォルダ内のPrefab"),
        new GUIContent("Project内の全Prefab"),
    };

    private static readonly GUIContent[] KawaiiPoseInstallModeLabels =
    {
        new GUIContent("導入しない"),
        new GUIContent("可愛いポーズ"),
        new GUIContent("可愛いポーズ（8bit・足の高さなし）"),
        new GUIContent("両方"),
    };

    private static readonly GUIContent[] KawaiiPoseInstallBehaviorLabels =
    {
        new GUIContent("プリセット自動適用・プレビルドなし"),
        new GUIContent("公式導入（確認あり）"),
        new GUIContent("Prefabのみ追加"),
    };

    private sealed class SetupTarget
    {
        public bool IsPrefabAsset;
        public string PrefabAssetPath;
        public GameObject SceneObject;
        public string Label;
    }

    private sealed class KawaiiPresetMatch
    {
        public Preset Preset;
        public string AvatarName;
        public string MatchType;
    }

    private sealed class KawaiiPoseInstallInfo
    {
        public bool HasAny;
        public bool HasNormal;
        public bool HasEightBitNoFoot;
        public List<GameObject> InstalledRoots = new List<GameObject>();
    }

    private sealed class LightLimitChangerInstallInfo
    {
        public bool HasAny;
        public bool HasV1;
        public bool HasV2;
        public bool HasPrefabOnly;
        public List<GameObject> InstalledRoots = new List<GameObject>();
    }

    private sealed class ToolDependencySnapshot
    {
        public bool Checked;
        public string Aao = "未判定";
        public string Lac = "未判定";
        public string Rbs = "未判定";
        public string Nade = "未判定";
        public string LightLimitChanger = "未判定";
        public string KawaiiPose = "未判定";
    }

    private sealed class AvatarInstallStatusSnapshot
    {
        public string TargetKey;
        public string Aao, Lac, Rbs, Nade, LightLimitChanger, KawaiiPose;
    }

    private enum LastRunStatus { NotRun, Success, Warning, Error }

    private Vector2 scroll;
    private Vector2 mainScroll;
    private string log = "";
    private string detailedLog = "";
    private readonly List<string> lastDryRunSummary = new List<string>();

    private TargetMode targetMode = TargetMode.SelectedHierarchyAvatars;
    private string selectedFolderPath = "";
    private bool requireAvatarDescriptor = true;
    private bool toolStatusChecked = false;
    private string avatarInstallStatusTargetKey = "";
    private readonly ToolDependencySnapshot dependencySnapshot = new ToolDependencySnapshot();
    private AvatarInstallStatusSnapshot avatarInstallStatusSnapshot;
    private LastRunStatus lastDryRunStatus;
    private LastRunStatus lastApplyStatus;
    private bool showToolDependencyStatus;
    private bool showNadeSystemSettings;
    private bool showDetailedLog;
    private List<GameObject> hierarchyAvatarSlots = new List<GameObject>();
    private List<SetupTarget> cachedTargets = new List<SetupTarget>();
    private string cachedTargetKey = "";
    private bool targetCacheDirty = true;
    private bool lastTargetScanCanceled = false;
    private readonly Dictionary<string, string> kawaiiPrefabPathCache = new Dictionary<string, string>();
    private readonly Dictionary<string, string> prefabPathCache = new Dictionary<string, string>();
    private readonly Dictionary<string, GameObject> nadePrefabCache = new Dictionary<string, GameObject>();
    private readonly List<UnityEngine.Object> kawaiiPresetDefinesCache = new List<UnityEngine.Object>();
    private bool kawaiiPresetDefinesCacheReady = false;

    private bool addAAO = true;
    private bool addLAC = true;
    private LacPresetMode lacPreset = LacPresetMode.HighQuality;
    private bool addRBS = true;
    private bool addNadeSystem = true;
    private bool installNadeShadowForHands = true;
    private bool installNadeShadowForFeet = true;
    private bool installNadeShadowForHead = false;
    private bool installNadeSphere = false;
    private bool installNadeFootSystem = false;
    private bool reinstallNadeSystem = false;
    private float nadeContactRadius = DefaultNadeContactRadius;
    private float nadeHeadOffsetY = DefaultNadeHeadOffsetY;
    private bool addLightLimitChanger = true;
    private bool reinstallLightLimitChanger = false;
    private bool addKawaiiNormal = false;
    private bool addKawaii8bitNoFoot = true;
    private bool reinstallKawaiiPose = false;
    private KawaiiPoseInstallMode allInstallKawaiiMode = KawaiiPoseInstallMode.EightBitNoFoot;
    private KawaiiPoseInstallBehavior kawaiiPoseInstallBehavior = KawaiiPoseInstallBehavior.AutoApplyPresetSkipPrebuild;

    private const string AAOTypeName = "Anatawa12.AvatarOptimizer.TraceAndOptimize";
    private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
    private const string LACTypeName = "dev.limitex.avatar.compressor.TextureCompressor";
    private const string LACPresetEnumTypeName = "dev.limitex.avatar.compressor.CompressorPreset";
    private const string LLCV2ComponentTypeName = "io.github.azukimochi.LightLimitChangerComponent";
    private const string LLCV1SettingsTypeName = "io.github.azukimochi.LightLimitChangerSettings";
    private const string LLCV2ContextMenuTypeName = "io.github.azukimochi.LightLimitChangerContextMenu";
    private const string LLCV1InstallerTypeName = "io.github.azukimochi.LightLimitChanger";
    private const string LLCInstalledObjectName = "Light Limit Changer";
    private const string KawaiiPoseNormalObjectName = "可愛いポーズ";
    private const string KawaiiPoseEightBitNoFootObjectName = "可愛いポーズ(8bit・足の高さなし)";
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
    private static readonly string[] KawaiiPosePrefabNames =
    {
        KawaiiPoseNormalObjectName,
        KawaiiPoseEightBitNoFootObjectName,
    };
    private static readonly string[] LLCPrefabPaths =
    {
        "Packages/io.github.azukimochi.light-limit-changer/Light Limit Changer.prefab",
        "Assets/LightLimitChanger/Light Limit Changer.prefab",
    };
    private const string KawaiiComponentTypeName = "jp.unisakistudio.kawaiiposing.KawaiiPosing";
    private const string PosingSystemMenuItemsTypeName = "jp.unisakistudio.posingsystemeditor.PosingSystemMenuItems";
    private const string PosingSystemComponentTypeName = "jp.unisakistudio.posingsystem.PosingSystem";
    private const string PosingSystemPresetDefinesTypeName = "jp.unisakistudio.posingsystemeditor.PosingSystemPresetDefines";
    private const string NadeSettingsTypeName = "RedNightWorks.NadeSystem.NadeSystemSettings";
    private const float DefaultNadeContactRadius = 0.14f;
    private const float DefaultNadeHeadOffsetY = 0.035f;
    private const string NadeSystemGUID = "491c3f399da5d064d9966982ddf0d191";
    private const string FootSystemGUID = "ca3cfa8587af6cc4f8f7205a0c16e108";
    private const string FootSystemMenuGUID = "f7ce8e50badf67b418b0c9d5b7e73442";
    private const string NadeShadowGUID = "fd1d0e8cc6fc6f646ad9f24b156a31ac";
    private const string DummyLightGUID = "c46c6e537bbb1a140957ad83f15c5afb";
    private const string NadeShadowMenuGUID = "7f6a3a1aa3e98df4e88571c89d365603";
    private const string NadeSphereGUID = "e6971546677df8d449b746136433e2cc";
    private const string NadeSphereMenuGUID = "4912f408e5d621d43a4d48dd368e3c3a";
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
        nadePrefabCache.Clear();
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
        mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
        EditorGUILayout.LabelField("VRC Avatar Toolkit Plus - Avatar Setup", EditorStyles.boldLabel);

        var previewTargets = DrawTargetArea();
        DrawDependencyStatusCards();
        DrawInstallOptionsAndDryRun(previewTargets);
        DrawDetailedLog();
        EditorGUILayout.EndScrollView();
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
                    RefreshToolDependencySnapshot();
                    detailedLog = Run(false, true);
                    log = BuildConciseLog(detailedLog);
                    RefreshAvatarInstallStatusSnapshot(previewTargets);
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
        showToolDependencyStatus = EditorGUILayout.Foldout(showToolDependencyStatus, "ツール導入状態", true);
        if (!showToolDependencyStatus)
            return;
        using (new EditorGUILayout.VerticalScope("box"))
        {
            if (!dependencySnapshot.Checked)
                EditorGUILayout.HelpBox("導入状態チェックを実行すると、各ツールの導入状態をカードに表示します。", MessageType.Info);

            var cards = new[]
            {
                new { Name = "AAO", Status = dependencySnapshot.Aao },
                new { Name = "LAC", Status = dependencySnapshot.Lac },
                new { Name = "RBS", Status = dependencySnapshot.Rbs },
                new { Name = "赤夜式 撫で音", Status = dependencySnapshot.Nade },
                new { Name = "LightLimitChanger", Status = dependencySnapshot.LightLimitChanger },
                new { Name = "可愛いポーズ", Status = dependencySnapshot.KawaiiPose },
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
            DrawInstallOptions(previewTargets);

            bool hasTargets = (previewTargets != null && previewTargets.Count > 0) || CanRefreshTargetsForRun();
            if (!hasTargets)
                EditorGUILayout.HelpBox("導入対象のアバターが未選択です。ボタンの赤いアイコンは、実行対象がないため処理できない状態を示します。", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawRunButton("導入テスト（DryRun）", hasTargets, 34))
                {
                    detailedLog = Run(false, false);
                    log = BuildConciseLog(detailedLog);
                    UpdateLastDryRunSummary(detailedLog);
                    lastDryRunStatus = GetRunStatus(detailedLog);
                    RefreshAvatarInstallStatusSnapshot(previewTargets);
                }

                if (DrawRunButton("導入実行", hasTargets, 34))
                {
                    detailedLog = Run(true, false);
                    log = BuildConciseLog(detailedLog);
                    lastApplyStatus = GetRunStatus(detailedLog);
                    RefreshAvatarInstallStatusSnapshot(previewTargets);
                }
            }
            EditorGUILayout.LabelField("簡易実行結果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("DryRun: " + FormatRunStatus(lastDryRunStatus) + "    導入実行: " + FormatRunStatus(lastApplyStatus));
        }
    }

    private void DrawInstallOptions(List<SetupTarget> previewTargets)
    {
        EditorGUILayout.LabelField("個別導入設定", EditorStyles.boldLabel);
        DrawSingleAvatarInstallStatus(previewTargets);
        allInstallKawaiiMode = DrawKawaiiPoseInstallModePopup("すべて導入時の可愛いポーズ", allInstallKawaiiMode);
        kawaiiPoseInstallBehavior = DrawKawaiiPoseInstallBehaviorPopup("可愛いポーズ導入方式", kawaiiPoseInstallBehavior);
        EditorGUILayout.HelpBox(GetKawaiiPoseInstallBehaviorDescription(), MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope())
            {
                addAAO = EditorGUILayout.ToggleLeft("AAO / TraceAndOptimize", addAAO);
                addLAC = EditorGUILayout.ToggleLeft("LAC / TextureCompressor", addLAC);
                using (new EditorGUI.DisabledScope(!addLAC))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("LAC Preset", GUILayout.Width(90));
                        lacPreset = (LacPresetMode)EditorGUILayout.EnumPopup(
                            lacPreset,
                            GUILayout.MinWidth(180),
                            GUILayout.ExpandWidth(true));
                    }
                }
                addRBS = EditorGUILayout.ToggleLeft("RBS 睡眠システム Ver2", addRBS);
                addNadeSystem = EditorGUILayout.ToggleLeft("赤夜式 撫で音ギミック", addNadeSystem);
            }

            using (new EditorGUILayout.VerticalScope())
            {
                addLightLimitChanger = EditorGUILayout.ToggleLeft("LightLimitChanger", addLightLimitChanger);
                using (new EditorGUI.DisabledScope(!addLightLimitChanger))
                    reinstallLightLimitChanger = EditorGUILayout.ToggleLeft("LightLimitChanger: 既存を削除して入れ直す", reinstallLightLimitChanger);
                addKawaiiNormal = EditorGUILayout.ToggleLeft("可愛いポーズ", addKawaiiNormal);
                addKawaii8bitNoFoot = EditorGUILayout.ToggleLeft("可愛いポーズ(8bit・足の高さなし)", addKawaii8bitNoFoot);
                using (new EditorGUI.DisabledScope(!addKawaiiNormal && !addKawaii8bitNoFoot))
                    reinstallKawaiiPose = EditorGUILayout.ToggleLeft("可愛いポーズ: 既存を削除して入れ直す", reinstallKawaiiPose);
            }
        }

        EditorGUILayout.HelpBox("入れ直しを有効にすると、対象アバター内の既存Prefabを削除してから再導入します。手動調整済みの設定は失われる可能性があります。DryRunで確認してから実行してください。", MessageType.Warning);
        using (new EditorGUI.DisabledScope(!addNadeSystem))
        {
            showNadeSystemSettings = EditorGUILayout.Foldout(showNadeSystemSettings, "赤夜式 撫で音ギミック設定", true);
        }
        if (showNadeSystemSettings)
        {
            installNadeShadowForHands = EditorGUILayout.ToggleLeft("手へ影シェーダーを導入", installNadeShadowForHands);
            installNadeShadowForHead = EditorGUILayout.ToggleLeft("頭へ影シェーダーを導入", installNadeShadowForHead);
            installNadeFootSystem = EditorGUILayout.ToggleLeft("足へ撫で音ギミックを導入", installNadeFootSystem);
            using (new EditorGUI.DisabledScope(!installNadeFootSystem))
                installNadeShadowForFeet = EditorGUILayout.ToggleLeft("足へ影シェーダーを導入", installNadeShadowForFeet);
            installNadeSphere = EditorGUILayout.ToggleLeft("カメラ撫でスフィアを導入", installNadeSphere);
            nadeContactRadius = EditorGUILayout.Slider("Contact Radius", nadeContactRadius, 0.01f, 1.0f);
            nadeHeadOffsetY = EditorGUILayout.FloatField("Contact Offset Y", nadeHeadOffsetY);
            reinstallNadeSystem = EditorGUILayout.ToggleLeft("赤夜式 撫で音を削除して入れ直す", reinstallNadeSystem);
            EditorGUILayout.HelpBox("赤夜式 撫で音の公式Installer相当の設定です。入れ直しを有効にすると既存のNadeSystemを削除して再導入します。手動調整済みの設定は失われる可能性があります。DryRunで確認してから実行してください。", MessageType.Warning);
        }
        if (addKawaiiNormal && addKawaii8bitNoFoot)
            EditorGUILayout.HelpBox("可愛いポーズの「両方」導入は通常版と8bit版を同時に追加します。既存の可愛いポーズ系がある場合、入れ直しOFFでは重複防止のため追加をスキップします。", MessageType.Info);
    }

    private KawaiiPoseInstallMode DrawKawaiiPoseInstallModePopup(string label, KawaiiPoseInstallMode value)
    {
        return (KawaiiPoseInstallMode)EditorGUILayout.Popup(new GUIContent(label), (int)value, KawaiiPoseInstallModeLabels);
    }

    private KawaiiPoseInstallBehavior DrawKawaiiPoseInstallBehaviorPopup(string label, KawaiiPoseInstallBehavior value)
    {
        return (KawaiiPoseInstallBehavior)EditorGUILayout.Popup(
            new GUIContent(label, "可愛いポーズPrefabの追加方法と対応アバター用プリセットの扱いを選びます。"),
            (int)value,
            KawaiiPoseInstallBehaviorLabels);
    }

    private string GetKawaiiPoseInstallBehaviorDescription()
    {
        switch (kawaiiPoseInstallBehavior)
        {
            case KawaiiPoseInstallBehavior.OfficialWithDialogs:
                return "可愛いポーズ公式ツールのAddPrefabを呼びます。対応アバター用プリセットの適用確認やプレビルド確認が表示される場合があります。大量のPrefabに実行すると確認が多数表示される可能性があります。";
            case KawaiiPoseInstallBehavior.PrefabOnly:
                return "Prefabのみ追加します。対応アバター用プリセットの検索・適用とプレビルドは行いません。確認ダイアログも表示されません。";
            default:
                return "対応アバター用プリセットが見つかった場合は自動適用し、プレビルドは実行しません。確認ダイアログは表示されません。大量のPrefabへの一括導入に推奨です。";
        }
    }

    private void DrawDetailedLog()
    {
        showDetailedLog = EditorGUILayout.Foldout(showDetailedLog, "詳細ログ", true);
        if (!showDetailedLog)
            return;
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("クリップボードコピー", GUILayout.Width(150)))
                    EditorGUIUtility.systemCopyBuffer = string.IsNullOrEmpty(detailedLog) ? log : detailedLog;
                if (GUILayout.Button("別ウィンドウで表示", GUILayout.Width(150)))
                    AvatarSetupLogWindow.ShowLog(string.IsNullOrEmpty(detailedLog) ? log : detailedLog);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(240));
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
        EditorGUILayout.LabelField("赤夜式 撫で音", FormatToolDependencyStatus(IsNadeSystemToolAvailable()));
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
        if (string.IsNullOrEmpty(statusTargetKey) || avatarInstallStatusSnapshot == null ||
            statusTargetKey != avatarInstallStatusSnapshot.TargetKey)
            return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("選択アバターの導入状態", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("AAO", avatarInstallStatusSnapshot.Aao);
        EditorGUILayout.LabelField("LAC", avatarInstallStatusSnapshot.Lac);
        EditorGUILayout.LabelField("RBS", avatarInstallStatusSnapshot.Rbs);
        EditorGUILayout.LabelField("赤夜式 撫で音", avatarInstallStatusSnapshot.Nade);
        EditorGUILayout.LabelField("LightLimitChanger", avatarInstallStatusSnapshot.LightLimitChanger);
        EditorGUILayout.LabelField("可愛いポーズ", avatarInstallStatusSnapshot.KawaiiPose);
        EditorGUILayout.Space(4);
    }

    private void RefreshAvatarInstallStatusSnapshot(List<SetupTarget> targets)
    {
        avatarInstallStatusSnapshot = null;
        avatarInstallStatusTargetKey = GetSingleAvatarInstallStatusTargetKey(targets);
        if (string.IsNullOrEmpty(avatarInstallStatusTargetKey)) return;
        var root = GetAvatarRootForInstallStatus(targets[0]);
        if (root == null) return;
        avatarInstallStatusSnapshot = new AvatarInstallStatusSnapshot
        {
            TargetKey = avatarInstallStatusTargetKey,
            Aao = GetAvatarComponentInstallState(root, AAOTypeName),
            Lac = GetAvatarComponentInstallState(root, LACTypeName),
            Rbs = GetRbsAvatarInstallState(root),
            Nade = GetAvatarNadeInstallState(root),
            LightLimitChanger = GetLightLimitChangerAvatarInstallState(root),
            KawaiiPose = GetAvatarKawaiiInstallState(root)
        };
    }

    private void RefreshToolDependencySnapshot()
    {
        TypeCache.Clear();
        prefabPathCache.Clear();
        kawaiiPrefabPathCache.Clear();
        nadePrefabCache.Clear();
        toolStatusChecked = true;
        dependencySnapshot.Checked = true;
        dependencySnapshot.Aao = GetTypeToolDependencyState(AAOTypeName);
        dependencySnapshot.Lac = GetTypeToolDependencyState(LACTypeName);
        dependencySnapshot.Rbs = GetPrefabToolDependencyState(RBSPrefabNames);
        dependencySnapshot.Nade = FormatToolDependencyStatus(IsNadeSystemToolAvailable());
        dependencySnapshot.LightLimitChanger = GetLightLimitChangerDependencyState();
        dependencySnapshot.KawaiiPose = FormatToolDependencyStatus(IsKawaiiPoseToolAvailable());
    }

    private static LastRunStatus GetRunStatus(string text)
    {
        if (text != null && text.Contains("[ERROR]")) return LastRunStatus.Error;
        if (text != null && text.Contains("[WARN]")) return LastRunStatus.Warning;
        return LastRunStatus.Success;
    }

    private static string FormatRunStatus(LastRunStatus status)
    {
        switch (status)
        {
            case LastRunStatus.Success: return "✓ 成功";
            case LastRunStatus.Warning: return "! 警告（詳細ログを確認）";
            case LastRunStatus.Error: return "! エラー（詳細ログを確認）";
            default: return "未実行";
        }
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

    private string GetAvatarNadeInstallState(GameObject avatarRoot)
    {
        if (!IsNadeSystemToolAvailable()) return FormatToolNotInstalledStatus();
        var installed = avatarRoot.GetComponentsInChildren<Transform>(true).Any(t => t.name == "NadeSystem");
        var type = FindType(NadeSettingsTypeName);
        if (!installed && type != null) installed = avatarRoot.GetComponentsInChildren(type, true).Length > 0;
        return FormatAvatarInstallStatus(installed);
    }

    private string GetAvatarKawaiiInstallState(GameObject avatarRoot)
    {
        if (!IsKawaiiPoseToolAvailable())
            return FormatToolNotInstalledStatus();

        var info = GetKawaiiPoseInstallInfo(avatarRoot);
        return FormatAvatarInstallStatus(info.HasAny) + FormatKawaiiPoseInstallSuffix(info);
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

        return FormatAvatarInstallStatus(GetLightLimitChangerInstallInfo(avatarRoot).HasAny) + FormatLightLimitChangerInstallSuffix(GetLightLimitChangerInstallInfo(avatarRoot));
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
        kawaiiPresetDefinesCache.Clear();
        kawaiiPresetDefinesCacheReady = false;
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
            RunInstallOrSkip(sb, avatarRoot, "赤夜式 撫で音", addNadeSystem, IsNadeSystemToolAvailable(), () => InstallNadeSystemWithOptions(sb, avatarRoot, apply));
            RunInstallOrSkip(sb, avatarRoot, "LightLimitChanger", addLightLimitChanger, IsLightLimitChangerToolAvailable(), () => InstallLightLimitChangerWithReinstall(sb, avatarRoot, apply));
            RunKawaiiPoseInstallFamily(sb, avatarRoot, apply);

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

    private bool IsNadeSystemToolAvailable()
    {
        return FindType(NadeSettingsTypeName) != null || FindNadePrefab(NadeSystemGUID, "NadeSystem") != null;
    }

    private bool IsLightLimitChangerToolAvailable()
    {
        return GetLightLimitChangerToolVariant() != LightLimitChangerVariant.NotInstalled;
    }

    private bool IsKawaiiPoseToolAvailable()
    {
        return FindType(KawaiiComponentTypeName) != null || FindType(PosingSystemComponentTypeName) != null || KawaiiPosePrefabNames.Any(name => FindKawaiiPrefabByExactNameCached(name) != null);
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
        avatarInstallStatusSnapshot = null;
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
        AppendTypeLine(sb, "PosingSystem Component", PosingSystemComponentTypeName);
        AppendTypeLine(sb, "PosingSystem Preset Defines", PosingSystemPresetDefinesTypeName);
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
        AppendNadeStatus(sb, avatarRoot);
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

    private void AppendNadeStatus(StringBuilder sb, GameObject root)
    {
        if (!IsNadeSystemToolAvailable()) { sb.AppendLine("赤夜式 撫で音: Tool not installed"); return; }
        var nadeSystems = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "NadeSystem").ToArray();
        var installed = nadeSystems.Length > 0;
        var type = FindType(NadeSettingsTypeName);
        if (!installed && type != null) installed = root.GetComponentsInChildren(type, true).Length > 0;
        sb.AppendLine("赤夜式 撫で音: " + (installed ? "Installed" : "Not Installed") + " / NadeSystem Count: " + nadeSystems.Length);
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
        var info = GetLightLimitChangerInstallInfo(root);
        sb.AppendLine("LightLimitChanger: " + (info.HasAny ? "Installed" : "Not Installed") + FormatLightLimitChangerInstallSuffix(info) + " / Root Count: " + info.InstalledRoots.Count);
        foreach (var h in info.InstalledRoots)
            sb.AppendLine("  - " + GetPath(h.transform));
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
        if (!IsKawaiiPoseToolAvailable())
        {
            sb.AppendLine("可愛いポーズツール: Tool not installed");
            return;
        }

        var info = GetKawaiiPoseInstallInfo(root);
        sb.AppendLine("可愛いポーズツール: " + (info.HasAny ? "Installed" : "Not Installed") + FormatKawaiiPoseInstallSuffix(info) + " / Root Count: " + info.InstalledRoots.Count);
        foreach (var h in info.InstalledRoots)
            sb.AppendLine("  - " + GetPath(h.transform));
        sb.AppendLine("  可愛いポーズ: " + (info.HasNormal ? "Found" : "Not Found"));
        sb.AppendLine("  可愛いポーズ(8bit・足の高さなし): " + (info.HasEightBitNoFoot ? "Found" : "Not Found"));
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
            var dryApplyPreset = lacType.GetMethod("ApplyPreset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { enumType }, null);
            sb.AppendLine(dryApplyPreset != null
                ? "LAC: [DRY] Preset=" + presetName + " を公式ApplyPresetで適用予定です。"
                : "LAC: [WARN] Preset名のみ設定予定です。公式ApplyPresetが見つからないため内部設定の完全な反映を保証できません。");
            return;
        }

        Undo.RecordObject(comp, "Set LAC Preset");
        var presetValue = Enum.Parse(enumType, presetName);
        var applyPreset = lacType.GetMethod("ApplyPreset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { enumType }, null);
        if (applyPreset != null)
        {
            applyPreset.Invoke(comp, new[] { presetValue });
            sb.AppendLine("LAC: [OK] Preset=" + presetName + " を公式ApplyPresetで適用しました。");
            AppendLacPresetValues(sb, comp, lacType);
        }
        else
        {
            presetField.SetValue(comp, presetValue);
            sb.AppendLine("LAC: [WARN] Preset名は設定しましたが、公式ApplyPresetが見つからないため内部設定の完全な反映を保証できません。");
        }
        EditorUtility.SetDirty(comp);
    }

    private static void AppendLacPresetValues(StringBuilder sb, Component comp, Type type)
    {
        string Read(string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return Convert.ToString(field.GetValue(comp));
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return property != null ? Convert.ToString(property.GetValue(comp, null)) : "?";
        }
        sb.AppendLine("LAC: [INFO] Divisor=" + Read("MinDivisor") + "x-" + Read("MaxDivisor") +
            "x / Resolution=" + Read("MinResolution") + "px-" + Read("MaxResolution") +
            "px / Complexity=" + Read("LowComplexityThreshold") + "-" + Read("HighComplexityThreshold"));
    }

    private void InstallLightLimitChangerWithReinstall(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        var info = GetLightLimitChangerInstallInfo(avatarRoot);
        if (info.HasAny && !reinstallLightLimitChanger)
        {
            sb.AppendLine("LightLimitChanger: [SKIP] 既に導入済みです。");
            return;
        }

        if (info.HasAny && reinstallLightLimitChanger)
        {
            var count = info.InstalledRoots.Distinct().Count();
            if (!apply)
            {
                sb.AppendLine("LightLimitChanger: [DRY] 既存のLightLimitChanger系Prefab " + count + "件を削除し、再導入予定です。");
                AppendLightLimitChangerDryInstallPlan(sb, avatarRoot);
                return;
            }
            else
                RemoveLightLimitChangerInstallations(avatarRoot, true, sb);
        }

        InstallLightLimitChangerOfficial(sb, avatarRoot, apply);
        if (info.HasAny && reinstallLightLimitChanger)
            sb.AppendLine("LightLimitChanger: " + (apply ? "[OK] 再導入しました。" : "[DRY] 再導入予定です。"));
    }

    private void InstallLightLimitChangerOfficial(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        if (GetLightLimitChangerInstallInfo(avatarRoot).HasAny)
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

    private void AppendLightLimitChangerDryInstallPlan(StringBuilder sb, GameObject avatarRoot)
    {
        if (FindLightLimitChangerV2SetupMethod() != null)
        {
            sb.AppendLine("LightLimitChanger: [DRY] Call official Setup() with avatar selected (V2 style)");
            return;
        }
        if (FindLightLimitChangerV1ApplyMethod() != null)
        {
            sb.AppendLine("LightLimitChanger: [DRY] Call ApplytoAvatar(MenuCommand) with avatar selected (V1 style)");
            return;
        }

        InstallLightLimitChangerPrefabFallback(sb, avatarRoot, false);
    }

    private void RunKawaiiPoseInstallFamily(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        bool selected = addKawaiiNormal || addKawaii8bitNoFoot;
        if (!selected)
        {
            sb.AppendLine("可愛いポーズ: [SKIP] 導入未選択");
            return;
        }

        if (!IsKawaiiPoseToolAvailable())
        {
            sb.AppendLine("可愛いポーズ: [SKIP] ツール未導入");
            return;
        }

        var info = GetKawaiiPoseInstallInfo(avatarRoot);
        if (info.HasAny && !reinstallKawaiiPose)
        {
            var names = new List<string>();
            if (addKawaiiNormal) names.Add(KawaiiPoseNormalObjectName);
            if (addKawaii8bitNoFoot) names.Add(KawaiiPoseEightBitNoFootObjectName);
            sb.AppendLine(string.Join(" / ", names) + ": [SKIP] 可愛いポーズ系が既に導入済みです。入れ替える場合は「可愛いポーズを入れ直す」を有効にしてください。");
            return;
        }

        if (info.HasAny && reinstallKawaiiPose)
        {
            RemoveKawaiiPoseInstallations(avatarRoot, apply, sb);
            if (!apply)
            {
                sb.AppendLine("可愛いポーズ: [DRY] 選択された可愛いポーズ系Prefabを再導入予定です。");
                if (addKawaiiNormal)
                    AppendKawaiiPoseDryInstallPlan(sb, avatarRoot, KawaiiPoseNormalObjectName);
                if (addKawaii8bitNoFoot)
                    AppendKawaiiPoseDryInstallPlan(sb, avatarRoot, KawaiiPoseEightBitNoFootObjectName);
                return;
            }
        }

        if (addKawaiiNormal)
            InstallKawaiiPose(sb, avatarRoot, KawaiiPoseNormalObjectName, apply);
        if (addKawaii8bitNoFoot)
            InstallKawaiiPose(sb, avatarRoot, KawaiiPoseEightBitNoFootObjectName, apply);
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
        return GetLightLimitChangerInstallInfo(avatarRoot).HasAny;
    }

    private static LightLimitChangerInstallInfo GetLightLimitChangerInstallInfo(GameObject avatarRoot)
    {
        var info = new LightLimitChangerInstallInfo();
        if (avatarRoot == null) return info;

        AddInstallRootsForType(avatarRoot, LLCV1SettingsTypeName, info.InstalledRoots, () => info.HasV1 = true);
        AddInstallRootsForType(avatarRoot, LLCV2ComponentTypeName, info.InstalledRoots, () => info.HasV2 = true);

        foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!LLCPrefabNames.Contains(t.name)) continue;
            info.HasPrefabOnly = true;
            info.InstalledRoots.Add(ResolveInstalledRoot(avatarRoot, t.gameObject));
        }

        info.InstalledRoots = info.InstalledRoots.Where(o => o != null).Distinct().ToList();
        info.HasAny = info.HasV1 || info.HasV2 || info.HasPrefabOnly || info.InstalledRoots.Count > 0;
        if (info.HasAny && !info.HasV1 && !info.HasV2)
            info.HasPrefabOnly = true;
        return info;
    }

    private static KawaiiPoseInstallInfo GetKawaiiPoseInstallInfo(GameObject avatarRoot)
    {
        var info = new KawaiiPoseInstallInfo();
        if (avatarRoot == null) return info;

        var kawaiiType = FindType(KawaiiComponentTypeName);
        if (kawaiiType != null)
        {
            foreach (var c in avatarRoot.GetComponentsInChildren(kawaiiType, true).OfType<Component>())
                AddKawaiiPoseRoot(avatarRoot, info, c.gameObject);
        }

        var posingType = FindType(PosingSystemComponentTypeName);
        if (posingType != null)
        {
            foreach (var c in avatarRoot.GetComponentsInChildren(posingType, true).OfType<Component>())
                if (KawaiiPosePrefabNames.Contains(c.gameObject.name))
                    AddKawaiiPoseRoot(avatarRoot, info, c.gameObject);
        }

        foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true))
            if (KawaiiPosePrefabNames.Contains(t.name))
                AddKawaiiPoseRoot(avatarRoot, info, t.gameObject);

        info.InstalledRoots = info.InstalledRoots.Where(o => o != null).Distinct().ToList();
        info.HasAny = info.HasNormal || info.HasEightBitNoFoot || info.InstalledRoots.Count > 0;
        return info;
    }

    private static void AddInstallRootsForType(GameObject avatarRoot, string typeName, List<GameObject> roots, Action mark)
    {
        var type = FindType(typeName);
        if (type == null) return;

        foreach (var c in avatarRoot.GetComponentsInChildren(type, true).OfType<Component>())
        {
            mark?.Invoke();
            roots.Add(ResolveInstalledRoot(avatarRoot, c.gameObject));
        }
    }

    private static void AddKawaiiPoseRoot(GameObject avatarRoot, KawaiiPoseInstallInfo info, GameObject obj)
    {
        if (obj == null) return;
        if (obj.name == KawaiiPoseNormalObjectName)
            info.HasNormal = true;
        if (obj.name == KawaiiPoseEightBitNoFootObjectName)
            info.HasEightBitNoFoot = true;
        info.InstalledRoots.Add(ResolveInstalledRoot(avatarRoot, obj));
    }

    private static GameObject ResolveInstalledRoot(GameObject avatarRoot, GameObject obj)
    {
        if (avatarRoot == null || obj == null)
            return obj;

        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(obj);
        if (prefabRoot != null
            && prefabRoot != avatarRoot
            && prefabRoot.transform.IsChildOf(avatarRoot.transform))
        {
            return prefabRoot;
        }

        return obj;
    }

    private int RemoveKawaiiPoseInstallations(GameObject avatarRoot, bool apply, StringBuilder sb)
    {
        var roots = GetKawaiiPoseInstallInfo(avatarRoot).InstalledRoots.Where(o => o != null).Distinct().ToList();
        if (roots.Count == 0) return 0;

        if (!apply)
        {
            sb.AppendLine("可愛いポーズ: [DRY] 既存の可愛いポーズ系Prefab " + roots.Count + "件を削除予定です。");
            return roots.Count;
        }

        foreach (var root in roots)
            Undo.DestroyObjectImmediate(root);
        sb.AppendLine("可愛いポーズ: [OK] 既存の可愛いポーズ系Prefab " + roots.Count + "件を削除しました。");
        return roots.Count;
    }

    private int RemoveLightLimitChangerInstallations(GameObject avatarRoot, bool apply, StringBuilder sb)
    {
        var roots = GetLightLimitChangerInstallInfo(avatarRoot).InstalledRoots.Where(o => o != null).Distinct().ToList();
        if (roots.Count == 0) return 0;

        if (!apply)
        {
            sb.AppendLine("LightLimitChanger: [DRY] 既存のLightLimitChanger系Prefab " + roots.Count + "件を削除予定です。");
            return roots.Count;
        }

        foreach (var root in roots)
            Undo.DestroyObjectImmediate(root);
        sb.AppendLine("LightLimitChanger: [OK] 既存のLightLimitChanger系Prefab " + roots.Count + "件を削除しました。");
        return roots.Count;
    }

    private string FormatKawaiiPoseInstallSuffix(KawaiiPoseInstallInfo info)
    {
        if (info.HasNormal && info.HasEightBitNoFoot) return "（通常 + 8bit）";
        if (info.HasNormal) return "（通常）";
        if (info.HasEightBitNoFoot) return "（8bit）";
        return info.HasAny ? "（Prefabのみ/種別不明）" : "";
    }

    private string FormatLightLimitChangerInstallSuffix(LightLimitChangerInstallInfo info)
    {
        var parts = new List<string>();
        if (info.HasV1) parts.Add("V1");
        if (info.HasV2) parts.Add("V2");
        if (info.HasPrefabOnly) parts.Add("Prefabのみ");
        return parts.Count > 0 ? "（" + string.Join(" + ", parts) + "）" : "";
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

    private void InstallKawaiiPose(StringBuilder sb, GameObject avatarRoot, string prefabName, bool apply)
    {
        switch (kawaiiPoseInstallBehavior)
        {
            case KawaiiPoseInstallBehavior.OfficialWithDialogs:
                InstallKawaiiOfficial(sb, avatarRoot, prefabName, apply);
                return;
            case KawaiiPoseInstallBehavior.PrefabOnly:
                InstallKawaiiPrefabOnly(sb, avatarRoot, prefabName, apply);
                return;
            default:
                InstallKawaiiPresetAutoSkipPrebuild(sb, avatarRoot, prefabName, apply);
                return;
        }
    }

    private void AppendKawaiiPoseDryInstallPlan(StringBuilder sb, GameObject avatarRoot, string prefabName)
    {
        switch (kawaiiPoseInstallBehavior)
        {
            case KawaiiPoseInstallBehavior.OfficialWithDialogs:
                sb.AppendLine(prefabName + ": [DRY] 公式AddPrefabを実行予定。対応アバター用プリセット適用確認やプレビルド確認が表示される場合があります。");
                return;
            case KawaiiPoseInstallBehavior.PrefabOnly:
                {
                    var prefab = FindKawaiiPrefabByExactNameCached(prefabName);
                    sb.AppendLine(prefabName + ": [DRY] Prefabのみ追加予定です。対応アバター用プリセット適用とプレビルドは行いません。" + (prefab != null ? " Prefab: " + AssetDatabase.GetAssetPath(prefab) : ""));
                    return;
                }
            default:
                {
                    var prefab = FindKawaiiPrefabByExactNameCached(prefabName);
                    var presetMatch = FindKawaiiPresetMatch(avatarRoot, prefab);
                    if (presetMatch != null)
                        sb.AppendLine(prefabName + ": [DRY] Prefabを追加し、対応アバター用プリセット「" + presetMatch.AvatarName + "」を自動適用予定です。プレビルドは実行しません。");
                    else
                        sb.AppendLine(prefabName + ": [DRY] Prefabを追加予定です。対応アバター用プリセットは見つかりませんでした。プレビルドは実行しません。");
                    return;
                }
        }
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
            sb.AppendLine(prefabName + ": [DRY] 公式AddPrefabを実行予定。対応アバター用プリセット適用確認やプレビルド確認が表示される場合があります。");
            return;
        }

        InvokeWithSelection(avatarRoot, () => addPrefab.Invoke(null, new object[] { prefabName }));
        sb.AppendLine(prefabName + ": [OK] 公式AddPrefabを実行しました。公式ツール側の確認ダイアログが表示される場合があります。");
    }

    private void InstallKawaiiPrefabOnly(StringBuilder sb, GameObject avatarRoot, string prefabName, bool apply)
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
            sb.AppendLine(prefabName + ": [DRY] Prefabのみ追加予定です。対応アバター用プリセット適用とプレビルドは行いません。Prefab: " + prefabPath);
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
        sb.AppendLine(prefabName + ": [OK] Prefabのみ追加しました。対応アバター用プリセット適用とプレビルドは行っていません。Prefab: " + prefabPath);
    }

    private void InstallKawaiiPresetAutoSkipPrebuild(StringBuilder sb, GameObject avatarRoot, string prefabName, bool apply)
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

        var prefab = FindKawaiiPrefabByExactNameCached(prefabName);
        if (prefab == null)
        {
            sb.AppendLine(prefabName + ": [SKIP] Prefab not found");
            return;
        }

        var presetMatch = FindKawaiiPresetMatch(avatarRoot, prefab);
        if (!apply)
        {
            if (presetMatch != null)
                sb.AppendLine(prefabName + ": [DRY] Prefabを追加し、対応アバター用プリセット「" + presetMatch.AvatarName + "」を自動適用予定です。プレビルドは実行しません。");
            else
                sb.AppendLine(prefabName + ": [DRY] Prefabを追加予定です。対応アバター用プリセットは見つかりませんでした。プレビルドは実行しません。");
            return;
        }

        var instanceObj = PrefabUtility.InstantiatePrefab(prefab, avatarRoot.transform) as GameObject;
        if (instanceObj == null)
        {
            sb.AppendLine(prefabName + ": [ERROR] InstantiatePrefab returned null");
            return;
        }

        Undo.RegisterCreatedObjectUndo(instanceObj, "Add " + prefabName);
        EditorUtility.SetDirty(instanceObj);

        if (presetMatch == null)
        {
            sb.AppendLine(prefabName + ": [OK] Prefabを追加しました。対応アバター用プリセットは見つかりませんでした。プレビルドは実行していません。");
            return;
        }

        try
        {
            if (ApplyKawaiiPresetToInstance(instanceObj, presetMatch.Preset, sb))
                sb.AppendLine(prefabName + ": [OK] Prefabを追加し、対応アバター用プリセット「" + presetMatch.AvatarName + "」を自動適用しました。プレビルドは実行していません。");
            else
                sb.AppendLine(prefabName + ": [WARN] Prefabは追加しましたが、対応アバター用プリセットの適用に失敗しました: PosingSystemコンポーネントまたはPresetが見つかりません。");
        }
        catch (Exception ex)
        {
            sb.AppendLine(prefabName + ": [WARN] Prefabは追加しましたが、対応アバター用プリセットの適用に失敗しました: " + GetInvocationErrorMessage(ex));
        }
    }

    private KawaiiPresetMatch FindKawaiiPresetMatch(GameObject avatarRoot, GameObject targetPrefab)
    {
        if (avatarRoot == null || targetPrefab == null)
            return null;

        var avatarKeys = BuildKawaiiAvatarPresetKeys(avatarRoot);
        KawaiiPresetMatch nameMatch = null;

        foreach (var definesAsset in GetKawaiiPresetDefinesAssets())
        {
            if (definesAsset == null) continue;
            var so = new SerializedObject(definesAsset);
            var presetDefines = so.FindProperty("presetDefines");
            if (presetDefines == null || !presetDefines.isArray) continue;

            for (int i = 0; i < presetDefines.arraySize; i++)
            {
                var define = presetDefines.GetArrayElementAtIndex(i);
                if (!KawaiiPresetDefineContainsPrefab(define.FindPropertyRelative("prefabs"), targetPrefab))
                    continue;

                var preset = define.FindPropertyRelative("preset")?.objectReferenceValue as Preset;
                if (preset == null) continue;

                var avatarName = GetStringPropertyValue(define.FindPropertyRelative("avatarName"));
                if (KawaiiStringArrayContainsAny(define.FindPropertyRelative("prefabsHashes"), avatarKeys.GuidHashes))
                    return new KawaiiPresetMatch { Preset = preset, AvatarName = string.IsNullOrEmpty(avatarName) ? avatarRoot.name : avatarName, MatchType = "GUID Hash" };

                if (nameMatch == null && KawaiiStringArrayContainsAny(define.FindPropertyRelative("prefabsNames"), avatarKeys.Names))
                    nameMatch = new KawaiiPresetMatch { Preset = preset, AvatarName = string.IsNullOrEmpty(avatarName) ? avatarRoot.name : avatarName, MatchType = "Name" };
            }
        }

        return nameMatch;
    }

    private bool ApplyKawaiiPresetToInstance(GameObject installedInstance, Preset preset, StringBuilder sb)
    {
        var posingSystemType = FindType(PosingSystemComponentTypeName);
        if (installedInstance == null || preset == null || posingSystemType == null)
            return false;

        var component = installedInstance.GetComponentInChildren(posingSystemType, true) as Component;
        if (component == null)
            return false;

        Undo.RecordObject(component, "Apply Kawaii Pose Preset");
        preset.ApplyTo(component, new[] { "defines", "overrideDefines" });
        EditorUtility.SetDirty(component);
        return true;
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

    private IEnumerable<UnityEngine.Object> GetKawaiiPresetDefinesAssets()
    {
        if (kawaiiPresetDefinesCacheReady)
            return kawaiiPresetDefinesCache;

        kawaiiPresetDefinesCacheReady = true;
        kawaiiPresetDefinesCache.Clear();

        var presetDefinesType = FindType(PosingSystemPresetDefinesTypeName);
        var guids = AssetDatabase.FindAssets("t:PosingSystemPresetDefines");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null) continue;
            if (presetDefinesType != null && !presetDefinesType.IsInstanceOfType(asset)) continue;
            kawaiiPresetDefinesCache.Add(asset);
        }

        return kawaiiPresetDefinesCache;
    }

    private sealed class KawaiiAvatarPresetKeys
    {
        public readonly HashSet<string> GuidHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private KawaiiAvatarPresetKeys BuildKawaiiAvatarPresetKeys(GameObject avatarRoot)
    {
        var keys = new KawaiiAvatarPresetKeys();
        if (avatarRoot == null)
            return keys;

        keys.Names.Add(avatarRoot.name);
        var source = PrefabUtility.GetCorrespondingObjectFromSource(avatarRoot);
        while (source != null)
        {
            keys.Names.Add(source.name);
            var path = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(path))
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    keys.GuidHashes.Add(GetPosingSystemGuidHash(guid));
            }
            source = PrefabUtility.GetCorrespondingObjectFromSource(source);
        }

        var rootPath = AssetDatabase.GetAssetPath(avatarRoot);
        if (!string.IsNullOrEmpty(rootPath))
        {
            var guid = AssetDatabase.AssetPathToGUID(rootPath);
            if (!string.IsNullOrEmpty(guid))
                keys.GuidHashes.Add(GetPosingSystemGuidHash(guid));
        }

        return keys;
    }

    private bool KawaiiPresetDefineContainsPrefab(SerializedProperty prefabsProperty, GameObject targetPrefab)
    {
        if (prefabsProperty == null || !prefabsProperty.isArray || targetPrefab == null)
            return false;

        var targetPath = AssetDatabase.GetAssetPath(targetPrefab);
        for (int i = 0; i < prefabsProperty.arraySize; i++)
        {
            var obj = prefabsProperty.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj == null) continue;
            if (obj == targetPrefab) return true;
            if (!string.IsNullOrEmpty(targetPath) && AssetDatabase.GetAssetPath(obj) == targetPath) return true;
        }
        return false;
    }

    private bool KawaiiStringArrayContainsAny(SerializedProperty arrayProperty, HashSet<string> candidates)
    {
        if (arrayProperty == null || !arrayProperty.isArray || candidates == null || candidates.Count == 0)
            return false;

        for (int i = 0; i < arrayProperty.arraySize; i++)
        {
            var value = arrayProperty.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(value) && candidates.Contains(value))
                return true;
        }
        return false;
    }

    private string GetStringPropertyValue(SerializedProperty property)
    {
        return property != null && property.propertyType == SerializedPropertyType.String ? property.stringValue : "";
    }

    private string GetPosingSystemGuidHash(string guid)
    {
        var menuType = FindType(PosingSystemMenuItemsTypeName);
        var method = menuType?.GetMethod("GetPosingSystemGUIDHash", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (method != null)
        {
            try
            {
                var result = method.Invoke(null, new object[] { guid }) as string;
                if (!string.IsNullOrEmpty(result))
                    return result;
            }
            catch
            {
                // Fallback below.
            }
        }

        return GetPosingSystemGuidHashFallback(guid);
    }

    private string GetPosingSystemGuidHashFallback(string guid)
    {
        const string seed = "_jp.unisakistudio.posingsystem.guihashmaker";
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(guid + seed));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private void InstallNadeSystemWithOptions(StringBuilder sb, GameObject avatarRoot, bool apply)
    {
        var existing = avatarRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "NadeSystem");
        if (existing != null && !reinstallNadeSystem)
        {
            sb.AppendLine("赤夜式 撫で音: [SKIP] 既にNadeSystemが導入済みです。設定を変更する場合は「赤夜式 撫で音を削除して入れ直す」を有効にしてください。");
            return;
        }

        var nadePrefab = FindNadePrefab(NadeSystemGUID, "NadeSystem");
        if (nadePrefab == null)
        {
            sb.AppendLine("赤夜式 撫で音: [ERROR] NadeSystem Prefabが見つかりません。");
            return;
        }

        if (!apply)
        {
            if (existing != null) sb.AppendLine("赤夜式 撫で音: [DRY] 既存のNadeSystemを削除して再導入予定です。");
            sb.AppendLine("赤夜式 撫で音: [DRY] NadeSystem Prefabを追加予定です。");
            sb.AppendLine(string.Format("赤夜式 撫で音: [DRY] Contact Radius={0}, Contact Offset Y={1} を適用予定です。", nadeContactRadius, nadeHeadOffsetY));
            sb.AppendLine("赤夜式 撫で音: [DRY] FootSystem: " + (installNadeFootSystem ? "ON（FootSystem と FootSystemMenu を追加予定）" : "OFF"));
            sb.AppendLine(string.Format("赤夜式 撫で音: [DRY] 影シェーダー: 手={0}, 頭={1}, 足={2}", OnOff(installNadeShadowForHands), OnOff(installNadeShadowForHead), OnOff(installNadeFootSystem && installNadeShadowForFeet)));
            sb.AppendLine("赤夜式 撫で音: [DRY] カメラ撫でスフィア: " + OnOff(installNadeSphere));
            return;
        }

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
            sb.AppendLine("赤夜式 撫で音: [OK] 既存のNadeSystemを削除しました。");
        }

        var nadeSystem = InstantiateNadePrefab(nadePrefab, avatarRoot.transform, sb, "NadeSystem");
        if (nadeSystem == null) return;
        sb.AppendLine("赤夜式 撫で音: [OK] NadeSystem Prefabを追加しました。");

        var rxHeadMain = RequireNadeTransform(sb, nadeSystem.transform, "RxHeadMain");
        var headSystem = RequireNadeTransform(sb, nadeSystem.transform, "HeadSystem");
        var rightHand = RequireNadeTransform(sb, nadeSystem.transform, "RightHandSystem");
        var leftHand = RequireNadeTransform(sb, nadeSystem.transform, "LeftHandSystem");
        var exMenu = RequireNadeTransform(sb, nadeSystem.transform, "ExMenu");
        var nadeControl = RequireNadeTransform(sb, nadeSystem.transform, "ExMenu/Nade Control");

        ApplyNadeContactSettings(sb, avatarRoot, rxHeadMain, headSystem);

        GameObject footSystem = null;
        if (installNadeFootSystem)
        {
            footSystem = InstantiateNadePrefab(FindNadePrefab(FootSystemGUID, "FootSystem"), nadeSystem.transform, sb, "FootSystem");
            var footMenu = InstantiateNadePrefab(FindNadePrefab(FootSystemMenuGUID, "FootSystemMenu"), nadeControl, sb, "FootSystemMenu");
            if (footSystem != null && footMenu != null) sb.AppendLine("赤夜式 撫で音: [OK] FootSystem と FootSystemMenu を追加しました。");
        }

        var shadowPrefab = FindNadePrefab(NadeShadowGUID, "NadeShadow");
        int shadowCount = 0;
        if (installNadeShadowForHands)
        {
            if (InstantiateNadePrefab(shadowPrefab, rightHand, sb, "NadeShadow（右手）") != null) shadowCount++;
            if (InstantiateNadePrefab(shadowPrefab, leftHand, sb, "NadeShadow（左手）") != null) shadowCount++;
        }
        if (installNadeShadowForHead && InstantiateNadePrefab(shadowPrefab, headSystem, sb, "NadeShadow（頭）") != null) shadowCount++;
        if (installNadeFootSystem && installNadeShadowForFeet && footSystem != null)
        {
            var rightFoot = RequireNadeTransform(sb, footSystem.transform, "RightFootSystem/RxRightFoot");
            var leftFoot = RequireNadeTransform(sb, footSystem.transform, "LeftFootSystem/RxLeftFoot");
            if (InstantiateNadePrefab(shadowPrefab, rightFoot, sb, "NadeShadow（右足）") != null) shadowCount++;
            if (InstantiateNadePrefab(shadowPrefab, leftFoot, sb, "NadeShadow（左足）") != null) shadowCount++;
        }
        if (shadowCount > 0)
        {
            InstantiateNadePrefab(FindNadePrefab(DummyLightGUID, "DummyLight"), nadeSystem.transform, sb, "DummyLight");
            InstantiateNadePrefab(FindNadePrefab(NadeShadowMenuGUID, "NadeShadowMenu"), exMenu, sb, "NadeShadowMenu");
            sb.AppendLine(string.Format("赤夜式 撫で音: [OK] 影シェーダーを追加しました（手: {0}, 頭: {1}, 足: {2}）。", OnOff(installNadeShadowForHands), OnOff(installNadeShadowForHead), OnOff(installNadeFootSystem && installNadeShadowForFeet)));
        }

        if (installNadeSphere)
        {
            var sphere = InstantiateNadePrefab(FindNadePrefab(NadeSphereGUID, "NadeSphere"), nadeSystem.transform, sb, "NadeSphere");
            var sphereMenu = InstantiateNadePrefab(FindNadePrefab(NadeSphereMenuGUID, "NadeSphereMenu"), nadeControl, sb, "NadeSphereMenu");
            if (sphere != null && sphereMenu != null) sb.AppendLine("赤夜式 撫で音: [OK] カメラ撫でスフィアを追加しました。");
        }

        if (headSystem != null)
        {
            Undo.RecordObject(headSystem.gameObject, "Initialize Nade HeadSystem");
            headSystem.gameObject.SetActive(false);
            sb.AppendLine("赤夜式 撫で音: [OK] HeadSystemを初期状態に戻しました。");
        }
    }

    private void ApplyNadeContactSettings(StringBuilder sb, GameObject avatarRoot, Transform rxHeadMain, Transform headSystem)
    {
        if (rxHeadMain == null || headSystem == null) return;
        var receiverType = FindType("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver");
        var receiver = receiverType != null ? rxHeadMain.GetComponent(receiverType) : null;
        bool radiusSet = SetFloatMember(receiver, "radius", nadeContactRadius);

        var animator = avatarRoot.GetComponentInChildren<Animator>(true);
        var headBone = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        Vector3 headPosition = headBone != null ? headBone.position : avatarRoot.transform.position;
        if (headBone == null) sb.AppendLine("赤夜式 撫で音: [WARN] Head boneを取得できないためAvatar Root位置を使用します。");

        var descriptor = avatarRoot.GetComponent("VRCAvatarDescriptor");
        Vector3 viewPosition;
        if (!TryGetVector3Member(descriptor, "ViewPosition", out viewPosition))
        {
            viewPosition = avatarRoot.transform.InverseTransformPoint(headPosition);
            sb.AppendLine("赤夜式 撫で音: [WARN] ViewPositionを取得できないためHead bone位置を使用します。");
        }
        Undo.RecordObjects(new UnityEngine.Object[] { rxHeadMain, headSystem }, "Configure Nade contacts");
        rxHeadMain.position = headPosition + avatarRoot.transform.up * nadeHeadOffsetY;
        headSystem.position = avatarRoot.transform.TransformPoint(viewPosition);
        sb.AppendLine(string.Format("赤夜式 撫で音: [{0}] Contact Radius={1}, Contact Offset Y={2} を適用しました。", radiusSet ? "OK" : "WARN", nadeContactRadius, nadeHeadOffsetY));
    }

    private static bool SetFloatMember(object target, string name, float value)
    {
        if (target == null) return false;
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(float)) { Undo.RecordObject((UnityEngine.Object)target, "Configure Nade Contact Radius"); field.SetValue(target, value); return true; }
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && property.PropertyType == typeof(float)) { Undo.RecordObject((UnityEngine.Object)target, "Configure Nade Contact Radius"); property.SetValue(target, value, null); return true; }
        return false;
    }

    private static bool TryGetVector3Member(object target, string name, out Vector3 value)
    {
        value = default(Vector3);
        if (target == null) return false;
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(Vector3)) { value = (Vector3)field.GetValue(target); return true; }
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(Vector3)) { value = (Vector3)property.GetValue(target, null); return true; }
        return false;
    }

    private Transform RequireNadeTransform(StringBuilder sb, Transform root, string path)
    {
        var found = root != null ? root.Find(path) : null;
        if (found == null && root != null && path.IndexOf('/') < 0)
            found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == path);
        if (found == null) sb.AppendLine("赤夜式 撫で音: [ERROR] 必須オブジェクト '" + path + "' が見つかりません。NadeSystem Prefabの構造が想定と異なる可能性があります。");
        return found;
    }

    private GameObject InstantiateNadePrefab(GameObject prefab, Transform parent, StringBuilder sb, string label)
    {
        if (prefab == null) { sb.AppendLine("赤夜式 撫で音: [ERROR] " + label + " Prefabが見つかりません。"); return null; }
        if (parent == null) { sb.AppendLine("赤夜式 撫で音: [ERROR] " + label + " の追加先が見つかりません。"); return null; }
        var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (instance == null) { sb.AppendLine("赤夜式 撫で音: [ERROR] " + label + " の追加に失敗しました。"); return null; }
        Undo.RegisterCreatedObjectUndo(instance, "Add " + label);
        return instance;
    }

    private GameObject FindNadePrefab(string guid, string prefabName)
    {
        GameObject cached;
        if (nadePrefabCache.TryGetValue(prefabName, out cached)) return cached;
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) { nadePrefabCache[prefabName] = prefab; return prefab; }
        foreach (var candidateGuid in AssetDatabase.FindAssets("t:Prefab " + prefabName))
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(candidateGuid));
            if (prefab != null && prefab.name == prefabName) { nadePrefabCache[prefabName] = prefab; return prefab; }
        }
        nadePrefabCache[prefabName] = null;
        return null;
    }

    private static string OnOff(bool value) { return value ? "ON" : "OFF"; }

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
        var cacheKey = string.Join("|", names);
        if (prefabPathCache.TryGetValue(cacheKey, out var cachedPath))
            return string.IsNullOrEmpty(cachedPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(cachedPath);

        // LightLimitChanger 固有の既知パスは、そのファミリーを検索する場合だけ確認する。
        if (ReferenceEquals(names, LLCPrefabNames))
        {
            foreach (var path in LLCPrefabPaths)
            {
                var explicitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (explicitPrefab != null && names.Contains(explicitPrefab.name))
                {
                    prefabPathCache[cacheKey] = path;
                    return explicitPrefab;
                }
            }
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
                {
                    prefabPathCache[cacheKey] = path;
                    return prefab;
                }
            }
        }

        prefabPathCache[cacheKey] = "";
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
        if (TypeCache.TryGetValue(fullName, out var cached))
            return cached;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = asm.GetType(fullName);
                if (type != null)
                {
                    TypeCache[fullName] = type;
                    return type;
                }
            }
            catch
            {
                // ignored
            }
        }
        TypeCache[fullName] = null;
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
