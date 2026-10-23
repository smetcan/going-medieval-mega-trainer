# ⚔ Going Medieval - Mega Trainer (TR) v1.4.0

**Going Medieval** oyunu için **BepInEx 5 + Harmony** altyapısıyla geliştirilmiş, tamamı Türkçe, oyun içi grafik arayüzlü (IMGUI) ve **Canlı Yenileme (Hot-Reload)** destekli kapsamlı Trainer / Mod paketi.

---

## ✨ Özellikler

### 1. 👥 Köylüler (Settlers)
- **Ölümsüzlük & Otomatik Tedavi (`[F3]`):** Köylülerinizin canı ve kanı asla tükenmez, bayılmazlar (`Faint` engeli), tüm yaraları ve kırıkları otomatik iyileştirilir.
- **Sonsuz İhtiyaçlar & %100 Moral (`[F4]`):** Açlık, uyku, eğlence ve moral sürekli %100 tutulur; sarhoşluk (`Inebriated`), ağrı (`Pain`), yetersiz beslenme (`Malnutrition`), hipotermi ve sıcak çarpması gibi negatif etkiler sıfırlanır.
- **Süper Hareket Hızı:** Köylülerin yürüme ve koşma hızını `1x` – `20x` arasında çarpanla artırma.
- **Tek Tıkla Toplu & Bireysel İşlemler:**
  - Tüm yetenekleri (Skills) anında **50 (Maksimum)** yapma.
  - Tüm yeteneklere **Çift Yıldız (Tutku / Passion)** verme.
  - Tek tıkla köye **+1 Yeni Köylü** ekleme.
  - Köylü listesinden bireysel iyileştirme ve yetenek fulleme.

### 2. 🏗 İnşaat & Üretim (Construction & Production)
- **Anında Otomatik İnşaat (`AutoConstruct` - `[F5]`):** Yerleştirilen yapı planları köylü beklemeden anında tamamlanır.
- **Kaynaksız İnşaat (`ConstructWithoutResource`):** Hiçbir hammadde harcamadan inşaat yapma.
- **Anında Kazı (`InstantDig`) & Anında Ağaç Kesimi (`InstantCut`):** Madencilik ve odunculuk işlemleri tek vuruşta biter.
- **Anında Hasat (`ForceHarvestPhase`):** Tarladaki ekinleri anında olgunlaştırma.
- **Global Tezgah Üretim Hızı:** Slider (`1x`–`100x`) ve hızlı seçim butonları (**`1x`**, **`2x`**, **`5x`**, **`10x`**, **`15x`**, **`20x`**, **`30x`**, **`50x`**, **`100x`**) ile tüm üretim tezgahlarını hızlandırma.
- **Mavi Planları Bitir (`[F6]`):** Haritadaki tüm bekleyen inşaat planlarını tek tıkla tamamlama.
- **Tüm Araştırmaları Aç:** Teknoloji ağacındaki tüm araştırmaları tek tıkla açma.

### 3. 📦 Kaynak & Depo (Resource Spawner & Stockpiles)
- **Depodaki Tüm Yığınları Doldur (`[F7]`):** Haritadaki mevcut tüm kaynak yığınlarını maksimum kapasiteye tamamlama ve tazelik/dayanıklılıklarını %100 yapma.
- **Çürüme Yok / Sürekli %100 Tazelik:** Yiyeceklerin ve malzemelerin bozulmasını engelleme.
- **Kaynak & Eşya Oluşturucu (Spawner):** Arama destekli katalog ve hızlı butonlarla kamera merkezine istediğiniz miktarda (`10`, `50`, `100`, `250`, `500`) odun, taş, kireçtaşı bloğu, demir/çelik/altın külçe, yemek, şarap, silah ve zırh indirme.

### 4. ⚔ Savaş & Canlılar (Combat & Animal Spawner)
- **Haritadaki Tüm Düşmanları Öldür (`[F8]`):** Saldıran tüm haydutları ve düşman birimleri tek tıkla yok etme.
- **Hayvan Oluşturucu (Animal Spawner):**
  - **Mod Seçimi:** **🐾 Evcil (`Pet` - Eğitimli)**, **🏠 Ehli (`Domestic` - Çiftlik Hayvanı)** veya **🐺 Yabani (`Wild`)**.
  - **Özelleştirme:** Cinsiyet (*Rastgele / Erkek / Dişi*), Yaş (*Yetişkin / Yavru*) ve Adet (*1 / 2 / 5 / 10*).
  - **18 Hayvan Türü:** Kedi, Köpek, İnek, Koyun, Keçi, Eşek, Domuz, Tavuk, Sülün, Tavşan, Geyik, Yaban Domuzu, Kurt, Ayı, Tilki, Porsuk, Kutup Tilkisi, Sırtlan.

### 5. 🌤 Zaman & Hava Durumu (Time & Weather)
- **Oyun Hızı:** `1x`, `2x`, `3x`, `5x`, `10x`, `20x` zaman hızlandırma.
- **Tarih / Saat Atlama:** `+1 Saat`, `+6 Saat`, `+1 Gün`, `+1 Mevsim` ileri sarma.
- **Anında Hava Durumu Değiştirme:**
  - **☀ Açık (Clear):** Aktif yağmur, kar, sis ve fırtına olaylarını anında sonlandırıp güneşli havaya geçer.
  - **🌧 Yağmur (Rain)**, **🌫 Sis (Fog)**, **❄ Kar (Snow)**, **⛈ Gök Gürültülü Fırtına (Thunderstorm)**.

### 6. 🔄 Canlı Yenileme (Hot-Reload - `[F9]`)
- Oyunu kapatmadan arka planda derlenen yeni `TrainerHotReload.dll` paketini **1.5 saniye içinde otomatik** (veya `[F9]` tuşuyla) belleğe yükler; açık menü sekmesini ve aktif hile durumlarını korur.

### 7. 📋 Durum Logları & Geliştirici Konsolu
- Tıklanan her butonun ve kısayolun sonucunu saat damgasıyla oyun içi **`📋 Loglar`** sekmesinde ve oyun klasöründeki `Trainer_Durum_Loglari.txt` dosyasında tutar.
- Oyunun gizli **Geliştirici Panelini (`[F2]`)** ve konsol komutlarını doğrudan çalıştırma imkanı sunar.

---

## ⌨ Kısayol Tuşları

| Tuş | İşlev |
| :--- | :--- |
| **`F1`** veya **`Insert`** | Trainer Menüsünü Aç / Kapat |
| **`F2`** | Oyunun Orijinal Geliştirici Panelini Aç / Kapat |
| **`F3`** | Köylü Ölümsüzlük & Otomatik Tedavi Aç / Kapat |
| **`F4`** | Sonsuz İhtiyaçlar & %100 Moral Aç / Kapat |
| **`F5`** | Anında İnşaat + Kaynaksız İnşa + Anında Kazı/Kesim Aç / Kapat |
| **`F6`** | Haritadaki Tüm İnşaat Planlarını (Blueprints) Bitir |
| **`F7`** | Depodaki Tüm Yığınları Maksimuma Doldur & Tazeliği %100 Yap |
| **`F8`** | Haritadaki Tüm Düşmanları Anında Öldür |
| **`F9`** | Canlı Yenile (Hot-Reload) |

---

## 🚀 Kurulum (Tek Adım - Hazır Paket)

1. Bu depodaki **`GoingMedieval_MegaTrainer_TR_v1.4.0.zip`** dosyasını (veya **Releases** bölümündeki en güncel ZIP paketini) indirin.
2. ZIP içindeki tüm dosya ve klasörleri (`BepInEx`, `winhttp.dll`, `doorstop_config.ini`) doğrudan **`Going Medieval.exe`** dosyasının bulunduğu oyun ana dizinine çıkartın:
   - **Epic Games:** `C:\Program Files\Epic Games\GoingMedieval\`
   - **Steam:** `C:\Program Files (x86)\Steam\steamapps\common\Going Medieval\`
3. Oyunu başlatın ve **`F1`** tuşuna basın.

---

## 🛠 Kaynak Koddan Derleme (Geliştiriciler İçin)

Oyun dizininiz `Build-And-Install.ps1` içinde tanımlıdır. Değişiklik yaptıktan sonra PowerShell üzerinden:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-And-Install.ps1
```

komutunu çalıştırmanız yeterlidir. Betik `GoingMedievalTrainer.cs` dosyasını derler, `BepInEx\TrainerHotReload.dll` ve `BepInEx\plugins\GoingMedievalTrainer.dll` konumlarına kopyalar ve paylaşılabilir ZIP paketini günceller.
