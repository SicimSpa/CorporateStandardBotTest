using CorporateStandardBotTest.BusinessLogic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CorporateStandardBotTest.BusinessLogic.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
        services.AddScoped<IKnowledgeBaseUrlService, KnowledgeBaseUrlService>();

        return services;
    }
}