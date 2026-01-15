using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PayPalIntegrationAPI.Models
{
    public enum OrderStatus { CREATED, APPROVED, COMPLETED }

    public class Order
    {
        [Key]
        public int Id { get; set; }

        // PayPal order id (token)
        [MaxLength(128)]
        public string? PayPalOrderId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // e.g. CREATED, APPROVED, COMPLETED, etc.
        public OrderStatus Status { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalAmount { get; set; }

        [MaxLength(8)]
        public string? Currency { get; set; }
    }
}