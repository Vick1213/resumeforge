using ResumeForge.Infrastructure.Ai;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>
/// Tests for <see cref="AiProviderCatalog"/>: the CONTRACTS.md §8 preset table and
/// <c>auto</c> resolution order. Environment-variable lookup is always a fake function
/// here — never <see cref="Environment.GetEnvironmentVariable(string)"/> — so these tests
/// never depend on (or pollute) the real process environment.
/// </summary>
public sealed class AiProviderCatalogTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] set) =>
        name => set.FirstOrDefault(e => e.Name == name).Value;

    [Theory]
    [InlineData("deepseek", AiWireFormat.OpenAi, "https://api.deepseek.com", "deepseek-chat", "DEEPSEEK_API_KEY")]
    [InlineData("openai", AiWireFormat.OpenAi, "https://api.openai.com/v1", "gpt-4o", "OPENAI_API_KEY")]
    [InlineData("lmstudio", AiWireFormat.OpenAi, "http://localhost:1234/v1", "", null)]
    [InlineData("anthropic", AiWireFormat.Anthropic, "https://api.anthropic.com", "claude-sonnet-5", "ANTHROPIC_API_KEY")]
    [InlineData("heuristic", AiWireFormat.Heuristic, "", "", null)]
    public void TryGetPreset_returns_the_documented_defaults_for_every_named_provider(
        string name, AiWireFormat wire, string baseUrl, string model, string? keyEnvironmentVariable)
    {
        AiProviderCatalog.TryGetPreset(name, out var preset).ShouldBeTrue();

        preset.Wire.ShouldBe(wire);
        preset.BaseUrl.ShouldBe(baseUrl);
        preset.Model.ShouldBe(model);
        preset.KeyEnvironmentVariable.ShouldBe(keyEnvironmentVariable);
    }

    [Fact]
    public void TryGetPreset_is_case_insensitive()
    {
        AiProviderCatalog.TryGetPreset("DeepSeek", out var preset).ShouldBeTrue();
        preset.Name.ShouldBe("deepseek");
    }

    [Fact]
    public void TryGetPreset_returns_false_for_an_unlisted_provider()
    {
        AiProviderCatalog.TryGetPreset("groq", out _).ShouldBeFalse();
    }

    [Fact]
    public void ResolveProviderName_passes_through_an_explicit_selection_verbatim()
    {
        AiProviderCatalog.ResolveProviderName("lmstudio", Env()).ShouldBe("lmstudio");
        AiProviderCatalog.ResolveProviderName("groq", Env()).ShouldBe("groq");
    }

    [Fact]
    public void ResolveProviderName_lowercases_an_explicit_selection()
    {
        AiProviderCatalog.ResolveProviderName("DeepSeek", Env()).ShouldBe("deepseek");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void ResolveProviderName_treats_unset_blank_or_auto_as_auto(string? configured)
    {
        AiProviderCatalog.ResolveProviderName(configured, Env()).ShouldBe("heuristic");
    }

    [Fact]
    public void Auto_selects_deepseek_when_only_deepseek_key_is_set()
    {
        var env = Env(("DEEPSEEK_API_KEY", "sk-deepseek"));

        AiProviderCatalog.ResolveProviderName("auto", env).ShouldBe("deepseek");
    }

    [Fact]
    public void Auto_selects_anthropic_when_only_anthropic_key_is_set()
    {
        var env = Env(("ANTHROPIC_API_KEY", "sk-ant"));

        AiProviderCatalog.ResolveProviderName("auto", env).ShouldBe("anthropic");
    }

    [Fact]
    public void Auto_prefers_deepseek_over_anthropic_when_both_keys_are_set()
    {
        var env = Env(("DEEPSEEK_API_KEY", "sk-deepseek"), ("ANTHROPIC_API_KEY", "sk-ant"));

        AiProviderCatalog.ResolveProviderName("auto", env).ShouldBe("deepseek");
    }

    [Fact]
    public void Auto_falls_back_to_heuristic_when_neither_key_is_set()
    {
        AiProviderCatalog.ResolveProviderName("auto", Env()).ShouldBe("heuristic");
    }

    [Fact]
    public void Auto_never_selects_lmstudio_no_matter_what_is_in_the_environment()
    {
        // Not even an LMSTUDIO_API_KEY-shaped variable exists in the preset table, but the
        // point of this test is structural: auto's resolution order (CONTRACTS.md §8) has
        // no path that returns "lmstudio" at all.
        var env = Env(("DEEPSEEK_API_KEY", ""), ("ANTHROPIC_API_KEY", ""), ("LMSTUDIO_API_KEY", "anything"));

        AiProviderCatalog.ResolveProviderName("auto", env).ShouldNotBe("lmstudio");
        AiProviderCatalog.ResolveProviderName("auto", env).ShouldBe("heuristic");
    }
}
