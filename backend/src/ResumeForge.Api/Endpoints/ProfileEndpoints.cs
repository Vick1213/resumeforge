using ResumeForge.Api.Contracts;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Ai;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/profile</c> routes (CONTRACTS.md §9).</summary>
public static class ProfileEndpoints
{
    /// <summary>Registers the profile routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/profile").WithTags("Profile");

        group.MapGet("/", GetProfileAsync)
            .WithName("GetProfile")
            .Produces<ProfileDto>();

        group.MapPut("/basics", PutBasicsAsync)
            .WithName("UpdateProfileBasics")
            .Produces<ResumeBasics>();

        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        IKnowledgeBaseReader reader,
        IResumeBuilder resumeBuilder,
        ILanguageModel languageModel,
        HeuristicLanguageModel heuristicLanguageModel,
        CancellationToken ct)
    {
        var snapshot = await reader.ReadAsync(ct).ConfigureAwait(false);

        // Skills are derived, not authored (CONTRACTS.md §3), so counting them means
        // building the (unpersisted) base resume rather than counting a knowledge-base item
        // category.
        var built = resumeBuilder.Build(snapshot);

        var counts = new KnowledgeCounts
        {
            Experience = snapshot.Items.Count(i => i.Type == KnowledgeItemType.Experience),
            Projects = snapshot.Items.Count(i => i.Type == KnowledgeItemType.Project),
            Education = snapshot.Items.Count(i => i.Type == KnowledgeItemType.Education),
            Certifications = snapshot.Items.Count(i => i.Type == KnowledgeItemType.Certification),
            Skills = built.Skills.Count,
        };

        // "Has an API key" is bound to whether the resolved model is the no-network
        // heuristic fallback, not to any one provider's environment variable name, since
        // provider selection is configurable (CONTRACTS.md §8).
        var hasApiKey = !string.Equals(languageModel.ModelId, heuristicLanguageModel.ModelId, StringComparison.Ordinal);

        var dto = new ProfileDto
        {
            Basics = snapshot.Basics,
            Counts = counts,
            Diagnostics = snapshot.Diagnostics,
            HasApiKey = hasApiKey,
            Model = languageModel.ModelId,
        };

        return TypedResults.Ok(dto);
    }

    private static async Task<IResult> PutBasicsAsync(
        ResumeBasics request, IKnowledgeBaseReader reader, IKnowledgeBaseWriter writer, CancellationToken ct)
    {
        // WriteBasicsAsync also takes the default summary, which ResumeBasics does not
        // carry; the current summary is read back first so a basics-only edit never wipes
        // profile/basics.md's body. See the implementation report for this ruling.
        var snapshot = await reader.ReadAsync(ct).ConfigureAwait(false);
        await writer.WriteBasicsAsync(request, snapshot.DefaultSummary, ct).ConfigureAwait(false);

        return TypedResults.Ok(request);
    }
}
