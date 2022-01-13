using System;
using System.Linq;
using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;


[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
[GitHubActionsAttribute("Build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "master", "dev" },
    OnPullRequestBranches = new[] { "master", "dev" },
    CacheKeyFiles = new[] { "global.json", "SchemaGenerator/*.csproj" },
    InvokedTargets = new[] { nameof(Compile) },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    PublishArtifacts = true)
]

[GitHubActionsAttribute("Tests",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "master", "dev" },
    OnPullRequestBranches = new[] { "master", "dev" },
    CacheKeyFiles = new[] { "global.json", "SchemaGenerator/*.csproj" },
    InvokedTargets = new[] { nameof(Test) },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    PublishArtifacts = true)
]


[GitHubActionsAttribute("PublishBeta",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "beta_branch" },
    CacheKeyFiles = new[] { "global.json", "SchemaGenerator/*.csproj" },
    InvokedTargets = new[] { nameof(PushBeta) },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    PublishArtifacts = true,
    ImportSecrets = new[] { "NUGET_API_KEY", "GITHUB_TOKEN" })]

[GitHubActionsAttribute("Publish",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "main" },
    CacheKeyFiles = new[] { "global.json", "SchemaGenerator/*.csproj" },
    InvokedTargets = new[] { nameof(Push) },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    PublishArtifacts = true,
    ImportSecrets = new[] { "NUGET_API_KEY", "GITHUB_TOKEN" })
]
partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Pack);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "net6.0")] readonly GitVersion GitVersion;

    [Parameter] string NugetApiUrl = "https://api.nuget.org/v3/index.json";
    [Parameter] string GithubSource = "https://nuget.pkg.github.com/OWNER/index.json";

    //[Parameter] string NugetApiKey = Environment.GetEnvironmentVariable("SHARP_PULSAR_NUGET_API_KEY");
    [Parameter] [Secret] string NuGetApiKey;

    [Parameter("GitHub Build Number", Name = "BUILD_NUMBER")]
    readonly string BuildNumber;

    [Parameter("GitHub Access Token for Packages", Name = "GH_API_KEY")]
    readonly string GitHubApiKey; 
    AbsolutePath TestsDirectory => RootDirectory;
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath TestSourceDirectory => RootDirectory / "AvroSchemaGenerator.Tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {

        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GetVersion())
                .SetFileVersion(GetVersion())
                //.SetInformationalVersion("1.9.0")
                .EnableNoRestore());
        });
    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var projectName = "AvroSchemaGenerator.Tests";
            var project = Solution.GetProjects("*.Tests").First();
            Information($"Running tests from {projectName}");
            var fw = "";
            if (!IsRunningOnWindows)
            {
                fw = "net6.0";
            }
            Information($"Running for {projectName} ({fw}) ...");
            DotNetTest(c => c
                   .SetProjectFile(project)
                   .SetConfiguration(Configuration.ToString())
                   .SetFramework("net6.0")                   
                   //.SetDiagnosticsFile(TestsDirectory)
                   //.SetLogger("trx")
                   .SetVerbosity(verbosity: DotNetVerbosity.Normal)
                   .EnableNoBuild());
        });

    Target Pack => _ => _
      .DependsOn(Test)
      .Executes(() =>
      {
          var project = Solution.GetProject("AvroSchemaGenerator");
          DotNetPack(s => s
              .SetProject(project)
              .SetConfiguration(Configuration)
              .EnableNoBuild()
              
              .EnableNoRestore()
              .SetAssemblyVersion(GetVersion())
              .SetVersion(GetVersion())
              .SetPackageReleaseNotes(GetReleasenote())
              .SetDescription("Generate Avro Schema with support for RECURSIVE SCHEMA")
              .SetPackageTags("Avro", "Schema Generator")
              .AddAuthors("Ebere Abanonu (@mestical)")
              .SetPackageProjectUrl("https://github.com/eaba/AvroSchemaGenerator")
              .SetOutputDirectory(ArtifactsDirectory / "nuget")); ;

      });
    Target PackBeta => _ => _
      .DependsOn(Test)
      .Executes(() =>
      {
          var project = Solution.GetProject("AvroSchemaGenerator");
          DotNetPack(s => s
              .SetProject(project)
              .SetConfiguration(Configuration)
              .EnableNoBuild()
              .EnableNoRestore()
              .SetAssemblyVersion($"{GetVersion()}-beta")
              .SetVersion($"{GetVersion()}-beta")
              .SetPackageReleaseNotes(GetReleasenote())
              .SetDescription("Generate Avro Schema with support for RECURSIVE SCHEMA")
              .SetPackageTags("Avro", "Schema Generator")
              .AddAuthors("Ebere Abanonu (@mestical)")
              .SetPackageProjectUrl("https://github.com/eaba/AvroSchemaGenerator")
              .SetOutputDirectory(ArtifactsDirectory / "nuget")); ;

      });
    Target Push => _ => _
      .DependsOn(Pack)
      .Requires(() => NugetApiUrl)
      .Requires(() => !NuGetApiKey.IsNullOrEmpty())
      .Requires(() => !GitHubApiKey.IsNullOrEmpty())
      //.Requires(() => !BuildNumber.IsNullOrEmpty())
      .Requires(() => Configuration.Equals(Configuration.Release))
      .Executes(() =>
      {
          
          GlobFiles(ArtifactsDirectory / "nuget", "*.nupkg")
              .Where(x => !x.EndsWith("symbols.nupkg"))
              .ForEach(x =>
              {
                  Assert.NotNullOrEmpty(x);
                  DotNetNuGetPush(s => s
                      .SetTargetPath(x)
                      .SetSource(NugetApiUrl)
                      .SetApiKey(NuGetApiKey)
                  );

                  /*DotNetNuGetPush(s => s
                      .SetApiKey(GitHubApiKey)
                      .SetSymbolApiKey(GitHubApiKey)
                      .SetTargetPath(x)
                      .SetSource(GithubSource)
                      .SetSymbolSource(GithubSource));*/
              });
      });
    Target PushBeta => _ => _
      .DependsOn(PackBeta)
      .Requires(() => NugetApiUrl)
      .Requires(() => !NuGetApiKey.IsNullOrEmpty())
      .Requires(() => !GitHubApiKey.IsNullOrEmpty())
      //.Requires(() => !BuildNumber.IsNullOrEmpty())
      .Requires(() => Configuration.Equals(Configuration.Release))
      .Executes(() =>
      {
          GlobFiles(ArtifactsDirectory / "nuget", "*.nupkg")
              .Where(x => !x.EndsWith("symbols.nupkg"))
              .ForEach(x =>
              {
                  Assert.NotNullOrEmpty(x);
                  DotNetNuGetPush(s => s
                      .SetTargetPath(x)
                      .SetSource(NugetApiUrl)
                      .SetApiKey(NuGetApiKey)
                  );
              });
      });

    static void Information(string info)
    {
        Serilog.Log.Information(info);  
    }
    static string GetVersion()
    {
        return "2.5.1";
    }
    static string GetReleasenote()
    {
        return "Added README.md with Nuget package";
    }
}
