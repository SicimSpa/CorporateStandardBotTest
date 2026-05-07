using System.Text.Json.Serialization;
using CorporateStandardBotTest.BusinessLogic.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using CorporateStandardBotTest.Api.Extensions;
using CorporateStandardBotTest.BusinessLogic.Settings;
using MinimalHelpers.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.ConfigureObservability();

builder.Services.Configure<KnowledgeBaseUrlSettings>(builder.Configuration.GetSection(KnowledgeBaseUrlSettings.Position));

builder.Services.AddAzureSearchKnowledgeBase(builder.Configuration);
builder.Services.AddBusinessLogic();

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthorization();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapFallbackToFile("/index.html")
    .AllowAnonymous();

app.MapEndpoints();

app.Run();