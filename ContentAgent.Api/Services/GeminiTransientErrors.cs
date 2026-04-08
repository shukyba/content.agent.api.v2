namespace ContentAgent.Api.Services;

/// <summary>
/// Shared detection for Gemini API failures that are worth retrying with backoff
/// (quota, rate limits, and transient overload such as "high demand").
/// </summary>
internal static class GeminiTransientErrors
{
    internal static bool IsRetriable(Exception ex)
    {
        var msg = ex.ToString();

        if (msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("resource exhausted", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("429", StringComparison.OrdinalIgnoreCase))
            return true;

        // Google.GenAI.ServerError: model overload / capacity (previously did not retry)
        if (msg.Contains("high demand", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("try again later", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase))
            return true;

        if (msg.Contains("503", StringComparison.Ordinal))
            return true;

        return false;
    }
}
