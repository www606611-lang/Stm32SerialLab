using System.Globalization;
using System.Text.RegularExpressions;

namespace Stm32SerialLab.Services;

public sealed record TelemetryChannelMetadata(string Name, string? Unit, double? Minimum, double? Maximum);

public readonly record struct TelemetryParseResult(
    bool IsTelemetry,
    bool IsHeader,
    bool IsError,
    IReadOnlyDictionary<string, double> Values,
    TelemetryChannelMetadata? Metadata);

public sealed partial class TelemetryParser
{
    private string[]? _csvHeaders;

    public TelemetryParseResult Parse(string line)
    {
        string input = line.Trim();
        if (input.Length == 0)
        {
            return EmptyResult();
        }

        if (input.StartsWith("@meta ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMetadata(input);
        }

        MatchCollection matches = KeyValuePattern().Matches(input);
        if (matches.Count > 0)
        {
            Dictionary<string, double> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in matches)
            {
                if (double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    values[match.Groups["name"].Value] = value;
                }
            }

            return new TelemetryParseResult(true, false, values.Count == 0, values, null);
        }

        if (!input.Contains(','))
        {
            return EmptyResult(input.Contains('='));
        }

        string[] fields = input.Split(',', StringSplitOptions.TrimEntries);
        double[] numericValues = new double[fields.Length];
        bool allNumeric = fields.Select((field, index) => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValues[index])).All(result => result);
        if (allNumeric)
        {
            string[] names = _csvHeaders is { Length: > 0 } && _csvHeaders.Length == fields.Length
                ? _csvHeaders
                : Enumerable.Range(0, fields.Length).Select(index => $"ch{index}").ToArray();
            Dictionary<string, double> values = names.Select((name, index) => new KeyValuePair<string, double>(name, numericValues[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return new TelemetryParseResult(true, false, false, values, null);
        }

        bool validHeader = fields.All(field => IdentifierPattern().IsMatch(field));
        if (validHeader)
        {
            _csvHeaders = fields;
            return new TelemetryParseResult(true, true, false, new Dictionary<string, double>(), null);
        }

        return EmptyResult(true);
    }

    private static TelemetryParseResult EmptyResult(bool isError = false)
    {
        return new TelemetryParseResult(false, false, isError, new Dictionary<string, double>(), null);
    }

    private static TelemetryParseResult ParseMetadata(string input)
    {
        string[] fields = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 2 || !IdentifierPattern().IsMatch(fields[1]))
        {
            return EmptyResult(true);
        }

        string? unit = null;
        double? minimum = null;
        double? maximum = null;
        foreach (string field in fields.Skip(2))
        {
            string[] pair = field.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[1].Length == 0)
            {
                return EmptyResult(true);
            }

            switch (pair[0].ToLowerInvariant())
            {
                case "unit":
                    unit = pair[1];
                    break;
                case "min" when double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMinimum):
                    minimum = parsedMinimum;
                    break;
                case "max" when double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMaximum):
                    maximum = parsedMaximum;
                    break;
                default:
                    return EmptyResult(true);
            }
        }

        if (minimum.HasValue && maximum.HasValue && minimum.Value >= maximum.Value)
        {
            return EmptyResult(true);
        }

        TelemetryChannelMetadata metadata = new(fields[1], unit, minimum, maximum);
        return new TelemetryParseResult(true, false, false, new Dictionary<string, double>(), metadata);
    }

    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_.\-/]*)\s*=\s*(?<value>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-/]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
