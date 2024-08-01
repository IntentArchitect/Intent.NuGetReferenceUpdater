using Intent.Engine;
using Intent.Modules.Common.VisualStudio;

namespace Intent.NuGetReferenceUpdater
{
    public static class NugetPackages
    {

        public static NugetPackageInfo SerilogAspNetCore(IOutputTarget outputTarget) => new(
            name: "Serilog.AspNetCore",
            version: outputTarget.GetMaxNetAppVersion() switch
            {
                (3, 1) => "6.1.0",
                (5, 0) => "6.1.0",
                (6, 0) => "8.0.2",
                (7, 0) => "8.0.2",
                _ => "8.0.2",
            });

        public static NugetPackageInfo SerilogAspNetCore(IOutputTarget outputTarget) => new(
            name: "serilog.sinks.graylog",
            version: outputTarget.GetMaxNetAppVersion() switch
            {
                (6, 0) => "3.1.1",
                _ => "3.1.1",
            });
    }
}
