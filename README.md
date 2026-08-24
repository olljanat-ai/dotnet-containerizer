# dotnet-containerizer

Visual Studio can add a `Dockerfile` to a .NET project when the project is created. This tool does the
same thing for solutions that already exist: it scans a folder, works out which projects can run in a
container, and writes the container assets for them.

```
dotnet-containerize [path] [options]
```

## What it generates

| File | Location | Purpose |
| --- | --- | --- |
| `Dockerfile` | next to every containerizable project | Multi stage build, same layout Visual Studio produces |
| `.dockerignore` | build context root | Keeps `bin`, `obj`, `.git` and friends out of the build context |
| `azure-pipelines.yml` | repository root | Builds and pushes an image per project to Azure Container Registry |
| `.azuredevops/templates/build-image.yml` | repository root | Job template used once per project by the pipeline |
| `.azuredevops/templates/helm-deploy.yml` | repository root | Deployment job that runs `helm upgrade --install` |
| `helm/<chart>/` | repository root | Helm chart that deploys every component of the solution |

## How projects are classified

The scanner reads every `*.sln`, `*.slnx`, `*.csproj`, `*.fsproj` and `*.vbproj` below the given folder
(`bin`, `obj`, `.git`, `node_modules` and similar folders are ignored) and inspects each project file:

| Project | Result |
| --- | --- |
| `Microsoft.NET.Sdk.Web`, or a reference to `Microsoft.AspNetCore.App` | `mcr.microsoft.com/dotnet/aspnet` image, ports exposed |
| `Microsoft.NET.Sdk.Worker`, or `OutputType` of `Exe` | `mcr.microsoft.com/dotnet/runtime` image |
| Class library, test project, Blazor WebAssembly, WinExe, .NET Framework | skipped, with the reason printed |

Multi targeted projects are containerized for their newest target framework, and every project keeps the
base image of its own framework version, so a solution that mixes .NET 6, 8 and 10 needs no switches.

The build context is the solution folder when exactly one solution file is found, otherwise the scanned
folder. Project references are copied into the image before `dotnet restore` runs, so the restore stays
in its own cached layer.

## Security hardening

Hardening is applied by default, `--no-hardening` falls back to the plain Visual Studio output:

| | Hardened (default) | `--no-hardening` |
| --- | --- | --- |
| Image user | Non root on every version: `$APP_UID` from .NET 8, a created `app` account before that, `ContainerUser` on Windows | `$APP_UID` from .NET 8, root before that |
| HTTP port | `8080` on every version, a non root user cannot bind port 80 | `8080` from .NET 8, `80` before that |
| Root filesystem | `readOnlyRootFilesystem: true` with an emptyDir mounted on `/tmp` | writable |
| Pod | `seccompProfile: RuntimeDefault`, no service account token mounted | defaults |
| Pipeline | Fails on a NuGet package with a known vulnerability | no audit |

`runAsNonRoot`, `allowPrivilegeEscalation: false`, `privileged: false` and dropping all capabilities apply
either way, they cost nothing and no .NET workload needs them off.

A `NetworkPolicy` template is generated but left `enabled: false`: it is only enforced by clusters whose
CNI plugin supports it, so switching it on is the cluster owner's call. Once enabled, web components accept
traffic from the release and the ingress namespace, workers accept none.

## The generated Azure DevOps pipeline

`azure-pipelines.yml` builds one container image per containerizable project and pushes it to Azure
Container Registry through the `Docker@2` task. Pull request builds only build the images, builds of the
default branch build and push them tagged with `$(Build.BuildNumber)` and `latest`. When the solution
contains test projects or hardening is on, a `Validate` job runs first — installing one SDK per framework
version in the solution, running `dotnet test`, and failing the build on packages with known
vulnerabilities — and the image jobs depend on it. A second stage
deploys the Helm chart with `helm upgrade --install` through a Kubernetes service connection, using the
tag that was just built.

Before the first run, create a **Docker Registry** service connection in *Project settings -> Service
connections* that points to your ACR, then set the pipeline variables:

| Variable | Meaning | Set it with |
| --- | --- | --- |
| `dockerRegistryServiceConnection` | Name of the Docker registry service connection | `--service-connection` |
| `containerRegistry` | ACR login server, e.g. `contoso.azurecr.io` | `--registry` |
| `imagePrefix` | Repository prefix, images become `<prefix>/<project>` | `--image-prefix` |
| `kubernetesServiceConnection` | Kubernetes service connection used by the deploy stage | `--kubernetes-connection` |
| `kubernetesNamespace` | Namespace the release is deployed into | `--namespace` |

Paths inside the pipeline are relative to the repository root, so the pipeline keeps working when the
solution lives in a subfolder of the repository.

## The generated Helm chart

The chart has one generic set of templates and a `components` map in `values.yaml` with one entry per
project, so the whole solution is deployed by a single release:

```
helm/contoso
├── Chart.yaml
├── values.yaml
└── templates
    ├── _helpers.tpl
    ├── deployment.yaml     # one Deployment per enabled component
    ├── service.yaml        # one Service per component that listens on a port
    ├── ingress.yaml        # one Ingress per component with ingress.enabled
    ├── networkpolicy.yaml  # opt in, see Security hardening
    ├── serviceaccount.yaml
    └── NOTES.txt
```

ASP.NET Core projects get a container port, a `ClusterIP` service, `ASPNETCORE_HTTP_PORTS` and a
disabled ingress and probe block to fill in. Worker and console projects get a Deployment only. Every
component can be tuned on its own, and turned off with `enabled: false`:

```yaml
components:
  contoso-api:
    enabled: true
    replicaCount: 2
    containerPort: 8080
    service:
      enabled: true
      port: 80
    ingress:
      enabled: false
    probes:
      enabled: false
      path: /healthz
```

Images are assembled as `<image.registry>/<image.prefix>/<component.repository>:<tag>`, so the deploy
stage of the pipeline only has to pass the tag it just built:

```bash
helm upgrade --install contoso helm/contoso   --namespace contoso --create-namespace   --set image.registry=contoso.azurecr.io   --set image.prefix=contoso   --set image.tag=20240521.3
```

Validate a generated chart with `helm lint helm/contoso` and `helm template contoso helm/contoso`.

## Options

| Option | Description |
| --- | --- |
| `-p`, `--path <folder>` | Folder to scan. Default: current folder |
| `--os <linux\|windows>` | Container operating system. Default: `linux` |
| `--include-tests` | Also generate assets for test projects |
| `--registry <server>` | ACR login server for the pipeline. Default: `myregistry.azurecr.io` |
| `--service-connection <name>` | Docker registry service connection name. Default: `acr-service-connection` |
| `--image-prefix <name>` | Image repository prefix. Default: the solution name |
| `--no-dockerfile` | Do not generate Dockerfiles |
| `--no-pipeline` | Do not generate the Azure DevOps pipeline |
| `--chart-name <name>` | Helm chart name. Default: the solution name |
| `--namespace <name>` | Kubernetes namespace to deploy into. Default: `default` |
| `--kubernetes-connection <name>` | Kubernetes service connection name. Default: `aks-service-connection` |
| `--no-helm` | Do not generate the Helm chart |
| `--no-hardening` | Keep the plain Visual Studio defaults instead of the hardened ones |
| `-f`, `--force` | Overwrite files that already exist |
| `--dry-run` | Report what would be written without touching the disk |
| `-l`, `--list` | Only list the discovered projects |
| `-v`, `--verbose` | Print why projects were skipped |
| `-h`, `--help` | Show the help text |
| `--version` | Show the tool version |

Existing files are never overwritten without `--force`, so hand tuned Dockerfiles survive a re-run.

## Download

Every push to `main` publishes a GitHub release with self contained single file binaries, one per
platform. They carry their own .NET runtime, so nothing has to be installed first:

| Platform | Asset |
| --- | --- |
| Linux x64 | `dotnet-containerize-<version>-linux-x64.tar.gz` |
| Linux arm64 | `dotnet-containerize-<version>-linux-arm64.tar.gz` |
| Windows x64 | `dotnet-containerize-<version>-win-x64.zip` |
| Windows arm64 | `dotnet-containerize-<version>-win-arm64.zip` |

```bash
tar -xzf dotnet-containerize-0.1.42-linux-x64.tar.gz
./dotnet-containerize ./my-solution
```

Each release also carries `SHA256SUMS.txt`, verify a download with `sha256sum --check SHA256SUMS.txt`.

## Building and running

```bash
dotnet build DotnetContainerizer.sln
dotnet test DotnetContainerizer.sln
dotnet run --project src/DotnetContainerizer -- ../my-solution --verbose
```

Install it as a global tool:

```bash
dotnet pack src/DotnetContainerizer -c Release
dotnet tool install --global --add-source src/DotnetContainerizer/bin/Release DotnetContainerizer
dotnet-containerize ./my-solution
```

## Example

```
$ dotnet-containerize ./contoso --verbose
Scanned /home/dev/contoso
  solution: Contoso.sln
  build context: /home/dev/contoso

Containerizable projects (2):
  Contoso.Api [asp.net core, net8.0, port 8080/8081] -> src/Contoso.Api/Contoso.Api.csproj
  Contoso.Worker [console/worker, net8.0] -> src/Contoso.Worker/Contoso.Worker.csproj

Skipped projects (2):
  Contoso.Core: class library
  Contoso.Tests: test project

created    src/Contoso.Api/Dockerfile
created    src/Contoso.Worker/Dockerfile
created    .dockerignore
created    azure-pipelines.yml
created    .azuredevops/templates/build-image.yml
created    .azuredevops/templates/helm-deploy.yml
created    helm/contoso/Chart.yaml
created    helm/contoso/values.yaml
created    helm/contoso/.helmignore
created    helm/contoso/templates/_helpers.tpl
created    helm/contoso/templates/serviceaccount.yaml
created    helm/contoso/templates/deployment.yaml
created    helm/contoso/templates/service.yaml
created    helm/contoso/templates/ingress.yaml
created    helm/contoso/templates/networkpolicy.yaml
created    helm/contoso/templates/NOTES.txt
```

## Continuous integration for this repository

Neither of these is the pipeline the tool generates, the generated one is described above.

- `.github/workflows/release.yml` runs the tests and the package audit on every push to `main`, builds
  the four platform binaries, smoke tests the Linux one, and publishes them as release `v0.1.<run
  number>` with checksums. Only the release job gets `contents: write`.
- `azure-pipelines.yml` builds, tests, audits and packs the tool itself.

## License

MIT, see [LICENSE](LICENSE).
