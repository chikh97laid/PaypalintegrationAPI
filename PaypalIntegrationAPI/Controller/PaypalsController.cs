using Microsoft.AspNetCore.Mvc;
using PayPalIntegrationAPI.Client;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PayPalIntegrationAPI.Models;
using PayPalIntegrationAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace PayPalIntegrationAPI.Controller
{
[ApiController]
[Route("api/[controller]")]
public class PaypalsController : ControllerBase
{
    private readonly ILogger<PaypalsController> _logger;
    private readonly AppDbContext _dbContext;
    private readonly IPayPalClient _payPalClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public PaypalsController(ILogger<PaypalsController> logger, AppDbContext dbContext, IPayPalClient payPalClient, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _payPalClient = payPalClient;
    }

    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder()
    {
        try
        {
            _logger.LogInformation("[CreateOrder] Starting order creation");

            // get access token
            var accessToken = await _payPalClient.GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogError("[CreateOrder] Failed to obtain access token");
                return StatusCode(500, new { error = "Failed to obtain PayPal access token" });
            }
            _logger.LogDebug("[CreateOrder] Access token obtained successfully");

            // create order
            using var http = _httpClientFactory.CreateClient("PayPal");
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var order = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new {
                        amount = new { currency_code = "USD", value = "10.00" }
                    }
                },
                application_context = new
                {
                    return_url = "http://localhost:5020/paypal-success.html",
                    cancel_url = "http://localhost:5020/paypal-cancel.html"
                }
            };

            _logger.LogDebug("[CreateOrder] Sending order to PayPal API");
            var response = await http.PostAsJsonAsync("https://api-m.sandbox.paypal.com/v2/checkout/orders", order);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("[CreateOrder] PayPal API error: Status={StatusCode}, Body={ErrorBody}", response.StatusCode, errorContent);
                return StatusCode((int)response.StatusCode, new { error = "Failed to create PayPal order", details = errorContent });
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            // extract approval link
            if (!result.TryGetProperty("links", out var linksElement))
            {
                _logger.LogError("[CreateOrder] 'links' property not found in PayPal response");
                return StatusCode(500, new { error = "Invalid PayPal response format" });
            }

            var approveLink = linksElement.EnumerateArray()
                                           .FirstOrDefault(x => x.GetProperty("rel").GetString() == "approve")
                                           .GetProperty("href")
                                           .GetString();

            if (string.IsNullOrWhiteSpace(approveLink))
            {
                _logger.LogError("[CreateOrder] Approval link not found in PayPal response");
                return StatusCode(500, new { error = "Approval link not found in PayPal response" });
            }

            // Create order to database
            var orderDb = new Order
            {
                PayPalOrderId = result.GetProperty("id").GetString(),
                CreatedAt = DateTime.UtcNow,
                Status = OrderStatus.CREATED,
                TotalAmount = 10.00m,
                Currency = "USD"
            };

            _logger.LogDebug("[CreateOrder] Order object created for database storage");
            
            // Here you would typically save orderDb to your database
            _dbContext.Orders.Add(orderDb);
            await _dbContext.SaveChangesAsync();
            _logger.LogDebug("[CreateOrder] Order saved to database");

            _logger.LogInformation("[CreateOrder] Order created successfully");
            return Ok(new { url = approveLink });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CreateOrder] Unexpected error occurred");
            return StatusCode(500, new { error = "Unexpected error during order creation", details = ex.Message });
        }
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        // Read raw body because webhook payload needs to be inspected and verified
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("[Webhook] Received empty webhook payload");
            return BadRequest(new { error = "Empty payload" });
        }

        // Verify webhook signature
        if (!VerifyWebhookSignature(body))
        {
            _logger.LogWarning("[Webhook] Signature verification failed");
            return Unauthorized(new { error = "Invalid webhook signature" });
        }
        _logger.LogDebug("[Webhook] Signature verification passed");

        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Webhook] Failed to parse webhook JSON");
            return BadRequest(new { error = "Invalid JSON" });
        }

        // Extract event type
        var eventType = payload.GetProperty("event_type").GetString();
        _logger.LogInformation("[Webhook] Received event_type: {EventType}", eventType);


        string? payPalOrderId = null;

        var resource = payload.GetProperty("resource");

        if (resource.TryGetProperty("supplementary_data", out var supp) &&
            supp.TryGetProperty("related_ids", out var related) &&
            related.TryGetProperty("order_id", out var orderIdProp))
        {
            payPalOrderId = orderIdProp.GetString();
        }
        else
        {
            // fallback 
            payPalOrderId = resource.GetProperty("id").GetString();
        }

        var order = await _dbContext.Orders.FirstOrDefaultAsync(x => x.PayPalOrderId == payPalOrderId);
        if(order == null)
        {
            _logger.LogWarning("[Webhook] Order not found for PayPalOrderId: {PayPalOrderId}", payPalOrderId);
            return Ok(); // Acknowledge but no action taken
        }

        try
        {
            switch (eventType)
            {
                case "CHECKOUT.ORDER.APPROVED":
                    _logger.LogInformation("[Webhook] CHECKOUT.ORDER.APPROVED. Resource: {Resource}", resourceToString(payload));
                    
                    if (order.Status != OrderStatus.CREATED)
                    {
                        _logger.LogInformation("[Webhook] APPROVED event ignored. CurrentStatus={Status}", order.Status);
                        return Ok(new { handled = true, ignored = true });
                    }

                    order.Status = OrderStatus.APPROVED;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogDebug("[Webhook] Order status updated to APPROVED for OrderId: {OrderId}", order.Id);

                    // 2) نفّذ capture
                    var result = await TryCaptureOrderInternal(order.PayPalOrderId!);
                    if (!result.IsSuccess)
                    {
                        _logger.LogError("[Webhook] Capture failed after APPROVED");
                        return StatusCode(result.StatusCode);
                    }

                    return Ok(new { handled = true });

                case "PAYMENT.CAPTURE.COMPLETED":
                    _logger.LogInformation("[Webhook] Payment capture completed. Resource: {Resource}", resourceToString(payload));
                    if (order.Status == OrderStatus.COMPLETED)
                    {
                        _logger.LogInformation("[Webhook] COMPLETED event ignored (idempotent)");
                        return Ok(new { handled = true, ignored = true });
                    }

                    order.Status = OrderStatus.COMPLETED;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogDebug("[Webhook] Order status updated to COMPLETED for OrderId: {OrderId}", order.Id);
                    return Ok(new { handled = true });

                default:
                    _logger.LogInformation("[Webhook] Unhandled event type: {EventType}", eventType);
                    return Ok(new { handled = false });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Webhook] Error processing webhook event");
            return StatusCode(500, new { error = "Server error processing webhook" });
        }

        string resourceToString(JsonElement p)
        {
            try { return p.GetProperty("resource").ToString(); } catch { return string.Empty; }
        }
    }

    private async Task<(bool IsSuccess, string Content, int StatusCode)> TryCaptureOrderInternal(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return (false, "orderId is required", 400);

        var accessToken = await _payPalClient.GetAccessTokenAsync(); 
        if (string.IsNullOrWhiteSpace(accessToken))
            return (false, "Failed to obtain PayPal access token", 500);

        using var http = _httpClientFactory.CreateClient("PayPal");
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // capture order API call requires empty body
        var emptyContent = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        var captureResponse = await http.PostAsync($"https://api-m.sandbox.paypal.com/v2/checkout/orders/{orderId}/capture", emptyContent);
        var captureContent = await captureResponse.Content.ReadAsStringAsync();

        if (!captureResponse.IsSuccessStatusCode)
        {
            return (false, captureContent, (int)captureResponse.StatusCode);
        }

        return (true, captureContent, (int)captureResponse.StatusCode);
    }

    private bool VerifyWebhookSignature(string body)
    {
        // PayPal webhook signature headers
        if (!Request.Headers.TryGetValue("PAYPAL-TRANSMISSION-ID", out var transmissionId))
        {
            _logger.LogWarning("[VerifyWebhookSignature] Missing PAYPAL-TRANSMISSION-ID header");
            return false;
        }

        if (!Request.Headers.TryGetValue("PAYPAL-TRANSMISSION-TIME", out var transmissionTime))
        {
            _logger.LogWarning("[VerifyWebhookSignature] Missing PAYPAL-TRANSMISSION-TIME header");
            return false;
        }

        if (!Request.Headers.TryGetValue("PAYPAL-AUTH-ALGO", out var authAlgo))
        {
            _logger.LogWarning("[VerifyWebhookSignature] Missing PAYPAL-AUTH-ALGO header");
            return false;
        }

        if (!Request.Headers.TryGetValue("PAYPAL-TRANSMISSION-SIG", out var transmissionSig))
        {
            _logger.LogWarning("[VerifyWebhookSignature] Missing PAYPAL-TRANSMISSION-SIG header");
            return false;
        }

        if (!Request.Headers.TryGetValue("PAYPAL-CERT-URL", out var certUrl))
        {
            _logger.LogWarning("[VerifyWebhookSignature] Missing PAYPAL-CERT-URL header");
            return false;
        }

        try
        {
            // Construct the expected signature string: transmissionId|transmissionTime|webhookId|body_hash
            // For now we'll use a simpler approach: verify using the cert URL and signature
            // In production, you should:
            // 1. Download the certificate from certUrl
            // 2. Extract the public key
            // 3. Verify the signature of (transmissionId + transmissionTime + webhookId + bodyHash)

            // This is a simplified implementation that checks basic structure.
            // For a production system, integrate with PayPal's SDK or implement full verification:
            // var webhookId = GetWebhookIdFromConfig();
            // var expectedSignature = ComputeSignature(transmissionId, transmissionTime, webhookId, body, certUrl);
            // return transmissionSig == expectedSignature;

            // TODO: Implement full signature verification with certificate download and validation
            // For now, log that verification is enabled but basic
            _logger.LogInformation("[VerifyWebhookSignature] Webhook signature headers present. Full verification not yet implemented.");
            return true; // Placeholder: change to proper verification once PayPal SDK is integrated
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VerifyWebhookSignature] Error during signature verification");
            return false;
        }
    }
}
}
