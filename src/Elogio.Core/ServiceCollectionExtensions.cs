using System.Net;
using Elogio.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Elogio.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Kelio API services to the service collection.
    /// </summary>
    public static IServiceCollection AddKelioServices(this IServiceCollection services, string baseUrl)
    {
        // Cookie container for session management (singleton)
        var cookieContainer = new CookieContainer();
        services.AddSingleton(cookieContainer);

        // HTTP handler with cookies
        services.AddTransient<HttpClientHandler>(sp => new HttpClientHandler
        {
            CookieContainer = sp.GetRequiredService<CookieContainer>(),
            AllowAutoRedirect = true,
            UseCookies = true
        });

        // BWP encoding handler
        services.AddTransient<BwpDelegatingHandler>();

        // Auth API (no BWP encoding)
        services.AddRefitClient<IKelioAuthApi>()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri(baseUrl);
                c.DefaultRequestHeaders.Add("User-Agent", "Elogio/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler<HttpClientHandler>();

        // BWP API (with encoding handler)
        services.AddRefitClient<IKelioApi>()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri(baseUrl);
                c.DefaultRequestHeaders.Add("User-Agent", "Elogio/1.0");
            })
            .AddHttpMessageHandler<BwpDelegatingHandler>()
            .ConfigurePrimaryHttpMessageHandler<HttpClientHandler>();

        return services;
    }
}
