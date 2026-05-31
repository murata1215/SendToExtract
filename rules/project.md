# プロジェクト固有ルール

## SharpCompress API の注意点

### ストリーム所有権
- `ArchiveFactory.OpenArchive(stream)` は dispose 時にストリームを閉じる
- 同じファイルに対して Archive チェック → Reader 展開の2段階処理を行う場合、**別々のストリーム**を開くこと
- 1つのストリームを使い回すと "Cannot access a closed file" エラーになる

### 形式別 API 使い分け
| 形式 | API | 理由 |
|------|-----|------|
| zip, tar, tar.gz, tar.bz2, gz, bz2 | `ArchiveFactory.OpenArchive` | ランダムアクセス可能 |
| 7z, rar | `ReaderFactory.OpenReader` | ソリッドアーカイブはランダムアクセス不可 |

### API 名の変更（0.49.1）
- 旧: `ArchiveFactory.Open` → 新: `ArchiveFactory.OpenArchive`
- 旧: `ReaderFactory.Open` → 新: `ReaderFactory.OpenReader`

## UIフロー
- 「送る」→ SelectionForm（ファイル選択）→ ProgressForm（展開進捗）の2段階
- 展開完了後は自動クローズしない（ユーザーが「閉じる」ボタンで終了）
- exe ダブルクリック（引数なし）→ SetupForm（「送る」登録/削除画面）
- 全フォームに `Icon.ExtractAssociatedIcon(Application.ExecutablePath)` でアイコン設定

## 展開ロジック
- 展開先フォルダが既に存在する場合の衝突ポリシーは設定可能（デフォルト: Rename）
- 展開失敗時は空の出力フォルダを自動削除する
- 二重ラップ平坦化は一時フォルダ経由で安全に実行
