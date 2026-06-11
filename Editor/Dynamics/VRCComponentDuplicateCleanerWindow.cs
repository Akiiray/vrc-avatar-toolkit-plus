using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
/// <summary>
/// VRC Avatar Toolkit Plus / VRC Component Duplicate Cleaner
///
/// 目的:
/// - 同一GameObjectに複数付いたVRC系コンポーネントを検出する。
/// - PhysBone / PhysBoneCollider / ContactReceiver / ContactSender を対象にできる。
/// - 判定レベルに応じて設定が同一とみなせるものだけを修正候補にする。
/// - 検出後から修正前までの状態変化をFingerprintで検出する。
/// - Hierarchy / Project Prefab / Folder配下Prefab に対応する。
///
/// 既存の AvatarDebugReportWindow がある場合は同じログウィンドウへ出力する。
/// </summary>
public sealed class VRCComponentDuplicateCleanerWindow : EditorWindow
{
    private const string WindowTitle = "VRC重複コンポーネント検出・修正";
    private const string LogWindowTitle = "VRC Avatar Toolkit Plus - 重複コンポーネントログ";

    private enum TargetMode
    {
        選択中のヒエラルキー,
        選択中のPrefab,
        指定フォルダ内Prefab
    }

    private enum DetectionLevel
    {
        レベル1_基本 = 1,
        レベル2_詳細 = 2,
        レベル3_完全比較 = 3
    }

    private enum ChangedStatePolicy
    {
        中断する,
        変更項目のみスキップ,
        警告して続行
    }

    private enum VrcComponentKind
    {
        PhysBone,
        PhysBoneCollider,
        ContactReceiver,
        ContactSender
    }

    private sealed class ComponentTargetDef
    {
        public VrcComponentKind Kind;
        public string Label;
        public string FullName;
        public string ShortTypeName;
        public string[] Level1Properties;
        public string[] Level2Properties;
    }

    private sealed class ComponentSnapshot
    {
        public Component Component;
        public ComponentTargetDef Def;
        public string Signature;
        public string DetailText;
    }

    private sealed class DuplicateGroup
    {
        public string Signature;
        public List<int> Indexes = new List<int>();
    }

    private sealed class ScanResult
    {
        public bool Selected = true;
        public bool Expanded;
        public bool IsPrefabAsset;
        public VrcComponentKind Kind;
        public string ComponentLabel;
        public string ComponentFullName;
        public GameObject RootObject;
        public string PrefabAssetPath;
        public string RootLabel;
        public string ObjectPath;
        public string DisplayName;
        public int ComponentCountAtScan;
        public int DuplicateRemoveCandidateCount;
        public string FingerprintAtScan;
        public string DetailsAtScan;
        public List<DuplicateGroup> DuplicateGroups = new List<DuplicateGroup>();
    }

    private sealed class ScanRoot
    {
        public bool IsPrefabAsset;
        public GameObject RootObject;
        public string PrefabAssetPath;
        public string Label;
    }

    private static readonly ComponentTargetDef[] ComponentDefs =
    {
        new ComponentTargetDef
        {
            Kind = VrcComponentKind.PhysBone,
            Label = "VRCPhysBone",
            FullName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone",
            ShortTypeName = "VRCPhysBone",
            Level1Properties = new[]
            {
                "rootTransform", "pull", "spring", "stiffness", "gravity", "allowCollision"
            },
            Level2Properties = new[]
            {
                "rootTransform", "pull", "spring", "stiffness", "gravity", "gravityFalloff", "immobile", "limitType",
                "maxAngleX", "maxAngleZ", "limitRotation", "radius", "endpointPosition", "multiChildType",
                "allowCollision", "colliders", "allowGrabbing", "allowPosing", "grabMovement", "maxStretch", "maxSquish",
                "stretchMotion", "isAnimated", "ignoreTransforms", "parameter"
            }
        },
        new ComponentTargetDef
        {
            Kind = VrcComponentKind.PhysBoneCollider,
            Label = "VRCPhysBoneCollider",
            FullName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider",
            ShortTypeName = "VRCPhysBoneCollider",
            Level1Properties = new[]
            {
                "rootTransform", "shapeType", "radius", "height", "position", "rotation"
            },
            Level2Properties = new[]
            {
                "rootTransform", "shapeType", "radius", "height", "position", "rotation", "insideBounds", "bonesAsSpheres", "colliders"
            }
        },
        new ComponentTargetDef
        {
            Kind = VrcComponentKind.ContactReceiver,
            Label = "VRCContactReceiver",
            FullName = "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver",
            ShortTypeName = "VRCContactReceiver",
            Level1Properties = new[]
            {
                "rootTransform", "shapeType", "radius", "height", "position", "rotation", "parameter", "receiverType"
            },
            Level2Properties = new[]
            {
                "rootTransform", "shapeType", "radius", "height", "position", "rotation", "collisionTags", "parameter",
                "receiverType", "minVelocity", "allowSelf", "allowOthers", "localOnly"
            }
        },
        new ComponentTargetDef
        {
            Kind = VrcComponentKind.ContactSender,
            Label = "VRCContactSender",
            FullName = "VRC.SDK3.Dynamics.Contact.Components.VRCContactSender",
            ShortTypeName = "VRCContactSender",
            Level1Properties = new[]
            {
                "rootTransform", "shapeType", "radius", "height", "position", "rotation"
            },
            Level2Properties = new[]
            {
                "rootTransform", "shapeType", "radius", "height", "position", "rotation", "collisionTags", "allowSelf", "allowOthers", "localOnly"
            }
        }
    };

    private TargetMode _targetMode = TargetMode.選択中のヒエラルキー;
    private DetectionLevel _detectionLevel = DetectionLevel.レベル2_詳細;
    private ChangedStatePolicy _changedStatePolicy = ChangedStatePolicy.変更項目のみスキップ;
    private bool _allowForceFix;

    private bool _scanPhysBone = true;
    private bool _scanPhysBoneCollider;
    private bool _scanContactReceiver = true;
    private bool _scanContactSender = true;

    private DefaultAsset _folder;
    private readonly List<GameObject> _prefabAssets = new List<GameObject>();
    private readonly List<GameObject> _hierarchyObjects = new List<GameObject>();
    private readonly List<ScanResult> _results = new List<ScanResult>();
    private Vector2 _scroll;
    private Vector2 _resultScroll;
    private string _lastLog = "";

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Dynamics/重複コンポーネント検出・修正ウィンドウ")]
    public static void OpenWindow()
    {
        var window = GetWindow<VRCComponentDuplicateCleanerWindow>(WindowTitle);
        window.minSize = new Vector2(760, 600);
        window.PullCurrentSelection();
        window.Show();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Dynamics/重複コンポーネント検出・修正", false, 30)]
    private static void OpenWindowFromHierarchy()
    {
        OpenWindow();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Dynamics/重複コンポーネント検出・修正", true)]
    private static bool ValidateOpenWindowFromHierarchy()
    {
        return Selection.activeGameObject != null;
    }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Dynamics/重複コンポーネント検出・修正", false, 2100)]
    private static void OpenWindowFromAssets()
    {
        OpenWindow();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("VRC Avatar Toolkit Plus / 重複コンポーネント検出・修正", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "同一GameObjectに複数付いたVRC系コンポーネントを検出します。完全に同一と判定できるものだけを修正候補にします。まず検出、結果確認、個別選択、修正の順で使ってください。",
            MessageType.Info);

        DrawTargetArea();
        EditorGUILayout.Space(8);
        DrawComponentTargetArea();
        EditorGUILayout.Space(8);
        DrawDetectionOptions();
        EditorGUILayout.Space(8);
        DrawActionButtons();
        EditorGUILayout.Space(8);
        DrawResults();
        EditorGUILayout.Space(8);
        DrawLastLogButtons();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTargetArea()
    {
        EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);
        _targetMode = (TargetMode)EditorGUILayout.EnumPopup("対象モード", _targetMode);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("現在の選択を取り込む", GUILayout.Width(180))) PullCurrentSelection();
        if (GUILayout.Button("対象リストをクリア", GUILayout.Width(160)))
        {
            _prefabAssets.Clear();
            _hierarchyObjects.Clear();
            _folder = null;
        }
        EditorGUILayout.EndHorizontal();

        if (_targetMode == TargetMode.指定フォルダ内Prefab)
        {
            _folder = EditorGUILayout.ObjectField("Prefabフォルダ", _folder, typeof(DefaultAsset), false) as DefaultAsset;
            string path = _folder != null ? AssetDatabase.GetAssetPath(_folder) : "";
            if (_folder != null && !AssetDatabase.IsValidFolder(path))
            {
                EditorGUILayout.HelpBox("フォルダを指定してください。", MessageType.Warning);
            }
        }
        else if (_targetMode == TargetMode.選択中のPrefab)
        {
            DrawObjectList(_prefabAssets, "Project Prefab", allowSceneObjects: false);
        }
        else
        {
            DrawObjectList(_hierarchyObjects, "Hierarchy Object", allowSceneObjects: true);
        }
    }

    private void DrawComponentTargetArea()
    {
        EditorGUILayout.LabelField("検出対象コンポーネント", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _scanPhysBone = EditorGUILayout.ToggleLeft("VRCPhysBone", _scanPhysBone);
        _scanPhysBoneCollider = EditorGUILayout.ToggleLeft("VRCPhysBoneCollider", _scanPhysBoneCollider);
        _scanContactReceiver = EditorGUILayout.ToggleLeft("VRCContactReceiver", _scanContactReceiver);
        _scanContactSender = EditorGUILayout.ToggleLeft("VRCContactSender", _scanContactSender);
        EditorGUILayout.EndVertical();

        if (!GetEnabledComponentDefs().Any())
        {
            EditorGUILayout.HelpBox("最低1つは検出対象コンポーネントを選んでください。", MessageType.Warning);
        }
    }

    private void DrawDetectionOptions()
    {
        EditorGUILayout.LabelField("検出設定", EditorStyles.boldLabel);
        _detectionLevel = (DetectionLevel)EditorGUILayout.EnumPopup("判定レベル", _detectionLevel);
        if (_detectionLevel == DetectionLevel.レベル1_基本)
        {
            EditorGUILayout.HelpBox("レベル1は主要項目のみで比較します。検出は広めですが、意図的な複数コンポーネントも候補に入りやすいです。修正前に詳細確認してください。", MessageType.Warning);
        }
        if (_detectionLevel == DetectionLevel.レベル3_完全比較)
        {
            EditorGUILayout.HelpBox("レベル3はSerializedPropertyを可能な範囲で全比較します。自動修正に使うなら一番安全寄りです。", MessageType.Info);
        }

        EditorGUILayout.LabelField("状態変化時の処理", EditorStyles.boldLabel);
        _changedStatePolicy = (ChangedStatePolicy)EditorGUILayout.EnumPopup("処理方針", _changedStatePolicy);
        _allowForceFix = EditorGUILayout.ToggleLeft("状態変化があっても強制修正を許可", _allowForceFix);

        if (_changedStatePolicy == ChangedStatePolicy.警告して続行 && !_allowForceFix)
        {
            EditorGUILayout.HelpBox("警告して続行は、修正時に現在状態で再判定して処理します。検出時の参照をそのまま削除する設計ではありません。", MessageType.None);
        }
        if (_allowForceFix)
        {
            EditorGUILayout.HelpBox("強制修正ONでも、削除対象は修正時点の現在状態から再取得します。ただし状態変化を無視するため危険です。", MessageType.Warning);
        }
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!GetEnabledComponentDefs().Any()))
        {
            if (GUILayout.Button("検出実行", GUILayout.Height(32))) RunScan();
        }
        using (new EditorGUI.DisabledScope(_results.Count == 0))
        {
            if (GUILayout.Button("選択項目を修正", GUILayout.Height(32))) RunFixSelected();
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(_results.Count == 0))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("すべて選択")) SetAllResultsSelected(true);
            if (GUILayout.Button("すべて解除")) SetAllResultsSelected(false);
            if (GUILayout.Button("ログウィンドウを開く")) ShowLogWindow(_lastLog);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawResults()
    {
        EditorGUILayout.LabelField($"検出結果: {_results.Count} 件", EditorStyles.boldLabel);

        if (_results.Count == 0)
        {
            EditorGUILayout.HelpBox("まだ検出結果はありません。", MessageType.None);
            return;
        }

        _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll, GUILayout.MinHeight(240), GUILayout.MaxHeight(560));
        foreach (var result in _results)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            result.Selected = EditorGUILayout.Toggle(result.Selected, GUILayout.Width(22));
            result.Expanded = EditorGUILayout.Foldout(result.Expanded, result.DisplayName, true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(result.ComponentLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField($"数: {result.ComponentCountAtScan}", GUILayout.Width(65));
            EditorGUILayout.LabelField($"削除候補: {result.DuplicateRemoveCandidateCount}", GUILayout.Width(95));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Object", result.ObjectPath);
            if (result.IsPrefabAsset) EditorGUILayout.LabelField("Prefab", result.PrefabAssetPath);
            else EditorGUILayout.LabelField("Root", result.RootLabel);

            if (result.Expanded)
            {
                EditorGUILayout.TextArea(result.DetailsAtScan ?? "", GUILayout.MinHeight(110));
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawLastLogButtons()
    {
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastLog)))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("最後のログをコピー"))
            {
                EditorGUIUtility.systemCopyBuffer = _lastLog ?? "";
                Debug.Log("重複コンポーネントログをクリップボードへコピーしました。");
            }
            if (GUILayout.Button("最後のログを表示")) ShowLogWindow(_lastLog);
            EditorGUILayout.EndHorizontal();
        }
    }

    private static void DrawObjectList(List<GameObject> list, string label, bool allowSceneObjects)
    {
        EditorGUILayout.LabelField($"{label} 数: {list.Count}", EditorStyles.miniBoldLabel);
        int remove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            list[i] = EditorGUILayout.ObjectField(list[i], typeof(GameObject), allowSceneObjects) as GameObject;
            if (GUILayout.Button("-", GUILayout.Width(24))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) list.RemoveAt(remove);
        if (GUILayout.Button("+ 追加")) list.Add(null);
    }

    private void PullCurrentSelection()
    {
        _prefabAssets.Clear();
        _hierarchyObjects.Clear();

        UnityEngine.Object active = Selection.activeObject;
        string activePath = active != null ? AssetDatabase.GetAssetPath(active) : "";
        if (!string.IsNullOrEmpty(activePath) && AssetDatabase.IsValidFolder(activePath))
        {
            _folder = active as DefaultAsset;
            _targetMode = TargetMode.指定フォルダ内Prefab;
            return;
        }

        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj == null) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (IsPrefabAssetPath(path))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && !_prefabAssets.Contains(go)) _prefabAssets.Add(go);
            }
        }

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            string path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path)) continue;
            if (!_hierarchyObjects.Contains(go)) _hierarchyObjects.Add(go);
        }

        if (_prefabAssets.Count > 0) _targetMode = TargetMode.選択中のPrefab;
        else if (_hierarchyObjects.Count > 0) _targetMode = TargetMode.選択中のヒエラルキー;
    }

    private IEnumerable<ComponentTargetDef> GetEnabledComponentDefs()
    {
        foreach (var def in ComponentDefs)
        {
            if (def.Kind == VrcComponentKind.PhysBone && _scanPhysBone) yield return def;
            else if (def.Kind == VrcComponentKind.PhysBoneCollider && _scanPhysBoneCollider) yield return def;
            else if (def.Kind == VrcComponentKind.ContactReceiver && _scanContactReceiver) yield return def;
            else if (def.Kind == VrcComponentKind.ContactSender && _scanContactSender) yield return def;
        }
    }

    private void SetAllResultsSelected(bool value)
    {
        foreach (var r in _results) r.Selected = value;
    }

    private void RunScan()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(WindowTitle, "Playモード中、またはPlayモードへ遷移中は実行できません。", "OK");
            return;
        }

        _results.Clear();
        var enabledDefs = GetEnabledComponentDefs().ToList();
        var log = new StringBuilder();
        AppendHeader(log, "VRC重複コンポーネント検出");
        log.AppendLine("対象コンポーネント: " + string.Join(", ", enabledDefs.Select(d => d.Label).ToArray()));
        log.AppendLine("判定レベル: " + _detectionLevel);
        log.AppendLine();

        List<ScanRoot> roots = CollectScanRoots(log);
        if (roots.Count == 0)
        {
            log.AppendLine("対象がありません。");
            CommitLog(log.ToString());
            return;
        }

        foreach (var root in roots)
        {
            if (root.IsPrefabAsset) ScanPrefabAsset(root, enabledDefs, log);
            else ScanHierarchyRoot(root, enabledDefs, log);
        }

        log.AppendLine();
        log.AppendLine($"検出対象Root数: {roots.Count}");
        log.AppendLine($"重複検出Object数: {_results.Count}");
        log.AppendLine($"削除候補Component数: {_results.Sum(r => r.DuplicateRemoveCandidateCount)}");
        CommitLog(log.ToString());
    }

    private void ScanHierarchyRoot(ScanRoot root, List<ComponentTargetDef> defs, StringBuilder log)
    {
        if (root.RootObject == null) return;
        log.AppendLine($"[Hierarchy] {GetHierarchyPath(root.RootObject.transform)}");
        ScanLoadedRoot(root.RootObject, root, defs, log);
    }

    private void ScanPrefabAsset(ScanRoot root, List<ComponentTargetDef> defs, StringBuilder log)
    {
        if (string.IsNullOrEmpty(root.PrefabAssetPath)) return;
        log.AppendLine($"[Prefab] {root.PrefabAssetPath}");

        GameObject loaded = null;
        try
        {
            loaded = PrefabUtility.LoadPrefabContents(root.PrefabAssetPath);
            root.RootObject = loaded;
            ScanLoadedRoot(loaded, root, defs, log);
        }
        catch (Exception ex)
        {
            log.AppendLine($"  ERROR: Prefab読込失敗: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
            root.RootObject = null;
        }
    }

    private void ScanLoadedRoot(GameObject loadedRoot, ScanRoot root, List<ComponentTargetDef> defs, StringBuilder log)
    {
        foreach (Transform t in loadedRoot.GetComponentsInChildren<Transform>(true))
        {
            foreach (var def in defs)
            {
                var components = GetTargetComponents(t.gameObject, def);
                if (components.Count <= 1) continue;

                List<ComponentSnapshot> snapshots = CreateSnapshots(components, def, loadedRoot.transform, (int)_detectionLevel);
                List<DuplicateGroup> groups = FindDuplicateGroups(snapshots);
                int removeCandidates = groups.Sum(g => Math.Max(0, g.Indexes.Count - 1));
                if (removeCandidates <= 0) continue;

                string objectPath = GetRelativePath(loadedRoot.transform, t);
                string details = BuildDetailsText(objectPath, def, snapshots, groups);
                var result = new ScanResult
                {
                    IsPrefabAsset = root.IsPrefabAsset,
                    Kind = def.Kind,
                    ComponentLabel = def.Label,
                    ComponentFullName = def.FullName,
                    RootObject = root.IsPrefabAsset ? null : root.RootObject,
                    PrefabAssetPath = root.PrefabAssetPath,
                    RootLabel = root.Label,
                    ObjectPath = objectPath,
                    DisplayName = $"{objectPath} / {def.Label}:{components.Count} / 削除候補:{removeCandidates}",
                    ComponentCountAtScan = components.Count,
                    DuplicateRemoveCandidateCount = removeCandidates,
                    FingerprintAtScan = BuildFingerprint(objectPath, def, snapshots),
                    DetailsAtScan = details,
                    DuplicateGroups = groups
                };

                _results.Add(result);
                log.AppendLine($"  DUPLICATE: {objectPath} / {def.Label}:{components.Count} / 削除候補:{removeCandidates}");
            }
        }
    }

    private void RunFixSelected()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(WindowTitle, "Playモード中、またはPlayモードへ遷移中は実行できません。", "OK");
            return;
        }

        List<ScanResult> targets = _results.Where(r => r.Selected).ToList();
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog(WindowTitle, "修正対象が選択されていません。", "OK");
            return;
        }

        string confirmMessage =
            $"選択された {targets.Count} 件を修正します。\n\n" +
            "設定が同一と判定されたコンポーネントのみ削除します。\n" +
            "Hierarchy対象はUndo可能です。Prefab Asset対象はPrefabファイルへ保存されます。\n\n" +
            "続行しますか？";
        if (!EditorUtility.DisplayDialog(WindowTitle, confirmMessage, "修正実行", "キャンセル")) return;

        var log = new StringBuilder();
        AppendHeader(log, "VRC重複コンポーネント修正");
        log.AppendLine($"選択項目: {targets.Count}");
        log.AppendLine($"判定レベル: {_detectionLevel}");
        log.AppendLine($"状態変化時の処理: {_changedStatePolicy}");
        log.AppendLine($"強制修正許可: {_allowForceFix}");
        log.AppendLine();

        int fixedObjects = 0;
        int removedComponents = 0;
        int skipped = 0;
        int failed = 0;
        bool abort = false;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("VRC Duplicate Component Cleanup");

        foreach (var group in targets.GroupBy(r => r.IsPrefabAsset ? "P:" + r.PrefabAssetPath : "H:" + GetInstanceIDSafe(r.RootObject)))
        {
            if (abort) break;
            var first = group.First();
            if (first.IsPrefabAsset)
            {
                ProcessPrefabFixGroup(first.PrefabAssetPath, group.ToList(), log, ref fixedObjects, ref removedComponents, ref skipped, ref failed, ref abort);
            }
            else
            {
                foreach (var result in group)
                {
                    ProcessHierarchyFixResult(result, log, ref fixedObjects, ref removedComponents, ref skipped, ref failed, ref abort);
                    if (abort) break;
                }
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        log.AppendLine();
        log.AppendLine("--- Summary ---");
        log.AppendLine($"修正Object数: {fixedObjects}");
        log.AppendLine($"削除Component数: {removedComponents}");
        log.AppendLine($"スキップ: {skipped}");
        log.AppendLine($"失敗: {failed}");
        log.AppendLine($"中断: {abort}");

        CommitLog(log.ToString());

        if (removedComponents > 0 || abort)
        {
            foreach (var r in _results) r.Selected = false;
            EditorUtility.DisplayDialog(WindowTitle, "修正処理が完了しました。結果は古くなっている可能性があります。必要なら再度検出してください。", "OK");
        }
    }

    private void ProcessHierarchyFixResult(ScanResult result, StringBuilder log, ref int fixedObjects, ref int removedComponents, ref int skipped, ref int failed, ref bool abort)
    {
        if (result.RootObject == null)
        {
            failed++;
            log.AppendLine($"ERROR: Root参照切れ: {result.ObjectPath}");
            return;
        }

        Transform target = FindChildByRelativePath(result.RootObject.transform, result.ObjectPath);
        if (target == null)
        {
            failed++;
            log.AppendLine($"ERROR: Objectが見つかりません: {result.ObjectPath}");
            return;
        }

        int removed = FixLoadedObject(result, result.RootObject, target.gameObject, log, ref skipped, ref failed, ref abort, savePrefabAsset: false);
        if (removed > 0)
        {
            fixedObjects++;
            removedComponents += removed;
        }
    }

    private void ProcessPrefabFixGroup(string prefabPath, List<ScanResult> results, StringBuilder log, ref int fixedObjects, ref int removedComponents, ref int skipped, ref int failed, ref bool abort)
    {
        if (string.IsNullOrEmpty(prefabPath)) return;
        GameObject loaded = null;
        bool changed = false;

        try
        {
            loaded = PrefabUtility.LoadPrefabContents(prefabPath);
            log.AppendLine($"[Prefab修正] {prefabPath}");

            foreach (var result in results)
            {
                if (abort) break;
                Transform target = FindChildByRelativePath(loaded.transform, result.ObjectPath);
                if (target == null)
                {
                    failed++;
                    log.AppendLine($"ERROR: Objectが見つかりません: {result.ObjectPath}");
                    continue;
                }

                int removed = FixLoadedObject(result, loaded, target.gameObject, log, ref skipped, ref failed, ref abort, savePrefabAsset: true);
                if (removed > 0)
                {
                    fixedObjects++;
                    removedComponents += removed;
                    changed = true;
                }
            }

            if (changed && !abort)
            {
                PrefabUtility.SaveAsPrefabAsset(loaded, prefabPath);
                log.AppendLine($"SAVE: {prefabPath}");
            }
        }
        catch (Exception ex)
        {
            failed++;
            log.AppendLine($"ERROR: Prefab修正失敗: {prefabPath} / {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
        }
    }

    private int FixLoadedObject(ScanResult result, GameObject loadedRoot, GameObject target, StringBuilder log, ref int skipped, ref int failed, ref bool abort, bool savePrefabAsset)
    {
        var def = ComponentDefs.FirstOrDefault(d => d.Kind == result.Kind);
        if (def == null)
        {
            failed++;
            log.AppendLine($"ERROR: 未知のコンポーネント種別: {result.ComponentLabel}");
            return 0;
        }

        var components = GetTargetComponents(target, def);
        var snapshots = CreateSnapshots(components, def, loadedRoot.transform, (int)_detectionLevel);
        string currentFingerprint = BuildFingerprint(result.ObjectPath, def, snapshots);

        bool changed = currentFingerprint != result.FingerprintAtScan;
        if (changed)
        {
            log.AppendLine($"CHANGED: {result.ObjectPath} / {def.Label}");

            if (_changedStatePolicy == ChangedStatePolicy.中断する && !_allowForceFix)
            {
                abort = true;
                skipped++;
                log.AppendLine("  状態変化を検出したため中断しました。再度検出してください。");
                return 0;
            }

            if (_changedStatePolicy == ChangedStatePolicy.変更項目のみスキップ && !_allowForceFix)
            {
                skipped++;
                log.AppendLine("  SKIP: 検出時からコンポーネント構成または設定が変化しています。");
                return 0;
            }

            log.AppendLine(_allowForceFix
                ? "  WARNING: 強制修正許可ONのため、現在状態に対して修正を試みます。"
                : "  WARNING: 現在状態に対して再判定して修正を試みます。");
        }

        List<DuplicateGroup> groups = FindDuplicateGroups(snapshots);
        int removeCandidates = groups.Sum(g => Math.Max(0, g.Indexes.Count - 1));
        if (removeCandidates <= 0)
        {
            skipped++;
            log.AppendLine($"SKIP: {result.ObjectPath} / {def.Label} / 現在状態では削除候補がありません。");
            return 0;
        }

        var removeIndexes = new HashSet<int>();
        foreach (var group in groups)
        {
            for (int i = 1; i < group.Indexes.Count; i++) removeIndexes.Add(group.Indexes[i]);
        }

        int removed = 0;
        for (int i = snapshots.Count - 1; i >= 0; i--)
        {
            if (!removeIndexes.Contains(i)) continue;
            Component component = snapshots[i].Component;
            if (component == null)
            {
                failed++;
                log.AppendLine($"ERROR: 削除対象参照切れ: {result.ObjectPath} / {def.Label} index {i}");
                continue;
            }

            if (!savePrefabAsset)
            {
                Undo.RecordObject(target, "Remove Duplicate VRC Component");
                Undo.DestroyObjectImmediate(component);
            }
            else
            {
                DestroyImmediate(component, true);
            }

            removed++;
            log.AppendLine($"REMOVE: {result.ObjectPath} / {def.Label} index {i}");
        }

        if (!savePrefabAsset && removed > 0)
        {
            EditorUtility.SetDirty(target);
            var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(target);
            if (prefabRoot != null) EditorUtility.SetDirty(prefabRoot);
        }

        return removed;
    }

    private List<ScanRoot> CollectScanRoots(StringBuilder log)
    {
        var roots = new List<ScanRoot>();

        if (_targetMode == TargetMode.指定フォルダ内Prefab)
        {
            string folderPath = _folder != null ? AssetDatabase.GetAssetPath(_folder) : "";
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                log.AppendLine("ERROR: 有効なフォルダが指定されていません。");
                return roots;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsPrefabAssetPath(path)) continue;
                roots.Add(new ScanRoot { IsPrefabAsset = true, PrefabAssetPath = path, Label = path });
            }
        }
        else if (_targetMode == TargetMode.選択中のPrefab)
        {
            foreach (var prefab in _prefabAssets.Where(p => p != null))
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!IsPrefabAssetPath(path))
                {
                    log.AppendLine($"SKIP: Prefabではありません: {prefab.name}");
                    continue;
                }
                if (!roots.Any(r => r.PrefabAssetPath == path)) roots.Add(new ScanRoot { IsPrefabAsset = true, PrefabAssetPath = path, Label = path });
            }
        }
        else
        {
            foreach (var go in _hierarchyObjects.Where(g => g != null))
            {
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(go))) continue;
                if (!roots.Any(r => r.RootObject == go)) roots.Add(new ScanRoot { IsPrefabAsset = false, RootObject = go, Label = GetHierarchyPath(go.transform) });
            }
        }

        return roots;
    }

    private static List<Component> GetTargetComponents(GameObject go, ComponentTargetDef def)
    {
        var list = new List<Component>();
        if (go == null || def == null) return list;
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            Type t = c.GetType();
            if (t.FullName == def.FullName || t.Name == def.ShortTypeName) list.Add(c);
        }
        return list;
    }

    private static List<ComponentSnapshot> CreateSnapshots(List<Component> components, ComponentTargetDef def, Transform root, int level)
    {
        var snapshots = new List<ComponentSnapshot>();
        foreach (var c in components)
        {
            string detail;
            string signature = BuildComponentSignature(c, def, root, level, out detail);
            snapshots.Add(new ComponentSnapshot { Component = c, Def = def, Signature = signature, DetailText = detail });
        }
        return snapshots;
    }

    private static List<DuplicateGroup> FindDuplicateGroups(List<ComponentSnapshot> snapshots)
    {
        var dict = new Dictionary<string, DuplicateGroup>();
        for (int i = 0; i < snapshots.Count; i++)
        {
            string sig = snapshots[i].Signature ?? "";
            DuplicateGroup group;
            if (!dict.TryGetValue(sig, out group))
            {
                group = new DuplicateGroup { Signature = sig };
                dict[sig] = group;
            }
            group.Indexes.Add(i);
        }
        return dict.Values.Where(g => g.Indexes.Count >= 2).ToList();
    }

    private static string BuildFingerprint(string objectPath, ComponentTargetDef def, List<ComponentSnapshot> snapshots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Object=" + objectPath);
        sb.AppendLine("Component=" + def.Label);
        sb.AppendLine("Count=" + snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
        {
            sb.AppendLine($"[{i}] {snapshots[i].Signature}");
        }
        return sb.ToString();
    }

    private static string BuildDetailsText(string objectPath, ComponentTargetDef def, List<ComponentSnapshot> snapshots, List<DuplicateGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Object: " + objectPath);
        sb.AppendLine("Component: " + def.Label);
        sb.AppendLine("Count: " + snapshots.Count);
        sb.AppendLine("Duplicate Groups: " + groups.Count);
        foreach (var g in groups)
        {
            sb.AppendLine("Group: " + string.Join(", ", g.Indexes.Select(i => i.ToString()).ToArray()));
        }
        sb.AppendLine();
        for (int i = 0; i < snapshots.Count; i++)
        {
            sb.AppendLine($"--- {def.Label} {i} ---");
            sb.AppendLine(snapshots[i].DetailText);
        }
        return sb.ToString();
    }

    private static string BuildComponentSignature(Component component, ComponentTargetDef def, Transform root, int level, out string detailText)
    {
        if (level >= 3)
        {
            return BuildAllSerializedSignature(component, root, out detailText);
        }

        string[] names = level <= 1 ? def.Level1Properties : def.Level2Properties;
        var signature = new StringBuilder();
        var detail = new StringBuilder();
        SerializedObject so;
        try
        {
            so = new SerializedObject(component);
        }
        catch (Exception ex)
        {
            detailText = "<SerializedObject作成失敗> " + ex.Message;
            return "<unreadable>";
        }

        foreach (string name in names)
        {
            SerializedProperty prop = so.FindProperty(name);
            string value = SerializedPropertyToStableString(prop, root, name, deepArray: level >= 2);
            signature.Append(name).Append("=").Append(value).Append(";");
            detail.AppendLine(name + ": " + value);
        }

        detailText = detail.ToString();
        return signature.ToString();
    }

    private static string BuildAllSerializedSignature(Component component, Transform root, out string detailText)
    {
        var signature = new StringBuilder();
        var detail = new StringBuilder();
        SerializedObject so;
        try
        {
            so = new SerializedObject(component);
        }
        catch (Exception ex)
        {
            detailText = "<SerializedObject作成失敗> " + ex.Message;
            return "<unreadable>";
        }

        SerializedProperty iterator = so.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            string path = iterator.propertyPath;
            if (path == "m_Script") continue;
            if (path.StartsWith("m_", StringComparison.Ordinal) && path != "m_Name") continue;

            SerializedProperty copy = iterator.Copy();
            string value = SerializedPropertyToStableString(copy, root, path, deepArray: true);
            signature.Append(path).Append("=").Append(value).Append(";");
            detail.AppendLine(path + ": " + value);
        }

        detailText = detail.ToString();
        return signature.ToString();
    }

    private static string SerializedPropertyToStableString(SerializedProperty prop, Transform root, string propertyName, bool deepArray)
    {
        if (prop == null) return "<missing>";

        try
        {
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                if (!deepArray) return "ArraySize=" + prop.arraySize;

                var sb = new StringBuilder();
                sb.Append("ArraySize=").Append(prop.arraySize).Append("[");
                for (int i = 0; i < prop.arraySize; i++)
                {
                    SerializedProperty element = prop.GetArrayElementAtIndex(i);
                    if (i > 0) sb.Append(",");
                    sb.Append(SerializedPropertyToStableString(element, root, propertyName + "[" + i + "]", deepArray: true));
                }
                sb.Append("]");
                return sb.ToString();
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return QuantizeFloat(prop.floatValue);
                case SerializedPropertyType.String:
                    return prop.stringValue ?? "";
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    return QuantizeVector2(prop.vector2Value);
                case SerializedPropertyType.Vector3:
                    return QuantizeVector3(prop.vector3Value);
                case SerializedPropertyType.Vector4:
                    return QuantizeVector4(prop.vector4Value);
                case SerializedPropertyType.Quaternion:
                    return QuantizeVector4(new Vector4(prop.quaternionValue.x, prop.quaternionValue.y, prop.quaternionValue.z, prop.quaternionValue.w));
                case SerializedPropertyType.ObjectReference:
                    return ObjectReferenceToStableString(prop.objectReferenceValue, root);
                case SerializedPropertyType.Color:
                    return QuantizeVector4(prop.colorValue);
                case SerializedPropertyType.Bounds:
                    return "center=" + QuantizeVector3(prop.boundsValue.center) + ";size=" + QuantizeVector3(prop.boundsValue.size);
                case SerializedPropertyType.Rect:
                    Rect r = prop.rectValue;
                    return QuantizeFloat(r.x) + "," + QuantizeFloat(r.y) + "," + QuantizeFloat(r.width) + "," + QuantizeFloat(r.height);
                default:
                    return prop.propertyType + ":" + prop.ToString();
            }
        }
        catch (Exception ex)
        {
            return "<error:" + ex.GetType().Name + ">";
        }
    }

    private static string ObjectReferenceToStableString(UnityEngine.Object obj, Transform root)
    {
        if (obj == null) return "<null>";
        Transform t = obj as Transform;
        if (t != null) return "Transform:" + GetRelativePath(root, t);
        Component c = obj as Component;
        if (c != null) return c.GetType().Name + ":" + GetRelativePath(root, c.transform);
        GameObject go = obj as GameObject;
        if (go != null) return "GameObject:" + GetRelativePath(root, go.transform);
        string path = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(path)) return obj.GetType().Name + ":" + path;
        return obj.GetType().Name + ":" + obj.name;
    }

    private static string QuantizeFloat(float v)
    {
        return Math.Round(v, 4).ToString("0.####");
    }

    private static string QuantizeVector2(Vector2 v)
    {
        return QuantizeFloat(v.x) + "," + QuantizeFloat(v.y);
    }

    private static string QuantizeVector3(Vector3 v)
    {
        return QuantizeFloat(v.x) + "," + QuantizeFloat(v.y) + "," + QuantizeFloat(v.z);
    }

    private static string QuantizeVector4(Vector4 v)
    {
        return QuantizeFloat(v.x) + "," + QuantizeFloat(v.y) + "," + QuantizeFloat(v.z) + "," + QuantizeFloat(v.w);
    }

    private static bool IsPrefabAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (AssetDatabase.IsValidFolder(path)) return false;
        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return false;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return false;
        return PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab;
    }

    private static Transform FindChildByRelativePath(Transform root, string relativePath)
    {
        if (root == null || string.IsNullOrEmpty(relativePath)) return null;
        if (relativePath == root.name || relativePath == ".") return root;

        string path = relativePath;
        if (path.StartsWith(root.name + "/", StringComparison.Ordinal)) path = path.Substring(root.name.Length + 1);
        if (string.IsNullOrEmpty(path)) return root;
        return root.Find(path);
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (target == null) return "<null>";
        if (root == null) return GetHierarchyPath(target);
        if (target == root) return root.name;

        var stack = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        if (current == root)
        {
            stack.Push(root.name);
            return string.Join("/", stack.ToArray());
        }

        return GetHierarchyPath(target);
    }

    private static string GetHierarchyPath(Transform t)
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

    private static int GetInstanceIDSafe(UnityEngine.Object obj)
    {
        return obj != null ? obj.GetInstanceID() : 0;
    }

    private static void AppendHeader(StringBuilder sb, string title)
    {
        sb.AppendLine("# " + title);
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Unity: " + Application.unityVersion);
        sb.AppendLine("Project: " + Application.dataPath);
        sb.AppendLine();
    }

    private void CommitLog(string text)
    {
        _lastLog = text ?? "";
        Debug.Log(_lastLog);
        ShowLogWindow(_lastLog);
    }

    private static void ShowLogWindow(string text)
    {
        AvatarDebugReportWindow.Open(text ?? "", LogWindowTitle);
    }
}

}
