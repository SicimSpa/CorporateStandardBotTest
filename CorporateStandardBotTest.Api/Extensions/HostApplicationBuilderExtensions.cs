using System.Diagnostics;
using CorporateStandardBotTest.BusinessLogic.Observability;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CorporateStandardBotTest.Api.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static IHostApplicationBuilder ConfigureObservability(this IHostApplicationBuilder builder)
    {
        ConfigureLogging(builder);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(m => ConfigureMetrics(m, builder))
            .WithTracing(t => ConfigureTracing(t, builder));

        builder.Services.AddSingleton(new Instrumentation(builder.Environment.ApplicationName));

        return builder;
    }

    private static void ConfigureLogging(IHostApplicationBuilder builder)
    {
        var logsEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var logsHeaders = builder.Configuration["OTEL_EXPORTER_OTLP_LOGS_HEADERS"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"];

        if (string.IsNullOrWhiteSpace(logsEndpoint))
            return;
        
        // OpenTelemetry
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        
            logging.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(logsEndpoint);
                otlpOptions.Headers = logsHeaders;
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
        });

        // Serilog, not used in this project
        // var loggerConfig = new LoggerConfiguration()
        //     .ReadFrom.Configuration(builder.Configuration)
        //     .Enrich.FromLogContext();
        //
        // if (!string.IsNullOrWhiteSpace(logsEndpoint))
        // {
        //     loggerConfig.WriteTo.OpenTelemetry(config =>
        //     {
        //         config.Endpoint = logsEndpoint;
        //         config.Protocol = OtlpProtocol.HttpProtobuf;
        //
        //         if (!string.IsNullOrWhiteSpace(logsHeaders))
        //             config.Headers = new Dictionary<string, string>(logsHeaders.Split(',').Select(h =>
        //             {
        //                 var separator = h.IndexOf('=');
        //                 return new KeyValuePair<string, string>(h[..separator], h[(separator + 1)..]);
        //             }));
        //     });
        // }
        //
        // Log.Logger = loggerConfig.CreateLogger();
        // builder.Services.AddSerilog();
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, IHostApplicationBuilder builder)
    {
        var metricsEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var metricsHeaders = builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_HEADERS"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"];

        if (string.IsNullOrWhiteSpace(metricsEndpoint))
            return;

        metrics.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(metricsEndpoint);
                otlpOptions.Headers = metricsHeaders;
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, IHostApplicationBuilder builder)
    {
        var tracesEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var tracesHeaders = builder.Configuration["OTEL_EXPORTER_OTLP_TRACES_HEADERS"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"];

        if (tracesEndpoint is null)
            return;

        tracing.AddSource(builder.Environment.ApplicationName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation(options =>
            {
                options.EnrichWithHttpResponseMessage = (activity, response) =>
                {
                    var uri = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;

                    var matched = uri.StartsWith("/api/DomainUser") || uri.StartsWith("/api/DomainGroup");

                    if (matched && (int)response.StatusCode < 400)
                    {
                        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                        activity.IsAllDataRequested = false;
                    }
                };
            })
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(tracesEndpoint);
                otlpOptions.Headers = tracesHeaders;
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
    }
}