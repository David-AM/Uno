using UnoGame.Api.Controllers;
using UnoGame.Api.Hubs;
using UnoGame.Api.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
var productionCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // Development clients may be opened from another device on the LAN.
    if (builder.Environment.IsDevelopment()) policy.SetIsOriginAllowed(_ => true);
    else policy.WithOrigins(productionCorsOrigins);
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddSingleton<IMultiplayerLobbyService, MultiplayerLobbyService>();
builder.Services.AddHostedService<MultiplayerPresenceMonitor>();
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<MultiplayerHub>("/hubs/multiplayer");
app.Run();
