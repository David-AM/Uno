using UnoGame.Api.Controllers;
using UnoGame.Api.Hubs;
using UnoGame.Api.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // Development clients may be opened from another device on the LAN.
    if (builder.Environment.IsDevelopment()) policy.SetIsOriginAllowed(_ => true);
    else policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173" , "http://192.168.137.1:5173");
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddSingleton<IGameService, GameService>();
builder.Services.AddSingleton<IMultiplayerLobbyService, MultiplayerLobbyService>();
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<MultiplayerHub>("/hubs/multiplayer");
app.Run();
