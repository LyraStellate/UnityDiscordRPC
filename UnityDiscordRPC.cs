#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Discord;
using System.IO;

// 設定クラス
public class UnityDiscordRPCSettings{
    private const string PREF_PREFIX = "UnityDiscordRPC_";

    public static string CustomDetails{
        get => EditorPrefs.GetString(PREF_PREFIX + "Details", "");
        set => EditorPrefs.SetString(PREF_PREFIX + "Details", value);
    }

    public static bool PrivacyMode{
        get => EditorPrefs.GetBool(PREF_PREFIX + "Privacy", false);
        set => EditorPrefs.SetBool(PREF_PREFIX + "Privacy", value);
    }

    public static bool ShowRootObject{
        get => EditorPrefs.GetBool(PREF_PREFIX + "ShowRoot", true);
        set => EditorPrefs.SetBool(PREF_PREFIX + "ShowRoot", value);
    }
}

// 設定ウィンドウ
public class UnityDiscordRPCWindow : EditorWindow{
    [MenuItem("Tools/UnityDiscordRPC Settings")]
    public static void ShowWindow(){
        GetWindow<UnityDiscordRPCWindow>("Discord RPC Settings");
    }

    private void OnGUI(){
        GUILayout.Label("Discord Rich Presence Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // --- Details ---
        GUILayout.Label("Details (Line 1: Project Name)", EditorStyles.label);
        string currentDetails = UnityDiscordRPCSettings.CustomDetails;
        string newDetails = EditorGUILayout.TextField("Custom Project Name", currentDetails);
        if (newDetails != currentDetails) UnityDiscordRPCSettings.CustomDetails = newDetails;
        
        EditorGUILayout.HelpBox("空白の場合はUnityのプロジェクト名が使用されます。", MessageType.Info);
        EditorGUILayout.Space();

        // --- State ---
        GUILayout.Label("State (Line 2: Status)", EditorStyles.boldLabel);
        
        bool currentPrivacy = UnityDiscordRPCSettings.PrivacyMode;
        bool newPrivacy = EditorGUILayout.Toggle("Privacy Mode", currentPrivacy);
        if (newPrivacy != currentPrivacy) UnityDiscordRPCSettings.PrivacyMode = newPrivacy;

        if (newPrivacy){
            EditorGUILayout.HelpBox("プライバシーモード有効: 作業中のオブジェクト名は隠されます。", MessageType.Warning);
        }
        else{
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Object Name Display Style:");
            
            bool isRoot = UnityDiscordRPCSettings.ShowRootObject;
            
            if (EditorGUILayout.Toggle("Show Root Object Name", isRoot)){
                if (!isRoot) UnityDiscordRPCSettings.ShowRootObject = true;
            }
            
            if (EditorGUILayout.Toggle("Show Selected Object Name", !isRoot)){
                if (isRoot) UnityDiscordRPCSettings.ShowRootObject = false;
            }
            
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Force Update Presence")){
            UnityDiscordRPC.ForceUpdate();
        }
    }
}

// メイン処理
[InitializeOnLoad]
public class UnityDiscordRPC : IPreprocessBuildWithReport, IPostprocessBuildWithReport{
    private static Discord.Discord discord;
    private const long AppId = 1442843071342579902;

    public int callbackOrder => 0;

    // キャッシュ用
    private static string lastState = "";
    private static string lastDetails = "";
    private static long lastUpdateTime = 0;
    private static bool isCompiling = false;

    // 開始時刻を保持
    private static long startTimeStamp = 0;

    static UnityDiscordRPC(){
        StopDiscord();

        startTimeStamp = System.DateTimeOffset.Now.ToUnixTimeSeconds();

        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.quitting += StopDiscord;
        Selection.selectionChanged += OnSelectionChanged;
        
        StartDiscord();
    }

    public static void ForceUpdate(){
        lastDetails = ""; 
        lastState = "";
        UpdateActivity(true);
    }

    private static void StartDiscord(){
        if (discord != null) return;
        try{
            discord = new Discord.Discord(AppId, (ulong)Discord.CreateFlags.NoRequireDiscord);
            isCompiling = false;
            UpdateActivity(true);
        }
        catch { /* 無視 */ }
    }

    private static void Update(){
        if (isCompiling || BuildPipeline.isBuildingPlayer) return;

        if (discord == null){
            long now = System.DateTimeOffset.Now.ToUnixTimeSeconds();
            if (now - lastUpdateTime > 10){
                lastUpdateTime = now;
                StartDiscord();
            }
            return;
        }

        try{
            discord.RunCallbacks();
            UpdateActivity(false);
        }
        catch
        {
            StopDiscord();
        }
    }

    private static void OnSelectionChanged(){
        if (!isCompiling) UpdateActivity(false);
    }

    private static void OnBeforeAssemblyReload(){
        isCompiling = true;
        if (discord == null) return;

        // コンパイル中の表示
        var activity = new Discord.Activity{
            Details = "System",
            State = "Compiling Scripts...",
            Assets = {
                LargeImage = "unity_logo",
                LargeText = "Unity 2022"
            },
            Timestamps = { Start = startTimeStamp },
            Instance = false
        };

        try {
            discord.GetActivityManager().UpdateActivity(activity, (res) => {});
            discord.RunCallbacks();
            System.Threading.Thread.Sleep(100);
        }
        catch {}

        StopDiscord();
    }

    private static void UpdateActivity(bool forceUpdate)
    {
        if (discord == null) return;

        // --- Details ---
        string detailsText = UnityDiscordRPCSettings.CustomDetails;
        if (string.IsNullOrEmpty(detailsText)){
            try {
                var projectDir = Directory.GetParent(Application.dataPath);
                detailsText = projectDir != null ? projectDir.Name : Application.productName;
            } catch {
                detailsText = Application.productName;
            }
        }

        // --- State (ステータス) ---
        string modePrefix = EditorApplication.isPlaying ? "In Execution" : "In Editor";
        string stateText = "";

        if (UnityDiscordRPCSettings.PrivacyMode){
            stateText = modePrefix;
        }
        else{
            string objName = "No Selection";
            if (Selection.activeGameObject != null){
                if (UnityDiscordRPCSettings.ShowRootObject)
                    objName = Selection.activeGameObject.transform.root.name;
                else
                    objName = Selection.activeGameObject.name;
            }
            stateText = $"{modePrefix}: {objName}";
        }

        // 送信制御
        if (!forceUpdate && detailsText == lastDetails && stateText == lastState) return;

        lastDetails = detailsText;
        lastState = stateText;

        // Activity構造体
        var activity = new Discord.Activity{
            Details = detailsText,
            State = stateText,
            Assets = {
                LargeImage = "unity_logo",
                LargeText = "Unity 2022"
            },
            Timestamps = {
                Start = startTimeStamp
            },
        };

        discord.GetActivityManager().UpdateActivity(activity, (res) => {});
    }

    private static void StopDiscord(){
        if (discord != null){
            discord.Dispose();
            discord = null;
        }
    }

    public void OnPreprocessBuild(BuildReport report) => StopDiscord();
    public void OnPostprocessBuild(BuildReport report) => StartDiscord();
}

#endif