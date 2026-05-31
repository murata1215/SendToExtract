# SendToExtract

Windowsの「送る」メニューから、フォルダ内のアーカイブを一括展開するツール。

## 特徴

- **「送る」メニュー統合** — フォルダやアーカイブを右クリック →「送る」→「SendToExtract」で一括展開
- **多形式対応** — zip, tar, tar.gz, tgz, tar.bz2, gz, bz2, 7z, rar
- **日本語ファイル名対応** — CP932（Shift_JIS）エンコーディングの ZIP でも文字化けなし
- **単一 exe** — .NET ランタイム不要の self-contained single-file
- **ファイル選択** — 展開前にフォルダ内のアーカイブを一覧表示、選択して展開
- **進捗表示** — プログレスバー付きの WinForms ウィンドウ
- **かんたんセットアップ** — exe ダブルクリックで「送る」に登録画面を表示
- **多重起動集約** — 大量ファイルを「送る」で渡しても1つのウィンドウに集約

## インストール

```bash
# ビルド
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true

# 任意の場所に配置（例: C:\Tools\）
# ダブルクリックで起動 → 「送る」に登録ボタンを押す
```

## 使い方

1. フォルダまたはアーカイブファイルを右クリック
2. 「送る」→「SendToExtract」を選択
3. ファイル選択画面でアーカイブを確認・選択
4. 「展開」ボタンで展開開始 → 進捗ウィンドウで完了を確認

### コマンドライン

```
SendToExtract.exe <フォルダ/ファイルパス> ...   展開を実行
SendToExtract.exe --install                     「送る」に登録
SendToExtract.exe --uninstall                   「送る」から削除
SendToExtract.exe                               セットアップ画面を表示
```

## 設定

`%APPDATA%\SendToExtract\settings.json` で設定を変更できます。

| 設定項目 | デフォルト | 説明 |
|---------|-----------|------|
| `collisionPolicy` | `Rename` | 既存フォルダの衝突時: Skip / Rename / Overwrite |
| `flattenSingleRoot` | `true` | 二重ラップ平坦化（name/name/... → name/...） |
| `recurseSubfolders` | `false` | サブフォルダも再帰的に探索 |
| `autoCloseOnSuccess` | `true` | （現在未使用） |
| `autoCloseDelaySec` | `3` | （現在未使用） |

## 技術スタック

| 項目 | 技術 |
|------|------|
| フレームワーク | .NET 8 (net8.0-windows) |
| UI | WinForms |
| アーカイブ処理 | SharpCompress 0.49.1 |
| エンコーディング | System.Text.Encoding.CodePages (CP932) |
| 配布形態 | self-contained single-file exe |

## プロジェクト構成

```
src/
├── Program.cs            エントリポイント、引数解析、Mutex/Pipe 多重起動集約
├── ArchiveHandler.cs     SharpCompress ラッパ、形式判定、CP932、パスワード
├── ExtractorService.cs   展開キュー制御、衝突ポリシー、平坦化、ログ
├── SelectionForm.cs      ファイル選択 UI
├── ProgressForm.cs       展開進捗 UI
├── SetupForm.cs          セットアップ UI（ダブルクリック時）
├── SendToInstaller.cs    「送る」ショートカット作成/削除
├── Settings.cs           settings.json 読み書き
├── app.ico               アプリケーションアイコン
└── SendToExtract.csproj  プロジェクト定義
```

## ライセンス

MIT
