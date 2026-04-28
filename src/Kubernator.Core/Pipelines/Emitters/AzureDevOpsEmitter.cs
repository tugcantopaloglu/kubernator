using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Models;

namespace Kubernator.Core.Pipelines.Emitters;

internal static class AzureDevOpsEmitter
{
    public static string Emit(PipelineOptions options)
    {
        var profile = LanguageBuildSteps.For(options.AppKind, options);
        var w = new IndentedTextWriter();
        w.Line("trigger:");
        w.Indent();
        w.Line("- main");
        w.Outdent();
        w.Line("pool:");
        w.Indent();
        w.Line("vmImage: ubuntu-latest");
        w.Outdent();
        w.Line("variables:");
        w.Indent();
        w.Line($"imageName: {YamlValue.String(options.ImageName)}");
        w.Line($"imageTag: {YamlValue.String(options.ImageTag)}");
        w.Line($"registry: {YamlValue.String(options.Registry)}");
        w.Outdent();
        w.Line("stages:");
        w.Indent();
        w.Line("- stage: Build");
        w.Indent();
        w.Line("jobs:");
        w.Indent();
        w.Line("- job: build");
        w.Indent();
        w.Line("steps:");
        w.Indent();
        EmitSetup(w, options.AppKind);
        foreach (var step in profile.Build)
        {
            EmitScriptStep(w, step);
        }
        foreach (var step in profile.Test)
        {
            EmitScriptStep(w, step);
        }
        EmitScriptStep(w, new LanguageStep(
            "install kubernator",
            $"curl -sSL https://github.com/kubernator/kubernator/releases/download/v{options.KubernatorVersion}/kubernator-linux-x64 -o /usr/local/bin/kubernator && chmod +x /usr/local/bin/kubernator"));
        EmitScriptStep(w, new LanguageStep(
            "kubernator bundle",
            $"kubernator bundle {options.PublishPath} -o ./out/$(imageName)-$(imageTag).kubpack --namespace {options.Namespace} --name $(imageName) --tag $(imageTag)"));
        if (options.SignBundle)
        {
            EmitScriptStep(w, new LanguageStep(
                "kubernator sign",
                "kubernator sign ./out/$(imageName)-$(imageTag).kubpack --key $(COSIGN_KEY_PATH) --password \"$(COSIGN_PASSWORD)\""));
        }
        if (options.RunVerify)
        {
            var args = options.SignBundle ? " --require-signature" : "";
            EmitScriptStep(w, new LanguageStep("kubernator verify",
                $"kubernator verify ./out/$(imageName)-$(imageTag).kubpack{args}"));
        }
        w.Line("- task: PublishPipelineArtifact@1");
        w.Indent();
        w.Line("inputs:");
        w.Indent();
        w.Line("targetPath: out");
        w.Line($"artifact: {YamlValue.String(options.BundleArtifactName)}");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    private static void EmitSetup(IndentedTextWriter w, AppKind kind)
    {
        switch (kind)
        {
            case AppKind.DotNet:
                w.Line("- task: UseDotNet@2");
                w.Indent();
                w.Line("inputs:");
                w.Indent();
                w.Line("packageType: sdk");
                w.Line("version: \"10.0.x\"");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.NodeJs:
            case AppKind.StaticWeb:
                w.Line("- task: NodeTool@0");
                w.Indent();
                w.Line("inputs:");
                w.Indent();
                w.Line("versionSpec: \"20.x\"");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.Python:
                w.Line("- task: UsePythonVersion@0");
                w.Indent();
                w.Line("inputs:");
                w.Indent();
                w.Line("versionSpec: \"3.12\"");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.Java:
                w.Line("- task: JavaToolInstaller@0");
                w.Indent();
                w.Line("inputs:");
                w.Indent();
                w.Line("versionSpec: \"21\"");
                w.Line("jdkArchitectureOption: \"x64\"");
                w.Line("jdkSourceOption: \"PreInstalled\"");
                w.Outdent();
                w.Outdent();
                break;
            case AppKind.Go:
                w.Line("- task: GoTool@0");
                w.Indent();
                w.Line("inputs:");
                w.Indent();
                w.Line("version: \"1.22\"");
                w.Outdent();
                w.Outdent();
                break;
        }
    }

    private static void EmitScriptStep(IndentedTextWriter w, LanguageStep step)
    {
        w.Line("- script: |");
        w.Indent();
        w.Line(step.Run);
        w.Outdent();
        w.Indent();
        w.Line($"displayName: {YamlValue.String(step.Name)}");
        w.Outdent();
    }
}
