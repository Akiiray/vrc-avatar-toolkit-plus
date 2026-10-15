# VRC Avatar Toolkit Plus 統合版

## メニュー

- `Tools > VRC Avatar Toolkit Plus > Avatar Setup > 導入ウィンドウ`（`Window`は互換エイリアス）
- `Tools > VRC Avatar Toolkit Plus > デバッグ > Avatar Debug Reporter`
- `Tools > VRC Avatar Toolkit Plus > Avatar Preflight Fixer`

Avatar Preflight Fixerの手順・判定条件・安全性は [専用ドキュメント](AvatarPreflightFixer.md) を参照してください。

## Avatar Setup Window の対象モード

- `SelectedHierarchyAvatars`: Hierarchy で選択したアバター、または子オブジェクトの親アバターに実行します。
- `SelectedProjectPrefabAssets`: Project で選択した Prefab アセットに実行します。
- `SelectedProjectFolderPrefabs`: Project で選択したフォルダ内の Prefab に実行します。
- `AllProjectPrefabs`: Project 内すべての Prefab に実行します。

Project Prefab に適用する場合は、`PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` で保存します。

LightLimitChanger は V2 の公式導入メソッドを Reflection で呼び出し、見つからない場合は V1 の `ApplytoAvatar(MenuCommand)`、最後に Light Limit Changer Prefab 検索による追加へフォールバックします。

可愛いポーズはプリセット自動適用・プレビルドなし、公式導入（確認あり）、Prefabのみ追加を区別します。公式導入では外部メソッドをReflectionで呼び出します。

RBSは既知Prefab候補の検索による追加です。赤夜式 撫で音は公式GUIDを優先し、Prefab名検索へフォールバックします。足システム、影シェーダー、Contact等の導入設定に対応します。

入れ直しは既存構成や手動設定を失う可能性があります。Dry Runで対象と変更予定を確認してください。ToolkitのProject Prefab保存や元TextureImporter編集はToolkit固有であり、LiteKitのBuild clone変換とは異なります。

## Material Copy

- `Tools > VRC Avatar Toolkit Plus > Material > Material Copy Window`
- `Assets` 右クリック > `VRC Avatar Toolkit Plus > Material > Prefabをマテリアルごと複製`
- `Assets` 右クリック > `VRC Avatar Toolkit Plus > Material > Prefab内マテリアルを複製して差し替え`
- `Hierarchy` 右クリック > `VRC Avatar Toolkit Plus > Material > マテリアルを複製して差し替え`

Material Copy Window では、アバターや Prefab で使用している Material 一覧を表示し、Material Asset を複製して Renderer の参照を差し替えるか、手動指定した Material へ差し替えできます。

Prefab Asset を複製するモードでは、Prefab 本体を別 Asset としてコピーしたうえで、コピー後 Prefab だけが参照する Material も複製します。

## Plusシリーズの設計資料

ToolkitはUI / UXの参照実装ですが、現行UIのすべてを標準とするものではありません。

- [Plus UI詳細設計ガイド](UI_DESIGN_GUIDELINES.md)
- [横断アーキテクチャ](https://github.com/Akiiray/vpm-repository/blob/main/docs/ECOSYSTEM_ARCHITECTURE.md)
- [共通設計思想](https://github.com/Akiiray/vpm-repository/blob/main/docs/DESIGN_PRINCIPLES.md)

機能一覧の正本は[Toolkit README](../README.md)、Release履歴は[CHANGELOG](../CHANGELOG.md)です。
