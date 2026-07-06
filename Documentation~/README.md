# VRC Avatar Toolkit Plus 統合版

## メニュー

- `Tools > VRC Avatar Toolkit Plus > Avatar Setup > Window`
- `Tools > VRC Avatar Toolkit Plus > デバッグ > Avatar Debug Reporter`

## Avatar Setup Window の対象モード

- `SelectedHierarchyAvatars`: Hierarchy で選択したアバター、または子オブジェクトの親アバターに実行します。
- `SelectedProjectPrefabAssets`: Project で選択した Prefab アセットに実行します。
- `SelectedProjectFolderPrefabs`: Project で選択したフォルダ内の Prefab に実行します。
- `AllProjectPrefabs`: Project 内すべての Prefab に実行します。

Project Prefab に適用する場合は、`PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` で保存します。

LightLimitChanger は V2 の公式導入メソッドを Reflection で呼び出し、見つからない場合は V1 の `ApplytoAvatar(MenuCommand)`、最後に Light Limit Changer Prefab 検索による追加へフォールバックします。

可愛いポーズは公式導入メソッドを Reflection で呼び出します。

RBS と 赤夜式 撫で音は、現時点では Prefab 名検索による追加です。

## Material Copy

- `Tools > VRC Avatar Toolkit Plus > Material > Material Copy Window`
- `Assets` 右クリック > `VRC Avatar Toolkit Plus > Material > Prefabをマテリアルごと複製`
- `Assets` 右クリック > `VRC Avatar Toolkit Plus > Material > Prefab内マテリアルを複製して差し替え`
- `Hierarchy` 右クリック > `VRC Avatar Toolkit Plus > Material > マテリアルを複製して差し替え`

Material Copy Window では、アバターや Prefab で使用している Material 一覧を表示し、Material Asset を複製して Renderer の参照を差し替えるか、手動指定した Material へ差し替えできます。

Prefab Asset を複製するモードでは、Prefab 本体を別 Asset としてコピーしたうえで、コピー後 Prefab だけが参照する Material も複製します。
