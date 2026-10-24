using System.ComponentModel.DataAnnotations;

namespace Clinisys.Application.Common;

public sealed class CatalogPageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    public bool? IsActive { get; init; }

    public int Offset => (Page - 1) * PageSize;

    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue || Search?.Length > 200)
            throw new CatalogException(CatalogError.Validation, "Invalid pagination or search.",
                new Dictionary<string, string[]> { ["query"] = ["Page must be positive, pageSize between 1 and 100, and search at most 200 characters; the requested offset must fit an integer."] });
    }
}

public sealed record CatalogPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
