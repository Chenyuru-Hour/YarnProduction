# YarnProduction
国际复合的取纱机产量统计+报表导出的web项目

## 项目简介

本项目是一个部署在本地 Windows 电脑上的工业数据监控系统，用于实时采集 16 台取纱机（每台包含多个台位）的生产数据（取纱机编号、台位、纱卷重量），实现数据持久化存储、历史查询与 Excel 导出，并提供实时看板动态展示当前重量、当日累计重量与累计次数。

系统采用 **分离式架构**，将后台数据采集与前端应用解耦，保证采集服务 7×24 小时稳定运行，同时便于独立维护与扩展。

---

## 技术栈

| 组件 | 技术选型 | 说明 |
|------|----------|------|
| 后端框架 | .NET 8 (ASP.NET Core) | 跨平台高性能框架 |
| 前端框架 | Blazor Server | 全栈 C#，内置 SignalR 实时通信 |
| 实时推送 | SignalR | WebSocket 长连接，实时更新看板 |
| 数据访问 | Entity Framework Core 8 | ORM，配合 SQL Server 分区表 |
| 数据库 | SQL Server 2019+ | 分区表 + SQL Agent 自动归档 |
| 缓存 | Redis 5+ | 存储最新重量、当日累计产能 |
| PLC 通信 | 抽象驱动 + 具体协议库（S7.Net, NModbus 等） | 可扩展，支持模拟驱动 |
| 日志 | Serilog | 结构化日志输出 |
| 部署 | Windows 服务 | Web 与 Worker 分别注册为独立服务 |

---

## 项目结构
```
YarnProduction/
├── README.md
├── scripts/
│   └── sql/
│      └── YarnProduction_CreateDatabase.sql
├── docs/
│   ├── deployment.md
│   └── SRS.md
└── src/
    └── YarnProductionSystem/
        ├── Production.Core/ # 核心共享库
        │   ├── Entities/ # 实体类（ProductionRecord, User 等）
        │   ├── Interfaces/ # 抽象接口（IPlcDriver, IRealTimeCache）
        │   └── DTOs/ # 数据传输对象
        ├── Production.Infrastructure/ # 基础设施层
        │   ├── Data/ # EF Core DbContext 及配置
        │   ├── PlcDrivers/ # PLC 驱动实现（西门子、Modbus、模拟）
        │   ├── Redis/ # Redis 缓存封装
        │   └── Extensions/ # 服务注册扩展方法
        ├── Production.Web/ # Blazor Server 前端应用
        │   ├── Pages/ # Blazor 页面（实时看板、查询导出）
        │   ├── Hubs/ # SignalR Hub（ProductionHub）
        │   ├── Controllers/ # Web API（查询、导出）
        │   └── appsettings.json # 配置文件
        └── Production.Worker/ # 后台采集服务
            ├── Worker.cs # 继承 BackgroundService
            └── appsettings.json # 配置文件（可独立配置）

```

**依赖关系**：
- `Production.Web` 引用 `Production.Infrastructure` 和 `Production.Core`
- `Production.Worker` 引用 `Production.Infrastructure` 和 `Production.Core`
- `Production.Infrastructure` 引用 `Production.Core`


## 功能特性

### 1. PLC 数据采集（Worker 服务）
- 周期性读取 16 台 PLC 的取纱数据（机台号、台位、重量、时间戳）
- 批量写入 SQL Server 主表（按月分区）
- 更新 Redis 缓存：
  - 最新重量：`latest:{MachineId}:{StationId}`
  - 当日累计：`daily:{yyyy-MM-dd}:{MachineId}:{StationId}`（原子累加）
- 通过 Redis Pub/Sub 发布实时数据（或直接通过 SignalR 推送，推荐使用 Redis Pub/Sub 解耦）

### 2. 实时看板（Web 前端）
- 展示所有机台/台位的当前重量、当日累计重量与累计次数
- 通过 SignalR 接收实时更新（数据源自 Redis）
- 支持初始数据加载（从 Redis 或 API 获取）

### 3. 历史数据查询与导出（Web 前端 + API）
- 按日期范围、机台编号、台位组合筛选
- 分页展示生产记录
- 导出为 Excel 文件（支持大量数据）

### 4. 数据自动分区与归档（SQL Server Agent）
- 主表按月分区，每月自动创建新分区
- 每天凌晨将超过 12 个月的数据迁移至归档表
- 记录归档日志

### 5. 用户管理（可选）
- 基于角色的简单用户认证（Admin/Operator）
- 密码哈希存储（BCrypt）

---

## 环境要求

- **操作系统**：Windows 10/11 或 Windows Server 2019+（64 位）
- **开发环境**：Visual Studio 2022（17.8+）或 .NET 8 SDK
- **数据库**：SQL Server 2019 或更高版本（Developer/Express/Standard）
- **Redis**：5.0 或更高版本（Windows 版或 WSL2 安装）
- **运行时**：.NET 8 Runtime（如发布为自包含则无需安装）

---


## 快速开始

### 1. 克隆仓库
```bash
git clone https://github.com/Chenyuru-Hour/YarnProduction.git
```
### 2. 配置数据库
- 在 SQL Server 中创建数据库（例如 YarnProduction）
- 执行 YarnProduction\scripts\sql\YarnProduction.sql（或运行 EF Core 迁移）

### 3. 配置 Redis
- 确保 Redis 服务已启动（默认端口 6379）

### 4. 修改配置文件
- Production.Web/appsettings.json
- Production.Worker/appsettings.json
```bash
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=YarnProduction;User Id=appuser;Password=xxx;TrustServerCertificate=true;",
    "Redis": "localhost:6379,password=xxx"
  },
  "PlcDriver": {
    "Type": "Simulated", // 或 "Siemens", "Modbus"
    "Parameters": {
      "IpAddress": "192.168.1.100",
      "Rack": 0,
      "Slot": 1
    }
  },
  "CollectIntervalMs": 1000
}
```

### 5. 运行应用
#### 开发环境
- 启动 Redis 服务
- 在 VS 中同时启动 YarnMonitor.Web 和 YarnMonitor.Worker（可设置为多项目启动）
- 浏览器打开 https://localhost:5001

#### 生产环境（Windows 服务）
- 发布项目：
```bash
dotnet publish YarnMonitor.Web -c Release -r win-x64 --self-contained true -o publish/web
dotnet publish YarnMonitor.Worker -c Release -r win-x64 --self-contained true -o publish/worker
```
- 注册服务：
```bash
New-Service -Name "YarnMonitorWeb" -BinaryPathName "C:\publish\web\YarnMonitor.Web.exe" -StartupType Automatic
New-Service -Name "YarnMonitorWorker" -BinaryPathName "C:\publish\worker\YarnMonitor.Worker.exe" -StartupType Automatic
```
- 启动服务并设置为自动启动
## 数据分区与归档（SQL Server Agent）

- **分区扩展**：每月 1 日凌晨 1 点执行 `CreateNextMonthPartition` 存储过程，自动添加下个月分区边界。
- **数据归档**：每天凌晨 2 点执行 `ArchiveOldData` 存储过程，将超过 12 个月的数据从主表迁移至归档表，并记录日志。

若使用 SQL Server Express（无 Agent），需改用 Windows 任务计划调用 `sqlcmd` 执行存储过程。

---

## 实时数据推送机制

1. **Worker** 采集到数据后：
   - 写入 SQL Server 主表
   - 更新 Redis（最新重量、当日累计重量与累计次数）
   - 向 Redis 频道 `realtime:data` 发布原始数据（JSON 格式）

2. **Web** 端：
   - 订阅 Redis 频道 `realtime:data`
   - 收到消息后，通过 SignalR 转发给所有连接的浏览器客户端

3. **前端** 接收 SignalR 消息，更新对应机台/台位的显示值。

**优势**：Worker 与 Web 完全解耦，即使 Web 重启，Worker 仍可继续采集和缓存数据。

---

## 配置说明

### appsettings.json 关键配置

| 配置项 | 说明 |
|--------|------|
| `ConnectionStrings:DefaultConnection` | SQL Server 连接字符串 |
| `ConnectionStrings:Redis` | Redis 连接字符串（含密码） |
| `PlcDriver:Type` | PLC 驱动类型：`Simulated`, `Siemens`, `Modbus` |
| `PlcDriver:Parameters` | 驱动特定参数（IP、端口等） |
| `CollectIntervalMs` | 采集周期（毫秒），默认 1000 |
| `RedisChannel:RealtimeData` | 实时数据发布的频道名，默认 `realtime:data` |

---

## 测试与模拟

- 使用 **模拟驱动**（`PlcDriver:Type = "Simulated"`）可在无真实 PLC 时测试系统。
- 模拟驱动随机生成数据，便于前端调试。

---

## 常见问题

### 1. Worker 无法连接 PLC
- 检查网络连通性（ping PLC IP）
- 确认 PLC 型号与驱动兼容，参数（Rack、Slot）正确

### 2. Redis 连接失败
- 确认 Redis 服务已启动，端口未被防火墙拦截
- 检查连接字符串中密码是否正确

### 3. 实时看板不更新
- 检查 SignalR 连接状态（浏览器 F12 → Console）
- 确认 Redis 订阅与发布频道名称一致
- 检查 Worker 是否正常采集并发布消息

### 4. 数据库分区或归档失败
- 查看 SQL Server Agent 作业历史
- 检查存储过程权限（需 `ALTER DATABASE` 权限创建分区）
- 确保归档表结构与主表一致

---