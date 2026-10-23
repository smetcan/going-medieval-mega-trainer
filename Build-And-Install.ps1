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
   GOING MEDIEVAL - MEGA TRAINER (TR) v1.4.0 - KURULUM KILAVUZU
========================================================================

1. KURULUM (TEK ADIM - DERLEME VEYA EK PROGRAM GEREKTİRMEZ)
------------------------------------------------------------------------
Bu ZIP dosyasının içindeki TÜM dosya ve klasörleri:
  - BepInEx (klasör)
  - winhttp.dll
  - doorstop_config.ini

doğrudan bilgisayarınızdaki "Going Medieval.exe" dosyasının bulunduğu
oyun ana klasörünün içine çıkartın (kopyalayın):

  * Epic Games varsayılan yolu:
    C:\Program Files\Epic Games\GoingMedieval\

  * Steam varsayılan yolu:
    C:\Program Files (x86)\Steam\steamapps\common\Going Medieval\

2. KULLANIM VE KISAYOL TUŞLARI
------------------------------------------------------------------------
Oyunu normal şekilde başlatın. Oyun açıldığında sol üstte "Mega Trainer [F1]"
rozetini göreceksiniz.

  [F1] veya [Insert] : Trainer Menüsünü Aç / Kapat
  [F2]               : Oyunun Gizli Geliştirici Panelini Aç / Kapat
  [F3]               : Köylü Ölümsüzlük & Otomatik Tedavi Aç / Kapat
  [F4]               : Sonsuz İhtiyaçlar & %100 Moral Aç / Kapat
  [F5]               : Anında İnşaat + Kaynaksız İnşa + Anında Kazı Aç / Kapat
  [F6]               : Haritadaki Tüm Mavi İnşaat Planlarını Bitir
  [F7]               : Depodaki Tüm Yığınları Maksimuma Doldur & Tazeliği %100 Yap
  [F8]               : Haritadaki Tüm Düşmanları Anında Öldür
  [F9]               : Canlı Yenile (Hot-Reload - Oyunu kapatmadan yeni sürümü yükler)

3. KALDIRMA (UNINSTALL)
------------------------------------------------------------------------
Trainer'ı tamamen kaldırmak isterseniz oyun klasöründeki:
  - winhttp.dll
  - doorstop_config.ini
  - BepInEx klasörü
dosyalarını silmeniz yeterlidir. Oyun orijinal haline döner.
========================================================================
"@
Set-Content -Path "$stageDir\KURULUM_OKU_BENI.txt" -Value $readme -Encoding UTF8

$zipPath = Join-Path $projDir "GoingMedieval_MegaTrainer_TR_v1.4.0.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force
Remove-Item $stageDir -Recurse -Force

Write-Host "Paylaşıma hazır ZIP paketi oluşturuldu: $zipPath" -ForegroundColor Green
