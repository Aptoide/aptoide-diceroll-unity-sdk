# Aptoide Diceroll Unity sample

   - [💻 About](#-about)
   - [⚙️ Design/Features](#️-designfeatures)
   - [🚀 How to run it](#-how-to-run-it)
   - [Limitations](#limitations)
   - [Prerequisites](#prerequisites)

## 💻 About

This sample app is used to show the integration of [Aptoide Unity Billing SDK](https://github.com/Aptoide/aptoide-unity-billing-sdk) following the official Aptoide Unity Billing SDK [integration documentation](https://docs.catappult.io/docs/unity-sdk).

You can download it in our offical Aptoide [page](https://com-appcoins-diceroll-unity.en.aptoide.com/app).

> This sample app is still under development and some features might be imcomplete for now.

## ⚙️ Design/Features

- Simple roll of the dice game with statistics for rolls to avoid having an oversimplified case and allow for some navigation and state managment
- Number of rolls available are limited to 3 maximum, requiring a payment if it reaches 0. Resetting to max if payment was completed.
- If the roll is guess is correct, it resets to the maximum just like a payment would.
- Contains also a Golden Dice which is managed via Subscriptions.

## 🚀 How to run it

To correctly test the Application, update the [CATAPPULT_PUBLIC_KEY](https://github.com/Aptoide/aptoide-unity-billing-sdk/blob/main/Assets/Scripts/Logic.cs#L61) with the Sample key on [Aptoide Documentation]([https://docs.catappult.io/docs/native-android-billing-sdk#faq](https://docs.catappult.io/docs/billing-integration#public-key)).

## Limitations

Since this app is still under development, some features may not be fully developed.

## Prerequisites

Unity 2021.3.8f1
