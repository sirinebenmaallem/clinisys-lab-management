using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Clinisys.API.AnalysisResults;

// Temporary local-only policy, not authentication. Never trust proxy headers here.
public sealed class AnalysisResultLocalAccess(IHostEnvironment environment, IConfiguration configuration)
{
    public const string ConfigurationKey = "AnalysisResults:EnableLocalAccess";
    public bool IsAllowed(HttpContext? context)
    {
        if (context is null || !(environment.IsDevelopment() || environment.IsEnvironment("Testing"))) return false;
        if (!bool.TryParse(configuration[ConfigurationKey], out var enabled) || !enabled) return false;
        if (context.Request.Headers.Keys.Any(x => x.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
            || x.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
            || x.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase))) return false;
        return IsLoopback(context.Connection.RemoteIpAddress) && IsLoopback(context.Connection.LocalIpAddress);
    }

    private static bool IsLoopback(IPAddress? address) => address is not null
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}

public sealed class AnalysisResultLocalAccessFilter(AnalysisResultLocalAccess access) : IAsyncResourceFilter
{
    internal static readonly object RequestMarker = new();
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!access.IsAllowed(context.HttpContext))
        {
            context.Result = new NotFoundResult();
            return;
        }
        context.HttpContext.Items[RequestMarker] = true;
        await next();
    }
}
