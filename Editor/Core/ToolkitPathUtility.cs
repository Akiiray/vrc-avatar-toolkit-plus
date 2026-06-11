using UnityEngine;
using System.Collections.Generic;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    public static class ToolkitPathUtility
    {
        public static string GetHierarchyPath(GameObject go)
        {
            return go == null ? "<null>" : GetHierarchyPath(go.transform);
        }

        public static string GetHierarchyPath(Component component)
        {
            return component == null ? "<null>" : GetHierarchyPath(component.transform);
        }

        public static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "<null>";

            var stack = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack.ToArray());
        }

        public static string GetRelativePath(Transform root, Transform target, bool includeRootName = true)
        {
            if (target == null) return "<null>";
            if (root == null) return GetHierarchyPath(target);
            if (target == root) return includeRootName ? root.name : string.Empty;

            var stack = new Stack<string>();
            var current = target;

            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return GetHierarchyPath(target);
            }

            if (includeRootName) stack.Push(root.name);
            return string.Join("/", stack.ToArray());
        }

        public static Transform FindChildByRelativePath(Transform root, string relativePath)
        {
            if (root == null || string.IsNullOrEmpty(relativePath)) return null;
            if (relativePath == "." || relativePath == root.name) return root;

            string path = relativePath;
            if (path.StartsWith(root.name + "/"))
            {
                path = path.Substring(root.name.Length + 1);
            }

            if (string.IsNullOrEmpty(path)) return root;
            return root.Find(path);
        }
    }
}
