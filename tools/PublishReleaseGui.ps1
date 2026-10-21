# Agents Hub Release Publisher GUI
# Uses native WPF and GitHub REST API to publish releases with zero dependencies

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Net.Http

if (-not ([System.Management.Automation.PSTypeName]'AgentsHubPublisher.UploadProgressStream').Type) {
    Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace AgentsHubPublisher {
    public class UploadProgressStream : Stream {
        private readonly Stream _inner;
        private readonly long _totalBytes;
        private long _bytesSent;
        private readonly ConcurrentQueue<string> _queue;
        private int _lastPercentReported = -1;

        public UploadProgressStream(Stream inner, long totalBytes, ConcurrentQueue<string> queue) {
            _inner = inner;
            _totalBytes = totalBytes;
            _queue = queue;
        }

        public override bool CanRead { get { return _inner.CanRead; } }
        public override bool CanSeek { get { return _inner.CanSeek; } }
        public override bool CanWrite { get { return _inner.CanWrite; } }
        public override long Length { get { return _totalBytes; } }
        public override long Position {
            get { return _inner.Position; }
            set { _inner.Position = value; }
        }

        public override void Flush() { _inner.Flush(); }
        public override long Seek(long offset, SeekOrigin origin) { return _inner.Seek(offset, origin); }
        public override void SetLength(long value) { _inner.SetLength(value); }
        public override void Write(byte[] buffer, int offset, int count) { _inner.Write(buffer, offset, count); }

        private void ReportProgress(int bytesJustRead) {
            if (bytesJustRead <= 0 || _totalBytes <= 0) return;
            _bytesSent += bytesJustRead;
            int currentPercent = (int)((_bytesSent * 100) / _totalBytes);
            if (currentPercent != _lastPercentReported) {
                _lastPercentReported = currentPercent;
                double mbSent = Math.Round((double)_bytesSent / (1024 * 1024), 2);
                double mbTotal = Math.Round((double)_totalBytes / (1024 * 1024), 2);
                if (_queue != null) {
                    _queue.Enqueue(string.Format("PROGRESS:{0}:{1:0.00}:{2:0.00}", currentPercent, mbSent, mbTotal));
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count) {
            int read = _inner.Read(buffer, offset, count);
            ReportProgress(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            ReportProgress(read);
            return read;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
"@
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = (Get-Item "$scriptDir\..").FullName
Set-Location $rootDir

# 1. Resolve current version from AgentsHub.csproj
$currentVersion = "1.0.3"
$csprojPath = Join-Path $rootDir "AgentsHub.csproj"
if (Test-Path $csprojPath) {
    $content = Get-Content $csprojPath -Raw
    if ($content -match '<Version>([^<]+)</Version>') {
        $currentVersion = $matches[1].Trim()
    }
}

# 2. Resolve saved token if present
$tokenFile = Join-Path $rootDir ".release_token"
$savedToken = ""
if (Test-Path $tokenFile) {
    try {
        $savedToken = (Get-Content $tokenFile -Raw).Trim()
    } catch {}
}

# 3. Define XAML
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Agents Hub Release Publisher" Height="720" Width="700"
        WindowStartupLocation="CenterScreen"
        Background="#0B0F19" Foreground="#F8FAFC"
        FontFamily="Segoe UI" UseLayoutRounding="True">
    <Window.Resources>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#111827" />
            <Setter Property="Foreground" Value="#F8FAFC" />
            <Setter Property="BorderBrush" Value="#334155" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="8,6" />
            <Setter Property="FontSize" Value="12" />
        </Style>
        <Style TargetType="PasswordBox">
            <Setter Property="Background" Value="#111827" />
            <Setter Property="Foreground" Value="#F8FAFC" />
            <Setter Property="BorderBrush" Value="#334155" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="8,6" />
            <Setter Property="FontSize" Value="12" />
        </Style>
        <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#E2E8F0" />
            <Setter Property="FontSize" Value="11.5" />
            <Setter Property="Cursor" Value="Hand" />
        </Style>
    </Window.Resources>

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Header -->
        <Border Grid.Row="0" BorderThickness="0,0,0,1" BorderBrush="#1E293B" Padding="0,0,0,14" Margin="0,0,0,16">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="Agents Hub" FontWeight="Bold" FontSize="16" Foreground="#10B981" />
                        <TextBlock Text=" · GitHub Release Publisher" FontWeight="SemiBold" FontSize="16" Foreground="#F8FAFC" />
                    </StackPanel>
                    <TextBlock Text="Target: https://github.com/ZerroDevs/AgentsHub" FontSize="10.5" Foreground="#94A3B8" Margin="0,3,0,0" />
                </StackPanel>
                <Button Grid.Column="1" Name="btnOpenReleases" Content="View Live Releases" 
                        Background="#1E293B" Foreground="#94A3B8" BorderThickness="1" BorderBrush="#334155"
                        Padding="12,5" FontSize="11" Cursor="Hand" VerticalAlignment="Center" />
            </Grid>
        </Border>

        <!-- Form Row 1: Tag & Title -->
        <Grid Grid.Row="1" Margin="0,0,0,12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="150" />
                <ColumnDefinition Width="16" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="Release Tag" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" Margin="0,0,0,4" />
                <TextBox Name="txtTag" Text="v$currentVersion" />
            </StackPanel>
            <StackPanel Grid.Column="2">
                <TextBlock Text="Release Title" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" Margin="0,0,0,4" />
                <TextBox Name="txtTitle" Text="Agents Hub v$currentVersion" />
            </StackPanel>
        </Grid>

        <!-- Form Row 2: PAT Token -->
        <StackPanel Grid.Row="2" Margin="0,0,0,12">
            <Grid Margin="0,0,0,4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="GitHub Personal Access Token (PAT)" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" />
                <TextBlock Grid.Column="1" Name="lblTokenStatus" Text="" FontSize="10.5" Foreground="#10B981" />
            </Grid>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="8" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <PasswordBox Grid.Column="0" Name="txtToken" />
                <Button Grid.Column="2" Name="btnForgetToken" Content="Clear Saved" 
                        Background="#1E293B" Foreground="#EF4444" BorderThickness="1" BorderBrush="#334155"
                        Padding="10,4" FontSize="11" Cursor="Hand" />
            </Grid>
            <TextBlock Text="Requires 'repo' scope. Saved securely to local .release_token (git-ignored)." FontSize="10" Foreground="#64748B" Margin="0,4,0,0" />
        </StackPanel>

        <!-- Form Row 3: Release Notes -->
        <StackPanel Grid.Row="3" Margin="0,0,0,12">
            <TextBlock Text="Release Notes (Markdown)" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" Margin="0,0,0,4" />
            <TextBox Name="txtNotes" Height="150" TextWrapping="Wrap" AcceptsReturn="True" VerticalScrollBarVisibility="Auto"
                     FontFamily="Consolas" FontSize="11.5"
                     Text="## What's Changed in Agents Hub v$currentVersion&#x0d;&#x0a;&#x0d;&#x0a;### Features &amp; Enhancements&#x0d;&#x0a;- &#x0d;&#x0a;&#x0d;&#x0a;### Improvements&#x0d;&#x0a;- &#x0d;&#x0a;&#x0d;&#x0a;### Bug Fixes&#x0d;&#x0a;- " />
        </StackPanel>

        <!-- Form Row 4: Console Output & Status -->
        <Grid Grid.Row="4" Margin="0,0,0,14">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <Grid Grid.Row="0" Margin="0,0,0,4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Activity Log" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" />
                <TextBlock Grid.Column="1" Name="lblProgress" Text="" FontSize="11" FontWeight="SemiBold" Foreground="#10B981" />
            </Grid>

            <TextBox Grid.Row="1" Name="txtLog" Background="#060911" Foreground="#38BDF8" BorderBrush="#1E293B"
                     IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                     FontFamily="Consolas" FontSize="11" Padding="8" />

            <ProgressBar Grid.Row="2" Name="pbUpload" Height="6" Margin="0,6,0,0" Background="#1E293B" Foreground="#10B981"
                         BorderThickness="0" Minimum="0" Maximum="100" Value="0" Visibility="Collapsed" />
        </Grid>

        <!-- Footer / Action Buttons -->
        <Grid Grid.Row="5">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                <CheckBox Name="chkPreRelease" Content="Pre-release" Margin="0,0,16,0" />
                <CheckBox Name="chkBuildFirst" Content="Compile Installer First" IsChecked="True" />
            </StackPanel>

            <Button Grid.Column="2" Name="btnBuildOnly" Content="Build Only" 
                    Background="#1E293B" Foreground="#F8FAFC" BorderThickness="1" BorderBrush="#334155"
                    Padding="16,8" FontSize="12" FontWeight="SemiBold" Cursor="Hand" />

            <Button Grid.Column="4" Name="btnPublish" Content="Build &amp; Publish Release" 
                    Background="#10B981" Foreground="#0B0F19" BorderThickness="0"
                    Padding="20,8" FontSize="12" FontWeight="Bold" Cursor="Hand" />
        </Grid>
    </Grid>
</Window>
"@

$reader = [System.Xml.XmlNodeReader]::new($xaml)
$window = [System.Windows.Markup.XamlReader]::Load($reader)

# Get Controls
$txtTag = $window.FindName("txtTag")
$txtTitle = $window.FindName("txtTitle")
$txtToken = $window.FindName("txtToken")
$btnForgetToken = $window.FindName("btnForgetToken")
$lblTokenStatus = $window.FindName("lblTokenStatus")
$txtNotes = $window.FindName("txtNotes")
$txtLog = $window.FindName("txtLog")
$lblProgress = $window.FindName("lblProgress")
$pbUpload = $window.FindName("pbUpload")
$chkPreRelease = $window.FindName("chkPreRelease")
$chkBuildFirst = $window.FindName("chkBuildFirst")
$btnBuildOnly = $window.FindName("btnBuildOnly")
$btnPublish = $window.FindName("btnPublish")
$btnOpenReleases = $window.FindName("btnOpenReleases")

if ($savedToken) {
    $txtToken.Password = $savedToken
    $lblTokenStatus.Text = "Loaded from .release_token"
}

function Write-LogMessage($msg) {
    if ($txtLog.Dispatcher.CheckAccess()) {
        $txtLog.AppendText("$msg`r`n")
        $txtLog.ScrollToEnd()
    } else {
        $txtLog.Dispatcher.Invoke([Action]{
            $txtLog.AppendText("$msg`r`n")
            $txtLog.ScrollToEnd()
        })
    }
}

function Sync-VersionToCsproj($verTag) {
    $cleanVer = $verTag.Trim().TrimStart('v', 'V')
    if (-not $cleanVer) { return $currentVersion }

    $csproj = Join-Path $rootDir "AgentsHub.csproj"
    if (Test-Path $csproj) {
        $raw = Get-Content $csproj -Raw
        if ($raw -match '<Version>([^<]+)</Version>') {
            $existingVer = $matches[1].Trim()
            if ($existingVer -ne $cleanVer) {
                Write-LogMessage "[*] Updating AgentsHub.csproj Version tag: $existingVer -> $cleanVer"
                $updated = $raw -replace '<Version>[^<]+</Version>', "<Version>$cleanVer</Version>"
                Set-Content -Path $csproj -Value $updated -NoNewline
            }
        }
    }
    return $cleanVer
}

function Find-InstallerBinary($cleanVer) {
    $distDir = Join-Path $rootDir "dist"
    if (-not (Test-Path $distDir)) { return $null }

    # First attempt: exact match with version
    $exactMatch = Join-Path $distDir "AgentsHub-Setup-v$cleanVer.exe"
    if (Test-Path $exactMatch) {
        return (Get-Item $exactMatch)
    }

    # Second attempt: any AgentsHub-Setup*.exe sorted newest
    $found = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    return $found
}

$btnForgetToken.Add_Click({
    if (Test-Path $tokenFile) {
        Remove-Item $tokenFile -Force -ErrorAction SilentlyContinue
    }
    $txtToken.Password = ""
    $lblTokenStatus.Text = "Cleared"
})

$btnOpenReleases.Add_Click({
    [System.Diagnostics.Process]::Start([System.Diagnostics.ProcessStartInfo]@{
        FileName = "https://github.com/ZerroDevs/AgentsHub/releases"
        UseShellExecute = $true
    })
})

# "Build Only" Handler
$btnBuildOnly.Add_Click({
    $tag = $txtTag.Text.Trim()
    $cleanVer = Sync-VersionToCsproj $tag

    $btnBuildOnly.IsEnabled = $false
    $btnPublish.IsEnabled = $false
    $pbUpload.Visibility = [System.Windows.Visibility]::Collapsed
    $lblProgress.Text = "Compiling installer..."
    $txtLog.Text = ""
    Write-LogMessage "Compiling self-contained single-file release package & Inno Setup installer..."

    $logQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $resultQueue = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()

    $workerScript = {
        param($rootDir, $cleanVer, $lq, $rq)
        try {
            $buildCmd = Join-Path $rootDir "build-installer.cmd"
            $psi = [System.Diagnostics.ProcessStartInfo]::new("cmd.exe", "/c `"$buildCmd`"")
            $psi.WorkingDirectory = $rootDir
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true

            $proc = [System.Diagnostics.Process]::Start($psi)
            while (-not $proc.StandardOutput.EndOfStream) {
                $line = $proc.StandardOutput.ReadLine()
                if ($line) { $lq.Enqueue($line) }
            }
            $proc.WaitForExit()

            if ($proc.ExitCode -eq 0) {
                $distDir = Join-Path $rootDir "dist"
                $installer = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup-v$cleanVer.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $installer) {
                    $installer = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }

                if ($installer) {
                    $sizeMb = [Math]::Round($installer.Length / 1MB, 2)
                    $rq.Enqueue(@{ Success = $true; Path = $installer.FullName; Size = $sizeMb })
                } else {
                    $rq.Enqueue(@{ Success = $false; Code = 1; Error = "Installer executable not found in dist\" })
                }
            } else {
                $rq.Enqueue(@{ Success = $false; Code = $proc.ExitCode })
            }
        } catch {
            $rq.Enqueue(@{ Success = $false; Error = $_.Exception.Message })
        }
    }

    $psInstance = [PowerShell]::Create().AddScript($workerScript).AddArgument($rootDir).AddArgument($cleanVer).AddArgument($logQueue).AddArgument($resultQueue)
    $asyncHandle = $psInstance.BeginInvoke()

    $timer = [System.Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds(100)
    $tickHandler = {
        $m = $null
        while ($logQueue.TryDequeue([ref]$m)) {
            Write-LogMessage $m
        }

        if ($asyncHandle.IsCompleted) {
            $timer.Stop()
            $finalM = $null
            while ($logQueue.TryDequeue([ref]$finalM)) {
                Write-LogMessage $finalM
            }

            try { $psInstance.EndInvoke($asyncHandle) | Out-Null } catch {}
            $psInstance.Dispose()

            $btnBuildOnly.IsEnabled = $true
            $btnPublish.IsEnabled = $true

            $res = $null
            if ($resultQueue.TryDequeue([ref]$res)) {
                if ($res.Success) {
                    $lblProgress.Text = "Installer Ready ($($res.Size) MB)"
                    Write-LogMessage "[SUCCESS] Self-contained single-file installer ready: $($res.Path)"
                    $dlg = [System.Windows.MessageBox]::Show("Setup installer package compiled successfully ($($res.Size) MB)!`n`nLocation:`n$($res.Path)`n`nWould you like to open the dist folder in File Explorer?", "Installer Build Succeeded", [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Information)
                    if ($dlg -eq [System.Windows.MessageBoxResult]::Yes) {
                        [System.Diagnostics.Process]::Start("explorer.exe", "/select,`"$($res.Path)`"")
                    }
                } else {
                    $lblProgress.Text = "Build Failed"
                    [System.Windows.MessageBox]::Show("Installer build failed. Check log output for details.", "Build Failed", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Error)
                }
            }
        }
    }.GetNewClosure()

    $timer.Add_Tick($tickHandler)
    $timer.Start()
})

# "Build & Publish Release" Handler
$btnPublish.Add_Click({
    $tag = $txtTag.Text.Trim()
    $title = $txtTitle.Text.Trim()
    $token = $txtToken.Password.Trim()
    $notes = $txtNotes.Text
    $shouldBuild = $chkBuildFirst.IsChecked -eq $true
    $isPre = $chkPreRelease.IsChecked -eq $true

    if (-not $tag) {
        [System.Windows.MessageBox]::Show("Release Tag is required (e.g. v1.0.3).", "Validation Error", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }
    if (-not $title) {
        [System.Windows.MessageBox]::Show("Release Title is required.", "Validation Error", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }
    if (-not $token) {
        [System.Windows.MessageBox]::Show("Please enter a GitHub Personal Access Token (PAT) with 'repo' scope to publish to ZerroDevs/AgentsHub.", "Validation Error", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }

    $cleanVer = Sync-VersionToCsproj $tag

    # Save token for convenience
    try {
        Set-Content -Path $tokenFile -Value $token -NoNewline
        $lblTokenStatus.Text = "Saved token in .release_token."
    } catch {}

    $btnPublish.IsEnabled = $false
    $btnBuildOnly.IsEnabled = $false
    $pbUpload.Value = 0
    $pbUpload.Visibility = [System.Windows.Visibility]::Collapsed
    $lblProgress.Text = ""
    $txtLog.Text = ""
    Write-LogMessage "Starting release process for $tag..."

    $logQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $resultQueue = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()

    $workerScript = {
        param($rootDir, $tag, $cleanVer, $title, $token, $notes, $shouldBuild, $isPre, $lq, $rq)

        function Log($m) {
            $lq.Enqueue($m)
        }

        try {
            # 1. Build installer if requested
            if ($shouldBuild) {
                Log "[1/3] Compiling application and Inno Setup installer package..."
                $buildCmd = Join-Path $rootDir "build-installer.cmd"
                $psi = [System.Diagnostics.ProcessStartInfo]::new("cmd.exe", "/c `"$buildCmd`"")
                $psi.WorkingDirectory = $rootDir
                $psi.RedirectStandardOutput = $true
                $psi.RedirectStandardError = $true
                $psi.UseShellExecute = $false
                $psi.CreateNoWindow = $true

                $proc = [System.Diagnostics.Process]::Start($psi)
                while (-not $proc.StandardOutput.EndOfStream) {
                    $line = $proc.StandardOutput.ReadLine()
                    if ($line -match '\[\*\]|\[1/2\]|\[2/2\]|SUCCESS') {
                        Log "  $line"
                    }
                }
                $proc.WaitForExit()

                if ($proc.ExitCode -ne 0) {
                    Log "[ERROR] build-installer.cmd exited with code $($proc.ExitCode)."
                    $rq.Enqueue(@{ Success = $false; Message = "Build failed." })
                    return
                }
            }

            # 2. Check installer file existence
            $distDir = Join-Path $rootDir "dist"
            $installer = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup-v$cleanVer.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $installer) {
                $installer = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            }

            if (-not $installer) {
                Log "[ERROR] Expected installer not found in: $distDir"
                $rq.Enqueue(@{ Success = $false; Message = "Installer missing in dist\" })
                return
            }

            $installerPath = $installer.FullName
            $installerFileName = $installer.Name
            $fileInfo = Get-Item $installerPath
            $sizeMb = [Math]::Round($fileInfo.Length / 1MB, 2)
            Log "[*] Verified installer binary ($sizeMb MB): $installerPath"

            # 3. Create Release on GitHub
            Log "[2/3] Calling GitHub Releases API on ZerroDevs/AgentsHub..."
            $repo = "ZerroDevs/AgentsHub"

            $httpClient = [System.Net.Http.HttpClient]::new()
            $httpClient.Timeout = [TimeSpan]::FromMinutes(10)
            $httpClient.DefaultRequestHeaders.Add("User-Agent", "AgentsHub-Publisher/1.0")
            $httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer $token")
            $httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json")

            $releaseBody = @{
                tag_name = $tag
                name = $title
                body = $notes
                draft = $false
                prerelease = [bool]$isPre
            } | ConvertTo-Json

            $jsonContent = [System.Net.Http.StringContent]::new($releaseBody, [System.Text.Encoding]::UTF8, "application/json")
            $createResp = $httpClient.PostAsync("https://api.github.com/repos/$repo/releases", $jsonContent).GetAwaiter().GetResult()

            if (-not $createResp.IsSuccessStatusCode) {
                $errContent = $createResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                Log "[ERROR] GitHub Release creation failed (Status: $($createResp.StatusCode)): $errContent"
                $rq.Enqueue(@{ Success = $false; Message = "GitHub API error: $errContent" })
                return
            }

            $respJson = $createResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $releaseData = $respJson | ConvertFrom-Json
            $uploadUrlTemplate = $releaseData.upload_url
            $htmlUrl = $releaseData.html_url

            Log "[*] Release record created ($($releaseData.tag_name))."
            Log "[3/3] Uploading $installerFileName ($sizeMb MB)..."

            # 4. Upload Setup Binary Asset
            $uploadUrl = $uploadUrlTemplate -replace '\{\?name,label\}', "?name=$installerFileName"
            $fileLength = [System.IO.FileInfo]::new($installerPath).Length
            $fileStream = [System.IO.File]::OpenRead($installerPath)
            $progressStream = [AgentsHubPublisher.UploadProgressStream]::new($fileStream, $fileLength, $lq)
            try {
                $streamContent = [System.Net.Http.StreamContent]::new($progressStream, 65536)
                $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
                $streamContent.Headers.ContentLength = $fileLength

                $uploadResp = $httpClient.PostAsync($uploadUrl, $streamContent).GetAwaiter().GetResult()
                if (-not $uploadResp.IsSuccessStatusCode) {
                    $errContent = $uploadResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    Log "[ERROR] Asset upload failed (Status: $($uploadResp.StatusCode)): $errContent"
                    $rq.Enqueue(@{ Success = $false; Message = "Asset upload error: $errContent" })
                    return
                }
            } finally {
                $progressStream.Dispose()
            }

            Log ""
            Log "=========================================================="
            Log "SUCCESS: Release $tag is now live on GitHub!"
            Log "URL: $htmlUrl"
            Log "=========================================================="

            $rq.Enqueue(@{ Success = $true; Tag = $tag; Url = $htmlUrl; Size = $sizeMb })
        } catch {
            Log "[EXCEPTION] $($_.Exception.Message)"
            $rq.Enqueue(@{ Success = $false; Message = $_.Exception.Message })
        }
    }

    $psInstance = [PowerShell]::Create().AddScript($workerScript).AddArgument($rootDir).AddArgument($tag).AddArgument($cleanVer).AddArgument($title).AddArgument($token).AddArgument($notes).AddArgument($shouldBuild).AddArgument($isPre).AddArgument($logQueue).AddArgument($resultQueue)
    $asyncHandle = $psInstance.BeginInvoke()

    $timer = [System.Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds(100)
    $tickHandler = {
        $m = $null
        while ($logQueue.TryDequeue([ref]$m)) {
            if ($m -like "PROGRESS:*") {
                $parts = $m.Split(':')
                $pct = [int]$parts[1]
                $mbSent = $parts[2]
                $mbTot = $parts[3]

                $pbUpload.Visibility = [System.Windows.Visibility]::Visible
                $pbUpload.Value = $pct
                $lblProgress.Text = "Uploading: $pct% ($mbSent / $mbTot MB)"
            } else {
                Write-LogMessage $m
            }
        }

        if ($asyncHandle.IsCompleted) {
            $timer.Stop()
            $finalM = $null
            while ($logQueue.TryDequeue([ref]$finalM)) {
                if ($finalM -notlike "PROGRESS:*") {
                    Write-LogMessage $finalM
                }
            }

            try { $psInstance.EndInvoke($asyncHandle) | Out-Null } catch {}
            $psInstance.Dispose()

            $btnPublish.IsEnabled = $true
            $btnBuildOnly.IsEnabled = $true

            $res = $null
            if ($resultQueue.TryDequeue([ref]$res)) {
                if ($res.Success) {
                    $pbUpload.Value = 100
                    $lblProgress.Text = "Published Successfully!"
                    $dlg = [System.Windows.MessageBox]::Show("Release $($res.Tag) was published to GitHub successfully with installer attached ($($res.Size) MB)!`n`nWould you like to open the release in your browser?", "Release Published", [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Information)
                    if ($dlg -eq [System.Windows.MessageBoxResult]::Yes) {
                        [System.Diagnostics.Process]::Start([System.Diagnostics.ProcessStartInfo]@{
                            FileName = $res.Url
                            UseShellExecute = $true
                        })
                    }
                } else {
                    $lblProgress.Text = "Publish Failed"
                    [System.Windows.MessageBox]::Show("Failed to publish release: $($res.Message)", "Publish Failed", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Error)
                }
            }
        }
    }.GetNewClosure()

    $timer.Add_Tick($tickHandler)
    $timer.Start()
})

# Show UI
[void]$window.ShowDialog()
