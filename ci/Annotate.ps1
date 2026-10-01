# Helpers for GitHub Actions workflow commands.
# The CI output is consumed through check-run annotations, so every report is emitted as ONE ::notice
# with all the text (newlines are percent-encoded as the workflow-command protocol requires).

$ErrorActionPreference = 'Continue'

function ConvertTo-AnnotationText([string]$Text) {
    return (($Text -replace '%', '%25') -replace "`r", '%0D') -replace "`n", '%0A'
}

function Write-Annotation {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [ValidateSet('notice', 'warning', 'error')][string]$Level = 'notice',
        [int]$Max = 60000
    )
    if ($Text.Length -gt $Max) {
        $Text = $Text.Substring(0, $Max) + "`n...(truncated, $($Text.Length) chars total)"
    }
    # Title values must not contain ':' or ',' (they are property separators).
    $safeTitle = $Title -replace '[:,]', '-'
    Write-Host ("::{0} title={1}::{2}" -f $Level, $safeTitle, (ConvertTo-AnnotationText $Text))
}

function Format-RepoPath([string]$Line) {
    $root = $env:GITHUB_WORKSPACE
    if ($root) {
        $Line = $Line.Replace($root + '\', '').Replace($root + '/', '')
    }
    # drop the trailing "[C:\...\Project.csproj]" that MSBuild appends
    return ($Line -replace '\s*\[[^\]]+\.(csproj|vbproj|sln|proj)\]\s*$', '').Trim()
}
