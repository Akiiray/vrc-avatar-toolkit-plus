# Avatar Preflight Fixer

## 使い方

1. `Tools > VRC Avatar Toolkit Plus > Avatar Preflight Fixer` を開きます。
2. Hierarchyの選択対象／全アバター、またはPrefabフォルダを指定します。フォルダ検索はサブフォルダのON/OFFを選べます。
3. 修正項目とBlendShape方式を選択します。
4. 「解析／変更プレビュー」で変更前後、参照元アバター、共有、フォルダ外参照、適用不可理由を確認します。
5. 「プレビューの変更を一括適用」を実行します。
6. 結果を確認して再解析し、SDKの検査・通常のビルド／アップロードを行います。

Continuous Avatar Uploaderとの直接連携はありません。修正後に従来の操作で利用してください。

## SDK 3.10.5の判定条件

基準は公式Avatarsパッケージの `Editor/VRCSDK/SDK3A/VRCSdkControlPanelAvatarBuilder.cs` です。

* Mesh: `SkinnedMeshRenderer.sharedMesh`、`MeshFilter.sharedMesh`、`ParticleSystemRenderer.GetMeshes()` を収集します。SDKの公開 `ValidationUtils.GetComponentsInChildrenExcludingEditorOnly<T>(true)` をReflectionで解決し、非アクティブを含め、SDKと同じEditorOnly除外を使います。
* Read/Write: Assetとして存在するMeshのModelImporterで `isReadable == false` の場合だけtrueにします。`Mesh.isReadable` は判定に使いません。
* BlendShape: 同じMesh群のModelImporterで `importBlendShapeNormals == Calculate` かつLegacy無効の場合だけ修正します。BlendShape数や `importBlendShapes` による独自の除外はありません。
* Texture: 全Rendererの `sharedMaterials` と `GetTexturePropertyNameIDs()` からTextureを収集し、TextureImporterの `mipmapEnabled && !streamingMipmaps` の場合だけStreamingを有効化します。小型Texture、Ramp、MatCap、LUT、Shader名等の独自除外はありません。

生成Mesh／Texture、Importerのないアセット、独立Mesh Asset等のModelImporterでないMeshは当該SDKチェックの対象外です。MeshColliderだけのMeshやRendererに現在割り当てられていないMaterial、アニメーションで後から切り替わるMaterialはSDKのこの収集処理に含まれません。

## BlendShapeの3方式

| 方式 | 対象の変更前 | 変更後 |
| --- | --- | --- |
| SDK Auto Fix相当（初期値） | Calculate / Legacy false | Calculate / Legacy true |
| Import | Calculate / Legacy false | Import / Legacy false |
| None | Calculate / Legacy false | None / Legacy false |

正常なImport、None、Calculate＋Legacy trueは選択方式にかかわらず変更しません。ベースNormals、Tangents、Smoothing設定は変更しません。

LegacyはUnity 2022.3の内部プロパティ `legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes` をReflectionで操作します。取得・書込・確認ができない場合、BlendShapeだけをスキップします。他項目と一緒に不明なLegacy変更を保存しないため元設定への復元確認ができない場合は、そのアセットの修正を失敗として扱います。

Legacyは法線の計算方法、Importはモデル側の法線（ない場合はFBX SDKの計算）、NoneはBlendShape法線の寄与を変えます。顔、表情、Viseme、Normal Map、陰影を実モデルで確認してください。

## Streaming設定の保持

変更する設定は `streamingMipmaps` だけです。SDK Auto FixはPriorityも0にしますが、本ツールは既存のPriority、Mip Maps、圧縮、最大サイズ、プラットフォーム別設定を維持します。

変更後にStreaming・Mip Maps・Priorityを再検査し、Unity 2022.3のSerializedProperty `m_StreamingMipmaps` を除くImporter設定も比較します。AssetPostprocessor等で他設定が変わった場合は未解決として報告します。確認APIがない場合は安全にスキップします。

## 対象・安全性

* Hierarchyの全対象はロード済み通常シーンです。非アクティブを含み、Prefab StageとPreview Sceneは除外します。
* 子を選択すると最寄りの親Descriptorを対象にします。複数アバターの親を選択すると子孫Descriptorを全抽出します。
* Prefabは `ToolkitPrefabEditScope` で読み込み、継承・Override後の参照を解析してUnloadします。Prefab保存、Unpack、Override適用は行いません。
* フォルダ外の参照も修正対象です。Importerパスで重複排除し、参照元をまとめます。
* 1個のモデルImporter設定は同じモデル内の全Meshに影響します。共有アセットは選択外アバターにも影響します。
* `Assets/` 外（`Packages/`等）は修正しません。Asset本体とmetaの存在、読み取り専用属性、VCSの編集可否を確認します。自動checkoutや権限変更は行いません。
* 対象・オプション変更でプレビューを無効化します。適用直前に再解析し、対象、参照、依存ハッシュ、Importer設定が変わっていれば全体停止して再解析を要求します。
* Play中、コンパイル中、Asset更新中は実行しません。走査は明示操作のみで行います。

## 失敗・キャンセル

変更のあるImporterだけを逐次再インポートします。同じモデルのRead/WriteとBlendShape変更は1回にまとめます。個別アセットの失敗は名称・パス・理由を記録して次へ進みます。失敗後も可能な限り設定を再検査します。

キャンセルはアセット間で受け付け、成功済みの変更を維持します。例外時には設定だけが保存された等の部分変更が残る場合があります。失敗を成功とは扱わず、再解析とログで確認してください。全体の自動ロールバックやUndoによる完全復旧は保証しません。

## 検証状況と実機チェック

Unity 2022.3／SDK 3.10.5を基準に実装しています。SDK 3.10.5以外、Unity Editorでのコンパイル、実機動作、モデル外観は未検証です。SDKの収集APIを取得できない場合は解析を停止します。既存VPM依存条件 `com.vrchat.avatars >=3.7.0` は変更していません。

実装時の静的検証では新規5ファイルのC#構文解析とUnity 2022.3公式ソースのAPI照合を行いました。Unity APIのスタブを使う.NET 8ハーネスでは新規5ファイルをコンパイルし、17件のロジック検証（判定の組合せ、3方式、重複排除、再実行、設定保持、プレビュー変更拒否、Reflection失敗の分離、個別失敗の継続、フォルダ検索等）が通過しました。これらはUnity Editorのコンパイル・動作検証ではなく、Prefab Variantの実際の継承やNative Importerの挙動も検証していません。

実プロジェクトで以下を確認してください。

* SDK表示との検出一致（EditorOnlyの祖先、非アクティブ、ParticleSystem複数Meshを含む）。
* Calculate／Import／None × Legacy true／false、BlendShapeなし、`importBlendShapes=false`、Legacy Reflectionの読書きと再インポート後の保持。
* Mip MapsとStreamingの各ON/OFF、非ゼロPriority、特殊Texture、全Platform設定の保持。
* 通常Prefab、Variant、Nested Prefab、複数Descriptor、フォルダ外参照、PrefabとSceneに保存差分がないこと。
* 複数アバターの共有Importerは1回だけ再インポートされ、2回目は不要な変更・再インポートがないこと。
* プレビュー後の変更拒否、読み取り専用meta、個別失敗時の継続、途中キャンセル、結果ログ。
* 顔・Viseme・表情・陰影・ビルドサイズと、通常の連続ビルド／アップロード。

ビルド時のMA・AAO等による生成・差し替え後のアセットは、静的解析だけでは保証できません。他のSDKエラーを解消する機能でもありません。
