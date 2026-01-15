# PayPalIntegrationAPI

[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/Postgres-PostgreSQL-green)](https://www.postgresql.org/)
[![PayPal](https://img.shields.io/badge/PayPal-Payments-blue)](https://developer.paypal.com/)

— A complete **PayPal Checkout integration** built with **ASP.NET Core Web API**, demonstrating a real-world payment flow including order creation, approval, capture, webhook handling, and an admin dashboard to manage orders.

This project is designed as a **backend-focused integration** with a lightweight HTML/JavaScript frontend for demonstration purposes.

---

## 🌐 Live Demo

[Checkout & Dashboard Page](https://paypalintegrationapi.onrender.com/checkout.html)

---

## 🚀 Features

- ✅ Create PayPal orders using PayPal REST API
- ✅ Redirect users to PayPal approval page
- ✅ Handle PayPal **Webhooks** securely
- ✅ Capture approved orders automatically
- ✅ Persist orders in a relational database (PostgreSQL)
- ✅ Admin dashboard to:
  - View all orders
  - Select single or multiple orders
  - Bulk delete selected orders
- ✅ Token caching for PayPal access tokens
- ✅ Idempotent webhook handling
- ✅ Clean separation of concerns (Client, Controllers, Data, Models)

---

## 🛠 Tech Stack

- **Backend**
  - ASP.NET Core Web API (8)
  - Entity Framework Core
  - PostgreSQL
  - IHttpClientFactory
  - PayPal REST API

- **Frontend**
  - HTML5 & CSS3
  - Vanilla JavaScript (Fetch API)

- **Payments**
  - PayPal Sandbox
  - Webhooks (CHECKOUT.ORDER.APPROVED, PAYMENT.CAPTURE.COMPLETED)

---

## 💻 Local Development (Run Locally)

To set up and run the project on your local machine, follow these steps:

> ### **1️⃣ Clone the Repository**
> ```bash
> git clone https://github.com/chikh97laid/PayPalIntegrationAPI.git
> cd PayPalIntegrationAPI
> ```
>
> ### **2️⃣ Configure PayPal & Environment Variables**
> Create a PayPal Developer account to get your Sandbox keys. Then, set your connection string and keys as system environment variables:
> ```bash
> # Example Environment Variables (Replace values with your details)
> export ConnectionStrings__DefaultConnection="Host=HOST;Database=DB;Username=USER;Password=PASS;SSL Mode=Require;Trust Server Certificate=true"
> export PayPal__ClientId="YOUR_CLIENT_ID"
> export PayPal__Secret="YOUR_SECRET"
> export PayPal__BaseUrl="[https://api-m.sandbox.paypal.com](https://api-m.sandbox.paypal.com)"
> ```
>
> ### **3️⃣ Apply Database Migrations**
> ```bash
> dotnet ef database update
> ```
>
> ### **4️⃣ Run the Project**
> ```bash
> dotnet run
> ```
> **Access the Demo:** Open `http://localhost:5000/checkout.html` in your browser.

---

## 🔗 Useful Links

* **GitHub:** https://github.com/chikh97laid
* **LinkedIn:** https://linkedin.com/in/chikhouladlaid

---

## 📝 Notes & Interview Tips

* **Why Webhooks?** We use webhooks because the frontend cannot be trusted. If a user closes the browser after payment but before the redirect, only webhooks ensure the database is updated.
* **Security:** In production, always move secrets to a secure Vault (like Azure Key Vault) instead of environment variables.
* **Testing:** Always use **PayPal Sandbox** accounts for testing. Never use real credit cards in a sandbox environment.
