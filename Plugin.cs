using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;

namespace DpsMeter
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Damage data per player (tracked across the whole match)
    // ─────────────────────────────────────────────────────────────────────────
    public class PlayerDamageRecord
    {
        public string DisplayName   { get; set; } = "";
        public Sprite? IconSprite   { get; set; }
        public PlayerTeam Team      { get; set; }
        public float  TotalDamage   { get; set; }
        public int    Kills         { get; set; }
        public float  MatchStartTime { get; set; }
        public bool   IsSelf        { get; set; }

        public float DPS(float now)
        {
            var timerSvc = SineusArena.SessionTimerService.I;
            float elapsed = (timerSvc != null && timerSvc.IsRunning && timerSvc.ElapsedTime > 0f)
                ? timerSvc.ElapsedTime
                : (now - MatchStartTime);
            return elapsed > 0.5f ? TotalDamage / elapsed : 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Central data store — static so Harmony patches can write to it
    // ─────────────────────────────────────────────────────────────────────────
    public static class DpsData
    {
        public static readonly Dictionary<int, PlayerDamageRecord> Records =
            new Dictionary<int, PlayerDamageRecord>();

        public static bool  MatchActive;
        public static float MatchStartTime;

        private static PlayerGameDataManager? _cachedPgdm;
        private static FactionManager? _cachedFm;
        private static FactionDefinition[]? _cachedFactionDefs;

        public static void Reset()
        {
            Records.Clear();
            MatchActive    = false;
            MatchStartTime = Time.time;
            _cachedPgdm    = null;
            _cachedFm      = null;
        }

        public static void OnMatchStart()
        {
            if (MatchActive && Records.Count > 0) return;
            Reset();
            MatchActive    = true;
            MatchStartTime = Time.time;
            Plugin.Log?.LogInfo("[DpsMeter] Match started – DPS tracking active.");
        }

        public static void OnMatchEnd()
        {
            MatchActive = false;
            Plugin.Log?.LogInfo("[DpsMeter] Match ended.");
        }

        public static void RecordDamage(PlayerTeam team, float damage)
        {
            if (damage <= 0) return;
            if (!MatchActive)
            {
                MatchActive = true;
                if (Records.Count == 0) MatchStartTime = Time.time;
            }
            int key = (int)team;
            if (!Records.TryGetValue(key, out var rec))
            {
                var (pName, icon, isSelf) = ResolvePlayerInfo(team);
                rec = new PlayerDamageRecord
                {
                    Team           = team,
                    DisplayName    = pName,
                    IconSprite     = icon,
                    IsSelf         = isSelf,
                    MatchStartTime = MatchStartTime
                };
                Records[key] = rec;
            }
            rec.TotalDamage += damage;
        }

        public static void RecordKill(PlayerTeam team, int amount)
        {
            if (amount <= 0) return;
            if (!MatchActive)
            {
                MatchActive = true;
                if (Records.Count == 0) MatchStartTime = Time.time;
            }
            int key = (int)team;
            if (!Records.TryGetValue(key, out var rec))
            {
                var (pName, icon, isSelf) = ResolvePlayerInfo(team);
                rec = new PlayerDamageRecord
                {
                    Team           = team,
                    DisplayName    = pName,
                    IconSprite     = icon,
                    IsSelf         = isSelf,
                    MatchStartTime = MatchStartTime
                };
                Records[key] = rec;
            }
            rec.Kills += amount;
        }

        public static void RefreshDisplayNames()
        {
            foreach (var rec in Records.Values)
            {
                var (pName, icon, isSelf) = ResolvePlayerInfo(rec.Team);
                rec.DisplayName = pName;
                rec.IsSelf      = isSelf;
                if (icon != null) rec.IconSprite = icon;
            }
        }

        public static void SyncFromNetwork()
        {
            try
            {
                var psm = PlayerStatisticsManager.I ?? UnityEngine.Object.FindAnyObjectByType<PlayerStatisticsManager>();
                if (psm == null) return;

                PlayerTeam[] teams = new[] { PlayerTeam.Player1, PlayerTeam.Player2, PlayerTeam.Player3, PlayerTeam.Player4 };
                foreach (var team in teams)
                {
                    float totalDmg = 0f;
                    var statsList = psm.GetDamageBySourceStats(team);
                    if (statsList != null)
                    {
                        for (int i = 0; i < statsList.Count; i++)
                            totalDmg += statsList[i].Damage;
                    }

                    int kills = psm.GetKills(team);

                    if (totalDmg > 0 || kills > 0)
                    {
                        int key = (int)team;
                        if (!Records.TryGetValue(key, out var rec))
                        {
                            var (pName, icon, isSelf) = ResolvePlayerInfo(team);
                            rec = new PlayerDamageRecord
                            {
                                Team           = team,
                                DisplayName    = pName,
                                IconSprite     = icon,
                                IsSelf         = isSelf,
                                MatchStartTime = MatchStartTime
                            };
                            Records[key] = rec;
                        }

                        if (totalDmg > rec.TotalDamage) rec.TotalDamage = totalDmg;
                        if (kills > rec.Kills) rec.Kills = kills;

                        if (!MatchActive && (totalDmg > 0 || kills > 0))
                        {
                            MatchActive = true;
                            if (MatchStartTime <= 0) MatchStartTime = Time.time;
                        }
                    }
                }
            }
            catch { }
        }

        private static (string playerName, Sprite? heroIcon, bool isSelf) ResolvePlayerInfo(PlayerTeam team)
        {
            string pName = "";
            Sprite? icon = null;
            bool isSelf  = false;

            try
            {
                if (_cachedFm == null) _cachedFm = FactionManager.I ?? UnityEngine.Object.FindAnyObjectByType<FactionManager>();
                if (_cachedFm != null)
                {
                    var skin = _cachedFm.GetTeamSkin(team);
                    if (skin != null && skin.preview != null)
                    {
                        icon = skin.preview;
                    }

                    if (icon == null)
                    {
                        var fDef = _cachedFm.GetTeamFaction(team);
                        if (fDef != null)
                        {
                            if (fDef.defaultSkin != null && fDef.defaultSkin.preview != null) icon = fDef.defaultSkin.preview;
                            else if (fDef.icon != null) icon = fDef.icon;
                            else if (fDef.PrimaryAbilityIcon != null) icon = fDef.PrimaryAbilityIcon;
                            else if (fDef.WeaponIcon != null) icon = fDef.WeaponIcon;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (_cachedPgdm == null) _cachedPgdm = PlayerGameDataManager.I ?? UnityEngine.Object.FindAnyObjectByType<PlayerGameDataManager>();
                if (_cachedPgdm != null)
                {
                    isSelf = _cachedPgdm.localPlayerTeam == team;

                    var data = _cachedPgdm.GetPlayerData(team);
                    if (data != null && !string.IsNullOrEmpty(data.playerSteamName))
                        pName = data.playerSteamName;

                    if (icon == null)
                    {
                        var unit = _cachedPgdm.GetTeamHeroUnit(team);
                        if (unit == null && data != null) unit = data.playerUnit;

                        if (unit != null)
                        {
                            if (_cachedFactionDefs == null || _cachedFactionDefs.Length == 0)
                                _cachedFactionDefs = Resources.FindObjectsOfTypeAll<FactionDefinition>();

                            string uName  = unit.UnitName;
                            string goName = unit.gameObject.name.Replace("(Clone)", "").Trim();

                            if (_cachedFactionDefs != null)
                            {
                                foreach (var fd in _cachedFactionDefs)
                                {
                                    if (fd != null)
                                    {
                                        bool isMatch = (!string.IsNullOrEmpty(fd.HeroName) && (fd.HeroName.Equals(uName, StringComparison.OrdinalIgnoreCase) || fd.HeroName.Equals(goName, StringComparison.OrdinalIgnoreCase)))
                                                    || (!string.IsNullOrEmpty(fd.factionId) && (fd.factionId.Equals(uName, StringComparison.OrdinalIgnoreCase) || fd.factionId.Equals(goName, StringComparison.OrdinalIgnoreCase)));

                                        if (isMatch)
                                        {
                                            if (fd.defaultSkin != null && fd.defaultSkin.preview != null) icon = fd.defaultSkin.preview;
                                            else if (fd.icon != null) icon = fd.icon;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(pName)) pName = TeamToFriendlyName(team);

            return (pName, icon, isSelf);
        }

        private static string TeamToFriendlyName(PlayerTeam team)
        {
            switch (team)
            {
                case PlayerTeam.Player1: return "Player 1";
                case PlayerTeam.Player2: return "Player 2";
                case PlayerTeam.Player3: return "Player 3";
                case PlayerTeam.Player4: return "Player 4";
                default: return team.ToString();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Plugin entry point & Config
    // ─────────────────────────────────────────────────────────────────────────
    [BepInPlugin("com.github.antigravity.dpsmeter", "DPS Meter", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource? Log;
        private Harmony? _harmony;

        // Config Entries
        public static ConfigEntry<KeyCode>? CfgToggleKey;
        public static ConfigEntry<bool>?    CfgDefaultVisible;
        public static ConfigEntry<float>?   CfgUpdateInterval;
        public static ConfigEntry<float>?   CfgWindowWidth;
        public static ConfigEntry<float>?   CfgRowHeight;
        public static ConfigEntry<float>?   CfgBarOpacity;
        public static ConfigEntry<int>?     CfgFontSize;
        public static ConfigEntry<float>?   CfgPositionX;
        public static ConfigEntry<float>?   CfgPositionY;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("DPS Meter loading configuration...");

            // Setup Configs
            CfgToggleKey      = Config.Bind("General", "ToggleKey", KeyCode.Delete, "Key code used to toggle visibility of the DPS Meter.");
            CfgDefaultVisible = Config.Bind("General", "DefaultVisible", true, "Should the DPS Meter window be visible by default upon starting.");
            CfgUpdateInterval = Config.Bind("General", "UpdateInterval", 0.25f, "Seconds between UI refreshes (lower is smoother, higher saves CPU).");

            CfgWindowWidth    = Config.Bind("Appearance", "WindowWidth", 380f, "Width of the DPS Meter window in pixels.");
            CfgRowHeight      = Config.Bind("Appearance", "RowHeight", 28f, "Height of each player row in pixels.");
            CfgBarOpacity     = Config.Bind("Appearance", "BarOpacity", 0.22f, "Opacity/alpha of the rank bar fill (0.0 to 1.0).");
            CfgFontSize       = Config.Bind("Appearance", "FontSize", 11, "Font size of player rows text.");

            CfgPositionX      = Config.Bind("Position", "PositionX", 430f, "Saved X position offset of the window relative to screen center.");
            CfgPositionY      = Config.Bind("Position", "PositionY", 180f, "Saved Y position offset of the window relative to screen center.");

            try
            {
                _harmony = new Harmony("com.github.antigravity.dpsmeter");
                _harmony.PatchAll();
                Log.LogInfo("DPS Meter Harmony patches applied.");

                var go = new GameObject("DpsMeterController");
                DontDestroyOnLoad(go);
                go.AddComponent<DpsMeterController>();

                Log.LogInfo($"DPS Meter ready. Press {CfgToggleKey.Value} to toggle.");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to initialize DPS Meter: " + ex);
            }
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Harmony Patches
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Trigger Reset when a round rematch/restart occurs in GameFlowManager.
    /// </summary>
    [HarmonyPatch(typeof(GameFlowManager), "DoRematch")]
    public static class GameRematchPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            Plugin.Log?.LogInfo("[DpsMeter] Round restarting (Rematch) – resetting DPS data.");
            DpsData.Reset();
        }
    }

    /// <summary>
    /// Trigger Reset when SessionTimerService restarts the timer for a new round.
    /// </summary>
    [HarmonyPatch(typeof(SineusArena.SessionTimerService), "RestartTimer")]
    public static class TimerRestartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.Log?.LogInfo("[DpsMeter] Session timer restarted – resetting DPS data.");
            DpsData.Reset();
        }
    }

    [HarmonyPatch(typeof(PlayerStatisticsManager), nameof(PlayerStatisticsManager.AddDamage))]
    public static class AddDamagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerTeam team, string sourceName, float amount)
        {
            DpsData.RecordDamage(team, amount);
        }
    }

    [HarmonyPatch(typeof(PlayerStatisticsManager), nameof(PlayerStatisticsManager.AddKill))]
    public static class AddKillPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerTeam team, int amount)
        {
            DpsData.RecordKill(team, amount);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI Controller
    // ─────────────────────────────────────────────────────────────────────────
    public class DpsMeterController : MonoBehaviour
    {
        private GameObject? _canvasObj;
        private GameObject? _windowObj;
        private RectTransform? _windowRect;
        private Text? _timerText;
        private Text? _titleText;
        private GameObject? _scrollContent;
        private readonly List<PlayerRow> _rows = new List<PlayerRow>();

        private bool    _isDragging;
        private Vector2 _dragOffset;

        private bool  _visible = true;
        private float _refreshTimer;
        private float _nameRefreshTimer;

        // Base Colours
        private static readonly Color ColHeader      = new Color(0.06f, 0.08f, 0.14f, 0.97f);
        private static readonly Color ColBody        = new Color(0.04f, 0.05f, 0.10f, 0.93f);
        private static readonly Color ColBorderOuter = new Color(0.22f, 0.48f, 1.00f, 0.95f);
        private static readonly Color ColTitle       = new Color(0.55f, 0.82f, 1.00f, 1.00f);
        private static readonly Color ColTimer       = new Color(0.60f, 0.65f, 0.78f, 1.00f);
        private static readonly Color ColDps         = new Color(0.60f, 0.95f, 1.00f, 1.00f);
        private static readonly Color ColKills       = new Color(1.00f, 0.55f, 0.30f, 1.00f);
        private static readonly Color ColLabel       = new Color(0.45f, 0.60f, 0.88f, 1.00f);

        private const float HeaderH = 30f;
        private const float SubH    = 20f;

        private Font? _font;

        private void Start()
        {
            _visible = Plugin.CfgDefaultVisible?.Value ?? true;
            _font = FindFont();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            BuildUI();
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            DpsData.Reset();
        }

        private void Update()
        {
            KeyCode toggleKey = Plugin.CfgToggleKey?.Value ?? KeyCode.Delete;
            if (Input.GetKeyDown(toggleKey))
                SetVisible(!_visible);

            if (!_visible) return;

            HandleDrag();

            float interval = Plugin.CfgUpdateInterval?.Value ?? 0.25f;
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= interval)
            {
                _refreshTimer = 0f;
                RefreshUI();
            }

            _nameRefreshTimer += Time.deltaTime;
            if (_nameRefreshTimer >= 5f)
            {
                _nameRefreshTimer = 0f;
                if (DpsData.MatchActive) DpsData.RefreshDisplayNames();
            }
        }

        private void HandleDrag()
        {
            if (_windowRect == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mp = ScreenToCanvas(Input.mousePosition);
                Vector2 wp = _windowRect.anchoredPosition;
                float hw = _windowRect.sizeDelta.x * 0.5f;
                float wh = _windowRect.sizeDelta.y * 0.5f;

                bool inHeader = mp.x > wp.x - hw && mp.x < wp.x + hw
                             && mp.y > wp.y + wh - HeaderH && mp.y < wp.y + wh;
                if (inHeader)
                {
                    _isDragging = true;
                    _dragOffset = wp - mp;
                }
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    SavePosition();
                }
            }
            if (_isDragging && Input.GetMouseButton(0))
            {
                _windowRect.anchoredPosition = ScreenToCanvas(Input.mousePosition) + _dragOffset;
                ClampToScreen();
            }
        }

        private void SavePosition()
        {
            if (_windowRect == null) return;
            if (Plugin.CfgPositionX != null) Plugin.CfgPositionX.Value = _windowRect.anchoredPosition.x;
            if (Plugin.CfgPositionY != null) Plugin.CfgPositionY.Value = _windowRect.anchoredPosition.y;
        }

        private Vector2 ScreenToCanvas(Vector3 screenPos) =>
            new Vector2(screenPos.x - Screen.width * 0.5f, screenPos.y - Screen.height * 0.5f);

        private void ClampToScreen()
        {
            if (_windowRect == null) return;
            float hw = _windowRect.sizeDelta.x * 0.5f;
            float hh = _windowRect.sizeDelta.y * 0.5f;
            var p = _windowRect.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -Screen.width * 0.5f + hw, Screen.width * 0.5f - hw);
            p.y = Mathf.Clamp(p.y, -Screen.height * 0.5f + hh, Screen.height * 0.5f - hh);
            _windowRect.anchoredPosition = p;
        }

        private void SetVisible(bool v)
        {
            _visible = v;
            _windowObj?.SetActive(v);
        }

        private void BuildUI()
        {
            float winW = Plugin.CfgWindowWidth?.Value ?? 380f;
            float posX = Plugin.CfgPositionX?.Value ?? 430f;
            float posY = Plugin.CfgPositionY?.Value ?? 180f;

            _canvasObj = new GameObject("DpsMeterCanvas");
            DontDestroyOnLoad(_canvasObj);
            var canvas = _canvasObj.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9998;
            _canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Window root container
            _windowObj = new GameObject("DpsWindow");
            _windowObj.transform.SetParent(_canvasObj.transform, false);
            _windowRect = _windowObj.AddComponent<RectTransform>();
            _windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _windowRect.pivot     = new Vector2(0.5f, 0.5f);
            _windowRect.anchoredPosition = new Vector2(posX, posY);
            _windowRect.sizeDelta        = new Vector2(winW, 200f);

            // Window background
            var bgObj = MakeChild(_windowObj, "Background");
            bgObj.AddComponent<Image>().color = ColBody;
            var bgr = bgObj.GetComponent<RectTransform>();
            bgr.anchorMin = Vector2.zero; bgr.anchorMax = Vector2.one;
            bgr.offsetMin = Vector2.zero; bgr.offsetMax = Vector2.zero;

            // Content container (inset by 2px to sit inside the outer border)
            var contentObj = MakeChild(_windowObj, "Content");
            var cntr = contentObj.AddComponent<RectTransform>();
            cntr.anchorMin = Vector2.zero; cntr.anchorMax = Vector2.one;
            cntr.offsetMin = new Vector2(2, 2); cntr.offsetMax = new Vector2(-2, -2);

            // ── Header ───────────────────────────────────────────────────────
            var hdr = MakeChild(contentObj, "Header");
            hdr.AddComponent<Image>().color = ColHeader;
            var hr = hdr.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot     = new Vector2(0.5f, 1);
            hr.offsetMin = Vector2.zero; hr.offsetMax = Vector2.zero;
            hr.sizeDelta = new Vector2(0, HeaderH);

            var accent = MakeChild(hdr, "Accent");
            accent.AddComponent<Image>().color = new Color(0.30f, 0.60f, 1.00f, 0.70f);
            var acr = accent.GetComponent<RectTransform>();
            acr.anchorMin = new Vector2(0, 0); acr.anchorMax = new Vector2(1, 0);
            acr.pivot = new Vector2(0.5f, 0); acr.sizeDelta = new Vector2(0, 2);

            _titleText = MakeText(hdr, "Title", 0f, 0.65f, 0f, 1.00f, TextAnchor.MiddleLeft, 13, FontStyle.Bold, ColTitle);
            _titleText.text = "⚔  DPS Meter";
            OffsetText(_titleText, 10, 0, 0, 0);

            _timerText = MakeText(hdr, "Timer", 0.65f, 1f, 0f, 1.00f, TextAnchor.MiddleRight, 12, FontStyle.Normal, ColTimer);
            _timerText.text = "--:--";
            OffsetText(_timerText, 0, 0, -10, 0);

            // ── Sub-header (column labels) ────────────────────────────────
            var sub = MakeChild(contentObj, "SubHeader");
            sub.AddComponent<Image>().color = new Color(0.09f, 0.12f, 0.22f, 0.95f);
            var sr = sub.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0, 1); sr.anchorMax = new Vector2(1, 1);
            sr.pivot     = new Vector2(0.5f, 1);
            sr.offsetMin = Vector2.zero; sr.offsetMax = Vector2.zero;
            sr.anchoredPosition = new Vector2(0, -HeaderH);
            sr.sizeDelta        = new Vector2(0, SubH);

            var subAccent = MakeChild(sub, "SubAccent");
            subAccent.AddComponent<Image>().color = new Color(0.30f, 0.60f, 1.00f, 0.35f);
            var sacr = subAccent.GetComponent<RectTransform>();
            sacr.anchorMin = new Vector2(0, 0); sacr.anchorMax = new Vector2(1, 0);
            sacr.pivot = new Vector2(0.5f, 0); sacr.sizeDelta = new Vector2(0, 1);

            AddColLabel(sub, "",       0.00f, 0.07f, TextAnchor.MiddleCenter);
            AddColLabel(sub, "#",      0.07f, 0.13f, TextAnchor.MiddleCenter);
            AddColLabel(sub, "Player", 0.13f, 0.42f, TextAnchor.MiddleLeft);
            AddColLabel(sub, "Damage", 0.42f, 0.58f, TextAnchor.MiddleRight);
            AddColLabel(sub, "DPS",    0.58f, 0.72f, TextAnchor.MiddleRight);
            AddColLabel(sub, "Kills",  0.72f, 0.85f, TextAnchor.MiddleCenter);
            AddColLabel(sub, "%",      0.85f, 1.00f, TextAnchor.MiddleRight);

            // ── Row container ────────────────────────────────────────────────
            var rowArea = MakeChild(contentObj, "RowArea");
            rowArea.AddComponent<RectTransform>();
            var rar = rowArea.GetComponent<RectTransform>();
            rar.anchorMin = Vector2.zero; rar.anchorMax = Vector2.one;
            rar.offsetMin = Vector2.zero; rar.offsetMax = new Vector2(0, -(HeaderH + SubH + 1));

            _scrollContent = MakeChild(rowArea, "Rows");
            var scr = _scrollContent.AddComponent<RectTransform>();
            scr.anchorMin = new Vector2(0, 1); scr.anchorMax = new Vector2(1, 1);
            scr.pivot     = new Vector2(0.5f, 1);
            scr.anchoredPosition = Vector2.zero;

            // ── Top-most 4-side Outer Border Overlay Frame ──────────────────
            var borderFrame = MakeChild(_windowObj, "BorderOverlay");
            borderFrame.AddComponent<RectTransform>();
            var bfr = borderFrame.GetComponent<RectTransform>();
            bfr.anchorMin = Vector2.zero; bfr.anchorMax = Vector2.one;
            bfr.offsetMin = Vector2.zero; bfr.offsetMax = Vector2.zero;

            Color borderCol = new Color(0.22f, 0.48f, 1.00f, 1.00f);

            // Top Line
            var borderTop = MakeChild(borderFrame, "BorderTop");
            borderTop.AddComponent<Image>().color = borderCol;
            var btr = borderTop.GetComponent<RectTransform>();
            btr.anchorMin = new Vector2(0, 1); btr.anchorMax = new Vector2(1, 1);
            btr.pivot = new Vector2(0.5f, 1); btr.sizeDelta = new Vector2(0, 2);

            // Bottom Line
            var borderBottom = MakeChild(borderFrame, "BorderBottom");
            borderBottom.AddComponent<Image>().color = borderCol;
            var bbr = borderBottom.GetComponent<RectTransform>();
            bbr.anchorMin = new Vector2(0, 0); bbr.anchorMax = new Vector2(1, 0);
            bbr.pivot = new Vector2(0.5f, 0); bbr.sizeDelta = new Vector2(0, 2);

            // Left Line
            var borderLeft = MakeChild(borderFrame, "BorderLeft");
            borderLeft.AddComponent<Image>().color = borderCol;
            var blr = borderLeft.GetComponent<RectTransform>();
            blr.anchorMin = new Vector2(0, 0); blr.anchorMax = new Vector2(0, 1);
            blr.pivot = new Vector2(0, 0.5f); blr.sizeDelta = new Vector2(2, 0);

            // Right Line
            var borderRight = MakeChild(borderFrame, "BorderRight");
            borderRight.AddComponent<Image>().color = borderCol;
            var brr = borderRight.GetComponent<RectTransform>();
            brr.anchorMin = new Vector2(1, 0); brr.anchorMax = new Vector2(1, 1);
            brr.pivot = new Vector2(1, 0.5f); brr.sizeDelta = new Vector2(2, 0);

            _windowObj.SetActive(_visible);
        }

        private void AddColLabel(GameObject parent, string text, float xMin, float xMax, TextAnchor anchor)
        {
            var t = MakeText(parent, "Col_" + text, xMin, xMax, 0f, 1f, anchor, 9, FontStyle.Bold, ColLabel);
            t.text = text.ToUpperInvariant();
            OffsetText(t, 4, 0, -4, 0);
        }

        private void RefreshUI()
        {
            if (_windowRect == null || _scrollContent == null) return;
            
            DpsData.SyncFromNetwork();
            
            float now = Time.time;

            float winW     = Plugin.CfgWindowWidth?.Value ?? 380f;
            float rowH     = Plugin.CfgRowHeight?.Value ?? 28f;
            float opacity  = Plugin.CfgBarOpacity?.Value ?? 0.22f;

            // Timer sync with SessionTimerService
            if (_timerText != null)
            {
                var timerSvc = SineusArena.SessionTimerService.I;
                if (timerSvc != null && timerSvc.IsRunning)
                {
                    _timerText.text = timerSvc.FormattedTime;
                }
                else if (DpsData.MatchActive)
                {
                    float e = now - DpsData.MatchStartTime;
                    _timerText.text = $"{(int)(e/60)}:{(int)(e%60):D2}";
                }
                else
                {
                    _timerText.text = DpsData.Records.Count > 0 ? "ENDED" : "--:--";
                }
            }

            var sorted = DpsData.Records.Values
                .OrderByDescending(r => r.TotalDamage)
                .ToList();

            float maxDmg      = sorted.Count > 0 && sorted[0].TotalDamage > 0 ? sorted[0].TotalDamage : 1f;
            float totalDmgAll = sorted.Sum(r => r.TotalDamage);

            while (_rows.Count < sorted.Count)
                _rows.Add(BuildRow(_rows.Count));

            for (int i = sorted.Count; i < _rows.Count; i++)
                _rows[i].Root?.SetActive(false);

            if (sorted.Count == 0) { EnsureEmptyLabel(); }
            else { DestroyEmptyLabel(); }

            Color[] barColors = new Color[]
            {
                new Color(1.00f, 0.78f, 0.08f, opacity), // gold
                new Color(0.68f, 0.68f, 0.78f, opacity * 0.85f), // silver
                new Color(0.78f, 0.48f, 0.22f, opacity * 0.75f), // bronze
                new Color(0.20f, 0.42f, 0.80f, opacity * 0.65f), // blue
            };

            for (int i = 0; i < sorted.Count; i++)
            {
                var rec = sorted[i];
                var row = _rows[i];

                row.Root?.SetActive(true);
                var rr = row.Root?.GetComponent<RectTransform>();
                if (rr != null)
                {
                    rr.anchoredPosition = new Vector2(0, -i * rowH);
                    rr.sizeDelta        = new Vector2(0, rowH);
                }

                float frac = rec.TotalDamage / maxDmg;
                if (row.BarFill != null)
                {
                    var br = row.BarFill.GetComponent<RectTransform>();
                    if (br != null) br.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
                    row.BarFill.color = barColors[Mathf.Min(i, barColors.Length - 1)];
                }

                if (row.IconImg != null)
                {
                    if (rec.IconSprite != null)
                    {
                        row.IconImg.sprite = rec.IconSprite;
                        row.IconImg.gameObject.SetActive(true);
                    }
                    else
                    {
                        row.IconImg.gameObject.SetActive(false);
                    }
                }

                if (row.RankText != null)
                {
                    row.RankText.text  = (i + 1).ToString();
                    row.RankText.color = i == 0 ? new Color(1f, 0.85f, 0.15f) :
                                         i == 1 ? new Color(0.75f, 0.75f, 0.82f) :
                                         i == 2 ? new Color(0.80f, 0.50f, 0.25f) :
                                                  new Color(0.50f, 0.55f, 0.70f);
                }

                bool isSelf = rec.IsSelf;

                if (row.NameText  != null)
                {
                    row.NameText.text      = rec.DisplayName;
                    row.NameText.color     = isSelf ? new Color(0.30f, 0.90f, 0.45f) : new Color(0.88f, 0.88f, 0.96f);
                    row.NameText.fontStyle = isSelf ? FontStyle.Bold : FontStyle.Normal;
                }
                if (row.DmgText   != null) row.DmgText.text  = FormatDamage(rec.TotalDamage);
                if (row.DpsText   != null) row.DpsText.text  = $"{rec.DPS(now):F0}";
                if (row.KillText  != null) row.KillText.text  = rec.Kills.ToString();
                
                float pct = totalDmgAll > 0f ? (rec.TotalDamage / totalDmgAll) * 100f : 0f;
                if (row.PctText   != null) row.PctText.text  = $"{pct:F1}%";

                if (row.Bg != null)
                    row.Bg.color = i % 2 == 0
                        ? new Color(0.08f, 0.10f, 0.18f, 0.88f)
                        : new Color(0.06f, 0.08f, 0.14f, 0.88f);
            }

            float bodyH  = Mathf.Max(sorted.Count * rowH, 28f);
            float totalH = HeaderH + SubH + bodyH + 4f;
            _windowRect.sizeDelta = new Vector2(winW, totalH);

            var cr = _scrollContent.GetComponent<RectTransform>();
            if (cr != null) cr.sizeDelta = new Vector2(0, bodyH);
        }

        private GameObject? _emptyLabel;

        private void EnsureEmptyLabel()
        {
            if (_emptyLabel != null) return;
            _emptyLabel = MakeChild(_scrollContent!, "Empty");
            var t = _emptyLabel.AddComponent<Text>();
            t.font      = _font;
            t.fontSize  = Plugin.CfgFontSize?.Value ?? 11;
            t.color     = new Color(0.38f, 0.44f, 0.60f, 0.85f);
            t.alignment = TextAnchor.MiddleCenter;
            t.text      = "Waiting for combat data…";
            var r = _emptyLabel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot     = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(0, 28f);
            r.anchoredPosition = new Vector2(0, 0);
        }

        private void DestroyEmptyLabel()
        {
            if (_emptyLabel != null) { Destroy(_emptyLabel); _emptyLabel = null; }
        }

        private PlayerRow BuildRow(int idx)
        {
            int fontSize = Plugin.CfgFontSize?.Value ?? 11;

            var root = MakeChild(_scrollContent!, $"Row_{idx}");
            var rr   = root.AddComponent<RectTransform>();
            rr.anchorMin = new Vector2(0, 1); rr.anchorMax = new Vector2(1, 1);
            rr.pivot     = new Vector2(0.5f, 1);

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.10f, 0.18f, 0.88f);

            var barObj = MakeChild(root, "Bar");
            var barImg = barObj.AddComponent<Image>();
            var barRt  = barObj.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0); barRt.anchorMax = new Vector2(0, 1);
            barRt.offsetMin = Vector2.zero; barRt.offsetMax = Vector2.zero;

            var iconObj = MakeChild(root, "Icon");
            var iconImg = iconObj.AddComponent<Image>();
            iconImg.preserveAspect = true;
            var iconRt  = iconObj.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.00f, 0f); iconRt.anchorMax = new Vector2(0.07f, 1f);
            iconRt.offsetMin = new Vector2(2, 2); iconRt.offsetMax = new Vector2(-2, -2);

            var rank  = MakeText(root, "Rank",  0.07f, 0.13f, 0f, 1f, TextAnchor.MiddleCenter, fontSize + 1, FontStyle.Bold,   new Color(1f, 0.85f, 0.15f));
            var name  = MakeText(root, "Name",  0.13f, 0.42f, 0f, 1f, TextAnchor.MiddleLeft,   fontSize,     FontStyle.Normal,  new Color(0.88f, 0.88f, 0.96f));
            var dmg   = MakeText(root, "Dmg",   0.42f, 0.58f, 0f, 1f, TextAnchor.MiddleRight,  fontSize,     FontStyle.Normal,  new Color(0.88f, 0.88f, 0.96f));
            var dps   = MakeText(root, "Dps",   0.58f, 0.72f, 0f, 1f, TextAnchor.MiddleRight,  fontSize,     FontStyle.Bold,    ColDps);
            var kills = MakeText(root, "Kills", 0.72f, 0.85f, 0f, 1f, TextAnchor.MiddleCenter, fontSize,     FontStyle.Normal,  ColKills);
            var pct   = MakeText(root, "Pct",   0.85f, 1.00f, 0f, 1f, TextAnchor.MiddleRight,  fontSize,     FontStyle.Normal,  new Color(0.75f, 0.92f, 1.00f));

            foreach (var t in new[] { rank, name, dmg, dps, kills, pct })
                OffsetText(t, 4, 1, -4, -1);

            return new PlayerRow { Root = root, Bg = bg, BarFill = barImg, IconImg = iconImg,
                                   RankText = rank, NameText = name, DmgText = dmg,
                                   DpsText = dps, KillText = kills, PctText = pct };
        }

        private static GameObject MakeChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private Text MakeText(GameObject parent, string name,
                              float xMin, float xMax, float yMin, float yMax,
                              TextAnchor anchor, int size, FontStyle style, Color color)
        {
            var go = MakeChild(parent, name);
            var t  = go.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.fontStyle = style;
            t.color = color; t.alignment = anchor;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(xMin, yMin); r.anchorMax = new Vector2(xMax, yMax);
            return t;
        }

        private static void OffsetText(Text t, float left, float bottom, float right, float top)
        {
            var r = t.GetComponent<RectTransform>();
            r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(right, top);
        }

        private static string FormatDamage(float d) =>
            d >= 1_000_000f ? $"{d/1_000_000f:F2}M" :
            d >= 1_000f     ? $"{d/1_000f:F1}k"     :
            $"{d:F0}";

        private static Font FindFont()
        {
            try
            {
                foreach (var t in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t?.font != null) return t.font;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    public class PlayerRow
    {
        public GameObject? Root;
        public Image?       Bg;
        public Image?       BarFill;
        public Image?       IconImg;
        public Text?        RankText;
        public Text?        NameText;
        public Text?        DmgText;
        public Text?        DpsText;
        public Text?        PctText;
        public Text?        KillText;
    }
}
