# Working on dotnet-containerizer

## Two defaults that always hold

Every generator in this repository has to satisfy both of these without the user passing a switch.
Treat them as acceptance criteria for any new generated asset, not as features that can be added later.

### 1. Handle every .NET version automatically

- Read the framework from the project file, never assume one. `ProjectAnalyzer.ParseFramework` decides
  what is supported; a multi targeted project is containerized on its newest target framework.
- Compare framework versions **as numbers**. Ordering them as text puts `8.0` after `10.0` and picks the
  wrong SDK, base image and port defaults. `ProjectInfo.FrameworkMajorVersion` exists for this.
- Every project keeps its own base image tag, and a solution that mixes versions gets one `UseDotNet@2`
  step per version in the pipeline, oldest first.
- Behaviour that differs per version belongs in one place: `ContainerPorts` for ports and
  `DockerfileGenerator.AppendNonRootUser` for the user account. Do not spread version checks around.
- A new .NET release must need no code change. If it does, the version handling is wrong.

### 2. Apply security hardening by default

Hardening is on unless `--no-hardening` is passed, and `--no-hardening` means *Visual Studio's defaults*,
never something less safe than the plain baseline. What hardening adds:

| Asset | Hardened default |
| --- | --- |
| Dockerfile | Non root user on every version: `$APP_UID` from .NET 8, a created `app` account before that, `ContainerUser` on Windows |
| Dockerfile | HTTP port 8080 on every version, because a non root user cannot bind port 80 |
| Chart | `readOnlyRootFilesystem: true` with an emptyDir on `/tmp`, `seccompProfile: RuntimeDefault`, no service account token |
| Chart | `runAsNonRoot`, `allowPrivilegeEscalation: false`, `privileged: false` and all capabilities dropped — these stay on even without hardening |
| Pipeline | `dotnet list package --vulnerable --include-transitive` fails the build on a known vulnerability |

Anything that can silently break a working deployment stays opt in and documented, for example the
NetworkPolicy template, which needs a CNI plugin that enforces policies.

## House rules

- No external NuGet dependencies in the tool, the BCL is enough.
- Generated files are never overwritten without `--force`.
- Cover new generated output with tests in `tests/DotnetContainerizer.Tests`, and validate chart changes
  with `helm lint` and `helm template` before committing.
- Every push to `main` publishes release binaries through `.github/workflows/release.yml`, so main has to
  stay releasable: the tool is published trimmed and self contained, and trim warnings are build errors.
