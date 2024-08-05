using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Intent.NuGetReferenceUpdater.NuGetConfig
{
    public class NuGetDependencyConfig
    {
        public NuGetDependencyConfig()
        {
            Packages = new List<Package>();
        }

        public List<Package> Packages { get; set; }

        [JsonIgnore]
        public string Filename { get; set; }

        [JsonIgnore]
        public bool Changed { get; set; } = false;

        internal void Sort()
        {
            foreach (var package in Packages)
            {
                package.Versions.Sort((v1, v2) => NuGetVersion.Parse(v1.Framework).CompareTo(NuGetVersion.Parse(v2.Framework)));
            }
        }

        internal void Initialize(string filename)
        {
            Filename = filename;
            foreach (var package in Packages)
            {
                package.Parent = this;
            }
        }

    }

    public class PackageVersion
    {
        private string? _version;
        public string Framework { get; set; }
        public string? Version
        {
            get
            {
                return _version;
            }
            set
            {
                OldVersion = _version;
                _version = value;
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Locked { get; set; }
        [JsonIgnore]
        public string? OldVersion { get; set; }
    }

    public class Package
    {
        public Package()
        {
            Versions = new List<PackageVersion>();
        }

        public string Name { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Locked { get; set; }
        public List<PackageVersion> Versions { get; set; }

        [JsonIgnore]
        public NuGetDependencyConfig Parent { get; set; }

        internal void Changed()
        {
            Parent.Changed = true;
            Parent.Sort();

        }
    }
}
