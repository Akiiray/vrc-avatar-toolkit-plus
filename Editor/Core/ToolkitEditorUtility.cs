using UnityEditor;
using UnityEngine;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    public static class ToolkitEditorUtility
    {
        public static bool IsPlayModeOrChanging(bool showDialog = true)
        {
            bool blocked = EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating;
            if (blocked && showDialog)
            {
                EditorUtility.DisplayDialog(
                    ToolkitConstants.ProductName,
                    "Play中、コンパイル中、またはAsset更新中は実行できません。処理が終わってから再実行してください。",
                    "OK"
                );
            }
            return blocked;
        }

        public static void SetDirtyWithPrefabSupport(UnityEngine.Object obj)
        {
            if (obj == null) return;

            EditorUtility.SetDirty(obj);

            GameObject go = null;
            if (obj is GameObject g) go = g;
            else if (obj is Component c) go = c.gameObject;

            if (go == null) return;

            var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (prefabRoot != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(prefabRoot);
                EditorUtility.SetDirty(prefabRoot);
            }
        }

        public static bool ConfirmDangerousAction(string title, string message)
        {
            return EditorUtility.DisplayDialog(title, message, "実行", "キャンセル");
        }

        public static void SaveAndRefreshAssets()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
