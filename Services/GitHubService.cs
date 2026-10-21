#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed class GitHubService
{
    public static GitHubService Instance { get; } = new();

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _authFilePath;

    private sealed class StoredGitHubAuth
    {
        public string Token { get; set; } = "";
        public string Username { get; set; } = "";
        public string? Name { get; set; }
        public string? AvatarUrl { get; set; }
        public string? HtmlUrl { get; set; }
        public string AuthMethod { get; set; } = "pat";
        public List<string> Scopes { get; set; } = [];
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }

    private GitHubService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AgentsHub-Desktop");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentsHub");
        if (!Directory.Exists(appData))
        {
            Directory.CreateDirectory(appData);
        }
        _authFilePath = Path.Combine(appData, "github_auth.json");
    }

    public async Task<GitHubAuthStatus> GetAuthStatusAsync(CancellationToken ct = default)
    {
        // 1. Check stored PAT first
        if (File.Exists(_authFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_authFilePath, ct).ConfigureAwait(false);
                var stored = JsonSerializer.Deserialize<StoredGitHubAuth>(json, JsonOptions);
                if (stored != null && !string.IsNullOrWhiteSpace(stored.Token))
                {
                    // Validate token with live API
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", stored.Token);

                    using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;

                        var scopes = new List<string>();
                        if (res.Headers.TryGetValues("X-OAuth-Scopes", out var scopeValues))
                        {
                            foreach (var sv in scopeValues)
                            {
                                foreach (var part in sv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    scopes.Add(part.Trim());
                                }
                            }
                        }

                        return new GitHubAuthStatus
                        {
                            IsAuthenticated = true,
                            AuthMethod = stored.AuthMethod ?? "pat",
                            Username = root.TryGetProperty("login", out var lProp) ? lProp.GetString() : stored.Username,
                            Name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : stored.Name,
                            AvatarUrl = root.TryGetProperty("avatar_url", out var aProp) ? aProp.GetString() : stored.AvatarUrl,
                            HtmlUrl = root.TryGetProperty("html_url", out var hProp) ? hProp.GetString() : stored.HtmlUrl,
                            Scopes = scopes.Count > 0 ? scopes : stored.Scopes
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GitHubService] PAT validation error: {ex.Message}");
            }
        }

        // 2. Check GitHub CLI (gh auth status / api user)
        var cliStatus = await CheckGitHubCliAuthAsync(ct).ConfigureAwait(false);
        if (cliStatus.IsAuthenticated)
        {
            return cliStatus;
        }

        // 3. Auto-check Git Credential Manager (GCM)
        var gcmStatus = await CheckGitCredentialManagerAuthAsync(ct).ConfigureAwait(false);
        if (gcmStatus.IsAuthenticated)
        {
            return gcmStatus;
        }

        return new GitHubAuthStatus
        {
            IsAuthenticated = false,
            AuthMethod = "none"
        };
    }

    public async Task<(bool success, string message, GitHubAuthStatus? status)> AutoConnectFromGitAsync(CancellationToken ct = default)
    {
        var gcmStatus = await CheckGitCredentialManagerAuthAsync(ct).ConfigureAwait(false);
        if (gcmStatus.IsAuthenticated)
        {
            return (true, $"Auto-connected to GitHub as @{gcmStatus.Username} via Git Credential Manager.", gcmStatus);
        }

        return (false, "No active GitHub credentials found in Git Credential Manager. You can generate a token on GitHub.", null);
    }

    public async Task<(bool success, string message, GitHubAuthStatus? status)> SavePatAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Token cannot be empty.", null);
        }

        token = token.Trim();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return (false, $"GitHub rejected token (HTTP {(int)res.StatusCode}: {res.ReasonPhrase}).", null);
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var username = root.TryGetProperty("login", out var lProp) ? lProp.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
            var avatarUrl = root.TryGetProperty("avatar_url", out var aProp) ? aProp.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var hProp) ? hProp.GetString() : null;

            var scopes = new List<string>();
            if (res.Headers.TryGetValues("X-OAuth-Scopes", out var scopeValues))
            {
                foreach (var sv in scopeValues)
                {
                    foreach (var part in sv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        scopes.Add(part.Trim());
                    }
                }
            }

            var stored = new StoredGitHubAuth
            {
                Token = token,
                Username = username,
                Name = name,
                AvatarUrl = avatarUrl,
                HtmlUrl = htmlUrl,
                Scopes = scopes,
                SavedAt = DateTime.UtcNow
            };

            await File.WriteAllTextAsync(_authFilePath, JsonSerializer.Serialize(stored, JsonOptions), ct).ConfigureAwait(false);

            var status = new GitHubAuthStatus
            {
                IsAuthenticated = true,
                AuthMethod = "pat",
                Username = username,
                Name = name,
                AvatarUrl = avatarUrl,
                HtmlUrl = htmlUrl,
                Scopes = scopes
            };

            return (true, $"Authenticated successfully as @{username}", status);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}", null);
        }
    }

    public bool Disconnect()
    {
        try
        {
            if (File.Exists(_authFilePath))
            {
                File.Delete(_authFilePath);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetEffectiveTokenAsync(CancellationToken ct = default)
    {
        // Check stored PAT
        if (File.Exists(_authFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_authFilePath, ct).ConfigureAwait(false);
                var stored = JsonSerializer.Deserialize<StoredGitHubAuth>(json, JsonOptions);
                if (stored != null && !string.IsNullOrWhiteSpace(stored.Token))
                {
                    return stored.Token;
                }
            }
            catch { }
        }

        // Check CLI token
        var (rc, tokenOut) = await RunGhCliAsync(["auth", "token"], ct).ConfigureAwait(false);
        if (rc == 0 && !string.IsNullOrWhiteSpace(tokenOut))
        {
            return tokenOut.Trim();
        }

        return null;
    }

    public async Task<GitHubPublishResult> PublishRepositoryAsync(
        string repoPath,
        string repoName,
        string description,
        bool isPrivate,
        CancellationToken ct = default)
    {
        var token = await GetEffectiveTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            return new GitHubPublishResult
            {
                Success = false,
                Message = "Not authenticated with GitHub. Please link a GitHub PAT or log in with GitHub CLI ('gh auth login')."
            };
        }

        if (string.IsNullOrWhiteSpace(repoName))
        {
            repoName = Path.GetFileName(repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        try
        {
            // 1. Create repository via GitHub REST API
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/user/repos");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                name = repoName,
                description = description ?? "",
                @private = isPrivate,
                auto_init = false
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            var resBody = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                return new GitHubPublishResult
                {
                    Success = false,
                    Message = $"GitHub API error (HTTP {(int)res.StatusCode}): {resBody}"
                };
            }

            using var doc = JsonDocument.Parse(resBody);
            var root = doc.RootElement;
            var htmlUrl = root.TryGetProperty("html_url", out var hProp) ? hProp.GetString() : null;
            var cloneUrl = root.TryGetProperty("clone_url", out var cProp) ? cProp.GetString() : null;

            if (string.IsNullOrEmpty(cloneUrl))
            {
                return new GitHubPublishResult
                {
                    Success = false,
                    Message = "Created remote repository, but clone URL was not returned by GitHub."
                };
            }

            // 2. Link remote origin locally
            var (remRc, remOut) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["remote", "add", "origin", cloneUrl], ct).ConfigureAwait(false);
            if (remRc != 0 && remOut.Contains("remote origin already exists", StringComparison.OrdinalIgnoreCase))
            {
                await GitRepositoryManagerService.RunGitAsync(repoPath, ["remote", "set-url", "origin", cloneUrl], ct).ConfigureAwait(false);
            }

            // 3. Detect current branch
            var (brRc, brOut) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"], ct).ConfigureAwait(false);
            string branch = brRc == 0 && !string.IsNullOrWhiteSpace(brOut) ? brOut.Trim() : "main";

            // 4. Push initial branch upstream
            var (pushRc, pushOut) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["push", "-u", "origin", branch], ct, timeoutSeconds: 45).ConfigureAwait(false);

            return new GitHubPublishResult
            {
                Success = true,
                RepoUrl = htmlUrl,
                CloneUrl = cloneUrl,
                Message = pushRc == 0
                    ? $"Repository published and branch '{branch}' pushed to GitHub."
                    : $"Remote repository created at {htmlUrl}, but push requires manual sync: {pushOut}"
            };
        }
        catch (Exception ex)
        {
            return new GitHubPublishResult
            {
                Success = false,
                Message = $"Failed to publish repository: {ex.Message}"
            };
        }
    }

    public static string? NormalizeGitHubWebUrl(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        var url = remoteUrl.Trim();

        // git@github.com:owner/repo.git or ssh://git@github.com/owner/repo.git
        var sshMatch = Regex.Match(url, @"^(?:ssh:\/\/)?git@github\.com[:\/]([A-Za-z0-9_.\-]+)\/([A-Za-z0-9_.\-]+?)(?:\.git)?$");
        if (sshMatch.Success)
        {
            return $"https://github.com/{sshMatch.Groups[1].Value}/{sshMatch.Groups[2].Value}";
        }

        // https://github.com/owner/repo.git or https://github.com/owner/repo
        var httpsMatch = Regex.Match(url, @"^https?:\/\/(?:[A-Za-z0-9_.\-]+@)?github\.com\/([A-Za-z0-9_.\-]+)\/([A-Za-z0-9_.\-]+?)(?:\.git)?\/?$");
        if (httpsMatch.Success)
        {
            return $"https://github.com/{httpsMatch.Groups[1].Value}/{httpsMatch.Groups[2].Value}";
        }

        return null;
    }

    public static string? GetGitHubBranchUrl(string? githubWebUrl, string? branch)
    {
        if (string.IsNullOrWhiteSpace(githubWebUrl)) return null;
        if (string.IsNullOrWhiteSpace(branch) || branch == "HEAD" || branch == "detached") return githubWebUrl;
        return $"{githubWebUrl.TrimEnd('/')}/tree/{Uri.EscapeDataString(branch)}";
    }

    private async Task<GitHubAuthStatus> CheckGitHubCliAuthAsync(CancellationToken ct)
    {
        var (rc, output) = await RunGhCliAsync(["api", "user"], ct).ConfigureAwait(false);
        if (rc == 0 && !string.IsNullOrWhiteSpace(output) && output.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement;
                if (root.TryGetProperty("login", out var lProp))
                {
                    return new GitHubAuthStatus
                    {
                        IsAuthenticated = true,
                        AuthMethod = "cli",
                        Username = lProp.GetString(),
                        Name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : null,
                        AvatarUrl = root.TryGetProperty("avatar_url", out var aProp) ? aProp.GetString() : null,
                        HtmlUrl = root.TryGetProperty("html_url", out var hProp) ? hProp.GetString() : null,
                        Scopes = ["repo", "read:user"]
                    };
                }
            }
            catch { }
        }

        return new GitHubAuthStatus { IsAuthenticated = false, AuthMethod = "none" };
    }

    private static async Task<(int exitCode, string output)> RunGhCliAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var outTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errTask = proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            var combined = (stdout + "\n" + stderr).Trim();
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private async Task<GitHubAuthStatus> CheckGitCredentialManagerAuthAsync(CancellationToken ct)
    {
        try
        {
            var (rc, output) = await RunGitWithStdinAsync(["credential", "fill"], "protocol=https\nhost=github.com\n\n", ct).ConfigureAwait(false);
            if (rc == 0 && !string.IsNullOrWhiteSpace(output))
            {
                string? token = null;
                string? username = null;
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                    {
                        token = line["password=".Length..].Trim();
                    }
                    else if (line.StartsWith("username=", StringComparison.OrdinalIgnoreCase))
                    {
                        username = line["username=".Length..].Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(token))
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;

                        var login = root.TryGetProperty("login", out var lProp) ? lProp.GetString() ?? username ?? "" : username ?? "";
                        var name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                        var avatarUrl = root.TryGetProperty("avatar_url", out var aProp) ? aProp.GetString() : null;
                        var htmlUrl = root.TryGetProperty("html_url", out var hProp) ? hProp.GetString() : null;

                        var scopes = new List<string>();
                        if (res.Headers.TryGetValues("X-OAuth-Scopes", out var scopeValues))
                        {
                            foreach (var sv in scopeValues)
                            {
                                foreach (var part in sv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    scopes.Add(part.Trim());
                                }
                            }
                        }

                        // Auto-persist so subsequent queries don't have to query git credential fill every time
                        var stored = new StoredGitHubAuth
                        {
                            Token = token,
                            Username = login,
                            Name = name,
                            AvatarUrl = avatarUrl,
                            HtmlUrl = htmlUrl,
                            AuthMethod = "gcm",
                            Scopes = scopes.Count > 0 ? scopes : ["repo", "workflow"],
                            SavedAt = DateTime.UtcNow
                        };
                        await File.WriteAllTextAsync(_authFilePath, JsonSerializer.Serialize(stored, JsonOptions), ct).ConfigureAwait(false);

                        return new GitHubAuthStatus
                        {
                            IsAuthenticated = true,
                            AuthMethod = "gcm",
                            Username = login,
                            Name = name,
                            AvatarUrl = avatarUrl,
                            HtmlUrl = htmlUrl,
                            Scopes = stored.Scopes
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitHubService] GCM check error: {ex.Message}");
        }

        return new GitHubAuthStatus { IsAuthenticated = false, AuthMethod = "none" };
    }

    private static async Task<(int returnCode, string output)> RunGitWithStdinAsync(string[] args, string stdinContent, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            if (!string.IsNullOrEmpty(stdinContent))
            {
                await proc.StandardInput.WriteAsync(stdinContent).ConfigureAwait(false);
                proc.StandardInput.Close();
            }

            var outTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errTask = proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            return (proc.ExitCode, (stdout + "\n" + stderr).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
