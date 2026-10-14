# Refresh Windows Explorer Icon Cache
# Clears stale icon database and restarts Windows Explorer to force immediate re-reading of embedded icons.

Write-Host "Stopping Windows Explorer..." -ForegroundColor Yellow
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

$IconCache1 = "$env:LOCALAPPDATA\IconCache.db"
$IconCacheFolder = "$env:LOCALAPPDATA\Microsoft\Windows\Explorer"

if (Test-Path $IconCache1) {
    Remove-Item $IconCache1 -Force -ErrorAction SilentlyContinue
    Write-Host "Removed legacy IconCache.db" -ForegroundColor Green
}

if (Test-Path $IconCacheFolder) {
    Get-ChildItem -Path $IconCacheFolder -Filter "iconcache_*.db" -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Remove-Item $_.FullName -Force -ErrorAction Stop
            Write-Host "Removed $($_.Name)" -ForegroundColor Green
        } catch {
            Write-Host "Skipped $($_.Name) (locked)" -ForegroundColor DarkGray
        }
    }
}

Write-Host "Restarting Windows Explorer..." -ForegroundColor Yellow
Start-Process explorer.exe
Start-Sleep -Seconds 1

Write-Host "`nWindows Explorer icon cache refreshed successfully." -ForegroundColor Green
