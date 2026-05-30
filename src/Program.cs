using System.IO.Pipes;
using System.Text;

namespace SendToExtract;

/// <summary>
/// SendToExtract アプリケーションのエントリポイント。
///
/// 起動経路:
/// 1. コマンドライン引数で --install / --uninstall → ショートカット操作して終了
/// 2. 引数なし → 使い方ダイアログを表示して終了
/// 3. フォルダ/ファイルパスが渡された場合 → 展開処理を開始
///
/// 多重起動対策:
/// - 名前付き Mutex で先発/後発を判定
/// - 先発プロセスがサーバ（ProgressForm 表示）
/// - 後発プロセスは Named Pipe で引数を先発に渡して即終了
/// </summary>
internal static class Program
{
    /// <summary>多重起動制御用の Mutex 名</summary>
    private const string MutexName = "Global\\SendToExtract_SingleInstance";

    /// <summary>後発プロセスから先発プロセスへ引数を渡す Named Pipe 名</summary>
    private const string PipeName = "SendToExtract_ArgPipe";

    /// <summary>
    /// アプリケーションのメインエントリポイント。
    /// STAThread 属性は WinForms に必要（COM / クリップボード等）。
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // .NET Core で CP932（Shift_JIS）を使用するために必須
        // SharpCompress が ZIP エントリ名を CP932 で解釈するのに必要
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // WinForms アプリケーションの初期化
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // === 引数解析 ===

        // --install: 「送る」メニューにショートカットを作成
        if (args.Length == 1 && args[0].Equals("--install", StringComparison.OrdinalIgnoreCase))
        {
            HandleInstall();
            return;
        }

        // --uninstall: 「送る」メニューからショートカットを削除
        if (args.Length == 1 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            HandleUninstall();
            return;
        }

        // 引数なし: 使い方ダイアログを表示
        if (args.Length == 0)
        {
            ShowUsageDialog();
            return;
        }

        // === 多重起動制御 ===
        // 名前付き Mutex で先発プロセスかどうかを判定
        using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // 後発プロセス: 引数を先発プロセスに Named Pipe で送信して即終了
            SendArgsToPrimary(args);
            return;
        }

        // 先発プロセス: 展開処理を実行
        RunAsPrimary(args);
    }

    /// <summary>
    /// --install コマンドの処理。
    /// 「送る」メニューにショートカットを作成し、結果をダイアログで表示する。
    /// </summary>
    private static void HandleInstall()
    {
        if (SendToInstaller.Install())
        {
            MessageBox.Show(
                "「送る」メニューに SendToExtract を登録しました。\n\n" +
                "フォルダやアーカイブを右クリック →「送る」→「SendToExtract」で使用できます。",
                "SendToExtract - インストール完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                "「送る」メニューへの登録に失敗しました。",
                "SendToExtract - エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// --uninstall コマンドの処理。
    /// 「送る」メニューからショートカットを削除し、結果をダイアログで表示する。
    /// </summary>
    private static void HandleUninstall()
    {
        if (SendToInstaller.Uninstall())
        {
            MessageBox.Show(
                "「送る」メニューから SendToExtract を削除しました。",
                "SendToExtract - アンインストール完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                "「送る」メニューからの削除に失敗しました。",
                "SendToExtract - エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 引数なしで起動された場合に表示する使い方ダイアログ。
    /// インストール状態も表示する。
    /// </summary>
    private static void ShowUsageDialog()
    {
        var installed = SendToInstaller.IsInstalled();
        var statusText = installed ? "✓ 「送る」メニューに登録済み" : "✗ 「送る」メニューに未登録";

        var message =
            $"SendToExtract - アーカイブ一括展開ツール\n\n" +
            $"使い方:\n" +
            $"  フォルダやアーカイブを右クリック →「送る」→「SendToExtract」\n\n" +
            $"コマンドライン:\n" +
            $"  SendToExtract.exe <フォルダ/ファイル> ...\n" +
            $"  SendToExtract.exe --install     「送る」に登録\n" +
            $"  SendToExtract.exe --uninstall   「送る」から削除\n\n" +
            $"状態: {statusText}\n\n" +
            $"対応形式: zip, tar, tar.gz, tgz, tar.bz2, gz, bz2, 7z, rar";

        var result = MessageBox.Show(
            message + "\n\n「送る」メニューに登録しますか？",
            "SendToExtract",
            installed ? MessageBoxButtons.OK : MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        // 未登録で「はい」が選ばれた場合はインストール
        if (!installed && result == DialogResult.Yes)
        {
            HandleInstall();
        }
    }

    /// <summary>
    /// 先発プロセスとして展開処理を実行する。
    /// Named Pipe サーバを起動し、後発プロセスからの引数を受け付ける。
    /// ProgressForm を表示して展開を行う。
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    private static void RunAsPrimary(string[] args)
    {
        var settings = Settings.Load();

        // 展開対象の最初のフォルダパスを記憶（「フォルダを開く」用）
        string? targetFolder = null;
        foreach (var arg in args)
        {
            if (Directory.Exists(arg))
            {
                targetFolder = arg;
                break;
            }
            else if (File.Exists(arg))
            {
                targetFolder = Path.GetDirectoryName(arg);
                break;
            }
        }

        // ProgressForm を先に作成（パスワードコールバックに使うため）
        ProgressForm? form = null;

        // パスワードコールバック: UIスレッドでダイアログを表示
        string? PasswordCallback(string archiveName)
        {
            if (form != null)
            {
                return form.ShowPasswordDialog(archiveName);
            }
            return null;
        }

        // 展開サービスを作成し、引数からキューを構築
        var service = new ExtractorService(settings, PasswordCallback);
        service.AddPaths(args);

        if (service.TotalCount == 0)
        {
            // 展開対象が見つからなかった場合
            MessageBox.Show(
                "展開対象のアーカイブが見つかりませんでした。\n\n" +
                "対応形式: zip, tar, tar.gz, tgz, tar.bz2, gz, bz2, 7z, rar",
                "SendToExtract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // 進捗ウィンドウを作成
        form = new ProgressForm(service, settings, targetFolder);

        // Named Pipe サーバをバックグラウンドで起動
        // 後発プロセスからの引数を受け付ける
        var pipeServerCts = new CancellationTokenSource();
        _ = Task.Run(() => RunPipeServer(form, service, pipeServerCts.Token));

        // WinForms メッセージループを開始（UIスレッド）
        Application.Run(form);

        // フォームが閉じられたらパイプサーバーも停止
        pipeServerCts.Cancel();
    }

    /// <summary>
    /// Named Pipe サーバを実行する。
    /// 後発プロセスから送信された引数（パス）を受け取り、
    /// 展開サービスのキューと ProgressForm に追加する。
    /// </summary>
    /// <param name="form">進捗ウィンドウ（UIスレッドへの通知用）</param>
    /// <param name="service">展開サービス</param>
    /// <param name="ct">キャンセルトークン</param>
    private static async Task RunPipeServer(
        ProgressForm form,
        ExtractorService service,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Named Pipe サーバインスタンスを作成（1接続ごとに再作成）
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // クライアント接続を待機
                await server.WaitForConnectionAsync(ct);

                // 引数を1行ずつ読み取る
                using var reader = new StreamReader(server, Encoding.UTF8);
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // UIスレッドで展開対象を追加
                        form.AddPathFromPipe(line.Trim());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は正常終了
                break;
            }
            catch
            {
                // パイプエラーは無視して再接続を試みる
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(100, ct);
                }
            }
        }
    }

    /// <summary>
    /// 後発プロセスとして、引数を先発プロセスに Named Pipe で送信する。
    /// 送信完了後、即座にプロセスを終了する。
    /// </summary>
    /// <param name="args">送信するコマンドライン引数</param>
    private static void SendArgsToPrimary(string[] args)
    {
        try
        {
            // Named Pipe クライアントで先発プロセスに接続
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            // 接続タイムアウト: 3秒（先発プロセスが起動しきっていない場合に備える）
            client.Connect(3000);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };

            // 各引数を1行ずつ送信
            foreach (var arg in args)
            {
                writer.WriteLine(arg);
            }
        }
        catch
        {
            // パイプ接続失敗時は独立して処理を実行（フォールバック）
            RunAsPrimary(args);
        }
    }
}
