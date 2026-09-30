# Run the unit tests and publish a compact report (totals + failed tests with messages).
param(
    [string]$Solution = 'Orbix.sln',
    [string]$Configuration = 'Release'
)

. "$PSScriptRoot/Annotate.ps1"

$results = Join-Path $PWD 'TestResults'
Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
$log = Join-Path $PWD 'test.log'

dotnet test $Solution -c $Configuration --no-build --nologo `
    --logger 'trx;LogFileName=tests.trx' --results-directory $results 2>&1 | Tee-Object -FilePath $log | Out-Host
$code = $LASTEXITCODE

$trx = Get-ChildItem $results -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($trx) {
    [xml]$xml = Get-Content $trx.FullName -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = $xml.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
    $report = New-Object System.Collections.Generic.List[string]
    $report.Add("total=$($counters.total) passed=$($counters.passed) failed=$($counters.failed) notExecuted=$($counters.notExecuted) exit=$code")

    foreach ($r in $xml.SelectNodes('//t:UnitTestResult[@outcome="Failed"]', $ns)) {
        $report.Add('')
        $report.Add("FAILED: $($r.testName)")
        $msg = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
        $stack = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
        if ($msg) { $report.Add(($msg.InnerText.Trim())) }
        if ($stack) {
            $top = ($stack.InnerText -split "`n" | Select-Object -First 4 | ForEach-Object { Format-RepoPath $_.Trim() }) -join "`n"
            $report.Add($top)
        }
    }
    Write-Annotation 'test-report' ($report -join "`n")
}
else {
    Write-Annotation 'test-report' ("no trx file produced, exit=$code`n" + ((Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 80) -join "`n"))
}

exit $code
