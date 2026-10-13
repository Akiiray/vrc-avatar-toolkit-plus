using System;
using System.Linq;
using UnityEditor;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal static class AvatarPreflightApplier
    {
        public static string Apply(AvatarPreflightPlan preview)
        {
            if (ToolkitEditorUtility.IsPlayModeOrChanging(false))
                throw new InvalidOperationException("Play中、コンパイル中、Asset更新中は適用できません。");
            var fresh = AvatarPreflightScanner.Scan(preview.Request);
            if (!string.Equals(preview.Fingerprint, fresh.Fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("対象・参照・Importer設定がプレビュー後に変化しました。再解析してください。");

            var report = new ToolkitReportBuilder("Avatar Preflight Fixer 実行結果（SDK 3.10.5基準）");
            int success = 0, failed = 0, unresolved = 0, skipped = 0;
            var candidates = fresh.Assets.Values.Where(a => a.HasChanges).ToArray();
            int completed = 0;
            try
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    var item = candidates[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Avatar Preflight Fixer", item.Path,
                        (float)i / candidates.Length))
                    {
                        report.Warning("キャンセルしました。成功済みの変更は維持します。未処理: " + (candidates.Length - i), false);
                        break;
                    }
                    if (ToolkitEditorUtility.IsPlayModeOrChanging(false))
                    {
                        report.Error("Editorの状態が変化したため全体停止しました。未処理: " + (candidates.Length - i), false);
                        break;
                    }
                    completed++;
                    if (!string.IsNullOrEmpty(item.BlockReason))
                    {
                        skipped++;
                        report.Warning("[スキップ] " + item.Name + " / " + item.Path + " / " + item.BlockReason, false);
                        continue;
                    }
                    try
                    {
                        string writable = AvatarPreflightScanner.WritableReason(item.Path);
                        if (writable != null) throw new InvalidOperationException(writable);
                        var importer = AssetImporter.GetAtPath(item.Path);
                        if (importer == null || AssetDatabase.AssetPathToGUID(item.Path) != item.Guid ||
                            EditorJsonUtility.ToJson(importer) != item.SettingsJson)
                            throw new InvalidOperationException("適用直前にImporter設定が変化しました。このアセットは再解析が必要です。");

                        bool changed = false;
                        bool blendApplied = false;
                        if (importer is ModelImporter model)
                        {
                            if (item.ChangeBlendShapes)
                            {
                                bool legacy = fresh.Request.BlendShapeMode == PreflightBlendShapeMode.SdkLegacy;
                                if (BlendShapeNormalsCompatibility.TryWriteAndVerify(model, legacy, out string reason))
                                {
                                    var expected = ExpectedNormals(fresh.Request.BlendShapeMode);
                                    if (model.importBlendShapeNormals != expected) model.importBlendShapeNormals = expected;
                                    blendApplied = true;
                                    changed = true;
                                }
                                else
                                {
                                    report.Warning("[BlendShapeスキップ] " + item.Name + " / " + item.Path + " / " + reason, false);
                                    // 読書き失敗で不明なLegacy設定を他項目と一緒に保存しない。
                                    if (!BlendShapeNormalsCompatibility.TryRead(model, out bool actual, out _) || actual != item.Legacy)
                                    {
                                        EditorJsonUtility.FromJsonOverwrite(item.SettingsJson, model);
                                        if (EditorJsonUtility.ToJson(model) != item.SettingsJson)
                                            throw new InvalidOperationException("Legacy操作後の元設定を復元・確認できません。再インポートせず停止しました。");
                                    }
                                }
                            }
                            if (item.ChangeReadWrite) { model.isReadable = true; changed = true; }
                        }
                        else if (importer is TextureImporter texture && item.ChangeStreaming)
                        {
                            // Priority、Mip Maps、圧縮、最大サイズ、Platform設定には触れない。
                            texture.streamingMipmaps = true;
                            changed = true;
                        }
                        if (changed)
                        {
                            EditorUtility.SetDirty(importer);
                            importer.SaveAndReimport(); // 同じImporterのA・Bはここで1回だけ。
                        }
                        string verification = Verify(item, fresh.Request.BlendShapeMode, blendApplied);
                        if (verification == null)
                        {
                            success++;
                            report.Info("[成功] " + item.Name + " / " + item.Path + "\n" + item.Describe(fresh.Request.BlendShapeMode));
                        }
                        else
                        {
                            unresolved++;
                            report.Warning("[未解決] " + item.Name + " / " + item.Path + " / " + verification, false);
                        }
                    }
                    catch (Exception e)
                    {
                        failed++;
                        report.Error("[失敗] " + item.Name + " / " + item.Path + " / " + e.GetBaseException().Message, false);
                        // SaveAndReimport例外でも設定だけ保存された可能性があるため再検査する。
                        try
                        {
                            string state = Verify(item, fresh.Request.BlendShapeMode, true);
                            report.Info("失敗後の再検査: " + (state ?? "対象設定は期待値。再インポート成功は未確認。"));
                        }
                        catch (Exception checkError) { report.Warning("失敗後の再検査不可: " + checkError.GetBaseException().Message, false); }
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            foreach (var item in fresh.Assets.Values.Where(a => !string.IsNullOrEmpty(a.BlendShapeReason)))
                report.Warning("[BlendShape判定不能] " + item.Name + " / " + item.Path + " / " + item.BlendShapeReason, false);
            foreach (string diagnostic in fresh.Diagnostics) report.Warning(diagnostic, false);
            report.Section("集計");
            report.KeyValue("成功（Importer単位）", success);
            report.KeyValue("失敗", failed);
            report.KeyValue("未解決", unresolved);
            report.KeyValue("スキップ", skipped);
            report.KeyValue("未処理", candidates.Length - completed);
            report.Line("成功済みの変更は維持します。失敗時に部分変更が残る場合があります。再解析で確認してください。");
            return report.ToString();
        }

        private static ModelImporterNormals ExpectedNormals(PreflightBlendShapeMode mode)
        {
            return mode == PreflightBlendShapeMode.SdkLegacy ? ModelImporterNormals.Calculate :
                mode == PreflightBlendShapeMode.Import ? ModelImporterNormals.Import : ModelImporterNormals.None;
        }

        private static string Verify(PreflightAsset item, PreflightBlendShapeMode mode, bool blendApplied)
        {
            var importer = AssetImporter.GetAtPath(item.Path);
            if (importer == null) return "変更後のImporterなし。";
            if (item.ChangeReadWrite || item.ChangeBlendShapes)
            {
                if (!(importer is ModelImporter model)) return "変更後のModelImporterなし。";
                if (item.ChangeReadWrite && !model.isReadable) return "Read/Writeが無効のままです。";
                if (item.ChangeBlendShapes)
                {
                    if (!blendApplied) return "BlendShape修正をスキップしました。";
                    if (!BlendShapeNormalsCompatibility.TryRead(model, out bool legacy, out string reason)) return reason;
                    if (model.importBlendShapeNormals != ExpectedNormals(mode) || legacy != (mode == PreflightBlendShapeMode.SdkLegacy))
                        return "BlendShape Normals／Legacyが期待値と一致しません。";
                }
            }
            if (item.ChangeStreaming)
            {
                if (!(importer is TextureImporter texture)) return "変更後のTextureImporterなし。";
                if (!texture.streamingMipmaps || texture.mipmapEnabled != item.Mipmaps || texture.streamingMipmapsPriority != item.Priority)
                    return "Streaming／Mip Maps／Priorityが期待値と一致しません。";
                // 他のImporter設定が変化していないことも比較する。
                if (AvatarPreflightScanner.PreservedTextureSettings(texture) != item.PreservedTextureSettings)
                    return "Streaming以外のImporter設定が変化しました。";
            }
            return null;
        }
    }
}
