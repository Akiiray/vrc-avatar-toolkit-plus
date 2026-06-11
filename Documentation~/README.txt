VRC Avatar Toolkit Plus 統合版

メニュー:
Tools > VRC Avatar Toolkit Plus > Avatar Setup > Window
Tools > VRC Avatar Toolkit Plus > デバッグ > Avatar Debug Reporter

Avatar Setup Window の対象モード:
- SelectedHierarchyAvatars: Hierarchyで選択したアバター、または子オブジェクトの親アバターに実行
- SelectedProjectPrefabAssets: Projectで選択したPrefabアセットに実行
- SelectedProjectFolderPrefabs: Projectで選択したフォルダ内のPrefabに実行
- AllProjectPrefabs: Project内すべてのPrefabに実行

Project Prefabに適用する場合は PrefabUtility.LoadPrefabContents / SaveAsPrefabAsset で保存します。
LightLimitChanger と 可愛いポーズ は公式導入メソッドをReflectionで呼び出します。
RBS と 赤夜式撫で音は現時点ではPrefab名検索による追加です。
