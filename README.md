# VRC Avatar Toolkit Plus

VRC Avatar Toolkit Plus は、VRChat アバター制作・改変作業を効率化するための Unity Editor 拡張ツール集です。

アバターへの便利ツールの導入支援、一括セットアップ、マテリアル管理、デバッグ、依存関係解析、VRC Dynamics の保守など、日常的な改変作業をサポートします。

---

# 主な機能

## Avatar Setup

`Tools > VRC Avatar Toolkit Plus > Avatar Setup`

アバターへの各種ツール・ギミックの導入を支援するメイン機能です。

### 対応対象

* Hierarchy 上のアバター
* Project 内の Prefab
* フォルダ内の Prefab
* 複数 Prefab の一括処理

### 導入支援ツール

* Avatar Optimizer (AAO)
* Avatar Compressor (LAC)
* RBS Sleep System Ver.2
* 赤夜式 撫で音ギミック
* LightLimitChanger
* 可愛いポーズ
* 可愛いポーズ（8bit・足の高さなし）

必要なものだけ個別に導入することも、まとめてセットアップすることもできます。

また、Dry Run（試行実行）により、実際に変更を加える前に処理内容を確認できます。

---

## Material Copy

`Tools > VRC Avatar Toolkit Plus > Material > Material Copy Window`

アバターや Prefab で使用されている Material を複製し、安全に差し替えるためのツールです。

### 主な機能

* 使用中 Material の一覧表示
* Material の一括複製
* Renderer の参照先を自動更新
* 任意の Material への置き換え
* Prefab コピー時に Material も同時複製
* Material の共有状態を解消して個別編集可能にする用途にも利用可能

右クリックメニューから簡易的に実行することもできます。

---

## Dependency Check

Avatar Setup には依存関係や導入状態を確認する機能があります。

以下のような情報を検出できます。

* Avatar Optimizer
* Avatar Compressor
* Modular Avatar
* LightLimitChanger
* 可愛いポーズ
* 赤夜式 撫で音ギミック
* その他対応ツール

アバターに既に導入済みかどうかや、必要なパッケージが存在するかを確認できます。

---

## Avatar Debug Reporter

選択中アバターを解析し、テキスト形式のレポートを生成します。

レポートには以下のような情報が含まれます。

* 使用されているコンポーネント
* 導入済みツールの状況
* Hierarchy の概要
* アバター配下の構成
* デバッグ・調査用情報

問題発生時の原因調査や、他者への情報共有にも利用できます。

---

## Debug Utilities

開発者向けの調査機能を多数搭載しています。

例:

* コンポーネント一覧表示
* SerializedProperty の確認
* Reflection 情報の表示
* アバターパラメーターの確認
* Prefab 開発支援情報
* 参照 Asset の一覧
* 完全レポート生成

Hierarchy の右クリックメニューから利用できる項目も用意されています。

---

## VRC Dynamics Duplicate Cleaner

`Tools > VRC Avatar Toolkit Plus > Dynamics`

同一 GameObject に重複して存在する VRC Dynamics コンポーネントを検出し、安全に整理します。

対応コンポーネント:

* VRCPhysBone
* VRCPhysBoneCollider
* VRCContactReceiver
* VRCContactSender

誤検出を防ぐため、内容が一致する重複のみを修正対象として扱います。

---

## Texture Streaming Mipmaps Setup

アバターで実際に使用されているテクスチャを検出し、Streaming Mipmaps を有効化します。

### 特徴

* 使用中テクスチャのみを対象に検出
* `streamingMipmaps` のみ変更
* Max Texture Size や Compression は変更しない
* Dry Run に対応
* Hierarchy・Prefab・フォルダ単位・プロジェクト単位で実行可能

VRAM 使用量の削減や負荷軽減を目的とした最適化作業を支援します。

---

# 対応・連携ツール

本ツールは以下のパッケージとの連携機能を備えています。

* Avatar Optimizer (AAO)
* Avatar Compressor (LAC)
* Modular Avatar
* RBS Sleep System Ver.2
* 赤夜式 撫で音ギミック
* LightLimitChanger
* 可愛いポーズ

対象パッケージがインストールされている場合、導入支援や状態確認などの追加機能を利用できます。

---

# 想定用途

* 新規アバターのセットアップ
* 複数アバターへの一括導入
* マテリアルの分離・複製
* VRChat 向け最適化
* PhysBone や Contact の重複修正
* アバター構成の調査
* 不具合解析・デバッグ
* 大量の Prefab やアバターの保守・管理

日常的な VRChat アバター制作・改変作業を効率化するためのオールインワンツールキットとして利用できます。
