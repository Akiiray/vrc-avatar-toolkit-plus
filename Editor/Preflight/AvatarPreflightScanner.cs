using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal static class AvatarPreflightScanner
    {
        private static MethodInfo _sdkCollector;

        public static AvatarPreflightPlan Scan(AvatarPreflightRequest request)
        {
            var descriptorType = ToolkitSelectionUtility.FindTypeByFullName(ToolkitConstants.AvatarDescriptorFullName);
            if (descriptorType == null) throw new InvalidOperationException("VRCAvatarDescriptorを取得できません。");
            ResolveSdkCollector(); // 独自のEditorOnly除外へフォールバックしない。
            var plan = new AvatarPreflightPlan { Request = request };
            if (request.TargetMode == PreflightTargetMode.PrefabFolder)
            {
                if (string.IsNullOrEmpty(request.Folder) || !AssetDatabase.IsValidFolder(request.Folder))
                    throw new InvalidOperationException("有効なPrefabフォルダを指定してください。");
                foreach (var path in ToolkitSelectionUtility.FindPrefabPathsInFolder(request.Folder))
                {
                    if (!request.Recursive && !string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        request.Folder.TrimEnd('/'), StringComparison.Ordinal)) continue;
                    try
                    {
                        using (var scope = new ToolkitPrefabEditScope(path))
                        {
                            var descriptors = scope.Root.GetComponentsInChildren(descriptorType, true);
                            if (descriptors.Length == 0) plan.Diagnostics.Add(path + ": VRCAvatarDescriptorなし。");
                            foreach (var descriptor in descriptors)
                                CollectAvatar(plan, descriptor.gameObject, path + " :: " +
                                    ToolkitPathUtility.GetRelativePath(scope.Root.transform, descriptor.transform), path);
                        }
                    }
                    catch (Exception e) { plan.Diagnostics.Add(path + ": Prefab解析失敗: " + e.GetBaseException().Message); }
                }
            }
            else
            {
                var avatars = new HashSet<GameObject>();
                if (request.TargetMode == PreflightTargetMode.AllHierarchy)
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                        foreach (var root in scene.GetRootGameObjects()) AddDescriptors(root, descriptorType, avatars);
                    }
                }
                else
                {
                    foreach (var selected in request.Selection)
                    {
                        if (!IsSceneObject(selected)) continue;
                        Component parent = selected.GetComponentInParent(descriptorType, true);
                        if (parent != null) avatars.Add(parent.gameObject);
                        else AddDescriptors(selected, descriptorType, avatars);
                    }
                }
                foreach (var avatar in avatars.Where(IsSceneObject).OrderBy(a => a.GetInstanceID()))
                    CollectAvatar(plan, avatar, avatar.scene.name + " :: " + ToolkitPathUtility.GetHierarchyPath(avatar) +
                        " [" + avatar.GetInstanceID() + "]", null);
            }
            foreach (var asset in plan.Assets.Values) Inspect(plan, asset);
            return plan;
        }

        private static bool IsSceneObject(GameObject go)
        {
            return go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid() && go.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(go.scene) && PrefabStageUtility.GetPrefabStage(go) == null;
        }

        private static void AddDescriptors(GameObject root, Type type, HashSet<GameObject> result)
        {
            foreach (var descriptor in root.GetComponentsInChildren(type, true))
                if (IsSceneObject(descriptor.gameObject)) result.Add(descriptor.gameObject);
        }

        private static void ResolveSdkCollector()
        {
            if (_sdkCollector != null) return;
            var type = ToolkitSelectionUtility.FindTypeByFullName("VRC.SDKBase.Validation.ValidationUtils");
            if (type != null)
                _sdkCollector = type.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(m =>
                {
                    if (m.Name != "GetComponentsInChildrenExcludingEditorOnly" || !m.IsGenericMethodDefinition ||
                        m.GetGenericArguments().Length != 1) return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 2 && parameters[0].ParameterType == typeof(GameObject) &&
                        parameters[1].ParameterType == typeof(bool);
                });
            if (_sdkCollector == null)
                throw new InvalidOperationException("SDKのEditorOnly除外APIを取得できません。安全な解析を行えないため停止しました。");
        }

        private static IEnumerable<T> Components<T>(GameObject root) where T : Component
        {
            return (IEnumerable<T>)_sdkCollector.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { root, true });
        }

        private static void CollectAvatar(AvatarPreflightPlan plan, GameObject root, string avatar, string prefabPath)
        {
            plan.Avatars.Add(avatar);
            if (prefabPath != null)
                plan.References.Add("Prefab: " + prefabPath + " # " + AssetDatabase.GetAssetDependencyHash(prefabPath));
            foreach (var renderer in Components<SkinnedMeshRenderer>(root))
                AddMesh(plan, renderer.sharedMesh, avatar, Source(root, renderer, "sharedMesh"));
            foreach (var filter in Components<MeshFilter>(root))
                AddMesh(plan, filter.sharedMesh, avatar, Source(root, filter, "sharedMesh"));
            foreach (var renderer in Components<ParticleSystemRenderer>(root))
            {
                var meshes = new Mesh[renderer.meshCount];
                renderer.GetMeshes(meshes);
                for (int i = 0; i < meshes.Length; i++) AddMesh(plan, meshes[i], avatar, Source(root, renderer, "mesh[" + i + "]"));
            }
            foreach (var renderer in Components<Renderer>(root))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    string source = Source(root, renderer, "material[" + slot + "]");
                    plan.References.Add(avatar + " / " + source + " = " + Identity(material));
                    if (material == null) continue;
                    var ids = material.GetTexturePropertyNameIDs();
                    if (ids == null) continue;
                    foreach (int id in ids)
                    {
                        var texture = material.GetTexture(id);
                        string textureSource = source + " / " + material.name + " / property " + id;
                        plan.References.Add(avatar + " / " + textureSource + " = " + Identity(texture));
                        if (texture == null) continue;
                        AddAsset(plan, texture, avatar, textureSource);
                    }
                }
            }
        }

        private static string Source(GameObject root, Component component, string field)
        {
            return ToolkitPathUtility.GetRelativePath(root.transform, component.transform) + " / " +
                component.GetType().Name + " / " + field;
        }

        private static string Identity(UnityEngine.Object obj)
        {
            if (obj == null) return "<null>";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                return obj.name + " @ " + guid + ":" + localId;
            return obj.name + " @ transient:" + obj.GetInstanceID();
        }

        private static void AddMesh(AvatarPreflightPlan plan, Mesh mesh, string avatar, string source)
        {
            plan.References.Add(avatar + " / " + source + " = " + Identity(mesh));
            if (mesh == null) return;
            if (!AssetDatabase.Contains(mesh))
            {
                plan.Diagnostics.Add(avatar + " / " + source + ": AssetではないMesh（SDKチェック対象外）。");
                return;
            }
            AddAsset(plan, mesh, avatar, source);
        }

        private static void AddAsset(AvatarPreflightPlan plan, UnityEngine.Object obj, string avatar, string source)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                plan.Diagnostics.Add(avatar + " / " + source + ": " + obj.name + " はAssetパスなし（SDKチェック対象外）。");
                return;
            }
            if (!plan.Assets.TryGetValue(path, out var asset))
            {
                asset = new PreflightAsset { Path = path, Name = Path.GetFileName(path), Guid = AssetDatabase.AssetPathToGUID(path),
                    OutsideFolder = plan.Request.TargetMode == PreflightTargetMode.PrefabFolder &&
                        !path.StartsWith(plan.Request.Folder.TrimEnd('/') + "/", StringComparison.Ordinal) };
                plan.Assets.Add(path, asset);
            }
            asset.Avatars.Add(avatar);
            asset.MeshReferenced |= obj is Mesh;
            asset.TextureReferenced |= obj is Texture;
            asset.Sources.Add(avatar + " / " + source + " = " + Identity(obj));
        }

        private static void Inspect(AvatarPreflightPlan plan, PreflightAsset asset)
        {
            try
            {
                var importer = AssetImporter.GetAtPath(asset.Path);
                if (importer == null) { asset.BlockReason = "Importerなし（SDKチェック対象外）。"; return; }
                asset.SettingsJson = EditorJsonUtility.ToJson(importer);
                asset.DependencyHash = AssetDatabase.GetAssetDependencyHash(asset.Path).ToString();
                if (importer is ModelImporter model && asset.MeshReferenced)
                {
                    asset.Readable = model.isReadable;
                    asset.Normals = model.importBlendShapeNormals;
                    asset.ChangeReadWrite = plan.Request.ReadWrite && !model.isReadable;
                    if (plan.Request.BlendShapes && asset.Normals == ModelImporterNormals.Calculate)
                    {
                        asset.LegacyKnown = BlendShapeNormalsCompatibility.TryRead(model, out asset.Legacy, out var reason);
                        asset.BlendShapeReason = reason;
                        asset.ChangeBlendShapes = asset.LegacyKnown && !asset.Legacy;
                    }
                }
                else if (importer is TextureImporter texture && asset.TextureReferenced)
                {
                    asset.PreservedTextureSettings = PreservedTextureSettings(texture);
                    asset.Mipmaps = texture.mipmapEnabled;
                    asset.Streaming = texture.streamingMipmaps;
                    asset.Priority = texture.streamingMipmapsPriority;
                    asset.ChangeStreaming = plan.Request.Streaming && asset.Mipmaps && !asset.Streaming;
                }
                else asset.BlockReason = "この参照はSDKのModelImporter／TextureImporter検査の対象外です。";
                if (asset.HasChanges) asset.BlockReason = WritableReason(asset.Path);
            }
            catch (Exception e) { asset.BlockReason = "Importer解析失敗: " + e.GetBaseException().Message; }
        }

        public static string WritableReason(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return "Packages等のAssets外アセットは修正しません。";
            try
            {
                foreach (var target in new[] { path, path + ".meta" })
                {
                    if (!File.Exists(target)) return "ファイルが存在しません: " + target;
                    if ((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0) return "読み取り専用: " + target;
                    if (!AssetDatabase.IsOpenForEdit(target, out string message, StatusQueryOptions.ForceUpdate))
                        return "書き込み不可: " + target + " / " + message;
                }
                return null;
            }
            catch (Exception e) { return "書込可否の確認失敗: " + e.GetBaseException().Message; }
        }

        public static string PreservedTextureSettings(TextureImporter importer)
        {
            // Unity 2022.3 TextureImporterInspectorのSerializedProperty名。
            // 実Importerを一時変更せず、Streamingフラグ以外を事後比較する。
            using (var serialized = new SerializedObject(importer))
            {
                if (serialized.FindProperty("m_StreamingMipmaps") == null)
                    throw new InvalidOperationException("Streaming以外の設定保持を確認するAPIを取得できません。");
                var result = new StringBuilder();
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyPath == "m_StreamingMipmaps") continue;
                    result.Append(property.propertyPath).Append(':')
                        .Append(ToolkitSerializedPropertyUtility.ToStableString(property, deepArray: false)).Append('\n');
                }
                return result.ToString();
            }
        }
    }
}
