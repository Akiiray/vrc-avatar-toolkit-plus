using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    /// <summary>
    /// VRC Avatar Toolkit Plus / Avatar Material Copy
    ///
    /// 目的:
    /// - Prefab Asset / Hierarchy上のアバターで参照しているMaterialを複製する。
    /// - 複製したMaterialへRenderer参照を差し替え、複製元アバターへの意図しない影響を防ぐ。
    /// - 使用Material一覧を表示し、手動差し替えにも対応する。
    /// - 右クリック用の簡易実行と、細かく指定できるEditorWindowの両方を提供する。
    /// </summary>
    public sealed class AvatarMaterialCopyWindow : EditorWindow
    {
        private const string WindowTitle = "Avatar Material Copy";
        private const string LogWindowTitle = ToolkitConstants.ProductName + " - Material Copy Log";

        private enum TargetMode
        {
            選択中のヒエラルキー,
            選択中のPrefab,
        }

        private enum ExecuteMode
        {
            選択対象のMaterialを複製して差し替え,
            PrefabAssetを複製してMaterialも複製差し替え,
            手動指定Materialへ差し替え
        }

        private sealed class MaterialUsage
        {
            public bool Selected = true;
            public Material Original;
            public Material Replacement;
            public string OriginalPath;
            public string SuggestedPath;
            public int RendererCount;
            public int SlotCount;
            public readonly List<string> Sources = new List<string>();
        }

        private sealed class ScanTarget
        {
            public bool IsPrefabAsset;
            public GameObject RootObject;
            public string PrefabAssetPath;
            public string Label;
        }

        private TargetMode _targetMode = TargetMode.選択中のヒエラルキー;
        private ExecuteMode _executeMode = ExecuteMode.選択対象のMaterialを複製して差し替え;
        private bool _preferAvatarRoot = true;
        private bool _includeInactive = true;
        private bool _onlySelectedMaterials = true;
        private bool _createMaterialFolder = true;
        private bool _selectCreatedAsset = true;
        private DefaultAsset _destinationFolder;
        private string _materialFolderSuffix = "_Materials";
        private string _duplicatedPrefabSuffix = "_Copy";

        private readonly List<GameObject> _hierarchyObjects = new List<GameObject>();
        private readonly List<GameObject> _prefabAssets = new List<GameObject>();
        private readonly List<MaterialUsage> _usages = new List<MaterialUsage>();
        private readonly List<ScanTarget> _targets = new List<ScanTarget>();
        private Vector2 _scroll;
        private Vector2 _usageScroll;
        private string _lastLog = string.Empty;

        [MenuItem("Tools/VRC Avatar Toolkit Plus/Material/Material Copy Window")]
        public static void OpenWindow()
        {
            var window = GetWindow<AvatarMaterialCopyWindow>(WindowTitle);
            window.minSize = new Vector2(860, 640);
            window.PullCurrentSelection();
            window.Scan();
            window.Show();
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/Material/マテリアル複製・差し替えウィンドウ", false, 32)]
        private static void OpenWindowFromHierarchy()
        {
            OpenWindow();
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/Material/マテリアル複製・差し替えウィンドウ", true)]
        private static bool ValidateOpenWindowFromHierarchy()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("Assets/VRC Avatar Toolkit Plus/Material/マテリアル複製・差し替えウィンドウ", false, 2102)]
        private static void OpenWindowFromAssets()
        {
            OpenWindow();
        }

        [MenuItem("Assets/VRC Avatar Toolkit Plus/Material/Prefabをマテリアルごと複製", false, 2103)]
        private static void DuplicateSelectedPrefabAssetsWithMaterials()
        {
            var prefabPaths = ToolkitSelectionUtility.GetSelectedPrefabAssetPaths();
            if (prefabPaths.Length == 0)
            {
                EditorUtility.DisplayDialog(WindowTitle, "Prefab Assetを選択してください。", "OK");
                return;
            }

            var report = new ToolkitReportBuilder("Prefab Material Copy - Quick Duplicate");
            var createdAssets = new List<UnityEngine.Object>();
            foreach (string path in prefabPaths)
            {
                DuplicatePrefabAssetWithMaterials(path, null, "_Copy", "_Materials", true, report, createdAssets);
            }
            FinishQuickAction(report, createdAssets);
        }

        [MenuItem("Assets/VRC Avatar Toolkit Plus/Material/Prefabをマテリアルごと複製", true)]
        private static bool ValidateDuplicateSelectedPrefabAssetsWithMaterials()
        {
            return ToolkitSelectionUtility.GetSelectedPrefabAssetPaths().Length > 0;
        }

        [MenuItem("Assets/VRC Avatar Toolkit Plus/Material/Prefab内マテリアルを複製して差し替え", false, 2104)]
        private static void ReplaceSelectedPrefabAssetMaterials()
        {
            var prefabPaths = ToolkitSelectionUtility.GetSelectedPrefabAssetPaths();
            if (prefabPaths.Length == 0)
            {
                EditorUtility.DisplayDialog(WindowTitle, "Prefab Assetを選択してください。", "OK");
                return;
            }

            var report = new ToolkitReportBuilder("Prefab Material Copy - Quick Replace");
            var createdAssets = new List<UnityEngine.Object>();
            foreach (string path in prefabPaths)
            {
                DuplicateMaterialsAndReplacePrefabAsset(path, null, "_Materials", true, report, createdAssets);
            }
            FinishQuickAction(report, createdAssets);
        }

        [MenuItem("Assets/VRC Avatar Toolkit Plus/Material/Prefab内マテリアルを複製して差し替え", true)]
        private static bool ValidateReplaceSelectedPrefabAssetMaterials()
        {
            return ToolkitSelectionUtility.GetSelectedPrefabAssetPaths().Length > 0;
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/Material/マテリアルを複製して差し替え", false, 33)]
        private static void ReplaceSelectedHierarchyMaterials()
        {
            var roots = ToolkitSelectionUtility.NormalizeHierarchyRoots(Selection.gameObjects, preferPrefabInstanceRoot: true, requireAvatarDescriptor: false);
            if (roots.Length == 0)
            {
                EditorUtility.DisplayDialog(WindowTitle, "Hierarchy上のGameObjectを選択してください。", "OK");
                return;
            }

            var report = new ToolkitReportBuilder("Hierarchy Material Copy - Quick Replace");
            var createdAssets = new List<UnityEngine.Object>();
            foreach (var root in roots)
            {
                DuplicateMaterialsAndReplaceHierarchy(root, null, "_Materials", true, report, createdAssets);
            }
            FinishQuickAction(report, createdAssets);
        }

        [MenuItem("GameObject/VRC Avatar Toolkit Plus/Material/マテリアルを複製して差し替え", true)]
        private static bool ValidateReplaceSelectedHierarchyMaterials()
        {
            return Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("VRC Avatar Toolkit Plus / Avatar Material Copy", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "アバターが参照しているMaterialを複製し、Rendererの参照を複製後Materialへ差し替えます。複製元と複製後アバターが同じMaterial Assetを共有してしまう事故を防ぐためのツールです。",
                MessageType.Info);

            DrawTargetArea();
            EditorGUILayout.Space(8);
            DrawOptions();
            EditorGUILayout.Space(8);
            DrawActionButtons();
            EditorGUILayout.Space(8);
            DrawUsageList();
            EditorGUILayout.Space(8);
            DrawLogButtons();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetArea()
        {
            EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUI.BeginChangeCheck();
                _targetMode = (TargetMode)EditorGUILayout.EnumPopup("対象モード", _targetMode);
                if (EditorGUI.EndChangeCheck()) PullCurrentSelection();

                _preferAvatarRoot = EditorGUILayout.ToggleLeft("VRCAvatarDescriptorが見つかる場合はアバタールートを対象にする", _preferAvatarRoot);
                _includeInactive = EditorGUILayout.ToggleLeft("非アクティブObjectも走査", _includeInactive);

                if (_targetMode == TargetMode.選択中のヒエラルキー)
                {
                    EditorGUILayout.LabelField("Hierarchy選択", _hierarchyObjects.Count + " 件");
                    DrawObjectList(_hierarchyObjects, allowSceneObjects: true);
                }
                else
                {
                    EditorGUILayout.LabelField("Prefab選択", _prefabAssets.Count + " 件");
                    DrawObjectList(_prefabAssets, allowSceneObjects: false);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("現在の選択を取り込む", GUILayout.Width(180)))
                    {
                        PullCurrentSelection();
                        Scan();
                    }
                    if (GUILayout.Button("対象クリア", GUILayout.Width(120)))
                    {
                        _hierarchyObjects.Clear();
                        _prefabAssets.Clear();
                        _targets.Clear();
                        _usages.Clear();
                    }
                }
            }
        }

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("実行設定", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                _executeMode = (ExecuteMode)EditorGUILayout.EnumPopup("実行モード", _executeMode);
                _onlySelectedMaterials = EditorGUILayout.ToggleLeft("一覧でチェックONのMaterialだけ処理", _onlySelectedMaterials);
                _createMaterialFolder = EditorGUILayout.ToggleLeft("対象ごとのMaterial保存フォルダを作成", _createMaterialFolder);
                using (new EditorGUI.DisabledScope(!_createMaterialFolder))
                {
                    _materialFolderSuffix = EditorGUILayout.TextField("Materialフォルダ接尾辞", _materialFolderSuffix);
                }
                _duplicatedPrefabSuffix = EditorGUILayout.TextField("複製Prefab接尾辞", _duplicatedPrefabSuffix);
                _destinationFolder = (DefaultAsset)EditorGUILayout.ObjectField("保存先フォルダ（未指定なら対象の近く）", _destinationFolder, typeof(DefaultAsset), false);
                _selectCreatedAsset = EditorGUILayout.ToggleLeft("作成/変更したAssetを選択", _selectCreatedAsset);
            }
        }

        private void DrawActionButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Material一覧を更新"))
                {
                    Scan();
                }

                if (GUILayout.Button("実行"))
                {
                    Execute();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("すべて選択", GUILayout.Width(120)))
                {
                    foreach (var usage in _usages) usage.Selected = true;
                }
                if (GUILayout.Button("すべて解除", GUILayout.Width(120)))
                {
                    foreach (var usage in _usages) usage.Selected = false;
                }
                if (GUILayout.Button("Replacementをクリア", GUILayout.Width(170)))
                {
                    foreach (var usage in _usages) usage.Replacement = null;
                }
            }
        }

        private void DrawUsageList()
        {
            EditorGUILayout.LabelField("使用Material一覧", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("検出対象", _targets.Count + " 件");
            EditorGUILayout.LabelField("Material", _usages.Count + " 件");

            _usageScroll = EditorGUILayout.BeginScrollView(_usageScroll, GUILayout.MinHeight(260));
            foreach (var usage in _usages)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        usage.Selected = EditorGUILayout.Toggle(usage.Selected, GUILayout.Width(20));
                        EditorGUILayout.ObjectField("Original", usage.Original, typeof(Material), false);
                    }

                    usage.Replacement = (Material)EditorGUILayout.ObjectField("Replacement", usage.Replacement, typeof(Material), false);
                    EditorGUILayout.LabelField("Original Path", string.IsNullOrEmpty(usage.OriginalPath) ? "<not asset>" : usage.OriginalPath);
                    EditorGUILayout.LabelField("Suggested Copy", usage.SuggestedPath);
                    EditorGUILayout.LabelField("Usage", usage.RendererCount + " Renderer / " + usage.SlotCount + " Slot");

                    if (usage.Sources.Count > 0)
                    {
                        EditorGUILayout.LabelField("Sources", usage.Sources.Count > 3 ? string.Join(" / ", usage.Sources.Take(3)) + " ..." : string.Join(" / ", usage.Sources));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawLogButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("最後のログを開く"))
                {
                    ToolkitLogWindow.Open(LogWindowTitle, _lastLog);
                }
                if (GUILayout.Button("ログをコピー"))
                {
                    EditorGUIUtility.systemCopyBuffer = _lastLog ?? string.Empty;
                }
                if (GUILayout.Button("Consoleへ出力"))
                {
                    Debug.Log(_lastLog ?? string.Empty);
                }
            }
        }

        private static void DrawObjectList(List<GameObject> list, bool allowSceneObjects)
        {
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("なし");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = (GameObject)EditorGUILayout.ObjectField(list[i], typeof(GameObject), allowSceneObjects);
                    if (GUILayout.Button("削除", GUILayout.Width(60)))
                    {
                        list.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        private void PullCurrentSelection()
        {
            if (_targetMode == TargetMode.選択中のヒエラルキー)
            {
                _hierarchyObjects.Clear();
                foreach (var go in ToolkitSelectionUtility.NormalizeHierarchyRoots(Selection.gameObjects, preferPrefabInstanceRoot: true, requireAvatarDescriptor: false))
                {
                    var root = _preferAvatarRoot ? ToolkitSelectionUtility.FindAvatarRoot(go) ?? go : go;
                    if (root != null && !_hierarchyObjects.Contains(root)) _hierarchyObjects.Add(root);
                }
            }
            else
            {
                _prefabAssets.Clear();
                foreach (string path in ToolkitSelectionUtility.GetSelectedPrefabAssetPaths())
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !_prefabAssets.Contains(prefab)) _prefabAssets.Add(prefab);
                }
            }
        }

        private void Scan()
        {
            _targets.Clear();
            _usages.Clear();

            BuildTargets(_targets);
            var map = new Dictionary<Material, MaterialUsage>();
            foreach (var target in _targets)
            {
                CollectUsagesFromRoot(target.RootObject, target.Label, _includeInactive, map);
            }

            _usages.AddRange(map.Values.OrderBy(x => x.Original != null ? x.Original.name : string.Empty, StringComparer.OrdinalIgnoreCase));
            RefreshSuggestedPaths();

            var report = new ToolkitReportBuilder("Avatar Material Copy Scan");
            report.KeyValue("Target Count", _targets.Count);
            report.KeyValue("Material Count", _usages.Count);
            foreach (var usage in _usages)
            {
                report.Line("- " + GetMaterialLabel(usage.Original) + " / " + usage.SlotCount + " slots / " + usage.OriginalPath);
            }
            _lastLog = report.ToString();
        }

        private void BuildTargets(List<ScanTarget> targets)
        {
            if (_targetMode == TargetMode.選択中のヒエラルキー)
            {
                foreach (var go in _hierarchyObjects.Where(x => x != null))
                {
                    var root = _preferAvatarRoot ? ToolkitSelectionUtility.FindAvatarRoot(go) ?? go : go;
                    if (root == null || targets.Any(t => t.RootObject == root)) continue;
                    targets.Add(new ScanTarget
                    {
                        IsPrefabAsset = false,
                        RootObject = root,
                        Label = ToolkitPathUtility.GetHierarchyPath(root)
                    });
                }
            }
            else
            {
                foreach (var prefab in _prefabAssets.Where(x => x != null))
                {
                    string path = AssetDatabase.GetAssetPath(prefab);
                    if (!ToolkitSelectionUtility.IsPrefabAssetPath(path) || targets.Any(t => t.PrefabAssetPath == path)) continue;
                    targets.Add(new ScanTarget
                    {
                        IsPrefabAsset = true,
                        RootObject = prefab,
                        PrefabAssetPath = path,
                        Label = path
                    });
                }
            }
        }

        private void RefreshSuggestedPaths()
        {
            string baseFolder = GetDestinationFolderPath();
            string targetName = _targets.Count == 1 ? SanitizeFileName(Path.GetFileNameWithoutExtension(_targets[0].IsPrefabAsset ? _targets[0].PrefabAssetPath : _targets[0].RootObject.name)) : "Materials";
            string materialFolder = _createMaterialFolder ? CombineAssetPath(baseFolder, targetName + SafeSuffix(_materialFolderSuffix, "_Materials")) : baseFolder;

            foreach (var usage in _usages)
            {
                string originalName = usage.Original != null ? usage.Original.name : "Material";
                usage.OriginalPath = usage.Original != null ? AssetDatabase.GetAssetPath(usage.Original) : string.Empty;
                usage.SuggestedPath = AssetDatabase.GenerateUniqueAssetPath(CombineAssetPath(materialFolder, SanitizeFileName(originalName) + ".mat"));
            }
        }

        private void Execute()
        {
            if (_executeMode != ExecuteMode.手動指定Materialへ差し替え)
            {
                RefreshSuggestedPaths();
            }

            var report = new ToolkitReportBuilder("Avatar Material Copy Execute");
            var createdAssets = new List<UnityEngine.Object>();

            BuildTargets(_targets);
            if (_targets.Count == 0)
            {
                report.Warning("対象がありません。Hierarchy上のアバター、またはPrefab Assetを選択してください。");
                CommitReport(report, createdAssets);
                return;
            }

            foreach (var target in _targets)
            {
                report.Section(target.Label);
                if (_executeMode == ExecuteMode.PrefabAssetを複製してMaterialも複製差し替え)
                {
                    if (!target.IsPrefabAsset)
                    {
                        report.Warning("Prefab Asset複製モードはAssets上のPrefabだけが対象です: " + target.Label);
                        continue;
                    }
                    DuplicatePrefabAssetWithMaterials(target.PrefabAssetPath, GetDestinationFolderPathOrNull(), SafeSuffix(_duplicatedPrefabSuffix, "_Copy"), SafeSuffix(_materialFolderSuffix, "_Materials"), _createMaterialFolder, report, createdAssets, _onlySelectedMaterials ? GetSelectedOriginalMaterials() : null);
                }
                else if (_executeMode == ExecuteMode.選択対象のMaterialを複製して差し替え)
                {
                    if (target.IsPrefabAsset)
                    {
                        DuplicateMaterialsAndReplacePrefabAsset(target.PrefabAssetPath, GetDestinationFolderPathOrNull(), SafeSuffix(_materialFolderSuffix, "_Materials"), _createMaterialFolder, report, createdAssets, _onlySelectedMaterials ? GetSelectedOriginalMaterials() : null);
                    }
                    else
                    {
                        DuplicateMaterialsAndReplaceHierarchy(target.RootObject, GetDestinationFolderPathOrNull(), SafeSuffix(_materialFolderSuffix, "_Materials"), _createMaterialFolder, report, createdAssets, _onlySelectedMaterials ? GetSelectedOriginalMaterials() : null);
                    }
                }
                else
                {
                    ApplyManualReplacements(target, report);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Scan();
            CommitReport(report, createdAssets);
        }

        private HashSet<Material> GetSelectedOriginalMaterials()
        {
            return new HashSet<Material>(_usages.Where(x => x.Selected && x.Original != null).Select(x => x.Original));
        }

        private void ApplyManualReplacements(ScanTarget target, ToolkitReportBuilder report)
        {
            var replacementMap = _usages
                .Where(x => (!_onlySelectedMaterials || x.Selected) && x.Original != null && x.Replacement != null && x.Original != x.Replacement)
                .ToDictionary(x => x.Original, x => x.Replacement);

            if (replacementMap.Count == 0)
            {
                report.Warning("Replacementが指定されたMaterialがありません。");
                return;
            }

            if (target.IsPrefabAsset)
            {
                using (var scope = new ToolkitPrefabEditScope(target.PrefabAssetPath))
                {
                    int replaced = ReplaceMaterialsInRoot(scope.Root, replacementMap, true, report);
                    if (replaced > 0)
                    {
                        scope.Save();
                        report.Info("Saved prefab: " + target.PrefabAssetPath);
                    }
                }
            }
            else
            {
                int replaced = ReplaceMaterialsInRoot(target.RootObject, replacementMap, true, report);
                if (replaced > 0) EditorUtility.SetDirty(target.RootObject);
            }
        }

        private static void DuplicatePrefabAssetWithMaterials(
            string prefabAssetPath,
            string destinationFolder,
            string prefabSuffix,
            string materialFolderSuffix,
            bool createMaterialFolder,
            ToolkitReportBuilder report,
            List<UnityEngine.Object> createdAssets,
            HashSet<Material> allowedMaterials = null)
        {
            if (!ToolkitSelectionUtility.IsPrefabAssetPath(prefabAssetPath))
            {
                report.Warning("Prefab Assetではありません: " + prefabAssetPath);
                return;
            }

            string sourceFolder = Path.GetDirectoryName(prefabAssetPath).Replace('\\', '/');
            string prefabName = Path.GetFileNameWithoutExtension(prefabAssetPath);
            string outputFolder = string.IsNullOrEmpty(destinationFolder) ? sourceFolder : destinationFolder;
            EnsureFolder(outputFolder);

            string duplicatedPrefabPath = AssetDatabase.GenerateUniqueAssetPath(CombineAssetPath(outputFolder, SanitizeFileName(prefabName + prefabSuffix) + ".prefab"));
            if (!AssetDatabase.CopyAsset(prefabAssetPath, duplicatedPrefabPath))
            {
                report.Error("Prefab複製に失敗しました: " + prefabAssetPath);
                return;
            }

            report.Info("Prefab copied: " + duplicatedPrefabPath);
            var duplicatedPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(duplicatedPrefabPath);
            if (duplicatedPrefabAsset != null) createdAssets.Add(duplicatedPrefabAsset);

            DuplicateMaterialsAndReplacePrefabAsset(duplicatedPrefabPath, outputFolder, materialFolderSuffix, createMaterialFolder, report, createdAssets, allowedMaterials);
        }

        private static void DuplicateMaterialsAndReplacePrefabAsset(
            string prefabAssetPath,
            string destinationFolder,
            string materialFolderSuffix,
            bool createMaterialFolder,
            ToolkitReportBuilder report,
            List<UnityEngine.Object> createdAssets,
            HashSet<Material> allowedMaterials = null)
        {
            if (!ToolkitSelectionUtility.IsPrefabAssetPath(prefabAssetPath))
            {
                report.Warning("Prefab Assetではありません: " + prefabAssetPath);
                return;
            }

            using (var scope = new ToolkitPrefabEditScope(prefabAssetPath))
            {
                string sourceFolder = Path.GetDirectoryName(prefabAssetPath).Replace('\\', '/');
                string baseFolder = string.IsNullOrEmpty(destinationFolder) ? sourceFolder : destinationFolder;
                string materialFolder = createMaterialFolder ? CombineAssetPath(baseFolder, SanitizeFileName(Path.GetFileNameWithoutExtension(prefabAssetPath)) + materialFolderSuffix) : baseFolder;
                EnsureFolder(materialFolder);

                var map = DuplicateMaterialsForRoot(scope.Root, materialFolder, allowedMaterials, report, createdAssets);
                int replaced = ReplaceMaterialsInRoot(scope.Root, map, true, report);
                if (replaced > 0)
                {
                    scope.Save();
                    report.Info("Saved prefab: " + prefabAssetPath);
                }
                else
                {
                    report.Info("差し替え対象Materialはありませんでした: " + prefabAssetPath);
                }
            }
        }

        private static void DuplicateMaterialsAndReplaceHierarchy(
            GameObject root,
            string destinationFolder,
            string materialFolderSuffix,
            bool createMaterialFolder,
            ToolkitReportBuilder report,
            List<UnityEngine.Object> createdAssets,
            HashSet<Material> allowedMaterials = null)
        {
            if (root == null)
            {
                report.Warning("Hierarchy対象がnullです。");
                return;
            }

            string baseFolder = string.IsNullOrEmpty(destinationFolder) ? GetDefaultFolderForHierarchy(root) : destinationFolder;
            string materialFolder = createMaterialFolder ? CombineAssetPath(baseFolder, SanitizeFileName(root.name) + materialFolderSuffix) : baseFolder;
            EnsureFolder(materialFolder);

            var map = DuplicateMaterialsForRoot(root, materialFolder, allowedMaterials, report, createdAssets);
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Duplicate Avatar Materials");
            int replaced = ReplaceMaterialsInRoot(root, map, true, report);
            if (replaced > 0)
            {
                EditorUtility.SetDirty(root);
                Undo.CollapseUndoOperations(undoGroup);
                report.Info("Hierarchy materials replaced: " + ToolkitPathUtility.GetHierarchyPath(root));
            }
            else
            {
                report.Info("差し替え対象Materialはありませんでした: " + ToolkitPathUtility.GetHierarchyPath(root));
            }
        }

        private static Dictionary<Material, Material> DuplicateMaterialsForRoot(
            GameObject root,
            string materialFolder,
            HashSet<Material> allowedMaterials,
            ToolkitReportBuilder report,
            List<UnityEngine.Object> createdAssets)
        {
            var usages = new Dictionary<Material, MaterialUsage>();
            CollectUsagesFromRoot(root, root != null ? root.name : "<null>", true, usages);

            var map = new Dictionary<Material, Material>();
            foreach (var usage in usages.Values.OrderBy(x => x.Original != null ? x.Original.name : string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                if (usage.Original == null) continue;
                if (allowedMaterials != null && !allowedMaterials.Contains(usage.Original)) continue;

                var duplicated = DuplicateMaterialAsset(usage.Original, materialFolder, report);
                if (duplicated == null) continue;

                map[usage.Original] = duplicated;
                createdAssets.Add(duplicated);
                report.Line("Material copied: " + GetMaterialLabel(usage.Original) + " -> " + AssetDatabase.GetAssetPath(duplicated));
            }

            return map;
        }

        private static Material DuplicateMaterialAsset(Material original, string materialFolder, ToolkitReportBuilder report)
        {
            if (original == null) return null;
            EnsureFolder(materialFolder);

            string targetPath = AssetDatabase.GenerateUniqueAssetPath(CombineAssetPath(materialFolder, SanitizeFileName(original.name) + ".mat"));
            string originalPath = AssetDatabase.GetAssetPath(original);

            if (!string.IsNullOrEmpty(originalPath))
            {
                if (AssetDatabase.CopyAsset(originalPath, targetPath))
                {
                    return AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                }

                report.Warning("Material Assetのコピーに失敗したため、新規Materialとして作成します: " + originalPath);
            }

            var copy = new Material(original)
            {
                name = Path.GetFileNameWithoutExtension(targetPath)
            };
            AssetDatabase.CreateAsset(copy, targetPath);
            return copy;
        }

        private static int ReplaceMaterialsInRoot(GameObject root, Dictionary<Material, Material> replacementMap, bool recordUndo, ToolkitReportBuilder report)
        {
            if (root == null || replacementMap == null || replacementMap.Count == 0) return 0;

            int replacedSlots = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0) continue;

                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && replacementMap.TryGetValue(materials[i], out var replacement) && replacement != null)
                    {
                        materials[i] = replacement;
                        changed = true;
                        replacedSlots++;
                    }
                }

                if (!changed) continue;
                if (recordUndo) Undo.RecordObject(renderer, "Replace Avatar Materials");
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                report.Line("Renderer replaced: " + ToolkitPathUtility.GetHierarchyPath(renderer));
            }

            report.KeyValue("Replaced Slots", replacedSlots);
            return replacedSlots;
        }

        private static void CollectUsagesFromRoot(GameObject root, string label, bool includeInactive, Dictionary<Material, MaterialUsage> map)
        {
            if (root == null || map == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                if (materials == null) continue;

                var distinctInRenderer = new HashSet<Material>();
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat == null) continue;

                    if (!map.TryGetValue(mat, out var usage))
                    {
                        usage = new MaterialUsage
                        {
                            Original = mat,
                            OriginalPath = AssetDatabase.GetAssetPath(mat)
                        };
                        map.Add(mat, usage);
                    }

                    usage.SlotCount++;
                    if (distinctInRenderer.Add(mat)) usage.RendererCount++;
                    string source = label + " :: " + ToolkitPathUtility.GetHierarchyPath(renderer) + " [" + i + "]";
                    if (usage.Sources.Count < 10) usage.Sources.Add(source);
                }
            }
        }

        private string GetDestinationFolderPath()
        {
            string folder = GetDestinationFolderPathOrNull();
            if (!string.IsNullOrEmpty(folder)) return folder;

            if (_targets.Count == 1)
            {
                var target = _targets[0];
                if (target.IsPrefabAsset && !string.IsNullOrEmpty(target.PrefabAssetPath))
                    return Path.GetDirectoryName(target.PrefabAssetPath).Replace('\\', '/');
                if (target.RootObject != null)
                    return GetDefaultFolderForHierarchy(target.RootObject);
            }

            return "Assets";
        }

        private string GetDestinationFolderPathOrNull()
        {
            if (_destinationFolder == null) return null;
            string path = AssetDatabase.GetAssetPath(_destinationFolder);
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        private static string GetDefaultFolderForHierarchy(GameObject root)
        {
            if (root != null)
            {
                var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(root);
                string prefabPath = prefabAsset != null ? AssetDatabase.GetAssetPath(prefabAsset) : string.Empty;
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    return Path.GetDirectoryName(prefabPath).Replace('\\', '/');
                }
            }

            return "Assets";
        }

        private static void FinishQuickAction(ToolkitReportBuilder report, List<UnityEngine.Object> createdAssets)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (createdAssets != null && createdAssets.Count > 0)
            {
                Selection.objects = createdAssets.Where(x => x != null).ToArray();
            }
            ToolkitLogWindow.Open(LogWindowTitle, report);
        }

        private void CommitReport(ToolkitReportBuilder report, List<UnityEngine.Object> createdAssets)
        {
            _lastLog = report.ToString();
            Debug.Log(_lastLog);
            ToolkitLogWindow.Open(LogWindowTitle, _lastLog);
            if (_selectCreatedAsset && createdAssets != null && createdAssets.Count > 0)
            {
                Selection.objects = createdAssets.Where(x => x != null).ToArray();
            }
        }

        private static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            if (!folderPath.StartsWith("Assets", StringComparison.Ordinal))
                throw new ArgumentException("Assets配下のフォルダを指定してください: " + folderPath);

            string current = "Assets";
            string[] parts = folderPath.Substring("Assets".Length).Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (string.IsNullOrEmpty(part)) continue;
                string next = CombineAssetPath(current, part);
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, part);
                }
                current = next;
            }
        }

        private static string CombineAssetPath(string folder, string fileOrFolder)
        {
            if (string.IsNullOrEmpty(folder)) return fileOrFolder ?? string.Empty;
            if (string.IsNullOrEmpty(fileOrFolder)) return folder.Replace('\\', '/');
            return folder.TrimEnd('/').Replace('\\', '/') + "/" + fileOrFolder.TrimStart('/').Replace('\\', '/');
        }

        private static string SafeSuffix(string suffix, string fallback)
        {
            return string.IsNullOrEmpty(suffix) ? fallback : suffix;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "NewAsset";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }

        private static string GetMaterialLabel(Material material)
        {
            if (material == null) return "<null>";
            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? material.name : material.name + " (" + path + ")";
        }
    }
}
