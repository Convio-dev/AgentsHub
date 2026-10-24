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
        private readonly string _prefix;

        public UploadProgressStream(Stream inner, long totalBytes, ConcurrentQueue<string> queue, string prefix = "") {
            _inner = inner;
            _totalBytes = totalBytes;
            _queue = queue;
            _prefix = prefix ?? string.Empty;
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
                    _queue.Enqueue(string.Format("PROGRESS:{0}:{1:0.00}:{2:0.00}:{3}", currentPercent, mbSent, mbTotal, _prefix));
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
$currentVersion = "1.0.4"
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
        Title="Agents Hub Multi-Platform Release Publisher" Height="860" Width="760"
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
                        <TextBlock Text=" - GitHub Release Publisher" FontWeight="SemiBold" FontSize="16" Foreground="#F8FAFC" />
                    </StackPanel>
                    <TextBlock Text="Target: https://github.com/ZerroDevs/AgentsHub (Windows, Linux, macOS)" FontSize="10.5" Foreground="#94A3B8" Margin="0,3,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Name="btnOpenDist" Content="Open Dist Folder" 
                            Background="#1E293B" Foreground="#94A3B8" BorderThickness="1" BorderBrush="#334155"
                            Padding="10,5" FontSize="11" Cursor="Hand" Margin="0,0,8,0" />
                    <Button Name="btnOpenReleases" Content="View Live Releases" 
                            Background="#1E293B" Foreground="#94A3B8" BorderThickness="1" BorderBrush="#334155"
                            Padding="10,5" FontSize="11" Cursor="Hand" />
                </StackPanel>
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
                <TextBlock Grid.Column="1" Name="lblTokenStatus" Text="" FontSize="10.5" FontWeight="SemiBold" Foreground="#10B981" />
            </Grid>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="6" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="6" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="6" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Grid Grid.Column="0">
                    <PasswordBox Name="txtToken" />
                    <TextBox Name="txtTokenPlain" Visibility="Collapsed" />
                </Grid>
                <Button Grid.Column="2" Name="btnToggleTokenVisibility" Content="Show" 
                        Background="#1E293B" Foreground="#94A3B8" BorderThickness="1" BorderBrush="#334155"
                        Padding="10,5" FontSize="11" Cursor="Hand" ToolTip="Toggle token visibility" />
                <Button Grid.Column="4" Name="btnSaveToken" Content="Save PAT" 
                        Background="#1E293B" Foreground="#10B981" BorderThickness="1" BorderBrush="#10B981"
                        Padding="12,5" FontSize="11" FontWeight="SemiBold" Cursor="Hand" ToolTip="Save token securely to local .release_token and verify with GitHub" />
                <Button Grid.Column="6" Name="btnForgetToken" Content="Clear" 
                        Background="#1E293B" Foreground="#EF4444" BorderThickness="1" BorderBrush="#334155"
                        Padding="10,5" FontSize="11" Cursor="Hand" ToolTip="Delete saved token from disk" />
            </Grid>
            <TextBlock Text="Requires 'repo' scope. Saved securely to local .release_token (git-ignored)." FontSize="10" Foreground="#64748B" Margin="0,4,0,0" />
        </StackPanel>

        <!-- Form Row 3: Target Platforms -->
        <Border Grid.Row="3" Background="#111827" BorderBrush="#1E293B" BorderThickness="1" CornerRadius="4" Padding="12,10" Margin="0,0,0,12">
            <StackPanel>
                <Grid Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="Target Platforms to Build &amp; Publish" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" />
                        <TextBlock Text=" (Default: All Selected)" FontSize="10" Foreground="#10B981" Margin="6,1,0,0" />
                    </StackPanel>
                    <StackPanel Grid.Column="1" Orientation="Horizontal">
                        <Button Name="btnSelectAllTargets" Content="Select All" 
                                Background="Transparent" Foreground="#38BDF8" BorderThickness="0"
                                FontSize="10.5" Cursor="Hand" Margin="0,0,10,0" />
                        <Button Name="btnSelectWinTarget" Content="Windows Only" 
                                Background="Transparent" Foreground="#94A3B8" BorderThickness="0"
                                FontSize="10.5" Cursor="Hand" Margin="0,0,10,0" />
                        <Button Name="btnClearTargets" Content="Clear" 
                                Background="Transparent" Foreground="#64748B" BorderThickness="0"
                                FontSize="10.5" Cursor="Hand" />
                    </StackPanel>
                </Grid>
                <WrapPanel Orientation="Horizontal">
                    <CheckBox Name="chkTargetWin" Content="Windows x64 (.exe installer + .zip portable)" IsChecked="True" Margin="0,3,18,3" />
                    <CheckBox Name="chkTargetLinuxX64" Content="Linux x64 (.tar.gz)" IsChecked="True" Margin="0,3,18,3" />
                    <CheckBox Name="chkTargetLinuxArm64" Content="Linux ARM64 (.tar.gz)" IsChecked="True" Margin="0,3,18,3" />
                    <CheckBox Name="chkTargetMacArm64" Content="macOS Apple Silicon (.tar.gz)" IsChecked="True" Margin="0,3,18,3" />
                    <CheckBox Name="chkTargetMacX64" Content="macOS Intel (.tar.gz)" IsChecked="True" Margin="0,3,8,3" />
                </WrapPanel>
            </StackPanel>
        </Border>

        <!-- Form Row 4: Release Notes -->
        <StackPanel Grid.Row="4" Margin="0,0,0,12">
            <Grid Margin="0,0,0,4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Release Notes (Markdown)" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" />
                <Button Grid.Column="1" Name="btnGenerateNotes" Content="Auto-Generate from Git" 
                        Background="#1E293B" Foreground="#38BDF8" BorderThickness="1" BorderBrush="#0284C7"
                        Padding="8,2" FontSize="10.5" FontWeight="SemiBold" Cursor="Hand" ToolTip="Auto-populate release notes from recent git commits since previous tag" />
            </Grid>
            <TextBox Name="txtNotes" Height="130" TextWrapping="Wrap" AcceptsReturn="True" VerticalScrollBarVisibility="Auto"
                     FontFamily="Consolas" FontSize="11.5"
                     Text="## What's Changed in Agents Hub v$currentVersion&#x0d;&#x0a;&#x0d;&#x0a;### Cross-Platform Support&#x0d;&#x0a;- Windows x64 (Native WinForms + Inno Setup)&#x0d;&#x0a;- Linux x64 &amp; Linux ARM64 (Self-contained tar.gz)&#x0d;&#x0a;- macOS Apple Silicon &amp; macOS Intel (Self-contained tar.gz)&#x0d;&#x0a;&#x0d;&#x0a;### Features &amp; Enhancements&#x0d;&#x0a;- Multi-target release engine with selective publisher GUI&#x0d;&#x0a;- Headless POSIX runtime with web control dashboard&#x0d;&#x0a;&#x0d;&#x0a;### Bug Fixes&#x0d;&#x0a;- " />
        </StackPanel>

        <!-- Form Row 5: Console Output & Status -->
        <Grid Grid.Row="5" Margin="0,0,0,14">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <Grid Grid.Row="0" Margin="0,0,0,4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="10" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Activity Log" FontSize="11" FontWeight="SemiBold" Foreground="#94A3B8" />
                <Button Grid.Column="1" Name="btnClearLog" Content="Clear Log" 
                        Background="Transparent" Foreground="#64748B" BorderThickness="0"
                        FontSize="10" Cursor="Hand" />
                <TextBlock Grid.Column="3" Name="lblProgress" Text="" FontSize="11" FontWeight="SemiBold" Foreground="#10B981" />
            </Grid>

            <TextBox Grid.Row="1" Name="txtLog" Background="#060911" Foreground="#38BDF8" BorderBrush="#1E293B"
                     IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                     FontFamily="Consolas" FontSize="11" Padding="8" />

            <ProgressBar Grid.Row="2" Name="pbUpload" Height="6" Margin="0,6,0,0" Background="#1E293B" Foreground="#10B981"
                         BorderThickness="0" Minimum="0" Maximum="100" Value="0" Visibility="Collapsed" />
        </Grid>

        <!-- Footer / Action Buttons -->
        <Grid Grid.Row="6">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                <CheckBox Name="chkPreRelease" Content="Pre-release" Margin="0,0,16,0" />
                <CheckBox Name="chkBuildFirst" Content="Build Selected Targets First" IsChecked="True" />
            </StackPanel>

            <Button Grid.Column="2" Name="btnBuildOnly" Content="Build Selected" 
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
$txtTokenPlain = $window.FindName("txtTokenPlain")
$btnToggleTokenVisibility = $window.FindName("btnToggleTokenVisibility")
$btnSaveToken = $window.FindName("btnSaveToken")
$btnForgetToken = $window.FindName("btnForgetToken")
$lblTokenStatus = $window.FindName("lblTokenStatus")
$btnGenerateNotes = $window.FindName("btnGenerateNotes")
$txtNotes = $window.FindName("txtNotes")
$txtLog = $window.FindName("txtLog")
$lblProgress = $window.FindName("lblProgress")
$pbUpload = $window.FindName("pbUpload")
$chkPreRelease = $window.FindName("chkPreRelease")
$chkBuildFirst = $window.FindName("chkBuildFirst")
$btnBuildOnly = $window.FindName("btnBuildOnly")
$btnPublish = $window.FindName("btnPublish")
$btnOpenReleases = $window.FindName("btnOpenReleases")
$btnOpenDist = $window.FindName("btnOpenDist")
$btnClearLog = $window.FindName("btnClearLog")

# Target platform controls
$chkTargetWin = $window.FindName("chkTargetWin")
$chkTargetLinuxX64 = $window.FindName("chkTargetLinuxX64")
$chkTargetLinuxArm64 = $window.FindName("chkTargetLinuxArm64")
$chkTargetMacArm64 = $window.FindName("chkTargetMacArm64")
$chkTargetMacX64 = $window.FindName("chkTargetMacX64")
$btnSelectAllTargets = $window.FindName("btnSelectAllTargets")
$btnSelectWinTarget = $window.FindName("btnSelectWinTarget")
$btnClearTargets = $window.FindName("btnClearTargets")

function Get-SelectedTargets() {
    $targets = [System.Collections.Generic.List[string]]::new()
    if ($chkTargetWin.IsChecked -eq $true) { $targets.Add("win-x64") }
    if ($chkTargetLinuxX64.IsChecked -eq $true) { $targets.Add("linux-x64") }
    if ($chkTargetLinuxArm64.IsChecked -eq $true) { $targets.Add("linux-arm64") }
    if ($chkTargetMacArm64.IsChecked -eq $true) { $targets.Add("osx-arm64") }
    if ($chkTargetMacX64.IsChecked -eq $true) { $targets.Add("osx-x64") }
    return $targets.ToArray()
}

$btnSelectAllTargets.Add_Click({
    $chkTargetWin.IsChecked = $true
    $chkTargetLinuxX64.IsChecked = $true
    $chkTargetLinuxArm64.IsChecked = $true
    $chkTargetMacArm64.IsChecked = $true
    $chkTargetMacX64.IsChecked = $true
})

$btnSelectWinTarget.Add_Click({
    $chkTargetWin.IsChecked = $true
    $chkTargetLinuxX64.IsChecked = $false
    $chkTargetLinuxArm64.IsChecked = $false
    $chkTargetMacArm64.IsChecked = $false
    $chkTargetMacX64.IsChecked = $false
})

$btnClearTargets.Add_Click({
    $chkTargetWin.IsChecked = $false
    $chkTargetLinuxX64.IsChecked = $false
    $chkTargetLinuxArm64.IsChecked = $false
    $chkTargetMacArm64.IsChecked = $false
    $chkTargetMacX64.IsChecked = $false
})

function Get-ActiveToken() {
    if ($txtTokenPlain.Visibility -eq [System.Windows.Visibility]::Visible) {
        return $txtTokenPlain.Text.Trim()
    } else {
        return $txtToken.Password.Trim()
    }
}

function Set-ActiveToken($val) {
    $txtToken.Password = $val
    $txtTokenPlain.Text = $val
}

if ($savedToken) {
    Set-ActiveToken $savedToken
    $lblTokenStatus.Text = "Saved in .release_token"
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

# Toggle Token Visibility Handler
$btnToggleTokenVisibility.Add_Click({
    if ($txtTokenPlain.Visibility -eq [System.Windows.Visibility]::Visible) {
        $txtToken.Password = $txtTokenPlain.Text
        $txtTokenPlain.Visibility = [System.Windows.Visibility]::Collapsed
        $txtToken.Visibility = [System.Windows.Visibility]::Visible
        $btnToggleTokenVisibility.Content = "Show"
    } else {
        $txtTokenPlain.Text = $txtToken.Password
        $txtToken.Visibility = [System.Windows.Visibility]::Collapsed
        $txtTokenPlain.Visibility = [System.Windows.Visibility]::Visible
        $btnToggleTokenVisibility.Content = "Hide"
    }
})

# Save Token Handler
$btnSaveToken.Add_Click({
    $tokenVal = Get-ActiveToken
    if (-not $tokenVal) {
        $lblTokenStatus.Text = "Enter a token first"
        $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::Orange
        return
    }

    try {
        Set-Content -Path $tokenFile -Value $tokenVal -NoNewline -Force
        $lblTokenStatus.Text = "Verifying token with GitHub..."
        $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::SkyBlue
        Write-LogMessage "[*] Saved PAT locally. Testing GitHub connection..."

        # Asynchronous validation against GitHub API
        [System.Threading.Tasks.Task]::Run([Action]{
            try {
                $client = [System.Net.Http.HttpClient]::new()
                $client.Timeout = [TimeSpan]::FromSeconds(6)
                $client.DefaultRequestHeaders.Add("User-Agent", "AgentsHub-Publisher/1.0")
                $client.DefaultRequestHeaders.Add("Authorization", "Bearer $tokenVal")
                $client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json")

                $resp = $client.GetAsync("https://api.github.com/user").GetAwaiter().GetResult()
                if ($resp.IsSuccessStatusCode) {
                    $json = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                    $user = $json.login
                    $window.Dispatcher.Invoke([Action]{
                        $lblTokenStatus.Text = "Verified (@$user)"
                        $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::LimeGreen
                        Write-LogMessage "[+] GitHub PAT verified successfully for user: @$user"
                    })
                } else {
                    $window.Dispatcher.Invoke([Action]{
                        $lblTokenStatus.Text = "Invalid Token (Status $($resp.StatusCode))"
                        $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::Tomato
                        Write-LogMessage "[!] GitHub authentication failed: Status $($resp.StatusCode)"
                    })
                }
            } catch {
                $errMsg = $_.Exception.Message
                $window.Dispatcher.Invoke([Action]{
                    $lblTokenStatus.Text = "Saved (Offline)"
                    $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::LightGreen
                    Write-LogMessage "[*] Saved locally. GitHub test skipped ($errMsg)."
                })
            }
        })
    } catch {
        $lblTokenStatus.Text = "Save error: $_"
        $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::Tomato
    }
})

# Forget Token Handler
$btnForgetToken.Add_Click({
    if (Test-Path $tokenFile) {
        Remove-Item $tokenFile -Force -ErrorAction SilentlyContinue
    }
    Set-ActiveToken ""
    $lblTokenStatus.Text = "Cleared"
    $lblTokenStatus.Foreground = [System.Windows.Media.Brushes]::Tomato
    Write-LogMessage "[-] Saved PAT removed from disk."
})

# Auto-Generate Release Notes from Git Commits Handler
$btnGenerateNotes.Add_Click({
    try {
        $prevTag = git describe --tags --abbrev=0 2>$null
        $logRange = if ($prevTag) { "$prevTag..HEAD" } else { "-20" }
        Write-LogMessage "[*] Inspecting Git history ($logRange)..."

        $commits = git log $logRange --oneline 2>$null
        if (-not $commits) {
            Write-LogMessage "[!] No commits found in range $logRange."
            return
        }

        $features = [System.Collections.Generic.List[string]]::new()
        $fixes = [System.Collections.Generic.List[string]]::new()
        $improvements = [System.Collections.Generic.List[string]]::new()

        foreach ($line in $commits) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^[0-9a-f]+\s+(?:feat|feature)(?:\([^)]+\))?:\s*(.+)$') {
                $features.Add("- $($matches[1].Trim())")
            } elseif ($trimmed -match '^[0-9a-f]+\s+(?:fix|bug)(?:\([^)]+\))?:\s*(.+)$') {
                $fixes.Add("- $($matches[1].Trim())")
            } elseif ($trimmed -match '^[0-9a-f]+\s+(?:refactor|perf|docs|chore)(?:\([^)]+\))?:\s*(.+)$') {
                $improvements.Add("- $($matches[1].Trim())")
            } else {
                $cleaned = $trimmed -replace '^[0-9a-f]+\s+', ''
                $improvements.Add("- $cleaned")
            }
        }

        $md = [System.Text.StringBuilder]::new()
        $tag = $txtTag.Text.Trim()
        $md.AppendLine("## What's Changed in Agents Hub $tag") | Out-Null
        $md.AppendLine("") | Out-Null

        $selected = Get-SelectedTargets
        if ($selected.Length -gt 0) {
            $md.AppendLine("### Platforms Included") | Out-Null
            if ($selected -contains "win-x64") { $md.AppendLine("- Windows x64 (Native Inno Setup Installer & Portable Zip)") | Out-Null }
            if ($selected -contains "linux-x64") { $md.AppendLine("- Linux x64 (Self-contained standalone archive)") | Out-Null }
            if ($selected -contains "linux-arm64") { $md.AppendLine("- Linux ARM64 (Self-contained standalone archive)") | Out-Null }
            if ($selected -contains "osx-arm64") { $md.AppendLine("- macOS Apple Silicon (Self-contained standalone archive)") | Out-Null }
            if ($selected -contains "osx-x64") { $md.AppendLine("- macOS Intel x64 (Self-contained standalone archive)") | Out-Null }
            $md.AppendLine("") | Out-Null
        }

        if ($features.Count -gt 0) {
            $md.AppendLine("### Features & Enhancements") | Out-Null
            foreach ($f in $features) { $md.AppendLine($f) | Out-Null }
            $md.AppendLine("") | Out-Null
        }

        if ($fixes.Count -gt 0) {
            $md.AppendLine("### Bug Fixes") | Out-Null
            foreach ($fx in $fixes) { $md.AppendLine($fx) | Out-Null }
            $md.AppendLine("") | Out-Null
        }

        if ($improvements.Count -gt 0) {
            $md.AppendLine("### Improvements & Refactoring") | Out-Null
            foreach ($imp in $improvements) { $md.AppendLine($imp) | Out-Null }
            $md.AppendLine("") | Out-Null
        }

        $txtNotes.Text = $md.ToString()
        Write-LogMessage "[+] Generated release notes from $($commits.Count) commits."
    } catch {
        Write-LogMessage "[!] Failed to auto-generate release notes: $_"
    }
})

$btnClearLog.Add_Click({
    $txtLog.Text = ""
})

$btnOpenDist.Add_Click({
    $distDir = Join-Path $rootDir "dist"
    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }
    [System.Diagnostics.Process]::Start("explorer.exe", $distDir)
})

$btnOpenReleases.Add_Click({
    [System.Diagnostics.Process]::Start([System.Diagnostics.ProcessStartInfo]@{
        FileName = "https://github.com/ZerroDevs/AgentsHub/releases"
        UseShellExecute = $true
    })
})

# Helper function to discover assets matching selected targets
function Find-TargetAssets($distDir, $cleanVer, $targets) {
    $assets = [System.Collections.Generic.List[psobject]]::new()

    foreach ($target in $targets) {
        switch ($target) {
            "win-x64" {
                # Setup exe
                $setup = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup-v$cleanVer.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $setup) {
                    $setup = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }
                if ($setup) {
                    $assets.Add([PSCustomObject]@{ Path = $setup.FullName; Name = $setup.Name; Target = "win-x64"; Kind = "Installer" })
                }
                # Portable zip
                $zip = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-win-x64.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $zip) {
                    $zip = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-win-x64.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }
                if ($zip) {
                    $assets.Add([PSCustomObject]@{ Path = $zip.FullName; Name = $zip.Name; Target = "win-x64"; Kind = "Zip" })
                }
            }
            "linux-x64" {
                $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-linux-x64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $tar) {
                    $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-linux-x64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }
                if ($tar) {
                    $assets.Add([PSCustomObject]@{ Path = $tar.FullName; Name = $tar.Name; Target = "linux-x64"; Kind = "Tarball" })
                }
            }
            "linux-arm64" {
                $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-linux-arm64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $tar) {
                    $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-linux-arm64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }
                if ($tar) {
                    $assets.Add([PSCustomObject]@{ Path = $tar.FullName; Name = $tar.Name; Target = "linux-arm64"; Kind = "Tarball" })
                }
            }
            "osx-arm64" {
                $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-osx-arm64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $tar) {
                    $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-osx-arm64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }
                if ($tar) {
                    $assets.Add([PSCustomObject]@{ Path = $tar.FullName; Name = $tar.Name; Target = "osx-arm64"; Kind = "Tarball" })
                }
            }
            "osx-x64" {
                $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-osx-x64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $tar) {
                    $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-osx-x64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                }
                if ($tar) {
                    $assets.Add([PSCustomObject]@{ Path = $tar.FullName; Name = $tar.Name; Target = "osx-x64"; Kind = "Tarball" })
                }
            }
        }
    }

    # Deduplicate by Path
    $seen = @{}
    $unique = [System.Collections.Generic.List[psobject]]::new()
    foreach ($a in $assets) {
        if (-not $seen.ContainsKey($a.Path)) {
            $seen[$a.Path] = $true
            $unique.Add($a)
        }
    }
    return $unique.ToArray()
}

# "Build Selected" Handler
$btnBuildOnly.Add_Click({
    $tag = $txtTag.Text.Trim()
    $selectedTargets = Get-SelectedTargets

    if ($selectedTargets.Length -eq 0) {
        [System.Windows.MessageBox]::Show("Please select at least one target platform to build.", "No Target Selected", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }

    $cleanVer = Sync-VersionToCsproj $tag

    $btnBuildOnly.IsEnabled = $false
    $btnPublish.IsEnabled = $false
    $pbUpload.Visibility = [System.Windows.Visibility]::Collapsed
    $lblProgress.Text = "Building targets..."
    $txtLog.Text = ""
    Write-LogMessage "Compiling packages for targets: $($selectedTargets -join ', ')..."

    $logQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $resultQueue = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()

    $workerScript = {
        param($rootDir, $cleanVer, $targets, $lq, $rq)
        try {
            $targetsArg = $targets -join " "
            $buildScript = Join-Path $rootDir "tools\BuildTargets.ps1"
            $psi = [System.Diagnostics.ProcessStartInfo]::new("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File `"$buildScript`" -Targets $targetsArg -Version $cleanVer")
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
                $rq.Enqueue(@{ Success = $true; Code = 0 })
            } else {
                $rq.Enqueue(@{ Success = $false; Code = $proc.ExitCode; Error = "Build script failed with exit code $($proc.ExitCode)" })
            }
        } catch {
            $rq.Enqueue(@{ Success = $false; Error = $_.Exception.Message })
        }
    }

    $psInstance = [PowerShell]::Create().AddScript($workerScript).AddArgument($rootDir).AddArgument($cleanVer).AddArgument($selectedTargets).AddArgument($logQueue).AddArgument($resultQueue)
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
                    $distDir = Join-Path $rootDir "dist"
                    $foundAssets = Find-TargetAssets $distDir $cleanVer $selectedTargets
                    $totalMb = 0
                    foreach ($fa in $foundAssets) {
                        $totalMb += [Math]::Round((Get-Item $fa.Path).Length / 1MB, 2)
                    }

                    $lblProgress.Text = "Build Succeeded ($($foundAssets.Length) packages)"
                    Write-LogMessage "[SUCCESS] All selected target packages built ($totalMb MB total in dist\):"
                    foreach ($fa in $foundAssets) {
                        Write-LogMessage "  + $($fa.Name) ($([Math]::Round((Get-Item $fa.Path).Length / 1MB, 2)) MB)"
                    }

                    $dlg = [System.Windows.MessageBox]::Show("$($foundAssets.Length) package(s) compiled successfully ($totalMb MB total)!`n`nWould you like to open the dist folder in File Explorer?", "Build Succeeded", [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Information)
                    if ($dlg -eq [System.Windows.MessageBoxResult]::Yes) {
                        [System.Diagnostics.Process]::Start("explorer.exe", $distDir)
                    }
                } else {
                    $lblProgress.Text = "Build Failed"
                    [System.Windows.MessageBox]::Show("Build failed ($($res.Error)). Check log output for details.", "Build Failed", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Error)
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
    $token = Get-ActiveToken
    $notes = $txtNotes.Text
    $shouldBuild = $chkBuildFirst.IsChecked -eq $true
    $isPre = $chkPreRelease.IsChecked -eq $true
    $selectedTargets = Get-SelectedTargets

    if ($selectedTargets.Length -eq 0) {
        [System.Windows.MessageBox]::Show("Please select at least one target platform to publish.", "Validation Error", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }
    if (-not $tag) {
        [System.Windows.MessageBox]::Show("Release Tag is required (e.g. v1.0.4).", "Validation Error", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
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
        $lblTokenStatus.Text = "Saved in .release_token"
    } catch {}

    $btnPublish.IsEnabled = $false
    $btnBuildOnly.IsEnabled = $false
    $pbUpload.Value = 0
    $pbUpload.Visibility = [System.Windows.Visibility]::Collapsed
    $lblProgress.Text = ""
    $txtLog.Text = ""
    Write-LogMessage "Starting multi-target release process for $tag..."
    Write-LogMessage "Selected targets: $($selectedTargets -join ', ')"

    $logQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $resultQueue = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()

    $workerScript = {
        param($rootDir, $tag, $cleanVer, $title, $token, $notes, $shouldBuild, $isPre, $targets, $lq, $rq)

        function Log($m) {
            $lq.Enqueue($m)
        }

        try {
            # 1. Build targets if requested
            if ($shouldBuild) {
                Log "[1/3] Compiling application packages for selected targets: $($targets -join ', ')..."
                $targetsArg = $targets -join " "
                $buildScript = Join-Path $rootDir "tools\BuildTargets.ps1"
                $psi = [System.Diagnostics.ProcessStartInfo]::new("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File `"$buildScript`" -Targets $targetsArg -Version $cleanVer")
                $psi.WorkingDirectory = $rootDir
                $psi.RedirectStandardOutput = $true
                $psi.RedirectStandardError = $true
                $psi.UseShellExecute = $false
                $psi.CreateNoWindow = $true

                $proc = [System.Diagnostics.Process]::Start($psi)
                while (-not $proc.StandardOutput.EndOfStream) {
                    $line = $proc.StandardOutput.ReadLine()
                    if ($line) {
                        Log "  $line"
                    }
                }
                $proc.WaitForExit()

                if ($proc.ExitCode -ne 0) {
                    Log "[ERROR] tools\BuildTargets.ps1 exited with code $($proc.ExitCode)."
                    $rq.Enqueue(@{ Success = $false; Message = "Target build step failed." })
                    return
                }
            }

            # 2. Discover target assets in dist/
            $distDir = Join-Path $rootDir "dist"
            $assetsToUpload = [System.Collections.Generic.List[psobject]]::new()

            foreach ($target in $targets) {
                switch ($target) {
                    "win-x64" {
                        $setup = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup-v$cleanVer.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $setup) {
                            $setup = Get-ChildItem -Path $distDir -Filter "AgentsHub-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        }
                        if ($setup) { $assetsToUpload.Add($setup) }

                        $zip = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-win-x64.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $zip) {
                            $zip = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-win-x64.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        }
                        if ($zip) { $assetsToUpload.Add($zip) }
                    }
                    "linux-x64" {
                        $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-linux-x64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $tar) {
                            $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-linux-x64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        }
                        if ($tar) { $assetsToUpload.Add($tar) }
                    }
                    "linux-arm64" {
                        $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-linux-arm64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $tar) {
                            $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-linux-arm64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        }
                        if ($tar) { $assetsToUpload.Add($tar) }
                    }
                    "osx-arm64" {
                        $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-osx-arm64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $tar) {
                            $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-osx-arm64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        }
                        if ($tar) { $assetsToUpload.Add($tar) }
                    }
                    "osx-x64" {
                        $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-v$cleanVer-osx-x64.tar.gz" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $tar) {
                            $tar = Get-ChildItem -Path $distDir -Filter "AgentsHub-*-osx-x64.tar.gz" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        }
                        if ($tar) { $assetsToUpload.Add($tar) }
                    }
                }
            }

            # Deduplicate assets
            $uniqueAssets = @()
            $seenPaths = @{}
            foreach ($a in $assetsToUpload) {
                if (-not $seenPaths.ContainsKey($a.FullName)) {
                    $seenPaths[$a.FullName] = $true
                    $uniqueAssets += $a
                }
            }

            if ($uniqueAssets.Count -eq 0) {
                Log "[ERROR] No matching release package assets found in: $distDir"
                $rq.Enqueue(@{ Success = $false; Message = "No package assets found to upload in dist\" })
                return
            }

            Log "[*] Identified $($uniqueAssets.Count) package asset(s) ready for upload:"
            foreach ($a in $uniqueAssets) {
                $mb = [Math]::Round($a.Length / 1MB, 2)
                Log "  + $($a.Name) ($mb MB)"
            }

            # 3. Create or Fetch Release on GitHub
            Log "[2/3] Calling GitHub Releases API on ZerroDevs/AgentsHub..."
            $repo = "ZerroDevs/AgentsHub"

            $httpClient = [System.Net.Http.HttpClient]::new()
            $httpClient.Timeout = [TimeSpan]::FromMinutes(15)
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

            $releaseData = $null
            if ($createResp.IsSuccessStatusCode) {
                $respJson = $createResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $releaseData = $respJson | ConvertFrom-Json
                Log "[*] Release record created ($($releaseData.tag_name), ID: $($releaseData.id))."
            } elseif ($createResp.StatusCode -eq [System.Net.HttpStatusCode]::UnprocessableEntity) {
                # Already exists? Try to get existing release
                Log "[*] Release tag $tag may already exist on GitHub. Fetching release info..."
                $getResp = $httpClient.GetAsync("https://api.github.com/repos/$repo/releases/tags/$tag").GetAwaiter().GetResult()
                if ($getResp.IsSuccessStatusCode) {
                    $respJson = $getResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    $releaseData = $respJson | ConvertFrom-Json
                    Log "[*] Found existing release: $($releaseData.name) (ID: $($releaseData.id))"
                } else {
                    $errContent = $createResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    Log "[ERROR] GitHub Release creation failed: $errContent"
                    $rq.Enqueue(@{ Success = $false; Message = "GitHub API error: $errContent" })
                    return
                }
            } else {
                $errContent = $createResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                Log "[ERROR] GitHub Release creation failed (Status: $($createResp.StatusCode)): $errContent"
                $rq.Enqueue(@{ Success = $false; Message = "GitHub API error: $errContent" })
                return
            }

            $uploadUrlTemplate = $releaseData.upload_url
            $htmlUrl = $releaseData.html_url

            # 4. Upload Assets Sequentially
            Log "[3/3] Uploading $($uniqueAssets.Count) package asset(s) to GitHub..."
            $assetIndex = 0
            $totalSizeMb = 0

            foreach ($assetFile in $uniqueAssets) {
                $assetIndex++
                $fileName = $assetFile.Name
                $filePath = $assetFile.FullName
                $fileLength = $assetFile.Length
                $sizeMb = [Math]::Round($fileLength / 1MB, 2)
                $totalSizeMb += $sizeMb

                # Check if asset already exists on this release
                if ($releaseData.assets) {
                    $existing = $releaseData.assets | Where-Object { $_.name -eq $fileName } | Select-Object -First 1
                    if ($existing) {
                        Log "[*] Asset $fileName already attached (ID: $($existing.id)). Deleting previous upload..."
                        $delResp = $httpClient.DeleteAsync($existing.url).GetAwaiter().GetResult()
                        if ($delResp.IsSuccessStatusCode) {
                            Log "[*] Removed stale asset $fileName."
                        }
                    }
                }

                $prefix = "[$assetIndex/$($uniqueAssets.Count)] $fileName"
                Log "[*] $prefix ($sizeMb MB)..."

                $uploadUrl = $uploadUrlTemplate -replace '\{\?name,label\}', "?name=$fileName"
                $fileStream = [System.IO.File]::OpenRead($filePath)
                $progressStream = [AgentsHubPublisher.UploadProgressStream]::new($fileStream, $fileLength, $lq, $prefix)

                try {
                    $streamContent = [System.Net.Http.StreamContent]::new($progressStream, 65536)
                    $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
                    $streamContent.Headers.ContentLength = $fileLength

                    $uploadResp = $httpClient.PostAsync($uploadUrl, $streamContent).GetAwaiter().GetResult()
                    if (-not $uploadResp.IsSuccessStatusCode) {
                        $errContent = $uploadResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        Log "[ERROR] Asset upload failed for $($fileName) (Status: $($uploadResp.StatusCode)): $errContent"
                        $rq.Enqueue(@{ Success = $false; Message = "Asset upload error on $($fileName): $errContent" })
                        return
                    }
                    Log "[+] Uploaded: $fileName"
                } finally {
                    $progressStream.Dispose()
                }
            }

            Log ""
            Log "=========================================================="
            Log "SUCCESS: Release $tag is now live on GitHub!"
            Log "Uploaded $($uniqueAssets.Count) package(s) ($totalSizeMb MB total)"
            Log "URL: $htmlUrl"
            Log "=========================================================="

            $rq.Enqueue(@{ Success = $true; Tag = $tag; Url = $htmlUrl; Count = $uniqueAssets.Count; Size = $totalSizeMb })
        } catch {
            Log "[EXCEPTION] $($_.Exception.Message)"
            $rq.Enqueue(@{ Success = $false; Message = $_.Exception.Message })
        }
    }

    $psInstance = [PowerShell]::Create().AddScript($workerScript).AddArgument($rootDir).AddArgument($tag).AddArgument($cleanVer).AddArgument($title).AddArgument($token).AddArgument($notes).AddArgument($shouldBuild).AddArgument($isPre).AddArgument($selectedTargets).AddArgument($logQueue).AddArgument($resultQueue)
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
                $prefix = if ($parts.Length -gt 4) { $parts[4] } else { "" }

                $pbUpload.Visibility = [System.Windows.Visibility]::Visible
                $pbUpload.Value = $pct
                if ($prefix) {
                    $lblProgress.Text = "$($prefix): $pct% ($mbSent / $mbTot MB)"
                } else {
                    $lblProgress.Text = "Uploading: $pct% ($mbSent / $mbTot MB)"
                }
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
                    $lblProgress.Text = "Published $($res.Count) Packages Successfully!"
                    $dlg = [System.Windows.MessageBox]::Show("Release $($res.Tag) was published to GitHub successfully with $($res.Count) platform package(s) attached ($($res.Size) MB total)!`n`nWould you like to open the release in your browser?", "Release Published", [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Information)
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
