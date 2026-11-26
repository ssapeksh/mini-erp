using Erp.Core.Dtos;
using FluentValidation;

namespace Erp.Core.Validators;

/*  FluentValidation for rules that data annotations express badly: formats,
    cross-field rules, collections. ValidationFilter in Erp.Api runs these
    before the action; failures become a 400 with per-field errors.          */

public static class GstRates
{
    /// <summary>Valid Indian GST slabs.</summary>
    public static readonly IReadOnlySet<decimal> Slabs = new HashSet<decimal> { 0, 5, 12, 18, 28 };
}

public sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^[A-Z]{3}-\d{3}$").WithMessage("Code must look like ELE-001 (3 capital letters, dash, 3 digits).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10).Must(u => u == u.ToUpperInvariant())
            .WithMessage("Unit must be upper case, e.g. NOS, MTR, BOX.");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxPercent).Must(GstRates.Slabs.Contains).WithMessage("Tax must be a GST slab: 0, 5, 12, 18 or 28.");
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateItemRequestValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxPercent).Must(GstRates.Slabs.Contains).WithMessage("Tax must be a GST slab: 0, 5, 12, 18 or 28.");
        RuleFor(x => x.RowVersion).NotEmpty()
            .Must(BeBase64).WithMessage("RowVersion must be the base64 value returned by GET.");
    }

    private static bool BeBase64(string value) => Convert.TryFromBase64String(value, new byte[value.Length], out _);
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.")
            .Must(l => l.Count <= 50).WithMessage("An order can have at most 50 lines.")
            .Must(l => l.Select(x => x.ItemId).Distinct().Count() == l.Count)
            .WithMessage("Each item may appear only once; combine the quantities.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).GreaterThan(0);
            line.RuleFor(l => l.Qty).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        });
        RuleFor(x => x.OrderDate)
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            .WithMessage("Order date cannot be in the future.");
    }
}

public sealed class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.Qty).NotEqual(0).WithMessage("Adjustment quantity cannot be zero.");
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
