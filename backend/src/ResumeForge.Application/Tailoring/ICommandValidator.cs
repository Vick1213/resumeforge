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
}
