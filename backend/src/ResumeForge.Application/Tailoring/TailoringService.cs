using ResumeForge.Application.Graph;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="ITailoringService"/> orchestrator. Deliberately thin: every
/// step of the pipeline lives in a <see cref="TailoringGraphFactory"/> node, and this type
/// only builds the graph, runs it, and reassembles the final <see cref="TailoringResult"/>
/// from its outputs and trace.
/// </summary>
public sealed class TailoringService(
    ITailoringGraphFactory graphFactory,
    IGraphExecutor executor,
    IServiceProvider services) : ITailoringService
{
    /// <inheritdoc />
    public async Task<TailoringResult> TailorAsync(TailoringRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var graph = graphFactory.Create(request);
        var runResult = await executor.RunAsync(graph, services, ct).ConfigureAwait(false);

        var executed = GetOutput<CommandExecutionResult>(runResult, TailoringGraphFactory.ExecuteCommands)
            ?? throw new InvalidOperationException("Tailoring graph did not produce an execute-commands result.");

        var validation = GetOutput<CommandValidationResult>(runResult, TailoringGraphFactory.ValidateCommands)
            ?? throw new InvalidOperationException("Tailoring graph did not produce a validate-commands result.");

        var coverage = GetOutput<CoverageReport>(runResult, TailoringGraphFactory.VerifyCoverage)
            ?? new CoverageReport { Score = 0.0, Requirements = [] };

        return new TailoringResult
        {
            Document = executed.Document,
            Diff = executed.Diff,
            Commands = validation,
            Coverage = coverage,
            Usage = runResult.Usage,
            Trace = runResult.Trace,
        };
    }

    private static T? GetOutput<T>(GraphRunResult result, string node) where T : class =>
        result.Outputs.TryGetValue(node, out var value) ? value as T : null;
}
