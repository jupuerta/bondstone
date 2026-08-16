namespace arch_forge_backend.Application.Export;

/// <summary>
/// V5 D2.3. The document's format is a contract: D3.3 freezes it in golden files, so changing a
/// table, the order of a section or the wording of the prompt is a change of version.
///
/// It travels in the header for the same reason <c>scoringVersion</c> does (V2 D1.5): the document
/// is meant to live <em>outside</em> the application - in a pull request, in a chat - long after it
/// was produced, and a reader has to be able to tell which shape produced it.
///
/// Bumps when the output changes for the same input. Does not bump for fixing an escape.
/// </summary>
public static class ExportVersion
{
    public const string Current = "1";
}
