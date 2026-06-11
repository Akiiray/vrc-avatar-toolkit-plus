using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    public enum ToolkitTargetMode
    {
        SelectedHierarchy,
        SelectedPrefabAssets,
        SelectedFolderPrefabs,
        AllProjectPrefabs
    }

    public sealed class ToolkitScanRoot
    {
        public bool IsPrefabAsset;
        public GameObject RootObject;
        public string PrefabAssetPath;
        public string Label;

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Label)) return Label;
            if (!string.IsNullOrEmpty(PrefabAssetPath)) return PrefabAssetPath;
            return RootObject != null ? ToolkitPathUtility.GetHierarchyPath(RootObject) : "<null>";
        }
    }

    public static class ToolkitSelectionUtility
    {
        public static GameObject GetActiveGameObject()
        {
            return Selection.activeGameObject;
        }

        public static GameObject[] GetSelectedHierarchyObjects()
        {
            return Selection.gameObjects
                .Where(go => go != null && !EditorUtility.IsPersistent(go))
                .Distinct()
                .ToArray();
        }

        public static string[] GetSelectedPrefabAssetPaths()
        {
            var paths = new List<string>();
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (IsPrefabAssetPath(path)) paths.Add(path);
            }
            return paths.Distinct().ToArray();
        }

        public static string GetSelectedFolderPath()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path)) return path;
            }
            return null;
        }

        public static string[] FindPrefabPathsInFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return Array.Empty<string>();

            return AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPrefabAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string[] FindAllProjectPrefabPaths()
        {
            return AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPrefabAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool IsPrefabAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return false;
            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(GameObject);
        }

        public static GameObject[] NormalizeHierarchyRoots(
            IEnumerable<GameObject> selected,
            bool preferPrefabInstanceRoot = true,
            bool requireAvatarDescriptor = false)
        {
            if (selected == null) return Array.Empty<GameObject>();

            var result = new List<GameObject>();
            foreach (var go in selected)
            {
                if (go == null || EditorUtility.IsPersistent(go)) continue;

                GameObject root = go;
                if (preferPrefabInstanceRoot)
                {
                    var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                    if (prefabRoot != null) root = prefabRoot;
                }

                if (requireAvatarDescriptor)
                {
                    var avatarRoot = FindAvatarRoot(root);
                    if (avatarRoot != null) root = avatarRoot;
                    else continue;
                }

                if (!result.Contains(root)) result.Add(root);
            }

            return result.ToArray();
        }

        public static GameObject FindAvatarRoot(GameObject go)
        {
            if (go == null) return null;

            Type descriptorType = Type.GetType(ToolkitConstants.AvatarDescriptorFullName + ", VRCSDK3A")
                ?? Type.GetType(ToolkitConstants.AvatarDescriptorFullName + ", VRC.SDK3A")
                ?? FindTypeByFullName(ToolkitConstants.AvatarDescriptorFullName);

            if (descriptorType == null) return null;

            var current = go.transform;
            while (current != null)
            {
                if (current.GetComponent(descriptorType) != null) return current.gameObject;
                current = current.parent;
            }

            var child = go.GetComponentInChildren(descriptorType, true);
            return child != null ? ((Component)child).gameObject : null;
        }

        public static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        public static List<ToolkitScanRoot> BuildScanRootsFromCurrentSelection(
            ToolkitTargetMode mode,
            string folderPath = null,
            bool requireAvatarDescriptor = false,
            bool preferPrefabInstanceRoot = true)
        {
            var roots = new List<ToolkitScanRoot>();

            if (mode == ToolkitTargetMode.SelectedHierarchy)
            {
                foreach (var go in NormalizeHierarchyRoots(GetSelectedHierarchyObjects(), preferPrefabInstanceRoot, requireAvatarDescriptor))
                {
                    roots.Add(new ToolkitScanRoot
                    {
                        IsPrefabAsset = false,
                        RootObject = go,
                        Label = ToolkitPathUtility.GetHierarchyPath(go)
                    });
                }
            }
            else if (mode == ToolkitTargetMode.SelectedPrefabAssets)
            {
                foreach (string path in GetSelectedPrefabAssetPaths())
                {
                    roots.Add(new ToolkitScanRoot
                    {
                        IsPrefabAsset = true,
                        PrefabAssetPath = path,
                        RootObject = AssetDatabase.LoadAssetAtPath<GameObject>(path),
                        Label = path
                    });
                }
            }
            else if (mode == ToolkitTargetMode.SelectedFolderPrefabs)
            {
                string folder = string.IsNullOrEmpty(folderPath) ? GetSelectedFolderPath() : folderPath;
                foreach (string path in FindPrefabPathsInFolder(folder))
                {
                    roots.Add(new ToolkitScanRoot
                    {
                        IsPrefabAsset = true,
                        PrefabAssetPath = path,
                        RootObject = AssetDatabase.LoadAssetAtPath<GameObject>(path),
                        Label = path
                    });
                }
            }
            else if (mode == ToolkitTargetMode.AllProjectPrefabs)
            {
                foreach (string path in FindAllProjectPrefabPaths())
                {
                    roots.Add(new ToolkitScanRoot
                    {
                        IsPrefabAsset = true,
                        PrefabAssetPath = path,
                        RootObject = AssetDatabase.LoadAssetAtPath<GameObject>(path),
                        Label = path
                    });
                }
            }

            return roots;
        }
    }
}
