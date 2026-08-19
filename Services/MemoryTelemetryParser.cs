using System.Globalization;

namespace Stm32SerialLab.Services;

public sealed record MemorySnapshot(
    uint FlashUsed,
    uint FlashCodeConst,
    uint FlashDataInit,
    uint FlashAppTotal,
    uint FlashParameter,
    uint RamStatic,
    uint RamData,
    uint RamBss,
    uint RamTotal,
    uint HeapTotal,
    uint HeapInitialFree,
    uint HeapFree,
    uint HeapMinimumFree);

public sealed record TaskMemorySnapshot(
    string Name,
    uint StackAllocated,
    uint StackMinimumFree,
    uint HeapAllocated,
    uint Priority,
    string State);

public sealed record ObjectMemorySnapshot(
    string Kind,
    string Name,
    uint HeapAllocated,
    uint PayloadAllocated,
    uint Capacity,
    uint Depth,
    uint ItemSize);

public readonly record struct MemoryTelemetryParseResult(
    bool IsMemoryTelemetry,
    bool IsError,
    MemorySnapshot? Memory,
    TaskMemorySnapshot? Task,
    ObjectMemorySnapshot? Object);

public sealed class MemoryTelemetryParser
{
    public MemoryTelemetryParseResult Parse(string line)
    {
        string input = line.Trim();
        if (HasPrefix(input, "@mem"))
        {
            return ParseMemory(input);
        }

        if (HasPrefix(input, "@task"))
        {
            return ParseTask(input);
        }

        if (HasPrefix(input, "@object"))
        {
            return ParseObject(input);
        }

        return new MemoryTelemetryParseResult(false, false, null, null, null);
    }

    private static MemoryTelemetryParseResult ParseMemory(string input)
    {
        if (!TryParseFields(input, "@mem", out Dictionary<string, string>? fields) ||
            !TryGetUInt(fields, "flash_used", out uint flashUsed) ||
            !TryGetUInt(fields, "flash_code_const", out uint flashCodeConst) ||
            !TryGetUInt(fields, "flash_data_init", out uint flashDataInit) ||
            !TryGetUInt(fields, "flash_app_total", out uint flashAppTotal) ||
            !TryGetUInt(fields, "flash_parameter", out uint flashParameter) ||
            !TryGetUInt(fields, "ram_static", out uint ramStatic) ||
            !TryGetUInt(fields, "ram_data", out uint ramData) ||
            !TryGetUInt(fields, "ram_bss", out uint ramBss) ||
            !TryGetUInt(fields, "ram_total", out uint ramTotal) ||
            !TryGetUInt(fields, "heap_total", out uint heapTotal) ||
            !TryGetUInt(fields, "heap_initial_free", out uint heapInitialFree) ||
            !TryGetUInt(fields, "heap_free", out uint heapFree) ||
            !TryGetUInt(fields, "heap_min_free", out uint heapMinimumFree) ||
            flashUsed > flashAppTotal ||
            ramStatic > ramTotal ||
            heapInitialFree > heapTotal ||
            heapFree > heapTotal ||
            heapMinimumFree > heapFree)
        {
            return ErrorResult();
        }

        MemorySnapshot snapshot = new(
            flashUsed,
            flashCodeConst,
            flashDataInit,
            flashAppTotal,
            flashParameter,
            ramStatic,
            ramData,
            ramBss,
            ramTotal,
            heapTotal,
            heapInitialFree,
            heapFree,
            heapMinimumFree);
        return new MemoryTelemetryParseResult(true, false, snapshot, null, null);
    }

    private static MemoryTelemetryParseResult ParseTask(string input)
    {
        if (!TryParseFields(input, "@task", out Dictionary<string, string>? fields) ||
            !TryGetIdentifier(fields, "name", out string name) ||
            !TryGetUInt(fields, "stack_alloc", out uint stackAllocated) ||
            !TryGetUInt(fields, "stack_min_free", out uint stackMinimumFree) ||
            !TryGetUInt(fields, "heap_alloc", out uint heapAllocated) ||
            !TryGetUInt(fields, "priority", out uint priority) ||
            !TryGetIdentifier(fields, "state", out string state) ||
            stackMinimumFree > stackAllocated)
        {
            return ErrorResult();
        }

        TaskMemorySnapshot snapshot = new(
            name,
            stackAllocated,
            stackMinimumFree,
            heapAllocated,
            priority,
            state);
        return new MemoryTelemetryParseResult(true, false, null, snapshot, null);
    }

    private static MemoryTelemetryParseResult ParseObject(string input)
    {
        if (!TryParseFields(input, "@object", out Dictionary<string, string>? fields) ||
            !TryGetIdentifier(fields, "kind", out string kind) ||
            !TryGetIdentifier(fields, "name", out string name) ||
            !TryGetUInt(fields, "heap_alloc", out uint heapAllocated) ||
            !TryGetUInt(fields, "payload_alloc", out uint payloadAllocated) ||
            !TryGetUInt(fields, "capacity", out uint capacity) ||
            !TryGetUInt(fields, "depth", out uint depth) ||
            !TryGetUInt(fields, "item_size", out uint itemSize) ||
            depth > capacity)
        {
            return ErrorResult();
        }

        ObjectMemorySnapshot snapshot = new(
            kind,
            name,
            heapAllocated,
            payloadAllocated,
            capacity,
            depth,
            itemSize);
        return new MemoryTelemetryParseResult(true, false, null, null, snapshot);
    }

    private static bool TryParseFields(
        string input,
        string prefix,
        out Dictionary<string, string> fields)
    {
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string payload = input[prefix.Length..].Trim();
        if (payload.Length == 0)
        {
            return false;
        }

        foreach (string token in payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
            {
                return false;
            }

            string name = token[..separator];
            string value = token[(separator + 1)..];
            if (!IsIdentifier(name) || !fields.TryAdd(name, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetUInt(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out uint value)
    {
        value = 0;
        return fields.TryGetValue(name, out string? text) &&
               uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetIdentifier(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out string value)
    {
        if (fields.TryGetValue(name, out string? text) && IsIdentifier(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool HasPrefix(string input, string prefix)
    {
        return input.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIdentifier(string value)
    {
        return value.Length > 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    }

    private static MemoryTelemetryParseResult ErrorResult()
    {
        return new MemoryTelemetryParseResult(true, true, null, null, null);
    }
}
