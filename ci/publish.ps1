# CI wrapper around build/publish.ps1: same output, but the result (or the failure reason) becomes an annotation.
param(
    [string]$Variant = 'portable',
    [string]$Version = ''
)

. "$PSScriptRoot/Annotate.ps1"

$log = Join-Path $PWD 'publish.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
$failed = $false
try {
    $publishArgs = @{ Variant = $Variant }
    if ($Version) { $publishArgs['Version'] = $Version }
    & (Join-Path $PSScriptRoot '../build/publish.ps1') @publishArgs *>&1 | Tee-Object -FilePath $log | Out-Host
}
catch {
    $failed = $true
    Add-Content $log ("EXCEPTION: " + $_)
}

$lines = @(Get-Content $log -ErrorAction SilentlyContinue)
$errors = @($lines | Where-Object { $_ -match '\berror\s+[A-Za-z]+\d+|EXCEPTION' } | ForEach-Object { Format-RepoPath $_ } | Select-Object -Unique)
$files = @(Get-ChildItem (Join-Path $PWD 'artifacts') -Recurse -Filter '*.exe' -ErrorAction SilentlyContinue |
    ForEach-Object { '{0} {1:N1} MB' -f (Resolve-Path -Relative $_.FullName), ($_.Length / 1MB) })

Write-Annotation 'publish-summary' ("failed=$failed seconds=$([int]$sw.Elapsed.TotalSeconds)`n" + ($files -join "`n"))
if ($failed -or $errors.Count -gt 0) {
    Write-Annotation 'publish-errors' ((@($errors | Select-Object -First 40) + @('--- tail ---') + @($lines | Select-Object -Last 25)) -join "`n")
    exit 1
}
