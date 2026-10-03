namespace MoneyMate.Services;

// Single-process guard; shared quota/lock storage is required before multiple-instance deployment.
public sealed class AnalysisGuard
{
    private readonly object sync = new();
    private readonly HashSet<string> busy = [];
    private readonly Dictionary<(string Owner, DateOnly Day), int> attempts = [];
    public bool Enter(string key) { lock (sync) return busy.Add(key); }
    public void Exit(string key) { lock (sync) busy.Remove(key); }
    public bool Charge(string owner, DateOnly day, int limit)
    {
        lock (sync)
        {
            foreach (var key in attempts.Keys.Where(x => x.Day < day).ToArray()) attempts.Remove(key);
            var keyNow = (owner, day);
            var count = attempts.GetValueOrDefault(keyNow);
            if (count >= limit) return false;
            attempts[keyNow] = count + 1;
            return true;
        }
    }
}
