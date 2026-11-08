using System;
using UnityEngine;
#if !UNITY_EDITOR && UNITY_ANDROID
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
#endif

// Google AdMob (Google Mobile Ads Unity plugin 11.5.0, in Assets/GoogleMobileAds) with few ads:
// - Rewarded: only when the player asks for one ("+1 FREE" slot, continue after losing, Free Coins in the shop).
// - Interstitial: only after a win, from level FirstInterstitialLevel on, at most once every InterstitialEveryWins
//   wins and never within MinGapSeconds of another full-screen ad. Never after a loss.
// - No banners and no app-open ads.
// The IDs are Google's public TEST IDs (they always serve test ads and pay nothing). Before release put the real
// App ID in Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset and the real ad unit IDs below.
// Consent (GDPR/EEA) goes through Google's UMP form before any ad is requested.
// In the Editor and desktop builds there is no SDK: rewarded ads succeed at once and no interstitial shows.
// Remove Ads (Progress.NoAds): no interstitials at all; GameUI gives rewards without an ad (a few a day).
// Testing "no ad available": a file named ads_off_test in Application.persistentDataPath (on a phone:
//   adb shell touch /sdcard/Android/data/com.zekoclub.kitetangle/files/ads_off_test
// then restart the app), or -noads on a desktop build, makes every ad fail to load, so the coin options can be checked.
public static class Ads
{
    public static readonly bool TestNoFill = DetectTestNoFill();

    static bool DetectTestNoFill()
    {
        try
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noads") >= 0) return true;
            return System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath, "ads_off_test"));
        }
        catch (Exception) { return false; }
    }

    const string RewardedUnitId = "ca-app-pub-3940256099942544/5224354917";      // TEST rewarded
    const string InterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";  // TEST interstitial

    public const int FirstInterstitialLevel = 10;
    public const int InterstitialEveryWins = 4;
    public const float MinGapSeconds = 180f;

    static int winsSinceInterstitial;
    static float lastFullScreenAt = -MinGapSeconds;

    // Called after every win. True when an interstitial may show now (the rules above).
    static bool InterstitialDue(int level)
    {
        if (Progress.NoAds) return false;
        winsSinceInterstitial++;
        return level >= FirstInterstitialLevel && winsSinceInterstitial >= InterstitialEveryWins
               && Time.realtimeSinceStartup - lastFullScreenAt >= MinGapSeconds;
    }

#if !UNITY_EDITOR && UNITY_ANDROID
    static bool started, initialised, loadingRewarded, loadingInterstitial;
    static RewardedAd rewarded;
    static InterstitialAd interstitial;
    static float retryRewardedAt;

    // Call once at startup: asks for consent where the law needs it, then starts the SDK.
    public static void Init()
    {
        if (started) return;
        started = true;
        if (TestNoFill) Debug.LogWarning("[Ads] ads_off_test: every ad will fail to load (testing).");
        ConsentInformation.Update(new ConsentRequestParameters(), updateError =>
        {
            if (updateError != null) Debug.LogWarning("[Ads] Consent update failed: " + updateError.Message);
            ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
            {
                if (formError != null) Debug.LogWarning("[Ads] Consent form: " + formError.Message);
                if (ConsentInformation.CanRequestAds()) StartSdk();
            });
        });
        if (ConsentInformation.CanRequestAds()) StartSdk(); // consent from an earlier session
    }

    static void StartSdk()
    {
        if (initialised) return;
        initialised = true;
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
        MobileAds.Initialize(_ => { LoadRewarded(); LoadInterstitial(); });
    }

    // Settings shows a "Privacy choices" button when this is true (EEA users must be able to change consent).
    public static bool PrivacyOptionsRequired =>
        ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

    public static void ShowPrivacyOptions() => ConsentForm.ShowPrivacyOptionsForm(error =>
    {
        if (error != null) Debug.LogWarning("[Ads] Privacy options: " + error.Message);
        if (ConsentInformation.CanRequestAds()) StartSdk();
    });

    static void LoadRewarded()
    {
        if (TestNoFill || !initialised || loadingRewarded || (rewarded != null && rewarded.CanShowAd())) return;
        loadingRewarded = true;
        RewardedAd.Load(RewardedUnitId, new AdRequest(), (ad, error) =>
        {
            loadingRewarded = false;
            if (error != null || ad == null)
            {
                Debug.LogWarning("[Ads] Rewarded failed to load: " + (error?.GetMessage() ?? "no ad"));
                retryRewardedAt = Time.realtimeSinceStartup + 30f;
                return;
            }
            rewarded = ad;
        });
    }

    static void LoadInterstitial()
    {
        if (TestNoFill || Progress.NoAds || !initialised || loadingInterstitial || (interstitial != null && interstitial.CanShowAd())) return;
        loadingInterstitial = true;
        InterstitialAd.Load(InterstitialUnitId, new AdRequest(), (ad, error) =>
        {
            loadingInterstitial = false;
            if (error != null || ad == null)
            {
                Debug.LogWarning("[Ads] Interstitial failed to load: " + (error?.GetMessage() ?? "no ad"));
                return;
            }
            interstitial = ad;
        });
    }

    public static bool RewardedReady
    {
        get
        {
            bool ready = rewarded != null && rewarded.CanShowAd();
            if (!ready && Time.realtimeSinceStartup >= retryRewardedAt) LoadRewarded();
            return ready;
        }
    }

    // onDone(true) only when Google confirms the reward was earned. Game sound is paused while the ad plays.
    public static void ShowRewarded(string placement, Action<bool> onDone)
    {
        var ad = rewarded;
        if (ad == null || !ad.CanShowAd()) { onDone?.Invoke(false); LoadRewarded(); return; }
        rewarded = null;
        bool earned = false, finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            AudioListener.pause = false;
            lastFullScreenAt = Time.realtimeSinceStartup;
            ad.Destroy();
            LoadRewarded();
            onDone?.Invoke(earned);
        }
        ad.OnAdFullScreenContentClosed += Finish;
        ad.OnAdFullScreenContentFailed += error => { Debug.LogWarning($"[Ads] Rewarded '{placement}' could not show: {error.GetMessage()}"); Finish(); };
        AudioListener.pause = true;
        ad.Show(_ => earned = true);
    }

    // After a win: shows an interstitial if one is due and loaded. onDone always runs (after the ad closes).
    public static void AfterWin(int level, Action onDone)
    {
        var ad = interstitial;
        if (!InterstitialDue(level) || ad == null || !ad.CanShowAd())
        {
            LoadInterstitial();
            onDone?.Invoke();
            return;
        }
        interstitial = null;
        winsSinceInterstitial = 0;
        bool finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            AudioListener.pause = false;
            lastFullScreenAt = Time.realtimeSinceStartup;
            ad.Destroy();
            LoadInterstitial();
            onDone?.Invoke();
        }
        ad.OnAdFullScreenContentClosed += Finish;
        ad.OnAdFullScreenContentFailed += error => { Debug.LogWarning("[Ads] Interstitial could not show: " + error.GetMessage()); Finish(); };
        AudioListener.pause = true;
        ad.Show();
    }
#else
    public static void Init() { }
    public static bool PrivacyOptionsRequired => false;
    public static void ShowPrivacyOptions() { }
    public static bool RewardedReady => !TestNoFill;

    public static void ShowRewarded(string placement, Action<bool> onDone)
    {
        Debug.Log($"[Ads] (no SDK here) rewarded '{placement}' " + (TestNoFill ? "has no ad (-noads)." : "granted."));
        onDone?.Invoke(!TestNoFill);
    }

    public static void AfterWin(int level, Action onDone)
    {
        if (InterstitialDue(level)) { winsSinceInterstitial = 0; Debug.Log($"[Ads] (no SDK here) interstitial would show after level {level}."); }
        onDone?.Invoke();
    }
#endif
}
