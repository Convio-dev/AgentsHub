#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using System.Windows.Forms;
#endif

using AgentsHub.Helpers;

namespace AgentsHub.Services;

public enum UpdateCheckResult
{
    UpdateAvailable,
    UpToDate,
    NoReleasesFound,
    CheckFailed
}

public sealed class UpdateInfo
{
    public UpdateCheckResult Result { get; set; } = UpdateCheckResult.UpToDate;
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long AssetSizeBytes { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class UpdateService
{
    public static UpdateService Instance { get; } = new();

    private const string LatestReleaseApiUrl = "https://api.github.com/repos/ZerroDevs/AgentsHub/releases/latest";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AgentsHub-Updater/1.0 (Windows Desktop; contact: zerrodevs@github.com)");
    }

    public static string CurrentVersionString => AppInfo.Version;

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = CurrentVersionString
        };

        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                info.Result = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? UpdateCheckResult.NoReleasesFound
                    : UpdateCheckResult.CheckFailed;
                return info;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
            info.LatestVersion = tagName.Trim().TrimStart('v', 'V');
            info.ReleaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
            info.ReleaseNotes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;

            if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var pubDate))
            {
                info.PublishedAtUtc = pubDate.ToUniversalTime();
            }

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                string? bestUrl = null;
                long bestSize = 0;

                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var anProp) ? anProp.GetString() ?? string.Empty : string.Empty;
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var url = asset.TryGetProperty("browser_download_url", out var dlProp) ? dlProp.GetString() ?? string.Empty : string.Empty;
                        var size = asset.TryGetProperty("size", out var sProp) ? sProp.GetInt64() : 0;

                        if (assetName.StartsWith("AgentsHub-Setup", StringComparison.OrdinalIgnoreCase))
                        {
                            bestUrl = url;
                            bestSize = size;
                            break;
                        }

                        if (bestUrl == null)
                        {
                            bestUrl = url;
                            bestSize = size;
                        }
                    }
                }

                info.DownloadUrl = bestUrl ?? string.Empty;
                info.AssetSizeBytes = bestSize;
            }

            if (TryParseVersion(info.LatestVersion, out var latestVer) &&
                TryParseVersion(info.CurrentVersion, out var currentVer))
            {
                info.IsUpdateAvailable = latestVer > currentVer && !string.IsNullOrEmpty(info.DownloadUrl);
                info.Result = info.IsUpdateAvailable ? UpdateCheckResult.UpdateAvailable : UpdateCheckResult.UpToDate;
            }
            else
            {
                info.Result = UpdateCheckResult.UpToDate;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            info.Result = UpdateCheckResult.CheckFailed;
        }

        return info;
    }

    public async Task DownloadAndInstallAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "AgentsHub_Updates");
        Directory.CreateDirectory(tempDir);

        var uri = new Uri(downloadUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "AgentsHub_Setup.exe";
        }
        var targetFile = Path.Combine(tempDir, fileName);

        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report(Math.Clamp((double)totalRead / totalBytes, 0.0, 1.0));
                }
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = targetFile,
            Arguments = "/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        };

        Process.Start(psi);

        // Allow installer to initiate and close current instance gracefully
        try
        {
#if WINDOWS
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0]?.InvokeRequired == true)
            {
                Application.OpenForms[0]?.BeginInvoke(new Action(() => Application.Exit()));
            }
            else
            {
                Application.Exit();
            }
#else
            Environment.Exit(0);
#endif
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    private static bool TryParseVersion(string input, out Version version)
    {
        var cleaned = input.Trim().TrimStart('v', 'V');
        if (Version.TryParse(cleaned, out var v))
        {
            version = v;
            return true;
        }

        if (Version.TryParse(cleaned + ".0", out v))
        {
            version = v;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }
}
