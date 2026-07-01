using Serilog;
using Serilog.Formatting.Compact;

namespace Kubernator.Web.Logging;

internal static class SerilogSinks
{
    public static LoggerConfiguration ApplyKubernatorSinks(this LoggerConfiguration cfg) => cfg
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            new CompactJsonFormatter(),
            KubernatorLogPaths.ResolveWebLogPath(),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 50_000_000,
            rollOnFileSizeLimit: true,
            shared: true);
}
