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
// - Auto backup (Progress.AutoBackup, on by default, Settings): uploads follow Progress.Changed at most every
//   MinUploadGap seconds, and always when the app goes to the background. When it is off, nothing is uploaded
//   until the player taps "Back up now" (BackUpNow). Restoring after sign-in happens either way.
// In the Editor and desktop builds there is no Play Games: Available is false and nothing happens.
public static class CloudSave
{
    const string SaveName = "kite_tangle_progress";
    const float MinUploadGap = 30f;

#pragma warning disable CS0067 // only raised on Android
    public static event Action Changed; // sign-in state changed, or a backup finished
#pragma warning restore CS0067

    /// Screenshot tour on desktop: show the backup rows in Settings even though there is no Play Games here.
    public static bool Preview;

#if !UNITY_EDITOR && UNITY_ANDROID
    static bool started, signedIn, loaded, busy, pending;
    static float lastPushAt = -MinUploadGap;
    static Action<bool> manualDone; // "Back up now" waiting for its upload

    // True when this build has a Play Games App ID.
    public static bool Available => Preview || PlayGamesSettings.LoadInstance() is { } settings && !string.IsNullOrEmpty(settings.AppId);
    public static bool SignedIn => signedIn;
    public static bool Busy => busy;

    static ISavedGameClient Client => PlayGamesPlatform.Instance.SavedGame;

    // Call once at startup.
    public static void Init()
    {
        if (started || !Available) return;
        started = true;
        PlayGamesPlatform.Activate();
        PlayGamesPlatform.Instance.Authenticate(OnSignIn);
        Progress.Changed += () => { if (Progress.AutoBackup) Push(); };
    }

    // The "Back up now" button: uploads at once (also with auto backup off). done(true) when it is saved.
    public static void BackUpNow(Action<bool> done)
    {
        if (!signedIn) { SignIn(); done?.Invoke(false); return; }
        if (!loaded) { done?.Invoke(false); return; } // still reading the cloud save; uploading now could overwrite it
        manualDone = done;
        Push(force: true, manual: true);
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
                if (Progress.AutoBackup) Push(force: true);
                Changed?.Invoke();
            });
        });
    }

    // Uploads this phone's progress. Without force it waits MinUploadGap between uploads (the next change or
    // going to the background sends what was skipped).
    public static void Push(bool force = false, bool manual = false)
    {
        if (!signedIn || !loaded) return;
        if (!manual && !Progress.AutoBackup) return;
        if (busy || (!force && Time.realtimeSinceStartup - lastPushAt < MinUploadGap))
        {
            pending = true;
            if (manual && busy) { manualDone?.Invoke(false); manualDone = null; }
            return;
        }
        busy = true;
        pending = false;
        lastPushAt = Time.realtimeSinceStartup;
        byte[] data = Encoding.UTF8.GetBytes(Progress.ToCloudJson());
        string description = $"Level {Progress.Level}";
        Open(game =>
        {
            if (game == null) { busy = false; pending = true; Finished(false); return; }
            var update = new SavedGameMetadataUpdate.Builder().WithUpdatedDescription(description).Build();
            Client.CommitUpdate(game, update, data, (status, _) =>
            {
                busy = false;
                bool ok = status == SavedGameRequestStatus.Success;
                if (!ok) { Debug.LogWarning("[CloudSave] Upload failed: " + status); pending = true; }
                else Progress.MarkBackedUp();
                Finished(ok);
            });
        });
    }

    static void Finished(bool ok)
    {
        manualDone?.Invoke(ok);
        manualDone = null;
        Changed?.Invoke();
    }

    // Called when the app goes to the background (auto backup only).
    public static void Flush()
    {
        if (!Progress.AutoBackup) return;
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
    public static bool Available => Preview;
    public static bool SignedIn => Preview;
    public static bool Busy => false;
    public static void Init() { }
    public static void SignIn() { }
    public static void Push(bool force = false, bool manual = false) { }
    public static void Flush() { }
    public static void BackUpNow(Action<bool> done) { if (Preview) Progress.MarkBackedUp(); done?.Invoke(Preview); } // desktop tour: pretend it worked
#endif
}
