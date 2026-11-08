using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Scripting;

// The "Remove Ads" in-app purchase, through Google Play Billing Library 8 called straight from C#
// (com.android.billingclient:billing in mainTemplate.gradle; no Unity IAP package).
// Product: a one-time "managed product" with ID RemoveAdsId, created in Play Console > Monetize > Products > In-app products.
// Until that product exists (and the app is installed from Play, e.g. internal testing), Play answers that it is
// unavailable and the shop says so. Owned purchases are fetched at every start, so a reinstall or new phone gets
// No Ads back, and a refunded purchase takes it away.
// Billing calls back on Android threads; every result is queued and handled on Unity's main thread.
// In the Editor and desktop builds there is no store: Buy grants No Ads at once (for testing the flow).
public static class Purchases
{
    public const string RemoveAdsId = "remove_ads";

    public static event Action Changed;     // price known, purchase done, or availability changed
    public static event Action<string> Message; // a line for the player (cancelled, pending, error)

    public static string RemoveAdsPrice { get; private set; } // formatted price from Play, null until known

    static readonly ConcurrentQueue<Action> mainThread = new();

    // Runs queued billing results on the main thread.
    class Pump : MonoBehaviour
    {
        void Update()
        {
            while (mainThread.TryDequeue(out var action)) action();
        }
    }

    static void OnMain(Action action) => mainThread.Enqueue(action);

    static void Grant()
    {
        bool had = Progress.NoAds;
        Progress.GrantNoAds();
        if (!had) { Sfx.Play(Sfx.Sound.Purchase); Message?.Invoke("Ads removed. Thank you!"); }
        Changed?.Invoke();
    }

#if !UNITY_EDITOR && UNITY_ANDROID
    const string Api = "com.android.billingclient.api.";
    const int Ok = 0, UserCanceled = 1, ItemAlreadyOwned = 7;
    const int Purchased = 1, Pending = 2;

    static AndroidJavaObject client, details;
    static bool started, connected;

    public static bool Available => details != null;

    static AndroidJavaObject Activity =>
        new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");

    static AndroidJavaObject Builder(string cls) => new AndroidJavaClass(Api + cls).CallStatic<AndroidJavaObject>("newBuilder");

    static AndroidJavaObject ListOf(AndroidJavaObject item)
    {
        var list = new AndroidJavaObject("java.util.ArrayList");
        list.Call<bool>("add", item);
        return list;
    }

    public static void Init()
    {
        if (started) return;
        started = true;
        new GameObject("Purchases").AddComponent<Pump>().gameObject.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var pending = Builder("PendingPurchasesParams").Call<AndroidJavaObject>("enableOneTimeProducts").Call<AndroidJavaObject>("build");
            var builder = new AndroidJavaClass(Api + "BillingClient").CallStatic<AndroidJavaObject>("newBuilder", Activity);
            builder.Call<AndroidJavaObject>("setListener", new UpdatedListener());
            builder.Call<AndroidJavaObject>("enablePendingPurchases", pending);
            builder.Call<AndroidJavaObject>("enableAutoServiceReconnection");
            client = builder.Call<AndroidJavaObject>("build");
            client.Call("startConnection", new StateListener());
        }
        catch (Exception e) { Debug.LogWarning("[Purchases] Billing unavailable: " + e.Message); }
    }

    public static void BuyRemoveAds()
    {
        if (Progress.NoAds) { Grant(); return; }
        if (details == null)
        {
            Message?.Invoke(connected ? "Remove Ads isn't available yet" : "Can't reach Google Play. Try again later");
            if (client != null && connected) { QueryProduct(); QueryPurchases(); }
            return;
        }
        var flow = Builder("BillingFlowParams")
            .Call<AndroidJavaObject>("setProductDetailsParamsList",
                ListOf(Builder("BillingFlowParams$ProductDetailsParams").Call<AndroidJavaObject>("setProductDetails", details).Call<AndroidJavaObject>("build")))
            .Call<AndroidJavaObject>("build");
        var activity = Activity;
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try
            {
                var result = client.Call<AndroidJavaObject>("launchBillingFlow", activity, flow);
                int code = result.Call<int>("getResponseCode");
                if (code == ItemAlreadyOwned) QueryPurchases();
                else if (code != Ok) OnMain(() => Message?.Invoke("Google Play couldn't start the purchase"));
            }
            catch (Exception e) { Debug.LogWarning("[Purchases] launchBillingFlow: " + e.Message); }
        }));
    }

    static void QueryProduct()
    {
        var product = Builder("QueryProductDetailsParams$Product")
            .Call<AndroidJavaObject>("setProductId", RemoveAdsId)
            .Call<AndroidJavaObject>("setProductType", "inapp")
            .Call<AndroidJavaObject>("build");
        var query = Builder("QueryProductDetailsParams").Call<AndroidJavaObject>("setProductList", ListOf(product)).Call<AndroidJavaObject>("build");
        client.Call("queryProductDetailsAsync", query, new DetailsListener());
    }

    static void QueryPurchases()
    {
        var query = Builder("QueryPurchasesParams").Call<AndroidJavaObject>("setProductType", "inapp").Call<AndroidJavaObject>("build");
        client.Call("queryPurchasesAsync", query, new OwnedListener());
    }

    // Grants No Ads for a completed purchase and acknowledges it (Play refunds unacknowledged purchases after 3 days).
    // Returns true when the list holds Remove Ads (bought or pending).
    static bool Handle(AndroidJavaObject purchases)
    {
        if (purchases == null) return false;
        bool found = false;
        int count = purchases.Call<int>("size");
        for (int i = 0; i < count; i++)
        {
            var purchase = purchases.Call<AndroidJavaObject>("get", i);
            var products = purchase.Call<AndroidJavaObject>("getProducts");
            if (!products.Call<bool>("contains", RemoveAdsId)) continue;
            found = true;
            int state = purchase.Call<int>("getPurchaseState");
            if (state == Purchased)
            {
                OnMain(Grant);
                if (!purchase.Call<bool>("isAcknowledged"))
                {
                    var ack = Builder("AcknowledgePurchaseParams")
                        .Call<AndroidJavaObject>("setPurchaseToken", purchase.Call<string>("getPurchaseToken"))
                        .Call<AndroidJavaObject>("build");
                    client.Call("acknowledgePurchase", ack, new AckListener());
                }
            }
            else if (state == Pending)
                OnMain(() => Message?.Invoke("Purchase pending. Ads go away once it completes"));
        }
        return found;
    }

    static int Code(AndroidJavaObject result) => result?.Call<int>("getResponseCode") ?? -1;

    class StateListener : AndroidJavaProxy
    {
        public StateListener() : base(Api + "BillingClientStateListener") { }

        [Preserve] public void onBillingSetupFinished(AndroidJavaObject result)
        {
            connected = Code(result) == Ok;
            if (!connected) { Debug.LogWarning("[Purchases] Billing setup failed: " + Code(result)); return; }
            QueryProduct();
            QueryPurchases();
        }

        [Preserve] public void onBillingServiceDisconnected() => connected = false; // the client reconnects by itself
    }

    class DetailsListener : AndroidJavaProxy
    {
        public DetailsListener() : base(Api + "ProductDetailsResponseListener") { }

        [Preserve] public void onProductDetailsResponse(AndroidJavaObject result, AndroidJavaObject query)
        {
            if (Code(result) != Ok || query == null) return;
            var list = query.Call<AndroidJavaObject>("getProductDetailsList");
            if (list == null || list.Call<int>("size") == 0)
            {
                Debug.LogWarning($"[Purchases] Product '{RemoveAdsId}' not found in Play Console.");
                return;
            }
            var product = list.Call<AndroidJavaObject>("get", 0);
            string price = product.Call<AndroidJavaObject>("getOneTimePurchaseOfferDetails")?.Call<string>("getFormattedPrice");
            OnMain(() => { details = product; RemoveAdsPrice = price; Changed?.Invoke(); });
        }
    }

    class UpdatedListener : AndroidJavaProxy
    {
        public UpdatedListener() : base(Api + "PurchasesUpdatedListener") { }

        [Preserve] public void onPurchasesUpdated(AndroidJavaObject result, AndroidJavaObject purchases)
        {
            int code = Code(result);
            if (code == Ok) Handle(purchases);
            else if (code == ItemAlreadyOwned) QueryPurchases();
            else if (code == UserCanceled) OnMain(() => Message?.Invoke("Purchase cancelled"));
            else OnMain(() => Message?.Invoke("Purchase failed. Please try again"));
        }
    }

    class OwnedListener : AndroidJavaProxy
    {
        public OwnedListener() : base(Api + "PurchasesResponseListener") { }

        [Preserve] public void onQueryPurchasesResponse(AndroidJavaObject result, AndroidJavaObject purchases)
        {
            if (Code(result) != Ok) return;
            // Play is the record of what was bought: no Remove Ads in the list = not owned (or refunded).
            if (!Handle(purchases)) OnMain(() => { if (Progress.NoAds) { Progress.RevokeNoAds(); Changed?.Invoke(); } });
        }
    }

    class AckListener : AndroidJavaProxy
    {
        public AckListener() : base(Api + "AcknowledgePurchaseResponseListener") { }

        [Preserve] public void onAcknowledgePurchaseResponse(AndroidJavaObject result)
        {
            if (Code(result) != Ok) Debug.LogWarning("[Purchases] Acknowledge failed: " + Code(result));
        }
    }
#else
    public static bool Available => true;

    public static void Init()
    {
        RemoveAdsPrice = "$1.99";
        new GameObject("Purchases").AddComponent<Pump>().gameObject.hideFlags = HideFlags.HideAndDontSave;
    }

    public static void BuyRemoveAds()
    {
        Debug.Log("[Purchases] (no store here) Remove Ads granted.");
        Grant();
    }
#endif
}
