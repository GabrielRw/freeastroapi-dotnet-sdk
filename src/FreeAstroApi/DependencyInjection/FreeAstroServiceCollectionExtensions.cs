using System;
using System.Net.Http;
using System.Threading;
using FreeAstroApi;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="FreeAstroClient"/> with dependency injection and <see cref="IHttpClientFactory"/>.</summary>
public static class FreeAstroServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FreeAstroClient"/> as a transient service backed by a named
    /// <see cref="IHttpClientFactory"/> client (<see cref="FreeAstroClient.HttpClientName"/>).
    /// Options come from <see cref="IOptions{TOptions}"/> of <see cref="FreeAstroClientOptions"/>, so they can
    /// also be bound with <c>services.Configure&lt;FreeAstroClientOptions&gt;(configuration.GetSection("FreeAstro"))</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client options.</param>
    /// <returns>The HTTP client builder, to add handlers (logging, resilience) to the underlying client.</returns>
    public static IHttpClientBuilder AddFreeAstro(this IServiceCollection services, Action<FreeAstroClientOptions>? configure = null)
    {
        FreeAstroApi.Internal.Guard.NotNull(services, nameof(services));
        var options = services.AddOptions<FreeAstroClientOptions>();
        if (configure is not null) options.Configure(configure);

        // The SDK enforces its own per-attempt timeout (FreeAstroClientOptions.Timeout).
        var builder = services.AddHttpClient(FreeAstroClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.TryAddTransient(provider => new FreeAstroClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(FreeAstroClient.HttpClientName),
            provider.GetRequiredService<IOptions<FreeAstroClientOptions>>().Value));
        return builder;
    }
}
