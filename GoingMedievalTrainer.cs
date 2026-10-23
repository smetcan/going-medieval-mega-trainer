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
    [BepInPlugin("com.selcuk.goingmedieval.megatrainer", "Going Medieval Mega Trainer (TR)", "1.4.0")]
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
                Log.LogInfo("Harmony yamaları başarıyla uygulandı.");
            }
            catch (Exception ex)
            {
                Log.LogError("Harmony yama hatası: " + ex);
            }

            TrainerHarmonyPatches.EnsureTrainerHost();
            Log.LogInfo("Going Medieval Mega Trainer (TR) v1.4.0 yüklendi! [F1] Menü | [F9] Hot-Reload");
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

        // Pencere ve UI Durumu
        private bool _showMenu = false;
        private bool _showMiniBadge = true;
        private Rect _windowRect = new Rect(40f, 60f, 740f, 660f);
        private int _selectedTab = 0;
        private readonly string[] _tabs = new string[]
        {
            "Köylüler",
            "İnşaat & Üretim",
            "Kaynak & Depo",
            "Savaş & Canlılar",
            "Zaman & Hava",
            "Dev Konsol",
            "📋 Loglar"
        };

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
        private string _statusMessage = "Mega Trainer v1.4.0 Hazır! [F1] Menü | [F9] Canlı Yenile (Hot-Reload)";
        private float _statusTimer = 0f;

        // Canlı Yenileme (Hot-Reload) Durumu
        private bool _isHotReloadedInstance = false;
        private bool _isReplacedByHotReload = false;
        private bool _autoHotReload = true;
        private float _hotReloadCheckTimer = 0f;
        private DateTime _lastHotReloadWriteUtc = DateTime.MinValue;
        private string _hotReloadDllPath;

        // Tıklama -> Durum Loglama Yapısı
        private string _lastClickedAction = "Başlangıç";
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
                                    $"Going Medieval Mega Trainer v1.4.0 - Oturum Başlangıcı: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                                    $"Format: [Saat] | Tıklanan: <Buton/Kısayol> ==> Durum: <Sonuç>{Environment.NewLine}" +
                                    $"================================================================================{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, header, Encoding.UTF8);

                    // Eğer oyun kapalıyken veya önceki oturumda TrainerHotReload.dll güncellenmişse ve plugins\GoingMedievalTrainer.dll eski kalmışsa, açılışta otomatik yükselt
                    string pluginDllPath = Path.Combine(Paths.PluginPath, "GoingMedievalTrainer.dll");
                    if (!string.IsNullOrEmpty(_hotReloadDllPath) && File.Exists(_hotReloadDllPath) && File.Exists(pluginDllPath))
                    {
                        DateTime hotTime = File.GetLastWriteTimeUtc(_hotReloadDllPath);
                        DateTime plugTime = File.GetLastWriteTimeUtc(pluginDllPath);
                        if (hotTime > plugTime.AddSeconds(2))
                        {
                            _lastClickedAction = "[Otomatik Başlangıç Güncellemesi]";
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
                    _lastClickedAction = "[Kısayol F2] Orijinal Geliştirici Paneli";
                    ToggleNativeDevTools();
                }
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    _lastClickedAction = "[Kısayol F3] Ölümsüzlük (God Mode)";
                    GodModeAllSettlers = !GodModeAllSettlers;
                    if (GodModeAllSettlers) ApplyGodModeAndHealNow();
                    SetStatus("Ölümsüzlük (God Mode): " + (GodModeAllSettlers ? "AÇIK" : "KAPALI"));
                }
                if (Input.GetKeyDown(KeyCode.F4))
                {
                    _lastClickedAction = "[Kısayol F4] Sonsuz İhtiyaçlar & %100 Moral";
                    InfiniteNeedsAllSettlers = !InfiniteNeedsAllSettlers;
                    if (InfiniteNeedsAllSettlers) ApplyInfiniteNeedsNow();
                    SetStatus("Sonsuz İhtiyaçlar & %100 Moral: " + (InfiniteNeedsAllSettlers ? "AÇIK" : "KAPALI"));
                }
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    _lastClickedAction = "[Kısayol F5] Tüm Hızlı İnşa/Kazı Hileleri";
                    ToggleAllFastBuildCheats();
                }
                if (Input.GetKeyDown(KeyCode.F6))
                {
                    _lastClickedAction = "[Kısayol F6] Haritadaki Tüm İnşaat Planlarını Bitir";
                    FinishAllBlueprintsNow();
                }
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    _lastClickedAction = "[Kısayol F7] Mevcut Tüm Yığınları Maksimuma Doldur";
                    RefillAllStockpilesAndFreshness();
                }
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    _lastClickedAction = "[Kısayol F8] Haritadaki Tüm Düşmanları Öldür";
                    KillAllEnemiesNow();
                }
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    _lastClickedAction = "[Kısayol F9] Canlı Yenile (Hot-Reload)";
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
                                _lastClickedAction = "[Otomatik Hot-Reload Algılandı]";
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
                MegaTrainerPlugin.Log?.LogWarning("Trainer Update Hatası: " + ex.Message);
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
                        ? "Global Tezgah Üretim Hızı 1x (Normal) yapıldı."
                        : $"Global Tezgah Üretim Hızı {speed:0}x yapıldı!");
                }
                else
                {
                    SetStatus($"Üretim Hızı {speed:0}x olarak ayarlandı (haritaya girince aktif olacak).");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Üretim hızı hatası: " + ex.Message, ex);
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
            if (_selectedTab >= 0 && _selectedTab < _tabs.Length)
                return _tabs[_selectedTab];
            return "Genel";
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
            string prefix = active ? "[✔ AÇIK] " : "[✘ KAPALI] ";
            GUIStyle style = active ? _activeButtonStyle : _buttonStyle;
            if (GUILayout.Button(prefix + text, style, GUILayout.Height(32f)))
            {
                _lastClickedAction = $"[{CurrentTabName()}] {text} (Önceki: {(active ? "AÇIK" : "KAPALI")})";
                return true;
            }
            return false;
        }

        private void SetStatus(string msg, Exception ex = null)
        {
            _statusMessage = msg;
            _statusTimer = 8f;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp}] | Tıklanan: {_lastClickedAction} ==> Durum: {msg}";
            if (ex != null)
            {
                logLine += $" | Hata Detayı: {ex.GetType().Name}: {ex.Message} ({ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()})";
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
                if (GUI.Button(new Rect(12f, 12f, 195f, 28f), "⚔ Mega Trainer v1.4.0 [F1]", _buttonStyle))
                {
                    _showMenu = true;
                }
            }

            if (!_showMenu) return;

            _windowRect = GUILayout.Window(987654, _windowRect, DrawTrainerWindow, "⚔ GOING MEDIEVAL - MEGA TRAINER v1.4.0 (TR) [Hot-Reload Aktif] ⚔", _windowStyle);
        }

        private void DrawTrainerWindow(int windowID)
        {
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Kısayollar: [F1] Menü | [F2] DevPanel | [F3] Ölümsüzlük | [F4] İhtiyaçlar | [F5] Hızlı İnşa | [F6] PlanBitir | [F7] Depo | [F8] Öldür | [F9] Yenile", _labelStyle);
            if (GUILayout.Button("🔄 Yenile [F9]", _activeButtonStyle, GUILayout.Width(105f), GUILayout.Height(24f)))
            {
                _lastClickedAction = "[Üst Bar] 🔄 Yenile [F9]";
                HotReloadTrainerNow(false);
                GUIUtility.ExitGUI();
                return;
            }
            if (GUILayout.Button("✕", _buttonStyle, GUILayout.Width(30f), GUILayout.Height(24f)))
            {
                _showMenu = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            for (int i = 0; i < _tabs.Length; i++)
            {
                GUIStyle st = (_selectedTab == i) ? _activeTabButtonStyle : _tabButtonStyle;
                if (GUILayout.Button(_tabs[i], st, GUILayout.Height(32f)))
                {
                    _selectedTab = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            if (!IsInActiveGameplay() && _selectedTab != 5 && _selectedTab != 6)
            {
                GUILayout.BeginVertical(_sectionStyle);
                GUILayout.Label("⚠ Şu anda aktif bir köy/harita yüklü değil. Lütfen bir kayıt (save) yükleyin veya yeni oyun başlatın.", _headerStyle);
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
            GUILayout.Label("Durum: " + _statusMessage, _statusStyle);
            if (GUILayout.Button(_autoHotReload ? "⚡ Oto-Yenile: AÇIK" : "⚡ Oto-Yenile: KAPALI", _autoHotReload ? _activeButtonStyle : _buttonStyle, GUILayout.Width(135f)))
            {
                _autoHotReload = !_autoHotReload;
                _lastClickedAction = "[Alt Bar] Oto-Yenile (Auto Hot-Reload)";
                SetStatus("Otomatik Canlı Yenileme (Auto Hot-Reload): " + (_autoHotReload ? "AÇIK" : "KAPALI"));
            }
            if (GUILayout.Button("📋 Loglar (" + _actionStatusLogs.Count + ")", _buttonStyle, GUILayout.Width(105f)))
            {
                _selectedTab = 6;
            }
            if (GUILayout.Button(_showMiniBadge ? "Rozeti Gizle" : "Rozeti Göster", _buttonStyle, GUILayout.Width(100f)))
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

            if (logs != null && logs.Count > 0)
            {
                _actionStatusLogs.Clear();
                _actionStatusLogs.AddRange(logs);
            }

            _lastClickedAction = isAutoTrigger ? "[Otomatik Canlı Yenileme]" : "[F9 / Canlı Yenile Butonu]";
            SetStatus($"Canlı Yenileme (Hot-Reload) tamamlandı! [Sürüm v1.4.0 - {DateTime.Now:HH:mm:ss}]");
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
                    SetStatus("TrainerHotReload.dll bulunamadı: " + _hotReloadDllPath);
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
                    SetStatus("Hot-Reload hatası: Yeni derlemede MegaTrainerBehaviour sınıfı bulunamadı!");
                    return;
                }

                // Log kaynağını yeni assembly'ye aktar
                if (newPluginType != null)
                {
                    FieldInfo fiLog = newPluginType.GetField("Log", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    fiLog?.SetValue(null, MegaTrainerPlugin.Log);
                }

                // Eski Harmony yamalarını kaldır ve yeni derlemenin yamalarını uygula
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
                    MegaTrainerPlugin.Log?.LogWarning("Hot-Reload Harmony yama yenileme uyarısı: " + hex.Message);
                }

                // Mevcut örnek bayraklarını devre dışı bırak ve yeni bileşeni aynı GameObject üzerine ekle
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
                SetStatus("Hot-Reload hatası: " + ex.Message, ex);
            }
        }

        private void DrawSettlersTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🛡 Sürekli Aktif Köylü Korumaları (Aç / Kapat)", _headerStyle);

            GUILayout.BeginHorizontal();
            if (DrawToggleButton("Ölümsüzlük & Otomatik Tedavi [F3]", GodModeAllSettlers))
            {
                GodModeAllSettlers = !GodModeAllSettlers;
                if (GodModeAllSettlers) ApplyGodModeAndHealNow();
                SetStatus("Ölümsüzlük: " + (GodModeAllSettlers ? "AÇIK" : "KAPALI"));
            }
            if (DrawToggleButton("Sonsuz İhtiyaçlar & %100 Moral [F4]", InfiniteNeedsAllSettlers))
            {
                InfiniteNeedsAllSettlers = !InfiniteNeedsAllSettlers;
                if (InfiniteNeedsAllSettlers) ApplyInfiniteNeedsNow();
                SetStatus("Sonsuz İhtiyaçlar & Moral: " + (InfiniteNeedsAllSettlers ? "AÇIK" : "KAPALI"));
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (DrawToggleButton($"Süper Hız & Çalışma Çarpanı ({_settlerSpeedMultiplier:0}x)", _superSpeedSettlers))
            {
                _superSpeedSettlers = !_superSpeedSettlers;
                ApplySettlerSpeedMultiplier(_superSpeedSettlers ? _settlerSpeedMultiplier : 1f);
                SetStatus("Köylü Süper Hız: " + (_superSpeedSettlers ? $"{_settlerSpeedMultiplier:0}x AÇIK" : "Normal (1x)"));
            }
            GUILayout.Label($"Çarpan: {_settlerSpeedMultiplier:0}x", _labelStyle, GUILayout.Width(80f));
            float newMult = GUILayout.HorizontalSlider(_settlerSpeedMultiplier, 1f, 20f, GUILayout.Width(160f));
            if (Math.Abs(newMult - _settlerSpeedMultiplier) > 0.1f)
            {
                _settlerSpeedMultiplier = Mathf.Round(newMult);
                if (_superSpeedSettlers) ApplySettlerSpeedMultiplier(_settlerSpeedMultiplier);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("⚡ Tek Tıkla Toplu Köylü İşlemleri", _headerStyle);

            GUILayout.BeginHorizontal();
            if (TrainerButton("❤ Tümünü Şimdi İyileştir & Doyur"))
            {
                ApplyGodModeAndHealNow();
                ApplyInfiniteNeedsNow();
                SetStatus("Tüm köylüler iyileştirildi, yaraları sarıldı ve ihtiyaçları %100 yapıldı!");
            }
            if (TrainerButton("⭐ Tüm Yetenekleri Maksimum (50) Yap"))
            {
                MaxAllSettlerSkills();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TrainerButton("🔥 Tüm Yeteneklere Çift Yıldız (Tutku) Ver"))
            {
                MaxAllSettlerPassions();
            }
            if (TrainerButton("➕ Köyüme +1 Yeni Köylü Ekle"))
            {
                SpawnNewSettlerNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("👥 Köydeki Köylüler (Bireysel Müdahale)", _headerStyle);
            _settlerScrollPos = GUILayout.BeginScrollView(_settlerScrollPos, GUILayout.Height(210f));
            try
            {
                if (IsInActiveGameplay())
                {
                    var workers = WorkerManager.LabourersHere.ToList();
                    foreach (var w in workers)
                    {
                        if (w == null || w.HasDisposed) continue;
                        string name = w.Info != null ? w.Info.GetFullName() : ("Köylü #" + w.GetUniqueId());
                        float hp = w.GetStat(StatType.Health)?.GetNormalizedPercentage() * 100f ?? 100f;
                        float mood = w.GetStat(StatType.Mood)?.GetNormalizedPercentage() * 100f ?? 100f;

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"• {name}  |  Can: %{hp:0}  |  Moral: %{mood:0}", _labelStyle, GUILayout.Width(350f));
                        if (TrainerButton($"İyileştir & Doyur ({name})", GUILayout.Width(155f)))
                        {
                            HealAndFillSingleSettler(w);
                            SetStatus($"{name} tamamen iyileştirildi ve doyuruldu.");
                        }
                        if (TrainerButton($"Skill 50 ({name})", GUILayout.Width(135f)))
                        {
                            MaxSingleSettlerSkills(w);
                            SetStatus($"{name} tüm yetenekleri 50 yapıldı!");
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
            GUILayout.Label("🏗 İnşaat & Harita Hileleri [F5 ile Toplu Aç/Kapat]", _headerStyle);

            GUILayout.BeginHorizontal();
            if (DrawToggleButton("Anında Otomatik İnşaat (AutoConstruct)", autoConstruct))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    MonoSingleton<BuildingPlacementManager>.Instance.AutoconstructToggle();
                    SetStatus("Anında İnşaat: " + (MonoSingleton<BuildingPlacementManager>.Instance.Autoconstruct ? "AÇIK" : "KAPALI"));
                }
            }
            if (DrawToggleButton("Kaynaksız İnşaat (Malzeme İstemez)", noResourceBuild))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    var bpm = MonoSingleton<BuildingPlacementManager>.Instance;
                    bpm.ConstructWithoutResource = !bpm.ConstructWithoutResource;
                    SetStatus("Kaynaksız İnşaat: " + (bpm.ConstructWithoutResource ? "AÇIK" : "KAPALI"));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawToggleButton("Tüm Bina Varyantlarını Aç", allVariants))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    MonoSingleton<BuildingPlacementManager>.Instance.VariantsUnlockedToggle();
                    SetStatus("Tüm Varyantlar: " + (MonoSingleton<BuildingPlacementManager>.Instance.VariantsUnlocked ? "AÇIK" : "KAPALI"));
                }
            }
            if (DrawToggleButton("Üretilebilir Binaları Doğrudan İnşa Et", craftableBuildings))
            {
                if (MonoSingleton<BuildingPlacementManager>.IsInstantiated())
                {
                    MonoSingleton<BuildingPlacementManager>.Instance.CraftableBuildingsEnabledToggle();
                    SetStatus("Üretilebilir Binalar: " + (MonoSingleton<BuildingPlacementManager>.Instance.CraftableBuildingsEnabled ? "AÇIK" : "KAPALI"));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawToggleButton("Anında Kazı / Madencilik (Instant Dig)", instantDig))
            {
                if (MonoSingleton<GroundManager>.IsInstantiated())
                {
                    var gm = MonoSingleton<GroundManager>.Instance;
                    gm.InstantDig = !gm.InstantDig;
                    SetStatus("Anında Kazı: " + (gm.InstantDig ? "AÇIK" : "KAPALI"));
                }
            }
            if (DrawToggleButton("Anında Ağaç/Bitki Kesimi (Instant Cut)", instantCut))
            {
                if (MonoSingleton<PlantResourceManager>.IsInstantiated())
                {
                    MonoSingleton<PlantResourceManager>.Instance.InstantCutToggle();
                    SetStatus("Anında Kesim: " + (MonoSingleton<PlantResourceManager>.Instance.InstantCut ? "AÇIK" : "KAPALI"));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawToggleButton("Ekinleri Anında Hasat Aşamasına Getir", forceHarvest))
            {
                if (MonoSingleton<PlantResourceManager>.IsInstantiated())
                {
                    MonoSingleton<PlantResourceManager>.Instance.ForceHarvestPhaseToggle();
                    SetStatus("Anında Hasat Evresi: " + (MonoSingleton<PlantResourceManager>.Instance.ForceHarvestPhase ? "AÇIK" : "KAPALI"));
                }
            }
            if (TrainerButton("⚡ Tüm Hızlı İnşa/Kazı Hilelerini Aç [F5]"))
            {
                ToggleAllFastBuildCheats();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🔬 Üretim Hızı, Mavi Planlar (Blueprints) & Araştırma", _headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Global Tezgah Üretim Hızı: {_productionSpeedMultiplier:0}x", _labelStyle, GUILayout.Width(220f));
            float newProd = GUILayout.HorizontalSlider(_productionSpeedMultiplier, 1f, 100f, GUILayout.Width(440f));
            if (Math.Abs(newProd - _productionSpeedMultiplier) > 0.2f)
            {
                float rounded = Mathf.Round(newProd);
                if (Math.Abs(rounded - _productionSpeedMultiplier) > 0.1f)
                {
                    _lastClickedAction = $"[İnşaat & Üretim] Üretim Hızı Slider ({rounded:0}x)";
                    SetGlobalProductionSpeed(rounded);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hızlı Seçim:", _labelStyle, GUILayout.Width(85f));
            float[] prodPresets = new float[] { 1f, 2f, 5f, 10f, 15f, 20f, 30f, 50f, 100f };
            foreach (float preset in prodPresets)
            {
                bool isCurrent = Math.Abs(_productionSpeedMultiplier - preset) < 0.1f;
                string btnLabel = preset == 1f ? "1x (Normal)" : $"{preset:0}x";
                GUIStyle st = isCurrent ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(btnLabel, st, GUILayout.Height(28f)))
                {
                    _lastClickedAction = $"[İnşaat & Üretim] Tezgah Hızı Hızlı Seçim ({btnLabel})";
                    SetGlobalProductionSpeed(preset);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (TrainerButton("🏛 Haritadaki Tüm İnşaat Planlarını Bitir [F6]", GUILayout.Height(36f)))
            {
                FinishAllBlueprintsNow();
            }
            if (TrainerButton("📚 Tüm Araştırmaları (Teknolojileri) Aç", GUILayout.Height(36f)))
            {
                UnlockAllResearchNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawResourcesTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("📦 Depo & Mevcut Yığın (Stockpile) Hileleri", _headerStyle);

            GUILayout.BeginHorizontal();
            if (DrawToggleButton("Çürüme Yok / Sürekli %100 Tazelik & Dayanıklılık", _noRotInfiniteFreshness))
            {
                _noRotInfiniteFreshness = !_noRotInfiniteFreshness;
                if (_noRotInfiniteFreshness) KeepAllResourcesFreshAndHealthy();
                SetStatus("Çürüme Yok (Sonsuz Tazelik): " + (_noRotInfiniteFreshness ? "AÇIK" : "KAPALI"));
            }
            if (TrainerButton("💎 Mevcut Tüm Yığınları Maksimuma Doldur [F7]"))
            {
                RefillAllStockpilesAndFreshness();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🎁 Kaynak & Eşya Oluşturucu (Köylünün Yanına Spawn Eder)", _headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Miktar:", _labelStyle, GUILayout.Width(55f));
            foreach (int amt in new int[] { 25, 50, 100, 250, 500 })
            {
                GUIStyle st = (_spawnAmount == amt) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(amt.ToString(), st, GUILayout.Width(55f)))
                {
                    _spawnAmount = amt;
                    _lastClickedAction = $"[Kaynak & Depo] Miktar Seçimi: {amt}";
                    SetStatus($"Spawn miktarı {amt} olarak ayarlandı.");
                }
            }
            GUILayout.Space(10f);
            GUILayout.Label("Ara:", _labelStyle, GUILayout.Width(35f));
            _resourceSearchQuery = GUILayout.TextField(_resourceSearchQuery ?? "", GUILayout.Width(180f));
            if (GUILayout.Button("Temizle", _buttonStyle, GUILayout.Width(70f)))
            {
                _resourceSearchQuery = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            DrawQuickSpawnButton("Odun (wood)", "wood");
            DrawQuickSpawnButton("Kireçtaşı (limestone)", "limestone");
            DrawQuickSpawnButton("Kil (clay)", "clay");
            DrawQuickSpawnButton("Demir Külçe (iron_ingot)", "iron_ingot");
            DrawQuickSpawnButton("Çelik Külçe (steel_ingot)", "steel_ingot");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawQuickSpawnButton("Altın Külçe (gold_ingot)", "gold_ingot");
            DrawQuickSpawnButton("Kömür (coal)", "coal");
            DrawQuickSpawnButton("Kireçtaşı Blok", "limestone_block");
            DrawQuickSpawnButton("Kil Tuğla", "clay_brick");
            DrawQuickSpawnButton("Yemek (packaged_meal)", "packaged_meal");
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
                    GUILayout.Label($"[{cat}] {id} (Max Yığın: {res.StackingLimit})", _labelStyle, GUILayout.Width(470f));
                    if (GUILayout.Button($"+{_spawnAmount} Oluştur", _buttonStyle, GUILayout.Width(140f)))
                    {
                        _lastClickedAction = $"[Kaynak & Depo] Listeden +{ _spawnAmount } Oluştur: {id}";
                        SpawnResourceById(id, _spawnAmount);
                    }
                    GUILayout.EndHorizontal();

                    shown++;
                    if (shown >= 120)
                    {
                        GUILayout.Label("... Daha fazla sonuç için yukarıdaki arama kutusunu kullanın ...", _labelStyle);
                        break;
                    }
                }
            }
            else
            {
                GUILayout.Label("Kaynak listesi harita yüklendiğinde otomatik dolacaktır.", _labelStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawQuickSpawnButton(string label, string resourceId)
        {
            if (GUILayout.Button(label, _buttonStyle))
            {
                _lastClickedAction = $"[Kaynak & Depo] Hızlı Spawn Butonu: {label} (id={resourceId}, miktar={_spawnAmount})";
                SpawnResourceById(resourceId, _spawnAmount);
            }
        }

        private static readonly string[][] AnimalCatalog = new string[][]
        {
            new string[] { "cow",        "🐄 İnek (cow)",               "Süt / Et / Taşıma (Pet)" },
            new string[] { "sheep",      "🐑 Koyun (sheep)",            "Yün / Süt / Et" },
            new string[] { "goat",       "🐐 Keçi (goat)",              "Süt / Et / Taşıma (Pet)" },
            new string[] { "donkey",     "🫏 Eşek (donkey)",            "Yük Taşıma (Pet)" },
            new string[] { "chicken",    "🐔 Tavuk (chicken)",          "Yumurta / Et" },
            new string[] { "boar",       "🐗 Yaban Domuzu (boar)",      "Et / Savaş & Taşıma (Pet)" },
            new string[] { "dog",        "🐕 Köpek (dog)",              "Taşıma & Savaş (Pet)" },
            new string[] { "cat",        "🐈 Kedi (cat)",               "Haşere Avcısı (Pet)" },
            new string[] { "bear",       "🐻 Ayı (bear)",               "Güçlü Savaş & Taşıma (Pet)" },
            new string[] { "wolf",       "🐺 Kurt (wolf)",              "Savaş & Taşıma (Pet)" },
            new string[] { "fox",        "🦊 Tilki (fox)",              "Taşıma & Haşere (Pet)" },
            new string[] { "deer",       "🦌 Geyik (deer)",             "Et / Deri / Taşıma (Pet)" },
            new string[] { "hare",       "🐇 Tavşan (hare)",            "Küçük Av / Çiftlik" },
            new string[] { "mallard",    "🦆 Yeşilbaş Ördek (mallard)", "Yumurta / Tüy" },
            new string[] { "pheasant",   "🐦 Sülün (pheasant)",         "Yumurta / Tüy" },
            new string[] { "polecat",    "🦡 Sansar (polecat)",         "Haşere Avcısı" },
            new string[] { "rat",        "🐀 Sıçan (rat)",              "Küçük Hayvan" },
            new string[] { "water_vole", "🐹 Su Sıçanı (water_vole)",   "Küçük Hayvan" }
        };

        private void DrawCombatAndCreaturesTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("⚔ Savaş, Tutsak & Mevcut Hayvan Kontrolleri", _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton("💀 Tüm Düşmanları Öldür [F8]", GUILayout.Height(32f)))
            {
                KillAllEnemiesNow();
            }
            if (TrainerButton("🤝 Tüm Tutsakları %100 İkna Et", GUILayout.Height(32f)))
            {
                RecruitAllPrisonersNow();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TrainerButton("🥛 Hayvan Üretimlerini Tamamla (Süt/Yün/Yumurta)", GUILayout.Height(32f)))
            {
                CompleteAllAnimalProductionNow();
            }
            if (TrainerButton("🐕 Haritadaki Tüm Hayvanları Evcilleştir & Eğit", GUILayout.Height(32f)))
            {
                TameAndTrainAllAnimalsNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🐾 Hayvan Oluşturucu (Yabani / Ehli / Evcil Seçimli - Köylünün Yanına)", _headerStyle);

            // 1. Satır: Tür Seçimi (Yabani / Ehli / Evcil)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Durum:", _labelStyle, GUILayout.Width(55f));
            string[] modeNames = new string[]
            {
                "🐺 Yabani (Wild)",
                "🐄 Ehli / Çiftlik (Domestic)",
                "🐕 Evcil / Eğitimli (Pet)"
            };
            for (int m = 0; m < modeNames.Length; m++)
            {
                GUIStyle st = (_animalSpawnMode == m) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(modeNames[m], st, GUILayout.Height(28f)))
                {
                    _animalSpawnMode = m;
                    _lastClickedAction = $"[Savaş & Canlılar] Hayvan Spawn Modu: {modeNames[m]}";
                    SetStatus($"Hayvan spawn durumu [{modeNames[m]}] olarak seçildi.");
                }
            }
            GUILayout.EndHorizontal();

            // 2. Satır: Cinsiyet, Yaş ve Adet Seçimi
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cinsiyet:", _labelStyle, GUILayout.Width(60f));
            string[] sexNames = new string[] { "🎲 Rastgele", "♂ Erkek", "♀ Dişi" };
            for (int s = 0; s < sexNames.Length; s++)
            {
                GUIStyle st = (_animalSpawnSex == s) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(sexNames[s], st, GUILayout.Width(90f), GUILayout.Height(26f)))
                {
                    _animalSpawnSex = s;
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Evre:", _labelStyle, GUILayout.Width(40f));
            if (GUILayout.Button(_animalSpawnMature ? "Yetişkin" : "Yavru", _activeButtonStyle, GUILayout.Width(75f), GUILayout.Height(26f)))
            {
                _animalSpawnMature = !_animalSpawnMature;
            }

            GUILayout.Space(8f);
            GUILayout.Label("Adet:", _labelStyle, GUILayout.Width(38f));
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
            string activeModeShort = _animalSpawnMode == 0 ? "Yabani" : (_animalSpawnMode == 1 ? "Ehli" : "Evcil Pet");
            _animalScrollPos = GUILayout.BeginScrollView(_animalScrollPos, GUILayout.Height(260f));
            for (int i = 0; i < AnimalCatalog.Length; i++)
            {
                string aId = AnimalCatalog[i][0];
                string aTitle = AnimalCatalog[i][1];
                string aDesc = AnimalCatalog[i][2];

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{aTitle}  —  {aDesc}", _labelStyle, GUILayout.Width(455f));
                if (GUILayout.Button($"+{_animalSpawnCount} {activeModeShort} Oluştur", _buttonStyle, GUILayout.Width(210f), GUILayout.Height(26f)))
                {
                    _lastClickedAction = $"[Savaş & Canlılar] Hayvan Spawn: {aTitle} (Mod={activeModeShort}, Cinsiyet={sexNames[_animalSpawnSex]}, Yetişkin={_animalSpawnMature}, Adet={_animalSpawnCount})";
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
            GUILayout.Label("⏩ Oyun Hızı (TimeScale)", _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton("Duraklat (0x)")) SetGameSpeed(0f);
            if (TrainerButton("Normal (1x)")) SetGameSpeed(1f);
            if (TrainerButton("Hızlı (3x)")) SetGameSpeed(3f);
            if (TrainerButton("Çok Hızlı (5x)")) SetGameSpeed(5f);
            if (TrainerButton("Ultra (10x)")) SetGameSpeed(10f);
            if (TrainerButton("Turbo (20x)")) SetGameSpeed(20f);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("☀ Günün Saati", _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton("🌅 Sabah (06:00)")) SetTimeOfDay(0.25f);
            if (TrainerButton("☀ Öğle (12:00)")) SetTimeOfDay(0.50f);
            if (TrainerButton("🌇 Akşam (18:00)")) SetTimeOfDay(0.75f);
            if (TrainerButton("🌙 Gece Yarısı (00:00)")) SetTimeOfDay(0.02f);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🍂 Mevsim Değiştir", _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton("🌸 İlkbahar (Spring)")) SetSeason(0);
            if (TrainerButton("☀ Yaz (Summer)")) SetSeason(1);
            if (TrainerButton("🍁 Sonbahar (Autumn)")) SetSeason(2);
            if (TrainerButton("❄ Kış (Winter)")) SetSeason(3);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🌧 Hava Durumu", _headerStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton("☀ Açık (Clear)")) SetWeather("clear");
            if (TrainerButton("🌧 Yağmur (Rain)")) SetWeather("rain");
            if (TrainerButton("🌫 Sis (Fog)")) SetWeather("fog");
            if (TrainerButton("❄ Kar (Snow)")) SetWeather("snow");
            if (TrainerButton("⛈ Fırtına (Thunderstorm)")) SetWeather("thunderstorm");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawDevConsoleTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("🛠 Oyunun Kendi Gizli Geliştirici Paneli (DeveloperToolsView)", _headerStyle);
            GUILayout.Label("Going Medieval'ın geliştiricilerinin kullandığı orijinal hile ve test panelini doğrudan açar.", _labelStyle);
            if (TrainerButton("🔧 Orijinal Geliştirici Panelini Aç / Kapat [F2]", GUILayout.Height(36f)))
            {
                ToggleNativeDevTools();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("💻 Geliştirici Konsolu Komut Çalıştırıcı", _headerStyle);
            GUILayout.BeginHorizontal();
            _customConsoleCommand = GUILayout.TextField(_customConsoleCommand ?? "", GUILayout.Width(460f));
            if (TrainerButton("Komutu Çalıştır"))
            {
                ExecuteConsoleCommand(_customConsoleCommand);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Hazır Konsol Komutları:", _labelStyle);
            GUILayout.BeginHorizontal();
            if (TrainerButton("Harita Sınırına İnşaat İzni")) ExecuteConsoleCommand("toggleAllowEdgePlacement");
            if (TrainerButton("Kilitli Binaları Aç")) ExecuteConsoleCommand("unlockLockedBuildings");
            if (TrainerButton("Kaynaksız Otomatik Üretim")) ExecuteConsoleCommand("autoprodResources");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TrainerButton("İşçisiz Otomatik Üretim")) ExecuteConsoleCommand("autoprodWorkers");
            if (TrainerButton("Savaş Hasar Sayılarını Göster"))
            {
                DeveloperVariables.ShowCombatNumbers = !DeveloperVariables.ShowCombatNumbers;
                SetStatus("Savaş Hasar Sayıları: " + (DeveloperVariables.ShowCombatNumbers ? "AÇIK" : "KAPALI"));
            }
            if (TrainerButton("Su Editörünü Aç (waterEdiort)")) ExecuteConsoleCommand("waterEdiort");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawLogsTab()
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("📋 Tıklanan Buton & Oluşan Durum Logları", _headerStyle);
            GUILayout.Label($"Dosya Yolu: {_logFilePath}", _labelStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 Tüm Logları Panoya Kopyala", _buttonStyle, GUILayout.Height(32f)))
            {
                GUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, _actionStatusLogs);
                _statusMessage = $"Tüm loglar ({_actionStatusLogs.Count} satır) panoya kopyalandı! (Ctrl+V ile yapıştırabilirsiniz)";
            }
            if (GUILayout.Button("📂 Log Dosyasını Notepad İle Aç", _buttonStyle, GUILayout.Height(32f)))
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
            if (GUILayout.Button("🗑 Ekran Listesini Temizle", _buttonStyle, GUILayout.Height(32f)))
            {
                _actionStatusLogs.Clear();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            _logScrollPos = GUILayout.BeginScrollView(_logScrollPos, GUILayout.Height(390f));
            if (_actionStatusLogs.Count == 0)
            {
                GUILayout.Label("Henüz bir işlem kaydı yok. Herhangi bir butona tıkladığınızda burada ve log dosyasında görünecektir.", _labelStyle);
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
                SetStatus($"{count} köylünün tüm yetenekleri (Skills) maksimum seviyeye (50) çıkarıldı!");
            }
            catch (Exception ex)
            {
                SetStatus("Yetenek artırma hatası: " + ex.Message, ex);
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
                SetStatus($"{count} köylünün tüm yeteneklerine Çift Yıldız (Tutku) verildi!");
            }
            catch (Exception ex)
            {
                SetStatus("Tutku verme hatası: " + ex.Message, ex);
            }
        }

        private void SpawnNewSettlerNow()
        {
            try
            {
                if (!IsInActiveGameplay() || !MonoSingleton<WorkerGenerator>.IsInstantiated() || !MonoSingleton<WorkerController>.IsInstantiated())
                {
                    SetStatus("Aktif köy bulunamadı!");
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
                    string name = newHumanoid.Info != null ? newHumanoid.Info.GetFullName() : "Yeni Köylü";
                    SetStatus($"Yeni köylü ({name}) köyünüze katıldı!");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Köylü oluşturma hatası: " + ex.Message, ex);
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
                SetStatus("Hızlı İnşaat + Kaynaksız İnşa + Anında Kazı/Kesim: " + (targetState ? "AÇIK" : "KAPALI"));
            }
            catch (Exception ex)
            {
                SetStatus("Hızlı inşaat hatası: " + ex.Message, ex);
            }
        }

        private void FinishAllBlueprintsNow()
        {
            try
            {
                if (MonoSingleton<DeveloperConsoleController>.IsInstantiated())
                {
                    MonoSingleton<DeveloperConsoleController>.Instance.ParseCommand("finishAllBluerpints");
                    SetStatus("Haritadaki tüm inşaat planları (Blueprints) tamamlanıyor!");
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Plan tamamlama hatası: " + ex.Message, ex);
            }
        }

        private void UnlockAllResearchNow()
        {
            try
            {
                if (MonoSingleton<ResearchController>.IsInstantiated())
                {
                    MonoSingleton<ResearchController>.Instance.ActivateAllResearch();
                    SetStatus("Tüm araştırmalar ve teknolojiler açıldı!");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Araştırma açma hatası: " + ex.Message, ex);
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
                    SetStatus("Harita yüklü değil!");
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

                SetStatus($"{count} kaynak yığını maksimum limite dolduruldu ve tüm erzakların tazeliği %100 yapıldı!");
            }
            catch (Exception ex)
            {
                SetStatus("Depo doldurma hatası: " + ex.Message, ex);
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
                    SetStatus("Aktif harita veya ResourcePileManager bulunamadı!");
                    return;
                }

                var blueprint = Repository<ResourceRepository, Resource>.Instance.GetByID(resourceId);
                if (blueprint == null)
                {
                    SetStatus($"Kaynak bulunamadı: {resourceId}");
                    return;
                }

                var worker = WorkerManager.LabourersHere.FirstOrDefault(w => w != null && !w.HasDisposed);
                if (worker == null)
                {
                    SetStatus("Haritada aktif köylü bulunamadı!");
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

                string workerName = worker.Info != null ? worker.Info.GetFullName() : "köylü";
                SetStatus($"+{amount} adet [{resourceId}] ({workerName} yanına) başarıyla oluşturuldu!");
            }
            catch (Exception ex)
            {
                SetStatus($"Kaynak oluşturma hatası ({resourceId}): {ex.Message}", ex);
            }
        }

        private void KillAllEnemiesNow()
        {
            try
            {
                if (MonoSingleton<DeveloperConsoleController>.IsInstantiated())
                {
                    MonoSingleton<DeveloperConsoleController>.Instance.ParseCommand("killEnemies");
                    SetStatus("Haritadaki tüm düşmanlar öldürüldü!");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Düşman öldürme hatası: " + ex.Message, ex);
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
                SetStatus($"{count} tutsak %100 ikna edildi (köye katılmaya hazır)!");
            }
            catch (Exception ex)
            {
                SetStatus("Tutsak ikna hatası: " + ex.Message, ex);
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
                SetStatus($"{count} hayvanın üretimi (süt/yün/yumurta) tamamlandı!");
            }
            catch (Exception ex)
            {
                SetStatus("Hayvan üretim hatası: " + ex.Message, ex);
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
                SetStatus($"{count} hayvan evcilleştirildi, eğitildi ve doyuruldu!");
            }
            catch (Exception ex)
            {
                SetStatus("Hayvan evcilleştirme hatası: " + ex.Message, ex);
            }
        }

        private void SpawnCustomAnimalsNow(string animalId, string displayLabel)
        {
            try
            {
                if (!IsInActiveGameplay() || !MonoSingleton<AnimalManager>.IsInstantiated())
                {
                    SetStatus("Aktif harita veya AnimalManager bulunamadı!");
                    return;
                }

                var worker = WorkerManager.LabourersHere.FirstOrDefault(w => w != null && !w.HasDisposed);
                if (worker == null)
                {
                    SetStatus("Haritada aktif köylü bulunamadı!");
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

                string modeStr = _animalSpawnMode == 0 ? "Yabani (Wild)" : (_animalSpawnMode == 1 ? "Ehli (Domestic)" : "Evcil (Pet)");
                string workerName = worker.Info != null ? worker.Info.GetFullName() : "köylü";
                SetStatus($"+{spawnedCount} adet {displayLabel} [{modeStr}] ({workerName} yanına) oluşturuldu!");
            }
            catch (Exception ex)
            {
                SetStatus($"Hayvan oluşturma hatası ({animalId}): {ex.Message}", ex);
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
                SetStatus($"Oyun Hızı (TimeScale): {scale:0.#}x olarak ayarlandı.");
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
                    SetStatus($"Günün saati %{percent * 100f:0} noktasına ayarlandı.");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Saat değiştirme hatası: " + ex.Message, ex);
            }
        }

        private void SetSeason(int seasonIndex)
        {
            try
            {
                if (MonoSingleton<WeatherManager>.IsInstantiated())
                {
                    MonoSingleton<WeatherManager>.Instance.DebugSetSeasonTime(seasonIndex, 0.1f);
                    string[] names = { "İlkbahar", "Yaz", "Sonbahar", "Kış" };
                    SetStatus($"Mevsim [{names[seasonIndex]}] olarak değiştirildi!");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Mevsim değiştirme hatası: " + ex.Message, ex);
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
                    SetStatus("WeatherManager aktif değil (önce haritaya girin).");
                    return;
                }

                var wm = MonoSingleton<WeatherManager>.Instance;

                if (weatherEvent == "clear")
                {
                    int ended = ClearAllActiveWeatherInternal(wm);
                    SetStatus($"Hava [Açık / Güneşli] yapıldı! ({ended} aktif yağış/fırtına olayı sonlandırıldı)");
                    return;
                }

                if (weatherEvent == "thunderstorm")
                {
                    ClearAllActiveWeatherInternal(wm);
                    if (MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.IsInstantiated())
                    {
                        MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.Instance.StartEvent("game_event_thunderstorm");
                        SetStatus("Hava durumu [⛈ Gök Gürültülü Fırtına (game_event_thunderstorm)] olarak başlatıldı!");
                    }
                    else
                    {
                        SetStatus("GameEventSystem bulunamadı!");
                    }
                    return;
                }

                // rain, fog, snow: Önce mevcut çakışan hava olaylarını temizle, ardından fade-in süresini atlayarak anında %100 yoğunlukta başlat
                ClearAllActiveWeatherInternal(wm);
                var dateObj = GlobalSaveController.CurrentVillageData?.DateAndTime;
                MethodInfo miForceStart = typeof(WeatherManager).GetMethod("ForceStartEvent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (dateObj != null && miForceStart != null)
                {
                    long minInHour = dateObj.MinutesInHour > 0 ? dateObj.MinutesInHour : 60L;
                    long startMin = dateObj.MinutesTotal - (minInHour * 2L); // 1.4 saatlik fadeIn süresini atlamak için 2 saat geriden başlat
                    long durationMin = minInHour * 8L;
                    miForceStart.Invoke(wm, new object[] { weatherEvent, startMin, durationMin, true });
                }
                else
                {
                    wm.DebugForceWeatherEvent(weatherEvent);
                }

                SetStatus($"Hava durumu [{weatherEvent}] anında %100 yoğunlukla başlatıldı!");
            }
            catch (Exception ex)
            {
                SetStatus("Hava durumu hatası: " + ex.Message, ex);
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
                        SetStatus("Orijinal Geliştirici Paneli kapatıldı.");
                    }
                    else
                    {
                        dtv.Open();
                        SetStatus("Orijinal Geliştirici Paneli (DeveloperToolsView) açıldı!");
                    }
                }
                else
                {
                    SetStatus("DeveloperToolsView henüz sahnede aktif değil (önce haritaya girin).");
                }
            }
            catch (Exception ex)
            {
                SetStatus("DevTools hatası: " + ex.Message, ex);
            }
        }

        private void ExecuteConsoleCommand(string rawInput)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawInput)) return;
                if (!MonoSingleton<DeveloperConsoleController>.IsInstantiated())
                {
                    SetStatus("Konsol kontrolcüsü henüz hazır değil (önce haritaya girin).");
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
                SetStatus($"Komut çalıştırıldı: {rawInput}");
            }
            catch (Exception ex)
            {
                SetStatus("Komut hatası: " + ex.Message, ex);
            }
        }
    }
}
