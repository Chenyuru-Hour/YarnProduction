using Production.Web.Components;
using Production.Core.Interfaces;
using Production.Infrastructure.Redis;
using Production.Web.Hubs;
using Production.Web.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("Î´ÅäÖÃ Redis Á¬½Ó×Ö·û´®£ºConnectionStrings:Redis");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<IRealTimeCache, RealTimeCache>();
builder.Services.AddSingleton<DashboardRuntimeState>();
builder.Services.AddSingleton<DashboardSnapshotService>();
builder.Services.AddHostedService<RedisToSignalRForwarder>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<ProductionHub>("/hubs/production");

app.Run();
