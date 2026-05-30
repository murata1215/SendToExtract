# Changelog

## 2026-05-31: 初版実装 + バグ修正

### 初版実装
- プロジェクト構築: .NET 8 / WinForms / SharpCompress 0.49.1
- アーカイブ展開コア: zip, tar, tar.gz, tgz, tar.bz2, gz, bz2, 7z, rar 対応
- CP932（Shift_JIS）日本語ファイル名の文字化け対策
- 進捗ウィンドウ: 全体/個別プログレスバー、ログ一覧、キャンセル、自動クローズ
- 多重起動集約: 名前付き Mutex + Named Pipe
- 「送る」メニュー登録: `--install` / `--uninstall`
- 衝突ポリシー: Skip / Rename / Overwrite
- 二重ラップ平坦化（name/name/... → name/...）
- パスワード付きアーカイブ対応（セッション内パスワード使い回し）
- 設定ファイル: `%APPDATA%\SendToExtract\settings.json`

### バグ修正: rar/7z 展開時の "Cannot access a closed file" エラー
- 原因: `ExtractWithReader` で Archive チェック用と展開用に同じストリームを共有していた。`ArchiveFactory.OpenArchive` の dispose 時にストリームが閉じられ、後続の Reader 展開が失敗
- 修正: チェック用と展開用で別々のストリームを開くように分離
- 展開失敗時に空の出力フォルダが残る問題も修正（自動削除）
- ログ一覧の「詳細」列幅を拡大（200→280）
