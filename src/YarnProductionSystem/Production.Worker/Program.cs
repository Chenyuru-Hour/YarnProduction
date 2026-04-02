using Production.Worker;
using Microsoft.EntityFrameworkCore;
using Production.Core.Interfaces;
using Production.Infrastructure.Data;
using Production.Infrastructure.Repositories;

// 创建主机构建器，配置服务和应用设置
var builder = Host.CreateApplicationBuilder(args);

// 从配置中获取连接字符串，确保其存在
var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    throw new InvalidOperationException("未配置连接字符串：ConnectionStrings:DefaultConnection");
}

// 配置 EF Core 使用 SQL Server，并注入 AppDbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(defaultConnectionString));

//添加生产数据仓储服务，供 Worker 使用
builder.Services.AddScoped<IProductionRepository, ProductionRepository>();

// 注册 Worker 作为托管服务，负责定时任务执行
builder.Services.AddHostedService<Worker>();
// 构建应用并运行，Worker 将在后台执行定时任务
var host = builder.Build();
// 启动应用，Worker 将在后台执行定时任务
host.Run();
