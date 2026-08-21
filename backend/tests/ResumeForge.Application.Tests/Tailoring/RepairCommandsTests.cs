using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Formatting;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// End-to-end (real <see cref="GraphExecutor"/>, real <see cref="CommandValidator"/>)
/// tests for the <c>repair-commands</c> node: a rewrite the first model round proposed and
/// the validator rejected for a fixable reason must be re-proposed in a second, bounded
/// model call and — when the fix passes validation — land in the final document instead of
/// silently vanishing.
/// </summary>
public sealed class RepairCommandsTests
{
    private const string BulletId = "exp:acme#0";
    private const string OriginalBullet = "Cut p99 latency from 840ms to 120ms.";

    /// <summary>Long enough to wrap, short enough to strand a fragment: rejected as ragged.</summary>
    private const string RaggedRewrite =
        "Cut p99 checkout latency from 840ms to 120ms across downstream services by replacing the serial fan-out entirely.";

    /// <summary>The corrected second attempt: one line, same figures.</summary>
    private const string FixedRewrite = "Cut p99 checkout latency from 840ms to 120ms by replacing the serial fan-out.";

    [Fact]
    public async Task A_ragged_rewrite_is_repaired_by_a_second_model_call_and_applied()
    {
        // Guard the fixture itself: the first proposal must actually be ragged and the
        // second must not be, or this test would pass without exercising the repair path.
        BulletLineFit.IsRagged(RaggedRewrite).ShouldBeTrue();
        BulletLineFit.IsRagged(FixedRewrite).ShouldBeFalse();

        var document = TestData.Document(
            experience:
            [
                TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null,
                    bullets: [TestData.Bullet(BulletId, OriginalBullet)]),
            ]);

        var languageModel = Substitute.For<ILanguageModel>();
        var repairCalls = 0;

        languageModel
            .CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ModelRequest>();
                var isRepair = request.CacheKey?.StartsWith("repair:", StringComparison.Ordinal) == true;

                if (isRepair)
                {
                    repairCalls++;

                    // The repair brief must carry the rejection it is asking the model to fix.
                    request.User.ShouldContain("ragged-line-fill");
                    request.User.ShouldContain(RaggedRewrite);
                    request.User.ShouldContain(OriginalBullet);
                }

                var command = new RewriteCommand { Target = BulletId, Text = isRepair ? FixedRewrite : RaggedRewrite };

                return new ModelResponse<TailorCommandParseResultList>
                {
                    Value = new TailorCommandParseResultList([new TailorCommandParseResult.Parsed(command)]),
                    Usage = TokenUsage.Empty,
                    FromCache = false,
                };
            });

        var runResult = await RunGraphAsync(document, languageModel);

        repairCalls.ShouldBe(1);

        var validation = (CommandValidationResult)runResult.Outputs[TailoringGraphFactory.RepairCommands]!;
        validation.Accepted.OfType<RewriteCommand>().ShouldContain(c => c.Text == FixedRewrite);
        validation.Rejected.ShouldNotContain(r => r.Code == "ragged-line-fill");

        var executed = (CommandExecutionResult)runResult.Outputs[TailoringGraphFactory.ExecuteCommands]!;
        executed.Document.Experience.Single().Bullets.Single().Text.ShouldBe(FixedRewrite);
    }

    [Fact]
    public async Task A_failed_repair_call_leaves_the_first_round_validation_untouched()
    {
        var document = TestData.Document(
            experience:
            [
                TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null,
                    bullets: [TestData.Bullet(BulletId, OriginalBullet)]),
            ]);

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel
            .CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ModelRequest>();
                if (request.CacheKey?.StartsWith("repair:", StringComparison.Ordinal) == true)
                {
                    throw new InvalidOperationException("provider outage");
                }

                var command = new RewriteCommand { Target = BulletId, Text = RaggedRewrite };
                return new ModelResponse<TailorCommandParseResultList>
                {
                    Value = new TailorCommandParseResultList([new TailorCommandParseResult.Parsed(command)]),
                    Usage = TokenUsage.Empty,
                    FromCache = false,
                };
            });

        var runResult = await RunGraphAsync(document, languageModel);

        // The advisory repair round failed, so the run behaves exactly as it did before the
        // node existed: the ragged rewrite stays rejected and the original bullet stands.
        var validation = (CommandValidationResult)runResult.Outputs[TailoringGraphFactory.RepairCommands]!;
        validation.Rejected.ShouldContain(r => r.Code == "ragged-line-fill");

        var executed = (CommandExecutionResult)runResult.Outputs[TailoringGraphFactory.ExecuteCommands]!;
        executed.Document.Experience.Single().Bullets.Single().Text.ShouldBe(OriginalBullet);
    }

    private static async Task<GraphRunResult> RunGraphAsync(ResumeDocument document, ILanguageModel languageModel)
    {
        var posting = TestData.Posting();

        var jobRepository = Substitute.For<IJobRepository>();
        jobRepository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(posting);

        var jobAnalyzer = Substitute.For<IJobAnalyzer>();
        jobAnalyzer.Analyze(Arg.Any<JobPosting>()).Returns(TestData.Analysis());

        var resumeBuilder = Substitute.For<IResumeBuilder>();
        resumeBuilder.Build(Arg.Any<KnowledgeBaseSnapshot>(), Arg.Any<string?>(), Arg.Any<string>())
            .Returns(document);

        var relevanceScorer = Substitute.For<IRelevanceScorer>();
        relevanceScorer.Score(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>())
            .Returns(new CandidateSet { Experience = [], Projects = [], Skills = [] });

        var briefBuilder = Substitute.For<IBriefBuilder>();
        briefBuilder.Build(
                Arg.Any<JobPosting>(), Arg.Any<JobAnalysis>(), Arg.Any<CandidateSet>(),
                Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>(), Arg.Any<AtsReview?>())
            .Returns("BRIEF");

        var renderer = Substitute.For<IResumeRenderer>();
        renderer.RenderAsync(Arg.Any<ResumeDocument>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedDocument { Content = [], ContentType = "text/markdown", FileName = "r.md", PageCount = 1 });

        var pageBudgetEnforcer = Substitute.For<IPageBudgetEnforcer>();
        pageBudgetEnforcer
            .EnforceAsync(
                Arg.Any<ResumeDocument>(), Arg.Any<IReadOnlyList<ResumeDiffEntry>>(),
                Arg.Any<CandidateSet>(), Arg.Any<TailorOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => new PageBudgetResult
            {
                Document = call.Arg<ResumeDocument>(),
                Diff = call.Arg<IReadOnlyList<ResumeDiffEntry>>(),
                PageCount = 1,
                FitsBudget = true,
            });

        var factory = new TailoringGraphFactory(
            jobRepository,
            jobAnalyzer,
            Substitute.For<IKnowledgeBaseReader>(),
            resumeBuilder,
            relevanceScorer,
            briefBuilder,
            languageModel,
            new CommandValidator(new FabricationGuard()),
            new FabricationGuard(),
            new CommandExecutor(TimeProvider.System),
            Substitute.For<ICoverageAnalyzer>(),
            renderer,
            pageBudgetEnforcer,
            new TailorOptions());

        var graph = factory.Create(new TailoringRequest { JobId = "job-1" });
        var executor = new GraphExecutor(TimeProvider.System, NullLogger<GraphExecutor>.Instance);

        return await executor.RunAsync(graph, Substitute.For<IServiceProvider>(), CancellationToken.None);
    }
}
