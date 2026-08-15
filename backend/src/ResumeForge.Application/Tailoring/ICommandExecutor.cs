using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>Applies a batch of accepted <see cref="TailorCommand"/>s to a resume.</summary>
public interface ICommandExecutor
{
    /// <summary>
    /// Applies <paramref name="commands"/> to <paramref name="document"/>, returning a new
    /// document and the diff that produced it. Never mutates <paramref name="document"/>.
    /// Application order is fixed regardless of the input order: includes/excludes, then
    /// variant selection and rewrites, then ordering, then summary/skills/section order.
    /// </summary>
    CommandExecutionResult Execute(ResumeDocument document, IReadOnlyList<TailorCommand> commands);
}

/// <summary>The result of <see cref="ICommandExecutor.Execute"/>.</summary>
public sealed record CommandExecutionResult
{
    /// <summary>The new document with every command applied.</summary>
    public required ResumeDocument Document { get; init; }

    /// <summary>Every change made to produce <see cref="Document"/>.</summary>
    public required IReadOnlyList<ResumeDiffEntry> Diff { get; init; }
}
