# SendToExtract

Windowsの「送る」メニューから、フォルダ内のアーカイブを一括展開するツール。

## 特徴

- **「送る」メニュー統合** — フォルダやアーカイブを右クリック →「送る」→「SendToExtract」で一括展開
- **多形式対応** — zip, tar, tar.gz, tgz, tar.bz2, gz, bz2, 7z, rar
- **日本語ファイル名対応** — CP932（Shift_JIS）エンコーディングの ZIP でも文字化けなし
- **単一 exe** — .NET ランタイム不要の self-contained single-file
- **進捗表示** — プログレスバー付きの WinForms ウィンドウ
- **多重起動集約** — 大量ファイルを「送る」で渡しても1つのウィンドウに集約

## インストール

```bash
# ビルド
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true

# 任意の場所に配置（例: C:\Tools\）
# 「送る」メニューに登録
SendToExtract.exe --install
```

## 使い方

1. フォルダまたはアーカイブファイルを右クリック
2. 「送る」→「SendToExtract」を選択
3. 進捗ウィンドウが表示され、アーカイブが順次展開される

### コマンドライン

```
SendToExtract.exe <フォルダ/ファイルパス> ...   展開を実行
SendToExtract.exe --install                     「送る」に登録
SendToExtract.exe --uninstall                   「送る」から削除
SendToExtract.exe                               使い方を表示
```

## 設定

`%APPDATA%\SendToExtract\settings.json` で設定を変更できます。

| 設定項目 | デフォルト | 説明 |
|---------|-----------|------|
| `collisionPolicy` | `Rename` | 既存フォルダの衝突時: Skip / Rename / Overwrite |
| `flattenSingleRoot` | `true` | 二重ラップ平坦化（name/name/... → name/...） |
| `recurseSubfolders` | `false` | サブフォルダも再帰的に探索 |
| `autoCloseOnSuccess` | `true` | 全成功時に自動クローズ |
| `autoCloseDelaySec` | `3` | 自動クローズまでの秒数 |

## 技術スタック

| 項目 | 技術 |
|------|------|
| フレームワーク | .NET 8 |
| UI | WinForms |
| アーカイブ処理 | SharpCompress |
| 配布形態 | self-contained single-file exe |

## ライセンス

MIT
