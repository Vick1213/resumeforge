using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Validates a batch of model-proposed <see cref="TailorCommand"/>s against a resume
/// before they are applied. A command that fails any rule is rejected, never silently
/// dropped and never clamped.
/// </summary>
public interface ICommandValidator
{
    /// <summary>Validates <paramref name="commands"/> against <paramref name="document"/>.</summary>
    CommandValidationResult Validate(IReadOnlyList<TailorCommand> commands, ResumeDocument document, TailorOptions options);

    /// <summary>
    /// Validates a batch of raw per-element parse results (see
    /// <see cref="TailorCommandParseResult"/>): every <see cref="TailorCommandParseResult.Malformed"/>
    /// element becomes a <c>malformed-command</c> <see cref="RejectedCommand"/> up front, and
    /// every <see cref="TailorCommandParseResult.Parsed"/> element's command is then validated
    /// exactly as <see cref="Validate(IReadOnlyList{TailorCommand}, ResumeDocument, TailorOptions)"/>
    /// would on its own.
    /// </summary>
    CommandValidationResult Validate(IReadOnlyList<TailorCommandParseResult> parseResults, ResumeDocument document, TailorOptions options);
}
