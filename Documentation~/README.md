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
LightLimitChanger はV2の公式導入メソッドをReflectionで呼び出し、V1はLight Limit Changer Prefab検索による追加にフォールバックします。
可愛いポーズは公式導入メソッドをReflectionで呼び出します。
RBS と 赤夜式撫で音は現時点ではPrefab名検索による追加です。

Material Copy:
- Tools > VRC Avatar Toolkit Plus > Material > Material Copy Window
- Assets右クリック > VRC Avatar Toolkit Plus > Material > Prefabをマテリアルごと複製
- Assets右クリック > VRC Avatar Toolkit Plus > Material > Prefab内マテリアルを複製して差し替え
- Hierarchy右クリック > VRC Avatar Toolkit Plus > Material > マテリアルを複製して差し替え

Material Copy Window では、アバターやPrefabで使用しているMaterial一覧を表示し、
Material Assetを複製してRendererの参照を差し替えるか、手動指定したMaterialへ差し替えできます。
Prefab Assetを複製するモードでは、Prefab本体を別Assetとしてコピーしたうえで、コピー後Prefabだけが参照するMaterialも複製します。
