# AGENTS.md

このファイルは、このリポジトリで Codex が実装・修正・レビューを行う際の作業方針です。リポジトリ全体に適用します。

## 基本方針

- このリポジトリは、VRChat 向け Unity Editor 拡張ツール `VRC Avatar Toolkit Plus` の開発用です。
- Unity 2022.3 系、VRChat SDK、VCC / ALCOM、VPM パッケージ運用を前提にします。
- 変更は安全性と非破壊性を優先します。
- 既存のアバター、Prefab、Material、Animator、Expression Menu、Modular Avatar 構成を不用意に破壊しないでください。
- DryRun がある処理では、実行前に DryRun で確認できる設計を優先してください。

## 言語方針

- ドキュメント、コメント、レビュー、PR 説明は日本語を基本にします。
- 英語のツール名・固有名詞・API 名・クラス名・メソッド名・名前空間・パッケージ ID・Unity メニュー名は無理に翻訳しません。
- 例:
  - `Avatar Optimizer`
  - `AAO`
  - `LAC`
  - `LightLimitChanger`
  - `Modular Avatar`
  - `VRCAvatarDescriptor`
  - `PrefabUtility`
  - `AssetDatabase`
  - `Tools > VRC Avatar Toolkit Plus`
- 直訳で不自然になる場合は、日本語説明 + 英語名併記にします。
- ユーザー向け UI 文言は日本語を基本にします。

## Markdown 方針

- `README.md`、`CHANGELOG.md`、`Documentation~` 配下などの Markdown は日本語を基本にします。
- 既存の Markdown が英語中心の場合は、日本語中心に書き換えます。
- ただし、以下は無理に翻訳しません。
  - ライセンス本文
  - URL
  - パッケージ ID
  - クラス名
  - メソッド名
  - コードブロック
  - Unity メニュー項目
  - 外部ツールの正式名称
- Markdown の見出し構造、表、リンク、コードブロックは壊さないでください。
- README の内容を大きく変える場合は、既存の構成を尊重して差分を最小限にしてください。
- CHANGELOG は既存形式に合わせます。

## コーディング方針

- Unity 2022.3 の IMGUI / EditorWindow でコンパイルできることを優先します。
- 外部パッケージの型には直接参照せず、必要に応じて Reflection や `SerializedObject` を使います。
- asmdef の依存関係を不用意に増やさないでください。
- `AssetDatabase.FindAssets` や Prefab 走査など重い処理を `OnGUI()` で毎回実行しないでください。
- Project 全体やフォルダ内 Prefab の走査は、明示的なボタン操作やキャッシュ方式を使います。
- Project Prefab 編集時は `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` / `UnloadPrefabContents` を適切に使います。
- 破壊的変更を行う処理には DryRun、ログ、注意表示を用意します。
- Hierarchy 上のオブジェクト変更では Undo を考慮します。

## Avatar Setup に関する注意

- AAO、LAC、RBS、赤夜式 撫で音、LightLimitChanger、可愛いポーズなどの既存導入処理を壊さないでください。
- LightLimitChanger は V1 / V2 の両対応を維持します。
- 可愛いポーズは通常版と 8bit 版を同一ファミリーとして扱います。
- 可愛いポーズのプリセット自動適用、Prefab のみ追加、公式導入の違いを明確に扱います。
- RBS や赤夜式 撫で音は既存導入済みなら原則スキップで構いません。
- 削除して入れ直す処理は、可愛いポーズや LightLimitChanger のようにバリエーション・バージョン違いが問題になるものを中心に扱います。
- 入れ直し処理はデフォルト OFF にし、DryRun とログで確認できるようにします。

## バージョン管理方針

- 機能追加や挙動変更を行った場合は、必要に応じて `package.json` の patch / minor を更新します。
- 小さな UI 改善やバグ修正は基本的に patch 更新とします。
- `CHANGELOG.md` の先頭に同じバージョン番号の変更履歴を追加します。
- CHANGELOG は日本語で記載します。
- 既存形式がある場合はそれに合わせます。

## レビュー方針

- Codex のレビューコメントは日本語で書きます。
- 問題点は、原因・影響・修正案が分かるように書きます。
- 単なる好みの指摘ではなく、Unity / VRChat で実害があるものを優先します。
- 特に以下を重点的に確認します。
  - コンパイルエラー
  - asmdef 依存
  - `OnGUI()` での重い処理
  - Prefab 保存漏れ
  - Undo 漏れ
  - DryRun と Apply の挙動差
  - 既存導入済み検出
  - 破壊的変更の安全性
  - V1 / V2 などバージョン違いの扱い
  - UI 文言が日本語として分かりやすいか
