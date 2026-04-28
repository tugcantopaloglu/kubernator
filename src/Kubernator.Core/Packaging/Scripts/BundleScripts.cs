namespace Kubernator.Core.Packaging.Scripts;

internal static class BundleScripts
{
    public const string InstallSh = """
        #!/usr/bin/env bash
        set -euo pipefail
        DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
        cd "$DIR"

        engine=""
        if command -v docker >/dev/null 2>&1; then
            engine=docker
        elif command -v podman >/dev/null 2>&1; then
            engine=podman
        elif command -v nerdctl >/dev/null 2>&1; then
            engine=nerdctl
        else
            echo "no container engine found (need docker, podman, or nerdctl)" >&2
            exit 1
        fi

        echo "verifying bundle integrity"
        sha256sum -c manifest.sha256

        echo "loading container images via $engine"
        for img in images/*.tar; do
            "$engine" load -i "$img"
        done

        if command -v kubectl >/dev/null 2>&1; then
            echo "applying kubernetes manifests"
            kubectl apply -f manifests/
        else
            echo "kubectl not found; skipping apply (manifests/ ready in $DIR/manifests)"
        fi

        echo "install complete"
        """;

    public const string InstallPs1 = """
        $ErrorActionPreference = 'Stop'
        $dir = Split-Path -Parent $MyInvocation.MyCommand.Path
        Set-Location $dir

        $engine = $null
        foreach ($candidate in @('docker','podman','nerdctl')) {
            if (Get-Command $candidate -ErrorAction SilentlyContinue) {
                $engine = $candidate
                break
            }
        }
        if (-not $engine) { throw 'no container engine found (need docker, podman, or nerdctl)' }

        Write-Host 'verifying bundle integrity'
        & "$dir/verify.ps1"

        Write-Host "loading container images via $engine"
        Get-ChildItem -Path "$dir/images" -Filter '*.tar' | ForEach-Object {
            & $engine load -i $_.FullName
        }

        if (Get-Command kubectl -ErrorAction SilentlyContinue) {
            Write-Host 'applying kubernetes manifests'
            & kubectl apply -f "$dir/manifests/"
        } else {
            Write-Host "kubectl not found; manifests ready in $dir/manifests"
        }

        Write-Host 'install complete'
        """;

    public const string VerifySh = """
        #!/usr/bin/env bash
        set -euo pipefail
        DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
        cd "$DIR"
        sha256sum -c manifest.sha256
        echo "bundle integrity verified"
        """;

    public const string VerifyPs1 = """
        $ErrorActionPreference = 'Stop'
        $dir = Split-Path -Parent $MyInvocation.MyCommand.Path
        Set-Location $dir
        Get-Content 'manifest.sha256' | ForEach-Object {
            $parts = $_ -split '\s+', 2
            if ($parts.Length -lt 2) { return }
            $expected = $parts[0]
            $relative = $parts[1].TrimStart('*')
            if (-not (Test-Path $relative)) { throw "missing file: $relative" }
            $actual = (Get-FileHash -Algorithm SHA256 $relative).Hash.ToLower()
            if ($actual -ne $expected.ToLower()) {
                throw "hash mismatch for $relative (expected $expected, got $actual)"
            }
        }
        Write-Host 'bundle integrity verified'
        """;
}
