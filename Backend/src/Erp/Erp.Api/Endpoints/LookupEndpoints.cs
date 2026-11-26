using Asp.Versioning;
using Asp.Versioning.Builder;
using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Entities;
using Erp.Core.Interfaces;
using MiniErp.Contracts;

namespace Erp.Api.Endpoints;

/// <summary>Query-string parameters bound with [AsParameters].</summary>
public sealed record CustomerQuery(string? Search, string? SortBy, bool Desc = false, int Page = 1, int PageSize = 20);

/// <summary>
/// MINIMAL API endpoints: a lambda per route, no controller class. Less
/// ceremony, good for small read-only endpoints like dropdown data. Controllers
/// still suit larger resources with filters, conventions and many actions.
/// </summary>
public static class LookupEndpoints
{
    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        ApiVersionSet versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();

        var group = app.MapGroup("/api/v{version:apiVersion}/lookups")
            .WithApiVersionSet(versions)
            .MapToApiVersion(1.0)
            .WithTags("Lookups")
            .RequireAuthorization()              // any logged-in user
            .CacheOutput("lookups");             // server-side output cache (policy in Program.cs)

        // TypedResults.Ok gives OpenAPI the exact response type
        group.MapGet("/categories", async (ILookupCache cache, CancellationToken ct) =>
            TypedResults.Ok(ApiResponse<IReadOnlyList<LookupDto>>.Ok(await cache.GetCategoriesAsync(ct))));

        group.MapGet("/warehouses", async (ILookupCache cache, CancellationToken ct) =>
            TypedResults.Ok(ApiResponse<IReadOnlyList<LookupDto>>.Ok(await cache.GetWarehousesAsync(ct))));

        // EF Core + IQueryable extensions: WhereIf + ApplySort (expression tree) + paging, all translated to SQL
        group.MapGet("/customers", async ([AsParameters] CustomerQuery q, IRepository<Customer> customers, CancellationToken ct) =>
        {
            string? search = q.Search?.Trim();
            var page = await customers.QueryNoTracking()
                .WhereIf(!string.IsNullOrEmpty(search), c => c.Name.Contains(search!) || c.Code.Contains(search!))
                .ApplySort(q.SortBy ?? nameof(Customer.Name), q.Desc)
                .Select(c => new CustomerDto(c.Id, c.Code, c.Name, c.City, c.Email, c.CreditLimit))
                .ToPagedResultAsync(q.Page, q.PageSize, ct);
            return TypedResults.Ok(ApiResponse<PagedResult<CustomerDto>>.Ok(page));
        });

        return app;
    }
}
