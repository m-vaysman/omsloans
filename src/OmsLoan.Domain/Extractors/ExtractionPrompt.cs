using System.Reflection;

namespace OmsLoan.Domain.Extractors;

/// <summary>
/// One version of the extraction prompt and the schema that constrains its response.
/// </summary>
/// <param name="Version">
/// Written to <see cref="Extraction.PromptVersion"/> on every row this prompt produces.
/// </param>
/// <param name="Text">The prompt, with <c>{types}</c> already substituted.</param>
/// <param name="JsonSchema">The schema, for providers that constrain output with one.</param>
public sealed record ExtractionPrompt(string Version, string Text, string JsonSchema);

/// <summary>
/// Prompts loaded from files rather than string literals.
/// </summary>
/// <remarks>
/// Files, embedded as resources. A prompt in a C# literal is not reviewable — nobody diffs
/// escaped newlines — and this is the highest-leverage artifact in the system: a model swap is
/// configuration; a bad prompt is wrong output on every provider at once.
///
/// The version is the point. Written to every extraction row; without it the accuracy report
/// cannot tell a model regression from a prompt edit. Bump it on every edit, however small.
/// </remarks>
public static class PromptCatalog
{
    /// <summary>The prompt currently used for extraction.</summary>
    /// <remarks>
    /// One prompt, not one per notice type. A document can describe several events at once —
    /// a paydown and the next period's rate reset is ordinary — and a per-type prompt has to
    /// drop one or flatten them together.
    /// </remarks>
    public const string CurrentVersion = "extraction.v1";

    private static readonly Lazy<ExtractionPrompt> Current = new(() => Load(CurrentVersion));

    /// <summary>Current prompt, with the allowed type list substituted in.</summary>
    public static ExtractionPrompt Extraction => Current.Value;

    /// <summary>Loads a specific version so a reprocess run can reproduce an old row exactly.</summary>
    public static ExtractionPrompt Load(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var text = ReadResource($"{version}.md")
            .Replace("{types}", string.Join(", ", NoticeTypes.AllowedNames), StringComparison.Ordinal);

        return new ExtractionPrompt(version, text, ReadResource($"{version}.schema.json"));
    }

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(PromptCatalog).Assembly;
        var name = $"{typeof(PromptCatalog).Namespace}.Prompts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Prompt resource '{name}' is not embedded. Available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
