using System.Text.Json;
using System.Text.Json.Serialization;

namespace SendToExtract;

/// <summary>
/// アプリケーション設定を管理するクラス。
/// 設定ファイルは %APPDATA%\SendToExtract\settings.json に保存される。
/// ファイルが存在しない場合はデフォルト値で自動生成する。
/// </summary>
public class Settings
{
    /// <summary>設定ファイルのディレクトリパス</summary>
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SendToExtract");

    /// <summary>設定ファイルのフルパス</summary>
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    /// <summary>
    /// 展開先フォルダが既に存在する場合の衝突ポリシー。
    /// Skip=何もしない / Rename=連番付与 / Overwrite=上書き
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CollisionPolicy CollisionPolicy { get; set; } = CollisionPolicy.Rename;

    /// <summary>
    /// 二重ラップ平坦化。展開後、出力フォルダ直下が単一フォルダのみの場合に
    /// その中身を1階層上げてラップを解消する。
    /// </summary>
    public bool FlattenSingleRoot { get; set; } = true;

    /// <summary>
    /// フォルダ指定時にサブフォルダも再帰的に探索するか。
    /// 初版ではOFF（直下のみ）。
    /// </summary>
    public bool RecurseSubfolders { get; set; } = false;

    /// <summary>
    /// 全アーカイブの展開が成功した場合、進捗ウィンドウを自動的に閉じるか。
    /// </summary>
    public bool AutoCloseOnSuccess { get; set; } = true;

    /// <summary>
    /// 自動クローズまでの待機時間（秒）。
    /// </summary>
    public int AutoCloseDelaySec { get; set; } = 3;

    /// <summary>
    /// 対象とするアーカイブ拡張子のリスト。
    /// フォルダ内のファイルを列挙する際にこのリストでフィルタする。
    /// </summary>
    public List<string> Extensions { get; set; } = new()
    {
        ".zip", ".tar", ".tar.gz", ".tgz", ".tar.bz2", ".tbz2",
        ".tar.xz", ".txz", ".gz", ".bz2", ".7z", ".rar"
    };

    /// <summary>JSON シリアライズオプション（読みやすいインデント付き出力）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// 設定ファイルを読み込む。ファイルが存在しなければデフォルト値で新規作成する。
    /// 読み込みに失敗した場合もデフォルト値を返す（設定ファイル破損時の安全策）。
    /// </summary>
    /// <returns>読み込まれた設定オブジェクト</returns>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                // 既存の設定ファイルを読み込む
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                return settings ?? new Settings();
            }
        }
        catch
        {
            // 読み込み失敗時はデフォルト値で続行（ログは出さない、まだログ機構がないため）
        }

        // ファイルが無い or 読み込み失敗 → デフォルト値で生成して保存
        var defaultSettings = new Settings();
        defaultSettings.Save();
        return defaultSettings;
    }

    /// <summary>
    /// 現在の設定をファイルに保存する。
    /// ディレクトリが存在しない場合は自動作成する。
    /// </summary>
    public void Save()
    {
        try
        {
            // 設定ディレクトリがなければ作成
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 保存失敗は無視（権限問題等の場合、動作に支障はない）
        }
    }
}

/// <summary>
/// 展開先フォルダの衝突時ポリシー。
/// </summary>
public enum CollisionPolicy
{
    /// <summary>既に存在したら何もしない</summary>
    Skip,
    /// <summary>"name (2)", "name (3)" のように連番を付与</summary>
    Rename,
    /// <summary>既存フォルダに上書き展開</summary>
    Overwrite
}
