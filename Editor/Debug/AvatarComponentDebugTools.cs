using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.IO;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
public static class AvatarComponentDebugTools
{
    private const string WindowTitle = "VRC Avatar Toolkit Plus - デバッグレポート";
    private const string AvatarDescriptorFullName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
    private const string ExpressionParametersFullName = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters";

    // ---------------------------------------------------------------------
    // Existing features: console output is kept, but the same result is also
    // shown in a copy-friendly EditorWindow.
    // ---------------------------------------------------------------------

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/選択中オブジェクトのコンポーネント型を表示")]
    public static void PrintSelectedComponentTypes()
    {
        var report = new DebugReportBuilder("Selected Component Types");

        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendSelectedComponentTypes(report, go);
        ShowReport(report);
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/選択中オブジェクトのSerializedPropertyを表示")]
    public static void PrintSelectedSerializedProperties()
    {
        var report = new DebugReportBuilder("Selected Serialized Properties");

        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendSerializedProperties(report, go, includeValues: true);
        ShowReport(report);
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/選択中オブジェクトのField情報を表示")]
    public static void PrintSelectedFieldInfo()
    {
        var report = new DebugReportBuilder("Selected Field Info");

        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendFieldInfo(report, go);
        ShowReport(report);
    }

    // ---------------------------------------------------------------------
    // Added debug/development features.
    // ---------------------------------------------------------------------

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/選択中オブジェクトの完全レポートを表示")]
    public static void ShowFullSelectedDebugReport()
    {
        var report = new DebugReportBuilder("Full Selected Debug Report");

        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendBasicGameObjectInfo(report, go);
        AppendSelectedComponentTypes(report, go);
        AppendSerializedProperties(report, go, includeValues: true);
        AppendFieldInfo(report, go);
        AppendPrefabInfo(report, go);
        AppendHierarchyComponentSummary(report, go);

        ShowReport(report);
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/選択中アバターのパラメーターを表示")]
    public static void ShowSelectedAvatarParameters()
    {
        var report = new DebugReportBuilder("Selected Avatar Parameters");

        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        var descriptor = FindAvatarDescriptor(go);
        if (descriptor == null)
        {
            report.Warning("選択中のGameObject自身、親、子にVRCAvatarDescriptorが見つかりませんでした。");
            report.Line("アバターのルート、またはアバター配下のGameObjectを選択してください。");
            ShowReport(report);
            return;
        }

        AppendAvatarDescriptorSummary(report, descriptor.gameObject);
        AppendExpressionParametersFromDescriptor(report, descriptor);
        AppendAnimatorControllerParametersFromDescriptor(report, descriptor);

        ShowReport(report);
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/Scene内すべてのアバターパラメーターを表示")]
    public static void ShowAllAvatarParametersInScene()
    {
        var report = new DebugReportBuilder("All Avatar Parameters In Scene");
        var descriptors = FindAllAvatarDescriptorsInScene();

        if (descriptors.Count == 0)
        {
            report.Warning("現在のScene内にVRCAvatarDescriptorが見つかりませんでした。");
            ShowReport(report);
            return;
        }

        report.Line($"Avatar Descriptor Count: {descriptors.Count}");
        report.Blank();

        foreach (var descriptor in descriptors)
        {
            AppendAvatarDescriptorSummary(report, descriptor.gameObject);
            AppendExpressionParametersFromDescriptor(report, descriptor);
            AppendAnimatorControllerParametersFromDescriptor(report, descriptor);
            report.Separator();
        }

        ShowReport(report);
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/選択中Prefabの開発用情報を表示")]
    public static void ShowSelectedPrefabDevelopmentInfo()
    {
        var report = new DebugReportBuilder("Selected Prefab Development Info");

        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendBasicGameObjectInfo(report, go);
        AppendPrefabInfo(report, go);
        AppendHierarchyComponentSummary(report, go);
        AppendLikelyAvatarRelatedAssets(report, go);

        ShowReport(report);
    }

    [MenuItem("Tools/VRC Avatar Toolkit Plus/デバッグ/デバッグメニューを開く")]
    public static void OpenDebugReportWindow()
    {
        AvatarDebugReportWindow.Open("ここにデバッグ結果が表示されます。\n上の「デバッグメニュー」から調査したい項目を実行してください。\nHierarchy右クリックからも実行できます。", WindowTitle);
    }


    // ---------------------------------------------------------------------
    // Hierarchy right-click menus.
    // GameObject/ 以下のMenuItemはHierarchyの右クリックメニューにも出ます。
    // ---------------------------------------------------------------------

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/選択中オブジェクトの完全レポート", false, 20)]
    public static void ContextShowFullSelectedDebugReport()
    {
        ShowFullSelectedDebugReport();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/選択中Prefabの開発用情報", false, 21)]
    public static void ContextShowSelectedPrefabDevelopmentInfo()
    {
        ShowSelectedPrefabDevelopmentInfo();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/このオブジェクトを含むアバターのパラメーター", false, 22)]
    public static void ContextShowContainingAvatarParameters()
    {
        ShowSelectedAvatarParameters();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/このオブジェクト配下のコンポーネント集計", false, 23)]
    public static void ContextShowHierarchyComponentSummary()
    {
        var report = new DebugReportBuilder("Hierarchy Component Summary");
        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendBasicGameObjectInfo(report, go);
        AppendHierarchyComponentSummary(report, go);
        ShowReport(report);
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/このオブジェクト配下の参照Asset一覧", false, 24)]
    public static void ContextShowReferencedAssets()
    {
        var report = new DebugReportBuilder("Referenced Assets In Selected Hierarchy");
        if (!TryGetSelectedGameObject(report, out var go))
        {
            ShowReport(report);
            return;
        }

        AppendBasicGameObjectInfo(report, go);
        AppendLikelyAvatarRelatedAssets(report, go);
        ShowReport(report);
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/デバッグメニューを開く", false, 25)]
    public static void ContextOpenDebugReportWindow()
    {
        OpenDebugReportWindow();
    }

    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/選択中オブジェクトの完全レポート", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/選択中Prefabの開発用情報", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/このオブジェクトを含むアバターのパラメーター", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/このオブジェクト配下のコンポーネント集計", true)]
    [MenuItem("GameObject/VRC Avatar Toolkit Plus/デバッグ/このオブジェクト配下の参照Asset一覧", true)]
    private static bool ValidateContextNeedsGameObject()
    {
        return Selection.activeGameObject != null;
    }

    private static bool TryGetSelectedGameObject(DebugReportBuilder report, out GameObject go)
    {
        go = Selection.activeGameObject;
        if (go != null) return true;

        report.Warning("GameObjectを選択してください。");
        return false;
    }

    private static void ShowReport(DebugReportBuilder report)
    {
        string text = report.ToString();
        Debug.Log(text);
        AvatarDebugReportWindow.Open(text, WindowTitle);
    }

    private static void AppendBasicGameObjectInfo(DebugReportBuilder report, GameObject go)
    {
        report.Section($"GameObject: {GetPath(go.transform)}");
        report.Line($"Name: {go.name}");
        report.Line($"Active Self: {go.activeSelf}");
        report.Line($"Active In Hierarchy: {go.activeInHierarchy}");
        report.Line($"Layer: {LayerMask.LayerToName(go.layer)} ({go.layer})");
        report.Line($"Tag: {go.tag}");
        report.Line($"Transform Local Position: {go.transform.localPosition}");
        report.Line($"Transform Local Rotation: {go.transform.localEulerAngles}");
        report.Line($"Transform Local Scale: {go.transform.localScale}");
        report.Blank();
    }

    private static void AppendSelectedComponentTypes(DebugReportBuilder report, GameObject go)
    {
        report.Section($"Selected GameObject Component Types: {go.name}");

        var components = go.GetComponents<Component>();
        foreach (var c in components)
        {
            if (c == null)
            {
                report.Line("Missing Component");
                continue;
            }

            Type t = c.GetType();
            report.Line($"Component Name: {t.Name}");
            report.Line($"Full Name: {t.FullName}");
            report.Line($"Assembly: {t.Assembly.GetName().Name}");
            report.Blank();
        }
    }

    private static void AppendSerializedProperties(DebugReportBuilder report, GameObject go, bool includeValues)
    {
        report.Section($"Serialized Properties: {go.name}");

        var components = go.GetComponents<Component>();
        foreach (var c in components)
        {
            if (c == null)
            {
                report.Line("--- Missing Component ---");
                continue;
            }

            report.Subsection($"Component: {c.GetType().FullName}");

            try
            {
                SerializedObject so = new SerializedObject(c);
                SerializedProperty prop = so.GetIterator();

                bool enterChildren = true;
                while (prop.NextVisible(enterChildren))
                {
                    string value = includeValues ? $" / Value: {PropertyValueToString(prop)}" : string.Empty;
                    report.Line($"{prop.propertyPath} / {prop.propertyType}{value}");
                    enterChildren = false;
                }
            }
            catch (Exception ex)
            {
                report.Warning($"SerializedProperty取得中に例外: {ex.GetType().Name}: {ex.Message}");
            }

            report.Blank();
        }
    }

    private static void AppendFieldInfo(DebugReportBuilder report, GameObject go)
    {
        report.Section($"Field Info: {go.name}");

        var components = go.GetComponents<Component>();
        foreach (var c in components)
        {
            if (c == null)
            {
                report.Line("--- Missing Component ---");
                continue;
            }

            Type type = c.GetType();
            report.Subsection($"Component: {type.FullName}");

            FieldInfo[] fields = type.GetFields(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance
            );

            foreach (var f in fields)
            {
                string valueText = "";
                try
                {
                    object value = f.GetValue(c);
                    valueText = $" / Value: {ObjectValueToString(value)}";
                }
                catch
                {
                    valueText = " / Value: <unreadable>";
                }

                report.Line($"Field: {f.Name} / Type: {f.FieldType.FullName}{valueText}");

                if (f.FieldType.IsEnum)
                {
                    string[] enumNames = Enum.GetNames(f.FieldType);
                    report.Line($"Enum Values: {string.Join(", ", enumNames)}");
                }
            }

            report.Blank();
        }
    }

    private static void AppendAvatarDescriptorSummary(DebugReportBuilder report, GameObject avatarRoot)
    {
        report.Section($"Avatar: {GetPath(avatarRoot.transform)}");
        report.Line($"Avatar Root Name: {avatarRoot.name}");
        report.Line($"Scene Path: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
        report.Blank();
    }

    private static void AppendExpressionParametersFromDescriptor(DebugReportBuilder report, Component descriptor)
    {
        report.Subsection("Expression Parameters Asset");

        UnityEngine.Object parametersAsset = GetObjectReferenceFromSerializedProperty(descriptor, "expressionParameters");
        if (parametersAsset == null)
        {
            report.Warning("expressionParameters が未設定です。");
            report.Blank();
            return;
        }

        report.Line($"Asset Name: {parametersAsset.name}");
        report.Line($"Asset Path: {AssetDatabase.GetAssetPath(parametersAsset)}");

        SerializedObject so = new SerializedObject(parametersAsset);
        SerializedProperty parameters = so.FindProperty("parameters");
        if (parameters == null || !parameters.isArray)
        {
            report.Warning("parameters 配列が見つかりませんでした。SDKの内部構造が変わっている可能性があります。");
            report.Blank();
            return;
        }

        report.Line($"Parameter Count: {parameters.arraySize}");

        for (int i = 0; i < parameters.arraySize; i++)
        {
            SerializedProperty element = parameters.GetArrayElementAtIndex(i);
            string name = FindRelativeValue(element, "name");
            string valueType = FindRelativeValue(element, "valueType");
            string defaultValue = FindRelativeValue(element, "defaultValue");
            string saved = FindRelativeValue(element, "saved");
            string networkSynced = FindRelativeValue(element, "networkSynced");

            report.Line($"[{i}] name={name}, type={valueType}, default={defaultValue}, saved={saved}, networkSynced={networkSynced}");
        }

        report.Blank();
    }

    private static void AppendAnimatorControllerParametersFromDescriptor(DebugReportBuilder report, Component descriptor)
    {
        report.Subsection("Playable Layer Animator Parameters");

        SerializedObject so = new SerializedObject(descriptor);
        AppendAnimatorControllerParametersFromLayerArray(report, so.FindProperty("baseAnimationLayers"), "Base Animation Layers");
        AppendAnimatorControllerParametersFromLayerArray(report, so.FindProperty("specialAnimationLayers"), "Special Animation Layers");
        report.Blank();
    }

    private static void AppendAnimatorControllerParametersFromLayerArray(DebugReportBuilder report, SerializedProperty layers, string label)
    {
        report.Line($"--- {label} ---");

        if (layers == null || !layers.isArray)
        {
            report.Warning($"{label} が見つかりませんでした。");
            return;
        }

        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            string layerType = FindRelativeValue(layer, "type");
            SerializedProperty controllerProp = layer.FindPropertyRelative("animatorController");
            var controller = controllerProp != null ? controllerProp.objectReferenceValue as RuntimeAnimatorController : null;

            report.Line($"[{i}] LayerType={layerType}, Controller={(controller ? controller.name : "<null>")}, Path={(controller ? AssetDatabase.GetAssetPath(controller) : "<null>")}");

            if (controller == null) continue;

            var animatorController = controller as AnimatorController;
            if (animatorController == null)
            {
                report.Line("    <AnimatorControllerではないためparametersを取得できません>");
                continue;
            }

            foreach (var p in animatorController.parameters)
            {
                report.Line($"    Param: {p.name} / Type: {p.type} / DefaultBool: {p.defaultBool} / DefaultFloat: {p.defaultFloat} / DefaultInt: {p.defaultInt}");
            }
        }
    }

    private static void AppendPrefabInfo(DebugReportBuilder report, GameObject go)
    {
        report.Section("Prefab / Override Info");

        GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
        if (root == null)
        {
            report.Line("Prefab Instance: No");
            report.Blank();
            return;
        }

        report.Line($"Nearest Prefab Instance Root: {GetPath(root.transform)}");
        report.Line($"Prefab Asset Path: {PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)}");
        report.Line($"Prefab Instance Status: {PrefabUtility.GetPrefabInstanceStatus(root)}");
        report.Line($"Prefab Asset Type: {PrefabUtility.GetPrefabAssetType(root)}");
        report.Blank();

        report.Subsection("Property Modifications");
        var modifications = PrefabUtility.GetPropertyModifications(root);
        if (modifications == null || modifications.Length == 0)
        {
            report.Line("No property modifications.");
        }
        else
        {
            foreach (var m in modifications)
            {
                string targetName = m.target != null ? m.target.name : "<null>";
                string targetType = m.target != null ? m.target.GetType().FullName : "<null>";
                string objectRef = m.objectReference != null ? $"{m.objectReference.name} ({AssetDatabase.GetAssetPath(m.objectReference)})" : "<null>";
                report.Line($"Target={targetName} / Type={targetType} / Property={m.propertyPath} / Value={m.value} / ObjectReference={objectRef}");
            }
        }

        AppendPrefabAddedRemovedInfoByReflection(report, root);
        report.Blank();
    }

    private static void AppendPrefabAddedRemovedInfoByReflection(DebugReportBuilder report, GameObject root)
    {
        report.Subsection("Added / Removed Prefab Objects and Components");

        TryAppendPrefabUtilityReflectionList(report, "GetAddedGameObjects", root);
        TryAppendPrefabUtilityReflectionList(report, "GetAddedComponents", root);
        TryAppendPrefabUtilityReflectionList(report, "GetRemovedComponents", root);
    }

    private static void TryAppendPrefabUtilityReflectionList(DebugReportBuilder report, string methodName, GameObject root)
    {
        MethodInfo method = typeof(PrefabUtility).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
        if (method == null)
        {
            report.Line($"{methodName}: <このUnityバージョンではAPIが見つかりません>");
            return;
        }

        try
        {
            object result = method.Invoke(null, new object[] { root });
            var enumerable = result as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                report.Line($"{methodName}: <no enumerable result>");
                return;
            }

            int count = 0;
            foreach (var item in enumerable)
            {
                count++;
                report.Line($"{methodName}[{count - 1}]: {ObjectValueToString(item)}");

                if (item == null) continue;
                foreach (var field in item.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object value = field.GetValue(item);
                    report.Line($"    {field.Name}: {ObjectValueToString(value)}");
                }

                foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    object value = null;
                    try { value = prop.GetValue(item, null); }
                    catch { continue; }
                    report.Line($"    {prop.Name}: {ObjectValueToString(value)}");
                }
            }

            if (count == 0) report.Line($"{methodName}: none");
        }
        catch (Exception ex)
        {
            report.Warning($"{methodName} 取得中に例外: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AppendHierarchyComponentSummary(DebugReportBuilder report, GameObject root)
    {
        report.Section("Hierarchy Component Summary");

        var counts = new SortedDictionary<string, int>();
        var missingPaths = new List<string>();
        var transforms = root.GetComponentsInChildren<Transform>(true);

        foreach (var t in transforms)
        {
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null)
                {
                    missingPaths.Add(GetPath(t));
                    continue;
                }

                string key = c.GetType().FullName;
                counts[key] = counts.ContainsKey(key) ? counts[key] + 1 : 1;
            }
        }

        report.Line($"GameObject Count: {transforms.Length}");
        report.Line($"Component Type Count: {counts.Count}");
        foreach (var pair in counts)
        {
            report.Line($"{pair.Key}: {pair.Value}");
        }

        if (missingPaths.Count > 0)
        {
            report.Blank();
            report.Warning($"Missing Component Count: {missingPaths.Count}");
            foreach (var path in missingPaths)
            {
                report.Line($"Missing at: {path}");
            }
        }

        report.Blank();
    }

    private static void AppendLikelyAvatarRelatedAssets(DebugReportBuilder report, GameObject root)
    {
        report.Section("Likely Avatar Related Assets / References");

        var objects = new SortedSet<string>();
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;

            SerializedObject so;
            try { so = new SerializedObject(component); }
            catch { continue; }

            var prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                {
                    var obj = prop.objectReferenceValue;
                    string path = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(path))
                    {
                        objects.Add($"{obj.GetType().Name}: {obj.name} / {path} / ReferencedBy={GetPath(component.transform)} / Property={prop.propertyPath}");
                    }
                }
                enterChildren = false;
            }
        }

        if (objects.Count == 0)
        {
            report.Line("No project asset references found from serialized component properties.");
            report.Blank();
            return;
        }

        foreach (var line in objects)
        {
            report.Line(line);
        }

        report.Blank();
    }

    private static Component FindAvatarDescriptor(GameObject go)
    {
        var current = go.transform;
        while (current != null)
        {
            var descriptor = GetComponentByFullName(current.gameObject, AvatarDescriptorFullName);
            if (descriptor != null) return descriptor;
            current = current.parent;
        }

        foreach (var component in go.GetComponentsInChildren<Component>(true))
        {
            if (component != null && component.GetType().FullName == AvatarDescriptorFullName)
                return component;
        }

        return null;
    }

    private static List<Component> FindAllAvatarDescriptorsInScene()
    {
        var result = new List<Component>();
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go.scene.IsValid()) continue;
            var descriptor = GetComponentByFullName(go, AvatarDescriptorFullName);
            if (descriptor != null) result.Add(descriptor);
        }
        return result;
    }

    private static Component GetComponentByFullName(GameObject go, string fullName)
    {
        foreach (var c in go.GetComponents<Component>())
        {
            if (c != null && c.GetType().FullName == fullName) return c;
        }
        return null;
    }

    private static UnityEngine.Object GetObjectReferenceFromSerializedProperty(UnityEngine.Object target, string propertyName)
    {
        if (target == null) return null;
        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null ? prop.objectReferenceValue : null;
    }

    private static string FindRelativeValue(SerializedProperty root, string relativePath)
    {
        if (root == null) return "<null>";
        SerializedProperty prop = root.FindPropertyRelative(relativePath);
        return prop != null ? PropertyValueToString(prop) : "<not found>";
    }

    private static string PropertyValueToString(SerializedProperty prop)
    {
        if (prop == null) return "<null>";

        switch (prop.propertyType)
        {
            case SerializedPropertyType.Integer:
                return prop.intValue.ToString();
            case SerializedPropertyType.Boolean:
                return prop.boolValue.ToString();
            case SerializedPropertyType.Float:
                return prop.floatValue.ToString();
            case SerializedPropertyType.String:
                return prop.stringValue;
            case SerializedPropertyType.Color:
                return prop.colorValue.ToString();
            case SerializedPropertyType.ObjectReference:
                if (prop.objectReferenceValue == null) return "<null>";
                return $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name}) Path={AssetDatabase.GetAssetPath(prop.objectReferenceValue)}";
            case SerializedPropertyType.LayerMask:
                return prop.intValue.ToString();
            case SerializedPropertyType.Enum:
                return prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString();
            case SerializedPropertyType.Vector2:
                return prop.vector2Value.ToString();
            case SerializedPropertyType.Vector3:
                return prop.vector3Value.ToString();
            case SerializedPropertyType.Vector4:
                return prop.vector4Value.ToString();
            case SerializedPropertyType.Rect:
                return prop.rectValue.ToString();
            case SerializedPropertyType.ArraySize:
                return prop.intValue.ToString();
            case SerializedPropertyType.Character:
                return prop.intValue.ToString();
            case SerializedPropertyType.AnimationCurve:
                return prop.animationCurveValue != null ? prop.animationCurveValue.ToString() : "<null>";
            case SerializedPropertyType.Bounds:
                return prop.boundsValue.ToString();
            case SerializedPropertyType.Quaternion:
                return prop.quaternionValue.eulerAngles.ToString();
            case SerializedPropertyType.ExposedReference:
                return prop.exposedReferenceValue != null ? prop.exposedReferenceValue.name : "<null>";
            case SerializedPropertyType.FixedBufferSize:
                return prop.intValue.ToString();
            case SerializedPropertyType.Vector2Int:
                return prop.vector2IntValue.ToString();
            case SerializedPropertyType.Vector3Int:
                return prop.vector3IntValue.ToString();
            case SerializedPropertyType.RectInt:
                return prop.rectIntValue.ToString();
            case SerializedPropertyType.BoundsInt:
                return prop.boundsIntValue.ToString();
            case SerializedPropertyType.ManagedReference:
                return prop.managedReferenceFullTypename;
            case SerializedPropertyType.Generic:
                return prop.isArray ? $"Array(size={prop.arraySize})" : "<generic>";
            default:
                return "<unsupported>";
        }
    }

    private static string ObjectValueToString(object value)
    {
        if (value == null) return "<null>";

        if (value is UnityEngine.Object unityObject)
        {
            string path = AssetDatabase.GetAssetPath(unityObject);
            return $"{unityObject.name} ({unityObject.GetType().FullName})" + (string.IsNullOrEmpty(path) ? "" : $" Path={path}");
        }

        if (value is Transform transform) return GetPath(transform);
        if (value is System.Collections.IEnumerable enumerable && !(value is string))
        {
            var items = new List<string>();
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count >= 8)
                {
                    items.Add("...");
                    break;
                }
                items.Add(ObjectValueToString(item));
                count++;
            }
            return "[" + string.Join(", ", items) + "]";
        }

        return value.ToString();
    }

    private static string GetPath(Transform t)
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

    private sealed class DebugReportBuilder
    {
        private readonly StringBuilder sb = new StringBuilder();

        public DebugReportBuilder(string title)
        {
            Line($"# {title}");
            Line($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"Unity: {Application.unityVersion}");
            Line($"Project: {Application.dataPath}");
            Blank();
        }

        public void Section(string text)
        {
            Separator();
            Line($"## {text}");
        }

        public void Subsection(string text)
        {
            Line($"-- {text} --");
        }

        public void Line(string text)
        {
            sb.AppendLine(text ?? string.Empty);
        }

        public void Warning(string text)
        {
            sb.AppendLine($"[Warning] {text}");
            Debug.LogWarning(text);
        }

        public void Blank()
        {
            sb.AppendLine();
        }

        public void Separator()
        {
            sb.AppendLine("============================================================");
        }

        public override string ToString()
        {
            return sb.ToString();
        }
    }
}

public sealed class AvatarDebugReportWindow : EditorWindow
{
    private static string reportText = "";
    private Vector2 scroll;
    private GUIStyle textAreaStyle;

    public static void Open(string text, string title)
    {
        reportText = text ?? string.Empty;
        var window = GetWindow<AvatarDebugReportWindow>(title);
        window.minSize = new Vector2(720, 420);
        window.Show();
        window.Repaint();
    }

    private void OnGUI()
    {
        if (textAreaStyle == null)
        {
            textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false,
                richText = false,
                font = EditorStyles.standardFont
            };
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("全コピー", EditorStyles.toolbarButton, GUILayout.Width(90)))
        {
            EditorGUIUtility.systemCopyBuffer = reportText ?? string.Empty;
            Debug.Log("デバッグレポートをクリップボードへコピーしました。");
        }

        if (GUILayout.Button("クリア", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            reportText = string.Empty;
            GUI.FocusControl(null);
        }

        if (GUILayout.Button("txt保存", EditorStyles.toolbarButton, GUILayout.Width(90)))
        {
            SaveTextFile();
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Ctrl+A / Ctrl+C でもコピーできます", EditorStyles.miniLabel, GUILayout.Width(210));
        EditorGUILayout.EndHorizontal();

        DrawDebugLauncher();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        reportText = EditorGUILayout.TextArea(reportText ?? string.Empty, textAreaStyle, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private static void DrawDebugLauncher()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("デバッグメニュー", EditorStyles.boldLabel);

        GameObject selected = Selection.activeGameObject;
        string selectedName = selected != null ? selected.name : "未選択";
        EditorGUILayout.LabelField($"現在の選択: {selectedName}", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = selected != null;
        if (GUILayout.Button("完全レポート"))
        {
            AvatarComponentDebugTools.ShowFullSelectedDebugReport();
        }
        if (GUILayout.Button("アバターパラメーター"))
        {
            AvatarComponentDebugTools.ShowSelectedAvatarParameters();
        }
        if (GUILayout.Button("Prefab開発情報"))
        {
            AvatarComponentDebugTools.ShowSelectedPrefabDevelopmentInfo();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = selected != null;
        if (GUILayout.Button("コンポーネント型"))
        {
            AvatarComponentDebugTools.PrintSelectedComponentTypes();
        }
        if (GUILayout.Button("SerializedProperty"))
        {
            AvatarComponentDebugTools.PrintSelectedSerializedProperties();
        }
        if (GUILayout.Button("Field情報"))
        {
            AvatarComponentDebugTools.PrintSelectedFieldInfo();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scene内すべてのアバターパラメーター"))
        {
            AvatarComponentDebugTools.ShowAllAvatarParametersInScene();
        }
        GUI.enabled = selected != null;
        if (GUILayout.Button("配下コンポーネント集計"))
        {
            AvatarComponentDebugTools.ContextShowHierarchyComponentSummary();
        }
        if (GUILayout.Button("配下参照Asset一覧"))
        {
            AvatarComponentDebugTools.ContextShowReferencedAssets();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("アバターのルートを選んでいても、アバター内のPrefabや子オブジェクトを選んでいても使えます。パラメーター調査は親方向にVRCAvatarDescriptorを探します。", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private static void SaveTextFile()
    {
        string path = EditorUtility.SaveFilePanel(
            "デバッグレポートを保存",
            Application.dataPath,
            "AvatarDebugReport.txt",
            "txt"
        );

        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllText(path, reportText ?? string.Empty, Encoding.UTF8);
        AssetDatabase.Refresh();
        Debug.Log($"デバッグレポートを保存しました: {path}");
    }
}

}
