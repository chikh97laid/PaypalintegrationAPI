using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayPalIntegrationAPI.Data;
using PayPalIntegrationAPI.Models;

namespace PayPalIntegrationAPI.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(AppDbContext dbContext, ILogger<OrdersController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            try
            {
                var orders = await _dbContext.Orders
                    .AsNoTracking()
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                return Ok(orders.Select(o => new {
                    o.Id,
                    o.PayPalOrderId,
                    o.CreatedAt,
                    Status = o.Status.ToString(),
                    o.TotalAmount,
                    o.Currency
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve orders");
                return StatusCode(500, new { error = "Failed to retrieve orders" });
            }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteOrders([FromBody] List<string> orderIds)
        {
            try
            {
                if (orderIds == null || orderIds.Count == 0)
                {
                    return BadRequest(new { error = "No order IDs provided" });
                }

                var orders = await _dbContext.Orders
                    .Where(o => orderIds.Contains(o.Id.ToString()))
                    .ToListAsync();

                if (orders.Count == 0)
                {
                    return NotFound(new { error = "No orders found to delete" });
                }

                _dbContext.Orders.RemoveRange(orders);
                await _dbContext.SaveChangesAsync();

                return Ok(new { message = $"Successfully deleted {orders.Count} order(s)" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete orders");
                return StatusCode(500, new { error = "Failed to delete orders" });
            }
        }
    }
}
