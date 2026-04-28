using Kubernator.Core.Generation.Emitters;
using Kubernator.Core.Models;

namespace Kubernator.Core.Pipelines.Emitters;

internal static class TektonEmitter
{
    public static (string Pipeline, string Task) Emit(PipelineOptions options)
    {
        return (EmitPipeline(options), EmitTask(options));
    }

    private static string EmitPipeline(PipelineOptions options)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: tekton.dev/v1");
        w.Line("kind: Pipeline");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(options.ImageName + "-pipeline")}");
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("params:");
        w.Indent();
        w.Line("- name: source-url");
        w.Indent();
        w.Line("type: string");
        w.Outdent();
        w.Line("- name: image-tag");
        w.Indent();
        w.Line("type: string");
        w.Line($"default: {YamlValue.String(options.ImageTag)}");
        w.Outdent();
        w.Outdent();
        w.Line("workspaces:");
        w.Indent();
        w.Line("- name: source");
        w.Outdent();
        w.Line("tasks:");
        w.Indent();
        w.Line("- name: clone");
        w.Indent();
        w.Line("taskRef:");
        w.Indent();
        w.Line("name: git-clone");
        w.Outdent();
        w.Line("workspaces:");
        w.Indent();
        w.Line("- name: output");
        w.Indent();
        w.Line("workspace: source");
        w.Outdent();
        w.Outdent();
        w.Line("params:");
        w.Indent();
        w.Line("- name: url");
        w.Indent();
        w.Line("value: $(params.source-url)");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Line($"- name: build-and-bundle");
        w.Indent();
        w.Line("runAfter: [\"clone\"]");
        w.Line("taskRef:");
        w.Indent();
        w.Line($"name: {YamlValue.String("kubernator-bundle-" + options.ImageName)}");
        w.Outdent();
        w.Line("workspaces:");
        w.Indent();
        w.Line("- name: source");
        w.Indent();
        w.Line("workspace: source");
        w.Outdent();
        w.Outdent();
        w.Line("params:");
        w.Indent();
        w.Line("- name: image-tag");
        w.Indent();
        w.Line("value: $(params.image-tag)");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    private static string EmitTask(PipelineOptions options)
    {
        var profile = LanguageBuildSteps.For(options.AppKind, options);
        var image = ResolveImage(options.AppKind);
        var w = new IndentedTextWriter();
        w.Line("apiVersion: tekton.dev/v1");
        w.Line("kind: Task");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String("kubernator-bundle-" + options.ImageName)}");
        w.Outdent();
        w.Line("spec:");
        w.Indent();
        w.Line("params:");
        w.Indent();
        w.Line("- name: image-tag");
        w.Indent();
        w.Line("type: string");
        w.Outdent();
        w.Outdent();
        w.Line("workspaces:");
        w.Indent();
        w.Line("- name: source");
        w.Outdent();
        w.Line("steps:");
        w.Indent();

        var stepIndex = 0;
        EmitStep(w, $"step-{stepIndex++}-build", image, $"cd $(workspaces.source.path) && {string.Join(" && ", profile.Build.Select(s => s.Run))}");
        if (profile.Test.Count > 0)
        {
            EmitStep(w, $"step-{stepIndex++}-test", image, $"cd $(workspaces.source.path) && {string.Join(" && ", profile.Test.Select(s => s.Run))}");
        }
        EmitStep(w, $"step-{stepIndex++}-bundle",
            "registry.example.com/kubernator:latest",
            $"cd $(workspaces.source.path) && kubernator bundle {options.PublishPath} -o ./out/{options.ImageName}-$(params.image-tag).kubpack --namespace {options.Namespace} --name {options.ImageName} --tag $(params.image-tag)");
        if (options.SignBundle)
        {
            EmitStep(w, $"step-{stepIndex++}-sign",
                "registry.example.com/kubernator:latest",
                $"cd $(workspaces.source.path) && kubernator sign ./out/{options.ImageName}-$(params.image-tag).kubpack --key /etc/cosign/cosign.key");
        }

        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    private static void EmitStep(IndentedTextWriter w, string name, string image, string script)
    {
        w.Line($"- name: {YamlValue.String(name)}");
        w.Indent();
        w.Line($"image: {YamlValue.String(image)}");
        w.Line("script: |");
        w.Indent();
        w.Line("#!/bin/sh");
        w.Line("set -eu");
        w.Line(script);
        w.Outdent();
        w.Outdent();
    }

    private static string ResolveImage(AppKind kind) => kind switch
    {
        AppKind.DotNet => "mcr.microsoft.com/dotnet/sdk:10.0",
        AppKind.NodeJs => "cgr.dev/chainguard/node:latest-dev",
        AppKind.StaticWeb => "cgr.dev/chainguard/node:latest-dev",
        AppKind.Python => "cgr.dev/chainguard/python:latest-dev",
        AppKind.Java => "cgr.dev/chainguard/jdk:latest-dev",
        AppKind.Go => "cgr.dev/chainguard/go:latest-dev",
        _ => "alpine:3.20"
    };
}
