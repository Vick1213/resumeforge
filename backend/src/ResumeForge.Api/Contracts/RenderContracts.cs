namespace ResumeForge.Api.Contracts;

/// <summary>
/// Request body for <c>POST /api/render/{{resumeId}}</c>, declared inline in
/// CONTRACTS.md §9's route table (<c>RenderRequest {{ format }}</c>). <see cref="Format"/>
/// is one of the literal wire values from that table: <c>pdf</c>, <c>html</c>, <c>md</c>
/// (accepting <c>markdown</c> as a synonym), or <c>docx</c> (which returns HTTP 501).
/// </summary>
public sealed record RenderRequest
{
    /// <summary>The requested output format: <c>pdf</c>, <c>html</c>, <c>md</c>, or <c>docx</c>.</summary>
    public required string Format { get; init; }
}
