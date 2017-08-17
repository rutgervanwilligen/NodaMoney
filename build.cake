#tool "xunit.runner.console"
#tool "GitVersion.CommandLine"
#addin "Cake.Figlet"

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var rootDir = Directory("./");
var artifactsDir = Directory("./artifacts/");
var srcProjects = GetFiles("./src/**/*.csproj");
var testProjects = GetFiles("./tests/**/*.csproj");

Setup(() =>
{
   Information(Figlet("NodaMoney"));
});

Task("Clean")
.Does(() =>
{
    CleanDirectory(artifactsDir);

    foreach(var path in srcProjects.Select(csproj => csproj.GetDirectory()))
    {
        CleanDirectory(path + "/bin/" + configuration);
        CleanDirectory(path + "/obj/" + configuration);
    }

    foreach(var path in testProjects.Select(csproj => csproj.GetDirectory()))
    {
        CleanDirectory(path + "/bin/" + configuration);
        CleanDirectory(path + "/obj/" + configuration);
    }
});

Task("Restore")
.Does(() =>
{
    DotNetCoreRestore(rootDir);
});

Task("Version").
Does(() =>
{
    var versionInfo = GitVersion();
    var buildVersion = EnvironmentVariable("APPVEYOR_BUILD_NUMBER") ?? "0";
    var assemblyVersion =  versionInfo.Major + ".0.0.0"; // Minor and Patch versions should work with base Major version
	var fileVersion = versionInfo.MajorMinorPatch + "." + buildVersion;
	var informationalVersion = versionInfo.FullSemVer;
	var nuGetVersion = versionInfo.NuGetVersion;

    Information("BuildVersion: " + buildVersion);
    Information("AssemblyVersion: " + assemblyVersion);
    Information("FileVersion: " + fileVersion);
    Information("InformationalVersion: " + informationalVersion);
    Information("NuGetVersion: " + nuGetVersion);
	
    if (AppVeyor.IsRunningOnAppVeyor)
    {
        AppVeyor.UpdateBuildVersion(informationalVersion + ".build." + buildVersion);
    }	
	
    Information("Update Directory.build.props");
    var file = File(rootDir.ToString() + "src/Directory.build.props");
    XmlPoke(file, "/Project/PropertyGroup/Version", nuGetVersion);
    XmlPoke(file, "/Project/PropertyGroup/AssemblyVersion", assemblyVersion);
    XmlPoke(file, "/Project/PropertyGroup/FileVersion", fileVersion);
    XmlPoke(file, "/Project/PropertyGroup/InformationalVersion", informationalVersion);
});

Task("Build")
.IsDependentOn("Clean")
.IsDependentOn("Restore")
.IsDependentOn("Version")
.Does(() =>
{
    DotNetCoreBuild(rootDir, new DotNetCoreBuildSettings { Configuration = configuration });
});

Task("Test")
.IsDependentOn("Clean")
.IsDependentOn("Restore")
.IsDependentOn("Build")
.Does(() =>
{
    foreach(var csproj in testProjects)
    {
        DotNetCoreTest(csproj.ToString(), new DotNetCoreTestSettings { Configuration = configuration });
    }
});

Task("Package")
.IsDependentOn("Build")
.IsDependentOn("Test")
.Does(() =>
{
    var packSettings = new DotNetCorePackSettings
    {
        Configuration = configuration,
        OutputDirectory = artifactsDir,
        NoBuild = true
    };
 
    foreach(var csproj in srcProjects)
    {
        DotNetCorePack(csproj.ToString(), packSettings);
    }
 });

Task("Upload-AppVeyor-Artifacts")
.WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
.WithCriteria(() => !AppVeyor.Environment.PullRequest.IsPullRequest)
.IsDependentOn("Package")
.Does(() =>
{
    foreach(var package in GetFiles(artifactsDir.ToString() + "/*.nupkg"))
    {
        AppVeyor.UploadArtifact(package);
    }
});

 Task("Publish-NuGet")
 .WithCriteria(() => HasEnvironmentVariable("NUGET_API_KEY"))
 .WithCriteria(() => AppVeyor.Environment.Repository.Branch == "master")
 .IsDependentOn("Package")
 .Does(() =>
 {	
    DotNetCoreNuGetPush("*.nupkg", new DotNetCoreNuGetPushSettings
    {
        WorkingDirectory = artifactsDir,
        Source = "https://www.nuget.org/",
        ApiKey = EnvironmentVariable("NUGET_API_KEY")
    });
});
 
Task("Default")
.IsDependentOn("Package");

Task("AppVeyor")
.IsDependentOn("Package")
.IsDependentOn("Upload-AppVeyor-Artifacts")
.IsDependentOn("Publish-NuGet");

RunTarget(target);