# Plusシリーズ UI詳細設計ガイド

ToolkitをUI / UXの参照実装として、ユーザー体験と設計判断を共通化する規約です。コード共有の指示ではありません。共通思想の正本は[VPM DESIGN_PRINCIPLES](https://github.com/Akiiray/vpm-repository/blob/main/docs/DESIGN_PRINCIPLES.md)です。

## 参照実装の3分類

| 分類 | 例と適用方針 |
| --- | --- |
| Plus共通原則 | 対象の明示、Section、標準HelpBox、結果と詳細の分離、明示走査、ScrollView。各機能へ原則を適用する |
| Toolkit固有仕様 | ギミック導入、全Prefab一括編集、入れ直し、元Importer変更、外部Installer連携。LiteKitへ移植しない |
| Toolkitにも改善余地がある実装 | 文言・確認方法・メニューpriorityのばらつき、Version / Documentation導線、古いPreviewの扱い。現行挙動を完成済み標準としない |

LiteKitのLocalization、Analyzer / Plannerの単一実装、Build clone / NDMFアーキテクチャを維持します。内部実装を機械的にコピーせず、まず設計規約で揃えます。継続的な重複問題が確認されるまでShared Packageを新設しません。

## Window構造

推奨順序は対象選択 → 設定 → Preview / Dry Run → 実行 → 結果 → 詳細です。診断専用WindowにはApplyを追加せず、機能に不要な段階を省きます。Component InspectorではPreviewとBuild時の有効化を説明し、手動Applyを無理に追加しません。

| 階層 | 役割 |
| --- | --- |
| Window title | 製品と機能を識別する |
| Header | Windowの目的と操作の概要 |
| Section | 対象、条件、操作、結果を整理する |
| HelpBox | 現在の状況・制限・対処をInfo / Warning / Errorで示す |
| Summary | 対象、条件、主結果、変更有無、次の操作を短く示す |
| Detail / Foldout | 件数付き一覧や専門設定を必要時に開く |
| Log | 処理・Skip・失敗理由を追跡可能にする。主要結果をログだけへ隠さない |

Unity標準GUIと整合するbox、太字Section Header、余白を使います。長いログは高さを制限したScrollView、Window全体は小さい表示でも主要操作へ到達できる構成を推奨します。

## 状態と結果の寿命

未選択、未実行、正常、情報、警告、エラー、再実行が必要を必要に応じて区別します。色やアイコンだけでなく文言で伝え、Infoを一律Warningにしません。

結果は対象Avatar、実行条件、実行時点に結び付けます。対象、設定、依存構成が変わった場合は古い結果を無効化するか、再実行が必要と明示します。新しい対象名の下に古い成功結果を表示しません。

## 操作と安全

- 重いPrefab / Asset / Component走査をOnGUIで毎回実行しません。明示操作またはキャッシュと無効化条件を使います。
- Avatar ObjectFieldと現在選択から設定する操作を分かりやすく示します。子から親Avatarを解決する場合は解決後の対象を表示します。
- 未選択や実行不能時は理由を示し、変更ボタンを適切に無効化します。
- Preview / Dry Runと変更操作はラベル・配置・説明で区別します。
- 危険操作は対象数、変更内容、共有Assetへの影響、失われる設定、Undo / 復元の可否を示して確認します。
- Hierarchy編集にはUndo、Prefab Asset編集にはPrefabUtilityを使用し、Asset変更対象を明示します。LiteKitではBuild clone優先の安全方針を適用します。
- エラーを黙ってFallbackで隠しません。代替処理の理由、変更範囲、結果をログとUIへ示します。

## 用語

| 用語 | 意味 |
| --- | --- |
| 診断 | 現状を読み取り、問題・推定値・改善候補を示す |
| Preview | 現在の入力から予想結果を表示する。実Build後の構成と一致する保証はない |
| Dry Run | 変更を実行せず、処理予定・対象・差分を確認する試行 |
| 適用 | 示した変更を対象へ実行する。対象と保存範囲を説明する |
| 生成 | 新しいAsset / Object / Reportを作る。出力先と元データ保持を示す |
| Build-time変換 | 通常Buildの途中でcloneや一時生成物を実際に変換する |
| Report | 対象・条件・結果・制限を保存または共有可能な形式で表す |

Build時に変換されるComponentのInspector Previewは、Build変換を実行しないDry Runと同一ではありません。有効化したComponentが通常Buildで作用することを明示します。

## メニュー・言語・Version・資料

- `Tools > 製品名 > 機能`を基本とし、通常Windowと即時変更操作を識別できる名称にします。
- 主要MenuItem priorityは同じ帯を確認します。階層内priorityとTools直下の製品順を混同せず、既存互換エイリアスも考慮します。
- 日本語UIは日本語、English UIは自然な英語にします。固有名詞を無理に翻訳しません。翻訳構造を持つPackageでは既存Localizationを使い、言語別の処理コピーをしません。
- Version表示を追加する場合はpackage metadataを参照し、固定Version文字列を埋め込みません。
- Documentationへの導線は機能の正本へ向けます。Reportには推定の限界、対象と条件、変更有無を含めます。

## 適用レビュー

今回変更する機能に必要な段階・状態だけを選びます。Toolkit固有編集経路を移植していないか、既存Localizationと非破壊設計を維持しているか、重い処理や古い結果を表示していないかを確認します。この文書は推奨規約であり、すべての既存Windowが準拠済みという保証ではありません。
