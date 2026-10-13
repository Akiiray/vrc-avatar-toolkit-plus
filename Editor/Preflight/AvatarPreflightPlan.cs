using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal enum PreflightTargetMode { SelectedHierarchy, AllHierarchy, PrefabFolder }
    internal enum PreflightBlendShapeMode { SdkLegacy, Import, None }

    internal sealed class AvatarPreflightRequest
    {
        public PreflightTargetMode TargetMode;
        public GameObject[] Selection = Array.Empty<GameObject>();
        public string Folder;
        public bool Recursive = true;
        public bool ReadWrite = true;
        public bool BlendShapes = true;
        public bool Streaming = true;
        public PreflightBlendShapeMode BlendShapeMode;
    }

    internal sealed class PreflightAsset
    {
        public string Path;
        public string Name;
        public string Guid;
        public string SettingsJson;
        public string DependencyHash;
        public string PreservedTextureSettings;
        public readonly SortedSet<string> Sources = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedSet<string> Avatars = new SortedSet<string>(StringComparer.Ordinal);
        public bool OutsideFolder;
        public bool MeshReferenced;
        public bool TextureReferenced;
        public bool ChangeReadWrite;
        public bool ChangeBlendShapes;
        public bool ChangeStreaming;
        public bool Readable;
        public ModelImporterNormals Normals;
        public bool Legacy;
        public bool LegacyKnown;
        public bool Mipmaps;
        public bool Streaming;
        public int Priority;
        public string BlockReason;
        public string BlendShapeReason;
        public bool HasChanges => ChangeReadWrite || ChangeBlendShapes || ChangeStreaming;
        public bool CanApply => HasChanges && string.IsNullOrEmpty(BlockReason);

        public string Describe(PreflightBlendShapeMode mode)
        {
            var lines = new List<string>();
            if (ChangeReadWrite) lines.Add("Read/Write: false → true");
            if (ChangeBlendShapes)
                lines.Add("Blend Shape Normals: Calculate / Legacy false → " +
                    (mode == PreflightBlendShapeMode.SdkLegacy ? "Calculate / Legacy true" : mode + " / Legacy false"));
            if (ChangeStreaming)
                lines.Add("Streaming Mip Maps: false → true（Mip Maps: " + Mipmaps + "、Priority: " + Priority + " を維持）");
            if (!HasChanges) lines.Add("変更不要／SDKチェック対象外");
            if (!string.IsNullOrEmpty(BlendShapeReason)) lines.Add("BlendShapeスキップ: " + BlendShapeReason);
            if (!string.IsNullOrEmpty(BlockReason)) lines.Add("適用不可: " + BlockReason);
            return string.Join("\n", lines);
        }

        public string Fingerprint()
        {
            // 完全なImporter設定と参照元を照合し、プレビュー外の変更を適用しない。
            return string.Join("\n", new[] { Path, Guid, SettingsJson, DependencyHash,
                MeshReferenced.ToString(), TextureReferenced.ToString(), Readable.ToString(), Normals.ToString(), LegacyKnown.ToString(), Legacy.ToString(),
                Mipmaps.ToString(), Streaming.ToString(), Priority.ToString(),
                ChangeReadWrite.ToString(), ChangeBlendShapes.ToString(), ChangeStreaming.ToString(),
                BlockReason, BlendShapeReason, string.Join("\n", Sources), string.Join("\n", Avatars) });
        }
    }

    internal sealed class AvatarPreflightPlan
    {
        public AvatarPreflightRequest Request;
        public readonly SortedDictionary<string, PreflightAsset> Assets =
            new SortedDictionary<string, PreflightAsset>(StringComparer.Ordinal);
        public readonly SortedSet<string> Avatars = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedSet<string> Diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedSet<string> References = new SortedSet<string>(StringComparer.Ordinal);
        public int ApplicableCount => Assets.Values.Count(a => a.CanApply);
        public string Fingerprint => string.Join("\n", Avatars) + "\n" +
            string.Join("\n", Diagnostics) + "\n" + string.Join("\n", References) + "\n" +
            string.Join("\n", Assets.Values.Select(a => a.Fingerprint()));
    }
}
