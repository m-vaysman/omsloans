namespace OmsLoan.Api.Ops;

public static class OpsLogText
{
    public const string Ellipsis = "…";

    public static string Summarize(string? message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var firstLine = message
            .Split('\r', '\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

        return firstLine.Length <= maxLength
            ? firstLine
            : firstLine[..maxLength] + Ellipsis;
    }

    public static IReadOnlyList<OpsLogEntry> Trim(
        IEnumerable<OpsLogEntry> entries,
        int maxEntries,
        int maxMessageLength) =>
        entries
            .Take(maxEntries)
            .Select(entry => entry with { Message = Summarize(entry.Message, maxMessageLength) })
            .ToList();
}
