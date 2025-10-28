using Xunit;

namespace NavShieldTracer.Tests.Utils;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (!PerformanceTestToggle.IsEnabled)
        {
            Skip = "Performance tests disabled. Set RUN_PERFORMANCE_TESTS=1 to enable.";
        }
    }
}

public static class PerformanceTestToggle
{
    private static readonly Lazy<bool> _isEnabled = new(() =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_PERFORMANCE_TESTS"), "1", StringComparison.OrdinalIgnoreCase));

    public static bool IsEnabled => _isEnabled.Value;
}
