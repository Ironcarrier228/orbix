# Ships the screenshots of the end-to-end test through annotations (the only channel that can be read without a browser):
# every image becomes one or more base64 chunks "shot-<file>-<k>of<n>". A step may publish only ten notices, so the
# workflow calls this script several times with -Skip / -Take.
param(
    [string]$Dir = (Join-Path $PWD 'artifacts/e2e'),
    [int]$Skip = 0,
    [int]$Take = 9,
    [int]$ChunkChars = 42000
)

. "$PSScriptRoot/Annotate.ps1"

$files = @(Get-ChildItem $Dir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.jpg', '.png' -and $_.Name -ne 'wallpaper.png' } | Sort-Object Name)

$chunks = @()
foreach ($f in $files) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))
    $n = [int][math]::Ceiling($b64.Length / [double]$ChunkChars)
    for ($i = 0; $i -lt $n; $i++) {
        $len = [math]::Min($ChunkChars, $b64.Length - $i * $ChunkChars)
        $chunks += [pscustomobject]@{ Title = "shot-$($f.Name)-$($i + 1)of$n"; Text = $b64.Substring($i * $ChunkChars, $len) }
    }
}

$slice = @($chunks | Select-Object -Skip $Skip -First $Take)
foreach ($c in $slice) { Write-Annotation $c.Title $c.Text }
Write-Host "emitted $($slice.Count) of $($chunks.Count) chunks from $($files.Count) files"
