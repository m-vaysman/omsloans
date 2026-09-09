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
/// The prompts, loaded from files rather than string literals.
/// </summary>
/// <remarks>
/// <para>
/// Files, embedded as resources, for two reasons. A prompt buried in a C# literal is not
/// reviewable — nobody diffs escaped newlines — and this is the highest-leverage artifact in
/// the system: a model swap is a configuration change, a bad prompt is wrong output on every
/// provider at once.
/// </para>
/// <para>
/// <strong>The version is the point.</strong> It is written to every extraction row, and
/// without it the accuracy report cannot tell a model regression from a prompt edit — the two
/// look identical in the data and have opposite remedies. Bump it on every edit, however
/// small; an edit that "cannot change anything" is exactly the one nobody will suspect.
/// </para>
/// </remarks>
public static class PromptCatalog
{
    /// <summary>The prompt currently used for extraction.</summary>
    /// <remarks>
    /// One prompt, not one per notice type. A document can describe several events at once —
    /// a paydown and the next period's rate reset is ordinary — and a per-type prompt has to
    /// either drop one or flatten them together.
    /// </remarks>
    public const string CurrentVersion = "extraction.v1";

    private static readonly Lazy<ExtractionPrompt> Current = new(() => Load(CurrentVersion));

    /// <summary>The current prompt, with the allowed type list substituted in.</summary>
    public static ExtractionPrompt Extraction => Current.Value;

    /// <summary>Loads a specific version, so a reprocess run can reproduce an old row exactly.</summary>
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
