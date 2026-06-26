using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;

public class Logic : MonoBehaviour,
                    IAptoideBillingClientStateListener,
                    IConsumeResponseListener,
                    IPurchasesUpdatedListener,
                    IProductDetailsResponseListener,
                    IPurchasesResponseListener,
                    IAcknowledgeResponseListener,      // NEW
                    IAptoideSignInResponseListener,    // NEW
                    IAptoideAccountStateListener       // NEW
{
    [SerializeField]
    private int _startingAttempts = 3;
    [SerializeField]
    private UIDice _dice;
    [SerializeField]
    private Button _btnRoll;
    [SerializeField]
    private Button _btnBuySDK;
    [SerializeField]
    private Button _btnSubsSDK;
    [SerializeField]
    private TMP_Text _txtAttempts;
    [SerializeField]
    private TMP_InputField _numberInput;
    [SerializeField]
    private TMP_Text _txtResult;

    public const string ATTEMPTS_KEY = "Attempts";
    private int _currentAttempts = 0;

    // ---- Product ids ----
    private const string SkuAttempts = "attempts";
    private const string SkuGoldenDice = "golden_dice";       // subs (existing entitlement, untouched)
    private const string SkuLegendaryDice = "legendary_dice"; // inapp non-consumable (rainbow skin)

    // Product types
    private const string ProductTypeInapp = "inapp";
    private const string ProductTypeSubs = "subs";

    // ---- Billing response codes ----
    private const int ResponseOk = 0;
    private const int ResponseUserCanceled = 1;
    private const int ResponseServiceUnavailable = 2;
    private const int ResponseFeatureNotSupported = -2;

    // ---- Feature flags (IsFeatureSupported) ----
    private const int FeatureSubscriptions = 0;
    private const int FeatureAccountSignIn = 3;
    private const int FeatureAcknowledge = 4;

    // ---- Account state ----
    private const int AccountSignedOut = 0;
    private const int AccountSignedIn = 1;

    // ---- Purchase state ----
    private const int PurchaseStatePurchased = 1;

    // ---- Skins / entitlements ----
    private enum Skin { Golden, Rainbow }
    private const string SkinGoldenKey = "skin_golden_owned";
    private const string SkinRainbowKey = "skin_rainbow_owned";
    private bool _ownedGolden;
    private bool _ownedRainbow;

    private static readonly Color GoldColor = new Color(168f / 255f, 125f / 255f, 5f / 255f);
    private static readonly Color DefaultDiceColor = Color.white;
    private static readonly Color[] RainbowColors =
    {
        new Color(1f, 0f, 0f),       // red
        new Color(1f, 0.5f, 0f),     // orange
        new Color(1f, 1f, 0f),       // yellow
        new Color(0f, 0.8f, 0f),     // green
        new Color(0f, 0.4f, 1f),     // blue
        new Color(0.6f, 0f, 1f)      // violet
    };

    // Products that must be acknowledged (not consumed) when purchased / restored.
    private static readonly HashSet<string> NonConsumableSkus = new HashSet<string> { SkuLegendaryDice };

    // Acknowledge bookkeeping (Purchase exposes no "isAcknowledged" flag, so track locally).
    private readonly Dictionary<string, string> _ackTokenToSku = new Dictionary<string, string>();
    private readonly HashSet<string> _acknowledgedTokens = new HashSet<string>();
    private readonly Dictionary<string, int> _ackRetryCounts = new Dictionary<string, int>();
    private const int MaxAcknowledgeRetries = 3;
    private const float AcknowledgeRetryDelaySeconds = 3f;

    // Serialize purchase queries so OnQueryPurchasesResponse can tell which product type
    // each response covers (the wrapper does not pass the product type back).
    private readonly Queue<string> _purchaseQueryQueue = new Queue<string>();
    private bool _purchaseQueryInFlight;

    // ---- Aptoide settings block (built at runtime) ----
    private GameObject _aptoideBlock;
    private Button _btnAptoideAccount;
    private TMP_Text _txtAptoideAccount;
    private Button _btnBuyLegendary;
    private TMP_Text _txtBuyLegendary;

    private static List<QueryProductDetailsParams.Product> inappProducts =
                           new List<QueryProductDetailsParams.Product>() {
                               QueryProductDetailsParams.Product.NewBuilder()
                                   .SetProductId(SkuAttempts)
                                   .SetProductType(ProductTypeInapp)
                                   .Build(),
                               QueryProductDetailsParams.Product.NewBuilder()
                                   .SetProductId(SkuLegendaryDice)
                                   .SetProductType(ProductTypeInapp)
                                   .Build()
                           };
    private static List<QueryProductDetailsParams.Product> subsProducts =
                           new List<QueryProductDetailsParams.Product>() {
                               QueryProductDetailsParams.Product.NewBuilder()
                                   .SetProductId(SkuGoldenDice)
                                   .SetProductType(ProductTypeSubs)
                                   .Build()
                           };

    // Start is called before the first frame update
    void Start()
    {
        if (PlayerPrefs.HasKey(ATTEMPTS_KEY))
        {
            _currentAttempts = PlayerPrefs.GetInt(ATTEMPTS_KEY, 0);
        }
        else
        {
            _currentAttempts = _startingAttempts;
        }

        UpdateAttemptsUI();

        // Restore locally-cached skin ownership for an immediate visual; the server
        // purchase queries (OnBillingSetupFinished -> RefreshAllPurchases) reconcile afterwards.
        _ownedGolden = PlayerPrefs.GetInt(SkinGoldenKey, 0) == 1;
        _ownedRainbow = PlayerPrefs.GetInt(SkinRainbowKey, 0) == 1;
        ApplyDiceSkin();

        _btnRoll.onClick.AddListener(OnRollDicePressed);
        _btnBuySDK.onClick.AddListener(OnBuySDKPressed);
        _btnSubsSDK.onClick.AddListener(OnSubsSDKPressed);

        AptoideBillingSDKManager.InitializePlugin(
            this, // billing client state listener
            this, // consume response listener
            this, // purchases updated listener
            this, // product details response listener
            this, // purchases response listener
            this, // acknowledge response listener      (NEW)
            this, // sign-in response listener           (NEW)
            this, // account state listener              (NEW)
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAzIR0OxCJDzaF2PvcymkPvG9PQTCVkGPxG5eLt5ZcIBftWKl6nFmgItAyYm2ixOrpNUOHjtuTOXuaMMABV91Y6CitQujsr0O76PsHduY0jG2j32wJAIluzspkzKS6sBp4MZvfG/ctUaqjDibYuvRZtE3Wv7kY7zH/lwKmD+BnGScFc8YTJUOlcRdqXtIPbX9Je2h5PtLUNmiLzcnjKxJ7dwsSc/QEuVXSY7k/jFkjIsv62EaLEcMtJrbuL+jvLg6/MpK2REuinLrkG9xK2JjgK9xhW6D7pEvQb/Dj3YFk0RbaP7EITsnrQaqZ1pL9aAEDzeG3qcsJSU2cn/wfGgZodwIDAQAB",
            this.gameObject.name);

        // Aptoide account settings block (sign-in / logout) + a buy entry for the
        // legendary (rainbow) non-consumable, built programmatically under the Canvas.
        BuildAptoideSettingsBlock();
        BindAptoideSettings();
    }

    private void OnRollDicePressed()
    {
        if (_currentAttempts > 0)
        {
            _currentAttempts--;
            UpdateAttemptsUI();

            int diceValue = Random.Range(1, 7); // Generate a random number between 1 and 6
            _dice.SetValue(diceValue); // Assuming _dice.SetValue updates the dice face

            // Check if the input number matches the dice value
            if (int.TryParse(_numberInput.text, out int inputNumber) && inputNumber == diceValue)
            {
                _currentAttempts = _startingAttempts; // Reset attempts
                UpdateAttemptsUI();
                ShowToast("Correct");
            }
            else if (_currentAttempts == 0)
            {
                ShowToast("No more attempts, purchase now.");
            }
        }
        else
        {
            ShowToast("No more attempts, purchase now.");
        }
    }

    private void OnBuySDKPressed()
    {
        ShowToast("Buy inapp purchase.");
        LaunchPurchase(SkuAttempts, ProductTypeInapp);
    }

    private void OnSubsSDKPressed()
    {
        ShowToast("Subscribe SDK button pressed.");
        LaunchPurchase(SkuGoldenDice, ProductTypeSubs);
    }

    private void OnBuyLegendaryDicePressed()
    {
        ShowToast("Buy legendary dice (non-consumable).");
        LaunchPurchase(SkuLegendaryDice, ProductTypeInapp);
    }

    private void LaunchPurchase(string productId, string productType)
    {
        ProductDetails productDetails = new ProductDetails();
        productDetails.ProductId = productId;
        productDetails.ProductType = productType;

        BillingFlowParams billingFlowParams =
            BillingFlowParams.NewBuilder()
                .SetProductDetailsParamsList(
                    new List<BillingFlowParams.ProductDetailsParams>()
                    {
                        BillingFlowParams.ProductDetailsParams.NewBuilder()
                            .SetProductDetails(productDetails)
                            .Build()
                    }
                )
                .SetDeveloperPayload("developerPayload")
                .SetObfuscatedAccountId("12345")
                .Build();

        AptoideBillingSDKManager.LaunchBillingFlow(billingFlowParams);
    }

    private void UpdateAttemptsUI()
    {
        PlayerPrefs.SetInt(ATTEMPTS_KEY, _currentAttempts);
        _txtAttempts.text = _currentAttempts.ToString();
    }

    private IEnumerator ValidatePurchase(Purchase purchase, bool isDebugVersion = false)
    {
        string url = $"https://sdk.diceroll.catappult.io/validate/{purchase.PackageName}/{purchase.Products[0]}/{purchase.PurchaseToken}";
        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            // Send the request and wait for a response
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Validation failed for purchase: {purchase.Products[0]}. Error: {webRequest.error}");
            }
            else
            {
                // Parse the response
                string responseText = webRequest.downloadHandler.text.Trim();
                Debug.Log($"Validation response for {purchase.Products[0]}: {responseText}");

                // Check if the response is "true" or "false"
                if (isDebugVersion || responseText == "true")
                {
                    string sku = purchase.Products[0];
                    Debug.Log($"Purchase validated successfully for {sku}. Finalizing the purchase...");

                    // Finalize: acknowledge non-consumables, consume everything else.
                    FinalizePurchase(purchase);

                    if (NonConsumableSkus.Contains(sku))
                    {
                        // Entitlement is granted only once the acknowledge succeeds
                        // (see OnAcknowledgeResponse -> GrantEntitlement).
                        Debug.Log("Non-consumable purchased; awaiting acknowledge before granting skin.");
                    }
                    else if (sku == SkuGoldenDice)
                    {
                        Debug.Log("Subscription purchased.");
                        GrantSkin(Skin.Golden);
                    }
                    else
                    {
                        Debug.Log("Item purchased.");
                        _currentAttempts = _startingAttempts;
                        UpdateAttemptsUI();
                    }
                }
                else if (responseText == "false")
                {
                    Debug.LogError($"Purchase validation failed for {purchase.Products[0]}.");
                }
                else
                {
                    Debug.LogError($"Unexpected response for purchase validation: {responseText}");
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Feature 1 — Finalize purchases (acknowledge vs. consume)
    // ---------------------------------------------------------------------

    private void FinalizePurchase(Purchase purchase)
    {
        bool isNonConsumable = purchase.Products != null
                               && purchase.Products.Any(sku => NonConsumableSkus.Contains(sku));
        if (isNonConsumable)
            Acknowledge(purchase);
        else
            Consume(purchase); // existing consumable / subscription flow, unchanged
    }

    private void Consume(Purchase purchase)
    {
        ConsumeParams consumeParams =
            ConsumeParams.NewBuilder()
                .SetPurchaseToken(purchase.PurchaseToken)
                .Build();
        AptoideBillingSDKManager.ConsumeAsync(consumeParams);
    }

    private void Acknowledge(Purchase purchase)
    {
        if (string.IsNullOrEmpty(purchase.PurchaseToken))
            return;

        if (purchase.Products != null && purchase.Products.Length > 0)
            _ackTokenToSku[purchase.PurchaseToken] = purchase.Products[0];

        AcknowledgeParams acknowledgeParams =
            AcknowledgeParams.NewBuilder()
                .SetPurchaseToken(purchase.PurchaseToken)
                .Build();
        AptoideBillingSDKManager.AcknowledgeAsync(acknowledgeParams);
    }

    // Re-acknowledge a restored non-consumable only if we have not already done so this session.
    private void EnsureAcknowledged(Purchase purchase)
    {
        if (string.IsNullOrEmpty(purchase.PurchaseToken))
            return;
        if (_acknowledgedTokens.Contains(purchase.PurchaseToken))
            return;
        Acknowledge(purchase);
    }

    public void OnAcknowledgeResponse(BillingResult billingResult, string purchaseToken)
    {
        switch (billingResult.ResponseCode)
        {
            case ResponseOk: // finalized & still owned -> unlock rainbow skin
                GrantEntitlement(purchaseToken);
                break;
            case ResponseServiceUnavailable: // not connected / config still loading -> retry later
                ScheduleAcknowledgeRetry(purchaseToken);
                break;
            case ResponseFeatureNotSupported: // backend hasn't enabled acknowledge yet
                Debug.LogWarning("Acknowledge not enabled by backend; cannot finalize non-consumable yet");
                break;
            default:
                Debug.LogError($"Acknowledge failed: {billingResult.DebugMessage}");
                break;
        }
    }

    private void GrantEntitlement(string purchaseToken)
    {
        _acknowledgedTokens.Add(purchaseToken);
        _ackRetryCounts.Remove(purchaseToken);

        // The only non-consumable is legendary_dice (rainbow). Map the token if we know it,
        // otherwise fall back to rainbow (e.g. an acknowledge issued during restore).
        if (_ackTokenToSku.TryGetValue(purchaseToken, out string sku))
        {
            if (sku == SkuLegendaryDice)
                GrantSkin(Skin.Rainbow);
        }
        else
        {
            GrantSkin(Skin.Rainbow);
        }
    }

    private void ScheduleAcknowledgeRetry(string purchaseToken)
    {
        int attempts = _ackRetryCounts.TryGetValue(purchaseToken, out int count) ? count : 0;
        if (attempts >= MaxAcknowledgeRetries)
        {
            Debug.LogError($"Acknowledge retries exhausted for token {purchaseToken}");
            return;
        }
        _ackRetryCounts[purchaseToken] = attempts + 1;
        StartCoroutine(AcknowledgeRetryCoroutine(purchaseToken));
    }

    private IEnumerator AcknowledgeRetryCoroutine(string purchaseToken)
    {
        yield return new WaitForSeconds(AcknowledgeRetryDelaySeconds);
        AcknowledgeParams acknowledgeParams =
            AcknowledgeParams.NewBuilder()
                .SetPurchaseToken(purchaseToken)
                .Build();
        AptoideBillingSDKManager.AcknowledgeAsync(acknowledgeParams);
    }

    // ---------------------------------------------------------------------
    //  Feature 2c — Re-query purchases for both product types and reconcile
    // ---------------------------------------------------------------------

    public void RefreshAllPurchases()
    {
        EnqueuePurchaseQuery(ProductTypeInapp); // legendary_dice (rainbow), etc.
        EnqueuePurchaseQuery(ProductTypeSubs);  // golden_dice, etc.
    }

    private void EnqueuePurchaseQuery(string productType)
    {
        _purchaseQueryQueue.Enqueue(productType);
        DrainPurchaseQueryQueue();
    }

    private void DrainPurchaseQueryQueue()
    {
        if (_purchaseQueryInFlight || _purchaseQueryQueue.Count == 0)
            return;

        _purchaseQueryInFlight = true;
        string productType = _purchaseQueryQueue.Peek();
        QueryPurchasesParams queryPurchasesParams =
            QueryPurchasesParams.NewBuilder()
                .SetProductType(productType)
                .Build();
        AptoideBillingSDKManager.QueryPurchasesAsync(queryPurchasesParams);
    }

    public void OnQueryPurchasesResponse(BillingResult billingResult, Purchase[] purchases)
    {
        // Front of the queue is the query this response answers (queries are serialized).
        string productType = _purchaseQueryQueue.Count > 0 ? _purchaseQueryQueue.Dequeue() : null;
        _purchaseQueryInFlight = false;

        if (billingResult.ResponseCode == ResponseOk)
        {
            ReconcileEntitlements(productType, purchases);
        }
        else
        {
            Debug.LogError($"Query purchases failed: {billingResult.DebugMessage}");
            ShowToast("Failed to update purchases.");
        }

        // Kick off the next queued query (e.g. subs after inapp).
        DrainPurchaseQueryQueue();
    }

    // Idempotent reconciliation. Grants skins for SKUs present in the result and revokes
    // skins whose SKU is no longer present. Only revokes for the product type this response
    // actually covers, so the inapp response never revokes a subs entitlement and vice-versa.
    private void ReconcileEntitlements(string productType, Purchase[] purchases)
    {
        HashSet<string> present = new HashSet<string>();
        if (purchases != null)
        {
            foreach (Purchase purchase in purchases)
            {
                if (purchase.PurchaseState != PurchaseStatePurchased || purchase.Products == null)
                    continue;

                foreach (string sku in purchase.Products)
                    present.Add(sku);

                // A non-consumable that is neither consumed nor acknowledged is auto-refunded,
                // so make sure restored non-consumables are acknowledged.
                if (purchase.Products.Any(sku => NonConsumableSkus.Contains(sku)))
                    EnsureAcknowledged(purchase);
            }
        }

        // golden_dice -> subs entitlement
        if (productType == null || productType == ProductTypeSubs)
        {
            if (present.Contains(SkuGoldenDice))
                GrantSkin(Skin.Golden);
            else if (productType == ProductTypeSubs)
                RevokeSkin(Skin.Golden);
        }

        // legendary_dice -> inapp non-consumable entitlement
        if (productType == null || productType == ProductTypeInapp)
        {
            if (present.Contains(SkuLegendaryDice))
                GrantSkin(Skin.Rainbow);
            else if (productType == ProductTypeInapp)
                RevokeSkin(Skin.Rainbow);
        }
    }

    // ---------------------------------------------------------------------
    //  Skins / entitlement application
    // ---------------------------------------------------------------------

    private void GrantSkin(Skin skin)
    {
        PlayerPrefs.SetInt(SkinKey(skin), 1);
        if (skin == Skin.Golden) _ownedGolden = true; else _ownedRainbow = true;
        ApplyDiceSkin();
        Debug.Log($"Granted skin: {skin}");
    }

    private void RevokeSkin(Skin skin)
    {
        PlayerPrefs.SetInt(SkinKey(skin), 0);
        if (skin == Skin.Golden) _ownedGolden = false; else _ownedRainbow = false;
        ApplyDiceSkin();
        Debug.Log($"Revoked skin: {skin}");
    }

    private static string SkinKey(Skin skin)
    {
        return skin == Skin.Golden ? SkinGoldenKey : SkinRainbowKey;
    }

    // Rainbow takes precedence over golden when both are owned; otherwise restore the default.
    private void ApplyDiceSkin()
    {
        if (_dice == null)
            return;

        if (_ownedRainbow)
            PaintRainbow();
        else if (_ownedGolden)
            PaintDice(GoldColor);
        else
            PaintDice(DefaultDiceColor);
    }

    private void PaintDice(Color color)
    {
        Image diceImage = _dice.GetComponent<Image>();
        if (diceImage != null)
            diceImage.color = color;

        for (int faceIndex = 1; faceIndex <= 5; faceIndex++)
            PaintFace(faceIndex, color);
    }

    private void PaintRainbow()
    {
        Image diceImage = _dice.GetComponent<Image>();
        if (diceImage != null)
            diceImage.color = RainbowColors[0];

        for (int faceIndex = 1; faceIndex <= 5; faceIndex++)
            PaintFace(faceIndex, RainbowColors[(faceIndex - 1) % RainbowColors.Length]);
    }

    private void PaintFace(int faceIndex, Color color)
    {
        Transform face = _dice.transform.Find(faceIndex.ToString());
        if (face == null)
            return;

        foreach (Transform child in face)
        {
            Image image = child.GetComponent<Image>();
            if (image != null)
                image.color = color;
        }
    }

    // ---------------------------------------------------------------------
    //  Feature 2a/2b — Aptoide settings block (sign-in / logout)
    // ---------------------------------------------------------------------

    public void BindAptoideSettings()
    {
        if (_aptoideBlock == null)
            return;

        // Optionally hide the whole block when account sign-in is hard-disabled by the backend.
        // SERVICE_UNAVAILABLE (config still loading) keeps the block visible and is re-evaluated
        // from OnBillingSetupFinished / OnAccountStateChanged.
        int signInSupport = AptoideBillingSDKManager.IsFeatureSupported(FeatureAccountSignIn).ResponseCode;
        bool hidden = signInSupport == ResponseFeatureNotSupported;
        _aptoideBlock.SetActive(!hidden);
        if (hidden)
            return;

        bool signedIn = AptoideBillingSDKManager.IsSignedInToAptoideServices();

        // Swap the single account button between Sign-in and Logout.
        _btnAptoideAccount.onClick.RemoveListener(OnSignInClicked);
        _btnAptoideAccount.onClick.RemoveListener(OnLogoutClicked);
        if (signedIn)
        {
            _txtAptoideAccount.text = "Logout from Aptoide Services";
            _btnAptoideAccount.onClick.AddListener(OnLogoutClicked);
        }
        else
        {
            _txtAptoideAccount.text = "Sign in to Aptoide Services";
            _btnAptoideAccount.onClick.AddListener(OnSignInClicked);
        }
    }

    public void OnSignInClicked()
    {
        AptoideBillingSDKManager.SignInToAptoideServices();
    }

    public void OnLogoutClicked()
    {
        AptoideBillingSDKManager.SignOutFromAptoideServices(); // async; OnAccountStateChanged fires SIGNED_OUT
    }

    public void OnSignInResponse(BillingResult billingResult)
    {
        switch (billingResult.ResponseCode)
        {
            // On OK the account-state listener refreshes the UI + purchases — nothing to do here.
            case ResponseOk: break;
            case ResponseUserCanceled: Debug.Log("Sign-in dismissed"); break;
            case ResponseFeatureNotSupported: Debug.Log("Sign-in disabled by backend"); break;
            case ResponseServiceUnavailable: Debug.Log("Not ready yet — retry shortly"); break;
            default: Debug.LogError($"Sign-in error: {billingResult.DebugMessage}"); break;
        }
    }

    public void OnAccountStateChanged(int state)
    {
        Debug.Log($"Account state changed: {(state == AccountSignedIn ? "SIGNED_IN" : "SIGNED_OUT")}");
        BindAptoideSettings();   // swap Sign-in <-> Logout button
        RefreshAllPurchases();   // re-query inapp + subs and reconcile (grant/revoke)
    }

    // Builds the "Aptoide" settings block (a titled panel with the account button and a
    // buy entry for the legendary rainbow non-consumable) under the scene Canvas.
    private void BuildAptoideSettingsBlock()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("No Canvas found; cannot build the Aptoide settings block.");
            return;
        }

        _aptoideBlock = new GameObject("AptoideSettingsBlock",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        _aptoideBlock.transform.SetParent(canvas.transform, false);

        RectTransform rect = _aptoideBlock.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);

        _aptoideBlock.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        VerticalLayoutGroup layout = _aptoideBlock.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = _aptoideBlock.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateLabel(_aptoideBlock.transform, "Aptoide", 30f, FontStyles.Bold);

        _btnAptoideAccount = CreateButton(_aptoideBlock.transform, "Sign in to Aptoide Services",
            new Color(0.13f, 0.45f, 0.85f, 1f), out _txtAptoideAccount);

        _btnBuyLegendary = CreateButton(_aptoideBlock.transform, "Buy Legendary Dice",
            new Color(0.45f, 0.2f, 0.7f, 1f), out _txtBuyLegendary);
        _btnBuyLegendary.onClick.AddListener(OnBuyLegendaryDicePressed);
    }

    private Button CreateButton(Transform parent, string text, Color color, out TMP_Text label)
    {
        GameObject go = new GameObject("Button",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = 76f;
        element.preferredWidth = 460f;

        label = CreateLabel(go.transform, text, 26f, FontStyles.Normal);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return go.GetComponent<Button>();
    }

    private TMP_Text CreateLabel(Transform parent, string text, float fontSize, FontStyles style)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private void ShowToast(string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (currentActivity != null)
            {
                AndroidJavaClass toastClass = new AndroidJavaClass("android.widget.Toast");
                currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    AndroidJavaObject toast = toastClass.CallStatic<AndroidJavaObject>(
                        "makeText",
                        currentActivity,
                        message,
                        toastClass.GetStatic<int>("LENGTH_SHORT")
                    );
                    toast.Call("show");
                }));
            }
        }
#else
        Debug.Log($"Toast: {message}");
#endif
    }


    //Implement Listeners

    public void OnBillingSetupFinished(BillingResult billingResult)
    {
        if (billingResult.ResponseCode == ResponseOk)
        {
            // Check if subscriptions are supported
            if (AptoideBillingSDKManager.IsFeatureSupported(FeatureSubscriptions).ResponseCode == ResponseOk)
            {
                Debug.Log("Subscriptions are supported.");
                QueryProductDetailsParams queryProductDetailsParamsSubs =
                    QueryProductDetailsParams.NewBuilder()
                        .SetProductList(subsProducts)
                        .Build();
                AptoideBillingSDKManager.QueryProductDetailsAsync(queryProductDetailsParamsSubs);
            }
            else
            {
                Debug.LogWarning("Subscriptions are not supported on this device.");
            }

            QueryProductDetailsParams queryProductDetailsParamsInapps =
                QueryProductDetailsParams.NewBuilder()
                    .SetProductList(inappProducts)
                    .Build();
            AptoideBillingSDKManager.QueryProductDetailsAsync(queryProductDetailsParamsInapps);

            // Query purchases for both in-app and subscription products (serialized).
            RefreshAllPurchases();

            // Feature availability may now be known — refresh the Aptoide block.
            BindAptoideSettings();
        }
        else
        {
            Debug.LogError($"Billing setup failed with response code: {billingResult.ResponseCode}");
        }
    }

    public void OnConsumeResponse(BillingResult billingResult, string purchaseToken)
    {
        if (billingResult.ResponseCode == ResponseOk)
        {
            Debug.Log($"Purchase with token {purchaseToken} consumed successfully.");
        }
        else
        {
            Debug.LogError($"Failed to consume purchase with token {purchaseToken}. Response code: {billingResult.ResponseCode}");
        }
    }

    public void OnPurchasesUpdated(BillingResult billingResult, Purchase[] purchases)
    {
        if (billingResult.ResponseCode == ResponseOk)
        {
            foreach (var purchase in purchases)
            {
                Debug.Log($"Purchase updated: {purchase.Products[0]}");
                StartCoroutine(ValidatePurchase(purchase));
            }
        }
        else
        {
            Debug.LogError($"Failed to update purchases. Response code: {billingResult.ResponseCode}");
            ShowToast("Failed to update purchases.");
        }
    }

    public void OnProductDetailsResponse(BillingResult billingResult, QueryProductDetailsResult productDetailsResult)
    {
        if (billingResult.ResponseCode == ResponseOk)
        {
            foreach (var productDetails in productDetailsResult.ProductDetailsList)
            {
                Debug.Log($"SKU Details received: {productDetails.ProductId}");
                if (productDetails.ProductId == SkuAttempts)
                {
                    Debug.Log($"Price for attempts: {productDetails.OneTimePurchaseOfferDetails.FormattedPrice}");
                    // Update the UI or perform any action with the SKU details
                    _btnBuySDK.GetComponentInChildren<TMP_Text>().text = "Buy Attempts: " + productDetails.OneTimePurchaseOfferDetails.FormattedPrice;
                }
                else if (productDetails.ProductId == SkuLegendaryDice)
                {
                    Debug.Log($"Price for legendary dice: {productDetails.OneTimePurchaseOfferDetails.FormattedPrice}");
                    if (_txtBuyLegendary != null)
                        _txtBuyLegendary.text = "Buy Legendary: " + productDetails.OneTimePurchaseOfferDetails.FormattedPrice;
                }
                else if (productDetails.ProductId == SkuGoldenDice)
                {
                    Debug.Log($"Price for golden dice subscription: {productDetails.SubscriptionOfferDetails[0].PricingPhases.PricingPhaseList[0].FormattedPrice}");
                    // Update the UI or perform any action with the SKU details
                    _btnSubsSDK.GetComponentInChildren<TMP_Text>().text = "Buy Subs: " + productDetails.SubscriptionOfferDetails[0].PricingPhases.PricingPhaseList[0].FormattedPrice;
                }
            }
        }
        else
        {
            Debug.LogError($"Failed to receive SKU details. Response code: {billingResult.ResponseCode}");
        }
    }


    public void OnBillingServiceDisconnected()
    {
        Debug.LogError("Billing service disconnected.");
        // Disable the buttons if billing setup fails
        _btnBuySDK.interactable = false;
        _btnSubsSDK.interactable = false;
        if (_btnBuyLegendary != null)
            _btnBuyLegendary.interactable = false;
    }
}
