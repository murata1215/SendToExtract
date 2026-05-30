using System.Runtime.InteropServices;

namespace SendToExtract;

/// <summary>
/// Windows の「送る」メニュー（SendTo フォルダ）にショートカットを作成/削除するクラス。
/// COM の WScript.Shell を使用してショートカット（.lnk）を生成する。
///
/// 使い方:
///   SendToInstaller.Install();    // 「送る」に登録
///   SendToInstaller.Uninstall();  // 「送る」から削除
/// </summary>
public static class SendToInstaller
{
    /// <summary>ショートカットファイル名</summary>
    private const string ShortcutName = "SendToExtract.lnk";

    /// <summary>
    /// 「送る」フォルダのパスを取得する。
    /// %APPDATA%\Microsoft\Windows\SendTo に相当する。
    /// </summary>
    /// <returns>SendTo フォルダのフルパス</returns>
    private static string GetSendToPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
    }

    /// <summary>
    /// ショートカットのフルパスを取得する。
    /// </summary>
    /// <returns>ショートカットファイルのフルパス</returns>
    private static string GetShortcutPath()
    {
        return Path.Combine(GetSendToPath(), ShortcutName);
    }

    /// <summary>
    /// 「送る」メニューに exe へのショートカットを作成する。
    /// COM の WScript.Shell（IWshShortcut）を使用して .lnk ファイルを生成する。
    /// 既にショートカットが存在する場合は上書き更新する。
    /// </summary>
    /// <returns>成功した場合は true</returns>
    public static bool Install()
    {
        try
        {
            var shortcutPath = GetShortcutPath();
            var exePath = GetExePath();

            // COM の WScript.Shell を使ってショートカット作成
            // Type.GetTypeFromProgID で COM オブジェクトを動的に取得
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                Console.Error.WriteLine("エラー: WScript.Shell COM オブジェクトが見つかりません");
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                // ショートカットオブジェクトを作成
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                try
                {
                    // ショートカットのプロパティを設定
                    shortcut.TargetPath = exePath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                    shortcut.Description = "SendToExtract - アーカイブ一括展開";
                    // アイコンは exe 自体のアイコンを使用
                    shortcut.IconLocation = $"{exePath},0";
                    // ショートカットを保存
                    shortcut.Save();
                }
                finally
                {
                    Marshal.ReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"インストールエラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 「送る」メニューからショートカットを削除する。
    /// ショートカットが存在しない場合は何もしない。
    /// </summary>
    /// <returns>成功した場合は true</returns>
    public static bool Uninstall()
    {
        try
        {
            var shortcutPath = GetShortcutPath();
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"アンインストールエラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ショートカットが既に存在するかチェックする。
    /// </summary>
    /// <returns>存在する場合は true</returns>
    public static bool IsInstalled()
    {
        return File.Exists(GetShortcutPath());
    }

    /// <summary>
    /// 現在実行中の exe のパスを取得する。
    /// self-contained single-file の場合、Environment.ProcessPath を使用する。
    /// </summary>
    /// <returns>exe のフルパス</returns>
    private static string GetExePath()
    {
        // .NET 6+ では Environment.ProcessPath が利用可能
        // self-contained single-file でも正しいパスを返す
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("実行ファイルのパスを取得できません");
    }
}
