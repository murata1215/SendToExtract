using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Text;

namespace SendToExtract;

/// <summary>
/// アーカイブの展開処理を担当するクラス。
/// SharpCompress をラップし、形式判定・CP932対応・パスワード処理・進捗通知を行う。
///
/// 使い方:
///   var handler = new ArchiveHandler(passwordCallback);
///   await handler.ExtractAsync(archivePath, outputDir, progress, cancellationToken);
/// </summary>
public class ArchiveHandler
{
    /// <summary>
    /// 二重拡張子の定義。長い拡張子から順にマッチさせる必要がある。
    /// 例: ".tar.gz" は ".gz" より先に判定しなければならない。
    /// </summary>
    private static readonly string[] DoubleExtensions =
    {
        ".tar.gz", ".tar.bz2", ".tar.xz"
    };

    /// <summary>
    /// 対応するすべてのアーカイブ拡張子（二重拡張子を含む）。
    /// 判定時は長い拡張子から順にマッチさせる。
    /// </summary>
    private static readonly string[] AllExtensions =
    {
        ".tar.gz", ".tar.bz2", ".tar.xz",  // 二重拡張子（先に判定）
        ".tgz", ".tbz2", ".txz",            // 二重拡張子の短縮形
        ".zip", ".tar", ".gz", ".bz2",      // 単一拡張子
        ".7z", ".rar"                        // 展開のみ対応
    };

    /// <summary>
    /// ストリーム逐次読み（ReaderFactory）を使用すべき形式。
    /// - 7z / rar: ソリッドアーカイブはランダムアクセス不可
    /// - tar.gz 等: gzip/bzip2/xz 圧縮された tar はストリーム逐次読みが必要
    ///   （Archive API では "Failed to read TAR header" エラーになる）
    /// </summary>
    private static readonly HashSet<string> StreamOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".rar",
        ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz"
    };

    /// <summary>
    /// パスワードが必要な場合に呼ばれるコールバック。
    /// UIスレッドでダイアログを表示し、ユーザーが入力したパスワードを返す。
    /// null を返した場合はキャンセル扱い。
    /// </summary>
    private readonly Func<string, string?>? _passwordCallback;

    /// <summary>セッション内で使い回すパスワード（前回入力されたもの）</summary>
    private string? _cachedPassword;

    /// <summary>
    /// ArchiveHandler のコンストラクタ。
    /// </summary>
    /// <param name="passwordCallback">
    /// パスワード要求時のコールバック関数。
    /// 引数はアーカイブファイル名、戻り値はパスワード（nullでキャンセル）。
    /// </param>
    public ArchiveHandler(Func<string, string?>? passwordCallback = null)
    {
        _passwordCallback = passwordCallback;
    }

    /// <summary>
    /// ファイル名からアーカイブ拡張子を取得する。
    /// 二重拡張子（.tar.gz 等）を正しく認識する。
    /// </summary>
    /// <param name="fileName">ファイル名（パスでも可）</param>
    /// <returns>認識された拡張子（小文字）。該当なしの場合は空文字列。</returns>
    public static string GetArchiveExtension(string fileName)
    {
        var name = Path.GetFileName(fileName).ToLowerInvariant();

        // 二重拡張子を先にチェック（長い拡張子が優先）
        foreach (var ext in AllExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return ext;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// ファイル名からアーカイブ拡張子を除去した名前を返す。
    /// 展開先フォルダ名の生成に使用する。
    /// 例: "batch-log_20221201.tar.gz" → "batch-log_20221201"
    /// </summary>
    /// <param name="fileName">ファイル名（パスではなくファイル名部分）</param>
    /// <returns>拡張子を除去した名前</returns>
    public static string RemoveArchiveExtension(string fileName)
    {
        var ext = GetArchiveExtension(fileName);
        if (!string.IsNullOrEmpty(ext))
        {
            return fileName[..^ext.Length];
        }

        // 認識できない拡張子の場合は Path.GetFileNameWithoutExtension を使う
        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>
    /// 指定されたファイルが対応アーカイブ形式かどうかを判定する。
    /// 拡張子で一次判定し、該当しない場合はマジックバイト判定にフォールバック。
    /// </summary>
    /// <param name="filePath">判定対象のファイルパス</param>
    /// <param name="allowedExtensions">許可する拡張子リスト（設定から取得）</param>
    /// <returns>アーカイブとして処理可能なら true</returns>
    public static bool IsArchive(string filePath, IEnumerable<string> allowedExtensions)
    {
        var ext = GetArchiveExtension(filePath);

        // 拡張子による一次判定
        if (!string.IsNullOrEmpty(ext))
        {
            return allowedExtensions.Any(e =>
                string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
        }

        // 拡張子で判定できない場合はマジックバイト判定にフォールバック
        try
        {
            using var stream = File.OpenRead(filePath);
            // SharpCompress の ArchiveFactory がマジックバイトで形式を判定する
            using var archive = ArchiveFactory.OpenArchive(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// マルチボリューム（分割）アーカイブかどうかを簡易判定する。
    /// 分割 rar（.part1.rar, .r00 等）や分割 7z（.7z.001 等）を検出する。
    /// 初版ではスコープ外のため、検出時はスキップする。
    /// </summary>
    /// <param name="filePath">判定対象のファイルパス</param>
    /// <returns>マルチボリュームの可能性がある場合 true</returns>
    public static bool IsMultiVolume(string filePath)
    {
        var name = Path.GetFileName(filePath).ToLowerInvariant();

        // 分割 rar: .part2.rar, .part3.rar, ... または .r00, .r01, ...
        // ※ .part1.rar は最初のボリュームなので処理対象にする場合もあるが、
        //   初版では全てスキップする
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.part[2-9]\d*\.rar$"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.r\d{2,}$"))
            return true;

        // 分割 7z: .7z.002, .7z.003, ...
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.7z\.\d{3}$") &&
            !name.EndsWith(".7z.001"))
            return true;

        return false;
    }

    /// <summary>
    /// アーカイブを指定ディレクトリに展開する。
    /// 形式に応じて Archive API または Reader API を使い分ける。
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputDir">展開先ディレクトリ</param>
    /// <param name="progress">進捗通知（null可）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>展開結果</returns>
    public async Task<ExtractResult> ExtractAsync(
        string archivePath,
        string outputDir,
        IProgress<ExtractProgress>? progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var ext = GetArchiveExtension(archivePath);
                Directory.CreateDirectory(outputDir);

                // 形式に応じて Archive API / Reader API を使い分ける
                if (StreamOnlyExtensions.Contains(ext))
                {
                    // 7z / rar はソリッドアーカイブ対応のため Reader API を使用
                    ExtractWithReader(archivePath, outputDir, progress, ct);
                }
                else
                {
                    // zip, tar, tar.gz 等は Archive API を使用（ランダムアクセス可能）
                    ExtractWithArchive(archivePath, outputDir, progress, ct);
                }

                return new ExtractResult(archivePath, true, null);
            }
            catch (OperationCanceledException)
            {
                return new ExtractResult(archivePath, false, "キャンセルされました");
            }
            catch (InvalidFormatException ex)
            {
                return new ExtractResult(archivePath, false, $"未対応の形式: {ex.Message}");
            }
            catch (CryptographicException)
            {
                return new ExtractResult(archivePath, false, "パスワードが正しくないか、入力がキャンセルされました");
            }
            catch (Exception ex)
            {
                return new ExtractResult(archivePath, false, $"展開エラー: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>
    /// Archive API を使用してアーカイブを展開する。
    /// zip, tar, tar.gz, tar.bz2, gz, bz2 等のランダムアクセス可能な形式向け。
    /// </summary>
    private void ExtractWithArchive(
        string archivePath,
        string outputDir,
        IProgress<ExtractProgress>? progress,
        CancellationToken ct)
    {
        // CP932 エンコーディング設定（日本語ファイル名の文字化け対策）
        var readerOptions = new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding
            {
                Default = Encoding.GetEncoding(932)  // CP932（Shift_JIS）
            },
            // パスワードが必要な場合のコールバック
            Password = _cachedPassword
        };

        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.OpenArchive(stream, readerOptions);

        // パスワード付きアーカイブの判定
        // SharpCompress では暗号化されたエントリが存在するかチェック
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        var totalEntries = entries.Count;
        var processedEntries = 0;

        // パスワードが必要で、まだ設定されていない場合
        if (entries.Any(e => e.IsEncrypted) && string.IsNullOrEmpty(_cachedPassword))
        {
            var password = RequestPassword(archivePath);
            if (password == null)
            {
                throw new System.Security.Cryptography.CryptographicException("パスワード入力がキャンセルされました");
            }
            _cachedPassword = password;

            // パスワードを設定して再オープン
            stream.Position = 0;
            readerOptions.Password = _cachedPassword;
            using var archive2 = ArchiveFactory.OpenArchive(stream, readerOptions);
            var entries2 = archive2.Entries.Where(e => !e.IsDirectory).ToList();
            foreach (var entry in entries2)
            {
                ct.ThrowIfCancellationRequested();
                var destPath = SanitizeEntryPath(entry.Key, outputDir);
                if (destPath != null)
                {
                    using var entryStream = entry.OpenEntryStream();
                    WriteStreamToFile(entryStream, destPath);
                }
                processedEntries++;
                progress?.Report(new ExtractProgress(processedEntries, totalEntries, entry.Key ?? ""));
            }
            return;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            // エントリパスをサニタイズして安全な出力先を生成
            var destPath = SanitizeEntryPath(entry.Key, outputDir);
            if (destPath != null)
            {
                using var entryStream = entry.OpenEntryStream();
                WriteStreamToFile(entryStream, destPath);
            }

            processedEntries++;
            progress?.Report(new ExtractProgress(processedEntries, totalEntries, entry.Key ?? ""));
        }
    }

    /// <summary>
    /// Reader API を使用してアーカイブを展開する。
    /// 7z / rar のソリッドアーカイブなど、ストリーム逐次読みが必要な形式向け。
    /// Reader API はエントリを順次読み出すため、総数は事前に不明。
    ///
    /// 注意: Archive API でストリームを開くと、dispose 時にストリームが閉じられるため、
    /// エントリ数の事前チェックには別のストリームを使用する。
    /// </summary>
    private void ExtractWithReader(
        string archivePath,
        string outputDir,
        IProgress<ExtractProgress>? progress,
        CancellationToken ct)
    {
        var readerOptions = new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding
            {
                Default = Encoding.GetEncoding(932)
            },
            Password = _cachedPassword
        };

        // まず別ストリームで Archive API を使い、パスワード要否と総数を確認する。
        // ArchiveFactory.OpenArchive は dispose 時にストリームを閉じるため、
        // 展開用のストリームとは分離する必要がある。
        bool needsPassword = false;
        int totalEntries = -1; // 不明の場合は -1

        try
        {
            // チェック専用のストリームを開く（using で確実に閉じる）
            using var checkStream = File.OpenRead(archivePath);
            using var archiveCheck = ArchiveFactory.OpenArchive(checkStream, readerOptions);
            var checkEntries = archiveCheck.Entries.Where(e => !e.IsDirectory).ToList();
            totalEntries = checkEntries.Count;
            needsPassword = checkEntries.Any(e => e.IsEncrypted);
        }
        catch
        {
            // Archive API で開けない場合は Reader のみで処理（総数不明）
        }

        // パスワード処理
        if (needsPassword && string.IsNullOrEmpty(_cachedPassword))
        {
            var password = RequestPassword(archivePath);
            if (password == null)
            {
                throw new System.Security.Cryptography.CryptographicException("パスワード入力がキャンセルされました");
            }
            _cachedPassword = password;
            readerOptions.Password = _cachedPassword;
        }

        // 展開用に新しいストリームを開き、Reader API で展開
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream, readerOptions);
        var processed = 0;

        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            if (!reader.Entry.IsDirectory)
            {
                // エントリパスをサニタイズして安全な出力先を生成
                // tar の絶対パス（/var/log/...）や ../ を含むパスに対応
                var destPath = SanitizeEntryPath(reader.Entry.Key, outputDir);
                if (destPath != null)
                {
                    using var entryStream = reader.OpenEntryStream();
                    WriteStreamToFile(entryStream, destPath);
                }

                processed++;
                progress?.Report(new ExtractProgress(processed, totalEntries, reader.Entry.Key ?? ""));
            }
        }
    }

    /// <summary>
    /// エントリのパスをサニタイズして安全な出力先パスを生成する。
    /// tar 等で絶対パス（/var/log/...）や相対パス（../...）が含まれる場合に
    /// 展開先ディレクトリの外に書き出されるのを防ぐ（Zip Slip 対策）。
    /// </summary>
    /// <param name="entryKey">アーカイブエントリのキー（パス）</param>
    /// <param name="outputDir">展開先ディレクトリ</param>
    /// <returns>安全な出力先フルパス。無効な場合は null。</returns>
    private static string? SanitizeEntryPath(string? entryKey, string outputDir)
    {
        if (string.IsNullOrWhiteSpace(entryKey)) return null;

        // 先頭の / \ を除去（絶対パス → 相対パスに変換）
        var sanitized = entryKey.TrimStart('/', '\\');

        // ".." を含むパスコンポーネントを除去
        var parts = sanitized.Split('/', '\\');
        var safeParts = parts.Where(p => p != ".." && !string.IsNullOrEmpty(p)).ToArray();
        if (safeParts.Length == 0) return null;

        var relativePath = Path.Combine(safeParts);
        var fullPath = Path.GetFullPath(Path.Combine(outputDir, relativePath));

        // 最終確認: 出力先ディレクトリ配下であることを検証
        var normalizedOutputDir = Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedOutputDir, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fullPath;
    }

    /// <summary>
    /// ストリームからファイルに書き出す。親ディレクトリが無ければ作成する。
    /// </summary>
    /// <param name="source">書き出し元ストリーム</param>
    /// <param name="destPath">書き出し先ファイルパス</param>
    private static void WriteStreamToFile(Stream source, string destPath)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (dir != null) Directory.CreateDirectory(dir);

        using var destStream = File.Create(destPath);
        source.CopyTo(destStream);
    }

    /// <summary>
    /// パスワード入力をコールバック経由でリクエストする。
    /// まずキャッシュ済みパスワードを返し、無ければコールバックを呼ぶ。
    /// </summary>
    /// <param name="archivePath">パスワードが必要なアーカイブのパス</param>
    /// <returns>パスワード文字列。キャンセル時は null。</returns>
    private string? RequestPassword(string archivePath)
    {
        // キャッシュがあればまずそれを返す（前回入力のパスワードを使い回し）
        if (!string.IsNullOrEmpty(_cachedPassword))
        {
            return _cachedPassword;
        }

        // コールバックが無ければパスワード入力不可
        if (_passwordCallback == null)
        {
            return null;
        }

        var fileName = Path.GetFileName(archivePath);
        return _passwordCallback(fileName);
    }
}

/// <summary>
/// 個別エントリの展開進捗情報。
/// </summary>
/// <param name="ProcessedEntries">処理済みエントリ数</param>
/// <param name="TotalEntries">総エントリ数（-1 = 不明）</param>
/// <param name="CurrentEntry">現在処理中のエントリ名</param>
public record ExtractProgress(int ProcessedEntries, int TotalEntries, string CurrentEntry);

/// <summary>
/// 1つのアーカイブの展開結果。
/// </summary>
/// <param name="ArchivePath">アーカイブファイルのパス</param>
/// <param name="Success">成功したか</param>
/// <param name="ErrorMessage">失敗時のエラーメッセージ（成功時は null）</param>
public record ExtractResult(string ArchivePath, bool Success, string? ErrorMessage);
