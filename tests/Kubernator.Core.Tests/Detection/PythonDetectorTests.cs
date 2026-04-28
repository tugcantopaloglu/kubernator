using Kubernator.Core.Detection.Python;
using Kubernator.Core.Models;
using Kubernator.Core.Tests.Fixtures;

namespace Kubernator.Core.Tests.Detection;

public sealed class PythonDetectorTests
{
    private readonly PythonDetector detector = new();

    [Fact]
    public async Task Detects_fastapi_via_requirements()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("requirements.txt", "fastapi==0.111\nuvicorn==0.30\n");
        t.WriteFile("main.py", "");

        var result = await detector.DetectAsync(t.Path);

        result.Kind.Should().Be(AppKind.Python);
        result.Flavor.Should().Be(AppFlavor.PythonFastApi);
    }

    [Fact]
    public async Task Detects_django_via_manage_py()
    {
        using var t = TempPublishOutput.Create();
        t.WriteFile("manage.py", "");
        t.WriteFile("requirements.txt", "Django>=5.0\n");

        var result = await detector.DetectAsync(t.Path);

        result.Flavor.Should().Be(AppFlavor.PythonDjango);
    }
}
