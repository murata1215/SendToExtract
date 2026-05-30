using System.Collections.Concurrent;
using System.Text;

namespace SendToExtract;

/// <summary>
/// 展開処理全体を制御するサービスクラス。
/// アーカイブのキュー管理、展開先フォルダ名の生成、衝突ポリシーの適用、
/// 二重ラップ平坦化、ログファイル出力を一元的に担当する。
///
/// 使い方:
///   var service = new ExtractorService(settings, passwordCallback);
///   service.AddPaths(args);  // 引数からファイル/フォルダを追加
///   await service.ExtractAllAsync(overallProgress, itemProgress, ct);
/// </summary>
public class ExtractorService
{
    /// <summary>アプリケーション設定</summary>
    private readonly Settings _settings;

    /// <summary>アーカイブ処理ハンドラ</summary>
    private readonly ArchiveHandler _archiveHandler;

    /// <summary>展開対象のアーカイブファイルキュー（スレッドセーフ）</summary>
    private readonly ConcurrentQueue<string> _queue = new();

    /// <summary>展開結果のログリスト</summary>
    private readonly ConcurrentBag<ExtractResult> _results = new();

    /// <summary>キューに追加された総数（進捗計算用）</summary>
    private int _totalCount;

    /// <summary>処理済みの数</summary>
    private int _processedCount;

    /// <summary>ログ出力用 StringBuilder</summary>
    private readonly StringBuilder _logBuilder = new();

    /// <summary>ログファイルの出力先パス</summary>
    private string? _logFilePath;

    /// <summary>
    /// ExtractorService のコンストラクタ。
    /// </summary>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="passwordCallback">パスワード要求時のコールバック（UIスレッドで呼ばれる）</param>
    public ExtractorService(Settings settings, Func<string, string?>? passwordCallback = null)
    {
        _settings = settings;
        _archiveHandler = new ArchiveHandler(passwordCallback);
    }

    /// <summary>キューに残っている展開対象の数</summary>
    public int QueueCount => _queue.Count;

    /// <summary>全体の総数</summary>
    public int TotalCount => _totalCount;

    /// <summary>処理済みの数</summary>
    public int ProcessedCount => _processedCount;

    /// <summary>展開結果のリスト（読み取り専用）</summary>
    public IReadOnlyCollection<ExtractResult> Results => _results;

    /// <summary>失敗した展開の数</summary>
    public int FailureCount => _results.Count(r => !r.Success);

    /// <summary>
    /// コマンドライン引数からファイル/フォルダを解析し、展開キューに追加する。
    /// フォルダが指定された場合はその直下（または再帰的に）アーカイブを列挙する。
    /// ファイルが指定された場合はそのまま追加する。
    /// </summary>
    /// <param name="paths">コマンドライン引数で渡されたパスの配列</param>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                // フォルダの場合: 中のアーカイブファイルを列挙
                EnqueueFromDirectory(path);
            }
            else if (File.Exists(path))
            {
                // ファイルの場合: アーカイブかどうか判定して追加
                EnqueueFile(path);
            }
            // 存在しないパスは無視（ログには残す）
            else
            {
                LogLine($"[スキップ] パスが見つかりません: {path}");
            }
        }
    }

    /// <summary>
    /// 単一のファイルパスをキューに追加する。
    /// Named Pipe 経由で後発プロセスから受け取った引数の追加に使用。
    /// </summary>
    /// <param name="filePath">追加するファイルパス</param>
    public void AddFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            EnqueueFile(filePath);
        }
        else if (Directory.Exists(filePath))
        {
            EnqueueFromDirectory(filePath);
        }
    }

    /// <summary>
    /// キュー内の全アーカイブを順次展開する。
    /// 1つの失敗で全体を止めず、全てのアーカイブを処理する。
    /// </summary>
    /// <param name="overallProgress">全体進捗の通知（処理済み数 / 総数）</param>
    /// <param name="itemProgress">個別アーカイブの展開進捗</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>全アーカイブの展開結果リスト</returns>
    public async Task<IReadOnlyList<ExtractResult>> ExtractAllAsync(
        IProgress<OverallProgress>? overallProgress,
        IProgress<ExtractProgress>? itemProgress,
        CancellationToken ct)
    {
        LogLine($"=== SendToExtract 展開開始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        LogLine($"対象アーカイブ数: {_totalCount}");
        LogLine("");

        while (_queue.TryDequeue(out var archivePath))
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(archivePath);

            // 全体進捗を通知
            overallProgress?.Report(new OverallProgress(
                _processedCount, _totalCount, fileName, OverallStatus.Processing));

            LogLine($"[展開中] {archivePath}");

            // マルチボリュームチェック（初版スコープ外）
            if (ArchiveHandler.IsMultiVolume(archivePath))
            {
                var skipResult = new ExtractResult(archivePath, false, "マルチボリュームアーカイブは未対応です");
                _results.Add(skipResult);
                LogLine($"  → [未対応] マルチボリュームアーカイブ");
                Interlocked.Increment(ref _processedCount);
                continue;
            }

            // 展開先フォルダを決定
            var outputDir = ResolveOutputDirectory(archivePath);
            if (outputDir == null)
            {
                // Skip ポリシーで既存フォルダがある場合
                var skipResult = new ExtractResult(archivePath, true, "スキップ（既存フォルダ）");
                _results.Add(skipResult);
                LogLine($"  → [スキップ] 展開先フォルダが既に存在します");
                Interlocked.Increment(ref _processedCount);
                continue;
            }

            // 展開実行
            var result = await _archiveHandler.ExtractAsync(archivePath, outputDir, itemProgress, ct);
            _results.Add(result);

            if (result.Success)
            {
                LogLine($"  → [成功] {outputDir}");

                // 二重ラップ平坦化
                if (_settings.FlattenSingleRoot)
                {
                    FlattenSingleRoot(outputDir);
                }
            }
            else
            {
                LogLine($"  → [失敗] {result.ErrorMessage}");

                // 展開失敗時、空の出力フォルダが残っていれば削除する
                // （Directory.CreateDirectory で作られた空フォルダのクリーンアップ）
                CleanupEmptyDirectory(outputDir);
            }

            Interlocked.Increment(ref _processedCount);
        }

        LogLine("");
        LogLine($"=== 展開完了: 成功 {_results.Count(r => r.Success)}, 失敗 {FailureCount} ===");

        // ログファイルを出力
        WriteLogFile();

        return _results.ToList().AsReadOnly();
    }

    /// <summary>
    /// フォルダ内のアーカイブファイルを列挙してキューに追加する。
    /// 設定に応じてサブフォルダも再帰的に探索する。
    /// </summary>
    /// <param name="directoryPath">探索するフォルダのパス</param>
    private void EnqueueFromDirectory(string directoryPath)
    {
        // ログファイルの出力先を最初に渡されたフォルダに設定
        _logFilePath ??= Path.Combine(directoryPath,
            $"SendToExtract_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var searchOption = _settings.RecurseSubfolders
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        try
        {
            var files = Directory.GetFiles(directoryPath, "*", searchOption);
            foreach (var file in files)
            {
                EnqueueFile(file);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            LogLine($"[エラー] フォルダへのアクセスが拒否されました: {directoryPath} ({ex.Message})");
        }
    }

    /// <summary>
    /// 単一ファイルをアーカイブとして判定し、キューに追加する。
    /// </summary>
    /// <param name="filePath">追加するファイルパス</param>
    private void EnqueueFile(string filePath)
    {
        if (ArchiveHandler.IsArchive(filePath, _settings.Extensions))
        {
            _queue.Enqueue(filePath);
            Interlocked.Increment(ref _totalCount);

            // ログファイルの出力先を最初のファイルの親フォルダに設定
            _logFilePath ??= Path.Combine(
                Path.GetDirectoryName(filePath) ?? Path.GetTempPath(),
                $"SendToExtract_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }
    }

    /// <summary>
    /// アーカイブの展開先ディレクトリを決定する。
    /// 衝突ポリシーに基づいて既存フォルダとの競合を解決する。
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>
    /// 展開先ディレクトリのパス。
    /// Skip ポリシーで既存フォルダがある場合は null を返す。
    /// </returns>
    private string? ResolveOutputDirectory(string archivePath)
    {
        var parentDir = Path.GetDirectoryName(archivePath) ?? ".";
        var baseName = ArchiveHandler.RemoveArchiveExtension(Path.GetFileName(archivePath));
        var outputDir = Path.Combine(parentDir, baseName);

        if (!Directory.Exists(outputDir))
        {
            // フォルダが存在しなければそのまま使用
            return outputDir;
        }

        // 衝突ポリシーに基づいて処理
        switch (_settings.CollisionPolicy)
        {
            case CollisionPolicy.Skip:
                // 何もしない
                return null;

            case CollisionPolicy.Rename:
                // "name (2)", "name (3)" ... と連番を付与して未使用の名前を見つける
                for (int i = 2; i < 10000; i++)
                {
                    var renamed = Path.Combine(parentDir, $"{baseName} ({i})");
                    if (!Directory.Exists(renamed))
                    {
                        return renamed;
                    }
                }
                // 10000回試して見つからなければ（ありえないが）タイムスタンプ付き
                return Path.Combine(parentDir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}");

            case CollisionPolicy.Overwrite:
                // 既存フォルダにそのまま上書き
                return outputDir;

            default:
                return outputDir;
        }
    }

    /// <summary>
    /// 二重ラップ平坦化。
    /// 展開後の出力フォルダ直下が「単一フォルダのみ」で他にファイルが無い場合、
    /// その中身を1階層上げてラップを解消する。
    /// 例: name/name/... → name/...
    /// </summary>
    /// <param name="outputDir">展開先フォルダのパス</param>
    private void FlattenSingleRoot(string outputDir)
    {
        try
        {
            var entries = Directory.GetFileSystemEntries(outputDir);

            // 直下に1つのフォルダのみが存在する場合
            if (entries.Length == 1 && Directory.Exists(entries[0]))
            {
                var singleSubDir = entries[0];
                var subEntries = Directory.GetFileSystemEntries(singleSubDir);

                // 一時フォルダに中身を移動してから元フォルダに戻す
                // （同名フォルダの移動は直接できないため）
                var tempDir = outputDir + "_flatten_temp_" + Guid.NewGuid().ToString("N")[..8];

                try
                {
                    // サブフォルダをリネーム → 一時フォルダ
                    Directory.Move(singleSubDir, tempDir);

                    // 一時フォルダの中身を元のフォルダに移動
                    foreach (var entry in Directory.GetFileSystemEntries(tempDir))
                    {
                        var destName = Path.GetFileName(entry);
                        var destPath = Path.Combine(outputDir, destName);
                        if (Directory.Exists(entry))
                        {
                            Directory.Move(entry, destPath);
                        }
                        else
                        {
                            File.Move(entry, destPath);
                        }
                    }

                    // 一時フォルダを削除
                    Directory.Delete(tempDir, false);

                    LogLine($"  → [平坦化] 単一ルートフォルダを解消しました");
                }
                catch (Exception ex)
                {
                    LogLine($"  → [平坦化エラー] {ex.Message}");
                    // 失敗しても展開自体は成功しているので続行
                }
            }
        }
        catch
        {
            // 平坦化処理の例外は無視（展開は成功している）
        }
    }

    /// <summary>
    /// 展開失敗時に作成された空の出力フォルダを削除する。
    /// フォルダが空でない場合（一部ファイルが展開済み）は削除しない。
    /// </summary>
    /// <param name="dirPath">削除対象のディレクトリパス</param>
    private void CleanupEmptyDirectory(string dirPath)
    {
        try
        {
            if (Directory.Exists(dirPath))
            {
                // フォルダが空の場合のみ削除
                var hasEntries = Directory.EnumerateFileSystemEntries(dirPath).Any();
                if (!hasEntries)
                {
                    Directory.Delete(dirPath);
                    LogLine($"  → [削除] 空の出力フォルダを削除しました");
                }
            }
        }
        catch
        {
            // クリーンアップ失敗は無視（本体処理には影響しない）
        }
    }

    /// <summary>
    /// ログに1行追記する。
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    private void LogLine(string message)
    {
        _logBuilder.AppendLine(message);
    }

    /// <summary>
    /// ログファイルを出力する。
    /// 展開元フォルダに出力し、失敗時は %TEMP% にフォールバック。
    /// </summary>
    private void WriteLogFile()
    {
        if (_logBuilder.Length == 0) return;

        // ログファイルパスのフォールバック
        var logPath = _logFilePath
            ?? Path.Combine(Path.GetTempPath(), "SendToExtract",
                $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        try
        {
            var logDir = Path.GetDirectoryName(logPath);
            if (logDir != null) Directory.CreateDirectory(logDir);
            File.WriteAllText(logPath, _logBuilder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // ログ出力失敗は最終手段として %TEMP% に書く
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), "SendToExtract",
                    $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
                File.WriteAllText(tempPath, _logBuilder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // どうしても書けなければ諦める
            }
        }
    }

    /// <summary>
    /// ログファイルのパスを取得する。
    /// 展開完了後に「ログを開く」ボタンで使用する。
    /// </summary>
    /// <returns>ログファイルのパス</returns>
    public string? GetLogFilePath() => _logFilePath;
}

/// <summary>
/// 全体の展開進捗情報。
/// </summary>
/// <param name="Processed">処理済みアーカイブ数</param>
/// <param name="Total">総アーカイブ数</param>
/// <param name="CurrentArchive">現在処理中のアーカイブ名</param>
/// <param name="Status">現在の状態</param>
public record OverallProgress(int Processed, int Total, string CurrentArchive, OverallStatus Status);

/// <summary>
/// 全体進捗のステータス。
/// </summary>
public enum OverallStatus
{
    /// <summary>処理中</summary>
    Processing,
    /// <summary>全て完了</summary>
    Completed,
    /// <summary>キャンセル済み</summary>
    Cancelled
}
