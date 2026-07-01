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
- [ ] **Real encryption-at-rest for vault entries** — `FileKeyVault`'s `Encrypted` flag
  on a `VaultEntry` is metadata only; nothing actually encrypts the file. This affects
  every vault-held secret, not just the new `VaultEntryKind.SshPrivateKey` entries added
  for node credentials — SSH private keys currently get the same file-permission-only
  protection as everything else in the vault.
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

- [ ] **Rate limiter sync-over-async** (`Kubernator.Web/Program.cs`, partition callback)
  — does a blocking `.GetAwaiter().GetResult()` against SQLite on every request to read
  per-key rate limits. Under load this can starve the thread pool. Should cache
  scopes/limits in memory with periodic refresh instead.
- [ ] **Blazor UI bypasses the API/audit/rate-limit layer** — pages like `Monitor.razor`
  inject `Kubernator.Core` services directly rather than going through
  `Kubernator.Web.Api` controllers, so UI-driven actions aren't rate-limited,
  API-key-scoped, or written to the audit log (session cookie auth only). `Cluster.razor`
  follows the same existing convention for consistency, so it has the same gap.
- [ ] **`Kubernator.Web` Dockerfile hardcodes `--allow-insecure-network=true`** in the
  `ENTRYPOINT`, disabling the loopback-only safety check by default inside the
  container. Either remove it or document the TLS-termination requirement clearly.
- [ ] **No Podman implementation** — `IContainerEngine` is engine-agnostic but only
  `DockerEngine` exists in `Kubernator.Runtime`. Podman is a common ask in air-gapped/
  enterprise environments that avoid Docker.
- [ ] **`Kubernator.Web` Jobs system is in-memory and single-worker** — `InMemoryJobManager`
  loses all state on restart and `JobBackgroundRunner` processes one job at a time, so a
  long `bundle`/`build`/`cluster install` job blocks every other submitted job. Consider a
  small SQLite-backed queue with N workers.
- [ ] **Test gaps** — no test coverage for `Jobs/`, `Services/BuildPipeline.cs`, or the
  Blazor `Components/` pages.
- [ ] **Serilog configuration duplication** — `Kubernator.Web/Program.cs` configures the
  bootstrap logger and the full logger separately with overlapping settings, and none of
  it lives in `appsettings.json`.
- [ ] **NU1903 NuGet audit warning on `Kubernator.Web`** — `SQLitePCLRaw.lib.e_sqlite3`
  2.1.11 (transitive via `Microsoft.Data.Sqlite`) is flagged with a known high-severity
  advisory, which fails the build under this repo's `TreatWarningsAsErrors=true`. Worked
  around locally with `--property:NuGetAudit=false`; needs an upstream package bump or an
  explicit suppression if it doesn't resolve on its own.
