# gRPC Invoke Tool

一个功能强大的 Windows WPF 桌面应用程序,用于动态加载 gRPC DLL 并可视化调用 gRPC 服务。支持所有 gRPC 调用模式、可视化消息编辑器、历史记录管理和压力测试功能。

![.NET 6.0](https://img.shields.io/badge/.NET-6.0-blue)
![WPF](https://img.shields.io/badge/UI-WPF-purple)
![gRPC](https://img.shields.io/badge/Protocol-gRPC-green)

## ✨ 主要特性

### 🔧 动态服务发现
- **DLL 动态加载**: 加载包含 gRPC 服务定义的 DLL 文件
- **自动服务发现**: 自动扫描并解析 DLL 中的所有 gRPC 服务和方法
- **服务树形展示**: 以树形结构展示服务和方法列表,支持按类型筛选

### 📡 全模式 gRPC 调用
支持 gRPC 的四种调用模式:
- **Unary**: 一元调用(请求-响应模式)
- **Server Streaming**: 服务端流式调用
- **Client Streaming**: 客户端流式调用
- **Duplex Streaming**: 双向流式调用

### 🎨 可视化消息编辑器
树形结构的 Protobuf 消息编辑器,支持:
- ✅ 所有基本数据类型(int, string, bool, float, double 等)
- ✅ 枚举类型(Enum)下拉选择
- ✅ 嵌套消息类型(Message)
- ✅ 数组类型(repeated 字段)
- ✅ 字典类型(map 字段)
- ✅ 选择字段(oneof 字段)动态切换
- ✅ 可选字段标记
- ✅ 字段添加/删除操作
- ✅ 实时 JSON 同步

### 📊 压力测试功能
完整的压力测试解决方案:
- **调用组管理**: 创建和管理多个接口调用组
- **测试计划**: 创建包含多个步骤的测试计划
- **执行模式**: 支持并行执行和顺序执行
- **循环控制**: 支持固定次数循环或无限循环
- **间隔设置**: 支持固定间隔或随机间隔
- **服务器管理**: 
  - Group顶部统一服务器地址选择
  - 自动记录最近使用的服务器地址
  - Plan执行时智能判断服务器是否相同，自动复用channel和双向流session
- **详细报告**: 
  - 生成包含成功率、响应时间、RPS等指标的详细测试报告
  - Duration显示优化：小于1秒显示毫秒，大于1秒精确到秒（保留三位小数）
  - 支持Execute Again快速重新执行
- **独立存储**: 
  - 每个Group和Plan使用单独JSON文件保存，避免全量IO写入
  - 自动迁移旧格式文件到新格式

### 📝 其他特性
- **历史记录**: 
  - 自动保存DLL路径历史（最多10条）
  - 自动保存服务器地址历史（最多10条，仅保存实际调用过的地址）
  - 自动保存接口调用历史（最多20条，每个接口只保存一份最新记录）
  - 启动时自动加载最近使用的DLL和服务器地址
- **智能提示**: 服务器地址下拉选择，支持历史记录和手动输入
- **响应展示**: Unary 结果格式化显示,流式结果列表展示
- **取消支持**: 支持取消正在进行的流式调用
- **主题支持**: 基于 HandyControl 的现代化 UI 主题

## 🚀 快速开始

### 系统要求
- Windows 操作系统
- .NET 6.0 Runtime

### 使用方法

1. **加载 DLL**
   - 点击 "Load DLL" 按钮
   - 选择包含 gRPC 服务定义的 DLL 文件
   - 应用会自动发现并展示所有服务和方法

2. **配置服务器地址**
   - 在 "Server" 下拉框中选择或输入 gRPC 服务器地址
   - 格式: `host:port` 或 `http://host:port`
   - 默认端口: 50051
   - 下拉列表显示历史服务器地址（最多10条）
   - 只有实际调用过的服务器才会保存到历史记录

3. **选择方法**
   - 在左侧服务树中选择要调用的方法
   - 查看方法类型和请求/响应类型信息

4. **编辑请求消息**
   - 在中间的请求编辑器中构建请求消息
   - 点击 "+" 按钮添加字段
   - 点击 "x" 按钮删除字段
   - 支持嵌套消息和复杂数据结构

5. **调用方法**
   - 点击 "Invoke" 按钮发起调用
   - Unary 结果显示在右侧文本框
   - 流式结果显示在右侧列表中
   - 可点击 "Cancel" 取消流式调用

6. **压力测试**
   - 点击 "Stress Test" 打开压力测试窗口
   - 创建调用组和测试计划
   - 执行测试并查看详细报告

## 📦 项目结构

```
GrpcInvokeTool/
├── Models/                  # 数据模型
│   ├── GrpcServiceInfo.cs       # gRPC 服务信息
│   ├── GrpcMethodInfo.cs        # gRPC 方法信息
│   ├── MessageFieldNode.cs      # 消息字段节点(编辑器树节点)
│   ├── CallGroup.cs             # 调用组
│   ├── StressTestPlan.cs        # 压力测试计划
│   ├── StressTestReport.cs      # 压力测试报告
│   └── ...
├── Services/                # 业务逻辑服务
│   ├── DllLoadService.cs        # DLL 加载服务
│   ├── GrpcDiscoveryService.cs  # 服务发现服务
│   ├── GrpcInvokeService.cs     # gRPC 调用服务
│   ├── MessageEditorService.cs  # 消息编辑服务
│   ├── HistoryService.cs        # 历史记录服务
│   ├── StressTestService.cs     # 压力测试服务
│   └── ...
├── ViewModels/              # MVVM 视图模型
│   ├── MainViewModel.cs         # 主窗口视图模型
│   └── StressTestViewModel.cs   # 压力测试视图模型
├── Converters/              # WPF 值转换器
│   ├── MethodTypeToColorConverter.cs
│   ├── BoolToVisibilityConverter.cs
│   └── ...
├── MainWindow.xaml          # 主窗口界面
├── StressTestWindow.xaml    # 压力测试窗口界面
├── Themes/                  # UI 主题
└── App.xaml                 # 应用程序入口
```

## 🔧 技术栈

- **框架**: .NET 6.0 (Windows)
- **UI**: WPF + HandyControl 3.5.1
- **MVVM**: CommunityToolkit.Mvvm 8.4.2
- **gRPC**: 
  - Grpc.Net.Client 2.76.0
  - Grpc.Core 2.46.6
  - Google.Protobuf 3.34.1

## 🎯 核心功能详解

### 服务发现机制
通过反射扫描 DLL 中的 `ClientBase<T>` 类型,提取:
- 服务名称和包名(从 ServiceDescriptor)
- 方法名称、类型(Unary/Streaming)
- 请求和响应类型
- MethodDescriptor 用于直接调用

### 消息编辑器架构
基于树形结构的可视化编辑器:
- **MessageFieldNode**: 表示一个字段节点
- **字段类型识别**: 自动识别 scalar、enum、message、repeated、map、oneof
- **动态添加**: 根据消息描述符动态添加可选字段
- **JSON 同步**: 实时将树形结构序列化为 JSON

### gRPC 调用实现
使用 `CallInvoker` + `MethodDescriptor` 的方式实现调用:
- 避免依赖客户端方法名称匹配
- 支持所有四种调用模式
- 支持流式调用的取消操作
- 支持双向流会话管理

### 压力测试引擎
- **调用组**: 
  - 一组接口调用的集合,支持并行/顺序执行
  - 顶部统一服务器地址选择，所有接口使用相同服务器
  - ServerAddress字段与Calls平级，记录最近使用的服务器
- **测试计划**: 
  - 多个步骤的执行序列,支持循环和间隔控制
  - 智能判断服务器是否相同，自动复用channel和双向流session
  - 相同服务器复用连接，提高性能；不同服务器创建新连接
- **报告生成**: 
  - 记录每次调用的详细信息,计算统计指标
  - Duration显示优化：小于1秒显示毫秒，大于1秒精确到秒（保留三位小数）
  - 支持Execute Again快速重新执行报告对应的Group或Plan

## 📸 界面预览

### 主窗口
- **左侧**: 服务和方法树形列表
- **中间**: 请求消息编辑器(树形结构)
- **右侧**: 响应结果展示区
- **顶部**: DLL 加载、服务器地址配置、压力测试入口
- **底部**: 状态栏显示调用状态

### 压力测试窗口
- **左侧**: 调用组、测试计划、测试报告列表（三个标签页）
- **右侧**: 详细配置和执行结果展示
- **支持**: 
  - 创建、编辑、删除、执行、查看报告
  - Group顶部服务器地址选择（下拉历史记录）
  - Execute Again快速重新执行
  - 自动保存最近使用的服务器地址

## 📁 配置文件目录结构

所有配置文件统一存储在应用程序执行目录的 `stress_test` 子目录下，便于管理和迁移。

**位置**: `[应用程序执行目录]/stress_test/`

例如如果程序在 `E:\Desktop\work\GrpcInvokeTool\GrpcInvokeTool\bin\Debug\net6.0-windows\` 运行,则配置目录为:
```
E:\Desktop\work\GrpcInvokeTool\GrpcInvokeTool\bin\Debug\net6.0-windows\stress_test\
```

**目录结构**:
```
[应用目录]/
├── history.json              # 历史记录配置（DLL、服务器地址、调用记录）
└── stress_test/              # 压力测试相关配置目录
    ├── groups/               # 调用组配置（每个group单独文件）
    │   ├── [groupId1].json
    │   ├── [groupId2].json
    │   └── ...
    ├── plans/                # 测试计划配置（每个plan单独文件）
    │   ├── [planId1].json
    │   ├── [planId2].json
    │   └── ...
    └── reports/              # 测试报告详情
        ├── [报告ID1].json
        ├── [报告ID2].json
        └── ...
```

### 📄 配置文件说明

| 文件/目录 | 存储位置 | 用途 | 容量限制 |
|----------|---------|------|---------|
| `history.json` | 应用目录根目录 | DLL历史、服务器地址历史、调用记录 | DLL: 10条, 服务器: 10条, 调用: 20条 |
| `groups/*.json` | stress_test/groups/ | 压力测试调用组（单独文件） | 无限制 |
| `plans/*.json` | stress_test/plans/ | 压力测试计划（单独文件） | 无限制 |
| `reports/*.json` | stress_test/reports/ | 测试报告详情 | 无限制 |

**存储优化**:
- ✅ Group和Plan使用单独JSON文件，每次修改只写入单个文件，避免全量IO
- ✅ 自动迁移旧的 `groups.json` 和 `plans.json` 到新的单独文件格式
- ✅ 所有配置文件均采用JSON格式存储，格式化输出（带缩进），便于查看

**历史记录优化**:
- ✅ DLL路径历史：最多10条，启动时自动加载最近的DLL
- ✅ 服务器地址历史：最多10条，仅保存实际调用过的地址（成功或失败都保存）
- ✅ 调用记录历史：最多20条，每个接口只保存一份最新记录（相同接口自动覆盖）
- ✅ 历史记录完整还原：正确还原删除的字段、动态修改的oneof、struct、list等

## 💡 使用场景

- ✅ gRPC 服务开发和调试
- ✅ 接口功能测试和验证
- ✅ 服务性能压力测试
- ✅ Protobuf 消息结构探索
- ✅ 动态服务调用(无需编写客户端代码)

## 🤝 贡献

欢迎提交 Issue 和 Pull Request!

## 📄 许可证

MIT License

## 🙏 致谢

- [HandyControl](https://github.com/HandyOrg/HandyControl) - 优秀的 WPF UI 控件库
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - 强大的 MVVM 工具包
- [gRPC for .NET](https://github.com/grpc/grpc-dotnet) - 官方 gRPC .NET 实现

---

**开发者**: 如果您在使用过程中遇到问题或有改进建议,欢迎在 GitHub 上提交 Issue!