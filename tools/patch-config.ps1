param(
    [Parameter(Mandatory=$true)]
    [string]$ConfigPath
)

$raw = [System.IO.File]::ReadAllText($ConfigPath, [System.Text.Encoding]::UTF8)
$lines = $raw -split "`r?`n"

$needles = @(
    @{ Name = "RemoteProjectPath"; Replacement = '"RemoteProjectPath":  "/var/www/with-docker"' },
    @{ Name = "ComposeFileName";  Replacement = '"ComposeFileName":  "docker-compose.yml"' },
    @{ Name = "HealthCheckUrl";   Replacement = '"HealthCheckUrl":  "http://localhost:8081/"' },
    @{ Name = "HealthCheckPort";  Replacement = '"HealthCheckPort":  8081' }
)

$set = @{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    $ln = $lines[$i]
    foreach ($n in $needles) {
        if (-not $set.ContainsKey($n.Name) -and $ln -match '^\s*"' + [regex]::Escape($n.Name) + '"\s*=') {
            $lines[$i] = $n.Replacement
            $set[$n.Name] = $true
        }
    }
}

foreach ($n in $needles) {
    if (-not $set.ContainsKey($n.Name)) {
        $idx = $lines.Count - 1
        if ($lines[$idx] -match '^\s*\}') {
            $lines = $lines[0..($idx - 1)] + @($n.Replacement) + $lines[$idx..($lines.Count - 1)]
        } else {
            $lines += $n.Replacement
        }
    }
}

$out = $lines -join "`r`n"
[System.IO.File]::WriteAllText($ConfigPath, $out, [System.Text.Encoding]::UTF8)

Write-Host "patched config path: $ConfigPath"
Get-Content $ConfigPath -Raw | Select-Object -First 240
