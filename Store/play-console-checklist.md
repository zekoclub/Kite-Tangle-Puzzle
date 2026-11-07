# Google Play release checklist: Kite Tangle

## 1. One-time setup
- [ ] Final app name in `listing.md` (and `PlayerSettings.productName` in `Assets/Scripts/Editor/Builder.cs` if it changes).
- [ ] Upload keystore: create `Keys/upload.keystore` and `Keys/keystore.properties` (steps in the docs, `projects-docs/kite-tangle/11-signing-and-release.md`). Back up `Keys/` somewhere safe; never commit or share it.
- [ ] Host `privacy-policy.html` at a public URL, for example https://zekoclub.com/kite-tangle-privacy.html.
- [ ] Host `app-ads.txt` at https://zekoclub.com/app-ads.txt (one file per website; it already has the ZekoClub AdMob line).

## 2. Ads (before the production release)
The game uses Google's TEST ad IDs now. Test ads are safe to tap; real ads on your own phone are not.
- [ ] In AdMob (https://admob.google.com): Apps > Add app > Android, "Kite Tangle". Copy the **App ID** (`ca-app-pub-...~...`).
- [ ] Create two ad units: **Rewarded** and **Interstitial**. Copy their IDs (`ca-app-pub-.../...`).
- [ ] Send the 3 IDs to Claude, or put them in yourself:
  - App ID: `Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset` (`adMobAndroidAppId`)
  - Unit IDs: `RewardedUnitId` and `InterstitialUnitId` in `Assets/Scripts/Ads.cs`
- [ ] AdMob > Privacy & messaging: create a **GDPR consent message** for the app (the game shows it through Google's UMP form, only in the EEA/UK).
- [ ] After the app is live: link it to the Play listing in AdMob.

How few ads: rewarded videos only when the player asks for one (+1 slot, continue, free coins); one full-screen ad at most every 4 wins, never before level 10, never after a loss, at least 3 minutes apart. No banners, no app-open ads.

## 3. Build the release bundle
Close the Unity Editor, then from the project folder:

    ~/Unity/Hub/Editor/6000.0.84f1/Editor/Unity -batchmode -nographics -quit -projectPath . \
      -buildTarget Android -executeMethod Builder.PerformBuild \
      -buildOutput Builds/KiteTangle-1.0-3.aab -versionCode 3 -versionName 1.0 -logFile tmp/build-aab.log

Every upload needs a higher `-versionCode`. The build stops if the upload key or the baked levels are missing.

## 4. Play Console: App content
- [ ] Privacy policy: the hosted URL
- [ ] Ads: **Yes, my app contains ads**
- [ ] App access: all functionality is available without special access
- [ ] Content rating: answers in `listing.md` (expect Everyone / PEGI 3)
- [ ] Target audience: 13+ (see `listing.md`)
- [ ] News app: No. Government app: No. Financial features: None. Health: No.
- [ ] Advertising ID: **Yes**, used for advertising (AdMob)
- [ ] Data safety:
  - Data collected: **Device or other IDs** (advertising ID), **App activity: app interactions**, **App info and performance: diagnostics** - all by the AdMob SDK, for **advertising** and **analytics**, shared with Google
  - Data encrypted in transit: **Yes**
  - Users can request deletion: **No** (the game stores progress only on the phone; uninstalling deletes it)
  - The game itself collects nothing else (no account, no name, no location)

## 5. Store listing
- [ ] Texts from `listing.md`, icon `play-icon-512.png`, feature graphic, 2-8 screenshots from `screenshots/`

## 6. Testing and release
- [ ] Upload the .aab to **Internal testing** first and install it from the Play link on your phone
- [ ] **Closed testing**: at least 12 testers (Gmail addresses) opted in for 14 days in a row (needed for a personal developer account)
- [ ] Then "Apply for production access", and promote the release to **Production**
