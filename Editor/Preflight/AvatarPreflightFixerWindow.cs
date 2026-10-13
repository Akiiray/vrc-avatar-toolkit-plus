using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    public sealed class AvatarPreflightFixerWindow : EditorWindow
    {
        private static readonly string[] TargetLabels = { "Hierarchyで選択中のアバター", "Hierarchy内の全アバター", "指定フォルダ内のPrefab" };
        private static readonly string[] BlendLabels = { "SDK Auto Fix相当（Calculate / Legacy ON）", "Import / Legacy OFF", "None / Legacy OFF" };
        [SerializeField] private PreflightTargetMode _targetMode;
        [SerializeField] private DefaultAsset _folder;
        [SerializeField] private bool _recursive = true;
        [SerializeField] private bool _readWrite = true;
        [SerializeField] private bool _blendShapes = true;
        [SerializeField] private bool _streaming = true;
        [SerializeField] private PreflightBlendShapeMode _blendMode;
        private AvatarPreflightPlan _plan;
        private string _result = "";
        private string _notice = "解析して変更プレビューを確認してください。";
        private string _sdkVersion;
        private bool _busy;
        private Vector2 _scroll;
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();

        [MenuItem(ToolkitConstants.MenuRoot + "/Avatar Preflight Fixer")]
        public static void Open()
        {
            var window = GetWindow<AvatarPreflightFixerWindow>("Avatar Preflight Fixer");
            window.minSize = new Vector2(760, 560);
            window.Show();
        }

        private void OnEnable()
        {
            _sdkVersion = ReadSdkVersion();
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnSelectionChanged()
        {
            if (_targetMode == PreflightTargetMode.SelectedHierarchy) Invalidate();
        }
        private void OnProjectChanged() { if (!_busy) { _sdkVersion = ReadSdkVersion(); Invalidate(); } }
        private void OnHierarchyChanged() { if (!_busy && _targetMode != PreflightTargetMode.PrefabFolder) Invalidate(); }

        private void Invalidate()
        {
            if (_plan != null) _notice = "対象または設定が変化しました。再解析してください。";
            _plan = null;
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Avatar Preflight Fixer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Unity: " + Application.unityVersion + " / SDK Avatars: " + _sdkVersion + " / 判定基準: SDK 3.10.5");
            if (_sdkVersion != "3.10.5")
                EditorGUILayout.HelpBox("SDK 3.10.5以外は未検証です。必要なAPIを取得できない場合は解析または該当修正を停止します。", MessageType.Warning);
            EditorGUILayout.HelpBox("Importerだけを変更します。共有モデル内の全Meshと、選択外のアバターにも影響します。BlendShape方式の変更は陰影に影響する場合があります。", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _targetMode = (PreflightTargetMode)EditorGUILayout.Popup("対象", (int)_targetMode, TargetLabels);
            if (_targetMode == PreflightTargetMode.PrefabFolder)
            {
                _folder = (DefaultAsset)EditorGUILayout.ObjectField("Prefabフォルダ", _folder, typeof(DefaultAsset), false);
                _recursive = EditorGUILayout.ToggleLeft("サブフォルダも検索する", _recursive);
            }
            _readWrite = EditorGUILayout.ToggleLeft("Read/Writeを有効化", _readWrite);
            _blendShapes = EditorGUILayout.ToggleLeft("BlendShape Normalsを修正", _blendShapes);
            using (new EditorGUI.DisabledScope(!_blendShapes))
                _blendMode = (PreflightBlendShapeMode)EditorGUILayout.Popup("修正方式", (int)_blendMode, BlendLabels);
            _streaming = EditorGUILayout.ToggleLeft("Streaming Mip Mapsを有効化（既存Priority・Mip Maps設定を維持）", _streaming);
            if (EditorGUI.EndChangeCheck()) Invalidate();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_busy || (!_readWrite && !_blendShapes && !_streaming)))
                    if (GUILayout.Button("解析／変更プレビュー")) Analyze();
                using (new EditorGUI.DisabledScope(_busy || _plan == null || _plan.ApplicableCount == 0))
                    if (GUILayout.Button("プレビューの変更を一括適用")) Apply();
            }
            EditorGUILayout.HelpBox(_notice, MessageType.Info);
            if (_plan != null) DrawPlan();
            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("実行結果・エラーログ", EditorStyles.boldLabel);
                if (GUILayout.Button("共通ログウィンドウを開く")) ToolkitLogWindow.Open("Avatar Preflight Fixer - Log", _result);
                EditorGUILayout.TextArea(_result, GUILayout.MinHeight(140));
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPlan()
        {
            EditorGUILayout.LabelField("アバター: " + _plan.Avatars.Count + " / 参照Importer: " + _plan.Assets.Count +
                " / 適用可能: " + _plan.ApplicableCount, EditorStyles.boldLabel);
            foreach (string avatar in _plan.Avatars) EditorGUILayout.LabelField(avatar);
            foreach (var asset in _plan.Assets.Values)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    string label = asset.Name + (asset.OutsideFolder ? " [フォルダ外]" : "") +
                        " / 参照アバター: " + asset.Avatars.Count + (asset.CanApply ? " [修正予定]" : "");
                    _expanded.TryGetValue(asset.Path, out bool expanded);
                    expanded = EditorGUILayout.Foldout(expanded, label, true);
                    _expanded[asset.Path] = expanded;
                    EditorGUILayout.LabelField(asset.Path);
                    EditorGUILayout.LabelField(asset.Describe(_plan.Request.BlendShapeMode), EditorStyles.wordWrappedLabel);
                    if (expanded)
                    {
                        foreach (string source in asset.Sources) EditorGUILayout.LabelField(source, EditorStyles.wordWrappedLabel);
                        if (GUILayout.Button("Projectで表示", GUILayout.Width(130)))
                            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(asset.Path));
                    }
                }
            }
            foreach (string diagnostic in _plan.Diagnostics) EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
        }

        private void Analyze()
        {
            if (ToolkitEditorUtility.IsPlayModeOrChanging()) return;
            _busy = true;
            _plan = null;
            try
            {
                var request = new AvatarPreflightRequest
                {
                    TargetMode = _targetMode,
                    Selection = ToolkitSelectionUtility.GetSelectedHierarchyObjects().ToArray(),
                    Folder = _folder == null ? null : AssetDatabase.GetAssetPath(_folder),
                    Recursive = _recursive, ReadWrite = _readWrite, BlendShapes = _blendShapes,
                    Streaming = _streaming, BlendShapeMode = _blendMode
                };
                _plan = AvatarPreflightScanner.Scan(request);
                _expanded.Clear();
                _notice = "解析完了。変更前後・参照元・適用不可理由を確認してから一括適用してください。";
            }
            catch (Exception e)
            {
                _notice = "解析に失敗しました。";
                _result = "[解析失敗] " + e.GetBaseException().Message;
            }
            finally { _busy = false; }
        }

        private void Apply()
        {
            if (_plan == null || ToolkitEditorUtility.IsPlayModeOrChanging()) return;
            _busy = true;
            try
            {
                _result = AvatarPreflightApplier.Apply(_plan);
                _notice = "処理終了。結果を確認し、再解析で残りの項目を確認してください。";
                ToolkitLogWindow.Open("Avatar Preflight Fixer - Log", _result);
            }
            catch (Exception e)
            {
                _result = "[全体停止] " + e.GetBaseException().Message;
                _notice = "適用を停止しました。再解析してください。";
            }
            finally { _plan = null; _busy = false; }
        }

        [Serializable] private sealed class PackageVersion { public string version = null; }
        private static string ReadSdkVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.vrchat.avatars/package.json");
                if (info != null) return info.version;
                const string path = "Packages/com.vrchat.avatars/package.json";
                if (File.Exists(path)) return JsonUtility.FromJson<PackageVersion>(File.ReadAllText(path)).version ?? "不明";
            }
            catch { /* バージョン表示失敗だけでSDK利用可否を推測しない。 */ }
            return "不明";
        }
    }
}
