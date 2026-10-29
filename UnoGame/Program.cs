using UnoGame.Api.Controllers;
using UnoGame.Api.Hubs;
using UnoGame.Api.Services;
using System.Text.Json.Serialization;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        new PrettyJsonFormatter(),
        "logs/uno-.json",
        rollingInterval: RollingInterval.Day,
        shared: true)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
var productionCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (builder.Environment.IsDevelopment()) policy.SetIsOriginAllowed(_ => true);
    else policy.WithOrigins(productionCorsOrigins);
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddSingleton<IMultiplayerLobbyService, MultiplayerLobbyService>();
builder.Services.AddHostedService<MultiplayerPresenceMonitor>();
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.Logger.LogInformation("UnoGame API started in {EnvironmentName} environment", app.Environment.EnvironmentName);
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<MultiplayerHub>("/hubs/multiplayer");
app.Run();

internal sealed class PrettyJsonFormatter : ITextFormatter
{
    private static readonly JsonValueFormatter ValueFormatter = new();
    private static readonly HashSet<string> AllowedPropertyNames =
    [
        "PlayerId",
        "Card",
        "NextPlayerId",
        "ConnectionId"
    ];
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var properties = new Dictionary<string, JsonElement>();
        foreach (var property in logEvent.Properties)
        {
            if (!AllowedPropertyNames.Contains(property.Key))
            {
                continue;
            }

            using var valueWriter = new StringWriter();
            ValueFormatter.Format(property.Value, valueWriter);
            using var document = JsonDocument.Parse(valueWriter.ToString());
            properties[property.Key] = document.RootElement.Clone();
        }

        var logEntry = new
        {
            Timestamp = logEvent.Timestamp.UtcDateTime,
            Level = logEvent.Level.ToString(),
            MessageTemplate = logEvent.MessageTemplate.Text,
            RenderedMessage = logEvent.RenderMessage(),
            Properties = properties,
            Exception = logEvent.Exception?.ToString()
        };

        output.WriteLine(JsonSerializer.Serialize(logEntry, SerializerOptions));
    }
}
