using System.Diagnostics;

namespace SendToExtract;

/// <summary>
/// 展開処理の進捗を表示する WinForms ウィンドウ。
/// 全体プログレスバー、個別プログレスバー、ログ一覧、キャンセルボタンを備える。
///
/// スレッドモデル:
/// - 展開処理は Task.Run でバックグラウンド実行
/// - 進捗は IProgress / Control.Invoke でUIスレッドへ通知
/// - UIをブロックしない
/// </summary>
public class ProgressForm : Form
{
    // === UIコントロール ===

    /// <summary>全体の進捗を表示するプログレスバー（処理済み / 総アーカイブ数）</summary>
    private readonly ProgressBar _overallProgressBar;

    /// <summary>個別アーカイブの展開進捗バー（エントリ単位、取得不可ならマーキー）</summary>
    private readonly ProgressBar _itemProgressBar;

    /// <summary>「処理済み / 総数」を表示するラベル</summary>
    private readonly Label _overallLabel;

    /// <summary>現在展開中のアーカイブ名を表示するラベル</summary>
    private readonly Label _currentArchiveLabel;

    /// <summary>展開ログを逐次表示するリストビュー</summary>
    private readonly ListView _logListView;

    /// <summary>キャンセルボタン</summary>
    private readonly Button _cancelButton;

    /// <summary>「ログを開く」ボタン（失敗時のみ表示）</summary>
    private readonly Button _openLogButton;

    /// <summary>「フォルダを開く」ボタン（完了時に表示）</summary>
    private readonly Button _openFolderButton;

    /// <summary>「閉じる」ボタン（完了時に表示）</summary>
    private readonly Button _closeButton;

    // === ロジック ===

    /// <summary>キャンセルトークンソース</summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>展開サービス</summary>
    private readonly ExtractorService _extractorService;

    /// <summary>アプリケーション設定</summary>
    private readonly Settings _settings;

    /// <summary>最初に渡されたフォルダパス（「フォルダを開く」用）</summary>
    private string? _targetFolder;

    /// <summary>
    /// ProgressForm のコンストラクタ。UIコントロールを初期化する。
    /// </summary>
    /// <param name="extractorService">展開サービス（キュー済み）</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="targetFolder">展開対象のフォルダパス</param>
    public ProgressForm(ExtractorService extractorService, Settings settings, string? targetFolder)
    {
        _extractorService = extractorService;
        _settings = settings;
        _targetFolder = targetFolder;

        // === フォーム基本設定 ===
        Text = $"展開中 - SendToExtract v{AppInfo.Version}";
        Size = new Size(620, 480);
        MinimumSize = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // === レイアウト構築 ===

        // 全体進捗ラベル
        _overallLabel = new Label
        {
            Text = "準備中...",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 8, 0),
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
        };

        // 全体プログレスバー
        _overallProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 24,
            Margin = new Padding(8),
            Style = ProgressBarStyle.Continuous
        };

        // 全体進捗バー用パネル（パディング付き）
        var overallPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(8, 4, 8, 4)
        };
        _overallProgressBar.Dock = DockStyle.Fill;
        overallPanel.Controls.Add(_overallProgressBar);

        // 現在のアーカイブ名ラベル
        _currentArchiveLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(8, 0, 8, 0),
            ForeColor = Color.DarkBlue,
            AutoEllipsis = true
        };

        // 個別プログレスバー
        _itemProgressBar = new ProgressBar
        {
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };

        // 個別進捗バー用パネル
        var itemPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(8, 0, 8, 4)
        };
        _itemProgressBar.Dock = DockStyle.Fill;
        itemPanel.Controls.Add(_itemProgressBar);

        // ログ一覧（ListView）
        _logListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _logListView.Columns.Add("状態", 60);
        _logListView.Columns.Add("ファイル名", 240);
        _logListView.Columns.Add("詳細", 280);

        // ログ一覧用パネル（マージン付き）
        var logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 4)
        };
        logPanel.Controls.Add(_logListView);

        // --- ボタンパネル ---
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
            WrapContents = false
        };

        // キャンセルボタン
        _cancelButton = new Button
        {
            Text = "キャンセル",
            Width = 100,
            Height = 32
        };
        _cancelButton.Click += OnCancelClick;

        // 閉じるボタン（初期は非表示）
        _closeButton = new Button
        {
            Text = "閉じる",
            Width = 100,
            Height = 32,
            Visible = false
        };
        _closeButton.Click += (_, _) => Close();

        // ログを開くボタン（初期は非表示）
        _openLogButton = new Button
        {
            Text = "ログを開く",
            Width = 100,
            Height = 32,
            Visible = false
        };
        _openLogButton.Click += OnOpenLogClick;

        // フォルダを開くボタン（初期は非表示）
        _openFolderButton = new Button
        {
            Text = "フォルダを開く",
            Width = 110,
            Height = 32,
            Visible = false
        };
        _openFolderButton.Click += OnOpenFolderClick;

        // ボタンを右から順に配置
        buttonPanel.Controls.AddRange(new Control[]
        {
            _closeButton, _cancelButton, _openLogButton, _openFolderButton
        });

        // === フォームにコントロールを追加（下から順に追加 = 上から順に表示） ===
        Controls.Add(logPanel);
        Controls.Add(itemPanel);
        Controls.Add(_currentArchiveLabel);
        Controls.Add(overallPanel);
        Controls.Add(_overallLabel);
        Controls.Add(buttonPanel);

        // フォーム表示後に展開処理を開始
        Shown += async (_, _) => await StartExtractionAsync();
    }

    /// <summary>
    /// 展開処理をバックグラウンドで開始する。
    /// IProgress を使ってUI更新を行い、UIスレッドをブロックしない。
    /// </summary>
    private async Task StartExtractionAsync()
    {
        // 全体進捗の通知ハンドラ
        var overallProgress = new Progress<OverallProgress>(p =>
        {
            // UIスレッドで実行される
            _overallLabel.Text = $"展開中: {p.Processed + 1} / {p.Total}";
            _currentArchiveLabel.Text = p.CurrentArchive;

            _overallProgressBar.Maximum = Math.Max(p.Total, 1);
            _overallProgressBar.Value = Math.Min(p.Processed, p.Total);

            // 個別プログレスバーをリセット（新しいアーカイブ開始）
            _itemProgressBar.Style = ProgressBarStyle.Marquee;
        });

        // 個別アーカイブの進捗通知ハンドラ
        var itemProgress = new Progress<ExtractProgress>(p =>
        {
            if (p.TotalEntries > 0)
            {
                // エントリ総数が分かる場合は確定プログレスバー
                _itemProgressBar.Style = ProgressBarStyle.Continuous;
                _itemProgressBar.Maximum = p.TotalEntries;
                _itemProgressBar.Value = Math.Min(p.ProcessedEntries, p.TotalEntries);
            }
            else
            {
                // 不明な場合はマーキー表示
                _itemProgressBar.Style = ProgressBarStyle.Marquee;
            }
        });

        try
        {
            // バックグラウンドで展開を実行
            var results = await _extractorService.ExtractAllAsync(
                overallProgress, itemProgress, _cts.Token);

            // 結果をログ一覧に表示
            foreach (var result in results)
            {
                AddLogEntry(result);
            }

            // 完了処理
            OnExtractionCompleted();
        }
        catch (OperationCanceledException)
        {
            // キャンセル時
            _overallLabel.Text = "キャンセルされました";
            _currentArchiveLabel.Text = "";
            _itemProgressBar.Style = ProgressBarStyle.Continuous;
            _itemProgressBar.Value = 0;
            ShowCompletionButtons(hasFailures: true);
        }
        catch (Exception ex)
        {
            // 予期しないエラー
            _overallLabel.Text = $"エラー: {ex.Message}";
            ShowCompletionButtons(hasFailures: true);
        }
    }

    /// <summary>
    /// 展開完了時の処理。成功/失敗に応じてUIを更新し、
    /// 自動クローズまたはボタン表示を行う。
    /// </summary>
    private void OnExtractionCompleted()
    {
        var failureCount = _extractorService.FailureCount;
        var totalCount = _extractorService.TotalCount;

        _overallProgressBar.Value = _overallProgressBar.Maximum;
        _itemProgressBar.Style = ProgressBarStyle.Continuous;
        _itemProgressBar.Value = _itemProgressBar.Maximum > 0 ? _itemProgressBar.Maximum : 0;
        _currentArchiveLabel.Text = "";

        if (failureCount == 0)
        {
            // 全成功 → 閉じるボタンで終了（自動クローズしない）
            _overallLabel.Text = $"完了: {totalCount} 個のアーカイブを展開しました";
            Text = $"完了 - SendToExtract v{AppInfo.Version}";
            ShowCompletionButtons(hasFailures: false);
        }
        else
        {
            // 失敗あり
            _overallLabel.Text = $"完了: 成功 {totalCount - failureCount} / 失敗 {failureCount}";
            Text = $"完了（失敗あり） - SendToExtract v{AppInfo.Version}";
            ShowCompletionButtons(hasFailures: true);
        }
    }

    /// <summary>
    /// 完了時のボタン表示を切り替える。
    /// キャンセルボタンを非表示にし、閉じる/ログを開く/フォルダを開くボタンを表示する。
    /// </summary>
    /// <param name="hasFailures">失敗があったか</param>
    private void ShowCompletionButtons(bool hasFailures)
    {
        _cancelButton.Visible = false;
        _closeButton.Visible = true;
        _openFolderButton.Visible = true;
        _openLogButton.Visible = hasFailures;
    }

    /// <summary>
    /// ログ一覧に展開結果のエントリを追加する。
    /// </summary>
    /// <param name="result">展開結果</param>
    private void AddLogEntry(ExtractResult result)
    {
        var fileName = Path.GetFileName(result.ArchivePath);
        var status = result.Success ? "成功" : "失敗";
        var detail = result.ErrorMessage ?? "";

        var item = new ListViewItem(status);
        item.SubItems.Add(fileName);
        item.SubItems.Add(detail);

        // 成功は緑系、失敗は赤系で色分け
        if (result.Success)
        {
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                // スキップ等
                item.BackColor = Color.LightYellow;
                item.SubItems[0].Text = "スキップ";
            }
            else
            {
                item.BackColor = Color.Honeydew;
            }
        }
        else
        {
            item.BackColor = Color.MistyRose;
        }

        _logListView.Items.Add(item);

        // 最新のエントリが見えるようにスクロール
        _logListView.EnsureVisible(_logListView.Items.Count - 1);
    }

    /// <summary>
    /// キャンセルボタンのクリックハンドラ。
    /// 展開処理をキャンセルする。
    /// </summary>
    private void OnCancelClick(object? sender, EventArgs e)
    {
        _cancelButton.Enabled = false;
        _cancelButton.Text = "中断中...";
        _cts.Cancel();
    }

    /// <summary>
    /// 「ログを開く」ボタンのクリックハンドラ。
    /// ログファイルをデフォルトのテキストエディタで開く。
    /// </summary>
    private void OnOpenLogClick(object? sender, EventArgs e)
    {
        var logPath = _extractorService.GetLogFilePath();
        if (logPath != null && File.Exists(logPath))
        {
            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show("ログファイルが見つかりません。", "SendToExtract",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// 「フォルダを開く」ボタンのクリックハンドラ。
    /// 展開先のフォルダをエクスプローラーで開く。
    /// </summary>
    private void OnOpenFolderClick(object? sender, EventArgs e)
    {
        if (_targetFolder != null && Directory.Exists(_targetFolder))
        {
            Process.Start(new ProcessStartInfo(_targetFolder) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// フォームが閉じられる際に自動クローズタイマーを停止し、
    /// キャンセルトークンを破棄する。
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        _cts.Dispose();

        base.OnFormClosing(e);
    }

    /// <summary>
    /// パスワード入力ダイアログを表示する。
    /// UIスレッドから呼ばれることを前提とする。
    /// バックグラウンドスレッドから呼ぶ場合は Invoke で委譲すること。
    /// </summary>
    /// <param name="archiveName">パスワードが必要なアーカイブのファイル名</param>
    /// <returns>入力されたパスワード。キャンセル時は null。</returns>
    public string? ShowPasswordDialog(string archiveName)
    {
        // UIスレッドで実行されるよう Invoke で委譲
        if (InvokeRequired)
        {
            return (string?)Invoke(() => ShowPasswordDialog(archiveName));
        }

        using var dialog = new Form
        {
            Text = "パスワード入力",
            Size = new Size(400, 170),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new Label
        {
            Text = $"パスワードを入力してください:\n{archiveName}",
            Location = new Point(12, 12),
            Size = new Size(360, 40),
            AutoEllipsis = true
        };

        var textBox = new TextBox
        {
            Location = new Point(12, 56),
            Size = new Size(360, 24),
            PasswordChar = '*',
            UseSystemPasswordChar = true
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(216, 92),
            Size = new Size(75, 28)
        };

        var cancelBtn = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(297, 92),
            Size = new Size(75, 28)
        };

        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelBtn;
        dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelBtn });

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            return textBox.Text;
        }

        return null;
    }

    /// <summary>
    /// 後発プロセスからパイプ経由で受け取った追加パスをキューに追加する。
    /// UIスレッドから呼ばれることを想定（Invoke 委譲済み）。
    /// </summary>
    /// <param name="path">追加するパス</param>
    public void AddPathFromPipe(string path)
    {
        if (InvokeRequired)
        {
            Invoke(() => AddPathFromPipe(path));
            return;
        }

        _extractorService.AddFile(path);

        // 全体進捗の表示を更新
        _overallLabel.Text = $"展開中: {_extractorService.ProcessedCount} / {_extractorService.TotalCount}";
        _overallProgressBar.Maximum = Math.Max(_extractorService.TotalCount, 1);
    }
}
