# FruitCatch

カメラとマイクを使って操作する、Unity製のフルーツキャッチゲームです。

## 開発環境

- Unity `6000.6.4f1`
- Universal Render Pipeline 17.6.0
- Input System 1.20.0
- Unity Pipeline 1.0.1（Unity CLIからEditorを操作・調査）
- Git

共同開発では、必ず同じUnityバージョンを使用してください。異なるバージョンでSceneやPrefabを保存すると、大量の差分や互換性問題が発生することがあります。

## プロジェクトを開く

1. このリポジトリをcloneします。
2. Unity Hubでリポジトリのルートフォルダーを追加します。
3. Unity `6000.6.4f1`で開きます。
4. 最初のimportが完了するまで待ちます。
5. `Assets/Scenes/Preparation.unity`から再生します。

ビルド対象のSceneは次の2つです。

1. `Assets/Scenes/Preparation.unity`
2. `Assets/Scenes/MainGame.unity`

`Assets/Scenes/Menu.unity`は現在、Build Settingsには登録されていません。

## 操作方法

通常操作、キーボード・マウスによるデバッグ操作、実機入力なしでの確認手順は、[操作ガイド](Docs/Controls.md)を参照してください。

アイテムの種類・取得ルール・効果は、[アイテム仕様](Docs/Items.md)を参照してください。

50秒間の体験フローと出現制御は、[出現設計](Docs/SpawnDesign.md)を参照してください。

## Windowsでの実行

配布用ファイルは`Tools/Distribution`にあります。

- 初回準備: `Setup FruitCatch.bat`
- 通常起動: `Start FruitCatch.bat`

PythonがないWindows PCでは、セットアップ時にPython 3.12を準備します。通常起動では日本語のランチャーが開きます。カメラとマイクを接続し、カラープレビューを確認して「ゲーム起動」を押してください。

「詳細設定」で解像度・要求FPS・映像形式と、骨格追跡／顔追跡を変更できます。Unity側の表示は30fpsが目標で、実測値はカメラやPCに依存します。[Windows配布版の起動ガイド](Tools/Distribution/はじめに.txt)

カメラなしの回帰テストは `python -m unittest discover -s Tools/Verification` で実行できます。

## macOSでの開発

Unityプロジェクトの編集はmacOSでも行えます。ただし、現在の配布・起動スクリプト（`.bat`、`.ps1`）とWindows向けビルド処理はWindows用です。顔認識ツールを含むmacOSでの実機起動手順は、まだ検証されていません。

MacではまずUnity Editor上で、カメラ判定を使わない開発用操作によってSceneやゲームロジックを確認してください。

## 共同開発

ブランチ、Sceneの担当、Pull Requestの手順は[CONTRIBUTING.md](CONTRIBUTING.md)を参照してください。

## Git管理上の注意

- `.meta`ファイルは削除せず、対応するAssetと一緒にコミットします。
- `Library`、`Temp`、`Logs`、`Builds`、`UserSettings`はコミットしません。
- テキストはUTF-8、改行は原則LFです。
- SceneやPrefabを同時に編集しないでください。

