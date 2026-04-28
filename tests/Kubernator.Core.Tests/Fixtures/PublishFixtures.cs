namespace Kubernator.Core.Tests.Fixtures;

internal static class PublishFixtures
{
    public static TempPublishOutput AspNetCorePublish(string assemblyName = "MyWebApp")
    {
        var output = TempPublishOutput.Create();
        output.WriteFile($"{assemblyName}.deps.json", BuildDepsJson(assemblyName, includeAspNet: true));
        output.WriteFile($"{assemblyName}.runtimeconfig.json", BuildRuntimeConfig(includeAspNet: true));
        output.WriteFile($"{assemblyName}.dll", string.Empty);
        output.WriteFile("appsettings.json", """
        {
          "Logging": { "LogLevel": { "Default": "Information" } },
          "Urls": "http://+:5080;https://+:5443",
          "Kestrel": {
            "Endpoints": {
              "Http": { "Url": "http://+:8080" }
            }
          }
        }
        """);
        return output;
    }

    public static TempPublishOutput ConsolePublish(string assemblyName = "MyConsole")
    {
        var output = TempPublishOutput.Create();
        output.WriteFile($"{assemblyName}.deps.json", BuildDepsJson(assemblyName, includeAspNet: false));
        output.WriteFile($"{assemblyName}.runtimeconfig.json", BuildRuntimeConfig(includeAspNet: false));
        output.WriteFile($"{assemblyName}.dll", string.Empty);
        return output;
    }

    public static TempPublishOutput SourceTree()
    {
        var output = TempPublishOutput.Create();
        output.WriteFile("MyApp.csproj", """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
        return output;
    }

    private static string BuildRuntimeConfig(bool includeAspNet)
    {
        var frameworks = includeAspNet
            ? """
              "frameworks": [
                { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" }
              ]
              """
            : """
              "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
              """;

        return $$"""
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            {{frameworks}},
            "configProperties": {
              "System.GC.Server": true,
              "System.Globalization.Invariant": false
            }
          }
        }
        """;
    }

    private static string BuildDepsJson(string assemblyName, bool includeAspNet)
    {
        var aspLib = includeAspNet
            ? """, "Microsoft.AspNetCore.App.Runtime/10.0.0": { "type": "package" } """
            : string.Empty;

        return $$"""
        {
          "runtimeTarget": {
            "name": ".NETCoreApp,Version=v10.0/linux-x64",
            "signature": ""
          },
          "compilationOptions": {},
          "targets": {
            ".NETCoreApp,Version=v10.0/linux-x64": {
              "{{assemblyName}}/1.0.0": {
                "runtime": { "{{assemblyName}}.dll": {} }
              }
            }
          },
          "libraries": {
            "{{assemblyName}}/1.0.0": { "type": "project", "serviceable": false, "sha512": "" }
            {{aspLib}}
          }
        }
        """;
    }
}
