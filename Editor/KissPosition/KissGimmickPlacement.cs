using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal sealed class KissGimmickPlacement
    {
        private const string RealAsset = "a90e23d40bb6e064396d2f03e861b899";
        private const string SoundAsset = "4ca0ffe6677dbf044975d285bd7bb0d4";
        private const string MouthTag = "superneko.realkiss.contact.mouth";

        internal Transform Move, Audio;
        internal Component Contact;
        internal ParentConstraint Constraint;
        internal bool Sound;
        internal string Problem;
        internal Vector3 Offset;
        internal Quaternion Rotation;
        internal float Radius, Height;
        internal int Shape;
        internal Vector3 Center => Contact.transform.TransformPoint(Offset);

        internal static VRCAvatarDescriptor Resolve(GameObject selected)
        {
            if (!selected || EditorUtility.IsPersistent(selected) || !selected.scene.IsValid() ||
                EditorSceneManagerIsPreview(selected)) return null;
            return selected.GetComponentInParent<VRCAvatarDescriptor>(true);
        }

        private static bool EditorSceneManagerIsPreview(GameObject selected) =>
            UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(selected.scene) ||
            UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(selected) != null;

        internal static List<KissGimmickPlacement> Find(VRCAvatarDescriptor avatar, out int incomplete)
        {
            var all = avatar.GetComponentsInChildren<Transform>(true);
            var head = KissFaceAnalysis.Head(avatar);
            var components = all.SelectMany(t => t.GetComponents<Component>())
                .Where(c => c && (c.GetType().Name == "VRCContactSender" || c.GetType().Name == "VRCContactReceiver")).ToArray();
            var results = new List<KissGimmickPlacement>();
            foreach (var c in components)
            {
                var data = new SerializedObject(c);
                var tags = data.FindProperty("collisionTags");
                if (tags == null) continue;
                var values = Enumerable.Range(0, tags.arraySize).Select(i => tags.GetArrayElementAtIndex(i).stringValue).ToArray();
                bool sound = c.GetType().Name == "VRCContactReceiver" && data.FindProperty("parameter")?.stringValue == "KissSFX";
                bool real = c.GetType().Name == "VRCContactSender" && values.Contains(MouthTag);
                if (!sound && !real) continue;
                if (sound && !values.Contains("Head")) continue;
                Transform move = sound ? c.transform : FindRealRoot(c.transform, avatar.transform, head);
                var item = new KissGimmickPlacement { Sound = sound, Contact = c, Move = move };
                results.Add(item);
                if (!move)
                {
                    item.Problem = "RealKiss の移動ルートを安全に特定できません。";
                    continue;
                }
                var position = data.FindProperty("position");
                var rotation = data.FindProperty("rotation");
                var radius = data.FindProperty("radius");
                var shape = data.FindProperty("shapeType");
                var customRoot = data.FindProperty("rootTransform")?.objectReferenceValue;
                if (customRoot && customRoot != c.transform)
                {
                    item.Problem = "Contact が別の Root Transform を参照しています。";
                    continue;
                }
                if (position == null || rotation == null || radius == null || shape == null ||
                    (shape.intValue != 0 && shape.intValue != 1) || radius.floatValue <= 0)
                {
                    item.Problem = "Contact の形状または設定を識別できません。";
                    continue;
                }
                item.Offset = position.vector3Value;
                item.Rotation = rotation.quaternionValue;
                item.Radius = radius.floatValue;
                item.Height = data.FindProperty("height")?.floatValue ?? 0;
                item.Shape = shape.intValue;

                if (!head || (!move.IsChildOf(head) && !(sound && HasHeadProxy(move))))
                    item.Problem = "Head への追従を確認できません。";
                if (!sound) continue;

                var constraints = move.parent.GetComponentsInChildren<ParentConstraint>(true)
                    .Where(x => x.sourceCount > 0 && GetSourcesReference(x, move) &&
                        x.GetComponentInChildren<AudioSource>(true)).ToArray();
                if (constraints.Length == 1)
                {
                    item.Constraint = constraints[0];
                    item.Audio = constraints[0].transform;
                    var constraint = item.Constraint;
                    if (constraint.enabled && constraint.constraintActive &&
                        (constraint.sourceCount != 1 || constraint.GetSource(0).weight < .999f || constraint.weight < .999f ||
                            constraint.translationAxis != (Axis.X | Axis.Y | Axis.Z)))
                        item.Problem = "音源の Parent Constraint が単一ソースの完全な位置追従になっていません。";
                }
                else
                {
                    item.Problem = "CenterOfHead を参照する音源を一意に特定できません。";
                }
            }
            foreach (var duplicates in results.Where(x => x.Move).GroupBy(x => x.Move).Where(g => g.Count() > 1))
                foreach (var item in duplicates) item.Problem = "同じ移動対象に複数の主 Contact が存在します。";
            var rootSet = new HashSet<Transform>();
            foreach (var item in results)
                for (var t = item.Contact.transform; t && t != avatar.transform; t = t.parent)
                    rootSet.Add(t);
            incomplete = all.Count(t => !rootSet.Contains(t) && (Source(t, RealAsset) || Source(t, SoundAsset)));
            return results;
        }

        private static bool GetSourcesReference(ParentConstraint constraint, Transform target)
        {
            for (int i = 0; i < constraint.sourceCount; i++)
                if (constraint.GetSource(i).sourceTransform == target) return true;
            return false;
        }

        private static bool HasHeadProxy(Transform t)
        {
            var proxy = t.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName ==
                "nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            if (!proxy) return false;
            var p = new SerializedObject(proxy);
            return p.FindProperty("boneReference")?.intValue == (int)HumanBodyBones.Head &&
                p.FindProperty("attachmentMode")?.intValue == 2 &&
                string.IsNullOrEmpty(p.FindProperty("subPath")?.stringValue);
        }

        private static Transform FindRealRoot(Transform sender, Transform avatar, Transform head)
        {
            Transform structural = null;
            for (var t = sender.parent; t && t != avatar && t != head; t = t.parent)
            {
                if (Source(t, RealAsset)) return t;
                if (t.GetComponents<Component>().Any(c => c && c.GetType().FullName ==
                    "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator"))
                    structural = t;
            }
            return structural;
        }

        private static bool Source(Transform t, string guid)
        {
            for (var source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject); source;
                source = PrefabUtility.GetCorrespondingObjectFromSource(source))
            {
                if (source.transform.parent == null &&
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)) == guid) return true;
            }
            return false;
        }

        internal Vector3 AudioPosition
        {
            get
            {
                if (Constraint && Constraint.enabled && Constraint.constraintActive &&
                    Constraint.sourceCount == 1 && Constraint.GetSource(0).sourceTransform == Move)
                    return Move.TransformPoint(Constraint.GetTranslationOffset(0));
                return Audio ? Audio.position : Center;
            }
        }

        internal void Apply(Vector3 target)
        {
            Vector3 delta = target - Center;
            bool follows = Constraint && Constraint.enabled && Constraint.constraintActive;
            Undo.RecordObject(Move, "Kissギミック位置調整");
            Move.position += delta;
            PrefabUtility.RecordPrefabInstancePropertyModifications(Move);
            // 有効な Constraint は CenterOfHead と一緒に追従し、既存のオフセットを保持する。
            if (Audio && !follows && !Audio.IsChildOf(Move))
            {
                Undo.RecordObject(Audio, "Kissギミック位置調整");
                Audio.position += delta;
                PrefabUtility.RecordPrefabInstancePropertyModifications(Audio);
            }
        }
    }
}
