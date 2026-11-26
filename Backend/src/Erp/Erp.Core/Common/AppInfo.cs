using System.Reflection;

namespace Erp.Core.Common;

/// <summary>
/// Classic thread-safe SINGLETON (coding problem 14): sealed class, private
/// constructor, and <see cref="Lazy{T}"/>, which guarantees the factory runs
/// exactly once even if many threads ask at the same moment, with no
/// hand-written double-checked locking.
/// (In ASP.NET Core you'd normally register a singleton in DI instead; this
/// is the pattern for code that lives outside DI.)
/// </summary>
public sealed class AppInfo
{
    private static readonly Lazy<AppInfo> _instance = new(() => new AppInfo());
    public static AppInfo Instance => _instance.Value;

    private AppInfo()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        Name = asm.GetName().Name ?? "MiniErp";
        Version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString() ?? "1.0.0";
        StartedAtUtc = DateTime.UtcNow;
    }

    public string Name { get; }
    public string Version { get; }
    public DateTime StartedAtUtc { get; }
    public string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
}
