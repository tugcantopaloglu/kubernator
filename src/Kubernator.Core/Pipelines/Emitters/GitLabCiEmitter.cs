using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Models;

namespace Kubernator.Core.Pipelines.Emitters;

internal static class GitLabCiEmitter
{
    public static string Emit(PipelineOptions options)
    {
        var profile = LanguageBuildSteps.For(options.AppKind, options);
        var image = ResolveImage(options.AppKind);

        var w = new IndentedTextWriter();
        w.Line("default:");
        w.Indent();
        w.Line($"image: {YamlValue.String(image)}");
        w.Outdent();
        w.Line("stages:");
        w.Indent();
        w.Line("- build");
        w.Line("- test");
        w.Line("- package");
        w.Outdent();
        w.Line("variables:");
        w.Indent();
        w.Line($"IMAGE_NAME: {YamlValue.String(options.ImageName)}");
        w.Line($"IMAGE_TAG: {YamlValue.String(options.ImageTag)}");
        w.Line($"REGISTRY: {YamlValue.String(options.Registry)}");
        w.Outdent();
        w.Line("cache:");
        w.Indent();
        w.Line($"key: {YamlValue.String(profile.CacheKey)}");
        w.Line("paths:");
        w.Indent();
        w.Line($"- {YamlValue.String(profile.CachePath)}");
        w.Outdent();
        w.Outdent();

        w.Line("build:");
        w.Indent();
        w.Line("stage: build");
        w.Line("script:");
        w.Indent();
        foreach (var step in profile.Build)
        {
            w.Line($"- {YamlValue.String(step.Run)}");
        }
        w.Outdent();
        w.Line("artifacts:");
        w.Indent();
        w.Line("paths:");
        w.Indent();
        w.Line($"- {YamlValue.String(profile.PublishOutput)}");
        w.Outdent();
        w.Line("expire_in: 1 day");
        w.Outdent();
        w.Outdent();

        if (profile.Test.Count > 0)
        {
            w.Line("test:");
            w.Indent();
            w.Line("stage: test");
            w.Line("needs: [\"build\"]");
            w.Line("script:");
            w.Indent();
            foreach (var step in profile.Test)
            {
                w.Line($"- {YamlValue.String(step.Run)}");
            }
            w.Outdent();
            w.Outdent();
        }

        w.Line("package:");
        w.Indent();
        w.Line("stage: package");
        w.Line("needs: [\"build\"]");
        w.Line("services:");
        w.Indent();
        w.Line("- docker:dind");
        w.Outdent();
        w.Line("variables:");
        w.Indent();
        w.Line("DOCKER_HOST: tcp://docker:2375");
        w.Line("DOCKER_TLS_CERTDIR: \"\"");
        w.Outdent();
        w.Line("script:");
        w.Indent();
        w.Line($"- curl -sSL https://github.com/kubernator/kubernator/releases/download/v{options.KubernatorVersion}/kubernator-linux-x64 -o /usr/local/bin/kubernator && chmod +x /usr/local/bin/kubernator");
        w.Line($"- kubernator bundle {options.PublishPath} -o ./out/$IMAGE_NAME-$IMAGE_TAG.kubpack --namespace {options.Namespace} --name $IMAGE_NAME --tag $IMAGE_TAG");
        if (options.SignBundle)
        {
            w.Line("- kubernator sign ./out/$IMAGE_NAME-$IMAGE_TAG.kubpack --key $COSIGN_KEY_PATH --password \"$COSIGN_PASSWORD\"");
        }
        if (options.RunVerify)
        {
            var args = options.SignBundle ? " --require-signature" : "";
            w.Line($"- kubernator verify ./out/$IMAGE_NAME-$IMAGE_TAG.kubpack{args}");
        }
        w.Outdent();
        w.Line("artifacts:");
        w.Indent();
        w.Line("paths:");
        w.Indent();
        w.Line("- out/*.kubpack*");
        w.Outdent();
        w.Line("expire_in: 30 days");
        w.Outdent();
        w.Outdent();

        return w.ToString();
    }

    private static string ResolveImage(AppKind kind) => kind switch
    {
        AppKind.DotNet => "mcr.microsoft.com/dotnet/sdk:10.0",
        AppKind.NodeJs => "node:20",
        AppKind.StaticWeb => "node:20",
        AppKind.Python => "python:3.12",
        AppKind.Java => "maven:3.9-eclipse-temurin-21",
        AppKind.Go => "golang:1.22",
        _ => "alpine:3.20"
    };
}
