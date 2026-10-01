using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using PolvorApp.SharedKernel.Security;
using IPNetwork = System.Net.IPNetwork;

namespace PolvorApp.Api.Platform.Security;

/// <summary>
/// Rate limits for sign-in and recovery endpoints, per client address (spec: Sign-in with
/// two-factor authentication; design D6), and for writes that reveal whether a personal identifier
/// exists, per signed-in user (add-arquebusier-registry D11). The client address comes from <c>X-Forwarded-For</c>
/// only when the direct peer is a trusted proxy (<c>ForwardedHeaders__KnownNetworks</c>).
/// </summary>
internal static class RateLimits
{
    public static IServiceCollection AddPlatformRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = Limit(configuration, "RateLimits:Auth:PermitLimit", 10);
        var authEmail = Limit(configuration, "RateLimits:AuthEmail:PermitLimit", 5);
        var personalDataWrites = Limit(configuration, "RateLimits:PersonalDataWrites:PermitLimit", 60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Auth, context => Window(context, auth, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.AuthEmail, context => Window(context, authEmail, TimeSpan.FromMinutes(15)));
            options.AddPolicy(RateLimitPolicies.PersonalDataWrites, context => PerUser(context, personalDataWrites, TimeSpan.FromMinutes(1)));
        });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var network in (configuration["ForwardedHeaders:KnownNetworks"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.KnownIPNetworks.Add(IPNetwork.Parse(network));
            }
        });
        return services;
    }

    private static RateLimitPartition<string> Window(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });

    /// <summary>Per signed-in user (the policy only guards authenticated endpoints); by address otherwise.</summary>
    private static RateLimitPartition<string> PerUser(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is { } userId
                ? "user:" + userId
                : ClientKey(context.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });

    /// <summary>
    /// IPv4 addresses as they are; IPv6 addresses by their /64 prefix, because one subscriber
    /// usually owns a whole /64 and could otherwise rotate addresses to escape the limit.
    /// </summary>
    internal static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static int Limit(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
}
