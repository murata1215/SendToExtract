<!-- DevRelay Agreement v6 -->
See `rules/devrelay.md` for DevRelay rules.
<!-- /DevRelay Agreement -->

---

# SendToExtract

Windowsの「送る」メニューからアーカイブを一括展開するツール。

## 技術スタック

| 項目 | 技術 |
|------|------|
| フレームワーク | .NET 8 (net8.0-windows) |
| UI | WinForms |
| アーカイブ処理 | SharpCompress 0.49.1 |
| 配布形態 | self-contained single-file exe |

## ビルド & デプロイ

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
# 「送る」に登録: SendToExtract.exe --install
```

## プロジェクト構成

- `src/` — メインソースコード
- `rules/project.md` — プロジェクト固有ルール
- `doc/changelog.md` — 変更履歴
- `doc/issues.md` — 課題管理
