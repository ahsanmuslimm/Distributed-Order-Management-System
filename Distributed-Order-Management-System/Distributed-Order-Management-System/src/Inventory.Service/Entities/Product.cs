namespace Inventory.Service.Entities;

/// <summary>
/// Product master data
/// Immutable reference for inventory tracking
/// </summary>
public class Product
{
    /// <summary>
    /// Unique product identifier (PK)
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Product name/SKU
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Product description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Base price per unit
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Initial stock quantity
    /// Used as baseline for ledger calculations
    /// </summary>
    public int InitialStock { get; set; }

    /// <summary>
    /// When product was created (UTC)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation: All reservation ledger entries for this product
    /// </summary>
    public virtual List<ReservationLedger> ReservationLedgers { get; set; } = new();
}
