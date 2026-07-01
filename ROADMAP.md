# Kubernator — Roadmap / Backlog

Working backlog of items identified during the offline Kubernetes cluster provisioning
work (`src/Kubernator.Core/ClusterProvisioning/`) and the earlier codebase review.
Not committed to any release; pick items up as needed.

## Cluster provisioning — remaining distro/security work

- [ ] **kubeadm-native distro plugin** — third `IClusterDistroProvisioner` implementation.
  Unlike RKE2/k3s (single vendor-provided installer + bundled containerd), this needs
  separate offline packaging for containerd, a CNI plugin, etcd, and the
  kubelet/kubeadm/kubectl binaries, plus `kubeadm init`/`kubeadm join` orchestration
  and manual HA bootstrap (`--control-plane` join, certs distribution). Meaningfully
  more work than RKE2/k3s were.
- [ ] **k3s HA follow-through** — HA path for k3s is wired (`cluster-init`, `tls-san`,
  shared `ApiServerPort`/`JoinPort` model) but has not been exercised against a real
  multi-node k3s cluster; RKE2 side is the one the abstractions were validated against.
- [x] **Real encryption-at-rest for vault entries** — `FileKeyVault` now encrypts every
  entry at rest with AES-256-GCM (DPAPI-wrapped on Windows) via a shared `SecretProtector`
  (moved to `Kubernator.Core.Security`, also used by the web auth TOTP secrets). The
  key-encryption-key is either sourced from `KUBERNATOR_VAULT_KEY` (base64, 32 bytes) or
  persisted alongside the vault as `vault.kek`. `ResolvePathAsync` decrypts on demand into
  a per-id cache file under `<vault>/.cache/`, which is purged on `RemoveAsync`, on
  `Dispose()`, and at startup (in case a prior process crashed before cleaning up). The
  `Encrypted` flag on `VaultEntry` is unchanged and still just describes whether the
  stored content itself (e.g. a PEM) is passphrase-protected.
- [ ] **RKE2/k3s version comparison** — `ClusterUpgradePlanner` treats "current version"
  as exact-string-equality only (no semver-with-build-suffix ordering, no downgrade
  protection). Fine for "skip if already current"; would need real ordering if
  downgrade prevention is ever required.
- [ ] **`cluster discover`** — build a `ClusterTopology` from a live cluster's kubeconfig
  instead of requiring a hand-written topology JSON file. Non-trivial: needs to reverse-map
  Kubernetes nodes back to SSH-reachable hosts.
- [ ] **Web Jobs UI polish** — `Cluster.razor` uses the same in-page `Progress<string>`
  pattern as `Build.razor`/`Deploy.razor` (log disappears if the page is left); the
  REST API's job-queue path (`ClusterController`) already streams progress into
  `IJobManager`, so a jobs-polling UI is possible later without touching Core.

## Pre-existing codebase backlog (unrelated to cluster provisioning)

- [x] **Rate limiter sync-over-async** — the partition callback no longer calls
  `IApiKeyStore.GetAsync(...).GetAwaiter().GetResult()` against SQLite on every request.
  Added `ApiKeyRateLimitCache` (a `BackgroundService` + in-memory `ConcurrentDictionary`)
  that refreshes all key rate limits from SQLite every 30s; `AdminApiKeysController`
  also primes/evicts it directly on key create/delete so a freshly created key's custom
  limit takes effect immediately instead of waiting for the next refresh tick.
- [ ] **Blazor UI bypasses the API/audit/rate-limit layer** — pages like `Monitor.razor`
  inject `Kubernator.Core` services directly rather than going through
  `Kubernator.Web.Api` controllers, so UI-driven actions aren't rate-limited,
  API-key-scoped, or written to the audit log (session cookie auth only). `Cluster.razor`
  follows the same existing convention for consistency, so it has the same gap.
- [x] **`Kubernator.Web` Dockerfile hardcodes `--allow-insecure-network=true`** — the
  flag was baked into `ENTRYPOINT` itself, so every consumer (`docker run`, the sample
  k8s manifest's `args:`, plain `docker compose`) silently inherited the insecure default
  with no visibility into it, and since the k8s manifest *also* passed `--bind`/
  `--allow-insecure-network` as `args:`, the process actually received those flags twice.
  Split the Dockerfile into `ENTRYPOINT ["dotnet", "/app/Kubernator.Web.dll"]` +
  `CMD ["--bind=0.0.0.0:5050"]` so the image is secure-by-default (refuses to bind
  non-loopback without `--trust-proxy-headers=true` or an explicit
  `--allow-insecure-network=true`, same as running the binary directly). The sample
  `deploy/k8s/web/deployment.yaml` still opts in via its own visible `args:` — that's an
  explicit, editable choice in a manifest with no bundled Ingress/TLS termination, not a
  hidden default.
- [ ] **No Podman implementation** — `IContainerEngine` is engine-agnostic but only
  `DockerEngine` exists in `Kubernator.Runtime`. Podman is a common ask in air-gapped/
  enterprise environments that avoid Docker.
- [ ] **`Kubernator.Web` Jobs system is in-memory and single-worker** — `InMemoryJobManager`
  loses all state on restart and `JobBackgroundRunner` processes one job at a time, so a
  long `bundle`/`build`/`cluster install` job blocks every other submitted job. Consider a
  small SQLite-backed queue with N workers.
- [ ] **Test gaps** — no test coverage for `Jobs/`, `Services/BuildPipeline.cs`, or the
  Blazor `Components/` pages.
- [x] **Serilog configuration duplication** — minimum levels/overrides and enrichers now
  live in `appsettings.json`'s `Serilog` section and are picked up by both loggers via
  `.ReadFrom.Configuration(...)` (the bootstrap logger builds a small standalone
  `IConfiguration` from `appsettings.json` + env vars before `WebApplication.CreateBuilder`
  runs). The two sinks (console + rolling compact-JSON file, whose path depends on
  `KUBERNATOR_HOME` and can't be a static JSON value) are now a single shared
  `ApplyKubernatorSinks()` extension in `Logging/SerilogSinks.cs` instead of two
  copy-pasted `WriteTo` blocks.
- [x] **NU1903 NuGet audit warning on `Kubernator.Web`** — `SQLitePCLRaw.lib.e_sqlite3`
  2.1.11 (transitive via `Microsoft.Data.Sqlite`) was flagged with a known high-severity
  advisory (GHSA-2m69-gcr7-jv3q / CVE-2025-6965, fixed upstream in SQLite 3.50.2+).
  `Directory.Packages.props` now pins the `SQLitePCLRaw.*` family to `3.0.3` (outside the
  vulnerable `<= 2.1.11` range) via central transitive pinning; `dotnet build`/`test` no
  longer need `--property:NuGetAudit=false`.
