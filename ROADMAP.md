# Kubernator — Roadmap / Backlog

Working backlog of items identified during the offline Kubernetes cluster provisioning
work (`src/Kubernator.Core/ClusterProvisioning/`) and the earlier codebase review.
Not committed to any release; pick items up as needed.

## Follow-through on previously-noted risks/deferrals

- [x] **kubeadm upgrade init-server signal** — `IClusterDistroProvisioner.UpgradeNodeAsync` now
  takes an explicit `isInitServer` flag instead of the kubelet-version self-detecting heuristic;
  `ClusterUpgradePlanner` orders the init control-plane node first so `kubeadm upgrade apply`
  runs there once and `kubeadm upgrade node` everywhere else. RKE2/k3s ignore the flag.
- [x] **Configurable pod CIDR** — `ClusterTopology.PodCidr` (default `10.244.0.0/16`) replaces the
  hard-coded `podSubnet`; the kubeadm provisioner rewrites the flannel `net-conf.json` Network /
  Calico `CALICO_IPV4POOL_CIDR` to match, but only when a non-default CIDR is set (default path
  unchanged). Validated as a real CIDR.
- [x] **Calico VXLAN mode** — `ClusterTopology.CalicoEncapsulation` (`bgp` default | `vxlan`);
  VXLAN flips `calico_backend` and the pool's VXLAN/IPIP env vars via targeted sed. Opt-in;
  needs real-cluster verification against the shipped Calico manifest version.
- [x] **`cluster discover` Web parity** — `POST /api/v1/cluster/discover` plus a "discover topology"
  form on `Clusters.razor`, both mirroring the CLI's reverse-mapping and returning the canonical
  `topology.json`. Covered by API tests and the first bUnit component tests (see below).
- [x] **Validate through the UI action gateway** — `Validate.razor`'s kind-cluster run now goes
  through `UiActionGateway` for audit + per-session rate limiting, like the other cluster-affecting
  pages. (The Jobs-UI rollout to Build/Bundle/Validate remains open: their durable job handlers
  deliberately do less than the interactive pages — they assume prior generate/build — so swapping
  the UI onto them is a behavior change needing product + browser verification, not a mechanical swap.)
- [x] **bUnit harness** — `bunit` 2.7.2 added; `Components/ClustersPageTests.cs` renders the page
  and drives the discover flow end-to-end. First Blazor component tests in the repo.

## Cluster provisioning — remaining distro/security work

- [x] **kubeadm-native distro plugin** — `KubeadmDistroProvisioner` added under
  `ClusterProvisioning/Distros/Kubeadm/`. Offline packaging: `ClusterArtifactBundleService`
  gained a kubeadm `BuildDownloadPlan` branch (containerd/runc/CNI-plugins/kubeadm/
  kubelet/kubectl binaries + both Flannel and Calico manifests, since the topology's CNI
  choice isn't known at pull time) and reuses the existing airgap image-bundling
  (`IImageBundleService`/`IContainerEngine`) to pull+export the `registry.k8s.io` images a
  new `KubeadmImageCatalog` table lists per k8s minor, imported on nodes via
  `ctr -n k8s.io images import`. `ClusterTopology.CniPlugin` must be `"flannel"` or
  `"calico"` for kubeadm topologies (validator-enforced — no silent default). The
  interface's single opaque `Token` string (designed around RKE2/k3s's shared-secret
  model) carries kubeadm's three distinct join values (bootstrap token, CA-cert-hash,
  control-plane certificate-key) via a `|`-delimited `KubeadmJoinToken` encode/decode,
  entirely internal to the provisioner — no changes to `IClusterDistroProvisioner` or
  `ClusterProvisioningService`. **Known open risk**: `UpgradeNodeAsync`'s signature only
  carries `NodeRole`, not `IsInitServer`, so it can't be told which control-plane node
  should run `kubeadm upgrade apply` (once, cluster-wide) vs `kubeadm upgrade node`
  (everywhere else); implemented as a self-detecting heuristic (checks whether any node's
  reported `kubeletVersion` already matches the target) rather than an interface change —
  works for the sequential-upgrade-order this codebase uses today, but is worth revisiting
  if that ordering assumption ever changes. Calico ships BGP-mode only (no VXLAN), and pod
  CIDR is hardcoded to Flannel's default (`10.244.0.0/16`) for both CNIs pending a
  `PodCidr` topology field. Kubeadm's own image/version tables need periodic manual
  updates as new Kubernetes minors are supported, same maintenance shape as RKE2/k3s's
  hardcoded per-version URLs.
- [x] **k3s HA follow-through** — code review found no structural bugs in the existing
  k3s HA wiring (`cluster-init`/`tls-san`/shared `ApiServerPort`/`JoinPort` abstractions
  are applied identically to RKE2's, just with `JoinPort` collapsed to the same `6443` as
  `ApiServerPort`). Since a real multi-node k3s cluster can't be provisioned here, "done"
  for this pass means test coverage: a `ClusterProvisioningServiceTests` case mirrors the
  existing RKE2 HA sequencing test with `DistroKind.K3s`, and a new
  `K3sDistroProvisionerTests.cs` (the first direct test of either RKE2/k3s provisioner,
  not just their config templates) asserts the exact remote config content and commands
  for first-server bootstrap, additional-server join, and agent join.
- [x] **Real encryption-at-rest for vault entries** — `FileKeyVault` now encrypts every
  entry at rest with AES-256-GCM (DPAPI-wrapped on Windows) via a shared `SecretProtector`
  (moved to `Kubernator.Core.Security`, also used by the web auth TOTP secrets). The
  key-encryption-key is either sourced from `KUBERNATOR_VAULT_KEY` (base64, 32 bytes) or
  persisted alongside the vault as `vault.kek`. `ResolvePathAsync` decrypts on demand into
  a per-id cache file under `<vault>/.cache/`, which is purged on `RemoveAsync`, on
  `Dispose()`, and at startup (in case a prior process crashed before cleaning up). The
  `Encrypted` flag on `VaultEntry` is unchanged and still just describes whether the
  stored content itself (e.g. a PEM) is passphrase-protected.
- [x] **RKE2/k3s version comparison** — new `DistroVersion`/`DistroVersionComparer`
  (`ClusterProvisioning/Upgrade/DistroVersion.cs`) parses `v<major>.<minor>.<patch>
  [-prerelease][+build]`, comparing the numeric core first and treating a same-core,
  different-build-suffix pair (e.g. `rke2r1` → `rke2r2`) as still needing a reinstall.
  Falls back to ordinal string comparison for anything that doesn't parse (kept
  deliberately lenient, not stricter, so it can't start throwing on unexpected input).
  `ClusterUpgradePlanner`'s `needsUpgrade` now calls this instead of raw `string.Equals`.
  This is ordering, not a downgrade guard — `DistroVersion.CompareCoreTo` is exposed as
  the primitive a future downgrade-prevention pass would use, per the original scoping.
- [x] **`cluster discover`** (CLI only this pass) — new `ClusterTopologyDiscoverer` in
  Core shells `kubectl get nodes -o json` (via the existing `IProcessRunner`, deliberately
  kept separate from `KubectlClusterMonitor`/`NodeStatus` to avoid touching that shared
  contract) and reverse-maps nodes to a best-effort `ClusterTopology`: `node-role.
  kubernetes.io/control-plane`/`master` labels → `NodeRole.Server` else `Agent`, each
  node's InternalIP (falling back to ExternalIP, then node name) → `NodeConnection.Host`,
  one shared SSH identity applied to every node from CLI flags (no per-node signal exists
  for credentials), and the oldest server by `creationTimestamp` chosen as `IsInitServer`
  (documented heuristic — nothing post-bootstrap identifies "the" init server). Runs the
  result through the existing `ClusterTopologyValidator` and surfaces warnings/errors
  rather than throwing, since fields like `FixedRegistrationAddresses` and
  `LocalArtifactBundlePath` have no cluster-side signal at all and are expected gaps for
  the operator to fill in. New `kubernator cluster discover` CLI action; Web UI/API parity
  intentionally deferred to a future pass.
- [x] **Web Jobs UI polish** — `Cluster.razor`'s pull/install/upgrade/status actions now
  go through `IJobManager` (the same job-queue path `ClusterController`'s REST endpoints
  already used) instead of an in-page `Progress<string>`, via a new reusable
  `JobProgressPanel.razor` component that polls `IJobManager.Get(id)` on a 1s
  `PeriodicTimer` and cleans itself up (`IAsyncDisposable`) regardless of whether the page
  is left mid-job. Progress now lives in server-side `JobState`, independent of the
  Blazor circuit. First jobs-polling UI pattern in this codebase (no prior art existed);
  scoped to this one page only, not the other 8 pages still using the old pattern. No
  automated test coverage added (this repo has zero Blazor component tests) — verified by
  building (Razor codegen catches most binding/type errors) and booting the app; a full
  interactive click-through wasn't possible in the session that made this change (no
  connected browser) and is worth a manual spot-check.

## Pre-existing codebase backlog (unrelated to cluster provisioning)

- [x] **Rate limiter sync-over-async** — the partition callback no longer calls
  `IApiKeyStore.GetAsync(...).GetAwaiter().GetResult()` against SQLite on every request.
  Added `ApiKeyRateLimitCache` (a `BackgroundService` + in-memory `ConcurrentDictionary`)
  that refreshes all key rate limits from SQLite every 30s; `AdminApiKeysController`
  also primes/evicts it directly on key create/delete so a freshly created key's custom
  limit takes effect immediately instead of waiting for the next refresh tick.
- [x] **Blazor UI bypasses the API/audit/rate-limit layer** — the interactive pages still
  inject `Kubernator.Core` services directly (routing every UI action back through a
  self-issued `/api/v1` HTTP call would mean minting a server-side API key for the circuit
  and re-serializing everything, an anti-pattern for an in-process Blazor Server app), but
  the three cross-cutting concerns the API middleware provided are now applied to UI actions
  through a shared `UiActionGateway` (`Ui/UiActionGateway.cs`, a scoped service) instead of
  the HTTP pipeline. Every cluster-affecting action is wrapped in `Gateway.InvokeAsync(name,
  work)`, which (1) **audits** to the same `AuditLog` singleton the API uses — one JSONL
  entry per action with `Method="UI"`, the action name as `Path`, the resolved status
  (`200`/`429`/`500`), duration, and — closing the "session cookie auth only, so we don't
  know who did it" gap — the logged-in username (via the circuit's
  `AuthenticationStateProvider`) plus its `auth_method` claim; (2) **rate-limits** per session
  user via a new singleton `UiActionRateLimiter` (`Ui/UiActionRateLimiter.cs`) that mirrors
  the HTTP `GlobalLimiter` — a fixed-window `PartitionedRateLimiter<string>` keyed by
  username at the same `SqliteApiKeyStore.DefaultRateLimitPerMinute` budget — since the
  circuit's SignalR traffic never reaches the `/api/v1`-scoped HTTP limiter; a refused action
  throws `UiRateLimitException`, which the pages surface as an error banner and log as a
  `429` audit entry. Wired into `Monitor.razor` (snapshot), `Deploy.razor`
  (`deploy/apply`+`deploy/dry-run`), and `Cluster.razor` (pull/install/upgrade/status job
  submission + SSH trust-probe) — the pages the original note named plus `Deploy` as the
  most cluster-sensitive one. Covered by `UiActionGatewayTests.cs`: success/failure audit
  attribution, anonymous fallback, per-user budget exhaustion, and per-user partitioning.
  API-key *scope* enforcement is intentionally not replicated — a cookie session is a single
  authenticated operator that logged in through password+TOTP, not a scoped machine key, so
  the meaningful gap was attribution (now closed via the audit user), not scope narrowing.
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
- [x] **No Podman implementation** — added `PodmanEngine`/`PodmanEngineProvider` under
  the new `Kubernator.Runtime/Podman/`, reusing `Docker.DotNet` against Podman's own
  Docker-API-compatible socket (`CONTAINER_HOST`, else `unix:///run/user/$UID/podman/
  podman.sock` → `unix:///run/podman/podman.sock` on Linux/macOS, the `podman-machine-
  default` named pipe on Windows) rather than reimplementing image build/pull/push/save
  against the Podman CLI. `PodmanEngine` wraps a `DockerEngine` and corrects the one field
  (`EngineInfo.Name`) that would otherwise still read `"docker"`. A new
  `ContainerEngineSelector` (now the `IContainerEngineProvider` registered by
  `AddKubernatorRuntime()`) tries Docker then Podman by default, or pins to one via
  `KUBERNATOR_CONTAINER_ENGINE=docker|podman`; none of the five existing CLI/Web call
  sites (`Build`/`Bundle`/`Pull`/`Rehost`/`Validate`) needed any changes since they were
  already coded against `IContainerEngineProvider`, not `DockerEngineProvider` directly.
  Also stood up the `Kubernator.Runtime.Tests` project (its `InternalsVisibleTo` entry
  already existed in `Kubernator.Runtime.csproj` but the project itself was never
  created) with tests for the selector's dispatch logic; `DockerEngine`/`PodmanEngine`
  themselves still have no tests since neither is mockable without a real daemon, the
  same pre-existing gap `DockerEngine`/`BuildxEngine` already had.
- [x] **`Kubernator.Web` Jobs system is in-memory and single-worker** — replaced
  `InMemoryJobManager`'s closure-based `JobSubmission.Work` (a `Func<...>` that can't
  survive a restart) with `SqliteJobManager` (`Jobs/SqliteJobManager.cs`, same
  connection-per-call + mutex pattern as `SqliteApiKeyStore`, at `KUBERNATOR_HOME/jobs/
  jobs.db`): jobs are persisted as `(kind, payload_json)` and dispatched through a new
  `IJobHandler`/`JobHandler<TPayload>` registry (one handler class per job kind under
  `Jobs/Handlers/`, resolved by `Kind` string) instead of an inline lambda, so every
  existing request DTO (`BuildRequest`, `ValidateRequest`, `BundleCreateRequest`,
  `ClusterPullRequest`) now doubles as the durable job payload with no new shape. The
  three cluster job kinds (`install`/`upgrade`/`status`) previously captured a pre-loaded
  `ClusterTopology` object in the closure — switched to capturing just `TopologyPath` and
  reloading it inside the handler at execution time, which incidentally let `Cluster.razor`
  drop ~80 lines of duplicated inline job logic and share the same handler classes as
  `ClusterController`. `JobBackgroundRunner` now runs `KUBERNATOR_JOB_WORKERS` (default 4)
  concurrent workers pulling job IDs off a shared channel instead of one `await foreach`
  loop, so a long-running job no longer blocks the queue. On startup, any job still marked
  `Running` (the previous process died mid-execution) is reset to `Queued` and re-dispatched
  — there's no checkpointing, so a resumed job restarts from scratch rather than resuming
  mid-step. `JobRecord.Result`/`JobDto.Result` changed from `object?` (the live in-memory
  DTO instance) to `JsonElement?` (deserialized from the persisted `result_json` column);
  `Cluster.razor`'s four completion handlers now call `.Deserialize<T>(JobJson.Options)`
  instead of an `as` cast. Added `InternalsVisibleTo` for `Kubernator.Web.Tests` (mirroring
  the existing `Kubernator.Runtime`/`Kubernator.Runtime.Tests` pattern) so
  `SqliteJobManagerTests.cs` can drive `JobBackgroundRunner` directly with an explicit
  worker count and a test-only `DelegateJobHandler<T>`, covering: result persistence,
  cancel-while-queued vs. cancel-while-running, genuine worker concurrency (a `Barrier`
  that only releases once N workers are inside the handler simultaneously), the
  crash-recovery requeue path, and handler-exception → `Failed` with the error captured.
  Verified end-to-end against the real `dotnet run` binary (not just the test host): a
  `POST /api/v1/build` job persists to `jobs.db`, executes on a worker, and the polled
  `GET /api/v1/jobs` result JSON round-trips correctly.
- [x] **Test gaps (`Jobs/` + `Services/BuildPipeline.cs`)** — `Jobs/` is covered by
  `SqliteJobManagerTests.cs` (see above). `Services/BuildPipeline.cs` is now covered by
  `BuildPipelineTests.cs` (NSubstitute-mocked `IAnalysisService`/`IStrategySelector`/
  `IGenerationService`/`IContainerEngineProvider`, first use of NSubstitute in
  `Kubernator.Web.Tests`), covering: the `NoBuild` early return never touching the container
  engine; a full build staging the source tree plus the generated `Dockerfile`/
  `.dockerignore` into `build-context/`; the case where the default output directory
  (`<path>/.kubernator`) sits *inside* the source path, which exercises
  `CopyDirectoryAsync`'s exclusion of `excludeRoot` — without it, the build context would
  recursively copy itself into itself; a multi-platform request against an engine that
  doesn't support it throwing before `BuildAsync` is ever called; and a custom output
  directory outside the source path, which copies the source tree unfiltered since there's
  nothing to exclude. The Blazor `Components/` pages still have no test coverage — this repo
  has no Blazor component test harness (e.g. bUnit) set up yet, which is a bigger, separate
  lift.
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
