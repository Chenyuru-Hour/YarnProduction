using Microsoft.EntityFrameworkCore;
using Production.Core.Interfaces;
using Production.Infrastructure.Data;
using Production.Infrastructure.PlcDrivers;
using Production.Infrastructure.PlcDrivers.Options;
using Production.Infrastructure.Redis;
using Production.Infrastructure.Repositories;
using Production.Worker;
using StackExchange.Redis;

// 创建主机构建器，配置服务和应用设置
var builder = Host.CreateApplicationBuilder(args);

// 从配置中获取连接字符串，确保其存在
var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    throw new InvalidOperationException("未配置数据库连接字符串：ConnectionStrings:DefaultConnection");
}

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("未配置 Redis 连接字符串：ConnectionStrings:Redis");
}

var plcDriverType = builder.Configuration["PlcDriver:Type"];
if (string.IsNullOrWhiteSpace(plcDriverType))
{
    throw new InvalidOperationException("未配置 PlcDriver:Type，支持 Siemens 或 Simulated。");
}

// 配置 EF Core 使用 SQL Server，并注入 AppDbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(defaultConnectionString));

//注册 Redis 连接和实时缓存服务，确保 Redis 连接字符串有效
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<IRealTimeCache, RealTimeCache>();

if (string.Equals(plcDriverType, "Siemens", StringComparison.OrdinalIgnoreCase))
{
    var siemensOptionsSection = builder.Configuration.GetSection("PlcDriver:Parameters");
    var siemensOptions = siemensOptionsSection.Get<SiemensPlcOptions>();
    if (siemensOptions is null)
    {
        throw new InvalidOperationException("未配置 PlcDriver:Parameters。", new ArgumentNullException(nameof(siemensOptions)));
    }

    siemensOptions.ValidateAndThrow();
    //注册 PLC 驱动服务，注入配置并确保参数有效
    builder.Services.Configure<SiemensPlcOptions>(siemensOptionsSection);
    builder.Services.AddSingleton<IPlcDriver, SiemensPlcDriver>();
}
else if (string.Equals(plcDriverType, "Simulated", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPlcDriver, SimulatedPlcDriver>();
}
else
{
    throw new InvalidOperationException($"不支持的 PlcDriver:Type：{plcDriverType}。仅支持 Siemens/Simulated。");
}

//添加生产数据仓储服务，供 Worker 使用
builder.Services.AddScoped<IProductionRepository, ProductionRepository>();

// 注册 Worker 作为托管服务，负责定时任务执行
builder.Services.AddHostedService<Worker>();
// 构建应用并运行，Worker 将在后台执行定时任务
var host = builder.Build();
// 启动应用，Worker 将在后台执行定时任务
host.Run();
