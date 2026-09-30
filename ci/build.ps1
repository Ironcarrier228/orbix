# Restore + build the solution, then publish every compiler message as a single annotation.
param(
    [string]$Solution = 'Orbix.sln',
    [string]$Configuration = 'Release'
)

. "$PSScriptRoot/Annotate.ps1"

$log = Join-Path $PWD 'build.log'
dotnet build $Solution -c $Configuration --nologo -v minimal 2>&1 | Tee-Object -FilePath $log | Out-Host
$code = $LASTEXITCODE

$lines = @(Get-Content $log -ErrorAction SilentlyContinue)
$errors = @($lines | Where-Object { $_ -match '\berror\s+[A-Za-z]+\d+' } | ForEach-Object { Format-RepoPath $_ } | Select-Object -Unique)
$warnings = @($lines | Where-Object { $_ -match '\bwarning\s+[A-Za-z]+\d+' } | ForEach-Object { Format-RepoPath $_ } | Select-Object -Unique)

Write-Annotation 'build-summary' "exit=$code errors=$($errors.Count) warnings=$($warnings.Count)"
if ($errors.Count -gt 0) {
    Write-Annotation 'build-errors' (($errors | Select-Object -First 120) -join "`n")
}
elseif ($code -ne 0) {
    Write-Annotation 'build-failed-without-compiler-errors' (($lines | Select-Object -Last 60) -join "`n")
}
if ($warnings.Count -gt 0) {
    Write-Annotation 'build-warnings' (($warnings | Select-Object -First 60) -join "`n")
}

exit $code
