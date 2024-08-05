

# Commands parameters

## Update if NuGet has updates

--directory  

Code will only change if there are new Nuget versions available

## Synchronize NugetPackages.cs with Json and check for latest packages

--directory  --force-code-updates 

Code will change even if there are not new Nuget versions. Maybe you have added new packages or locked package versions

## Synchronize NugetPackages.cs with Json only 

--directory  --force-code-updates true --skip-nuget-check true --suppress-versioning true
