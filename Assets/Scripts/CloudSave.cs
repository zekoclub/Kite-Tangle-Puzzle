using System;
using System.Text;
using UnityEngine;
#if !UNITY_EDITOR && UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;
#endif

// Cloud save through Google Play Games Saved Games (plugin 2.3.0, in Assets/GooglePlayGames), so progress comes back
// after a reinstall or on a new phone. One save file holds Progress.ToCloudJson().
// - Off until Play Games is set up: Window > Google Play Games > Setup > Android setup fills in the App ID
//   (Assets/GooglePlayGames/Resources/PlayGamesSettings.asset and GooglePlayGamesManifest.androidlib).
// - At startup it signs in silently. After sign-in it first reads the cloud save and merges it (Progress.MergeCloud);
//   only then does it upload, so a fresh install never overwrites a good cloud save.
// - Uploads follow Progress.Changed at most every MinUploadGap seconds, and always when the app goes to the background.
// In the Editor and desktop builds there is no Play Games: Available is false and nothing happens.
public static class CloudSave
{
    const string SaveName = "kite_tangle_progress";
    const float MinUploadGap = 30f;

#pragma warning disable CS0067 // only raised on Android
    public static event Action Changed; // sign-in state changed
#pragma warning restore CS0067

#if !UNITY_EDITOR && UNITY_ANDROID
    static bool started, signedIn, loaded, busy, pending;
    static float lastPushAt = -MinUploadGap;

    // True when this build has a Play Games App ID.
    public static bool Available => PlayGamesSettings.LoadInstance() is { } settings && !string.IsNullOrEmpty(settings.AppId);
    public static bool SignedIn => signedIn;

    static ISavedGameClient Client => PlayGamesPlatform.Instance.SavedGame;

    // Call once at startup.
    public static void Init()
    {
        if (started || !Available) return;
        started = true;
        PlayGamesPlatform.Activate();
        PlayGamesPlatform.Instance.Authenticate(OnSignIn);
        Progress.Changed += () => Push();
    }

    // The Settings button, for players who skipped the automatic sign-in.
    public static void SignIn()
    {
        Init();
        if (started && !signedIn) PlayGamesPlatform.Instance.ManuallyAuthenticate(OnSignIn);
    }

    static void OnSignIn(SignInStatus status)
    {
        signedIn = status == SignInStatus.Success;
        if (!signedIn) Debug.Log("[CloudSave] Not signed in: " + status);
        Changed?.Invoke();
        if (signedIn && !loaded && !busy) Load();
    }

    static void Load()
    {
        busy = true;
        Open(game =>
        {
            if (game == null) { busy = false; return; }
            Client.ReadBinaryData(game, (status, data) =>
            {
                busy = false;
                if (status != SavedGameRequestStatus.Success)
                {
                    Debug.LogWarning("[CloudSave] Read failed: " + status); // no upload: it could overwrite a good save
                    return;
                }
                if (data != null && data.Length > 0) Progress.MergeCloud(Encoding.UTF8.GetString(data));
                loaded = true;
                Push(force: true);
            });
        });
    }

    // Uploads this phone's progress. Without force it waits MinUploadGap between uploads (the next change or
    // going to the background sends what was skipped).
    public static void Push(bool force = false)
    {
        if (!signedIn || !loaded) return;
        if (busy || (!force && Time.realtimeSinceStartup - lastPushAt < MinUploadGap)) { pending = true; return; }
        busy = true;
        pending = false;
        lastPushAt = Time.realtimeSinceStartup;
        byte[] data = Encoding.UTF8.GetBytes(Progress.ToCloudJson());
        string description = $"Level {Progress.Level}";
        Open(game =>
        {
            if (game == null) { busy = false; pending = true; return; }
            var update = new SavedGameMetadataUpdate.Builder().WithUpdatedDescription(description).Build();
            Client.CommitUpdate(game, update, data, (status, _) =>
            {
                busy = false;
                if (status != SavedGameRequestStatus.Success) { Debug.LogWarning("[CloudSave] Upload failed: " + status); pending = true; }
            });
        });
    }

    // Called when the app goes to the background.
    public static void Flush()
    {
        if (pending || Time.realtimeSinceStartup - lastPushAt >= MinUploadGap) Push(force: true);
    }

    static void Open(Action<ISavedGameMetadata> done) =>
        Client.OpenWithAutomaticConflictResolution(SaveName, DataSource.ReadCacheOrNetwork,
            ConflictResolutionStrategy.UseMostRecentlySaved, (status, game) =>
            {
                if (status == SavedGameRequestStatus.Success) done(game);
                else { Debug.LogWarning("[CloudSave] Open failed: " + status); done(null); }
            });
#else
    public static bool Available => false;
    public static bool SignedIn => false;
    public static void Init() { }
    public static void SignIn() { }
    public static void Push(bool force = false) { }
    public static void Flush() { }
#endif
}
