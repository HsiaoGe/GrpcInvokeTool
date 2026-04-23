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
- **详细报告**: 生成包含成功率、响应时间、RPS 等指标的详细测试报告

### 📝 其他特性
- **历史记录**: 自动保存调用历史、DLL 路径和服务器地址
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
   - 在 "Server" 输入框中输入 gRPC 服务器地址
   - 格式: `host:port` 或 `http://host:port`
   - 默认端口: 50051

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
- **调用组**: 一组接口调用的集合,支持并行/顺序执行
- **测试计划**: 多个步骤的执行序列,支持循环和间隔控制
- **报告生成**: 记录每次调用的详细信息,计算统计指标

## 📸 界面预览

### 主窗口
- **左侧**: 服务和方法树形列表
- **中间**: 请求消息编辑器(树形结构)
- **右侧**: 响应结果展示区
- **顶部**: DLL 加载、服务器地址配置、压力测试入口
- **底部**: 状态栏显示调用状态

### 压力测试窗口
- **左侧**: 调用组、测试计划、测试报告列表
- **右侧**: 详细配置和执行结果展示
- **支持**: 创建、编辑、删除、执行、查看报告

## 📁 配置文件目录结构

所有配置文件统一存储在应用程序执行目录下,便于管理和迁移。

**位置**: `[应用程序执行目录]`

例如如果程序在 `E:\Desktop\work\GrpcInvokeTool\GrpcInvokeTool\bin\Debug\net6.0-windows\` 运行,则配置目录为:
```
E:\Desktop\work\GrpcInvokeTool\GrpcInvokeTool\bin\Debug\net6.0-windows\
```

**目录结构**:
```
[应用目录]/
├── history.json              # 历史记录配置
├── groups.json               # 压力测试调用组配置
├── plans.json                # 压力测试计划配置
└── stress_test/
    └── reports/
        ├── [报告ID1].json    # 测试报告详情
        ├── [报告ID2].json
        └── ...
```

### 📄 配置文件说明

| 文件名 | 存储位置 | 用途 | 最大容量 |
|--------|---------|------|---------|
| `history.json` | 应用目录根目录 | DLL历史、服务器地址、调用记录 | DLL: 10条, 调用: 50条 |
| `groups.json` | 应用目录根目录 | 压力测试调用组 | 无限制 |
| `plans.json` | 应用目录根目录 | 压力测试计划 | 无限制 |
| `*.json` (reports) | 应用目录\stress_test\reports | 测试报告详情 | 无限制 |

所有配置文件均采用 **JSON 格式**存储,格式化输出(带缩进),便于人工查看和修改。

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