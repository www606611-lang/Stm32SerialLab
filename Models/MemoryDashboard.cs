using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Stm32SerialLab.Services;

namespace Stm32SerialLab.Models;

public readonly record struct MemoryHistorySample(
    DateTimeOffset Timestamp,
    uint HeapFree,
    uint HeapMinimumFree,
    uint HeapTotal);

public sealed record MemoryRegionRow(
    string Kind,
    string Owner,
    uint Start,
    uint End,
    string State)
{
    public uint Size => End >= Start ? End - Start : 0;
    public string RangeText => Start == 0 && End == 0
        ? "address pending"
        : $"{MemoryDashboard.FormatAddress(Start)} - {MemoryDashboard.FormatAddress(End)}";
    public string SizeText => MemoryDashboard.FormatBytes(Size);
    public string DetailText => $"{Kind}  |  {State}";
}

public sealed class MemoryDashboard : INotifyPropertyChanged
{
    private bool _hasData;
    private string _sourceLabel = "WAITING FOR MEMORY TELEMETRY";
    private DateTimeOffset _lastUpdated;
    private MemorySnapshot? _snapshot;

    public bool HasData => _hasData;
    public string SourceLabel => _sourceLabel;
    public string LastUpdateText => _hasData ? $"Updated {_lastUpdated:HH:mm:ss}" : "No snapshot received";
    public ObservableCollection<MemoryRegionRow> Regions { get; } = [];
    public ObservableCollection<TaskMemoryRow> Tasks { get; } = [];
    public ObservableCollection<ObjectMemoryRow> Objects { get; } = [];

    public uint FlashUsed { get; private set; }
    public uint FlashCodeConst { get; private set; }
    public uint FlashDataInit { get; private set; }
    public uint FlashAppTotal { get; private set; } = 1;
    public uint FlashParameter { get; private set; }
    public uint RamStatic { get; private set; }
    public uint RamData { get; private set; }
    public uint RamBss { get; private set; }
    public uint RamTotal { get; private set; } = 1;
    public uint HeapTotal { get; private set; } = 1;
    public uint HeapInitialFree { get; private set; }
    public uint HeapFree { get; private set; }
    public uint HeapMinimumFree { get; private set; }

    public uint HeapUsed => HeapTotal >= HeapFree ? HeapTotal - HeapFree : 0;
    public uint RamLinkerFree => RamTotal >= RamStatic ? RamTotal - RamStatic : 0;
    public double FlashProgressValue => FlashUsed;
    public double FlashProgressMaximum => Math.Max(1, FlashAppTotal);
    public double RamProgressValue => RamStatic;
    public double RamProgressMaximum => Math.Max(1, RamTotal);
    public double HeapProgressValue => HeapUsed;
    public double HeapProgressMaximum => Math.Max(1, HeapTotal);
    public string FlashUsedText => $"{FormatBytes(FlashUsed)} / {FormatBytes(FlashAppTotal)}";
    public string FlashPercentText => FormatPercent(FlashUsed, FlashAppTotal);
    public string FlashDetailText => $"code + const {FormatBytes(FlashCodeConst)}  |  .data image {FormatBytes(FlashDataInit)}  |  reserved page {FormatBytes(FlashParameter)}";
    public string RamUsedText => $"{FormatBytes(RamStatic)} / {FormatBytes(RamTotal)}";
    public string RamPercentText => FormatPercent(RamStatic, RamTotal);
    public string RamDetailText => $".data {FormatBytes(RamData)}  |  .bss {FormatBytes(RamBss)}  |  linker free {FormatBytes(RamLinkerFree)}";
    public string HeapUsedText => $"{FormatBytes(HeapUsed)} used  |  {FormatBytes(HeapFree)} free";
    public string HeapPercentText => FormatPercent(HeapUsed, HeapTotal);
    public string HeapDetailText => $"minimum-ever free {FormatBytes(HeapMinimumFree)}  |  pool {FormatBytes(HeapTotal)} inside .bss";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(MemorySnapshot snapshot, string sourceLabel, DateTimeOffset timestamp)
    {
        _hasData = true;
        _sourceLabel = sourceLabel;
        _lastUpdated = timestamp;
        _snapshot = snapshot;
        FlashUsed = snapshot.FlashUsed;
        FlashCodeConst = snapshot.FlashCodeConst;
        FlashDataInit = snapshot.FlashDataInit;
        FlashAppTotal = snapshot.FlashAppTotal;
        FlashParameter = snapshot.FlashParameter;
        RamStatic = snapshot.RamStatic;
        RamData = snapshot.RamData;
        RamBss = snapshot.RamBss;
        RamTotal = snapshot.RamTotal;
        HeapTotal = snapshot.HeapTotal;
        HeapInitialFree = snapshot.HeapInitialFree;
        HeapFree = snapshot.HeapFree;
        HeapMinimumFree = snapshot.HeapMinimumFree;
        NotifyOverviewChanged();

        foreach (TaskMemoryRow task in Tasks)
        {
            task.SetHeapTotal(HeapTotal);
        }

        foreach (ObjectMemoryRow item in Objects)
        {
            item.SetHeapTotal(HeapTotal);
        }

        RebuildRegions();
    }

    public void Apply(TaskMemorySnapshot snapshot)
    {
        TaskMemoryRow? row = Tasks.FirstOrDefault(item =>
            item.Name.Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new TaskMemoryRow(snapshot.Name);
            Tasks.Add(row);
        }

        row.Update(snapshot, HeapTotal);
        RebuildRegions();
    }

    public void Apply(ObjectMemorySnapshot snapshot)
    {
        ObjectMemoryRow? row = Objects.FirstOrDefault(item =>
            item.Kind.Equals(snapshot.Kind, StringComparison.OrdinalIgnoreCase) &&
            item.Name.Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new ObjectMemoryRow(snapshot.Kind, snapshot.Name);
            Objects.Add(row);
        }

        row.Update(snapshot, HeapTotal);
        RebuildRegions();
    }

    public void Reset(string sourceLabel)
    {
        _hasData = false;
        _sourceLabel = sourceLabel;
        _lastUpdated = default;
        _snapshot = null;
        FlashUsed = 0;
        FlashCodeConst = 0;
        FlashDataInit = 0;
        FlashAppTotal = 1;
        FlashParameter = 0;
        RamStatic = 0;
        RamData = 0;
        RamBss = 0;
        RamTotal = 1;
        HeapTotal = 1;
        HeapInitialFree = 0;
        HeapFree = 0;
        HeapMinimumFree = 0;
        Regions.Clear();
        Tasks.Clear();
        Objects.Clear();
        NotifyOverviewChanged();
    }

    public static string FormatBytes(uint bytes)
    {
        return bytes >= 1024
            ? $"{bytes / 1024.0:0.##} KiB"
            : $"{bytes} B";
    }

    public static string FormatAddress(uint address)
    {
        return $"0x{address:X8}";
    }

    public static string FormatRange(uint start, uint end)
    {
        return start == 0 && end == 0
            ? "address pending"
            : $"{FormatAddress(start)} - {FormatAddress(end)}";
    }

    private void RebuildRegions()
    {
        Regions.Clear();
        if (_snapshot is { } snapshot)
        {
            AddRegion("FLASH APP", "firmware code + const", snapshot.FlashAppStart, snapshot.FlashUsedEnd, "fixed");
            AddRegion("PARAM FLASH", "reserved parameter page", snapshot.ParameterStart, snapshot.ParameterEnd, "fixed");
            AddRegion("RAM .data", "initialized globals", snapshot.DataStart, snapshot.DataEnd, "fixed");
            AddRegion("RAM .bss", "zero globals + kernel heap", snapshot.BssStart, snapshot.BssEnd, "fixed");
            AddRegion("FREERTOS HEAP", "heap_4 pool", snapshot.HeapStart, snapshot.HeapEnd, "live free/min-free");
        }

        foreach (TaskMemoryRow task in Tasks)
        {
            AddRegion($"TCB {task.Name}", "task control block", task.TcbAddress, task.TcbAddress + task.TcbBytes, task.State);
            AddRegion($"STACK {task.Name}", "task stack", task.StackStart, task.StackEnd, task.StackMarginText);
        }

        foreach (ObjectMemoryRow item in Objects)
        {
            AddRegion($"{item.KindText} CTRL {item.Name}", "kernel object", item.HandleAddress, item.HandleAddress + item.StructBytes, item.HeapAllocationText);
            AddRegion($"{item.KindText} DATA {item.Name}", "payload storage", item.StorageAddress, item.StorageEnd, item.LiveStateText);
        }
    }

    private void AddRegion(string kind, string owner, uint start, uint end, string state)
    {
        if (start == 0 && end == 0)
        {
            return;
        }

        Regions.Add(new MemoryRegionRow(kind, owner, start, end, state));
    }

    private static string FormatPercent(uint value, uint total)
    {
        return total == 0 ? "0%" : $"{value * 100.0 / total:0.0}%";
    }

    private void NotifyOverviewChanged()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(LastUpdateText));
        OnPropertyChanged(nameof(FlashUsed));
        OnPropertyChanged(nameof(FlashCodeConst));
        OnPropertyChanged(nameof(FlashDataInit));
        OnPropertyChanged(nameof(FlashAppTotal));
        OnPropertyChanged(nameof(FlashParameter));
        OnPropertyChanged(nameof(RamStatic));
        OnPropertyChanged(nameof(RamData));
        OnPropertyChanged(nameof(RamBss));
        OnPropertyChanged(nameof(RamTotal));
        OnPropertyChanged(nameof(RamLinkerFree));
        OnPropertyChanged(nameof(HeapTotal));
        OnPropertyChanged(nameof(HeapInitialFree));
        OnPropertyChanged(nameof(HeapFree));
        OnPropertyChanged(nameof(HeapMinimumFree));
        OnPropertyChanged(nameof(HeapUsed));
        OnPropertyChanged(nameof(FlashProgressValue));
        OnPropertyChanged(nameof(FlashProgressMaximum));
        OnPropertyChanged(nameof(RamProgressValue));
        OnPropertyChanged(nameof(RamProgressMaximum));
        OnPropertyChanged(nameof(HeapProgressValue));
        OnPropertyChanged(nameof(HeapProgressMaximum));
        OnPropertyChanged(nameof(FlashUsedText));
        OnPropertyChanged(nameof(FlashPercentText));
        OnPropertyChanged(nameof(FlashDetailText));
        OnPropertyChanged(nameof(RamUsedText));
        OnPropertyChanged(nameof(RamPercentText));
        OnPropertyChanged(nameof(RamDetailText));
        OnPropertyChanged(nameof(HeapUsedText));
        OnPropertyChanged(nameof(HeapPercentText));
        OnPropertyChanged(nameof(HeapDetailText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TaskMemoryRow : INotifyPropertyChanged
{
    private uint _heapTotal = 1;

    public TaskMemoryRow(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public uint StackAllocated { get; private set; }
    public uint StackMinimumFree { get; private set; }
    public uint HeapAllocated { get; private set; }
    public uint Priority { get; private set; }
    public string State { get; private set; } = "UNKNOWN";
    public uint TcbAddress { get; private set; }
    public uint TcbBytes { get; private set; }
    public uint StackStart { get; private set; }
    public uint StackEnd { get; private set; }
    public uint StackPeakUsed => StackAllocated >= StackMinimumFree ? StackAllocated - StackMinimumFree : 0;
    public double StackUsagePercent => StackAllocated == 0 ? 0 : StackPeakUsed * 100.0 / StackAllocated;
    public double HeapSharePercent => HeapAllocated * 100.0 / Math.Max(1, _heapTotal);
    public string StackUsageText => $"peak {MemoryDashboard.FormatBytes(StackPeakUsed)} / {MemoryDashboard.FormatBytes(StackAllocated)}";
    public string StackMarginText => $"{MemoryDashboard.FormatBytes(StackMinimumFree)} min free";
    public string HeapAllocationText => $"{MemoryDashboard.FormatBytes(HeapAllocated)} heap block";
    public string PriorityText => $"P{Priority}  {State}";
    public string TcbAddressText => $"TCB  {MemoryDashboard.FormatRange(TcbAddress, TcbAddress + TcbBytes)}";
    public string StackAddressText => $"STACK  {MemoryDashboard.FormatRange(StackStart, StackEnd)}";
    public string MarginStatus => StackAllocated == 0
        ? "UNKNOWN"
        : StackMinimumFree * 10U <= StackAllocated
            ? "CRITICAL"
            : StackMinimumFree * 4U <= StackAllocated
                ? "WATCH"
                : "HEALTHY";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(TaskMemorySnapshot snapshot, uint heapTotal)
    {
        StackAllocated = snapshot.StackAllocated;
        StackMinimumFree = snapshot.StackMinimumFree;
        HeapAllocated = snapshot.HeapAllocated;
        Priority = snapshot.Priority;
        State = snapshot.State;
        TcbAddress = snapshot.TcbAddress;
        TcbBytes = snapshot.TcbBytes;
        StackStart = snapshot.StackStart;
        StackEnd = snapshot.StackEnd;
        _heapTotal = Math.Max(1, heapTotal);
        NotifyChanged();
    }

    public void SetHeapTotal(uint heapTotal)
    {
        _heapTotal = Math.Max(1, heapTotal);
        OnPropertyChanged(nameof(HeapSharePercent));
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(StackAllocated));
        OnPropertyChanged(nameof(StackMinimumFree));
        OnPropertyChanged(nameof(HeapAllocated));
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(TcbAddress));
        OnPropertyChanged(nameof(TcbBytes));
        OnPropertyChanged(nameof(StackStart));
        OnPropertyChanged(nameof(StackEnd));
        OnPropertyChanged(nameof(StackPeakUsed));
        OnPropertyChanged(nameof(StackUsagePercent));
        OnPropertyChanged(nameof(HeapSharePercent));
        OnPropertyChanged(nameof(StackUsageText));
        OnPropertyChanged(nameof(StackMarginText));
        OnPropertyChanged(nameof(HeapAllocationText));
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(TcbAddressText));
        OnPropertyChanged(nameof(StackAddressText));
        OnPropertyChanged(nameof(MarginStatus));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ObjectMemoryRow : INotifyPropertyChanged
{
    private uint _heapTotal = 1;

    public ObjectMemoryRow(string kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public string Kind { get; }
    public string Name { get; }
    public uint HeapAllocated { get; private set; }
    public uint PayloadAllocated { get; private set; }
    public uint Capacity { get; private set; }
    public uint Depth { get; private set; }
    public uint ItemSize { get; private set; }
    public uint HandleAddress { get; private set; }
    public uint StorageAddress { get; private set; }
    public uint StorageEnd { get; private set; }
    public uint StructBytes { get; private set; }
    public double HeapSharePercent => HeapAllocated * 100.0 / Math.Max(1, _heapTotal);
    public string KindText => Kind.ToUpperInvariant();
    public string HeapAllocationText => $"{MemoryDashboard.FormatBytes(HeapAllocated)} heap block";
    public string PayloadText => Kind.Equals("queue", StringComparison.OrdinalIgnoreCase)
        ? $"payload {MemoryDashboard.FormatBytes(PayloadAllocated)}  |  {ItemSize} B/item"
        : "heap_4 bookkeeping and alignment";
    public string LiveStateText => Kind.Equals("queue", StringComparison.OrdinalIgnoreCase)
        ? $"{Depth} / {Capacity} messages"
        : "reserved at heap initialization";
    public string HandleAddressText => $"CTRL  {MemoryDashboard.FormatAddress(HandleAddress)}";
    public string StorageAddressText => $"DATA  {MemoryDashboard.FormatRange(StorageAddress, StorageEnd)}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(ObjectMemorySnapshot snapshot, uint heapTotal)
    {
        HeapAllocated = snapshot.HeapAllocated;
        PayloadAllocated = snapshot.PayloadAllocated;
        Capacity = snapshot.Capacity;
        Depth = snapshot.Depth;
        ItemSize = snapshot.ItemSize;
        HandleAddress = snapshot.HandleAddress;
        StorageAddress = snapshot.StorageAddress;
        StorageEnd = snapshot.StorageEnd;
        StructBytes = snapshot.StructBytes;
        _heapTotal = Math.Max(1, heapTotal);
        NotifyChanged();
    }

    public void SetHeapTotal(uint heapTotal)
    {
        _heapTotal = Math.Max(1, heapTotal);
        OnPropertyChanged(nameof(HeapSharePercent));
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HeapAllocated));
        OnPropertyChanged(nameof(PayloadAllocated));
        OnPropertyChanged(nameof(Capacity));
        OnPropertyChanged(nameof(Depth));
        OnPropertyChanged(nameof(ItemSize));
        OnPropertyChanged(nameof(HandleAddress));
        OnPropertyChanged(nameof(StorageAddress));
        OnPropertyChanged(nameof(StorageEnd));
        OnPropertyChanged(nameof(StructBytes));
        OnPropertyChanged(nameof(HeapSharePercent));
        OnPropertyChanged(nameof(HeapAllocationText));
        OnPropertyChanged(nameof(PayloadText));
        OnPropertyChanged(nameof(LiveStateText));
        OnPropertyChanged(nameof(HandleAddressText));
        OnPropertyChanged(nameof(StorageAddressText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
