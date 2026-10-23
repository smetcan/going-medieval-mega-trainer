# ⚔ Going Medieval - Mega Trainer (EN / TR) v1.5.0

A full-featured in-game **Mega Trainer** for **Going Medieval**, built with **BepInEx 5 + Harmony**, featuring an interactive IMGUI interface, **Bilingual Support (English & Turkish — defaults to English, switchable in-game with 1 click)**, and **Live Hot-Reload (`[F9]`)**.

**Going Medieval** oyunu için **BepInEx 5 + Harmony** altyapısıyla geliştirilmiş, **Çift Dilli (İngilizce & Türkçe — varsayılan İngilizce, oyun içinden tek tıkla değiştirilebilir)** ve **Canlı Yenileme (Hot-Reload)** destekli kapsamlı Trainer / Mod paketi.

---

## 🌐 Language Support / Dil Desteği

- **Default Language:** English (`EN`)
- **In-Game Toggle:** Click the **`🌐 Lang: EN ➔ TR`** / **`🌐 Dil: TR ➔ EN`** button in the top-right bar of the Trainer window to switch languages instantly. Your selection is saved automatically via `PlayerPrefs`.

---

## ✨ Features / Özellikler

### 1. 👥 Settlers / Köylüler
- **God Mode & Auto-Heal (`[F3]`):** Settlers never lose health or blood, never faint (`Faint` prevention), and all wounds/fractures are healed automatically.
- **Infinite Needs & 100% Mood (`[F4]`):** Hunger, sleep, entertainment, and mood stay at 100%; negative effectors like drunkenness (`Inebriated`), pain, malnutrition, hypothermia, and heatstroke are cleared.
- **Super Movement & Work Speed:** Multiply settler movement and work speed (`1x` – `20x`).
- **One-Click Bulk & Individual Actions:**
  - Max all settler skills to **Level 50**.
  - Grant **Double Passion (2 Stars)** to all skills.
  - Spawn **+1 New Settler** directly into your settlement.
  - Heal/fill or max skills per individual settler.

### 2. 🏗 Build & Craft / İnşaat & Üretim
- **Instant Auto-Construct (`AutoConstruct` - `[F5]`):** Placed blueprints finish instantaneously.
- **Build Without Resources (`ConstructWithoutResource`):** Free building without consuming materials.
- **Instant Dig (`InstantDig`) & Instant Cut (`InstantCut`):** One-hit mining and tree cutting.
- **Force Harvest Phase (`ForceHarvestPhase`):** Instantly mature crops.
- **Global Workbench Speed:** Slider (`1x`–`100x`) + quick preset buttons (**`1x`**, **`2x`**, **`5x`**, **`10x`**, **`15x`**, **`20x`**, **`30x`**, **`50x`**, **`100x`**).
- **Finish All Blueprints (`[F6]`):** Complete all pending blueprints on the map.
- **Unlock All Research:** Unlock the entire technology tree in one click.

### 3. 📦 Items & Storage / Kaynak & Depo
- **Refill All Stockpiles (`[F7]`):** Fill all existing stockpiles to their maximum stack limit and restore 100% freshness & HP.
- **No Rot / Infinite Freshness:** Prevent food and items from rotting or decaying.
- **Resource & Item Spawner:** Searchable catalog + quick spawn buttons (`25`, `50`, `100`, `250`, `500`) for wood, limestone, clay, iron/steel/gold ingots, coal, blocks, bricks, meals, weapons, and armor.

### 4. ⚔ Combat & Animals / Savaş & Canlılar
- **Kill All Enemies (`[F8]`):** Instantly eliminate all hostile enemies on the map.
- **Recruit All Prisoners (100%) & Tame/Train All Animals.**
- **Animal Spawner (18 Species):**
  - **Mode Selection:** **🐕 Trained Pet (Haul/Follow)**, **🐄 Domestic (Livestock)**, or **🐺 Wild**.
  - **Customization:** Sex (*Random / Male / Female*), Age (*Adult / Young*), and Quantity (*1 / 2 / 5 / 10*).

### 5. 🌤 Time & Weather / Zaman & Hava Durumu
- **Game Speed (`TimeScale`):** `0x`, `1x`, `3x`, `5x`, `10x`, `20x`.
- **Time of Day & Season Control:** Morning, Noon, Evening, Midnight; Spring, Summer, Autumn, Winter.
- **Instant Weather Control:**
  - **☀ Clear / Sunny:** Immediately terminates all active rain, snow, fog, and storm events.
  - **🌧 Rain**, **🌫 Fog**, **❄ Snow**, **⛈ Thunderstorm** (starts immediately at 100% intensity).

### 6. 🔄 Live Hot-Reload (`[F9]`)
- Reloads newly compiled `TrainerHotReload.dll` builds into the running game **automatically within 1.5 seconds** (or manually via **`[F9]`**) while preserving your open tab, window position, and active cheat toggles.

---

## ⌨ Hotkeys / Kısayol Tuşları

| Key / Tuş | Function (EN) | İşlev (TR) |
| :--- | :--- | :--- |
| **`F1`** / **`Insert`** | Open / Close Trainer Menu | Trainer Menüsünü Aç / Kapat |
| **`F2`** | Toggle Native Hidden Developer Panel | Orijinal Geliştirici Panelini Aç / Kapat |
| **`F3`** | Toggle God Mode & Auto-Heal | Köylü Ölümsüzlük & Otomatik Tedavi Aç / Kapat |
| **`F4`** | Toggle Infinite Needs & 100% Mood | Sonsuz İhtiyaçlar & %100 Moral Aç / Kapat |
| **`F5`** | Toggle All Fast Build/Dig Cheats | Anında İnşaat + Kaynaksız İnşa + Anında Kazı Aç / Kapat |
| **`F6`** | Finish All Blueprints on Map | Haritadaki Tüm İnşaat Planlarını Bitir |
| **`F7`** | Refill All Stockpiles & 100% Freshness | Depodaki Tüm Yığınları Doldur & %100 Tazelik |
| **`F8`** | Kill All Enemies on Map | Haritadaki Tüm Düşmanları Öldür |
| **`F9`** | Live Hot-Reload | Canlı Yenile (Hot-Reload) |

---

## 🚀 Installation / Kurulum

1. Download **`GoingMedieval_MegaTrainer_v1.5.0_EN_TR.zip`** from the repository or the [**Releases**](https://github.com/smetcan/going-medieval-mega-trainer/releases) page.
2. Extract all files (`BepInEx`, `winhttp.dll`, `doorstop_config.ini`) directly into your **`Going Medieval.exe`** game folder:
   - **Epic Games:** `C:\Program Files\Epic Games\GoingMedieval\`
   - **Steam:** `C:\Program Files (x86)\Steam\steamapps\common\Going Medieval\`
3. Launch the game and press **`F1`**.
