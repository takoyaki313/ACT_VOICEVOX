# ACT_VOICEVOX

Advanced Combat Tracker (ACT) plugin for Text-to-Speech (TTS) with multiple output options including VOICEVOX support.

<img width="620" height="476" alt="image" src="https://github.com/user-attachments/assets/d9aa3e93-1007-48b7-a1c8-4be0d5173699" />

[日本語版はこちら](#日本語)

## Features

This plugin replaces ACT's default TTS functionality with three selectable modes:

### 1. TCP Server Mode
- Sends TTS messages to external applications via TCP (e.g., Bouyomi-chan)
- Compatible with standard Bouyomi-chan protocol
- Configurable host and port

### 2. SAPI64 Mode
- Uses Windows Speech API (SAPI) for voice output
- Supports all installed SAPI voices

### 3. VOICEVOX Mode
- Connects to VOICEVOX API for high-quality Japanese TTS
- Dynamically loads available speakers from VOICEVOX
- Adjustable speed and volume controls
- Selectable audio output device
- Two playback modes:
  - **Concurrent**: Play multiple messages simultaneously
  - **Queue**: Queue up to 4 messages for sequential playback

## Requirements

- **Advanced Combat Tracker** (ACT)
- **.NET Framework 4.0** or later
- **VOICEVOX** (only for VOICEVOX mode)
  - Download from: https://voicevox.hiroshiba.jp/

## Installation

1. Download `ACT_VOICEVOX.dll` from the [Releases](../../releases) page
2. In ACT, go to **Plugins** > **Plugin Listing**
3. Click **Browse** and select the downloaded `ACT_VOICEVOX.dll`
4. Click **Add/Enable Plugin**

## Usage

### TCP Server Mode
1. Select **TCP Server** mode
2. Configure the host (default: `127.0.0.1`) and port (default: `50001`)
3. Ensure your TTS application (e.g., Bouyomi-chan) is running and listening on the same port

### SAPI64 Mode
1. Select **SAPI64** mode
2. Choose your preferred voice from the dropdown list
3. Click **Test** to verify the voice output

### VOICEVOX Mode
1. Start VOICEVOX application
2. Select **VOICEVOX** mode
3. Choose your preferred speaker from the list
4. Adjust speed and volume as needed
5. Select audio output device
6. Choose playback mode (Concurrent or Queue)
7. Click **Test** to verify

## Building from Source

### Prerequisites
- .NET Framework 4.0 SDK or later
- Advanced Combat Tracker executable (`Advanced Combat Tracker.exe`; copy your own executable into the same folder as `ACT_VOICEVOX.bat`)

### Build Instructions

Using the provided batch file:
```batch
ACT_VOICEVOX.bat
```

Or manually using C# compiler:
```batch
%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe /t:library /r:"Advanced Combat Tracker.exe" /r:"%windir%\Microsoft.NET\Framework\v4.0.30319\WPF\System.Speech.dll" ACT_VOICEVOX.cs
```

This will generate `ACT_VOICEVOX.dll` in the same directory.

## Configuration

Plugin settings are automatically saved to:
```
%APPDATA%\Advanced Combat Tracker\ACT_VOICEVOX_Config.xml
```

## License

This project is open source. Feel free to use, modify, and distribute.

## Troubleshooting

### VOICEVOX mode not working
- Ensure VOICEVOX is running and accessible at `http://127.0.0.1:50021`
- Click the **Reload** button to refresh the speaker list

### No sound output
- Check your selected audio device in settings
- Verify the output device is not muted
- Try the **Test** button to diagnose the issue

---

# 日本語

Advanced Combat Tracker (ACT) 用のテキスト読み上げ (TTS) プラグインです。

## 機能

ACT のデフォルト TTS 機能を置き換え、3つの読み上げモードから選択できます：

### 1. TCP Server モード
- TCP 経由で外部アプリケーション（棒読みちゃんなど）にメッセージを送信
- 標準的な棒読みちゃんプロトコルに対応
- ホストとポートを設定可能

### 2. SAPI64 モード
- Windows 音声合成 API (SAPI) を使用
- インストール済みのすべての SAPI 音声に対応

### 3. VOICEVOX モード
- VOICEVOX API に接続して高品質な日本語 TTS を実現
- VOICEVOX から利用可能な話者を動的に読み込み
- 速度と音量を調整可能
- オーディオ出力デバイスを選択可能
- 2つの再生モード：
  - **Concurrent（同時再生）**: 複数のメッセージを同時に再生
  - **Queue（キュー）**: 最大4つのメッセージを順次再生

## 必要要件

- **Advanced Combat Tracker** (ACT) — `Advanced Combat Tracker.exe` は同梱・配布しません。公式サイトから各自取得して手元に保管してください。
- **.NET Framework 4.0** 以降
- **VOICEVOX**（VOICEVOX モードを使用する場合のみ）
  - ダウンロード: https://voicevox.hiroshiba.jp/

## インストール方法

1. [Releases](../../releases) ページから `ACT_VOICEVOX.dll` をダウンロード
2. ACT で **Plugins** > **Plugin Listing** を開く
3. **Browse** をクリックし、ダウンロードした `ACT_VOICEVOX.dll` を選択
4. **Add/Enable Plugin** をクリック

## 使い方

### TCP Server モード
1. **TCP Server** モードを選択
2. ホスト（デフォルト: `127.0.0.1`）とポート（デフォルト: `50001`）を設定
3. TTS アプリケーション（棒読みちゃんなど）が同じポートで待機していることを確認

### SAPI64 モード
1. **SAPI64** モードを選択
2. ドロップダウンリストから使用したい音声を選択
3. **Test** ボタンで音声出力を確認

### VOICEVOX モード
1. VOICEVOX アプリケーションを起動
2. **VOICEVOX** モードを選択
3. リストから話者を選択
4. 必要に応じて速度と音量を調整
5. オーディオ出力デバイスを選択
6. 再生モード（Concurrent または Queue）を選択
7. **Test** ボタンで動作確認

## ソースからのビルド

### 前提条件
- .NET Framework 4.0 SDK 以降
- Advanced Combat Tracker 実行ファイル （ACT_VOICEVOX.batと同階層にコピペします）

### ビルド手順

付属のバッチファイルを使用：
```batch
ACT_VOICEVOX.bat
```

または C# コンパイラで手動ビルド：
```batch
%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe /t:library /r:"Advanced Combat Tracker.exe" /r:"%windir%\Microsoft.NET\Framework\v4.0.30319\WPF\System.Speech.dll" ACT_VOICEVOX.cs
```

同じディレクトリに `ACT_VOICEVOX.dll` が生成されます。

## 設定

プラグインの設定は自動的に以下の場所に保存されます：
```
%APPDATA%\Advanced Combat Tracker\ACT_VOICEVOX_Config.xml
```

## ライセンス

このプロジェクトはオープンソースです。自由に使用、変更、配布できます。

## トラブルシューティング

### VOICEVOX モードが動作しない
- VOICEVOX が起動し、`http://127.0.0.1:50021` でアクセス可能か確認
- **Reload** ボタンで話者リストを更新してみる

### 音声が出力されない
- 設定で選択したオーディオデバイスを確認
- 出力デバイスがミュートになっていないか確認
- **Test** ボタンで問題を診断

## 貢献

バグ報告や機能リクエストは Issues からお願いします。
プルリクエストも歓迎します。

## クレジット

- VOICEVOX: https://voicevox.hiroshiba.jp/
- Advanced Combat Tracker: https://advancedcombattracker.com/
