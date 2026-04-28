using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Models;

namespace Kubernator.Core.Pipelines.Emitters;

internal static class GitHubActionsEmitter
{
    public static string Emit(PipelineOptions options)
    {
        var profile = LanguageBuildSteps.For(options.AppKind, options);
        var w = new IndentedTextWriter();
        w.Line($"name: kubernator-{options.ImageName}");
        w.Line("on:");
        w.Indent();
        w.Line("push:");
        w.Indent();
        w.Line("branches: [main]");
        w.Outdent();
        w.Line("workflow_dispatch:");
        w.Outdent();
        w.Line("permissions:");
        w.Indent();
        w.Line("contents: read");
        w.Line("id-token: write");
        w.Outdent();
        w.Line("jobs:");
        w.Indent();
        w.Line("build:");
        w.Indent();
        w.Line("runs-on: ubuntu-latest");
        w.Line("env:");
        w.Indent();
        w.Line($"IMAGE_NAME: {YamlValue.String(options.ImageName)}");
        w.Line($"IMAGE_TAG: {YamlValue.String(options.ImageTag)}");
        w.Line($"REGISTRY: {YamlValue.String(options.Registry)}");
        w.Outdent();
        w.Line("steps:");
        w.Indent();
        w.Line("- name: checkout");
        w.Indent();
        w.Line("uses: actions/checkout@v4");
        w.Outdent();

        EmitSetupSteps(w, options, profile);

        foreach (var step in profile.Build)
        {
            EmitRunStep(w, step);
        }
        foreach (var step in profile.Test)
        {
            EmitRunStep(w, step);
        }

        EmitInstallKubernatorStep(w, options);

        EmitRunStep(w, new LanguageStep(
            "kubernator bundle",
            $"kubernator bundle {options.PublishPath} -o ./out/{options.ImageName}-{options.ImageTag}.kubpack --namespace {options.Namespace} --name {options.ImageName} --tag {options.ImageTag}"));

        if (options.SignBundle)
        {
            EmitRunStep(w, new LanguageStep(
                "kubernator sign",
                $"kubernator sign ./out/{options.ImageName}-{options.ImageTag}.kubpack --key ${{{{ secrets.COSIGN_KEY_PATH }}}} --password \"${{{{ secrets.COSIGN_PASSWORD }}}}\""));
        }
        if (options.RunVerify)
        {
            var verifyArgs = options.SignBundle ? " --require-signature" : "";
            EmitRunStep(w, new LanguageStep(
                "kubernator verify",
                $"kubernator verify ./out/{options.ImageName}-{options.ImageTag}.kubpack{verifyArgs}"));
        }

        w.Line($"- name: upload bundle");
        w.Indent();
        w.Line("uses: actions/upload-artifact@v4");
        w.Line("with:");
        w.Indent();
        w.Line($"name: {YamlValue.String(options.BundleArtifactName)}");
        w.Line("path: out/*.kubpack*");
        w.Line("retention-days: 30");
        w.Outdent();
        w.Outdent();

        return w.ToString();
    }

    private static void EmitSetupSteps(IndentedTextWriter w, PipelineOptions options, LanguageProfile profile)
    {
        switch (options.AppKind)
        {
            case AppKind.DotNet:
                w.Line("- name: setup .NET");
                w.Indent();
                w.Line($"uses: {profile.SetupAction}");
                w.Line("with:");
                w.Indent();
                w.Line("dotnet-version: \"10.0.x\"");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.NodeJs:
            case AppKind.StaticWeb:
                w.Line("- name: setup Node.js");
                w.Indent();
                w.Line($"uses: {profile.SetupAction}");
                w.Line("with:");
                w.Indent();
                w.Line("node-version: \"20.x\"");
                w.Line("cache: npm");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.Python:
                w.Line("- name: setup Python");
                w.Indent();
                w.Line($"uses: {profile.SetupAction}");
                w.Line("with:");
                w.Indent();
                w.Line("python-version: \"3.12\"");
                w.Line("cache: pip");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.Java:
                w.Line("- name: setup JDK");
                w.Indent();
                w.Line($"uses: {profile.SetupAction}");
                w.Line("with:");
                w.Indent();
                w.Line("distribution: temurin");
                w.Line("java-version: \"21\"");
                w.Line("cache: maven");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.Go:
                w.Line("- name: setup Go");
                w.Indent();
                w.Line($"uses: {profile.SetupAction}");
                w.Line("with:");
                w.Indent();
                w.Line("go-version: \"1.22.x\"");
                w.Outdent();
                w.Outdent();
                break;
        }
    }

    private static void EmitInstallKubernatorStep(IndentedTextWriter w, PipelineOptions options)
    {
        w.Line("- name: install kubernator");
        w.Indent();
        w.Line("run: |");
        w.Indent();
        w.Line($"curl -sSL https://github.com/kubernator/kubernator/releases/download/v{options.KubernatorVersion}/kubernator-linux-x64 -o /usr/local/bin/kubernator");
        w.Line("chmod +x /usr/local/bin/kubernator");
        w.Outdent();
        w.Outdent();
    }

    private static void EmitRunStep(IndentedTextWriter w, LanguageStep step)
    {
        w.Line($"- name: {YamlValue.String(step.Name)}");
        w.Indent();
        w.Line($"run: {YamlValue.String(step.Run)}");
        w.Outdent();
    }
}
