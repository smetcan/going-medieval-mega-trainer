# Going Medieval Mega Trainer - Derleme, Canlı Yenileme (Hot-Reload) ve Paylaşım Paketi Betiği
$ErrorActionPreference = "Stop"

$csc = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
$gameDir = "C:\Program Files\Epic Games\GoingMedieval"
$managed = "$gameDir\Going Medieval_Data\Managed"
$bepCore = "$gameDir\BepInEx\core"
$projDir = $PSScriptRoot

$stamp = Get-Date -Format "yyyyMMdd_HHmmss_fff"
$buildTemp = Join-Path $projDir "BuildTemp"
New-Item -ItemType Directory -Path $buildTemp -Force | Out-Null
$uniqueOutDll = Join-Path $buildTemp "GoingMedievalTrainer_Hot_$stamp.dll"

Write-Host "GoingMedievalTrainer.dll (Hot-Reload Kimliği: GoingMedievalTrainer_Hot_$stamp) derleniyor..." -ForegroundColor Cyan
& $csc /nologo /target:library /nostdlib /noconfig `
    /r:"$managed\netstandard.dll" `
    /r:"$managed\mscorlib.dll" `
    /r:"$managed\System.dll" `
    /r:"$managed\System.Core.dll" `
    /r:"$managed\UnityEngine.dll" `
    /r:"$managed\UnityEngine.CoreModule.dll" `
    /r:"$managed\UnityEngine.IMGUIModule.dll" `
    /r:"$managed\UnityEngine.InputLegacyModule.dll" `
    /r:"$managed\UnityEngine.TextRenderingModule.dll" `
    /r:"$managed\UnityEngine.UI.dll" `
    /r:"$managed\Assembly-CSharp.dll" `
    /r:"$bepCore\BepInEx.dll" `
    /r:"$bepCore\0Harmony.dll" `
    /out:"$uniqueOutDll" `
    "$projDir\GoingMedievalTrainer.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Derleme başarısız oldu!"
}

Copy-Item -Path $uniqueOutDll -Destination "$projDir\GoingMedievalTrainer.dll" -Force
Remove-Item -Path $buildTemp -Recurse -Force

New-Item -ItemType Directory -Path "$gameDir\BepInEx\plugins" -Force | Out-Null

# 1) Canlı Yenileme (Hot-Reload) dosyasını her zaman güncelle (Oyun açıkken bile kilitlenmez)
$hotReloadTarget = "$gameDir\BepInEx\TrainerHotReload.dll"
Copy-Item -Path "$projDir\GoingMedievalTrainer.dll" -Destination $hotReloadTarget -Force
Write-Host "Hot-Reload paketi güncellendi: $hotReloadTarget" -ForegroundColor Green

# 2) Ana eklenti DLL'ini güncelle (Oyun kapalıysa doğrudan yazar, açıksa Hot-Reload devreye girer)
try {
    Copy-Item -Path "$projDir\GoingMedievalTrainer.dll" -Destination "$gameDir\BepInEx\plugins\GoingMedievalTrainer.dll" -Force
    Write-Host "Ana eklenti güncellendi: $gameDir\BepInEx\plugins\GoingMedievalTrainer.dll" -ForegroundColor Green
} catch {
    Write-Host "Oyun şu an açık: Ana DLL kilitli olduğu için güncelleme TrainerHotReload.dll üzerinden canlı yüklenecek (1.5 sn içinde otomatik veya [F9] ile)." -ForegroundColor Yellow
}

# 3) Başkalarıyla paylaşmak için taşınabilir ZIP paketi oluştur
$stageDir = Join-Path $projDir "ReleasePackage"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path "$stageDir\BepInEx\core" -Force | Out-Null
New-Item -ItemType Directory -Path "$stageDir\BepInEx\plugins" -Force | Out-Null

Copy-Item "$gameDir\winhttp.dll" "$stageDir\" -Force
Copy-Item "$gameDir\doorstop_config.ini" "$stageDir\" -Force
Copy-Item "$gameDir\BepInEx\core\*" "$stageDir\BepInEx\core\" -Force
Copy-Item "$projDir\GoingMedievalTrainer.dll" "$stageDir\BepInEx\plugins\GoingMedievalTrainer.dll" -Force
Copy-Item "$projDir\GoingMedievalTrainer.dll" "$stageDir\BepInEx\TrainerHotReload.dll" -Force

$readme = @"
========================================================================
   GOING MEDIEVAL - MEGA TRAINER (EN / TR) v1.5.0 - INSTALLATION GUIDE
========================================================================

1. INSTALLATION (ONE STEP - NO EXTRA TOOLS NEEDED)
------------------------------------------------------------------------
Extract ALL files and folders inside this ZIP archive:
  - BepInEx (folder)
  - winhttp.dll
  - doorstop_config.ini

directly into your "Going Medieval.exe" root game folder:

  * Epic Games default path:
    C:\Program Files\Epic Games\GoingMedieval\

  * Steam default path:
    C:\Program Files (x86)\Steam\steamapps\common\Going Medieval\

2. USAGE, LANGUAGE SWITCH & HOTKEYS
------------------------------------------------------------------------
Launch the game normally. You will see the "Mega Trainer v1.5.0 [F1]" badge
in the top-left corner.
* Default language is English (EN). Click "🌐 Lang: EN ➔ TR" in the top bar
  to switch between English and Turkish at any time!

  [F1] or [Insert] : Open / Close Trainer Menu
  [F2]             : Open / Close Native Hidden Developer Panel
  [F3]             : Settler God Mode & Auto-Heal ON / OFF
  [F4]             : Infinite Needs & 100% Mood ON / OFF
  [F5]             : Instant Build + Free Build + Instant Dig/Cut ON / OFF
  [F6]             : Finish All Construction Blueprints on Map
  [F7]             : Refill All Stockpiles to Max & 100% Freshness
  [F8]             : Kill All Enemies on Map
  [F9]             : Live Hot-Reload (loads new updates without restarting)

========================================================================
   TÜRKÇE KURULUM VE KULLANIM KILAVUZU
========================================================================
1. Bu ZIP içindeki tüm dosyaları (BepInEx, winhttp.dll, doorstop_config.ini)
   "Going Medieval.exe" dosyasının bulunduğu oyun ana klasörüne kopyalayın.
2. Oyunu başlatıp [F1] tuşuna basın. Menü varsayılan olarak İngilizce açılır;
   sağ üstteki "🌐 Lang: EN ➔ TR" butonuna tıklayarak Türkçe'ye geçebilirsiniz.
========================================================================
"@
Set-Content -Path "$stageDir\README_INSTALL.txt" -Value $readme -Encoding UTF8

$oldZip = Join-Path $projDir "GoingMedieval_MegaTrainer_TR_v1.4.0.zip"
if (Test-Path $oldZip) { Remove-Item $oldZip -Force }

$zipPath = Join-Path $projDir "GoingMedieval_MegaTrainer_v1.5.0_EN_TR.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force
Remove-Item $stageDir -Recurse -Force

Write-Host "Paylaşıma hazır ZIP paketi oluşturuldu: $zipPath" -ForegroundColor Green
