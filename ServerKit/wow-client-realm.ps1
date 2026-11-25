# Dreamport ServerKit - point the normal WoW 3.3.5a client at your own realm, or back at Warmane.
# Used by "WoW client - my server.cmd" and "WoW client - Warmane.cmd" (double-click those; close WoW first).
# It changes two lines the client reads at start: Data\<locale>\realmlist.wtf ("set realmlist ...") and, in WTF\Config.wtf,
# SET realmList / SET realmName. The first run keeps copies of both files as *.before-dreamport.
param(
    [ValidateSet('Server', 'Warmane')][string]$Target = 'Server',
    [string]$WowFolder = 'D:\WOTLK\World of Warcraft 3.3.5a'
)

if ($Target -eq 'Server') { $addr = '145.241.232.229'; $realm = 'AzerothCore' }
else { $addr = 'logon.warmane.com'; $realm = 'Lordaeron' }

if (Get-Process -Name 'Wow' -ErrorAction SilentlyContinue) {
    Write-Host 'WoW is running - close it first (it rewrites WTF\Config.wtf when it exits), then try again.'
    exit 1
}

$realmlists = @(Get-ChildItem -Path (Join-Path $WowFolder 'Data') -Filter 'realmlist.wtf' -Recurse -Depth 1 -ErrorAction SilentlyContinue)
if ($realmlists.Count -eq 0) {
    Write-Host "No Data\<locale>\realmlist.wtf found in $WowFolder - is that your WoW 3.3.5a folder?"
    exit 1
}
$config = Join-Path $WowFolder 'WTF\Config.wtf'

foreach ($f in @($realmlists | ForEach-Object { $_.FullName }) + @($config)) {
    if ((Test-Path $f) -and -not (Test-Path "$f.before-dreamport")) { Copy-Item $f "$f.before-dreamport" }
}

foreach ($r in $realmlists) {
    Set-Content -Path $r.FullName -Value "set realmlist $addr" -Encoding ASCII
    Write-Host ("  " + $r.FullName + ": set realmlist " + $addr)
}

if (Test-Path $config) {
    $lines = @(Get-Content $config)
    $out = New-Object System.Collections.Generic.List[string]
    $haveList = $false; $haveName = $false
    foreach ($l in $lines) {
        if ($l -match '^SET realmList ') { $out.Add("SET realmList `"$addr`""); $haveList = $true }
        elseif ($l -match '^SET realmName ') { $out.Add("SET realmName `"$realm`""); $haveName = $true }
        else { $out.Add($l) }
    }
    if (-not $haveList) { $out.Add("SET realmList `"$addr`"") }
    if (-not $haveName) { $out.Add("SET realmName `"$realm`"") }
    Set-Content -Path $config -Value $out -Encoding ASCII
    Write-Host ("  " + $config + ": SET realmList `"" + $addr + "`", SET realmName `"" + $realm + "`"")
}

Write-Host ''
if ($Target -eq 'Server') {
    Write-Host 'Done: WoW now logs in to YOUR realm (145.241.232.229). Start Wow.exe and log in with a normal account'
    Write-Host '(e.g. TEST02 - not the one you use in the Dreamport at the same time).'
} else {
    Write-Host 'Done: WoW logs in to Warmane again (logon.warmane.com, realm Lordaeron).'
}
exit 0
