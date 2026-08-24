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

## How projects are classified

The scanner reads every `*.sln`, `*.slnx`, `*.csproj`, `*.fsproj` and `*.vbproj` below the given folder
(`bin`, `obj`, `.git`, `node_modules` and similar folders are ignored) and inspects each project file:

| Project | Result |
| --- | --- |
| `Microsoft.NET.Sdk.Web`, or a reference to `Microsoft.AspNetCore.App` | `mcr.microsoft.com/dotnet/aspnet` image, ports exposed |
| `Microsoft.NET.Sdk.Worker`, or `OutputType` of `Exe` | `mcr.microsoft.com/dotnet/runtime` image |
| Class library, test project, Blazor WebAssembly, WinExe, .NET Framework | skipped, with the reason printed |

Ports follow the Visual Studio defaults: .NET 8 and newer run as `$APP_UID` and listen on `8080`
(plus `8081` when the project has an HTTPS launch profile), older versions listen on `80` and `443`.
Multi targeted projects are containerized for their newest target framework.

The build context is the solution folder when exactly one solution file is found, otherwise the scanned
folder. Project references are copied into the image before `dotnet restore` runs, so the restore stays
in its own cached layer.

## Options

| Option | Description |
| --- | --- |
| `-p`, `--path <folder>` | Folder to scan. Default: current folder |
| `--os <linux\|windows>` | Container operating system. Default: `linux` |
| `--include-tests` | Also generate assets for test projects |
| `-f`, `--force` | Overwrite files that already exist |
| `--dry-run` | Report what would be written without touching the disk |
| `-l`, `--list` | Only list the discovered projects |
| `-v`, `--verbose` | Print why projects were skipped |
| `-h`, `--help` | Show the help text |
| `--version` | Show the tool version |

Existing files are never overwritten without `--force`, so hand tuned Dockerfiles survive a re-run.

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
```

## License

MIT, see [LICENSE](LICENSE).
