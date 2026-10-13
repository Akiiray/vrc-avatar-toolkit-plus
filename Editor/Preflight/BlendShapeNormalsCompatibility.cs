using System;
using System.Reflection;
using UnityEditor;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal static class BlendShapeNormalsCompatibility
    {
        private const string PropertyName = "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes";
        private static PropertyInfo _property;
        private static bool _resolved;

        public static bool TryRead(ModelImporter importer, out bool legacy, out string reason)
        {
            legacy = false;
            reason = null;
            try
            {
                // 解決自体の例外もBだけのスキップにする（型初期化例外にしない）。
                if (!_resolved)
                {
                    _property = typeof(ModelImporter).GetProperty(PropertyName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _resolved = true;
                }
                if (_property == null || _property.PropertyType != typeof(bool) ||
                    _property.GetGetMethod(true) == null || _property.GetSetMethod(true) == null)
                {
                    reason = "Legacy Blend Shape Normalsの読書きAPIを取得できません。";
                    return false;
                }
                legacy = (bool)_property.GetValue(importer);
                return true;
            }
            catch (Exception e)
            {
                reason = "Legacy取得失敗: " + e.GetBaseException().Message;
                return false;
            }
        }

        public static bool TryWriteAndVerify(ModelImporter importer, bool value, out string reason)
        {
            if (!TryRead(importer, out var before, out reason)) return false;
            try
            {
                if (before != value) _property.SetValue(importer, value);
                if (!TryRead(importer, out var actual, out reason)) return false;
                if (actual == value) return true;
                reason = "Legacy設定値を確認できません（期待値: " + value + "、実際: " + actual + "）。";
            }
            catch (Exception e)
            {
                reason = "Legacy設定失敗: " + e.GetBaseException().Message;
            }
            return false;
        }
    }
}
