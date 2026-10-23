using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using NSEipix.Base;
using NSEipix.Repository;
using NSMedieval;
using NSMedieval.BuildingComponents;
using NSMedieval.Controllers;
using NSMedieval.DevConsole;
using NSMedieval.Manager;
using NSMedieval.Model;
using NSMedieval.Repository;
using NSMedieval.Research;
using NSMedieval.State;
using NSMedieval.StatsSystem;
using NSMedieval.StorageUniversal;
using NSMedieval.Terrain;
using NSMedieval.Types;
using NSMedieval.Village;

namespace GoingMedievalTrainer
{
    [BepInPlugin("com.selcuk.goingmedieval.megatrainer", "Going Medieval Mega Trainer (EN/TR)", "1.5.0")]
    public class MegaTrainerPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            try
            {
                var harmony = new Harmony("com.selcuk.goingmedieval.megatrainer.harmony");
                harmony.PatchAll(typeof(TrainerHarmonyPatches));
                Log.LogInfo("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.LogError("Harmony patch error: " + ex);
            }

            TrainerHarmonyPatches.EnsureTrainerHost();
            Log.LogInfo("Going Medieval Mega Trainer (EN/TR) v1.5.0 loaded! [F1] Menu | [F9] Hot-Reload");
        }
    }

    internal static class TrainerHarmonyPatches
    {
        public static void EnsureTrainerHost()
        {
            if (MegaTrainerBehaviour.Instance != null) return;

            var existingGo = GameObject.Find("GM_MegaTrainer_Host");
            if (existingGo != null)
            {
                var comps = existingGo.GetComponents<MonoBehaviour>();
                if (comps != null)
                {
                    for (int i = 0; i < comps.Length; i++)
                    {
                        if (comps[i] != null && comps[i].GetType().Name == "MegaTrainerBehaviour")
                        {
                            return;
                        }
                    }
                }
                existingGo.AddComponent<MegaTrainerBehaviour>();
                return;
            }

            var go = new GameObject("GM_MegaTrainer_Host");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MegaTrainerBehaviour>();
        }

        [HarmonyPatch(typeof(SceneController), "Update")]
        [HarmonyPostfix]
        public static void SceneController_Update_Postfix()
        {
            EnsureTrainerHost();
        }

        public static bool IsPlayerSettler(CreatureBase creature)
        {
            try
            {
                if (creature == null || creature.HasDisposed) return false;
                var hum = creature as HumanoidInstance;
                if (hum == null) return false;
                if (!MonoSingleton<WorkerManager>.IsInstantiated() || WorkerManager.LabourersHere == null) return false;
                return WorkerManager.LabourersHere.Contains(hum);
            }
            catch
            {
                return false;
            }
        }

        [HarmonyPatch(typeof(CreatureBase), "OnHealthDepleted", new Type[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool Prefix_OnHealthDepleted(CreatureBase __instance)
        {
            if (MegaTrainerBehaviour.GodModeAllSettlers && IsPlayerSettler(__instance))
            {
                MegaTrainerBehaviour.HealSingleCreatureStatic(__instance);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CreatureBase), "OnBloodDepleted", new Type[0])]
        [HarmonyPrefix]
        public static bool Prefix_OnBloodDepleted(CreatureBase __instance)
        {
            if (MegaTrainerBehaviour.GodModeAllSettlers && IsPlayerSettler(__instance))
            {
                MegaTrainerBehaviour.HealSingleCreatureStatic(__instance);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CreatureBase), "Faint", new Type[0])]
        [HarmonyPrefix]
        public static bool Prefix_Faint(CreatureBase __instance)
        {
            if ((MegaTrainerBehaviour.GodModeAllSettlers || MegaTrainerBehaviour.InfiniteNeedsAllSettlers) && IsPlayerSettler(__instance))
            {
                MegaTrainerBehaviour.HealSingleCreatureStatic(__instance);
                return false;
            }
            return true;
        }
    }

    public class MegaTrainerBehaviour : MonoBehaviour
    {
        public static MegaTrainerBehaviour Instance { get; private set; }

        // Dil Durumu: Varsayılan İngilizce (_isTurkish = false)
        private bool _isTurkish = false;
        private string L(string en, string tr) => _isTurkish ? tr : en;
        private string OnOff(bool state) => state ? L("ON", "AÇIK") : L("OFF", "KAPALI");

        // Pencere ve UI Durumu
        private bool _showMenu = false;
        private bool _showMiniBadge = true;
        private Rect _windowRect = new Rect(40f, 60f, 750f, 665f);
        private int _selectedTab = 0;

        private static readonly string[] TabsEN = new string[]
        {
            "Settlers",
            "Build & Craft",
            "Items & Storage",
            "Combat & Animals",
            "Time & Weather",
            "Dev Console",
            "📋 Logs"
        };

        private static readonly string[] TabsTR = new string[]
        {
            "Köylüler",
            "İnşaat & Üretim",
            "Kaynak & Depo",
            "Savaş & Canlılar",
            "Zaman & Hava",
            "Dev Konsol",
            "📋 Loglar"
        };

        private string[] CurrentTabs => _isTurkish ? TabsTR : TabsEN;

        // Sürekli Aktif (Loop) Hile Bayrakları
        public static bool GodModeAllSettlers = false;
        public static bool InfiniteNeedsAllSettlers = false;
        private bool _superSpeedSettlers = false;
        private float _settlerSpeedMultiplier = 5f;
        private bool _noRotInfiniteFreshness = false;
        private float _productionSpeedMultiplier = 1f;

        // Kaynak Spawner Durumu
        private string _resourceSearchQuery = "";
        private int _spawnAmount = 100;
        private Vector2 _resourceScrollPos = Vector2.zero;
        private Vector2 _settlerScrollPos = Vector2.zero;
        private Vector2 _logScrollPos = Vector2.zero;
        private Vector2 _animalScrollPos = Vector2.zero;
        private List<Resource> _cachedResources = null;

        // Hayvan Spawner Durumu (0 = Yabani/Wild, 1 = Ehli/Domestic, 2 = Evcil/Pet)
        private int _animalSpawnMode = 2; // Varsayılan: Evcil (Pet)
        private int _animalSpawnSex = 0;  // 0 = Rastgele, 1 = Erkek, 2 = Dişi
        private bool _animalSpawnMature = true; // true = Yetişkin, false = Yavru
        private int _animalSpawnCount = 1;

        // Dev Konsol Özel Komut
        private string _customConsoleCommand = "help";
        private string _statusMessage = "Mega Trainer v1.5.0 Ready! [F1] Menu | [F9] Hot-Reload";
        private float _statusTimer = 0f;

        // Canlı Yenileme (Hot-Reload) Durumu
        private bool _isHotReloadedInstance = false;
        private bool _isReplacedByHotReload = false;
        private bool _autoHotReload = true;
        private float _hotReloadCheckTimer = 0f;
        private DateTime _lastHotReloadWriteUtc = DateTime.MinValue;
        private string _hotReloadDllPath;

        // Tıklama -> Durum Loglama Yapısı
        private string _lastClickedAction = "Startup";
        private readonly List<string> _actionStatusLogs = new List<string>();
        private string _logFilePath;

        // Periyodik Güncelleme Zamanlayıcısı
        private float _updateTickTimer = 0f;

        // UI Stilleri
        private bool _stylesInitialized = false;
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _activeButtonStyle;
        private GUIStyle _tabButtonStyle;
        private GUIStyle _activeTabButtonStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _labelStyle;
        private Texture2D _bgTex;
        private Texture2D _sectionTex;
        private Texture2D _btnTex;
        private Texture2D _btnActiveTex;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                // Varsayılan olarak İngilizce (0) açılır; kullanıcı değiştirdiyse PlayerPrefs'ten okur
                _isTurkish = PlayerPrefs.GetInt("GM_MegaTrainer_LangTR", 0) == 1;
                _statusMessage = L(
                    "Mega Trainer v1.5.0 Ready! [F1] Menu | [F9] Hot-Reload",
                    "Mega Trainer v1.5.0 Hazır! [F1] Menü | [F9] Canlı Yenile (Hot-Reload)");

                _hotReloadDllPath = Path.Combine(Paths.BepInExRootPath, "TrainerHotReload.dll");
                if (File.Exists(_hotReloadDllPath))
                {
                    _lastHotReloadWriteUtc = File.GetLastWriteTimeUtc(_hotReloadDllPath);
                }

                _logFilePath = Path.Combine(Paths.GameRootPath, "Trainer_Durum_Loglari.txt");
            }
            catch { }
        }

        private void Start()
        {
            try
            {
                if (!_isHotReloadedInstance)
                {
                    string header = $"================================================================================{Environment.NewLine}" +
                                    $"Going Medieval Mega Trainer v1.5.0 - Session Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                                    $"Format: [Time] | Action: <Button/Hotkey> ==> Status: <Result>{Environment.NewLine}" +
                                    $"================================================================================{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, header, Encoding.UTF8);

                    string pluginDllPath = Path.Combine(Paths.PluginPath, "GoingMedievalTrainer.dll");
                    if (!string.IsNullOrEmpty(_hotReloadDllPath) && File.Exists(_hotReloadDllPath) && File.Exists(pluginDllPath))
                    {
                        DateTime hotTime = File.GetLastWriteTimeUtc(_hotReloadDllPath);
                        DateTime plugTime = File.GetLastWriteTimeUtc(pluginDllPath);
                        if (hotTime > plugTime.AddSeconds(2))
                        {
                            _lastClickedAction = L("[Auto Startup Upgrade]", "[Otomatik Başlangıç Güncellemesi]");
                            HotReloadTrainerNow(true);
                        }
                    }
                }
            }
            catch { }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ToggleLanguage()
        {
            _isTurkish = !_isTurkish;
            try
            {
                PlayerPrefs.SetInt("GM_MegaTrainer_LangTR", _isTurkish ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch { }
            _lastClickedAction = L("[Top Bar] Language Switch (EN)", "[Üst Bar] Dil Değiştir (TR)");
            SetStatus(L("Language switched to English (EN).", "Dil Türkçe (TR) olarak değiştirildi."));
        }

        private void Update()
        {
            if (_isReplacedByHotReload) return;

            try
            {
                // Menü Aç/Kapat Kısayolları
                if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.Insert))
                {
                    _showMenu = !_showMenu;
                }

                // Hızlı Kısayol Tuşları
                if (Input.GetKeyDown(KeyCode.F2))
                {
                    _lastClickedAction = L("[Hotkey F2] Native Developer Panel", "[Kısayol F2] Orijinal Geliştirici Paneli");
                    ToggleNativeDevTools();
                }
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    _lastClickedAction = L("[Hotkey F3] God Mode", "[Kısayol F3] Ölümsüzlük (God Mode)");
                    GodModeAllSettlers = !GodModeAllSettlers;
                    if (GodModeAllSettlers) ApplyGodModeAndHealNow();
                    SetStatus(L("God Mode & Auto-Heal: ", "Ölümsüzlük (God Mode): ") + OnOff(GodModeAllSettlers));
                }
                if (Input.GetKeyDown(KeyCode.F4))
                {
                    _lastClickedAction = L("[Hotkey F4] Infinite Needs & 100% Mood", "[Kısayol F4] Sonsuz İhtiyaçlar & %100 Moral");
                    InfiniteNeedsAllSettlers = !InfiniteNeedsAllSettlers;
                    if (InfiniteNeedsAllSettlers) ApplyInfiniteNeedsNow();
                    SetStatus(L("Infinite Needs & 100% Mood: ", "Sonsuz İhtiyaçlar & %100 Moral: ") + OnOff(InfiniteNeedsAllSettlers));
                }
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    _lastClickedAction = L("[Hotkey F5] All Fast Build/Dig Cheats", "[Kısayol F5] Tüm Hızlı İnşa/Kazı Hileleri");
                    ToggleAllFastBuildCheats();
                }
                if (Input.GetKeyDown(KeyCode.F6))
                {
                    _lastClickedAction = L("[Hotkey F6] Finish All Blueprints", "[Kısayol F6] Haritadaki Tüm İnşaat Planlarını Bitir");
                    FinishAllBlueprintsNow();
                }
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    _lastClickedAction = L("[Hotkey F7] Refill All Stockpiles", "[Kısayol F7] Mevcut Tüm Yığınları Maksimuma Doldur");
                    RefillAllStockpilesAndFreshness();
                }
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    _lastClickedAction = L("[Hotkey F8] Kill All Enemies", "[Kısayol F8] Haritadaki Tüm Düşmanları Öldür");
                    KillAllEnemiesNow();
                }
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    _lastClickedAction = L("[Hotkey F9] Hot-Reload", "[Kısayol F9] Canlı Yenile (Hot-Reload)");
                    HotReloadTrainerNow(false);
                    return;
                }

                if (_statusTimer > 0f)
                {
                    _statusTimer -= Time.unscaledDeltaTime;
                }

                // Otomatik Hot-Reload Dosya İzleyici (Her 1.5 saniyede bir kontrol eder)
                if (_autoHotReload && !string.IsNullOrEmpty(_hotReloadDllPath))
                {
                    _hotReloadCheckTimer += Time.unscaledDeltaTime;
                    if (_hotReloadCheckTimer >= 1.5f)
                    {
                        _hotReloadCheckTimer = 0f;
                        if (File.Exists(_hotReloadDllPath))
                        {
                            DateTime writeUtc = File.GetLastWriteTimeUtc(_hotReloadDllPath);
                            if (writeUtc > _lastHotReloadWriteUtc.AddSeconds(0.5))
                            {
                                _lastClickedAction = L("[Auto Hot-Reload Detected]", "[Otomatik Hot-Reload Algılandı]");
                                HotReloadTrainerNow(true);
                                return;
                            }
                        }
                    }
                }

                // Sürekli aktif olan hileleri her 0.5 saniyede bir uygula
                _updateTickTimer += Time.unscaledDeltaTime;
                if (_updateTickTimer >= 0.5f)
                {
                    _updateTickTimer = 0f;
                    RunContinuousCheats();
                }
            }
            catch (Exception ex)
            {
                MegaTrainerPlugin.Log?.LogWarning("Trainer Update Error: " + ex.Message);
            }
        }

        private void RunContinuousCheats()
        {
            if (!IsInActiveGameplay()) return;

            if (GodModeAllSettlers)
            {
                ApplyGodModeAndHealNow();
            }

            if (InfiniteNeedsAllSettlers)
            {
                ApplyInfiniteNeedsNow();
            }

            if (_superSpeedSettlers)
            {
                ApplySettlerSpeedMultiplier(_settlerSpeedMultiplier);
            }

            if (_noRotInfiniteFreshness)
            {
                KeepAllResourcesFreshAndHealthy();
            }

            if (_productionSpeedMultiplier > 1.01f && MonoSingleton<ProductionManager>.IsInstantiated())
            {
                MonoSingleton<ProductionManager>.Instance.GlobalSpeedMultiplier = _productionSpeedMultiplier;
            }
        }

        private void SetGlobalProductionSpeed(float speed)
        {
            _productionSpeedMultiplier = speed;
            try
            {
                if (MonoSingleton<ProductionManager>.IsInstantiated())
                {
                    MonoSingleton<ProductionManager>.Instance.GlobalSpeedMultiplier = speed;
                    SetStatus(speed <= 1.05f
                        ? L("Global Workbench Production Speed reset to 1x (Normal).", "Global Tezgah Üretim Hızı 1x (Normal) yapıldı.")
                        : L($"Global Workbench Production Speed set to {speed:0}x!", $"Global Tezgah Üretim Hızı {speed:0}x yapıldı!"));
                }
                else
                {
                    SetStatus(L(
                        $"Production Speed set to {speed:0}x (will apply when in a map).",
                        $"Üretim Hızı {speed:0}x olarak ayarlandı (haritaya girince aktif olacak)."));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Production speed error: ", "Üretim hızı hatası: ") + ex.Message, ex);
            }
        }

        private bool IsInActiveGameplay()
        {
            try
            {
                return MonoSingleton<WorkerManager>.IsInstantiated() && WorkerManager.LabourersHere != null;
            }
            catch
            {
                return false;
            }
        }

        private string CurrentTabName()
        {
            var tabs = CurrentTabs;
            if (_selectedTab >= 0 && _selectedTab < tabs.Length)
                return tabs[_selectedTab];
            return L("General", "Genel");
        }

        private bool TrainerButton(string text, params GUILayoutOption[] options)
        {
            if (GUILayout.Button(text, _buttonStyle, options))
            {
                _lastClickedAction = $"[{CurrentTabName()}] {text}";
                return true;
            }
            return false;
        }

        private bool DrawToggleButton(string text, bool active)
        {
            string prefix = active ? L("[✔ ON] ", "[✔ AÇIK] ") : L("[✘ OFF] ", "[✘ KAPALI] ");
            GUIStyle style = active ? _activeButtonStyle : _buttonStyle;
            if (GUILayout.Button(prefix + text, style, GUILayout.Height(32f)))
            {
                _lastClickedAction = $"[{CurrentTabName()}] {text} ({L("Prev", "Önceki")}: {OnOff(active)})";
                return true;
            }
            return false;
        }

        private void SetStatus(string msg, Exception ex = null)
        {
            _statusMessage = msg;
            _statusTimer = 8f;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp}] | {L("Action", "Tıklanan")}: {_lastClickedAction} ==> {L("Status", "Durum")}: {msg}";
            if (ex != null)
            {
                logLine += $" | {L("Error", "Hata Detayı")}: {ex.GetType().Name}: {ex.Message} ({ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()})";
            }

            _actionStatusLogs.Add(logLine);
            if (_actionStatusLogs.Count > 250)
            {
                _actionStatusLogs.RemoveAt(0);
            }
            _logScrollPos = new Vector2(0f, float.MaxValue);

            MegaTrainerPlugin.Log?.LogInfo(logLine);

            try
            {
                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.hideFlags = HideFlags.HideAndDontSave;
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void InitStyles()
        {
            if (_stylesInitialized && _bgTex != null) return;

            _bgTex = MakeTex(2, 2, new Color(0.11f, 0.12f, 0.15f, 0.96f));
            _sectionTex = MakeTex(2, 2, new Color(0.16f, 0.18f, 0.22f, 0.95f));
            _btnTex = MakeTex(2, 2, new Color(0.22f, 0.25f, 0.32f, 1f));
            _btnActiveTex = MakeTex(2, 2, new Color(0.16f, 0.52f, 0.32f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _bgTex;
            _windowStyle.onNormal.background = _bgTex;
            _windowStyle.normal.textColor = new Color(0.95f, 0.85f, 0.55f);
            _windowStyle.fontStyle = FontStyle.Bold;
            _windowStyle.fontSize = 15;

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = 14;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = new Color(0.95f, 0.82f, 0.45f);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

            _sectionStyle = new GUIStyle(GUI.skin.box);
            _sectionStyle.normal.background = _sectionTex;
            _sectionStyle.padding = new RectOffset(10, 10, 8, 8);
            _sectionStyle.margin = new RectOffset(4, 4, 6, 6);

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.normal.background = _btnTex;
            _buttonStyle.hover.background = MakeTex(2, 2, new Color(0.28f, 0.33f, 0.42f, 1f));
            _buttonStyle.active.background = _btnActiveTex;
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.textColor = new Color(1f, 0.92f, 0.6f);
            _buttonStyle.fontSize = 13;
            _buttonStyle.fontStyle = FontStyle.Bold;
            _buttonStyle.padding = new RectOffset(8, 8, 6, 6);

            _activeButtonStyle = new GUIStyle(_buttonStyle);
            _activeButtonStyle.normal.background = _btnActiveTex;
            _activeButtonStyle.hover.background = MakeTex(2, 2, new Color(0.20f, 0.62f, 0.38f, 1f));
            _activeButtonStyle.normal.textColor = new Color(0.9f, 1f, 0.9f);

            _tabButtonStyle = new GUIStyle(_buttonStyle);
            _tabButtonStyle.fontSize = 12;

            _activeTabButtonStyle = new GUIStyle(_activeButtonStyle);
            _activeTabButtonStyle.fontSize = 12;
            _activeTabButtonStyle.normal.background = MakeTex(2, 2, new Color(0.72f, 0.52f, 0.14f, 1f));

            _statusStyle = new GUIStyle(GUI.skin.box);
            _statusStyle.normal.background = MakeTex(2, 2, new Color(0.08f, 0.09f, 0.11f, 0.98f));
            _statusStyle.normal.textColor = new Color(0.55f, 0.95f, 0.65f);
            _statusStyle.fontSize = 12;
            _statusStyle.fontStyle = FontStyle.Bold;
            _statusStyle.alignment = TextAnchor.MiddleLeft;
            _statusStyle.padding = new RectOffset(10, 10, 6, 6);

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (_isReplacedByHotReload) return;

            InitStyles();

            if (_showMiniBadge && !_showMenu)
            {
                if (GUI.Button(new Rect(12f, 12f, 200f, 28f), "⚔ Mega Trainer v1.5.0 [F1]", _buttonStyle))
                {
                    _showMenu = true;
                }
            }

            if (!_showMenu) return;

            string title = L(
                "⚔ GOING MEDIEVAL - MEGA TRAINER v1.5.0 (EN) [Hot-Reload Active] ⚔",
                "⚔ GOING MEDIEVAL - MEGA TRAINER v1.5.0 (TR) [Hot-Reload Aktif] ⚔");
            _windowRect = GUILayout.Window(987654, _windowRect, DrawTrainerWindow, title, _windowStyle);
        }

        private void DrawTrainerWindow(int windowID)
        {
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                L(
                    "Hotkeys: [F1] Menu | [F2] DevPanel | [F3] GodMode | [F4] Needs | [F5] FastBuild | [F6] FinishPlans | [F7] Refill | [F8] Kill | [F9] Reload",
                    "Kısayollar: [F1] Menü | [F2] DevPanel | [F3] Ölümsüzlük | [F4] İhtiyaçlar | [F5] Hızlıİnşa | [F6] PlanBitir | [F7] Depo | [F8] Öldür | [F9] Yenile"),
                _labelStyle);

            if (GUILayout.Button(_isTurkish ? "🌐 Dil: TR ➔ EN" : "🌐 Lang: EN ➔ TR", _activeTabButtonStyle, GUILayout.Width(120f), GUILayout.Height(24f)))
            {
                ToggleLanguage();
            }

            if (GUILayout.Button(L("🔄 Reload [F9]", "🔄 Yenile [F9]"), _activeButtonStyle, GUILayout.Width(105f), GUILayout.Height(24f)))
            {
                _lastClickedAction = L("[Top Bar] 🔄 Reload [F9]", "[Üst Bar] 🔄 Yenile [F9]");
                HotReloadTrainerNow(false);
                GUIUtility.ExitGUI();
                return;
            }
            if (GUILayout.Button("✕", _buttonStyle, GUILayout.Width(30f), GUILayout.Height(24f)))
            {
                _showMenu = false;
            }
            GUILayout.EndHorizontal();

            var tabs = CurrentTabs;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabs.Length; i++)
            {
                GUIStyle st = (_selectedTab == i) ? _activeTabButtonStyle : _tabButtonStyle;
                if (GUILayout.Button(tabs[i], st, GUILayout.Height(32f)))
                {
                    _selectedTab = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            if (!IsInActiveGameplay() && _selectedTab != 5 && _selectedTab != 6)
            {
                GUILayout.BeginVertical(_sectionStyle);
                GUILayout.Label(
                    L(
                        "⚠ No active settlement/map loaded right now. Please load a save or start a new game.",
                        "⚠ Şu anda aktif bir köy/harita yüklü değil. Lütfen bir kayıt (save) yükleyin veya yeni oyun başlatın."),
                    _headerStyle);
                GUILayout.EndVertical();
            }

            switch (_selectedTab)
            {
                case 0: DrawSettlersTab(); break;
                case 1: DrawConstructionTab(); break;
                case 2: DrawResourcesTab(); break;
                case 3: DrawCombatAndCreaturesTab(); break;
                case 4: DrawTimeAndWeatherTab(); break;
                case 5: DrawDevConsoleTab(); break;
                case 6: DrawLogsTab(); break;
            }

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal(_statusStyle);
            GUILayout.Label(L("Status: ", "Durum: ") + _statusMessage, _statusStyle);
            if (GUILayout.Button(
                _autoHotReload ? L("⚡ Auto-Reload: ON", "⚡ Oto-Yenile: AÇIK") : L("⚡ Auto-Reload: OFF", "⚡ Oto-Yenile: KAPALI"),
                _autoHotReload ? _activeButtonStyle : _buttonStyle,
                GUILayout.Width(140f)))
            {
                _autoHotReload = !_autoHotReload;
                _lastClickedAction = L("[Bottom Bar] Auto Hot-Reload", "[Alt Bar] Oto-Yenile (Auto Hot-Reload)");
                SetStatus(L("Auto Hot-Reload: ", "Otomatik Canlı Yenileme: ") + OnOff(_autoHotReload));
            }
            if (GUILayout.Button(L("📋 Logs (", "📋 Loglar (") + _actionStatusLogs.Count + ")", _buttonStyle, GUILayout.Width(105f)))
            {
                _selectedTab = 6;
            }
            if (GUILayout.Button(_showMiniBadge ? L("Hide Badge", "Rozeti Gizle") : L("Show Badge", "Rozeti Göster"), _buttonStyle, GUILayout.Width(100f)))
            {
                _showMiniBadge = !_showMiniBadge;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 28f));
        }

        public void ReceiveHotReloadState(
            bool showMenu,
            bool showMiniBadge,
            int selectedTab,
            Rect windowRect,
            bool godMode,
            bool infiniteNeeds,
            bool superSpeed,
            float speedMult,
            bool noRot,
            float prodMult,
            DateTime hotWriteUtc,
            List<string> logs,
            bool isAutoTrigger)
        {
            _isHotReloadedInstance = true;
            _showMenu = showMenu;
            _showMiniBadge = showMiniBadge;
            _selectedTab = selectedTab;
            _windowRect = windowRect;
            GodModeAllSettlers = godMode;
            InfiniteNeedsAllSettlers = infiniteNeeds;
            _superSpeedSettlers = superSpeed;
            _settlerSpeedMultiplier = speedMult;
            _noRotInfiniteFreshness = noRot;
            _productionSpeedMultiplier = prodMult;
            _lastHotReloadWriteUtc = hotWriteUtc;

            try
            {
                _isTurkish = PlayerPrefs.GetInt("GM_MegaTrainer_LangTR", 0) == 1;
            }
            catch { }

            if (logs != null && logs.Count > 0)
            {
                _actionStatusLogs.Clear();
                _actionStatusLogs.AddRange(logs);
            }

            _lastClickedAction = isAutoTrigger
                ? L("[Auto Hot-Reload]", "[Otomatik Canlı Yenileme]")
                : L("[F9 / Reload Button]", "[F9 / Canlı Yenile Butonu]");
            SetStatus(L(
                $"Hot-Reload completed! [Version v1.5.0 (EN/TR) - {DateTime.Now:HH:mm:ss}]",
                $"Canlı Yenileme (Hot-Reload) tamamlandı! [Sürüm v1.5.0 (EN/TR) - {DateTime.Now:HH:mm:ss}]"));
        }

        private void HotReloadTrainerNow(bool isAutoTrigger)
        {
            try
            {
                if (string.IsNullOrEmpty(_hotReloadDllPath))
                {
                    _hotReloadDllPath = Path.Combine(Paths.BepInExRootPath, "TrainerHotReload.dll");
                }

                if (!File.Exists(_hotReloadDllPath))
                {
                    SetStatus(L("TrainerHotReload.dll not found: ", "TrainerHotReload.dll bulunamadı: ") + _hotReloadDllPath);
                    return;
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(_hotReloadDllPath);
                byte[] rawBytes = File.ReadAllBytes(_hotReloadDllPath);
                Assembly newAsm = Assembly.Load(rawBytes);

                Type newPluginType = newAsm.GetType("GoingMedievalTrainer.MegaTrainerPlugin");
                Type newPatchesType = newAsm.GetType("GoingMedievalTrainer.TrainerHarmonyPatches");
                Type newBehaviourType = newAsm.GetType("GoingMedievalTrainer.MegaTrainerBehaviour");

                if (newBehaviourType == null)
                {
                    SetStatus(L(
                        "Hot-Reload error: MegaTrainerBehaviour class not found in new assembly!",
                        "Hot-Reload hatası: Yeni derlemede MegaTrainerBehaviour sınıfı bulunamadı!"));
                    return;
                }

                if (newPluginType != null)
                {
                    FieldInfo fiLog = newPluginType.GetField("Log", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    fiLog?.SetValue(null, MegaTrainerPlugin.Log);
                }

                try
                {
                    Harmony.UnpatchID("com.selcuk.goingmedieval.megatrainer.harmony");
                    if (newPatchesType != null)
                    {
                        var harmony = new Harmony("com.selcuk.goingmedieval.megatrainer.harmony");
                        harmony.PatchAll(newPatchesType);
                    }
                }
                catch (Exception hex)
                {
                    MegaTrainerPlugin.Log?.LogWarning("Hot-Reload Harmony patch warning: " + hex.Message);
                }

                _isReplacedByHotReload = true;
                if (Instance == this) Instance = null;

                Component newComp = gameObject.AddComponent(newBehaviourType);
                MethodInfo miReceive = newBehaviourType.GetMethod("ReceiveHotReloadState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (miReceive != null)
                {
                    miReceive.Invoke(newComp, new object[]
                    {
                        _showMenu,
                        _showMiniBadge,
                        _selectedTab,
                        _windowRect,
                        GodModeAllSettlers,
                        InfiniteNeedsAllSettlers,
                        _superSpeedSettlers,
                        _settlerSpeedMultiplier,
                        _noRotInfiniteFreshness,
                        _productionSpeedMultiplier,
                        writeUtc,
                        new List<string>(_actionStatusLogs),
                        isAutoTrigger
                    });
                }

                Destroy(this);
            }
            catch (Exception ex)
            {
                _isReplacedByHotReload = false;
                if (Instance == null) Instance = this;
                SetStatus(L("Hot-Reload error: ", "Hot-Reload hatası: ") + ex.Message, ex);
            }
        }

        private void DrawSettlersTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🛡 Continuous Settler Protections (Toggle)", "🛡 Sürekli Aktif Köylü Korumaları (Aç / Kapat)"), _headerStyle);

            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L("God Mode & Auto-Heal [F3]", "Ölümsüzlük & Otomatik Tedavi [F3]"), GodModeAllSettlers))
            {
                GodModeAllSettlers = !GodModeAllSettlers;
                if (GodModeAllSettlers) ApplyGodModeAndHealNow();
                SetStatus(L("God Mode: ", "Ölümsüzlük: ") + OnOff(GodModeAllSettlers));
            }
            if (DrawToggleButton(L("Infinite Needs & 100% Mood [F4]", "Sonsuz İhtiyaçlar & %100 Moral [F4]"), InfiniteNeedsAllSettlers))
            {
                InfiniteNeedsAllSettlers = !InfiniteNeedsAllSettlers;
                if (InfiniteNeedsAllSettlers) ApplyInfiniteNeedsNow();
                SetStatus(L("Infinite Needs & Mood: ", "Sonsuz İhtiyaçlar & Moral: ") + OnOff(InfiniteNeedsAllSettlers));
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L($"Super Movement & Work Speed ({_settlerSpeedMultiplier:0}x)", $"Süper Hız & Çalışma Çarpanı ({_settlerSpeedMultiplier:0}x)"), _superSpeedSettlers))
            {
                _superSpeedSettlers = !_superSpeedSettlers;
                ApplySettlerSpeedMultiplier(_superSpeedSettlers ? _settlerSpeedMultiplier : 1f);
                SetStatus(L("Settler Super Speed: ", "Köylü Süper Hız: ") + (_superSpeedSettlers ? $"{_settlerSpeedMultiplier:0}x {OnOff(true)}" : "Normal (1x)"));
            }
            GUILayout.Label(L($"Mult: {_settlerSpeedMultiplier:0}x", $"Çarpan: {_settlerSpeedMultiplier:0}x"), _labelStyle, GUILayout.Width(80f));
            float newMult = GUILayout.HorizontalSlider(_settlerSpeedMultiplier, 1f, 20f, GUILayout.Width(160f));
            if (Math.Abs(newMult - _settlerSpeedMultiplier) > 0.1f)
            {
                _settlerSpeedMultiplier = Mathf.Round(newMult);
                if (_superSpeedSettlers) ApplySettlerSpeedMultiplier(_settlerSpeedMultiplier);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("⚡ One-Click Bulk Settler Actions", "⚡ Tek Tıkla Toplu Köylü İşlemleri"), _headerStyle);

            GUILayout.BeginHorizontal();
            if (TrainerButton(L("❤ Heal & Fill All Needs Now", "❤ Tümünü Şimdi İyileştir & Doyur")))
            {
                ApplyGodModeAndHealNow();
                ApplyInfiniteNeedsNow();
                SetStatus(L(
                    "All settlers healed, wounds tended, and all needs set to 100%!",
                    "Tüm köylüler iyileştirildi, yaraları sarıldı ve ihtiyaçları %100 yapıldı!"));
            }
            if (TrainerButton(L("⭐ Max All Settler Skills (Level 50)", "⭐ Tüm Yetenekleri Maksimum (50) Yap")))
            {
                MaxAllSettlerSkills();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TrainerButton(L("🔥 Give Double Passion (2 Stars) to All Skills", "🔥 Tüm Yeteneklere Çift Yıldız (Tutku) Ver")))
            {
                MaxAllSettlerPassions();
            }
            if (TrainerButton(L("➕ Spawn +1 New Settler", "➕ Köyüme +1 Yeni Köylü Ekle")))
            {
                SpawnNewSettlerNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("👥 Settlement Citizens (Individual Actions)", "👥 Köydeki Köylüler (Bireysel Müdahale)"), _headerStyle);
            _settlerScrollPos = GUILayout.BeginScrollView(_settlerScrollPos, GUILayout.Height(210f));
            try
            {
                if (IsInActiveGameplay())
                {
                    var workers = WorkerManager.LabourersHere.ToList();
                    foreach (var w in workers)
                    {
                        if (w == null || w.HasDisposed) continue;
                        string name = w.Info != null ? w.Info.GetFullName() : (L("Settler #", "Köylü #") + w.GetUniqueId());
                        float hp = w.GetStat(StatType.Health)?.GetNormalizedPercentage() * 100f ?? 100f;
                        float mood = w.GetStat(StatType.Mood)?.GetNormalizedPercentage() * 100f ?? 100f;

                        GUILayout.BeginHorizontal();
                        GUILayout.Label(
                            L($"• {name}  |  HP: {hp:0}%  |  Mood: {mood:0}%", $"• {name}  |  Can: %{hp:0}  |  Moral: %{mood:0}"),
                            _labelStyle,
                            GUILayout.Width(350f));
                        if (TrainerButton(L($"Heal & Fill ({name})", $"İyileştir & Doyur ({name})"), GUILayout.Width(155f)))
                        {
                            HealAndFillSingleSettler(w);
                            SetStatus(L($"{name} fully healed and needs filled.", $"{name} tamamen iyileştirildi ve doyuruldu."));
                        }
                        if (TrainerButton($"Skill 50 ({name})", GUILayout.Width(135f)))
                        {
                            MaxSingleSettlerSkills(w);
                            SetStatus(L($"{name} all skills maxed to 50!", $"{name} tüm yetenekleri 50 yapıldı!"));
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }
            catch { }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawConstructionTab()
        {
            bool autoConstruct = false;
            bool noResourceBuild = false;
            bool allVariants = false;
            bool craftableBuildings = false;
            bool instantDig = false;
            bool instantCut = false;
            bool forceHarvest = false;

            try
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    var bpm = MonoSingleton<BuildingPlacementManager>.Instance;
                    autoConstruct = bpm.Autoconstruct;
                    noResourceBuild = bpm.ConstructWithoutResource;
                    allVariants = bpm.VariantsUnlocked;
                    craftableBuildings = bpm.CraftableBuildingsEnabled;
                }
                if (MonoSingleton<GroundManager>.IsInstantiated())
                {
                    instantDig = MonoSingleton<GroundManager>.Instance.InstantDig;
                }
                if (MonoSingleton<PlantResourceManager>.IsInstantiated())
                {
                    instantCut = MonoSingleton<PlantResourceManager>.Instance.InstantCut;
                    forceHarvest = MonoSingleton<PlantResourceManager>.Instance.ForceHarvestPhase;
                }
            }
            catch { }

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🏗 Building & Terrain Cheats [F5 Toggle All]", "🏗 İnşaat & Harita Hileleri [F5 ile Toplu Aç/Kapat]"), _headerStyle);

            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L("Instant Auto-Construct (AutoConstruct)", "Anında Otomatik İnşaat (AutoConstruct)"), autoConstruct))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    MonoSingleton<BuildingPlacementManager>.Instance.AutoconstructToggle();
                    SetStatus(L("Instant Build: ", "Anında İnşaat: ") + OnOff(MonoSingleton<BuildingPlacementManager>.Instance.Autoconstruct));
                }
            }
            if (DrawToggleButton(L("Build Without Resources (Free Build)", "Kaynaksız İnşaat (Malzeme İstemez)"), noResourceBuild))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    var bpm = MonoSingleton<BuildingPlacementManager>.Instance;
                    bpm.ConstructWithoutResource = !bpm.ConstructWithoutResource;
                    SetStatus(L("Free Build (No Resources): ", "Kaynaksız İnşaat: ") + OnOff(bpm.ConstructWithoutResource));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L("Unlock All Building Variants", "Tüm Bina Varyantlarını Aç"), allVariants))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    MonoSingleton<BuildingPlacementManager>.Instance.VariantsUnlockedToggle();
                    SetStatus(L("All Building Variants: ", "Tüm Varyantlar: ") + OnOff(MonoSingleton<BuildingPlacementManager>.Instance.VariantsUnlocked));
                }
            }
            if (DrawToggleButton(L("Place Craftable Buildings Directly", "Üretilebilir Binaları Doğrudan İnşa Et"), craftableBuildings))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    MonoSingleton<BuildingPlacementManager>.Instance.CraftableBuildingsEnabledToggle();
                    SetStatus(L("Craftable Buildings Direct Place: ", "Üretilebilir Binalar: ") + OnOff(MonoSingleton<BuildingPlacementManager>.Instance.CraftableBuildingsEnabled));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L("Instant Digging / Mining (Instant Dig)", "Anında Kazı / Madencilik (Instant Dig)"), instantDig))
            {
                if (MonoSingleton<GroundManager>.IsInstantiated())
                {
                    var gm = MonoSingleton<GroundManager>.Instance;
                    gm.InstantDig = !gm.InstantDig;
                    SetStatus(L("Instant Dig: ", "Anında Kazı: ") + OnOff(gm.InstantDig));
                }
            }
            if (DrawToggleButton(L("Instant Tree/Plant Cutting (Instant Cut)", "Anında Ağaç/Bitki Kesimi (Instant Cut)"), instantCut))
            {
                if (MonoSingleton<PlantResourceManager>.IsInstantiated())
                {
                    MonoSingleton<PlantResourceManager>.Instance.InstantCutToggle();
                    SetStatus(L("Instant Cut: ", "Anında Kesim: ") + OnOff(MonoSingleton<PlantResourceManager>.Instance.InstantCut));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L("Force Crops to Harvest Phase", "Ekinleri Anında Hasat Aşamasına Getir"), forceHarvest))
            {
                if (MonoSingleton<PlantResourceManager>.IsInstantiated())
                {
                    MonoSingleton<PlantResourceManager>.Instance.ForceHarvestPhaseToggle();
                    SetStatus(L("Force Harvest Phase: ", "Anında Hasat Evresi: ") + OnOff(MonoSingleton<PlantResourceManager>.Instance.ForceHarvestPhase));
                }
            }
            if (TrainerButton(L("⚡ Toggle All Fast Build/Dig Cheats [F5]", "⚡ Tüm Hızlı İnşa/Kazı Hilelerini Aç [F5]")))
            {
                ToggleAllFastBuildCheats();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🔬 Production Speed, Blueprints & Research", "🔬 Üretim Hızı, Mavi Planlar (Blueprints) & Araştırma"), _headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L($"Global Workbench Speed: {_productionSpeedMultiplier:0}x", $"Global Tezgah Üretim Hızı: {_productionSpeedMultiplier:0}x"), _labelStyle, GUILayout.Width(220f));
            float newProd = GUILayout.HorizontalSlider(_productionSpeedMultiplier, 1f, 100f, GUILayout.Width(440f));
            if (Math.Abs(newProd - _productionSpeedMultiplier) > 0.2f)
            {
                float rounded = Mathf.Round(newProd);
                if (Math.Abs(rounded - _productionSpeedMultiplier) > 0.1f)
                {
                    _lastClickedAction = $"[{CurrentTabName()}] Slider ({rounded:0}x)";
                    SetGlobalProductionSpeed(rounded);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Quick Presets:", "Hızlı Seçim:"), _labelStyle, GUILayout.Width(95f));
            float[] prodPresets = new float[] { 1f, 2f, 5f, 10f, 15f, 20f, 30f, 50f, 100f };
            foreach (float preset in prodPresets)
            {
                bool isCurrent = Math.Abs(_productionSpeedMultiplier - preset) < 0.1f;
                string btnLabel = preset == 1f ? "1x (Normal)" : $"{preset:0}x";
                GUIStyle st = isCurrent ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(btnLabel, st, GUILayout.Height(28f)))
                {
                    _lastClickedAction = $"[{CurrentTabName()}] Preset ({btnLabel})";
                    SetGlobalProductionSpeed(preset);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("🏛 Finish All Blueprints on Map [F6]", "🏛 Haritadaki Tüm İnşaat Planlarını Bitir [F6]"), GUILayout.Height(36f)))
            {
                FinishAllBlueprintsNow();
            }
            if (TrainerButton(L("📚 Unlock All Research (Technologies)", "📚 Tüm Araştırmaları (Teknolojileri) Aç"), GUILayout.Height(36f)))
            {
                UnlockAllResearchNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawResourcesTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("📦 Stockpile & Storage Cheats", "📦 Depo & Mevcut Yığın (Stockpile) Hileleri"), _headerStyle);

            GUILayout.BeginHorizontal();
            if (DrawToggleButton(L("No Rot / Always 100% Freshness & HP", "Çürüme Yok / Sürekli %100 Tazelik & Dayanıklılık"), _noRotInfiniteFreshness))
            {
                _noRotInfiniteFreshness = !_noRotInfiniteFreshness;
                if (_noRotInfiniteFreshness) KeepAllResourcesFreshAndHealthy();
                SetStatus(L("No Rot (Infinite Freshness): ", "Çürüme Yok (Sonsuz Tazelik): ") + OnOff(_noRotInfiniteFreshness));
            }
            if (TrainerButton(L("💎 Refill All Existing Stockpiles to Max [F7]", "💎 Mevcut Tüm Yığınları Maksimuma Doldur [F7]")))
            {
                RefillAllStockpilesAndFreshness();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🎁 Resource & Item Spawner (Spawns Next to Settler)", "🎁 Kaynak & Eşya Oluşturucu (Köylünün Yanına Spawn Eder)"), _headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Amount:", "Miktar:"), _labelStyle, GUILayout.Width(60f));
            foreach (int amt in new int[] { 25, 50, 100, 250, 500 })
            {
                GUIStyle st = (_spawnAmount == amt) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(amt.ToString(), st, GUILayout.Width(55f)))
                {
                    _spawnAmount = amt;
                    _lastClickedAction = $"[{CurrentTabName()}] Amount: {amt}";
                    SetStatus(L($"Spawn amount set to {amt}.", $"Spawn miktarı {amt} olarak ayarlandı."));
                }
            }
            GUILayout.Space(10f);
            GUILayout.Label(L("Search:", "Ara:"), _labelStyle, GUILayout.Width(50f));
            _resourceSearchQuery = GUILayout.TextField(_resourceSearchQuery ?? "", GUILayout.Width(170f));
            if (GUILayout.Button(L("Clear", "Temizle"), _buttonStyle, GUILayout.Width(70f)))
            {
                _resourceSearchQuery = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            DrawQuickSpawnButton(L("Wood (wood)", "Odun (wood)"), "wood");
            DrawQuickSpawnButton(L("Limestone", "Kireçtaşı (limestone)"), "limestone");
            DrawQuickSpawnButton(L("Clay (clay)", "Kil (clay)"), "clay");
            DrawQuickSpawnButton(L("Iron Ingot", "Demir Külçe (iron_ingot)"), "iron_ingot");
            DrawQuickSpawnButton(L("Steel Ingot", "Çelik Külçe (steel_ingot)"), "steel_ingot");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawQuickSpawnButton(L("Gold Ingot", "Altın Külçe (gold_ingot)"), "gold_ingot");
            DrawQuickSpawnButton(L("Coal (coal)", "Kömür (coal)"), "coal");
            DrawQuickSpawnButton(L("Limestone Block", "Kireçtaşı Blok"), "limestone_block");
            DrawQuickSpawnButton(L("Clay Brick", "Kil Tuğla"), "clay_brick");
            DrawQuickSpawnButton(L("Packaged Meal", "Yemek (packaged_meal)"), "packaged_meal");
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            EnsureResourcesCached();

            _resourceScrollPos = GUILayout.BeginScrollView(_resourceScrollPos, GUILayout.Height(230f));
            if (_cachedResources != null && _cachedResources.Count > 0)
            {
                string q = (_resourceSearchQuery ?? "").Trim().ToLowerInvariant();
                int shown = 0;
                foreach (var res in _cachedResources)
                {
                    if (res == null) continue;
                    string id = res.GetID();
                    if (string.IsNullOrEmpty(id)) continue;
                    string cat = res.Category.ToString();
                    if (!string.IsNullOrEmpty(q) && !id.ToLowerInvariant().Contains(q) && !cat.ToLowerInvariant().Contains(q))
                        continue;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"[{cat}] {id} ({L("Max Stack", "Max Yığın")}: {res.StackingLimit})", _labelStyle, GUILayout.Width(470f));
                    if (GUILayout.Button(L($"+{_spawnAmount} Spawn", $"+{_spawnAmount} Oluştur"), _buttonStyle, GUILayout.Width(140f)))
                    {
                        _lastClickedAction = $"[{CurrentTabName()}] +{ _spawnAmount } {id}";
                        SpawnResourceById(id, _spawnAmount);
                    }
                    GUILayout.EndHorizontal();

                    shown++;
                    if (shown >= 120)
                    {
                        GUILayout.Label(L("... Use the search box above for more results ...", "... Daha fazla sonuç için yukarıdaki arama kutusunu kullanın ..."), _labelStyle);
                        break;
                    }
                }
            }
            else
            {
                GUILayout.Label(L("Resource list will populate automatically once a map is loaded.", "Kaynak listesi harita yüklendiğinde otomatik dolacaktır."), _labelStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawQuickSpawnButton(string label, string resourceId)
        {
            if (GUILayout.Button(label, _buttonStyle))
            {
                _lastClickedAction = $"[{CurrentTabName()}] Quick Spawn: {label} (id={resourceId}, qty={_spawnAmount})";
                SpawnResourceById(resourceId, _spawnAmount);
            }
        }

        // [0]=id, [1]=TR Başlık, [2]=TR Açıklama, [3]=EN Title, [4]=EN Description
        private static readonly string[][] AnimalCatalog = new string[][]
        {
            new string[] { "cow",        "🐄 İnek (cow)",               "Süt / Et / Taşıma (Pet)",        "🐄 Cow (cow)",               "Milk / Meat / Hauling (Pet)" },
            new string[] { "sheep",      "🐑 Koyun (sheep)",            "Yün / Süt / Et",                 "🐑 Sheep (sheep)",           "Wool / Milk / Meat" },
            new string[] { "goat",       "🐐 Keçi (goat)",              "Süt / Et / Taşıma (Pet)",        "🐐 Goat (goat)",             "Milk / Meat / Hauling (Pet)" },
            new string[] { "donkey",     "🫏 Eşek (donkey)",            "Yük Taşıma (Pet)",               "🫏 Donkey (donkey)",         "Heavy Hauling (Pet)" },
            new string[] { "chicken",    "🐔 Tavuk (chicken)",          "Yumurta / Et",                   "🐔 Chicken (chicken)",       "Eggs / Meat" },
            new string[] { "boar",       "🐗 Yaban Domuzu (boar)",      "Et / Savaş & Taşıma (Pet)",      "🐗 Wild Boar (boar)",        "Meat / Combat & Hauling (Pet)" },
            new string[] { "dog",        "🐕 Köpek (dog)",              "Taşıma & Savaş (Pet)",           "🐕 Dog (dog)",               "Hauling & Combat (Pet)" },
            new string[] { "cat",        "🐈 Kedi (cat)",               "Haşere Avcısı (Pet)",            "🐈 Cat (cat)",               "Pest Hunter (Pet)" },
            new string[] { "bear",       "🐻 Ayı (bear)",               "Güçlü Savaş & Taşıma (Pet)",     "🐻 Bear (bear)",             "Heavy Combat & Hauling (Pet)" },
            new string[] { "wolf",       "🐺 Kurt (wolf)",              "Savaş & Taşıma (Pet)",           "🐺 Wolf (wolf)",             "Combat & Hauling (Pet)" },
            new string[] { "fox",        "🦊 Tilki (fox)",              "Taşıma & Haşere (Pet)",          "🦊 Fox (fox)",               "Hauling & Pest Control (Pet)" },
            new string[] { "deer",       "🦌 Geyik (deer)",             "Et / Deri / Taşıma (Pet)",       "🦌 Deer (deer)",             "Meat / Leather / Hauling (Pet)" },
            new string[] { "hare",       "🐇 Tavşan (hare)",            "Küçük Av / Çiftlik",             "🐇 Hare (hare)",             "Small Game / Livestock" },
            new string[] { "mallard",    "🦆 Yeşilbaş Ördek (mallard)", "Yumurta / Tüy",                  "🦆 Mallard Duck (mallard)",  "Eggs / Feathers" },
            new string[] { "pheasant",   "🐦 Sülün (pheasant)",         "Yumurta / Tüy",                  "🐦 Pheasant (pheasant)",     "Eggs / Feathers" },
            new string[] { "polecat",    "🦡 Sansar (polecat)",         "Haşere Avcısı",                  "🦡 Polecat (polecat)",       "Pest Hunter" },
            new string[] { "rat",        "🐀 Sıçan (rat)",              "Küçük Hayvan",                   "🐀 Rat (rat)",               "Small Creature" },
            new string[] { "water_vole", "🐹 Su Sıçanı (water_vole)",   "Küçük Hayvan",                   "🐹 Water Vole (water_vole)", "Small Creature" }
        };

        private void DrawCombatAndCreaturesTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("⚔ Combat, Prisoners & Existing Animals", "⚔ Savaş, Tutsak & Mevcut Hayvan Kontrolleri"), _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("💀 Kill All Enemies [F8]", "💀 Tüm Düşmanları Öldür [F8]"), GUILayout.Height(32f)))
            {
                KillAllEnemiesNow();
            }
            if (TrainerButton(L("🤝 Recruit All Prisoners (100%)", "🤝 Tüm Tutsakları %100 İkna Et"), GUILayout.Height(32f)))
            {
                RecruitAllPrisonersNow();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TrainerButton(L("🥛 Complete Animal Production (Milk/Wool/Egg)", "🥛 Hayvan Üretimlerini Tamamla (Süt/Yün/Yumurta)"), GUILayout.Height(32f)))
            {
                CompleteAllAnimalProductionNow();
            }
            if (TrainerButton(L("🐕 Tame & Train All Animals on Map", "🐕 Haritadaki Tüm Hayvanları Evcilleştir & Eğit"), GUILayout.Height(32f)))
            {
                TameAndTrainAllAnimalsNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🐾 Animal Spawner (Wild / Domestic / Pet - Next to Settler)", "🐾 Hayvan Oluşturucu (Yabani / Ehli / Evcil Seçimli - Köylünün Yanına)"), _headerStyle);

            // 1. Satır: Tür Seçimi (Yabani / Ehli / Evcil)
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Mode:", "Durum:"), _labelStyle, GUILayout.Width(55f));
            string[] modeNames = _isTurkish
                ? new string[] { "🐺 Yabani (Wild)", "🐄 Ehli / Çiftlik (Domestic)", "🐕 Evcil / Eğitimli (Pet)" }
                : new string[] { "🐺 Wild", "🐄 Domestic (Livestock)", "🐕 Trained Pet (Haul/Follow)" };

            for (int m = 0; m < modeNames.Length; m++)
            {
                GUIStyle st = (_animalSpawnMode == m) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(modeNames[m], st, GUILayout.Height(28f)))
                {
                    _animalSpawnMode = m;
                    _lastClickedAction = $"[{CurrentTabName()}] Animal Mode: {modeNames[m]}";
                    SetStatus(L($"Animal spawn mode set to [{modeNames[m]}].", $"Hayvan spawn durumu [{modeNames[m]}] olarak seçildi."));
                }
            }
            GUILayout.EndHorizontal();

            // 2. Satır: Cinsiyet, Yaş ve Adet Seçimi
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Sex:", "Cinsiyet:"), _labelStyle, GUILayout.Width(60f));
            string[] sexNames = _isTurkish
                ? new string[] { "🎲 Rastgele", "♂ Erkek", "♀ Dişi" }
                : new string[] { "🎲 Random", "♂ Male", "♀ Female" };
            for (int s = 0; s < sexNames.Length; s++)
            {
                GUIStyle st = (_animalSpawnSex == s) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(sexNames[s], st, GUILayout.Width(90f), GUILayout.Height(26f)))
                {
                    _animalSpawnSex = s;
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label(L("Age:", "Evre:"), _labelStyle, GUILayout.Width(40f));
            if (GUILayout.Button(_animalSpawnMature ? L("Adult", "Yetişkin") : L("Young", "Yavru"), _activeButtonStyle, GUILayout.Width(75f), GUILayout.Height(26f)))
            {
                _animalSpawnMature = !_animalSpawnMature;
            }

            GUILayout.Space(8f);
            GUILayout.Label(L("Qty:", "Adet:"), _labelStyle, GUILayout.Width(38f));
            foreach (int cnt in new int[] { 1, 2, 5, 10 })
            {
                GUIStyle st = (_animalSpawnCount == cnt) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(cnt.ToString(), st, GUILayout.Width(36f), GUILayout.Height(26f)))
                {
                    _animalSpawnCount = cnt;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            string activeModeShort = _animalSpawnMode == 0
                ? L("Wild", "Yabani")
                : (_animalSpawnMode == 1 ? L("Domestic", "Ehli") : L("Pet", "Evcil Pet"));
            _animalScrollPos = GUILayout.BeginScrollView(_animalScrollPos, GUILayout.Height(260f));
            for (int i = 0; i < AnimalCatalog.Length; i++)
            {
                string aId = AnimalCatalog[i][0];
                string aTitle = _isTurkish ? AnimalCatalog[i][1] : AnimalCatalog[i][3];
                string aDesc = _isTurkish ? AnimalCatalog[i][2] : AnimalCatalog[i][4];

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{aTitle}  —  {aDesc}", _labelStyle, GUILayout.Width(455f));
                string btnText = _isTurkish
                    ? $"+{_animalSpawnCount} {activeModeShort} Oluştur"
                    : $"Spawn +{_animalSpawnCount} {activeModeShort}";
                if (GUILayout.Button(btnText, _buttonStyle, GUILayout.Width(210f), GUILayout.Height(26f)))
                {
                    _lastClickedAction = $"[{CurrentTabName()}] Spawn Animal: {aTitle} (Mode={activeModeShort}, Sex={sexNames[_animalSpawnSex]}, Adult={_animalSpawnMature}, Qty={_animalSpawnCount})";
                    SpawnCustomAnimalsNow(aId, aTitle);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawTimeAndWeatherTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("⏩ Game Speed (TimeScale)", "⏩ Oyun Hızı (TimeScale)"), _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("Pause (0x)", "Duraklat (0x)"))) SetGameSpeed(0f);
            if (TrainerButton("Normal (1x)")) SetGameSpeed(1f);
            if (TrainerButton(L("Fast (3x)", "Hızlı (3x)"))) SetGameSpeed(3f);
            if (TrainerButton(L("Very Fast (5x)", "Çok Hızlı (5x)"))) SetGameSpeed(5f);
            if (TrainerButton("Ultra (10x)")) SetGameSpeed(10f);
            if (TrainerButton("Turbo (20x)")) SetGameSpeed(20f);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("☀ Time of Day", "☀ Günün Saati"), _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("🌅 Morning (06:00)", "🌅 Sabah (06:00)"))) SetTimeOfDay(0.25f);
            if (TrainerButton(L("☀ Noon (12:00)", "☀ Öğle (12:00)"))) SetTimeOfDay(0.50f);
            if (TrainerButton(L("🌇 Evening (18:00)", "🌇 Akşam (18:00)"))) SetTimeOfDay(0.75f);
            if (TrainerButton(L("🌙 Midnight (00:00)", "🌙 Gece Yarısı (00:00)"))) SetTimeOfDay(0.02f);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🍂 Change Season", "🍂 Mevsim Değiştir"), _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("🌸 Spring", "🌸 İlkbahar (Spring)"))) SetSeason(0);
            if (TrainerButton(L("☀ Summer", "☀ Yaz (Summer)"))) SetSeason(1);
            if (TrainerButton(L("🍁 Autumn", "🍁 Sonbahar (Autumn)"))) SetSeason(2);
            if (TrainerButton(L("❄ Winter", "❄ Kış (Winter)"))) SetSeason(3);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🌧 Weather Control", "🌧 Hava Durumu"), _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("☀ Clear / Sunny", "☀ Açık (Clear)"))) SetWeather("clear");
            if (TrainerButton(L("🌧 Rain", "🌧 Yağmur (Rain)"))) SetWeather("rain");
            if (TrainerButton(L("🌫 Fog", "🌫 Sis (Fog)"))) SetWeather("fog");
            if (TrainerButton(L("❄ Snow", "❄ Kar (Snow)"))) SetWeather("snow");
            if (TrainerButton(L("⛈ Thunderstorm", "⛈ Fırtına (Thunderstorm)"))) SetWeather("thunderstorm");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawDevConsoleTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("🛠 Native Hidden Developer Panel (DeveloperToolsView)", "🛠 Oyunun Kendi Gizli Geliştirici Paneli (DeveloperToolsView)"), _headerStyle);
            GUILayout.Label(
                L(
                    "Directly opens the built-in developer cheat & testing panel created by Going Medieval's developers.",
                    "Going Medieval'ın geliştiricilerinin kullandığı orijinal hile ve test panelini doğrudan açar."),
                _labelStyle);
            if (TrainerButton(L("🔧 Toggle Native Developer Panel [F2]", "🔧 Orijinal Geliştirici Panelini Aç / Kapat [F2]"), GUILayout.Height(36f)))
            {
                ToggleNativeDevTools();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("💻 Developer Console Command Runner", "💻 Geliştirici Konsolu Komut Çalıştırıcı"), _headerStyle);
            GUILayout.BeginHorizontal();
            _customConsoleCommand = GUILayout.TextField(_customConsoleCommand ?? "", GUILayout.Width(460f));
            if (TrainerButton(L("Run Command", "Komutu Çalıştır")))
            {
                ExecuteConsoleCommand(_customConsoleCommand);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label(L("Quick Console Commands:", "Hazır Konsol Komutları:"), _labelStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton(L("Allow Map Edge Building", "Harita Sınırına İnşaat İzni"))) ExecuteConsoleCommand("toggleAllowEdgePlacement");
            if (TrainerButton(L("Unlock Locked Buildings", "Kilitli Binaları Aç"))) ExecuteConsoleCommand("unlockLockedBuildings");
            if (TrainerButton(L("Auto-Craft Without Resources", "Kaynaksız Otomatik Üretim"))) ExecuteConsoleCommand("autoprodResources");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TrainerButton(L("Auto-Craft Without Workers", "İşçisiz Otomatik Üretim"))) ExecuteConsoleCommand("autoprodWorkers");
            if (TrainerButton(L("Toggle Combat Damage Numbers", "Savaş Hasar Sayılarını Göster")))
            {
                DeveloperVariables.ShowCombatNumbers = !DeveloperVariables.ShowCombatNumbers;
                SetStatus(L("Combat Damage Numbers: ", "Savaş Hasar Sayıları: ") + OnOff(DeveloperVariables.ShowCombatNumbers));
            }
            if (TrainerButton(L("Open Water Editor (waterEdiort)", "Su Editörünü Aç (waterEdiort)"))) ExecuteConsoleCommand("waterEdiort");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawLogsTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(L("📋 Clicked Action & Status Logs", "📋 Tıklanan Buton & Oluşan Durum Logları"), _headerStyle);
            GUILayout.Label(L($"File Path: {_logFilePath}", $"Dosya Yolu: {_logFilePath}"), _labelStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("📋 Copy All Logs to Clipboard", "📋 Tüm Logları Panoya Kopyala"), _buttonStyle, GUILayout.Height(32f)))
            {
                GUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, _actionStatusLogs);
                _statusMessage = L(
                    $"All logs ({_actionStatusLogs.Count} lines) copied to clipboard! (Paste with Ctrl+V)",
                    $"Tüm loglar ({_actionStatusLogs.Count} satır) panoya kopyalandı! (Ctrl+V ile yapıştırabilirsiniz)");
            }
            if (GUILayout.Button(L("📂 Open Log File in Notepad", "📂 Log Dosyasını Notepad İle Aç"), _buttonStyle, GUILayout.Height(32f)))
            {
                try
                {
                    if (File.Exists(_logFilePath))
                    {
                        Process.Start("notepad.exe", $"\"{_logFilePath}\"");
                    }
                }
                catch { }
            }
            if (GUILayout.Button(L("🗑 Clear Screen List", "🗑 Ekran Listesini Temizle"), _buttonStyle, GUILayout.Height(32f)))
            {
                _actionStatusLogs.Clear();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            _logScrollPos = GUILayout.BeginScrollView(_logScrollPos, GUILayout.Height(390f));
            if (_actionStatusLogs.Count == 0)
            {
                GUILayout.Label(
                    L(
                        "No action logs yet. Click any cheat button and its result will appear here and in the log file.",
                        "Henüz bir işlem kaydı yok. Herhangi bir butona tıkladığınızda burada ve log dosyasında görünecektir."),
                    _labelStyle);
            }
            else
            {
                for (int i = 0; i < _actionStatusLogs.Count; i++)
                {
                    GUILayout.Label(_actionStatusLogs[i], _labelStyle);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        // =====================================================================
        // FONKSİYONEL HİLE METOTLARI
        // =====================================================================

        private void ApplyGodModeAndHealNow()
        {
            try
            {
                if (!IsInActiveGameplay()) return;
                foreach (var worker in WorkerManager.LabourersHere)
                {
                    if (worker == null || worker.HasDisposed) continue;
                    HealSingleCreatureStatic(worker);
                }
            }
            catch (Exception ex)
            {
                MegaTrainerPlugin.Log?.LogWarning("ApplyGodModeAndHealNow hatası: " + ex.Message);
            }
        }

        private static readonly string[] NegativeEffectorsToClear = new string[]
        {
            "InebriatedLow", "InebriatedMed", "InebriatedHigh", "InebriatedVeryHigh",
            "InebriatedWorkLowPerkEffector", "InebriatedWorkMedPerkEffector", "InebriatedWorkHighPerkEffector", "InebriatedWorkVeryHighPerkEffector",
            "InebriatedLowPunchDrunk", "InebriatedMedPunchDrunk", "InebriatedHighPunchDrunk", "InebriatedVeryHighPunchDrunk",
            "InebriatedVeryHighLightweight", "DrankWhileDrunk",
            "ConDmgPain01", "ConDmgPain02", "ConDmgPain03",
            "ConDmgBloodLoss01", "ConDmgBloodLoss02", "ConDmgBloodLoss03", "BleedingWarning",
            "ConDmgHP01", "CriticalWarning",
            "ConLoseMotor", "ConLoseMotorHigh", "WorkerFaintEffector", "WorkerFaintEffectorInstant",
            "MalnutritionMinor", "MalnutritionModerate", "MalnutritionSevere", "MalnutritionExtreme",
            "MalnutritionMinorAustere", "MalnutritionModerateAustere", "MalnutritionSevereAustere", "MalnutritionExtremeAustere",
            "Starving", "VeryHungry", "Hungry", "SlightlyHungry",
            "ExtremeNeedSleep", "ExtremeNeedSleepConLoss", "NeedSleep", "SlightNeedSleep",
            "StomachHurtLow", "StomachHurtMed", "VomitAfter", "stomach_pain",
            "Chilly", "Cold", "Freezing", "FreezinDying",
            "Sweaty", "Hot", "VeryHot", "VeryHotDying", "VeryHotDyingFaster"
        };

        public static void ClearNegativeEffectorsStatic(StatsInstance stats)
        {
            if (stats == null) return;
            foreach (string eff in NegativeEffectorsToClear)
            {
                try
                {
                    if (stats.IsEffectorActive(eff))
                    {
                        stats.EndEffector(eff);
                    }
                }
                catch { }
            }
        }

        public static void HealSingleCreatureStatic(CreatureBase creature)
        {
            if (creature == null || creature.HasDisposed || creature.Stats == null) return;

            MaxStatStatic(creature.Stats, StatType.Health);
            MaxStatStatic(creature.Stats, StatType.Blood);
            MaxStatStatic(creature.Stats, StatType.Consciousness);
            MaxStatStatic(creature.Stats, StatType.Pain);
            MaxStatStatic(creature.Stats, StatType.Malnutrition);
            MaxStatStatic(creature.Stats, StatType.Inebriated);
            MaxStatStatic(creature.Stats, StatType.Stomach);
            MaxStatStatic(creature.Stats, StatType.TemperatureCold);
            MaxStatStatic(creature.Stats, StatType.TemperatureHot);
            MaxStatStatic(creature.Stats, StatType.Dryness);
            MaxStatStatic(creature.Stats, StatType.Breath);

            ClearNegativeEffectorsStatic(creature.Stats);

            try { creature.TendWounds(100f); } catch { }
            try { creature.Stats.Update(); } catch { }
            try { creature.UnFaint(); } catch { }
        }

        private void ApplyInfiniteNeedsNow()
        {
            try
            {
                if (!IsInActiveGameplay()) return;
                foreach (var worker in WorkerManager.LabourersHere)
                {
                    if (worker == null || worker.HasDisposed || worker.Stats == null) continue;
                    MaxStatStatic(worker.Stats, StatType.Hunger);
                    MaxStatStatic(worker.Stats, StatType.Stomach);
                    MaxStatStatic(worker.Stats, StatType.Sleep);
                    MaxStatStatic(worker.Stats, StatType.Mood);
                    MaxStatStatic(worker.Stats, StatType.Entertaiment);
                    MaxStatStatic(worker.Stats, StatType.EntertainmentInt);
                    MaxStatStatic(worker.Stats, StatType.EntertainmentRanged);
                    MaxStatStatic(worker.Stats, StatType.EntertainmentDex);
                    MaxStatStatic(worker.Stats, StatType.EntertainmentPrecision);
                    MaxStatStatic(worker.Stats, StatType.Comfort);
                    MaxStatStatic(worker.Stats, StatType.Faith);
                    MaxStatStatic(worker.Stats, StatType.Alcohol);
                    MaxStatStatic(worker.Stats, StatType.Beauty);
                    MaxStatStatic(worker.Stats, StatType.Social);
                    MaxStatStatic(worker.Stats, StatType.Inebriated);
                    MaxStatStatic(worker.Stats, StatType.Malnutrition);
                    MaxStatStatic(worker.Stats, StatType.Pain);
                    MaxStatStatic(worker.Stats, StatType.Consciousness);
                    MaxStatStatic(worker.Stats, StatType.TemperatureCold);
                    MaxStatStatic(worker.Stats, StatType.TemperatureHot);
                    MaxStatStatic(worker.Stats, StatType.Dryness);
                    MaxStatStatic(worker.Stats, StatType.FoodTypeA);
                    MaxStatStatic(worker.Stats, StatType.FoodTypeB);
                    MaxStatStatic(worker.Stats, StatType.FoodTypeC);

                    ClearNegativeEffectorsStatic(worker.Stats);
                    try { worker.Stats.Update(); } catch { }
                    try { worker.UnFaint(); } catch { }
                }
            }
            catch (Exception ex)
            {
                MegaTrainerPlugin.Log?.LogWarning("ApplyInfiniteNeedsNow hatası: " + ex.Message);
            }
        }

        private void HealAndFillSingleSettler(HumanoidInstance worker)
        {
            if (worker == null || worker.HasDisposed) return;
            HealSingleCreatureStatic(worker);
            if (worker.Stats != null)
            {
                MaxStatStatic(worker.Stats, StatType.Hunger);
                MaxStatStatic(worker.Stats, StatType.Stomach);
                MaxStatStatic(worker.Stats, StatType.Sleep);
                MaxStatStatic(worker.Stats, StatType.Mood);
                MaxStatStatic(worker.Stats, StatType.Entertaiment);
                MaxStatStatic(worker.Stats, StatType.Comfort);
            }
        }

        public static void MaxStatStatic(StatsInstance stats, StatType type)
        {
            try
            {
                var st = stats.GetStat(type);
                if (st != null) st.SetCurrent(st.Max);
            }
            catch { }
        }

        public static void MinStatStatic(StatsInstance stats, StatType type)
        {
            try
            {
                var st = stats.GetStat(type);
                if (st != null) st.SetCurrent(st.Min);
            }
            catch { }
        }

        private void ApplySettlerSpeedMultiplier(float mult)
        {
            try
            {
                if (!IsInActiveGameplay()) return;
                AttributeType[] targetAttrs = new AttributeType[]
                {
                    AttributeType.MovementSpeed,
                    AttributeType.GlobalWorkSpeed,
                    AttributeType.ConstructionSpeed,
                    AttributeType.MineSpeed,
                    AttributeType.CutSpeed,
                    AttributeType.HarvestSpeed,
                    AttributeType.CookingSpeed,
                    AttributeType.SmithingSpeed,
                    AttributeType.ResearchSpeed,
                    AttributeType.GlobalXpGain
                };

                foreach (var worker in WorkerManager.LabourersHere)
                {
                    if (worker == null || worker.HasDisposed || worker.Stats == null) continue;
                    foreach (var attrType in targetAttrs)
                    {
                        var attr = worker.Stats.GetAttributeInstance(attrType);
                        if (attr != null)
                        {
                            attr.SetFixedMultiplier(mult);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MegaTrainerPlugin.Log?.LogWarning("ApplySettlerSpeedMultiplier hatası: " + ex.Message);
            }
        }

        private void MaxAllSettlerSkills()
        {
            try
            {
                if (!IsInActiveGameplay()) return;
                int count = 0;
                foreach (var worker in WorkerManager.LabourersHere)
                {
                    if (worker == null || worker.HasDisposed) continue;
                    MaxSingleSettlerSkills(worker);
                    count++;
                }
                SetStatus(L(
                    $"All skills maxed to level 50 for {count} settlers!",
                    $"{count} köylünün tüm yetenekleri (Skills) maksimum seviyeye (50) çıkarıldı!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Skill maxing error: ", "Yetenek artırma hatası: ") + ex.Message, ex);
            }
        }

        private void MaxSingleSettlerSkills(HumanoidInstance worker)
        {
            if (worker == null || worker.Skills == null || worker.Skills.Skills == null) return;
            foreach (var skill in worker.Skills.Skills)
            {
                if (skill == null) continue;
                int maxLvl = skill.GetMaxLevel();
                if (maxLvl <= 0) maxLvl = 50;
                skill.SetLevel(maxLvl);
                skill.ResetExperienceAddedToday();
            }
        }

        private void MaxAllSettlerPassions()
        {
            try
            {
                if (!IsInActiveGameplay()) return;
                int count = 0;
                foreach (var worker in WorkerManager.LabourersHere)
                {
                    if (worker == null || worker.HasDisposed || worker.Skills?.Skills == null) continue;
                    foreach (var skill in worker.Skills.Skills)
                    {
                        if (skill == null) continue;
                        skill.SetGoalPreferenceLevel(2);
                        skill.ResetExperienceAddedToday();
                    }
                    count++;
                }
                SetStatus(L(
                    $"Double Passion (2 Stars) granted to all skills for {count} settlers!",
                    $"{count} köylünün tüm yeteneklerine Çift Yıldız (Tutku) verildi!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Passion error: ", "Tutku verme hatası: ") + ex.Message, ex);
            }
        }

        private void SpawnNewSettlerNow()
        {
            try
            {
                if (!IsInActiveGameplay() || !MonoSingleton<WorkerGenerator>.IsInstantiated() || !MonoSingleton<WorkerController>.IsInstantiated())
                {
                    SetStatus(L("No active settlement found!", "Aktif köy bulunamadı!"));
                    return;
                }

                Vector3 spawnPos = Vector3.zero;
                var firstWorker = WorkerManager.LabourersHere.FirstOrDefault(w => w != null && !w.HasDisposed);
                if (firstWorker != null)
                {
                    spawnPos = firstWorker.GetPosition() + new Vector3(1f, 0f, 1f);
                }

                var newHumanoid = MonoSingleton<WorkerGenerator>.Instance.GenerateWorker(null);
                if (newHumanoid != null)
                {
                    newHumanoid.UpdatePosition(spawnPos);
                    MonoSingleton<WorkerController>.Instance.CreateWorker(newHumanoid);
                    string name = newHumanoid.Info != null ? newHumanoid.Info.GetFullName() : L("New Settler", "Yeni Köylü");
                    SetStatus(L($"New settler ({name}) joined your settlement!", $"Yeni köylü ({name}) köyünüze katıldı!"));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Settler spawn error: ", "Köylü oluşturma hatası: ") + ex.Message, ex);
            }
        }

        private void ToggleAllFastBuildCheats()
        {
            try
            {
                bool targetState = true;
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    var bpm = MonoSingleton<BuildingPlacementManager>.Instance;
                    targetState = !bpm.Autoconstruct;
                    bpm.Autoconstruct = targetState;
                    bpm.ConstructWithoutResource = targetState;
                    bpm.VariantsUnlocked = targetState;
                }
                if (MonoSingleton<GroundManager>.IsInstantiated())
                {
                    MonoSingleton<GroundManager>.Instance.InstantDig = targetState;
                }
                if (MonoSingleton<PlantResourceManager>.IsInstantiated())
                {
                    MonoSingleton<PlantResourceManager>.Instance.InstantCut = targetState;
                    MonoSingleton<PlantResourceManager>.Instance.ForceHarvestPhase = targetState;
                }
                SetStatus(L("Instant Build + Free Build + Instant Dig/Cut: ", "Hızlı İnşaat + Kaynaksız İnşa + Anında Kazı/Kesim: ") + OnOff(targetState));
            }
            catch (Exception ex)
            {
                SetStatus(L("Fast build error: ", "Hızlı inşaat hatası: ") + ex.Message, ex);
            }
        }

        private void FinishAllBlueprintsNow()
        {
            try
            {
                if (MonoSingleton<DeveloperConsoleController>.IsInstantiated())
                {
                    MonoSingleton<DeveloperConsoleController>.Instance.ParseCommand("finishAllBluerpints");
                    SetStatus(L("Completing all construction blueprints on the map!", "Haritadaki tüm inşaat planları (Blueprints) tamamlanıyor!"));
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Blueprint finish error: ", "Plan tamamlama hatası: ") + ex.Message, ex);
            }
        }

        private void UnlockAllResearchNow()
        {
            try
            {
                if (MonoSingleton<ResearchController>.IsInstantiated())
                {
                    MonoSingleton<ResearchController>.Instance.ActivateAllResearch();
                    SetStatus(L("All research & technologies unlocked!", "Tüm araştırmalar ve teknolojiler açıldı!"));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Research unlock error: ", "Araştırma açma hatası: ") + ex.Message, ex);
            }
        }

        private void KeepAllResourcesFreshAndHealthy()
        {
            try
            {
                if (!MonoSingleton<ResourcePileManager>.IsInstantiated()) return;
                var piles = MonoSingleton<ResourcePileManager>.Instance.AllPileInstances;
                if (piles == null) return;

                foreach (var pile in piles)
                {
                    if (pile == null || pile.HasDisposed) continue;
                    var res = pile.GetStoredResource();
                    if (res != null && !res.HasDisposed && res.Stats != null)
                    {
                        MaxStatStatic(res.Stats, StatType.Freshness);
                        MaxStatStatic(res.Stats, StatType.Health);
                    }
                    if (pile.Stats != null)
                    {
                        MaxStatStatic(pile.Stats, StatType.Freshness);
                        MaxStatStatic(pile.Stats, StatType.Health);
                    }
                }
            }
            catch { }
        }

        private void RefillAllStockpilesAndFreshness()
        {
            try
            {
                if (!MonoSingleton<ResourcePileManager>.IsInstantiated())
                {
                    SetStatus(L("Map not loaded!", "Harita yüklü değil!"));
                    return;
                }

                var piles = MonoSingleton<ResourcePileManager>.Instance.AllPileInstances.ToList();
                int count = 0;
                foreach (var pile in piles)
                {
                    if (pile == null || pile.HasDisposed || pile.Blueprint == null) continue;
                    var stored = pile.GetStoredResource();
                    if (stored == null || stored.HasDisposed || !stored.OwnedByPlayer()) continue;

                    int limit = pile.Blueprint.StackingLimit;
                    if (limit > 1 && stored.Amount < limit)
                    {
                        int needed = limit - stored.Amount;
                        var extra = new ResourceInstance(pile.Blueprint, needed);
                        var storage = pile.GetStorage();
                        if (storage != null)
                        {
                            extra.TransferTo(storage, -1);
                            count++;
                        }
                    }

                    if (stored.Stats != null)
                    {
                        MaxStatStatic(stored.Stats, StatType.Freshness);
                        MaxStatStatic(stored.Stats, StatType.Health);
                    }
                    if (pile.Stats != null)
                    {
                        MaxStatStatic(pile.Stats, StatType.Freshness);
                        MaxStatStatic(pile.Stats, StatType.Health);
                    }
                }

                if (MonoSingleton<ResourcePileTracker>.IsInstantiated())
                {
                    MonoSingleton<ResourcePileTracker>.Instance.ScheduleRecountPiles();
                }

                SetStatus(L(
                    $"{count} stockpiles refilled to max stack and all resources restored to 100% freshness!",
                    $"{count} kaynak yığını maksimum limite dolduruldu ve tüm erzakların tazeliği %100 yapıldı!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Stockpile refill error: ", "Depo doldurma hatası: ") + ex.Message, ex);
            }
        }

        private void EnsureResourcesCached()
        {
            if (_cachedResources != null && _cachedResources.Count > 0) return;
            try
            {
                if (Repository<ResourceRepository, Resource>.IsInstantiated())
                {
                    _cachedResources = Repository<ResourceRepository, Resource>.Instance
                        .GetAllItems()
                        .Where(r => r != null && !r.HideInGame && !string.IsNullOrEmpty(r.GetID()))
                        .OrderBy(r => r.GetID())
                        .ToList();
                }
            }
            catch { }
        }

        private void SpawnResourceById(string resourceId, int amount)
        {
            try
            {
                if (!IsInActiveGameplay() || !Repository<ResourceRepository, Resource>.IsInstantiated() || !MonoSingleton<ResourcePileManager>.IsInstantiated())
                {
                    SetStatus(L("Active map or ResourcePileManager not found!", "Aktif harita veya ResourcePileManager bulunamadı!"));
                    return;
                }

                var blueprint = Repository<ResourceRepository, Resource>.Instance.GetByID(resourceId);
                if (blueprint == null)
                {
                    SetStatus(L($"Resource not found: {resourceId}", $"Kaynak bulunamadı: {resourceId}"));
                    return;
                }

                var worker = WorkerManager.LabourersHere.FirstOrDefault(w => w != null && !w.HasDisposed);
                if (worker == null)
                {
                    SetStatus(L("No active settler found on the map!", "Haritada aktif köylü bulunamadı!"));
                    return;
                }

                Vector3 pos = worker.GetPosition();
                var rpm = MonoSingleton<ResourcePileManager>.Instance;

                if (!string.IsNullOrEmpty(blueprint.BuildingBlueprintID))
                {
                    rpm.SpawnPile(blueprint, pos + Vector3.up * 0.5f, blueprint.GetID());
                }
                else
                {
                    var resInstance = new ResourceInstance(blueprint, amount);
                    resInstance.SetProducerUniqueId(worker.UniqueId);
                    rpm.SpawnPile(resInstance, pos, false, 0f);
                }

                if (MonoSingleton<ResourcePileTracker>.IsInstantiated())
                {
                    MonoSingleton<ResourcePileTracker>.Instance.ScheduleRecountPiles();
                }

                string workerName = worker.Info != null ? worker.Info.GetFullName() : L("settler", "köylü");
                SetStatus(L(
                    $"+{amount} [{resourceId}] spawned next to {workerName}!",
                    $"+{amount} adet [{resourceId}] ({workerName} yanına) başarıyla oluşturuldu!"));
            }
            catch (Exception ex)
            {
                SetStatus(L($"Resource spawn error ({resourceId}): {ex.Message}", $"Kaynak oluşturma hatası ({resourceId}): {ex.Message}"), ex);
            }
        }

        private void KillAllEnemiesNow()
        {
            try
            {
                if (MonoSingleton<DeveloperConsoleController>.IsInstantiated())
                {
                    MonoSingleton<DeveloperConsoleController>.Instance.ParseCommand("killEnemies");
                    SetStatus(L("All enemies on the map have been eliminated!", "Haritadaki tüm düşmanlar öldürüldü!"));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Kill enemies error: ", "Düşman öldürme hatası: ") + ex.Message, ex);
            }
        }

        private void RecruitAllPrisonersNow()
        {
            try
            {
                if (!MonoSingleton<NPCManager>.IsInstantiated()) return;
                int count = 0;
                var npcs = MonoSingleton<NPCManager>.Instance.IterateNPCs(x => x != null && !x.HasDisposed).ToList();
                foreach (var npc in npcs)
                {
                    if (npc.Stats != null)
                    {
                        var st = npc.Stats.GetStat(StatType.PrisonerNotRecruited);
                        if (st != null)
                        {
                            st.SetCurrent(0f);
                            npc.Stats.Update();
                            count++;
                        }
                    }
                }
                SetStatus(L(
                    $"{count} prisoners 100% convinced (ready to join settlement)!",
                    $"{count} tutsak %100 ikna edildi (köye katılmaya hazır)!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Prisoner recruit error: ", "Tutsak ikna hatası: ") + ex.Message, ex);
            }
        }

        private void CompleteAllAnimalProductionNow()
        {
            try
            {
                if (!MonoSingleton<AnimalManager>.IsInstantiated()) return;
                int count = 0;
                foreach (var kvp in MonoSingleton<AnimalManager>.Instance.Animals)
                {
                    var animal = kvp.Key;
                    if (animal == null || animal.HasDisposed) continue;
                    animal.CompleteAnimalProduction();
                    count++;
                }
                SetStatus(L(
                    $"Production (milk/wool/eggs) completed for {count} animals!",
                    $"{count} hayvanın üretimi (süt/yün/yumurta) tamamlandı!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Animal production error: ", "Hayvan üretim hatası: ") + ex.Message, ex);
            }
        }

        private void TameAndTrainAllAnimalsNow()
        {
            try
            {
                if (!MonoSingleton<AnimalManager>.IsInstantiated()) return;
                int count = 0;
                var firstWorker = IsInActiveGameplay() ? WorkerManager.LabourersHere.FirstOrDefault() : null;
                foreach (var kvp in MonoSingleton<AnimalManager>.Instance.Animals)
                {
                    var animal = kvp.Key;
                    if (animal == null || animal.HasDisposed) continue;
                    if (animal.AnimalType == AnimalType.Wild || animal.AnimalType == AnimalType.WildAggressive)
                    {
                        animal.SetAnimalType(AnimalType.Domestic);
                    }
                    if (animal.Stats != null)
                    {
                        MinStatStatic(animal.Stats, StatType.AnimalWild);
                        MinStatStatic(animal.Stats, StatType.AnimalUntrained);
                        MaxStatStatic(animal.Stats, StatType.Health);
                        MaxStatStatic(animal.Stats, StatType.Hunger);
                    }
                    if (firstWorker != null && animal.PetOwner == null)
                    {
                        try { animal.SetAnimalType(AnimalType.Pet); } catch { }
                    }
                    count++;
                }
                SetStatus(L(
                    $"{count} animals tamed, trained, and fed!",
                    $"{count} hayvan evcilleştirildi, eğitildi ve doyuruldu!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Animal taming error: ", "Hayvan evcilleştirme hatası: ") + ex.Message, ex);
            }
        }

        private void SpawnCustomAnimalsNow(string animalId, string displayLabel)
        {
            try
            {
                if (!IsInActiveGameplay() || !MonoSingleton<AnimalManager>.IsInstantiated())
                {
                    SetStatus(L("Active map or AnimalManager not found!", "Aktif harita veya AnimalManager bulunamadı!"));
                    return;
                }

                var worker = WorkerManager.LabourersHere.FirstOrDefault(w => w != null && !w.HasDisposed);
                if (worker == null)
                {
                    SetStatus(L("No active settler found on the map!", "Haritada aktif köylü bulunamadı!"));
                    return;
                }

                Vector3 basePos = worker.GetPosition();
                var am = MonoSingleton<AnimalManager>.Instance;
                int lifePhaseIndex = _animalSpawnMature ? 1 : 0;
                int spawnedCount = 0;

                for (int i = 0; i < _animalSpawnCount; i++)
                {
                    BodyType bodyType;
                    if (_animalSpawnSex == 1) bodyType = BodyType.Male;
                    else if (_animalSpawnSex == 2) bodyType = BodyType.Female;
                    else bodyType = (UnityEngine.Random.value < 0.5f) ? BodyType.Male : BodyType.Female;

                    Vector3 offset = new Vector3((i % 3) * 0.6f, 0f, (i / 3) * 0.6f);
                    var animal = am.SpawnAnimal(animalId, basePos + offset, bodyType, lifePhaseIndex, 0f, false, AnimalType.Wild, null, null, null, "", 0, 0);
                    if (animal == null) continue;

                    if (_animalSpawnMode == 1) // Ehli / Çiftlik (Domestic)
                    {
                        if (animal.Stats != null)
                        {
                            MinStatStatic(animal.Stats, StatType.AnimalWild);
                            MaxStatStatic(animal.Stats, StatType.Health);
                            MaxStatStatic(animal.Stats, StatType.Hunger);
                        }
                        animal.SetAnimalType(AnimalType.Domestic);
                        if (animal.HasHarvestableProduction() && MonoSingleton<AnimalController>.IsInstantiated())
                        {
                            MonoSingleton<AnimalController>.Instance.MarkForOrder(AnimalOrderType.Harvest, animal);
                        }
                    }
                    else if (_animalSpawnMode == 2) // Evcil / Eğitimli (Pet)
                    {
                        if (animal.Stats != null)
                        {
                            MinStatStatic(animal.Stats, StatType.AnimalWild);
                            MaxStatStatic(animal.Stats, StatType.Health);
                            MaxStatStatic(animal.Stats, StatType.Hunger);
                        }
                        animal.SetAnimalType(AnimalType.Domestic);
                        if (animal.Stats != null)
                        {
                            MinStatStatic(animal.Stats, StatType.AnimalUntrained);
                        }
                        animal.SetAnimalType(AnimalType.Pet);
                        if (animal.Blueprint != null && animal.Blueprint.CanHaulAsPet)
                        {
                            animal.PetHaulEnabled = true;
                        }
                        if (animal.HasHarvestableProduction() && MonoSingleton<AnimalController>.IsInstantiated())
                        {
                            MonoSingleton<AnimalController>.Instance.MarkForOrder(AnimalOrderType.Harvest, animal);
                        }
                    }

                    StartCoroutine(StartAnimalGoapDelayed(animal));
                    spawnedCount++;
                }

                string modeStr = _animalSpawnMode == 0
                    ? L("Wild", "Yabani (Wild)")
                    : (_animalSpawnMode == 1 ? L("Domestic", "Ehli (Domestic)") : L("Trained Pet", "Evcil (Pet)"));
                string workerName = worker.Info != null ? worker.Info.GetFullName() : L("settler", "köylü");
                SetStatus(L(
                    $"+{spawnedCount} {displayLabel} [{modeStr}] spawned next to {workerName}!",
                    $"+{spawnedCount} adet {displayLabel} [{modeStr}] ({workerName} yanına) oluşturuldu!"));
            }
            catch (Exception ex)
            {
                SetStatus(L($"Animal spawn error ({animalId}): {ex.Message}", $"Hayvan oluşturma hatası ({animalId}): {ex.Message}"), ex);
            }
        }

        private IEnumerator StartAnimalGoapDelayed(AnimalInstance animal)
        {
            yield return new WaitForSeconds(0.5f);
            try
            {
                if (animal != null && !animal.HasDisposed)
                {
                    var agent = animal.GetGoapAgent();
                    if (agent != null)
                    {
                        agent.StartTicker();
                    }
                }
            }
            catch { }
        }

        private void SetGameSpeed(float scale)
        {
            try
            {
                Time.timeScale = scale;
                SetStatus(L($"Game Speed (TimeScale) set to {scale:0.#}x.", $"Oyun Hızı (TimeScale): {scale:0.#}x olarak ayarlandı."));
            }
            catch { }
        }

        private void SetTimeOfDay(float percent)
        {
            try
            {
                if (MonoSingleton<WeatherManager>.IsInstantiated())
                {
                    MonoSingleton<WeatherManager>.Instance.DebugSetDayTime(percent);
                    SetStatus(L($"Time of day set to {percent * 100f:0}%.", $"Günün saati %{percent * 100f:0} noktasına ayarlandı."));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Time change error: ", "Saat değiştirme hatası: ") + ex.Message, ex);
            }
        }

        private void SetSeason(int seasonIndex)
        {
            try
            {
                if (MonoSingleton<WeatherManager>.IsInstantiated())
                {
                    MonoSingleton<WeatherManager>.Instance.DebugSetSeasonTime(seasonIndex, 0.1f);
                    string[] namesEN = { "Spring", "Summer", "Autumn", "Winter" };
                    string[] namesTR = { "İlkbahar", "Yaz", "Sonbahar", "Kış" };
                    SetStatus(L(
                        $"Season changed to [{namesEN[seasonIndex]}]!",
                        $"Mevsim [{namesTR[seasonIndex]}] olarak değiştirildi!"));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("Season change error: ", "Mevsim değiştirme hatası: ") + ex.Message, ex);
            }
        }

        private int ClearAllActiveWeatherInternal(WeatherManager wm)
        {
            int endedCount = 0;
            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                // 1) WeatherManager.eventsRunning içindeki aktif yağmur/sis/kar olaylarını ForceEnd ile bitir
                FieldInfo fiRunning = typeof(WeatherManager).GetField("eventsRunning", flags);
                if (fiRunning != null)
                {
                    var runningList = fiRunning.GetValue(wm) as IEnumerable;
                    if (runningList != null)
                    {
                        var snapshot = runningList.Cast<object>().Where(x => x != null).ToList();
                        foreach (var evObj in snapshot)
                        {
                            try
                            {
                                MethodInfo miForceEnd = evObj.GetType().GetMethod("ForceEnd", flags);
                                miForceEnd?.Invoke(evObj, null);
                                endedCount++;
                            }
                            catch { }
                        }
                    }
                    try
                    {
                        var listObj = fiRunning.GetValue(wm) as IList;
                        listObj?.Clear();
                    }
                    catch { }
                }

                // 2) WeatherManager.scheduledEvents içinde şu anki dakikayı kapsayan tüm zamanlanmış hava olaylarını da bitir
                FieldInfo fiScheduled = typeof(WeatherManager).GetField("scheduledEvents", flags);
                var dateObj = GlobalSaveController.CurrentVillageData?.DateAndTime;
                if (fiScheduled != null && dateObj != null)
                {
                    long nowMin = dateObj.MinutesTotal;
                    var schedList = fiScheduled.GetValue(wm) as IEnumerable;
                    if (schedList != null)
                    {
                        var snapshot = schedList.Cast<object>().Where(x => x != null).ToList();
                        foreach (var evObj in snapshot)
                        {
                            try
                            {
                                PropertyInfo piStart = evObj.GetType().GetProperty("StartTime", flags);
                                PropertyInfo piEnd = evObj.GetType().GetProperty("EndTime", flags);
                                long start = piStart != null ? Convert.ToInt64(piStart.GetValue(evObj, null)) : 0L;
                                long end = piEnd != null ? Convert.ToInt64(piEnd.GetValue(evObj, null)) : 0L;
                                if (nowMin >= start - 60 && nowMin <= end + 60)
                                {
                                    MethodInfo miForceEnd = evObj.GetType().GetMethod("ForceEnd", flags);
                                    miForceEnd?.Invoke(evObj, null);
                                    endedCount++;
                                }
                            }
                            catch { }
                        }
                    }
                }

                // 3) Eğer GameEventSystem üzerinden çalışan Fırtına (thunderstorm), Dolu (hailstorm), Sıcak Dalga vb. varsa onları da bitir
                if (MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.IsInstantiated())
                {
                    var ges = MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.Instance;
                    if (ges.RunningEvents != null)
                    {
                        var runningGameEvents = ges.RunningEvents.ToList();
                        foreach (var ge in runningGameEvents)
                        {
                            if (ge == null || ge.Blueprint == null) continue;
                            string geId = ge.Blueprint.GetID() ?? "";
                            if (ge is NSMedieval.GameEventSystem.Events.AlterWeatherEvent ||
                                geId.Contains("thunderstorm") || geId.Contains("hailstorm") ||
                                geId.Contains("heat_wave") || geId.Contains("cold_snap"))
                            {
                                try
                                {
                                    ge.ForceEnd();
                                    endedCount++;
                                }
                                catch { }
                            }
                        }
                    }
                }

                // 4) Animasyon katmanlarını, efekt ağırlıklarını ve Shader anahtarlarını sıfırla
                try
                {
                    FieldInfo fiIsRunning = typeof(WeatherManager).GetField("isEventRunning", flags);
                    (fiIsRunning?.GetValue(wm) as IList)?.Clear();
                }
                catch { }

                try { wm.SetAnimationLayerWeight("rainDayNightAnim", 0f); } catch { }
                try { wm.SetAnimationLayerWeight("fogAnim", 0f); } catch { }
                try { wm.SetAnimationLayerWeight("snowAnim", 0f); } catch { }
                try { wm.SetAnimationLayerWeight("thunderstormAnim", 0f); } catch { }
                try { wm.SetAnimationLayerWeight("hailstormAnim", 0f); } catch { }

                if (wm.Overrides != null)
                {
                    wm.Overrides.IsActive = false;
                    wm.Overrides.SetRainEffectWeight(0f);
                }

                try
                {
                    MethodInfo miReset = typeof(WeatherManager).GetMethod("ResetShaderKeywords", flags);
                    miReset?.Invoke(wm, null);
                }
                catch { }
            }
            catch { }
            return endedCount;
        }

        private void SetWeather(string weatherEvent)
        {
            try
            {
                if (!MonoSingleton<WeatherManager>.IsInstantiated())
                {
                    SetStatus(L("WeatherManager is not active (enter a map first).", "WeatherManager aktif değil (önce haritaya girin)."));
                    return;
                }

                var wm = MonoSingleton<WeatherManager>.Instance;

                if (weatherEvent == "clear")
                {
                    int ended = ClearAllActiveWeatherInternal(wm);
                    SetStatus(L(
                        $"Weather set to [Clear / Sunny]! ({ended} active weather/storm events terminated)",
                        $"Hava [Açık / Güneşli] yapıldı! ({ended} aktif yağış/fırtına olayı sonlandırıldı)"));
                    return;
                }

                if (weatherEvent == "thunderstorm")
                {
                    ClearAllActiveWeatherInternal(wm);
                    if (MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.IsInstantiated())
                    {
                        MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.Instance.StartEvent("game_event_thunderstorm");
                        SetStatus(L(
                            "Weather [⛈ Thunderstorm (game_event_thunderstorm)] started!",
                            "Hava durumu [⛈ Gök Gürültülü Fırtına (game_event_thunderstorm)] olarak başlatıldı!"));
                    }
                    else
                    {
                        SetStatus(L("GameEventSystem not found!", "GameEventSystem bulunamadı!"));
                    }
                    return;
                }

                ClearAllActiveWeatherInternal(wm);
                var dateObj = GlobalSaveController.CurrentVillageData?.DateAndTime;
                MethodInfo miForceStart = typeof(WeatherManager).GetMethod("ForceStartEvent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (dateObj != null && miForceStart != null)
                {
                    long minInHour = dateObj.MinutesInHour > 0 ? dateObj.MinutesInHour : 60L;
                    long startMin = dateObj.MinutesTotal - (minInHour * 2L);
                    long durationMin = minInHour * 8L;
                    miForceStart.Invoke(wm, new object[] { weatherEvent, startMin, durationMin, true });
                }
                else
                {
                    wm.DebugForceWeatherEvent(weatherEvent);
                }

                SetStatus(L(
                    $"Weather [{weatherEvent}] started immediately at 100% intensity!",
                    $"Hava durumu [{weatherEvent}] anında %100 yoğunlukla başlatıldı!"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Weather error: ", "Hava durumu hatası: ") + ex.Message, ex);
            }
        }

        private void ToggleNativeDevTools()
        {
            try
            {
                if (MonoSingleton<DeveloperToolsView>.IsInstantiated())
                {
                    var dtv = MonoSingleton<DeveloperToolsView>.Instance;
                    FieldInfo fi = typeof(DeveloperToolsView).GetField("devToolsActive", BindingFlags.Instance | BindingFlags.NonPublic);
                    bool isActive = fi != null && (bool)fi.GetValue(dtv);
                    if (isActive)
                    {
                        dtv.CloseDevToolsPanel();
                        SetStatus(L("Native Developer Panel closed.", "Orijinal Geliştirici Paneli kapatıldı."));
                    }
                    else
                    {
                        dtv.Open();
                        SetStatus(L("Native Developer Panel (DeveloperToolsView) opened!", "Orijinal Geliştirici Paneli (DeveloperToolsView) açıldı!"));
                    }
                }
                else
                {
                    SetStatus(L(
                        "DeveloperToolsView is not active in the scene yet (enter a map first).",
                        "DeveloperToolsView henüz sahnede aktif değil (önce haritaya girin)."));
                }
            }
            catch (Exception ex)
            {
                SetStatus(L("DevTools error: ", "DevTools hatası: ") + ex.Message, ex);
            }
        }

        private void ExecuteConsoleCommand(string rawInput)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawInput)) return;
                if (!MonoSingleton<DeveloperConsoleController>.IsInstantiated())
                {
                    SetStatus(L(
                        "Console controller is not ready yet (enter a map first).",
                        "Konsol kontrolcüsü henüz hazır değil (önce haritaya girin)."));
                    return;
                }

                string[] parts = rawInput.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0];
                var dcc = MonoSingleton<DeveloperConsoleController>.Instance;

                if (parts.Length == 1)
                {
                    dcc.ParseCommand(cmd);
                }
                else
                {
                    string[] args = parts.Skip(1).ToArray();
                    dcc.ParseCommand(cmd, args);
                }
                SetStatus(L($"Command executed: {rawInput}", $"Komut çalıştırıldı: {rawInput}"));
            }
            catch (Exception ex)
            {
                SetStatus(L("Command error: ", "Komut hatası: ") + ex.Message, ex);
            }
        }
    }
}
