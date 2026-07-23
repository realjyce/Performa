using Performa.Prefs;

namespace Performa.Desktop.Services;

/// <summary>
/// The one place the app asks a model anything.
///
/// Callers name what they want, not who provides it. Which vendor answers is a
/// setting, and the deterministic output stays the source of truth either way:
/// every path here can return null and the caller falls back to facts.
/// </summary>
public sealed class AiService
{
    private static readonly Dictionary<AiProvider, IAiProvider> Providers = new()
    {
        [AiProvider.Gemini] = new GeminiProvider(),
        [AiProvider.Claude] = new AnthropicProvider(),
        [AiProvider.OpenAi] = new OpenAiProvider(),
    };

    public static IAiProvider For(AiProvider choice) => Providers[choice];

    public static IEnumerable<AiProvider> All => Providers.Keys;

    /// <summary>Human-readable name for a provider, for Settings and status lines.</summary>
    public static string NameOf(AiProvider choice) => Providers[choice].Name;

    /// <summary>Where to get a key for a provider.</summary>
    public static string KeyUrlOf(AiProvider choice) => Providers[choice].KeyUrl;

    /// <summary>
    /// Asks the configured provider, or nothing at all when AI is switched off
    /// or no key is on hand.
    /// </summary>
    public async Task<AiAnswer?> AskAsync(Preferences prefs, string systemContext, string question)
    {
        if (!prefs.AiEnabled) return null;
        var key = AppCredentialStore.AiKey(prefs, prefs.AiProvider);
        if (string.IsNullOrWhiteSpace(key)) return null;
        return await For(prefs.AiProvider).AskAsync(key, systemContext, question);
    }

    /// <summary>
    /// A short prose read of one email. The structured extraction stays on the
    /// card either way, so this only ever adds; it never replaces the facts.
    /// </summary>
    public async Task<string?> SummariseEmailAsync(
        Preferences prefs, string from, string subject, string body)
    {
        var trimmed = body.Length > 6000 ? body[..6000] : body;
        var answer = await AskAsync(prefs,
            $"Email from {from}, subject \"{subject}\":\n{trimmed}",
            "In two sentences, what is this asking of me and by when? If nothing is asked, say what it is telling me.");
        return answer?.Text;
    }
}
