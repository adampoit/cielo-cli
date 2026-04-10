{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  version,
}:
buildDotnetModule {
  pname = "cielo";
  inherit version;

  src = lib.fileset.toSource {
    root = ../.;
    fileset = lib.fileset.unions [
      ../src/cielo/cielo.csproj
      ../src/cielo/CieloCommandApp.cs
      ../src/cielo/Program.cs
      ../src/cielo/Configuration
      ../src/cielo/Models
      ../src/cielo/Properties
      ../src/cielo/Services
    ];
  };

  projectFile = "src/cielo/cielo.csproj";
  nugetDeps = ./nuget-deps.json;
  executables = ["cielo"];

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;

  meta = {
    description = "CLI and monitor daemon for Cielo Breez cloud control";
    homepage = "https://github.com/adampoit/cielo";
    license = lib.licenses.mit;
    mainProgram = "cielo";
    platforms = lib.platforms.unix;
  };
}
