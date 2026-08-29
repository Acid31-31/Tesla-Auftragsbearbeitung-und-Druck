using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class ResourceMonitor : IResourceMonitor
    {
        private readonly Process _process = Process.GetCurrentProcess();

        public bool PreferGpu => DetectGpu();

        public int RecommendedWorkerCount
        {
            get
            {
                var cores = Environment.ProcessorCount;
                return Math.Max(1, PreferGpu ? cores : Math.Max(1, cores - 1));
            }
        }

        public ResourceSnapshot GetSnapshot()
        {
            _process.Refresh();
            return new ResourceSnapshot
            {
                WorkingSetMb = _process.WorkingSet64 / (1024 * 1024),
                ProcessorCount = Environment.ProcessorCount,
                CpuUsagePercent = EstimateCpuUsage(),
                GpuAvailable = PreferGpu,
                GpuName = PreferGpu ? ReadGpuName() : string.Empty
            };
        }

        private static double EstimateCpuUsage()
        {
            try
            {
                using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue();
                    System.Threading.Thread.Sleep(100);
                    return Math.Round(cpuCounter.NextValue(), 1);
                }
            }
            catch
            {
                return 0d;
            }
        }

        private static bool DetectGpu()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (key == null)
                        return false;

                    return key.GetSubKeyNames()
                        .Select(name => key.OpenSubKey(name))
                        .Where(sub => sub != null)
                        .Any(sub =>
                        {
                            var desc = (sub.GetValue("DriverDesc") as string) ?? string.Empty;
                            return desc.IndexOf("nvidia", StringComparison.OrdinalIgnoreCase) >= 0
                                   || desc.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0
                                   || desc.IndexOf("radeon", StringComparison.OrdinalIgnoreCase) >= 0
                                   || desc.IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ReadGpuName()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (key == null)
                        return string.Empty;

                    foreach (var name in key.GetSubKeyNames())
                    {
                        using (var sub = key.OpenSubKey(name))
                        {
                            var desc = sub?.GetValue("DriverDesc") as string;
                            if (!string.IsNullOrWhiteSpace(desc))
                                return desc;
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
