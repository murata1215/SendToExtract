namespace SendToExtract;

/// <summary>
/// 展開対象のアーカイブファイルを選択するフォーム。
/// 「送る」で渡されたフォルダ内のアーカイブを一覧表示し、
/// ユーザーがチェックボックスで展開するファイルを選択する。
///
/// フロー:
///   「送る」→ SelectionForm（ここ）→ 選択確定 → ProgressForm で展開
/// </summary>
public class SelectionForm : Form
{
    /// <summary>アーカイブ一覧（チェックボックス付き）</summary>
    private readonly CheckedListBox _fileList;

    /// <summary>対象フォルダのパス表示ラベル</summary>
    private readonly Label _folderLabel;

    /// <summary>選択数カウント表示ラベル</summary>
    private readonly Label _countLabel;

    /// <summary>全選択ボタン</summary>
    private readonly Button _selectAllButton;

    /// <summary>全解除ボタン</summary>
    private readonly Button _deselectAllButton;

    /// <summary>展開ボタン</summary>
    private readonly Button _extractButton;

    /// <summary>閉じるボタン</summary>
    private readonly Button _closeButton;

    /// <summary>アーカイブファイルのフルパスリスト（CheckedListBox のインデックスと対応）</summary>
    private readonly List<string> _archivePaths;

    /// <summary>対象フォルダのパス</summary>
    private readonly string? _folderPath;

    /// <summary>ユーザーが選択したファイルパス（展開ボタン押下後にセットされる）</summary>
    public List<string>? SelectedFiles { get; private set; }

    /// <summary>
    /// SelectionForm のコンストラクタ。
    /// </summary>
    /// <param name="archivePaths">フォルダ内で見つかったアーカイブファイルのフルパスリスト</param>
    /// <param name="folderPath">対象フォルダのパス（ラベル表示用）</param>
    public SelectionForm(List<string> archivePaths, string? folderPath)
    {
        _archivePaths = archivePaths;
        _folderPath = folderPath;

        // === フォーム基本設定 ===
        Text = "SendToExtract - ファイル選択";
        Size = new Size(580, 440);
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // === 対象フォルダ表示 ===
        _folderLabel = new Label
        {
            Text = $"対象フォルダ: {_folderPath ?? "（不明）"}",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(8, 6, 8, 0),
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 9f)
        };

        // === 選択数カウント ===
        _countLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(8, 2, 8, 0),
            ForeColor = Color.DarkBlue
        };

        // === アーカイブ一覧（CheckedListBox） ===
        _fileList = new CheckedListBox
        {
            CheckOnClick = true,
            IntegralHeight = false
        };

        // ファイル名とサイズを表示アイテムとして追加
        foreach (var path in _archivePaths)
        {
            var fileName = Path.GetFileName(path);
            var sizeText = FormatFileSize(path);
            _fileList.Items.Add($"{fileName}    ({sizeText})", true); // 初期状態で全選択
        }

        // チェック状態変更時にカウントを更新
        _fileList.ItemCheck += (_, e) =>
        {
            // ItemCheck イベントは変更前に発火するため、BeginInvoke で変更後に更新
            BeginInvoke(() => UpdateCountLabel());
        };

        // リスト用パネル（パディング付き）
        var listPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 4)
        };
        _fileList.Dock = DockStyle.Fill;
        listPanel.Controls.Add(_fileList);

        // === 選択操作ボタン（上段） ===
        var selectPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 2, 4, 2),
            WrapContents = false
        };

        _selectAllButton = new Button
        {
            Text = "全選択",
            Width = 80,
            Height = 28
        };
        _selectAllButton.Click += OnSelectAllClick;

        _deselectAllButton = new Button
        {
            Text = "全解除",
            Width = 80,
            Height = 28
        };
        _deselectAllButton.Click += OnDeselectAllClick;

        selectPanel.Controls.AddRange(new Control[] { _selectAllButton, _deselectAllButton });

        // === アクションボタン（下段） ===
        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
            WrapContents = false
        };

        _closeButton = new Button
        {
            Text = "閉じる",
            Width = 90,
            Height = 32
        };
        _closeButton.Click += (_, _) =>
        {
            SelectedFiles = null;
            Close();
        };

        _extractButton = new Button
        {
            Text = "展開",
            Width = 90,
            Height = 32,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
        };
        _extractButton.Click += OnExtractClick;

        // 右から順に配置（RightToLeft）
        actionPanel.Controls.AddRange(new Control[] { _closeButton, _extractButton });

        // === フォームにコントロールを追加（下から順に追加 = 上から順に表示） ===
        Controls.Add(listPanel);
        Controls.Add(selectPanel);
        Controls.Add(_countLabel);
        Controls.Add(_folderLabel);
        Controls.Add(actionPanel);

        // 初期カウント表示
        UpdateCountLabel();
    }

    /// <summary>
    /// 選択数カウントラベルを更新する。
    /// </summary>
    private void UpdateCountLabel()
    {
        var checkedCount = _fileList.CheckedItems.Count;
        var totalCount = _fileList.Items.Count;
        _countLabel.Text = $"選択: {checkedCount} / {totalCount} ファイル";

        // 選択が0の場合は展開ボタンを無効化
        _extractButton.Enabled = checkedCount > 0;
    }

    /// <summary>
    /// 全選択ボタンのクリックハンドラ。全てのアイテムにチェックを入れる。
    /// </summary>
    private void OnSelectAllClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _fileList.Items.Count; i++)
        {
            _fileList.SetItemChecked(i, true);
        }
        UpdateCountLabel();
    }

    /// <summary>
    /// 全解除ボタンのクリックハンドラ。全てのアイテムのチェックを外す。
    /// </summary>
    private void OnDeselectAllClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _fileList.Items.Count; i++)
        {
            _fileList.SetItemChecked(i, false);
        }
        UpdateCountLabel();
    }

    /// <summary>
    /// 展開ボタンのクリックハンドラ。
    /// チェックされたファイルのパスを SelectedFiles にセットしてフォームを閉じる。
    /// </summary>
    private void OnExtractClick(object? sender, EventArgs e)
    {
        SelectedFiles = new List<string>();

        for (int i = 0; i < _fileList.Items.Count; i++)
        {
            if (_fileList.GetItemChecked(i))
            {
                SelectedFiles.Add(_archivePaths[i]);
            }
        }

        if (SelectedFiles.Count == 0)
        {
            SelectedFiles = null;
            return;
        }

        Close();
    }

    /// <summary>
    /// ファイルサイズを見やすい形式にフォーマットする。
    /// 例: 1234567 → "1.2 MB"
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>フォーマットされたサイズ文字列</returns>
    private static string FormatFileSize(string filePath)
    {
        try
        {
            var size = new FileInfo(filePath).Length;
            return size switch
            {
                < 1024 => $"{size} B",
                < 1024 * 1024 => $"{size / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{size / (1024.0 * 1024):F1} MB",
                _ => $"{size / (1024.0 * 1024 * 1024):F2} GB"
            };
        }
        catch
        {
            return "不明";
        }
    }
}
