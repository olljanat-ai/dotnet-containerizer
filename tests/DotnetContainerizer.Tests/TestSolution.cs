namespace DotnetContainerizer.Tests;

/// <summary>Builds throwaway solution folders on disk so the scanner can be tested end to end.</summary>
internal sealed class TestSolution : IDisposable
{
    public TestSolution()
    {
        Root = Path.Combine(Path.GetTempPath(), "containerizer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public TestSolution AddSolution(string name, params string[] projectPaths)
    {
        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00",
        };

        foreach (var project in projectPaths)
        {
            var projectName = Path.GetFileNameWithoutExtension(project);
            lines.Add($"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{projectName}\", \"{project.Replace('/', '\\')}\", \"{{{Guid.NewGuid()}}}\"");
            lines.Add("EndProject");
        }

        Write($"{name}.sln", string.Join(Environment.NewLine, lines));
        return this;
    }

    public TestSolution AddProject(string relativePath, string content)
    {
        Write(relativePath, content);
        return this;
    }

    public TestSolution AddFile(string relativePath, string content)
    {
        Write(relativePath, content);
        return this;
    }

    public string PathTo(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string ReadGenerated(string relativePath) => File.ReadAllText(PathTo(relativePath));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Leaving a temp folder behind must not fail a test run.
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = PathTo(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public static string WebProject(string targetFramework = "net8.0") => $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>{targetFramework}</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    public static string WorkerProject(string targetFramework = "net8.0") => $"""
        <Project Sdk="Microsoft.NET.Sdk.Worker">
          <PropertyGroup>
            <TargetFramework>{targetFramework}</TargetFramework>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """;

    public static string LibraryProject(string targetFramework = "net8.0") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{targetFramework}</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    public static string TestProject(string targetFramework = "net8.0") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{targetFramework}</TargetFramework>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
            <PackageReference Include="xunit" Version="2.5.3" />
          </ItemGroup>
        </Project>
        """;

    public static string ConsoleProject(string targetFramework = "net8.0", string? assemblyName = null, string? projectReference = null) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{targetFramework}</TargetFramework>
            <OutputType>Exe</OutputType>
            {(assemblyName is null ? string.Empty : $"<AssemblyName>{assemblyName}</AssemblyName>")}
          </PropertyGroup>
          {(projectReference is null ? string.Empty : $"""
          <ItemGroup>
            <ProjectReference Include="{projectReference}" />
          </ItemGroup>
          """)}
        </Project>
        """;

    public const string HttpsLaunchSettings = """
        {
          "profiles": {
            "https": {
              "commandName": "Project",
              "applicationUrl": "https://localhost:7185;http://localhost:5185"
            }
          }
        }
        """;

    public const string HttpOnlyLaunchSettings = """
        {
          "profiles": {
            "http": {
              "commandName": "Project",
              "applicationUrl": "http://localhost:5185"
            }
          }
        }
        """;
}
