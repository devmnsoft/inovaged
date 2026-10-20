using System.Collections.Concurrent;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>Process-wide availability protection. It never retries or resends a document.</summary>
public sealed class AiProviderCircuitBreaker(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);

    public bool CanExecute(string endpoint)
    {
        if (!_states.TryGetValue(endpoint, out var state)) return true;
        lock (state)
        {
            if (state.OpenUntil > _clock.GetUtcNow()) return false;
            if (state.OpenUntil != default) { state.Failures = 0; state.OpenUntil = default; }
            return true;
        }
    }

    public void Record(string endpoint, bool unavailable, int threshold, TimeSpan duration)
    {
        var state = _states.GetOrAdd(endpoint, _ => new State());
        lock (state)
        {
            if (!unavailable) { state.Failures = 0; state.OpenUntil = default; return; }
            if (++state.Failures >= Math.Max(1, threshold)) state.OpenUntil = _clock.GetUtcNow().Add(duration);
        }
    }

    private sealed class State { public int Failures; public DateTimeOffset OpenUntil; }
}
