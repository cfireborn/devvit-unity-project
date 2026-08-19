[CmdletBinding()]
param(
    [switch]$Background,
    [switch]$BuildOnly,
    [switch]$DryRun,
    [Parameter(Position = 0)]
    [string]$SourceRef = "HEAD"
)

$ErrorActionPreference = "Stop"

function Find-GitForWindowsRoot {
    $GitCommand = Get-Command git.exe -ErrorAction Stop
    $Current = [System.IO.DirectoryInfo](Split-Path -Parent $GitCommand.Source)

    for ($Depth = 0; $Depth -lt 5 -and $null -ne $Current; $Depth++) {
        $Bash = Join-Path $Current.FullName "bin\bash.exe"
        $Cygpath = Join-Path $Current.FullName "usr\bin\cygpath.exe"
        if ((Test-Path -LiteralPath $Bash -PathType Leaf) -and (Test-Path -LiteralPath $Cygpath -PathType Leaf)) {
            return @($Bash, $Cygpath)
        }
        $Current = $Current.Parent
    }

    throw "Could not find Git Bash next to git.exe. Install Git for Windows, then try again."
}

if ($env:OS -ne "Windows_NT") {
    throw "This wrapper is for Windows. On macOS run ./scripts/publish-webgl.sh instead."
}

$GitTools = Find-GitForWindowsRoot
$BashExe = $GitTools[0]
$CygpathExe = $GitTools[1]
$ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublisherWindowsPath = Join-Path $ScriptDirectory "publish-webgl.sh"
$PublisherBashPath = (& $CygpathExe -u $PublisherWindowsPath).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($PublisherBashPath)) {
    throw "Could not translate the publisher path for Git Bash."
}

$PublisherArguments = @($PublisherBashPath)
if ($Background) { $PublisherArguments += "--background" }
if ($BuildOnly) { $PublisherArguments += "--build-only" }
if ($DryRun) { $PublisherArguments += "--dry-run" }
$PublisherArguments += $SourceRef

& $BashExe @PublisherArguments
exit $LASTEXITCODE
