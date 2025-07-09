using System.Collections;
using System.Collections.Generic;
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
                    IPurchasesResponseListener
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

    private static List<QueryProductDetailsParams.Product> inappProducts =
                           new List<QueryProductDetailsParams.Product>() {
                               QueryProductDetailsParams.Product.NewBuilder()
                                   .SetProductId("attempts")
                                   .SetProductType("inapp")
                                   .Build()
                           };
    private static List<QueryProductDetailsParams.Product> subsProducts =
                           new List<QueryProductDetailsParams.Product>() {
                               QueryProductDetailsParams.Product.NewBuilder()
                                   .SetProductId("golden_dice")
                                   .SetProductType("subs")
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

        _btnRoll.onClick.AddListener(OnRollDicePressed);
        _btnBuySDK.onClick.AddListener(OnBuySDKPressed);
        _btnSubsSDK.onClick.AddListener(OnSubsSDKPressed);

        AptoideBillingSDKManager.InitializePlugin(
            this,
            this,
            this,
            this,
            this,
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAzIR0OxCJDzaF2PvcymkPvG9PQTCVkGPxG5eLt5ZcIBftWKl6nFmgItAyYm2ixOrpNUOHjtuTOXuaMMABV91Y6CitQujsr0O76PsHduY0jG2j32wJAIluzspkzKS6sBp4MZvfG/ctUaqjDibYuvRZtE3Wv7kY7zH/lwKmD+BnGScFc8YTJUOlcRdqXtIPbX9Je2h5PtLUNmiLzcnjKxJ7dwsSc/QEuVXSY7k/jFkjIsv62EaLEcMtJrbuL+jvLg6/MpK2REuinLrkG9xK2JjgK9xhW6D7pEvQb/Dj3YFk0RbaP7EITsnrQaqZ1pL9aAEDzeG3qcsJSU2cn/wfGgZodwIDAQAB",
            this.gameObject.name);
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

        ProductDetails productDetails = new ProductDetails();
        productDetails.ProductId = "attempts";
        productDetails.ProductType = "inapp";

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

    private void OnSubsSDKPressed()
    {
        ShowToast("Subscribe SDK button pressed.");

        ProductDetails productDetails = new ProductDetails();
        productDetails.ProductId = "golden_dice";
        productDetails.ProductType = "subs";


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
                    Debug.Log($"Purchase validated successfully for {purchase.Products[0]}. Consuming the purchase...");
                    _currentAttempts = _startingAttempts;
                    ConsumeParams consumeParams =
                        ConsumeParams.NewBuilder()
                            .SetPurchaseToken(purchase.PurchaseToken)
                            .Build();
                    AptoideBillingSDKManager.ConsumeAsync(consumeParams);

                    if (purchase.Products[0] == "golden_dice")
                    {
                        Debug.Log("Subscription purchased.");
                        setGoldenDice();
                    }
                    else
                    {
                        Debug.Log("Item purchased.");
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

    private void setGoldenDice()
    {
        Color diceColor = new Color(168f / 255f, 125f / 255f, 5f / 255f);
        _dice.GetComponent<Image>().color = diceColor;

        // Assuming _dice is the parent GameObject
        UIDice parentDice = _dice;

        int i = 1;
        while (i < 6)
        {
            // Find the child GameObject by name
            Transform childTransform = parentDice.transform.Find(i.ToString());
            if (childTransform != null)
            {
                GameObject childGameObject = childTransform.gameObject;

                // Get the Transform component of childGameObject
                Transform childTransformm = childGameObject.transform;

                // Loop through each child
                foreach (Transform child in childTransformm)
                {
                    Color diceColor_ = new Color(168f / 255f, 125f / 255f, 5f / 255f);
                    child.GetComponent<Image>().color = diceColor_;
                }
            }
            else
            {
                Debug.LogError("Child GameObject not found.");
            }
            i++;
        }
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
        if (billingResult.ResponseCode == 0) // Assuming 0 indicates success
        {
            // Check if subscriptions are supported
            if (AptoideBillingSDKManager.IsFeatureSupported(0).ResponseCode == 0)
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

            // Query purchases for both in-app and subscription products
            QueryPurchasesParams queryPurchasesParamsInapps =
                QueryPurchasesParams.NewBuilder()
                    .SetProductType("inapp")
                    .Build();
            AptoideBillingSDKManager.QueryPurchasesAsync(queryPurchasesParamsInapps);

            QueryPurchasesParams queryPurchasesParamsSubs =
                QueryPurchasesParams.NewBuilder()
                    .SetProductType("subs")
                    .Build();
            AptoideBillingSDKManager.QueryPurchasesAsync(queryPurchasesParamsSubs);
        }
        else
        {
            Debug.LogError($"Billing setup failed with response code: {billingResult.ResponseCode}");
        }
    }

    public void OnConsumeResponse(BillingResult billingResult, string purchaseToken)
    {
        if (billingResult.ResponseCode == 0) // Assuming 0 indicates success
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
        if (billingResult.ResponseCode == 0) // Assuming 0 indicates success
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

    public void OnQueryPurchasesResponse(BillingResult billingResult, Purchase[] purchases)
    {
        if (billingResult.ResponseCode == 0) // Assuming 0 indicates success
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
        if (billingResult.ResponseCode == 0) // Assuming 0 indicates success
        {
            foreach (var productDetails in productDetailsResult.ProductDetailsList)
            {
                Debug.Log($"SKU Details received: {productDetails.ProductId}");
                if (productDetails.ProductId == "attempts")
                {
                    Debug.Log($"Price for attempts: {productDetails.OneTimePurchaseOfferDetails.FormattedPrice}");
                    // Update the UI or perform any action with the SKU details
                    _btnBuySDK.GetComponentInChildren<TMP_Text>().text = "Buy Attempts: " + productDetails.OneTimePurchaseOfferDetails.FormattedPrice;
                }
                else if (productDetails.ProductId == "golden_dice")
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
    }

    // Add this method to handle the PurchasesResult
    private void HandlePurchasesResult(PurchasesResult purchasesResult)
    {
        if (purchasesResult.BillingResult.ResponseCode == 0) // Assuming 0 indicates success
        {
            foreach (var purchase in purchasesResult.Purchases)
            {
                Debug.Log($"Purchase found: {purchase.Products[0]}");
                StartCoroutine(ValidatePurchase(purchase));
            }
        }
        else
        {
            Debug.LogError($"Failed to query purchases. Response code: {purchasesResult.BillingResult.ResponseCode}");
        }
    }

}
