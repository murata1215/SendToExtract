namespace SendToExtract;

/// <summary>
/// exe をダブルクリック（引数なし）で起動した時に表示するセットアップ画面。
/// 「送る」メニューへのショートカット登録/削除をボタンで行える。
/// </summary>
public class SetupForm : Form
{
    /// <summary>登録状態を表示するラベル</summary>
    private readonly Label _statusLabel;

    /// <summary>「送る」に登録ボタン</summary>
    private readonly Button _installButton;

    /// <summary>「送る」から削除ボタン</summary>
    private readonly Button _uninstallButton;

    /// <summary>閉じるボタン</summary>
    private readonly Button _closeButton;

    /// <summary>
    /// SetupForm のコンストラクタ。UIを構築する。
    /// </summary>
    public SetupForm()
    {
        // === フォーム基本設定 ===
        Text = $"SendToExtract v{AppInfo.Version}";
        Size = new Size(420, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // === タイトルラベル ===
        var titleLabel = new Label
        {
            Text = "SendToExtract",
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            Location = new Point(24, 16),
            AutoSize = true
        };

        // === 説明ラベル ===
        var descLabel = new Label
        {
            Text = "アーカイブ一括展開ツール\n対応形式: zip, tar, tar.gz, 7z, rar 等",
            Location = new Point(24, 52),
            Size = new Size(360, 36),
            ForeColor = Color.DimGray
        };

        // === 使い方ラベル ===
        var usageLabel = new Label
        {
            Text = "フォルダやアーカイブを右クリック →「送る」→「SendToExtract」",
            Location = new Point(24, 96),
            Size = new Size(360, 20)
        };

        // === 登録状態ラベル ===
        _statusLabel = new Label
        {
            Location = new Point(24, 128),
            Size = new Size(360, 24),
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold)
        };

        // === ボタンパネル ===
        // 「送る」に登録ボタン
        _installButton = new Button
        {
            Text = "「送る」に登録",
            Location = new Point(24, 168),
            Size = new Size(140, 36),
            Font = new Font(Font.FontFamily, 9f)
        };
        _installButton.Click += OnInstallClick;

        // 「送る」から削除ボタン
        _uninstallButton = new Button
        {
            Text = "「送る」から削除",
            Location = new Point(172, 168),
            Size = new Size(140, 36),
            Font = new Font(Font.FontFamily, 9f)
        };
        _uninstallButton.Click += OnUninstallClick;

        // 閉じるボタン
        _closeButton = new Button
        {
            Text = "閉じる",
            Location = new Point(320, 168),
            Size = new Size(72, 36)
        };
        _closeButton.Click += (_, _) => Close();

        // === フォームにコントロールを追加 ===
        Controls.AddRange(new Control[]
        {
            titleLabel, descLabel, usageLabel, _statusLabel,
            _installButton, _uninstallButton, _closeButton
        });

        // 初期状態を反映
        UpdateStatus();
    }

    /// <summary>
    /// 登録状態に応じてラベルとボタンの有効/無効を更新する。
    /// </summary>
    private void UpdateStatus()
    {
        var installed = SendToInstaller.IsInstalled();

        if (installed)
        {
            _statusLabel.Text = "状態: ✓ 「送る」メニューに登録済み";
            _statusLabel.ForeColor = Color.Green;
            _installButton.Enabled = false;
            _uninstallButton.Enabled = true;
        }
        else
        {
            _statusLabel.Text = "状態: ✗ 「送る」メニューに未登録";
            _statusLabel.ForeColor = Color.OrangeRed;
            _installButton.Enabled = true;
            _uninstallButton.Enabled = false;
        }
    }

    /// <summary>
    /// 「送る」に登録ボタンのクリックハンドラ。
    /// ショートカットを作成して状態を更新する。
    /// </summary>
    private void OnInstallClick(object? sender, EventArgs e)
    {
        if (SendToInstaller.Install())
        {
            UpdateStatus();
            MessageBox.Show(
                "「送る」メニューに登録しました。",
                "SendToExtract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                "登録に失敗しました。",
                "SendToExtract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 「送る」から削除ボタンのクリックハンドラ。
    /// ショートカットを削除して状態を更新する。
    /// </summary>
    private void OnUninstallClick(object? sender, EventArgs e)
    {
        if (SendToInstaller.Uninstall())
        {
            UpdateStatus();
            MessageBox.Show(
                "「送る」メニューから削除しました。",
                "SendToExtract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                "削除に失敗しました。",
                "SendToExtract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
