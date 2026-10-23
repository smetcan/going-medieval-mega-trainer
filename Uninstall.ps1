# Going Medieval Mega Trainer - Kaldırma (Uninstall) Betiği
$gameDir = "C:\Program Files\Epic Games\GoingMedieval"

foreach ($item in @("winhttp.dll", "doorstop_config.ini", "changelog.txt", "BepInEx")) {
    $target = Join-Path $gameDir $item
    if (Test-Path $target) {
        Remove-Item -Path $target -Recurse -Force
        Write-Host "Silindi: $target" -ForegroundColor Yellow
    }
}

Write-Host "Mega Trainer ve BepInEx oyundan tamamen kaldırıldı." -ForegroundColor Green
