using System.Globalization;

namespace SquadDash;

internal static class ProviderThrottlePresentation
{
    internal static DateTimeOffset? ResolveRetryAt(
        string? retryAt,
        int? retryDelayMs,
        DateTimeOffset observedAt)
    {
        if (!string.IsNullOrWhiteSpace(retryAt) &&
            DateTimeOffset.TryParse(
                retryAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed;
        }

        return retryDelayMs is > 0
            ? observedAt.AddMilliseconds(retryDelayMs.Value)
            : null;
    }

    internal static string BuildCardStatus(DateTimeOffset? retryAt, DateTimeOffset now)
    {
        if (retryAt is null)
            return "Throttled";

        var remainingSeconds = (int)Math.Ceiling((retryAt.Value - now).TotalSeconds);
        return remainingSeconds > 0
            ? $"Throttled {remainingSeconds}s"
            : "Retrying…";
    }

    internal static string BuildTranscriptNotice(int? retryDelayMs)
    {
        if (retryDelayMs is not > 0)
            return "Provider reported a rate limit. Throttled; waiting before retrying.";

        var seconds = Math.Max(1, (int)Math.Ceiling(retryDelayMs.Value / 1000d));
        return $"Provider reported a rate limit. Throttled; retrying in {seconds}s.";
    }
}
