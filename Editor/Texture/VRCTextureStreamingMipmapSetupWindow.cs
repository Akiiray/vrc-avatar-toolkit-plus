using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
/// <summary>
/// VRC Avatar Toolkit Plus / Texture Streaming Mipmap Setup
///
/// 目的:
/// - Hierarchy上のアバター / Project上のPrefab / フォルダ配下Prefab / Project内Texture2D を対象にする。
/// - Renderer -> Material -> Shader Texture Property 経由で使用Texture2Dを検出する。
/// - 除外ルールにより UI / Icon / Ramp / MatCap / LUT / 小型Texture 等を除外できる。
/// - TextureImporter.streamingMipmaps のみを変更する。
/// - Generate Mip Maps / Max Texture Size / Compression / Crunch Compression 等は変更しない。
/// - Dry Run、変更前後ログ、対象外リスト表示に対応する。
/// </summary>
public sealed class VRCTextureStreamingMipmapSetupWindow : EditorWindow
{
    private const string WindowTitle = "VRC Texture Streaming Mipmap Setup";
    private const string LogWindowTitle = "VRC Avatar Toolkit Plus - Texture Streaming Mipmap Log";

    private enum TargetMode
    {
        選択中のヒエラルキー,
        選択中のPrefab,
        指定フォルダ内Prefab,
        プロジェクト内Texture2Dすべて
    }

    private enum ResultKind
    {
        Target,
        Excluded
    }

    private sealed class TextureCandidate
    {
        public ResultKind Kind;
        public Texture2D Texture;
        public string AssetPath;
        public string TextureName;
        public int Width;
        public int Height;
        public bool MipmapEnabled;
        public bool StreamingMipmaps;
        public bool WouldChange;
        public string Source;
        public string PropertyName;
        public string Reason;
    }

    private sealed class TextureRecord
    {
        public Texture2D Texture;
        public string AssetPath;
        public string Source;
        public string PropertyName;
    }

    private sealed class ScanRoot
    {
        public bool IsPrefabAsset;
        public GameObject RootObject;
        public string PrefabAssetPath;
        public string Label;
    }

    private TargetMode _targetMode = TargetMode.選択中のヒエラルキー;
    private DefaultAsset _folder;
    private readonly List<GameObject> _hierarchyObjects = new List<GameObject>();
    private readonly List<GameObject> _prefabAssets = new List<GameObject>();

    private bool _dryRun = true;
    private bool _excludeSmallTextures = true;
    private int _smallTextureThreshold = 256;
    private bool _excludeSpriteAndNon2D = true;
    private bool _excludeEditorGizmosPaths = true;
    private bool _excludeNameKeywords = true;
    private bool _excludeShaderPropertyKeywords = true;
    private bool _excludeTexturesWithoutMipmaps = false;
    private bool _includeAlreadyEnabledInTargetList = true;

    private string _excludePathKeywords = "/Editor/\n/Gizmos/\n/Icons/\n/Icon/\n/Thumbnail/\n/Thumbnails/\n/Sprites/\n/Sprite/\n/Resources/UI/";
    private string _excludeNameKeywordsText = "icon\nui\nthumbnail\nthumb\nramp\ngradient\ngrad\nmatcap\nlut\nexpression\nmenu";
    private string _excludePropertyKeywordsText = "matcap\nramp\ntoonramp\ngradient\nlut\nlookup";

    private readonly List<TextureCandidate> _targets = new List<TextureCandidate>();
    private readonly List<TextureCandidate> _excluded = new List<TextureCandidate>();
    private Vector2 _scroll;
    private Vector2 _targetScroll;
    private Vector2 _excludedScroll;
    private string _lastLog = "";

    [MenuItem("Tools/VRC Avatar Toolkit Plus/Texture/Streaming Mipmap Setup")]
    public static void OpenWindow()
    {
        var window = GetWindow<VRCTextureStreamingMipmapSetupWindow>(WindowTitle);
        window.minSize = new Vector2(820, 680);
        window.PullCurrentSelection();
        window.Show();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Texture/Streaming Mipmap Setup", false, 31)]
    private static void OpenWindowFromHierarchy()
    {
        OpenWindow();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/Texture/Streaming Mipmap Setup", true)]
    private static bool ValidateOpenWindowFromHierarchy()
    {
        return Selection.activeGameObject != null;
    }

    [MenuItem("Assets/VRC Avatar Toolkit Plus/Texture/Streaming Mipmap Setup", false, 2101)]
    private static void OpenWindowFromAssets()
    {
        OpenWindow();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("VRC Avatar Toolkit Plus / Streaming Mipmap Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "アバターやPrefabで使用されているTexture2Dを検出し、TextureImporter.streamingMipmaps のみをONにします。Generate Mip Maps、Max Texture Size、Compression、Crunch Compression等は変更しません。まずDry Runで解析してください。",
            MessageType.Info);

        DrawTargetArea();
        EditorGUILayout.Space(8);
        DrawExcludeOptions();
        EditorGUILayout.Space(8);
        DrawActionButtons();
        EditorGUILayout.Space(8);
        DrawResults();
        EditorGUILayout.Space(8);
        DrawLogButtons();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTargetArea()
    {
        EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _targetMode = (TargetMode)EditorGUILayout.EnumPopup("対象モード", _targetMode);
        if (EditorGUI.EndChangeCheck())
        {
            PullCurrentSelection();
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            if (_targetMode == TargetMode.選択中のヒエラルキー)
            {
                EditorGUILayout.LabelField("Hierarchy選択", _hierarchyObjects.Count + " 件");
                DrawObjectList(_hierarchyObjects);
            }
            else if (_targetMode == TargetMode.選択中のPrefab)
            {
                EditorGUILayout.LabelField("Prefab選択", _prefabAssets.Count + " 件");
                DrawObjectList(_prefabAssets);
            }
            else if (_targetMode == TargetMode.指定フォルダ内Prefab)
            {
                _folder = (DefaultAsset)EditorGUILayout.ObjectField("対象フォルダ", _folder, typeof(DefaultAsset), false);
            }
            else
            {
                EditorGUILayout.HelpBox("Project内のTexture2Dを直接すべて走査します。Prefabからの参照検出ではないため、上級者向けです。", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("現在の選択を取り込む", GUILayout.Width(180)))
                {
                    PullCurrentSelection();
                }
                if (GUILayout.Button("対象クリア", GUILayout.Width(120)))
                {
                    _hierarchyObjects.Clear();
                    _prefabAssets.Clear();
                    _folder = null;
                }
            }
        }
    }

    private static void DrawObjectList(List<GameObject> list)
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
                EditorGUILayout.ObjectField(list[i], typeof(GameObject), true);
                if (GUILayout.Button("削除", GUILayout.Width(60)))
                {
                    list.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    private void DrawExcludeOptions()
    {
        EditorGUILayout.LabelField("除外設定", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            _dryRun = EditorGUILayout.ToggleLeft("Dry Run（変更せず解析のみ）", _dryRun);
            _includeAlreadyEnabledInTargetList = EditorGUILayout.ToggleLeft("既にStreaming MipmapsがONのTextureも対象一覧に表示", _includeAlreadyEnabledInTargetList);
            _excludeTexturesWithoutMipmaps = EditorGUILayout.ToggleLeft("MipMap未生成のTextureを除外", _excludeTexturesWithoutMipmaps);

            EditorGUILayout.Space(4);
            _excludeSmallTextures = EditorGUILayout.ToggleLeft("小さいTextureを除外", _excludeSmallTextures);
            using (new EditorGUI.DisabledScope(!_excludeSmallTextures))
            {
                _smallTextureThreshold = EditorGUILayout.IntField("小型判定しきい値", Mathf.Max(1, _smallTextureThreshold));
                EditorGUILayout.LabelField("判定", "width <= threshold && height <= threshold");
            }

            EditorGUILayout.Space(4);
            _excludeSpriteAndNon2D = EditorGUILayout.ToggleLeft("Sprite / 非Texture2D Shapeを除外", _excludeSpriteAndNon2D);
            _excludeEditorGizmosPaths = EditorGUILayout.ToggleLeft("Editor / Gizmos / UI系フォルダを除外", _excludeEditorGizmosPaths);
            using (new EditorGUI.DisabledScope(!_excludeEditorGizmosPaths))
            {
                EditorGUILayout.LabelField("除外パスキーワード（1行1件）");
                _excludePathKeywords = EditorGUILayout.TextArea(_excludePathKeywords, GUILayout.MinHeight(70));
            }

            EditorGUILayout.Space(4);
            _excludeNameKeywords = EditorGUILayout.ToggleLeft("名前キーワードで除外", _excludeNameKeywords);
            using (new EditorGUI.DisabledScope(!_excludeNameKeywords))
            {
                EditorGUILayout.LabelField("除外名キーワード（1行1件 / 大文字小文字無視）");
                _excludeNameKeywordsText = EditorGUILayout.TextArea(_excludeNameKeywordsText, GUILayout.MinHeight(80));
            }

            EditorGUILayout.Space(4);
            _excludeShaderPropertyKeywords = EditorGUILayout.ToggleLeft("Shader Property名で Ramp / MatCap / LUT 系を除外", _excludeShaderPropertyKeywords);
            using (new EditorGUI.DisabledScope(!_excludeShaderPropertyKeywords))
            {
                EditorGUILayout.LabelField("除外Propertyキーワード（1行1件 / 大文字小文字無視）");
                _excludePropertyKeywordsText = EditorGUILayout.TextArea(_excludePropertyKeywordsText, GUILayout.MinHeight(70));
            }
        }
    }

    private void DrawActionButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("解析", GUILayout.Height(32)))
            {
                Scan();
            }

            using (new EditorGUI.DisabledScope(_targets.Count == 0))
            {
                if (GUILayout.Button(_dryRun ? "Dry Runで確認" : "Streaming MipmapsをON", GUILayout.Height(32)))
                {
                    ApplyStreamingMipmaps();
                }
            }
        }
    }

    private void DrawResults()
    {
        EditorGUILayout.LabelField("結果", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            int changeCount = _targets.Count(t => t.WouldChange);
            EditorGUILayout.LabelField("対象Texture", _targets.Count + " 件 / 変更候補 " + changeCount + " 件");
            _targetScroll = EditorGUILayout.BeginScrollView(_targetScroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(260));
            foreach (var item in _targets)
            {
                DrawCandidateRow(item, false);
            }
            EditorGUILayout.EndScrollView();
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("除外Texture", _excluded.Count + " 件");
            _excludedScroll = EditorGUILayout.BeginScrollView(_excludedScroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(260));
            foreach (var item in _excluded)
            {
                DrawCandidateRow(item, true);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private static void DrawCandidateRow(TextureCandidate item, bool excluded)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(item.Texture, typeof(Texture2D), false, GUILayout.Width(220));
                GUILayout.Label(item.TextureName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(item.Width + "x" + item.Height, GUILayout.Width(90));
                GUILayout.Label("Mip:" + (item.MipmapEnabled ? "ON" : "OFF"), GUILayout.Width(70));
                GUILayout.Label("Streaming:" + (item.StreamingMipmaps ? "ON" : "OFF"), GUILayout.Width(110));
            }

            EditorGUILayout.LabelField("Path", item.AssetPath);
            if (!string.IsNullOrEmpty(item.Source)) EditorGUILayout.LabelField("Source", item.Source);
            if (!string.IsNullOrEmpty(item.PropertyName)) EditorGUILayout.LabelField("Property", item.PropertyName);

            if (excluded)
            {
                EditorGUILayout.LabelField("除外理由", item.Reason);
            }
            else
            {
                EditorGUILayout.LabelField("状態", item.WouldChange ? "OFF → ON" : "Already Enabled");
            }
        }
    }

    private void DrawLogButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("ログをクリップボードにコピー", GUILayout.Width(220)))
            {
                EditorGUIUtility.systemCopyBuffer = _lastLog ?? string.Empty;
            }
            if (GUILayout.Button("ログウィンドウ表示", GUILayout.Width(180)))
            {
                ToolkitLogWindow.ShowLog(LogWindowTitle, _lastLog ?? string.Empty);
            }
            if (GUILayout.Button("Consoleへ出力", GUILayout.Width(140)))
            {
                Debug.Log(_lastLog ?? string.Empty);
            }
        }
    }

    private void PullCurrentSelection()
    {
        _hierarchyObjects.Clear();
        _prefabAssets.Clear();

        foreach (var obj in Selection.objects)
        {
            if (obj is GameObject go)
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab)
                {
                    if (!_prefabAssets.Contains(go)) _prefabAssets.Add(go);
                }
                else
                {
                    if (!_hierarchyObjects.Contains(go)) _hierarchyObjects.Add(go);
                }
            }
            else if (obj is DefaultAsset da)
            {
                string path = AssetDatabase.GetAssetPath(da);
                if (AssetDatabase.IsValidFolder(path)) _folder = da;
            }
        }
    }

    private void Scan()
    {
        _targets.Clear();
        _excluded.Clear();

        var rawRecords = new List<TextureRecord>();

        if (_targetMode == TargetMode.プロジェクト内Texture2Dすべて)
        {
            rawRecords.AddRange(CollectAllProjectTextures());
        }
        else
        {
            var roots = ResolveScanRoots();
            foreach (var root in roots)
            {
                rawRecords.AddRange(CollectTexturesFromRoot(root));
            }
        }

        var unique = MergeRecords(rawRecords);
        foreach (var record in unique)
        {
            AddCandidate(record);
        }

        BuildLog("解析");
        ToolkitLogWindow.ShowLog(LogWindowTitle, _lastLog);
    }

    private List<ScanRoot> ResolveScanRoots()
    {
        var roots = new List<ScanRoot>();

        if (_targetMode == TargetMode.選択中のヒエラルキー)
        {
            foreach (var go in _hierarchyObjects.Where(x => x != null))
            {
                roots.Add(new ScanRoot
                {
                    IsPrefabAsset = false,
                    RootObject = go,
                    Label = GetTransformPath(go.transform)
                });
            }
        }
        else if (_targetMode == TargetMode.選択中のPrefab)
        {
            foreach (var go in _prefabAssets.Where(x => x != null))
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path))
                {
                    roots.Add(new ScanRoot
                    {
                        IsPrefabAsset = true,
                        RootObject = go,
                        PrefabAssetPath = path,
                        Label = path
                    });
                }
            }
        }
        else if (_targetMode == TargetMode.指定フォルダ内Prefab)
        {
            if (_folder == null) return roots;
            string folderPath = AssetDatabase.GetAssetPath(_folder);
            if (!AssetDatabase.IsValidFolder(folderPath)) return roots;

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                roots.Add(new ScanRoot
                {
                    IsPrefabAsset = true,
                    RootObject = prefab,
                    PrefabAssetPath = path,
                    Label = path
                });
            }
        }

        return roots;
    }

    private static List<TextureRecord> CollectTexturesFromRoot(ScanRoot root)
    {
        var records = new List<TextureRecord>();
        if (root == null || root.RootObject == null) return records;

        var renderers = root.RootObject.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            string rendererPath = root.IsPrefabAsset
                ? root.PrefabAssetPath + " :: " + GetTransformPath(renderer.transform)
                : GetTransformPath(renderer.transform);

            var materials = renderer.sharedMaterials;
            if (materials == null) continue;

            foreach (var mat in materials)
            {
                if (mat == null || mat.shader == null) continue;

                int propertyCount;
                try
                {
                    propertyCount = ShaderUtil.GetPropertyCount(mat.shader);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;

                    string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                    Texture tex = null;
                    try
                    {
                        tex = mat.GetTexture(propName);
                    }
                    catch
                    {
                        // Shader側にPropertyがあってもMaterial側で取得に失敗するケースを無視する。
                    }

                    if (tex is Texture2D tex2D)
                    {
                        string path = AssetDatabase.GetAssetPath(tex2D);
                        if (string.IsNullOrEmpty(path)) continue;

                        records.Add(new TextureRecord
                        {
                            Texture = tex2D,
                            AssetPath = path,
                            Source = rendererPath + " / " + mat.name,
                            PropertyName = propName
                        });
                    }
                }
            }
        }

        return records;
    }

    private static List<TextureRecord> CollectAllProjectTextures()
    {
        var records = new List<TextureRecord>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) continue;
            records.Add(new TextureRecord
            {
                Texture = texture,
                AssetPath = path,
                Source = "Project Texture2D",
                PropertyName = string.Empty
            });
        }
        return records;
    }

    private static List<TextureRecord> MergeRecords(List<TextureRecord> records)
    {
        var map = new Dictionary<string, TextureRecord>();
        foreach (var r in records)
        {
            if (r == null || string.IsNullOrEmpty(r.AssetPath) || r.Texture == null) continue;

            if (!map.TryGetValue(r.AssetPath, out var existing))
            {
                map[r.AssetPath] = r;
            }
            else
            {
                if (!string.IsNullOrEmpty(r.Source) && !existing.Source.Contains(r.Source))
                {
                    existing.Source += "\n" + r.Source;
                }
                if (!string.IsNullOrEmpty(r.PropertyName) && !existing.PropertyName.Contains(r.PropertyName))
                {
                    existing.PropertyName += string.IsNullOrEmpty(existing.PropertyName) ? r.PropertyName : ", " + r.PropertyName;
                }
            }
        }
        return map.Values.OrderBy(x => x.AssetPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddCandidate(TextureRecord record)
    {
        if (record == null || record.Texture == null || string.IsNullOrEmpty(record.AssetPath)) return;

        var candidate = new TextureCandidate
        {
            Texture = record.Texture,
            AssetPath = record.AssetPath,
            TextureName = record.Texture.name,
            Width = record.Texture.width,
            Height = record.Texture.height,
            Source = record.Source,
            PropertyName = record.PropertyName
        };

        var importer = AssetImporter.GetAtPath(record.AssetPath) as TextureImporter;
        if (importer == null)
        {
            candidate.Kind = ResultKind.Excluded;
            candidate.Reason = "TextureImporterを取得できない";
            _excluded.Add(candidate);
            return;
        }

        candidate.MipmapEnabled = importer.mipmapEnabled;
        candidate.StreamingMipmaps = importer.streamingMipmaps;
        candidate.WouldChange = !importer.streamingMipmaps;

        string reason = GetExcludeReason(candidate, importer);
        if (!string.IsNullOrEmpty(reason))
        {
            candidate.Kind = ResultKind.Excluded;
            candidate.Reason = reason;
            _excluded.Add(candidate);
            return;
        }

        if (!_includeAlreadyEnabledInTargetList && !candidate.WouldChange) return;

        candidate.Kind = ResultKind.Target;
        _targets.Add(candidate);
    }

    private string GetExcludeReason(TextureCandidate c, TextureImporter importer)
    {
        if (_excludeSpriteAndNon2D)
        {
            if (importer.textureType == TextureImporterType.Sprite) return "TextureImporterType.Sprite";
            if (importer.textureShape != TextureImporterShape.Texture2D) return "TextureShapeがTexture2Dではない: " + importer.textureShape;
        }

        if (_excludeTexturesWithoutMipmaps && !importer.mipmapEnabled)
        {
            return "MipMap未生成";
        }

        if (_excludeSmallTextures && c.Width <= _smallTextureThreshold && c.Height <= _smallTextureThreshold)
        {
            return "小型Texture: " + c.Width + "x" + c.Height;
        }

        if (_excludeEditorGizmosPaths)
        {
            string match = FirstContains(c.AssetPath, SplitLines(_excludePathKeywords));
            if (!string.IsNullOrEmpty(match)) return "除外パス一致: " + match;
        }

        if (_excludeNameKeywords)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(c.AssetPath);
            string match = FirstContains(name, SplitLines(_excludeNameKeywordsText));
            if (!string.IsNullOrEmpty(match)) return "名前除外一致: " + match;
        }

        if (_excludeShaderPropertyKeywords && !string.IsNullOrEmpty(c.PropertyName))
        {
            string match = FirstContains(c.PropertyName, SplitLines(_excludePropertyKeywordsText));
            if (!string.IsNullOrEmpty(match)) return "Shader Property除外一致: " + match;
        }

        return string.Empty;
    }

    private void ApplyStreamingMipmaps()
    {
        if (_targets.Count == 0)
        {
            EditorUtility.DisplayDialog(WindowTitle, "対象Textureがありません。", "OK");
            return;
        }

        int changed = 0;
        int already = 0;
        int failed = 0;
        var sb = new StringBuilder();
        sb.AppendLine("# Streaming Mipmap Setup 実行ログ");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Dry Run: " + _dryRun);
        sb.AppendLine();

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var item in _targets)
            {
                if (item == null || string.IsNullOrEmpty(item.AssetPath)) continue;
                var importer = AssetImporter.GetAtPath(item.AssetPath) as TextureImporter;
                if (importer == null)
                {
                    failed++;
                    sb.AppendLine("[Failed] " + item.AssetPath + " / TextureImporter取得不可");
                    continue;
                }

                bool before = importer.streamingMipmaps;
                if (before)
                {
                    already++;
                    sb.AppendLine("[Already Enabled] " + item.AssetPath);
                    continue;
                }

                if (_dryRun)
                {
                    changed++;
                    sb.AppendLine("[Would Change] OFF -> ON : " + item.AssetPath);
                }
                else
                {
                    importer.streamingMipmaps = true;
                    importer.SaveAndReimport();
                    changed++;
                    sb.AppendLine("[Changed] OFF -> ON : " + item.AssetPath);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        if (!_dryRun)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        sb.AppendLine();
        sb.AppendLine("# Summary");
        sb.AppendLine("Change Candidate: " + changed);
        sb.AppendLine("Already Enabled: " + already);
        sb.AppendLine("Failed: " + failed);
        sb.AppendLine("Excluded: " + _excluded.Count);

        _lastLog = sb.ToString();
        Debug.Log(_lastLog);
        ToolkitLogWindow.ShowLog(LogWindowTitle, _lastLog);

        // 実処理後は状態更新。
        if (!_dryRun) Scan();
    }

    private void BuildLog(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Streaming Mipmap Setup " + title + "ログ");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Target Mode: " + _targetMode);
        sb.AppendLine("Dry Run: " + _dryRun);
        sb.AppendLine();

        sb.AppendLine("# Targets");
        foreach (var t in _targets)
        {
            sb.AppendLine("- " + (t.WouldChange ? "[OFF -> ON] " : "[Already Enabled] ") + t.AssetPath + " | " + t.Width + "x" + t.Height + " | Mip:" + t.MipmapEnabled + " | Property:" + t.PropertyName);
        }

        sb.AppendLine();
        sb.AppendLine("# Excluded");
        foreach (var e in _excluded)
        {
            sb.AppendLine("- [Excluded] " + e.AssetPath + " | " + e.Width + "x" + e.Height + " | Reason: " + e.Reason + " | Property:" + e.PropertyName);
        }

        sb.AppendLine();
        sb.AppendLine("# Summary");
        sb.AppendLine("Targets: " + _targets.Count);
        sb.AppendLine("Change Candidates: " + _targets.Count(x => x.WouldChange));
        sb.AppendLine("Excluded: " + _excluded.Count);

        _lastLog = sb.ToString();
        Debug.Log(_lastLog);
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();
    }

    private static string FirstContains(string source, IEnumerable<string> keywords)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        foreach (string keyword in keywords)
        {
            if (string.IsNullOrEmpty(keyword)) continue;
            if (source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return keyword;
        }
        return string.Empty;
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null) return string.Empty;
        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack.ToArray());
    }

    private sealed class ToolkitLogWindow : EditorWindow
    {
        private string _text = string.Empty;
        private Vector2 _scroll;

        public static void ShowLog(string title, string text)
        {
            var window = GetWindow<ToolkitLogWindow>(title);
            window.minSize = new Vector2(720, 520);
            window._text = text ?? string.Empty;
            window.Show();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("コピー", GUILayout.Width(100)))
                {
                    EditorGUIUtility.systemCopyBuffer = _text ?? string.Empty;
                }
                if (GUILayout.Button("Consoleへ出力", GUILayout.Width(140)))
                {
                    Debug.Log(_text ?? string.Empty);
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_text ?? string.Empty, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }
}

}
