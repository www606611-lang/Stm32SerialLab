# STM32 Serial Lab

<p align="center">
  <img src="Assets/AppIcon.png" width="112" alt="STM32 Serial Lab icon">
</p>

面向 STM32 学习与调试的独立 Windows 串口、遥测和实时波形工具。它独立于裸机和 FreeRTOS 固件构建系统：固件负责输出证据，工具负责把字节、文本和数值证据显示清楚。

## 下载

Windows 10/11 x64 用户可从 [GitHub Releases](https://github.com/www606611-lang/Stm32SerialLab/releases) 下载 `Stm32SerialLab-v0.1.0-win-x64.zip`。解压完整目录后运行 `Stm32SerialLab.exe`，不要只复制单个 EXE。

## 构建链

输入文件：

- `Stm32SerialLab.csproj`：目标框架、Windows App SDK 和 `System.IO.Ports` 依赖。
- `MainPage.xaml`：Console、Scope、Telemetry 三个工作区的界面。
- `MainPage.xaml.cs`：有界日志、遥测统计、实时曲线和交互逻辑。
- `Services/SerialPortService.cs`：COM 口枚举、8-N-1 打开、收发和错误报告。
- `Services/TelemetryParser.cs`：`key=value` 与 CSV 解析。

执行：

```powershell
dotnet restore .\Stm32SerialLab.csproj -p:Platform=x64
dotnet build .\Stm32SerialLab.csproj -c Debug -p:Platform=x64
```

`dotnet restore` 把 NuGet 依赖写入本机缓存，并生成 `obj/.../project.assets.json`。`dotnet build` 接着编译 C#、编译 XAML，并在下面的目录生成普通 unpackaged Windows 程序：

```text
bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Stm32SerialLab.exe
```

运行：

```powershell
.\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Stm32SerialLab.exe
```

## Console

- 选择 COM 口和波特率后连接；串口固定为 8 数据位、无校验、1 停止位。
- RX、TX、SIM、SYS 分方向显示时间戳、字节数和内容。
- ASCII 模式把回车、换行、制表符和不可打印字节显示为 `\r`、`\n`、`\t`、`\xNN`。
- HEX 模式按字节显示；发送区也支持 Text/HEX 输入和行尾选择。
- `Enter` 发送，方向键上/下浏览最近 100 条发送历史。
- Pause 只冻结可见时间线，底层接收与统计继续运行；恢复时重新显示有界日志。
- 原始日志最多保留 5000 条，防止长时间运行时内存无限增长。

## Scope

- Time 滑杆使用对数档位选择 100 ms 到 300 s 固定时间窗；LIVE 状态下右边缘始终是 `now`，波形持续向左滚动。
- Gain 滑杆使用 1/16x、1/8x、1/4x、1/2x、1x、2x、4x、8x、16x、32x、64x 档位纵向缩放；低于 1x 会把波形压小，高于 1x 会放大，超出坐标网格的折线会被裁剪。
- `Auto: Follow` 默认开启，让可见通道约占绘图区高度的 65%；信号超界时立即扩大量程，信号缩小时约用 2 秒平滑收缩，避免坐标轴频繁跳动。
- `Fit once` 按当前可见数据适配一次，然后冻结 Y 轴并启用 Gain；`Manual` 直接冻结当前 Y 轴。
- `Full range` 优先采用 `@meta` 声明的物理范围；缺少元数据时回退到可见数据范围，不猜测通道含义。
- Hold 状态下 Follow 也会冻结，便于检查定格波形。
- 鼠标滚轮缩放时间轴，`Ctrl + 滚轮` 调整纵向增益。
- 在波形区拖动超过 4 DIP 后进入 HOLD 并回看历史；普通单击不会误冻结。
- Hold 固定当前时间位置，Live 回到实时边缘，Reset 恢复 10 秒、1x、Auto: Follow、LIVE。
- 每个通道最多保留 6000 个样本；CSV 导出采用 `timestamp,channel,value` 长表格式。

## 遥测输入

推荐固件每次输出一整行，以 `\r\n` 结束：

```text
tick=1000 heap=4200 adc=1878 avg=1874 overrun=0
```

字段名可以增加或减少。解析器会为首次出现的数值字段创建通道，并维护 latest、minimum、maximum、sample count。

固件可以在普通遥测前发送一次通道元数据：

```text
@meta adc unit=count min=0 max=4095
@meta heap unit=B min=0 max=6144
@meta tick unit=ms
```

`unit` 会显示在通道名后；同时提供合法 `min/max` 后，Scope 的 Full range 模式会直接采用这个范围。元数据缺失或 `min >= max` 会留下解析错误计数。

也支持 CSV。先发一次表头，后续发送同列数值：

```text
tick,heap,adc,avg,overrun
1000,4200,1878,1874,0
1100,4200,1882,1875,0
```

没有表头的纯数值 CSV 会自动命名为 `ch0`、`ch1`、`ch2`。

## 与 FreeRTOS 学习路线的连接

第一课最少输出：

```text
tick=... led_runs=... status_runs=... heap=...
```

后续实验逐步增加：

```text
queue_depth=... queue_drop=...
button_irq=... button_handled=...
dma_samples=... processed_samples=... overrun=...
stack_led=... stack_uart=... heap_min=...
```

验收时同时保留三类证据：Console 中的原始行、Telemetry 中的统计值、Scope 中随时间变化的曲线。工具显示正常只证明 PC 端管线工作；真实串口、波特率和固件输出仍需在 STM32F103 板上验证。

## 真机首验

1. 固件 USART1 使用 PA9/PA10，并确认 USB 转串口共地。
2. 先以 `115200 8-N-1` 每秒输出一行示例遥测。
3. 关闭 Demo，刷新并选择真实 COM 口，然后连接。
4. 检查 Console 的 RX 字节和 LINES 持续增加，ERR 保持 0。
5. 检查 Telemetry 的样本数增长，并在 Scope 中选择 `adc`/`avg` 观察滚动。
6. 拔掉或关闭串口做一次故障验证，确认 SYS 日志和 ERR 计数留下证据。
