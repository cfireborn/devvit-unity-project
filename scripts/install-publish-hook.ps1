[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepositoryRoot = (& git -C (Join-Path $ScriptDirectory "..") rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    throw "Could not locate the Git repository."
}

$HookPath = Join-Path $RepositoryRoot ".githooks\post-commit"
$PublisherPath = Join-Path $RepositoryRoot "scripts\publish-webgl.sh"
$PublisherWrapperPath = Join-Path $RepositoryRoot "scripts\publish-webgl.ps1"
if (-not (Test-Path -LiteralPath $HookPath -PathType Leaf)) {
    throw "Missing $HookPath"
}
if (-not (Test-Path -LiteralPath $PublisherPath -PathType Leaf)) {
    throw "Missing $PublisherPath"
}
if (-not (Test-Path -LiteralPath $PublisherWrapperPath -PathType Leaf)) {
    throw "Missing $PublisherWrapperPath"
}

Write-Host "Checking this computer's local Unity and website-repository setup..."
$PowerShellHostPath = (Get-Process -Id $PID).Path
& $PowerShellHostPath -NoProfile -ExecutionPolicy Bypass -File $PublisherWrapperPath -DryRun
if ($LASTEXITCODE -ne 0) {
    throw "The local publisher readiness check failed; the Git hook was not activated."
}

& git -C $RepositoryRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw "Git could not configure core.hooksPath."
}

$ConfiguredPath = (& git -C $RepositoryRoot config --local --get core.hooksPath).Trim()
if ($LASTEXITCODE -ne 0 -or $ConfiguredPath -ne ".githooks") {
    throw "Git hook configuration did not stick."
}

Write-Host "Installed the Compersion [publish] trigger for this clone."
Write-Host "A commit containing [publish] will now start its exact SHA as a local background release."
Write-Host "Ordinary commits do nothing, and unsupported operating systems never fall back to GitHub Actions."
