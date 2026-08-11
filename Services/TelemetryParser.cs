using System.Globalization;
using System.Text.RegularExpressions;

namespace Stm32SerialLab.Services;

public readonly record struct TelemetryParseResult(bool IsTelemetry, bool IsHeader, bool IsError, IReadOnlyDictionary<string, double> Values);

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

            return new TelemetryParseResult(true, false, values.Count == 0, values);
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
            return new TelemetryParseResult(true, false, false, values);
        }

        bool validHeader = fields.All(field => IdentifierPattern().IsMatch(field));
        if (validHeader)
        {
            _csvHeaders = fields;
            return new TelemetryParseResult(true, true, false, new Dictionary<string, double>());
        }

        return EmptyResult(true);
    }

    private static TelemetryParseResult EmptyResult(bool isError = false)
    {
        return new TelemetryParseResult(false, false, isError, new Dictionary<string, double>());
    }

    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_.\-/]*)\s*=\s*(?<value>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-/]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
