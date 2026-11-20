# FruitCatch 共同開発ガイド

## 基本方針

`main`は、動作確認済みの安定版として維持します。通常の作業を`main`へ直接コミットせず、作業ごとにブランチを作成し、Pull Requestで統合します。

今回の大規模改修中も、完成前の状態を`main`へ直接入れないでください。

## 役割

### プロジェクト所有者・統合担当

- Pull Requestの最終確認と`main`への統合
- Windows実機、カメラ、マイク、Python連携の確認
- Windowsビルドと配布物の確認
- UnityバージョンとBuild Settingsの管理

### Mac共同開発者・大規模改修担当

- 合意した範囲のゲームロジック、UI、演出などの改修
- macOS上でのUnity Editor動作確認
- 変更内容、確認方法、未確認事項をPull Requestに記載
- Windows専用部分へ影響する変更を事前に共有

役割は責任範囲を表すもので、永久的なファイル所有権ではありません。実際の編集権は、次のScene担当ルールで作業ごとに決めます。

## SceneとPrefabの担当ルール

作業開始前に、チャットまたはGitHub Issueで次の形式を宣言します。

```text
担当: 名前
期間: 11/20〜11/22
編集: MainGame.unity、Fruit.prefab
目的: スコア演出の改修
```

宣言されたSceneやPrefabは、担当者が作業を終えるまで他の人は編集しません。スクリプトだけを変更する場合も、そのスクリプトが同じSceneやPrefabのInspector設定変更を必要とするか確認してください。

特に次のファイルは同時編集を避けます。

- `Assets/Scenes/Preparation.unity`
- `Assets/Scenes/MainGame.unity`
- `Assets/Scenes/Menu.unity`
- `Assets/Prefab/*.prefab`
- `ProjectSettings/*`
- `Packages/manifest.json`
- `Packages/packages-lock.json`

## ブランチ

通常は作業単位でブランチを作ります。人ごとのブランチを永続的に使い続けません。

```text
feature/<短い作業名>   新機能・大きな変更
fix/<短い作業名>       不具合修正
docs/<短い作業名>      文書のみの変更
chore/<短い作業名>     設定・整理
```

例:

```text
feature/main-game-effects
fix/microphone-detection
docs/mac-setup
```

共同開発者による今回の大規模改修は、例外的に`feature/mac-overhaul`のような専用ブランチで開始できます。ただし、その中でも意味のある小さな単位でコミットし、長期間`main`から離れたままにしないでください。

## 作業手順

1. Unityを閉じるか、保存を完了します。
2. `main`を最新にします。
3. 作業用ブランチを作ります。
4. 担当を宣言した範囲だけを編集します。
5. UnityでConsoleエラーがないことと、対象機能を確認します。
6. 変更したScene、Prefab、`.meta`を確認してコミットします。
7. GitHubへpushし、Pull Requestを作ります。
8. 相手の確認後に`main`へ統合します。

他の人の未完了ブランチを直接編集せず、必要な場合は担当者と相談してから同じブランチで作業します。

## コミット

コミットには、何を変えたか分かる短い説明を付けます。

```text
Add score result animation
Fix microphone threshold handling
Update Preparation instructions
```

「test」「work」「変更」だけの説明や、無関係な変更をまとめた巨大なコミットは避けます。大規模改修でも、ロジック、UI、素材、設定など確認可能な単位に分けます。

## Pull Requestに書くこと

- 変更の目的
- 主な変更ファイル
- 動作確認の手順と結果
- 編集したSceneとPrefab
- WindowsまたはMacで未確認の項目
- スクリーンショット（画面変更がある場合）

統合担当は、Sceneの意図しない差分、`.meta`の欠落、Consoleエラー、Windows専用機能への影響を確認します。

## 文字コードと改行

- テキストファイルはUTF-8を使用します。
- 改行は原則LFです。
- `.bat`と`.cmd`だけはCRLFです。
- Shift_JIS、CP932、JISでは保存しません。

このリポジトリの`.editorconfig`と`.gitattributes`を有効にした状態で編集してください。

## Unity固有の注意

- `.meta`はAssetと必ず一緒に扱います。
- Unity外でAssetを移動する場合も、対応する`.meta`を一緒に移動します。
- Unityのバージョンを勝手に更新しません。
- Packageの追加や更新は、事前に相談します。
- SceneやPrefabのコンフリクトをGit上で安易に自動解決しません。
- コンフリクトした場合は、両者で正しい状態を確認してUnity上で再構成します。

## 現在の保留事項

- macOSでの顔認識、Python環境、配布用起動手順は未検証です。
