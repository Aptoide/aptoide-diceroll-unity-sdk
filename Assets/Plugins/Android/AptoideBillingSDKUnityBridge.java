import android.util.Log;
import androidx.annotation.NonNull;
import com.aptoide.sdk.billing.*;
import com.aptoide.sdk.billing.listeners.AptoideBillingClientStateListener;
import com.aptoide.sdk.billing.listeners.ConsumeResponseListener;
import com.unity3d.player.UnityPlayer;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;


public class AptoideBillingSDKUnityBridge {
    private static String unityClassName;
    private static String TAG = "AptoideBillingSDKUnityBridge";
    private static AptoideBillingClient billingClient;

    private static Map<String, ProductDetails> fetchedProductDetailsMap = new HashMap<>();

    private static AptoideBillingClientStateListener aptoideBillingClientStateListener =
            new AptoideBillingClientStateListener() {
                @Override
                public void onBillingSetupFinished(BillingResult billingResult) {
                    Log.d(TAG, "Billing setup finished.");
                    UnityPlayer.UnitySendMessage(unityClassName,
                            "BillingSetupFinishedCallback",
                            "" + getBillingResultJsonObject(billingResult).toString());
                }

                @Override
                public void onBillingServiceDisconnected() {
                    Log.d(TAG, "Billing service disconnected.");
                    UnityPlayer.UnitySendMessage(unityClassName,
                            "BillingServiceDisconnectedCallback",
                            "");
                }
            };

    private static PurchasesUpdatedListener purchasesUpdatedListener =
            (billingResult, purchases) -> {
                Log.d(TAG,
                        "Purchase updated: " + billingResult.getResponseCode() + " debugMessage: "
                                + billingResult.getDebugMessage());
                UnityPlayer.UnitySendMessage(unityClassName, "PurchasesUpdatedCallback",
                        purchasesResponseResultToJson(billingResult, purchases));
            };

    private static PurchasesResponseListener purchasesResponseListener =
            (billingResult, purchases) -> {
                Log.d(TAG,
                        "Purchases received: " + billingResult.getResponseCode() + " debugMessage: "
                                + billingResult.getDebugMessage());
                UnityPlayer.UnitySendMessage(unityClassName, "PurchasesResponseCallback",
                        purchasesResponseResultToJson(billingResult, (List<Purchase>) purchases));
            };

    private static ProductDetailsResponseListener productDetailsResponseListener =
            (billingResult, productDetailsResult) -> {
                Log.d(TAG, "Product details received: " + billingResult.getResponseCode()
                        + " debugMessage: " + billingResult.getDebugMessage());
                if (!productDetailsResult.getProductDetailsList().isEmpty()) {
                    for (ProductDetails productDetail :
                            productDetailsResult.getProductDetailsList()) {
                        fetchedProductDetailsMap.put(productDetail.getProductId(), productDetail);
                    }
                }
                UnityPlayer.UnitySendMessage(unityClassName, "ProductDetailsResponseCallback",
                        productDetailsResultToJson(billingResult, productDetailsResult));
            };

    private static ConsumeResponseListener consumeResponseListener =
            (billingResult, purchaseToken) -> {
                Log.d(TAG, "Consume response: " + purchaseToken + ", result: "
                        + billingResult.getResponseCode() + " debugMessage: "
                        + billingResult.getDebugMessage());
                UnityPlayer.UnitySendMessage(unityClassName, "ConsumeResponseCallback",
                        consumeResultToJson(billingResult, purchaseToken));
            };

    public static void initialize(String _unityClassName, String _publicKey) {
        unityClassName = _unityClassName;
        billingClient = AptoideBillingClient.newBuilder(UnityPlayer.currentActivity)
                .setListener(purchasesUpdatedListener)
                .setPublicKey(_publicKey)
                .build();
    }

    public static void startConnection() {
        billingClient.startConnection(aptoideBillingClientStateListener);
    }

    public static void endConnection() {
        billingClient.endConnection();
        Log.d(TAG, "Billing client connection ended.");
    }

    public static boolean isReady() {
        boolean ready = billingClient.isReady();
        Log.d(TAG, "Billing client is ready: " + ready);
        return ready;
    }

    public static void queryProductDetailsAsync(List<String> products, String productType) {
        List<QueryProductDetailsParams.Product> productList = new ArrayList<>();
        for (String product : products) {
            productList.add(
                    QueryProductDetailsParams.Product.newBuilder()
                            .setProductId(product)
                            .setProductType(productType)
                            .build()
            );
        }

        QueryProductDetailsParams queryProductDetailsParams2 =
                QueryProductDetailsParams.newBuilder()
                        .setProductList(productList)
                        .build();
        billingClient.queryProductDetailsAsync(queryProductDetailsParams2,
                productDetailsResponseListener);
    }

    public static String launchBillingFlow(String productId, String productType,
            String developerPayload, String obfuscatedAccountId, boolean freeTrial) {
        ProductDetails productDetails = getProductDetailsFromProductId(productId);
        BillingResult billingResult;
        if (productDetails != null) {
            ArrayList<BillingFlowParams.ProductDetailsParams> productDetailsParamsList =
                    new ArrayList<>();
            productDetailsParamsList.add(
                    BillingFlowParams.ProductDetailsParams.newBuilder()
                            .setProductDetails(productDetails)
                            .build()
            );
            BillingFlowParams billingFlowParams =
                    BillingFlowParams.newBuilder()
                            .setProductDetailsParamsList(productDetailsParamsList)
                            .setFreeTrial(freeTrial)
                            .setObfuscatedAccountId(obfuscatedAccountId)
                            .setDeveloperPayload(developerPayload)
                            .build();
            billingResult = billingClient.launchBillingFlow(UnityPlayer.currentActivity,
                    billingFlowParams);
        } else {
            BillingFlowParams billingFlowParams = new BillingFlowParams(productId, productType,
                    developerPayload, obfuscatedAccountId, freeTrial);
            billingResult = billingClient.launchBillingFlow(UnityPlayer.currentActivity,
                    billingFlowParams);
        }
        Log.d(TAG, "Launch of Billing Flow result: " + billingResult.getResponseCode()
                + " debugMessage: " + billingResult.getDebugMessage());
        return getBillingResultJsonObject(billingResult).toString();
    }

    public static void consumeAsync(String purchaseToken) {
        ConsumeParams consumeParams =
                ConsumeParams.newBuilder()
                        .setPurchaseToken(purchaseToken)
                        .build();
        billingClient.consumeAsync(consumeParams, consumeResponseListener);
    }

    public static String isFeatureSupported(int feature) {
        BillingResult billingResult = billingClient.isFeatureSupported(feature);
        Log.d(TAG, "Feature " + feature + " supported: " + (billingResult.getResponseCode() == 0));
        return getBillingResultJsonObject(billingResult).toString();
    }

    public static void queryPurchasesAsync(String productType) {
        Log.d(TAG, "Querying purchases async of product type: " + productType);
        QueryPurchasesParams queryPurchasesParams =
                QueryPurchasesParams.newBuilder()
                        .setProductType(productType)
                        .build();
        billingClient.queryPurchasesAsync(queryPurchasesParams, purchasesResponseListener);
    }

    public static String getReferralDeeplink() {
        ReferralDeeplink referralDeeplink = billingClient.getReferralDeeplink();
        Log.d(TAG, "Referral deeplink: " + referralDeeplink);
        return referralDeeplinkResultToJson(referralDeeplink);
    }

    public static boolean isAppUpdateAvailable() {
        boolean isUpdateAvailable = billingClient.isAppUpdateAvailable();
        Log.d(TAG, "Is app update available: " + isUpdateAvailable);
        return isUpdateAvailable;
    }

    public static void launchAppUpdateDialog() {
        billingClient.launchAppUpdateDialog(UnityPlayer.currentActivity);
        Log.d(TAG, "Launched app update dialog.");
    }

    public static void launchAppUpdateStore() {
        billingClient.launchAppUpdateStore(UnityPlayer.currentActivity);
        Log.d(TAG, "Launched app update store.");
    }

    private static String purchasesResponseResultToJson(BillingResult billingResult,
            List<Purchase> purchases) {
        JSONObject jsonObject = new JSONObject();
        try {
            JSONObject billingResultJsonObject = getBillingResultJsonObject(billingResult);
            jsonObject.put("BillingResult", billingResultJsonObject);
            JSONArray purchasesJsonArray = new JSONArray();
            for (Purchase purchase : purchases) {
                JSONObject purchaseJsonObject = new JSONObject();

                if (purchase.getAccountIdentifiers() != null) {
                    JSONObject accountIdentifiersJsonObject = new JSONObject();
                    accountIdentifiersJsonObject.put("ObfuscatedAccountId",
                            purchase.getAccountIdentifiers().getObfuscatedAccountId());
                    purchaseJsonObject.put("AccountIdentifiers", accountIdentifiersJsonObject);
                }

                purchaseJsonObject.put("DeveloperPayload", purchase.getDeveloperPayload());
                purchaseJsonObject.put("OrderId", purchase.getOrderId());
                purchaseJsonObject.put("OriginalJson", purchase.getOriginalJson());
                purchaseJsonObject.put("PackageName", purchase.getPackageName());

                JSONArray productsJsonArray = new JSONArray();
                for (String product : purchase.getProducts()) {
                    productsJsonArray.put(product);
                }
                purchaseJsonObject.put("Products", productsJsonArray);

                purchaseJsonObject.put("PurchaseState", purchase.getPurchaseState());
                purchaseJsonObject.put("PurchaseTime", purchase.getPurchaseTime());
                purchaseJsonObject.put("PurchaseToken", purchase.getPurchaseToken());
                purchaseJsonObject.put("Signature", purchase.getSignature());
                purchaseJsonObject.put("IsAutoRenewing", purchase.isAutoRenewing());

                purchasesJsonArray.put(purchaseJsonObject);
            }
            jsonObject.put("Purchases", purchasesJsonArray);
        } catch (JSONException exception) {
            Log.e(TAG, "purchasesResponseResultToJson: ", exception);
            return new JSONObject().toString();
        }
        return jsonObject.toString();
    }

    private static String productDetailsResultToJson(BillingResult billingResult,
            QueryProductDetailsResult productDetailsResult) {
        JSONObject jsonObject = new JSONObject();
        try {
            JSONObject billingResultJsonObject = getBillingResultJsonObject(billingResult);
            jsonObject.put("BillingResult", billingResultJsonObject);

            JSONObject productDetailsResultJsonObject = new JSONObject();
            JSONArray productDetailsJsonArray = new JSONArray();
            for (ProductDetails productDetails : productDetailsResult.getProductDetailsList()) {
                JSONObject productDetailsJsonObject = new JSONObject();

                productDetailsJsonObject.put("ProductId", productDetails.getProductId());
                productDetailsJsonObject.put("ProductType", productDetails.getProductType());
                productDetailsJsonObject.put("Title", productDetails.getTitle());

                if (productDetails.getDescription() != null) {
                    productDetailsJsonObject.put("Description", productDetails.getDescription());
                }

                // One-time purchase
                if (productDetails.getOneTimePurchaseOfferDetails() != null) {
                    JSONObject oneTimeOfferJson = getOneTimePurchaseOfferDetailsJsonObject(
                            productDetails);

                    productDetailsJsonObject.put("OneTimePurchaseOfferDetails", oneTimeOfferJson);
                }

                // Subscription offers
                if (productDetails.getSubscriptionOfferDetails() != null) {
                    JSONArray subscriptionOffersArray = getSubscriptionOfferDetailsJsonArray(
                            productDetails);
                    productDetailsJsonObject.put("SubscriptionOfferDetails",
                            subscriptionOffersArray);
                }

                productDetailsJsonArray.put(productDetailsJsonObject);
            }
            productDetailsResultJsonObject.put("ProductDetailsList", productDetailsJsonArray);

            JSONArray unfetchedProductsJsonArray = getUnfetchedProductsJsonArray(
                    productDetailsResult);
            productDetailsResultJsonObject.put("UnfetchedProductList",
                    unfetchedProductsJsonArray);

            jsonObject.put("ProductDetailsResult", productDetailsResultJsonObject);
        } catch (JSONException exception) {
            Log.e(TAG, "productDetailsResultToJson: ", exception);
            return new JSONObject().toString();
        }
        return jsonObject.toString();
    }

    @NonNull
    private static JSONArray getUnfetchedProductsJsonArray(
            QueryProductDetailsResult queryProductDetailsResult)
            throws JSONException {
        JSONArray unfetchedProductsJsonArray = new JSONArray();
        for (UnfetchedProduct unfetchedProduct :
                queryProductDetailsResult.getUnfetchedProductList()) {
            JSONObject unfetchedProductJsonObject = new JSONObject();
            unfetchedProductJsonObject.put("ProductId", unfetchedProduct.getProductId());
            unfetchedProductJsonObject.put("ProductType", unfetchedProduct.getProductType());
            unfetchedProductJsonObject.put("StatusCode", unfetchedProduct.getStatusCode());
            unfetchedProductsJsonArray.put(unfetchedProductJsonObject);
        }
        return unfetchedProductsJsonArray;
    }

    @NonNull
    private static JSONArray getSubscriptionOfferDetailsJsonArray(ProductDetails productDetails)
            throws JSONException {
        JSONArray subscriptionOffersArray = new JSONArray();

        if (productDetails.getSubscriptionOfferDetails() != null) {
            for (ProductDetails.SubscriptionOfferDetails offerDetail :
                    productDetails.getSubscriptionOfferDetails()) {
                JSONObject offerDetailJson = new JSONObject();

                // Pricing phases
                JSONArray pricingPhasesArray = getPricingPhasesJsonArray(offerDetail);

                JSONObject pricingPhasesJson = new JSONObject();
                pricingPhasesJson.put("PricingPhaseList", pricingPhasesArray);
                offerDetailJson.put("PricingPhases", pricingPhasesJson);

                // Trial details
                if (offerDetail.getTrialDetails() != null) {
                    JSONObject trialJson = new JSONObject();
                    trialJson.put("Period", offerDetail.getTrialDetails().getPeriod());
                    trialJson.put("PeriodEndDate",
                            offerDetail.getTrialDetails().getPeriodEndDate());
                    offerDetailJson.put("TrialDetails", trialJson);
                }
                subscriptionOffersArray.put(offerDetailJson);
            }
        }
        return subscriptionOffersArray;
    }

    @NonNull
    private static JSONArray getPricingPhasesJsonArray(
            ProductDetails.SubscriptionOfferDetails offerDetail)
            throws JSONException {
        JSONArray pricingPhasesArray = new JSONArray();
        for (ProductDetails.PricingPhase pricingPhase :
                offerDetail.getPricingPhases()
                        .getPricingPhaseList()) {
            JSONObject phaseJson = new JSONObject();
            phaseJson.put("BillingPeriod", pricingPhase.getBillingPeriod());
            phaseJson.put("FormattedPrice", pricingPhase.getFormattedPrice());
            phaseJson.put("PriceAmountMicros", pricingPhase.getPriceAmountMicros());
            phaseJson.put("PriceCurrencyCode", pricingPhase.getPriceCurrencyCode());
            phaseJson.put("AppcFormattedPrice",
                    pricingPhase.getAppcFormattedPrice());
            phaseJson.put("AppcPriceAmountMicros",
                    pricingPhase.getAppcPriceAmountMicros());
            phaseJson.put("AppcPriceCurrencyCode",
                    pricingPhase.getAppcPriceCurrencyCode());
            phaseJson.put("FiatFormattedPrice",
                    pricingPhase.getFiatFormattedPrice());
            phaseJson.put("FiatPriceAmountMicros",
                    pricingPhase.getFiatPriceAmountMicros());
            phaseJson.put("FiatPriceCurrencyCode",
                    pricingPhase.getFiatPriceCurrencyCode());
            pricingPhasesArray.put(phaseJson);
        }
        return pricingPhasesArray;
    }

    @NonNull
    private static JSONObject getOneTimePurchaseOfferDetailsJsonObject(
            ProductDetails productDetails) throws JSONException {
        ProductDetails.OneTimePurchaseOfferDetails offer =
                productDetails.getOneTimePurchaseOfferDetails();
        JSONObject oneTimeOfferJson = new JSONObject();
        oneTimeOfferJson.put("FormattedPrice", offer.getFormattedPrice());
        oneTimeOfferJson.put("PriceAmountMicros", offer.getPriceAmountMicros());
        oneTimeOfferJson.put("PriceCurrencyCode", offer.getPriceCurrencyCode());
        oneTimeOfferJson.put("AppcFormattedPrice", offer.getAppcFormattedPrice());
        oneTimeOfferJson.put("AppcPriceAmountMicros", offer.getAppcPriceAmountMicros());
        oneTimeOfferJson.put("AppcPriceCurrencyCode", offer.getAppcPriceCurrencyCode());
        oneTimeOfferJson.put("FiatFormattedPrice", offer.getFiatFormattedPrice());
        oneTimeOfferJson.put("FiatPriceAmountMicros", offer.getFiatPriceAmountMicros());
        oneTimeOfferJson.put("FiatPriceCurrencyCode", offer.getFiatPriceCurrencyCode());
        return oneTimeOfferJson;
    }

    private static String consumeResultToJson(BillingResult billingResult, String purchaseToken) {
        JSONObject jsonObject = new JSONObject();
        try {
            JSONObject billingResultJsonObject = getBillingResultJsonObject(billingResult);
            jsonObject.put("BillingResult", billingResultJsonObject);
            jsonObject.put("PurchaseToken", purchaseToken);
        } catch (JSONException exception) {
            Log.e(TAG, "consumeResultToJson: ", exception);
            return new JSONObject().toString();
        }
        return jsonObject.toString();
    }

    private static String referralDeeplinkResultToJson(ReferralDeeplink referralDeeplink) {
        JSONObject jsonObject = new JSONObject();
        try {
            JSONObject billingResultJsonObject = getBillingResultJsonObject(
                    referralDeeplink.getBillingResult());
            jsonObject.put("BillingResult", billingResultJsonObject);
            jsonObject.put("StoreDeeplink", referralDeeplink.getStoreDeeplink());
            jsonObject.put("FallbackDeeplink", referralDeeplink.getFallbackDeeplink());
        } catch (JSONException exception) {
            Log.e(TAG, "referralDeeplinkResultToJson: ", exception);
            return new JSONObject().toString();
        }
        return jsonObject.toString();
    }

    private static ProductDetails getProductDetailsFromProductId(String productId) {
        return fetchedProductDetailsMap.get(productId);
    }

    private static JSONObject getBillingResultJsonObject(BillingResult billingResult) {
        JSONObject billingResultJsonObject = new JSONObject();
        try {
            billingResultJsonObject.put("ResponseCode", billingResult.getResponseCode());
            if (billingResult.getDebugMessage() != null && !billingResult.getDebugMessage()
                    .isEmpty() && !billingResult.getDebugMessage().equals("null")) {
                billingResultJsonObject.put("DebugMessage", billingResult.getDebugMessage());
            }
            return billingResultJsonObject;
        } catch (JSONException exception) {
            Log.e(TAG, "getBillingResultJsonObject: ", exception);
            return new JSONObject();
        }
    }
}
