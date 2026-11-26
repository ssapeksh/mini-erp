using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace Erp.Core.Pricing;

/// <summary>Bound from appsettings.json "Pricing" (OPTIONS PATTERN: IOptions&lt;PricingOptions&gt;).</summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";
    public decimal BulkQtyThreshold { get; set; } = 15;
    public decimal BulkDiscountPercent { get; set; } = 5;
    public DateOnly? FestivalStart { get; set; }
    public DateOnly? FestivalEnd { get; set; }
    public decimal FestivalDiscountPercent { get; set; } = 10;
}

public sealed record PricingContext(DateOnly OrderDate, decimal Qty, decimal UnitPrice);

/// <summary>
/// STRATEGY PATTERN: one interface, interchangeable discount algorithms.
/// OrderService asks the factory for "the strategy for this date" and calls it,
/// never knowing which concrete class it got (run-time POLYMORPHISM).
/// </summary>
public interface IPricingStrategy
{
    string Name { get; }
    decimal GetDiscountPercent(PricingContext context);
}

/// <summary>Abstract base: shared bulk-discount code + a virtual hook to override.</summary>
public abstract class PricingStrategyBase(PricingOptions options) : IPricingStrategy
{
    protected PricingOptions Options { get; } = options;

    public abstract string Name { get; }

    public virtual decimal GetDiscountPercent(PricingContext context) =>
        context.Qty >= Options.BulkQtyThreshold ? Options.BulkDiscountPercent : 0m;
}

/// <summary>Normal days: bulk discount only (same rule as the seed data).</summary>
public sealed class StandardPricingStrategy(IOptions<PricingOptions> options) : PricingStrategyBase(options.Value)
{
    public override string Name => "Standard";
}

/// <summary>Festival window: flat festival discount, or bulk if that's better.</summary>
public sealed class FestivalPricingStrategy(IOptions<PricingOptions> options) : PricingStrategyBase(options.Value)
{
    public override string Name => "Festival";

    public override decimal GetDiscountPercent(PricingContext context) =>
        Math.Max(Options.FestivalDiscountPercent, base.GetDiscountPercent(context));   // override + base call
}

public interface IPricingStrategyFactory
{
    IPricingStrategy For(DateOnly orderDate);
}

/// <summary>
/// FACTORY: hides the "which strategy?" decision. Adding a new strategy
/// (e.g. dealer pricing) means a new class + one line here; OrderService is
/// untouched (OPEN/CLOSED principle).
/// </summary>
public sealed class PricingStrategyFactory(
    StandardPricingStrategy standard,
    FestivalPricingStrategy festival,
    IOptions<PricingOptions> options) : IPricingStrategyFactory
{
    public IPricingStrategy For(DateOnly orderDate)
    {
        var o = options.Value;
        bool inFestival = o.FestivalStart is { } start && o.FestivalEnd is { } end
                          && orderDate >= start && orderDate <= end;
        return inFestival ? festival : standard;
    }
}

/// <summary>Phase 2 tax source: the GST rate stored on the item master.</summary>
public sealed class ItemMasterTaxRateProvider : ITaxRateProvider
{
    // ValueTask: the answer is already in memory, so no Task allocation is needed.
    public ValueTask<decimal> GetTaxPercentAsync(Item item, CancellationToken ct = default) =>
        ValueTask.FromResult(item.TaxPercent);
}
