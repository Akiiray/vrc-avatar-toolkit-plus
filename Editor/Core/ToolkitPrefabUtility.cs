using UnityEditor;
using UnityEngine;
using System;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    /// <summary>
    /// Prefab AssetをLoadPrefabContentsで安全に開くためのScope。
    /// Saveしなければ読み取り用途として使えます。
    /// </summary>
    public sealed class ToolkitPrefabEditScope : IDisposable
    {
        private bool _disposed;
        private bool _saved;

        public string Path { get; private set; }
        public GameObject Root { get; private set; }

        public ToolkitPrefabEditScope(string prefabAssetPath)
        {
            if (string.IsNullOrEmpty(prefabAssetPath))
                throw new ArgumentException("Prefab path is empty.", nameof(prefabAssetPath));

            if (!ToolkitSelectionUtility.IsPrefabAssetPath(prefabAssetPath))
                throw new ArgumentException("指定されたパスはPrefab Assetではありません: " + prefabAssetPath, nameof(prefabAssetPath));

            Path = prefabAssetPath;
            Root = PrefabUtility.LoadPrefabContents(prefabAssetPath);
            if (Root == null)
                throw new InvalidOperationException("Prefabを読み込めませんでした: " + prefabAssetPath);
        }

        public void Save()
        {
            ThrowIfDisposed();
            PrefabUtility.SaveAsPrefabAsset(Root, Path);
            _saved = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Root != null)
            {
                PrefabUtility.UnloadPrefabContents(Root);
                Root = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ToolkitPrefabEditScope));
        }
    }
}
