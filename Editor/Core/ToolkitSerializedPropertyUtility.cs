using UnityEditor;
using UnityEngine;
using System.Globalization;
using System.Text;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    public static class ToolkitSerializedPropertyUtility
    {
        public static string ToDisplayString(SerializedProperty property)
        {
            if (property == null) return "<null>";

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return property.stringValue ?? string.Empty;
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return ObjectReferenceToDisplayString(property.objectReferenceValue);
                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                    return property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString();
                case SerializedPropertyType.AnimationCurve:
                    return property.animationCurveValue == null ? "<null>" : "AnimationCurve(keys=" + property.animationCurveValue.length + ")";
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString();
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.eulerAngles.ToString();
                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue.ToString();
                case SerializedPropertyType.RectInt:
                    return property.rectIntValue.ToString();
                case SerializedPropertyType.BoundsInt:
                    return property.boundsIntValue.ToString();
                default:
                    return property.propertyType.ToString();
            }
        }

        /// <summary>
        /// 比較・Fingerprint用の安定した文字列化。
        /// 表示用ではなく、重複検出などで使う想定です。
        /// </summary>
        public static string ToStableString(SerializedProperty property, Transform root = null, bool deepArray = true)
        {
            if (property == null) return "<null>";

            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                if (!deepArray) return "Array(size=" + property.arraySize + ")";

                var sb = new StringBuilder();
                sb.Append("Array(size=").Append(property.arraySize).Append(")[");
                for (int i = 0; i < property.arraySize; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(ToStableString(property.GetArrayElementAtIndex(i), root, deepArray));
                }
                sb.Append("]");
                return sb.ToString();
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return "i:" + property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return "b:" + (property.boolValue ? "1" : "0");
                case SerializedPropertyType.Float:
                    return "f:" + QuantizeFloat(property.floatValue);
                case SerializedPropertyType.String:
                    return "s:" + (property.stringValue ?? string.Empty);
                case SerializedPropertyType.Color:
                    return "c:" + QuantizeFloat(property.colorValue.r) + "," + QuantizeFloat(property.colorValue.g) + "," + QuantizeFloat(property.colorValue.b) + "," + QuantizeFloat(property.colorValue.a);
                case SerializedPropertyType.ObjectReference:
                    return "o:" + ObjectReferenceToStableString(property.objectReferenceValue, root);
                case SerializedPropertyType.LayerMask:
                    return "lm:" + property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                    return "e:" + property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector2:
                    return "v2:" + QuantizeVector2(property.vector2Value);
                case SerializedPropertyType.Vector3:
                    return "v3:" + QuantizeVector3(property.vector3Value);
                case SerializedPropertyType.Vector4:
                    return "v4:" + QuantizeVector4(property.vector4Value);
                case SerializedPropertyType.Rect:
                    return "rect:" + QuantizeFloat(property.rectValue.x) + "," + QuantizeFloat(property.rectValue.y) + "," + QuantizeFloat(property.rectValue.width) + "," + QuantizeFloat(property.rectValue.height);
                case SerializedPropertyType.Bounds:
                    return "bounds:" + QuantizeVector3(property.boundsValue.center) + ";" + QuantizeVector3(property.boundsValue.size);
                case SerializedPropertyType.Quaternion:
                    return "q:" + QuantizeFloat(property.quaternionValue.x) + "," + QuantizeFloat(property.quaternionValue.y) + "," + QuantizeFloat(property.quaternionValue.z) + "," + QuantizeFloat(property.quaternionValue.w);
                default:
                    return property.propertyType + ":" + ToDisplayString(property);
            }
        }

        public static string ObjectReferenceToDisplayString(Object obj)
        {
            if (obj == null) return "<null>";
            string path = AssetDatabase.GetAssetPath(obj);
            return string.IsNullOrEmpty(path) ? obj.name + " (SceneObject)" : obj.name + " (" + path + ")";
        }

        public static string ObjectReferenceToStableString(Object obj, Transform root = null)
        {
            if (obj == null) return "<null>";

            if (obj is GameObject go)
            {
                if (root != null && go.transform != null && IsChildOfOrSame(go.transform, root))
                    return "go:" + ToolkitPathUtility.GetRelativePath(root, go.transform, includeRootName: true);
                return "go:" + ToolkitPathUtility.GetHierarchyPath(go);
            }

            if (obj is Component component)
            {
                if (root != null && component.transform != null && IsChildOfOrSame(component.transform, root))
                    return "component:" + component.GetType().FullName + "@" + ToolkitPathUtility.GetRelativePath(root, component.transform, includeRootName: true);
                return "component:" + component.GetType().FullName + "@" + ToolkitPathUtility.GetHierarchyPath(component);
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path)) return "asset:" + path + "#" + obj.name + ":" + obj.GetType().FullName;

            return "object:" + obj.GetType().FullName + ":" + obj.name;
        }

        private static bool IsChildOfOrSame(Transform target, Transform root)
        {
            var current = target;
            while (current != null)
            {
                if (current == root) return true;
                current = current.parent;
            }
            return false;
        }

        private static string QuantizeFloat(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string QuantizeVector2(Vector2 value)
        {
            return QuantizeFloat(value.x) + "," + QuantizeFloat(value.y);
        }

        private static string QuantizeVector3(Vector3 value)
        {
            return QuantizeFloat(value.x) + "," + QuantizeFloat(value.y) + "," + QuantizeFloat(value.z);
        }

        private static string QuantizeVector4(Vector4 value)
        {
            return QuantizeFloat(value.x) + "," + QuantizeFloat(value.y) + "," + QuantizeFloat(value.z) + "," + QuantizeFloat(value.w);
        }
    }
}
