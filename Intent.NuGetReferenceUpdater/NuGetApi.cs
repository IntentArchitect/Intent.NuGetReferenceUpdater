using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.NuGetReferenceUpdater
{
    internal class NuGetApi
    {

        private static List<NuGetFramework> _frameworks = new List<NuGetFramework>
            {
            /*  NuGetFramework.Parse(".NETFramework,Version=v4.5"),
                NuGetFramework.Parse(".NETFramework,Version=v4.6"),
                NuGetFramework.Parse(".NETFramework,Version=v4.7"),
                NuGetFramework.Parse(".NETFramework,Version=v4.8"),*/
                NuGetFramework.Parse(".NETCoreApp,Version=v3.1"),
                NuGetFramework.Parse(".NETCoreApp,Version=v5.0"),
                NuGetFramework.Parse(".NETCoreApp,Version=v6.0"),
                NuGetFramework.Parse(".NETCoreApp,Version=v7.0"),
                NuGetFramework.Parse(".NETCoreApp,Version=v8.0")
            };

        public static async Task<List<NugetVersionInfo>> GetLatestVersionsForFrameworksAsync(string packageName)
        {
            Console.WriteLine($"Fetching Package Details : {packageName}");
            var providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());

            var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
            var sourceRepository = new SourceRepository(packageSource, providers);

            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>();
            var searchMetadata = await packageMetadataResource.GetMetadataAsync(packageName, includePrerelease: false, includeUnlisted: false, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None);

            var versionsForFrameworks = new List<NugetVersionInfo>();

            foreach (var framework in _frameworks)
            {
                var latestPackage = searchMetadata?
                    .Where(p => p.DependencySets.Any(d => d.TargetFramework == framework))
                    .OrderByDescending(p => p.Identity.Version)
                    .FirstOrDefault();

                if (latestPackage != null)
                {
                    versionsForFrameworks.Add(new NugetVersionInfo(framework.Version, latestPackage.Identity.Version));
                }
            }

            return versionsForFrameworks;
        }

        internal class NugetVersionInfo
        {
            public NugetVersionInfo(Version frameworkVersion, NuGetVersion packageVersion)
            {
                FrameworkVersion = frameworkVersion;
                PackageVersion = packageVersion;
            }

            public Version FrameworkVersion { get; }
            public NuGetVersion PackageVersion { get; }

            public string GetFrameworkVersion()
            {
                //This lines up with how we handling this in Intent Common
                return $"{FrameworkVersion.Major:D}.{FrameworkVersion.Minor:D}";
            }

        }
    }
}
