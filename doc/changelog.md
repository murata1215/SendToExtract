# Changelog

## 2026-05-31: ファイル選択画面・UI改善・アイコン追加

### ファイル選択画面（SelectionForm）
- 「送る」後に即展開ではなく、フォルダ内のアーカイブ一覧を表示して選択してから展開するフローに変更
- CheckedListBox でファイル名+サイズを表示、全選択/全解除ボタン付き
- ExtractorService に `ListArchives` static メソッドを追加（キュー追加せず列挙のみ）

### セットアップ画面（SetupForm）
- exe ダブルクリック時に MessageBox → 専用ウィンドウに変更
- 「送る」に登録 / 削除ボタン、登録状態表示（緑/オレンジ）
- 登録済みなら登録ボタンをグレーアウト、未登録なら削除ボタンをグレーアウト

### 自動クローズ廃止
- 展開完了後、自動で閉じずに「閉じる」ボタン押下で終了するように変更
- StartAutoClose / _autoCloseTimer 関連コードを削除

### アプリケーションアイコン
- app.ico を追加（256/48/32/16px の4サイズ内包）
- csproj に `<ApplicationIcon>app.ico</ApplicationIcon>` を設定
- 全フォーム（SelectionForm, ProgressForm, SetupForm）の左上にアイコンを表示

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
