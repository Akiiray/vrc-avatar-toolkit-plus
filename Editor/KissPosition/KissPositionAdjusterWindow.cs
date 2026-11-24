using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal sealed class KissPositionAdjusterWindow : EditorWindow
    {
        private sealed class Preview
        {
            internal KissGimmickPlacement Item;
            internal Vector3 Target;
        }

        private VRCAvatarDescriptor avatar;
        private readonly List<Preview> previews = new List<Preview>();
        private List<KissGimmickPlacement> candidates = new List<KissGimmickPlacement>();
        private readonly HashSet<KissGimmickPlacement> selected = new HashSet<KissGimmickPlacement>();
        private Vector2 scroll;
        private int basis, incomplete;
        private string message, evidence;
        private MessageType severity;
        private bool scanned;
        private Action validateLive;
        private double nextCheck;

        [MenuItem("Tools/VRC Avatar Toolkit Plus/Kissギミック位置調整")]
        private static void Open()
        {
            var window = GetWindow<KissPositionAdjusterWindow>("Kissギミック位置調整");
            window.minSize = new Vector2(330, 360);
            if (!window.avatar) window.SelectAvatar(Selection.activeGameObject);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DrawScene;
            Undo.undoRedoPerformed += Changed;
            EditorApplication.hierarchyChanged += Changed;
            EditorApplication.update += CheckLive;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            Undo.undoRedoPerformed -= Changed;
            EditorApplication.hierarchyChanged -= Changed;
            EditorApplication.update -= CheckLive;
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            ClearPreview();
        }

        private void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            // エディタ側からの構成変更は再検出する。表情・ボーンの直接変更はスナップショットでも確認する。
            if (scanned && stream.length > 0) Changed();
        }

        private void Changed()
        {
            if (!scanned && previews.Count == 0) return;
            ClearPreview();
            candidates.Clear();
            selected.Clear();
            scanned = false;
            Show("構成が変更されました。対象を再検出してください。", MessageType.Info);
        }

        private void Show(string text, MessageType severity)
        {
            message = text;
            this.severity = severity;
            Repaint();
        }

        private void ClearPreview()
        {
            previews.Clear();
            validateLive = null;
            evidence = null;
            message = null;
            SceneView.RepaintAll();
        }

        private void SelectAvatar(GameObject target)
        {
            ClearPreview();
            avatar = KissGimmickPlacement.Resolve(target);
            Scan();
        }

        private void Scan()
        {
            ClearPreview();
            selected.Clear();
            candidates = avatar ? KissGimmickPlacement.Find(avatar, out incomplete) : new List<KissGimmickPlacement>();
            scanned = avatar;
            foreach (bool sound in new[] { false, true })
            {
                var group = candidates.Where(x => x.Sound == sound && x.Problem == null).ToArray();
                if (group.Length == 1) selected.Add(group[0]);
            }
            message = null;
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Kissギミック位置調整", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Hierarchy 上の導入済みギミックを検出し、Scene View で位置を確認してから適用します。", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("対象アバター", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var target = (GameObject)EditorGUILayout.ObjectField("アバター", avatar ? avatar.gameObject : null, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) SelectAvatar(target);
            if (GUILayout.Button("Hierarchy の現在選択から設定")) SelectAvatar(Selection.activeGameObject);
            using (new EditorGUI.DisabledScope(!avatar))
                if (GUILayout.Button("ギミックを再検出")) Scan();
            if (!avatar)
                EditorGUILayout.HelpBox("アバター、またはその子を選択してください。Prefab Asset と Prefab モードは対象外です。", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("調整対象", EditorStyles.boldLabel);
            if (scanned && candidates.Count == 0)
                EditorGUILayout.HelpBox("対応する RealKissSystem または JustKissSFX の Contact が見つかりませんでした。", MessageType.Info);
            if (scanned && incomplete > 0)
                EditorGUILayout.HelpBox("対応Prefabの参照がありますが、主 Contact または構成を確認できない個体があります。", MessageType.Warning);
            foreach (var item in candidates)
            {
                if (!item.Contact) continue;
                using (new EditorGUI.DisabledScope(item.Problem != null))
                {
                    bool before = selected.Contains(item);
                    bool after = EditorGUILayout.ToggleLeft(item.Sound ? "JustKissSFX" : "RealKissSystem", before);
                    if (after != before)
                    {
                        if (after) selected.Add(item); else selected.Remove(item);
                        ClearPreview();
                    }
                }
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("移動対象", item.Move, typeof(Transform), true);
                if (item.Problem != null) EditorGUILayout.HelpBox(item.Problem, MessageType.Warning);
                if (!item.Contact.gameObject.activeInHierarchy)
                    EditorGUILayout.HelpBox("このギミックは非アクティブです。適用後も非アクティブのままです。", MessageType.Info);
            }
            if (candidates.Any(x => x.Sound))
            {
                int value = EditorGUILayout.Popup("JustKissSFX の配置基準", basis,
                    new[] { "鼻先候補（標準）", "鼻と口の中点", "口元候補" });
                if (value != basis) { basis = value; ClearPreview(); }
            }

            using (new EditorGUI.DisabledScope(!avatar || !scanned || selected.Count == 0))
                if (GUILayout.Button("自動位置推定")) Analyze();

            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, severity);
            if (previews.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Scene View プレビュー", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(evidence, MessageType.Info);
                foreach (var preview in previews)
                {
                    if (!preview.Item.Contact) continue;
                    EditorGUILayout.LabelField(preview.Item.Sound ? "JustKissSFX" : "RealKissSystem", EditorStyles.boldLabel);
                    var frame = avatar.transform;
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.Vector3Field("現在位置（Avatar ローカル）", frame.InverseTransformPoint(preview.Item.Center));
                    EditorGUI.BeginChangeCheck();
                    var point = EditorGUILayout.Vector3Field("候補位置（Avatar ローカル）", frame.InverseTransformPoint(preview.Target));
                    if (EditorGUI.EndChangeCheck())
                    {
                        preview.Target = frame.TransformPoint(point);
                        SceneView.RepaintAll();
                    }
                    EditorGUILayout.LabelField($"移動距離 {Vector3.Distance(preview.Item.Center, preview.Target) * 1000:0.0} mm 相当");
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("プレビュー解除")) ClearPreview();
                    if (GUILayout.Button("調整結果を適用")) Apply();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Analyze()
        {
            ClearPreview();
            try
            {
                var result = KissFaceAnalysis.Analyze(avatar);
                foreach (var item in candidates.Where(selected.Contains))
                {
                    if (!item.Move || !item.Contact || item.Problem != null)
                        throw new InvalidOperationException("対象が変更されました。再検出してください。");
                    KissFaceAnalysis.CheckFrame(item.Contact.transform);
                    KissFaceAnalysis.CheckFrame(item.Move);
                    if (item.Move.parent) KissFaceAnalysis.CheckFrame(item.Move.parent);
                    var position = result.Surface;
                    if (item.Sound)
                        position = basis == 2 ? result.Mouth : (basis == 1 || !result.ReliableNose ? result.Midpoint : result.Nose);
                    previews.Add(new Preview { Item = item, Target = position });
                }
                CaptureLiveState();
                bool fallback = !result.Projected && previews.Any(p => !p.Item.Sound);
                bool noseFallback = basis != 2 && !result.ReliableNose && previews.Any(p => p.Item.Sound);
                evidence = $"{result.Visemes} 種類の Viseme の一致を使用。灰色が現在、色付きが候補です。Scene View または数値欄で微調整できます。";
                var warnings = new List<string>();
                if (result.RebuiltNormals) warnings.Add("BakeMesh の法線が不足したため、三角形から正面を推定しました。");
                if (fallback) warnings.Add("RealKiss の前方表面を確認できないため、未投影の口元候補です。");
                if (noseFallback) warnings.Add("鼻先の突出を確認できないため、口元の上の仮点を使った中点候補です。");
                Show(warnings.Count > 0 ? "要確認。 " + string.Join(" ", warnings) : "推定できました。適用前に顔の正面と側面を確認してください。",
                    warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
                SceneView.RepaintAll();
            }
            catch (Exception e)
            {
                ClearPreview();
                Show(e.Message, MessageType.Warning);
            }
        }

        private void CaptureLiveState()
        {
            var renderer = avatar.VisemeSkinnedMesh;
            var mesh = renderer.sharedMesh;
            var head = KissFaceAnalysis.Head(avatar);
            var transforms = renderer.bones.Concat(new[] { renderer.transform, head, avatar.transform })
                .Concat(previews.SelectMany(p => new[] { p.Item.Move, p.Item.Contact.transform, p.Item.Audio }).Where(t => t))
                .Where(t => t).Distinct().ToArray();
            var matrices = transforms.Select(t => t.localToWorldMatrix).ToArray();
            var weights = Enumerable.Range(0, mesh.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
            var names = (avatar.VisemeBlendShapes ?? Array.Empty<string>()).ToArray();
            validateLive = () =>
            {
                if (!avatar || !renderer || avatar.VisemeSkinnedMesh != renderer || renderer.sharedMesh != mesh ||
                    !names.SequenceEqual(avatar.VisemeBlendShapes ?? Array.Empty<string>()) ||
                    previews.Any(p => !p.Item.Move || !p.Item.Contact) ||
                    transforms.Where((t, i) => !t || t.localToWorldMatrix != matrices[i]).Any() ||
                    weights.Where((w, i) => w != renderer.GetBlendShapeWeight(i)).Any())
                    throw new InvalidOperationException("対象や顔の状態が変わりました。再解析してください。");
            };
        }

        private void CheckLive()
        {
            if (validateLive == null || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + .25;
            try { validateLive(); }
            catch (InvalidOperationException e)
            {
                ClearPreview();
                Show(e.Message, MessageType.Warning);
            }
        }

        private void Apply()
        {
            if (previews.Count == 0 || validateLive == null) return;
            try
            {
                validateLive();
                var current = KissGimmickPlacement.Find(avatar, out _);
                foreach (var p in previews)
                {
                    var match = current.Find(x => x.Contact == p.Item.Contact && x.Move == p.Item.Move);
                    if (match == null || match.Problem != null || match.Audio != p.Item.Audio ||
                        match.Center != p.Item.Center || match.Offset != p.Item.Offset ||
                        match.Constraint != p.Item.Constraint || KissGimmickPlacement.Resolve(match.Move.gameObject) != avatar ||
                        float.IsNaN(p.Target.sqrMagnitude) || float.IsInfinity(p.Target.sqrMagnitude))
                        throw new InvalidOperationException("適用条件が変わりました。再検出してください。");
                    if (previews.Any(other => other != p && (p.Item.Move.IsChildOf(other.Item.Move) ||
                        (p.Item.Audio && p.Item.Audio == other.Item.Audio))))
                        throw new InvalidOperationException("移動対象が重なるため、個別に適用してください。");
                }
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Kissギミック位置調整");
                try
                {
                    foreach (var p in previews) p.Item.Apply(p.Target);
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(group);
                }
                catch
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.RevertAllDownToGroup(group);
                    throw;
                }
                Scan();
                Show("アバターのインスタンスへ適用しました。Undo で元に戻せます。", MessageType.Info);
            }
            catch (Exception e)
            {
                ClearPreview();
                Show(e.Message, MessageType.Error);
            }
        }

        private void DrawScene(SceneView view)
        {
            foreach (var p in previews)
            {
                if (!p.Item.Contact || !p.Item.Move) continue;
                var current = p.Item.Center;
                var color = p.Item.Sound ? new Color(.3f, .75f, 1) : new Color(1, .45f, .65f);
                DrawContact(p.Item, current, Color.gray);
                DrawContact(p.Item, p.Target, color);
                using (new Handles.DrawingScope(color))
                {
                    Handles.DrawDottedLine(current, p.Target, 4);
                    Handles.Label(p.Target, p.Item.Sound ? "JustKissSFX" : "RealKiss");
                    if (p.Item.Audio)
                    {
                        Vector3 audio = p.Item.AudioPosition;
                        Vector3 targetAudio = audio + (p.Target - current);
                        Handles.DrawDottedLine(audio, targetAudio, 4);
                        Handles.Label(targetAudio, "Audio");
                        Handles.SphereHandleCap(0, targetAudio, Quaternion.identity, .008f, EventType.Repaint);
                    }
                    EditorGUI.BeginChangeCheck();
                    var target = Handles.PositionHandle(p.Target, p.Item.Move.rotation);
                    if (EditorGUI.EndChangeCheck()) { p.Target = target; Repaint(); }
                }
            }
        }

        private static void DrawContact(KissGimmickPlacement item, Vector3 center, Color color)
        {
            var t = item.Contact.transform;
            using (new Handles.DrawingScope(color, Matrix4x4.TRS(center, t.rotation * item.Rotation, t.lossyScale)))
            {
                float r = item.Radius;
                if (item.Shape == 0)
                {
                    Handles.DrawWireDisc(Vector3.zero, Vector3.right, r);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, r);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.forward, r);
                    return;
                }
                var top = Vector3.up * Mathf.Max(0, item.Height * .5f - r);
                Handles.DrawWireDisc(top, Vector3.up, r);
                Handles.DrawWireDisc(-top, Vector3.up, r);
                foreach (var axis in new[] { Vector3.right, Vector3.forward })
                {
                    var normal = Vector3.Cross(axis, Vector3.up);
                    Handles.DrawWireArc(top, normal, axis, 180, r);
                    Handles.DrawWireArc(-top, -normal, axis, 180, r);
                    Handles.DrawLine(top + axis * r, -top + axis * r);
                    Handles.DrawLine(top - axis * r, -top - axis * r);
                }
            }
        }
    }
}
