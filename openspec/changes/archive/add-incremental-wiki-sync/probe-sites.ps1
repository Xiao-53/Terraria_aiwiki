param([Parameter(Mandatory = $true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$syncSites = @(
    'https://terraria.wiki.gg/zh/api.php',
    'https://calamity.huijiwiki.com/api.php',
    'https://terraria.wiki.gg/api.php',
    'https://calamitymod.wiki.gg/api.php'
)
$syncQueries = @(
    'curtimestamp=1&list=recentchanges&rcprop=title%7Cids%7Ctimestamp%7Cloginfo&rclimit=2',
    'prop=info&titles=TerrariaAIWiki_sync_probe_nonexistent_20260915',
    'list=embeddedin&eititle=Template%3AItem%20infobox&eilimit=2',
    'list=imageusage&iutitle=File%3ATerra%20Blade.png&iulimit=2',
    'prop=imageinfo&iiprop=url%7Ctimestamp&titles=File%3ATerra%20Blade.png'
)
$syncResults = [System.Collections.Generic.List[object]]::new()
foreach ($syncSite in $syncSites) {
    foreach ($syncQuery in $syncQueries) {
        try {
            $syncTimer = [Diagnostics.Stopwatch]::StartNew()
            $syncResponse = Invoke-WebRequest -Uri ($syncSite + '?action=query&format=json&formatversion=2&' + $syncQuery) -Headers @{'User-Agent'='TerrariaAIWiki/1.0'} -TimeoutSec 15
            $syncBody = $syncResponse.Content | ConvertFrom-Json
            $syncResults.Add([ordered]@{Site=$syncSite;Query=$syncQuery;Milliseconds=$syncTimer.ElapsedMilliseconds;Status=$syncResponse.StatusCode;Body=$syncBody})
            if ($syncQuery.StartsWith('curtimestamp') -and $syncBody.continue) {
                $syncContinuation = ($syncBody.continue.PSObject.Properties | ForEach-Object { [uri]::EscapeDataString($_.Name) + '=' + [uri]::EscapeDataString([string]$_.Value) }) -join '&'
                $syncNext = Invoke-WebRequest -Uri ($syncSite + '?action=query&format=json&formatversion=2&' + $syncQuery + '&' + $syncContinuation) -Headers @{'User-Agent'='TerrariaAIWiki/1.0'} -TimeoutSec 15
                $syncResults.Add([ordered]@{Site=$syncSite;Query='continuation';Status=$syncNext.StatusCode;Body=($syncNext.Content | ConvertFrom-Json)})
            }
        } catch {
            $syncResults.Add([ordered]@{Site=$syncSite;Query=$syncQuery;Error=$_.Exception.Message})
            break # Do not amplify an unavailable site's failure into more requests.
        }
        $syncResults | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    }
    $syncResults | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $OutputPath -Encoding utf8
}
$syncResults | ForEach-Object { [PSCustomObject]@{Site=$_.Site;Query=$_.Query;Status=$_.Status;Error=$_.Error} } | Format-Table -AutoSize
