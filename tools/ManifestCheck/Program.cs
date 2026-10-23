using System.IO;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    // 用装机版 Toolkit 自己的 ValidateManifest 校验清单，避免“照抄规则”与真实实现漂移。
    private static int Main(string[] args)
    {
        var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "dist/tbt.memory.cleaner");
        var install = Environment.GetEnvironmentVariable("TBT_INSTALL_DIR") ?? @"C:\Program Files\ThinkBook Toolkit";
        var appDll = Path.Combine(install, "ThinkBookToolkit.dll");
        var apiDll = Path.Combine(install, "ThinkBookToolkit.PluginApi.dll");

        try
        {
            var api = Assembly.LoadFrom(apiDll);
            var app = Assembly.LoadFrom(appDll);

            var manifestType = api.GetType("ThinkBookToolkit.PluginApi.PluginManifest", throwOnError: true)!;
            var managerType = app.GetType("ThinkBookToolkit.ToolkitPluginManager", throwOnError: true)!;
            var method = managerType.GetMethod("ValidateManifest",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (method is null)
            {
                Console.WriteLine("FAIL: ValidateManifest not found");
                return 1;
            }

            var json = File.ReadAllText(Path.Combine(directory, "plugin.json"));
            var manifest = JsonSerializer.Deserialize(json, manifestType);
            if (manifest is null)
            {
                Console.WriteLine("FAIL: manifest did not deserialize");
                return 1;
            }

            method.Invoke(null, new object?[] { manifest, directory });
            Console.WriteLine("PASS: installed Toolkit accepted the manifest");
            return 0;
        }
        catch (TargetInvocationException ex)
        {
            Console.WriteLine("FAIL: " + ex.InnerException?.GetType().Name + ": " + ex.InnerException?.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}
