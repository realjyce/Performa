using Performa.Desktop.Services;
using Performa.Prefs;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// The AI layer is optional by design, so what matters most is that it stays
/// silent when it should. These cover the routing and the refusal-to-send
/// paths; they make no network calls.
/// </summary>
public class AiProviderTests
{
    [Fact]
    public void Every_provider_is_reachable_and_named()
    {
        foreach (var choice in Enum.GetValues<AiProvider>())
        {
            Assert.NotNull(AiService.For(choice));
            Assert.False(string.IsNullOrWhiteSpace(AiService.NameOf(choice)));
            Assert.StartsWith("https://", AiService.KeyUrlOf(choice));
        }
    }

    [Fact]
    public void Gemini_is_the_default_because_it_is_the_only_free_tier()
    {
        Assert.Equal(AiProvider.Gemini, new Preferences().AiProvider);
    }

    [Fact]
    public async Task No_key_means_no_call()
    {
        foreach (var provider in Enum.GetValues<AiProvider>().Select(AiService.For))
            Assert.Null(await provider.AskAsync("", "context", "question"));
    }

    [Fact]
    public async Task Ai_switched_off_sends_nothing_even_with_a_key()
    {
        var prefs = new Preferences
        {
            AiEnabled = false,
            AiProvider = AiProvider.Claude,
            AnthropicApiKey = "sk-ant-not-a-real-key",
        };
        Assert.Null(await new AiService().AskAsync(prefs, "context", "question"));
    }

    [Fact]
    public async Task Selecting_a_provider_with_no_key_sends_nothing()
    {
        // A key for one vendor must not be spent on another.
        var prefs = new Preferences
        {
            AiEnabled = true,
            AiProvider = AiProvider.OpenAi,
            AnthropicApiKey = "sk-ant-not-a-real-key",
        };
        Assert.Null(await new AiService().AskAsync(prefs, "context", "question"));
    }

    [Theory]
    [InlineData(AiProvider.Claude)]
    [InlineData(AiProvider.OpenAi)]
    public void Billed_providers_never_fall_back_to_a_shipped_key(AiProvider provider)
    {
        // Gemini may resolve a shared key from the credentials file. The two
        // metered vendors must not, or an install would spend someone else's money.
        Assert.Null(AppCredentialStore.AiKey(new Preferences(), provider));
    }

    [Fact]
    public void Each_provider_keeps_its_own_key()
    {
        var prefs = new Preferences
        {
            GeminiApiKey = "gem",
            AnthropicApiKey = "ant",
            OpenAiApiKey = "oai",
        };
        Assert.Equal("gem", AppCredentialStore.AiKey(prefs, AiProvider.Gemini));
        Assert.Equal("ant", AppCredentialStore.AiKey(prefs, AiProvider.Claude));
        Assert.Equal("oai", AppCredentialStore.AiKey(prefs, AiProvider.OpenAi));
    }

    [Fact]
    public void Keys_and_provider_survive_a_save_and_reload()
    {
        var prefs = new Preferences
        {
            AiProvider = AiProvider.Claude,
            AnthropicApiKey = "ant",
            OpenAiApiKey = "oai",
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            prefs, PerformaJsonContext.Default.Preferences);
        var back = System.Text.Json.JsonSerializer.Deserialize(
            json, PerformaJsonContext.Default.Preferences)!;

        Assert.Equal(AiProvider.Claude, back.AiProvider);
        Assert.Equal("ant", back.AnthropicApiKey);
        Assert.Equal("oai", back.OpenAiApiKey);
    }
}
