using System.Text.Json;
using Kubernator.Core.Strategy;

namespace Kubernator.Core.Generation.Emitters;

internal static class DockerfileEmitter
{
    public static string Emit(BuildPlan plan)
    {
        var w = new IndentedTextWriter();
        w.Line($"FROM {plan.RuntimeImage.Reference}");
        w.Line($"WORKDIR {plan.WorkingDirectory}");
        w.Line($"COPY --chown={plan.Security.RunAsUser}:{plan.Security.RunAsGroup} . {plan.WorkingDirectory}/");
        w.Line($"USER {plan.Security.RunAsUser}:{plan.Security.RunAsGroup}");
        foreach (var port in plan.ExposedPorts)
        {
            w.Line($"EXPOSE {port}");
        }
        foreach (var env in plan.EnvironmentVariables)
        {
            w.Line($"ENV {env.Key}={EscapeEnvValue(env.Value)}");
        }
        var argv = new List<string>(1 + plan.EntrypointArguments.Count) { plan.EntrypointCommand };
        argv.AddRange(plan.EntrypointArguments);
        w.Line($"ENTRYPOINT {JsonSerializer.Serialize(argv)}");
        return w.ToString();
    }

    private static string EscapeEnvValue(string value)
    {
        if (NeedsQuoting(value))
        {
            return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }
        return value;
    }

    private static bool NeedsQuoting(string value)
    {
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c == '"' || c == '\\' || c == '$' || c == '#')
            {
                return true;
            }
        }
        return false;
    }
}
