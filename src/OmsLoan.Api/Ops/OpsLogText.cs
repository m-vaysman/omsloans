namespace OmsLoan.Api.Ops;

public static class OpsLogText
{
    public const string Ellipsis = "…";

    // First line only, then the configured cap. A stack frame on line 2 never
    // reaches the payload.
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

    // Newest first, then 50 entries and 300 characters. Ordering here rather than
    // trusting the caller: an Event Log query that hands over oldest-first would
    // otherwise trim away the failure worth reading.
    public static IReadOnlyList<OpsLogEntry> Trim(
        IEnumerable<OpsLogEntry> entries,
        int maxEntries,
        int maxMessageLength) =>
        entries
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(maxEntries)
            .Select(entry => entry with { Message = Summarize(entry.Message, maxMessageLength) })
            .ToList();
}
