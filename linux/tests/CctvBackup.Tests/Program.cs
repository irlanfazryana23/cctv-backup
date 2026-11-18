using System.Reflection;
using Xunit;

namespace CctvBackup.Tests;

public static class Program
{
    public static async Task<int> Main()
    {
        int passed = 0, failed = 0;
        var asm = typeof(Program).Assembly;

        foreach (var type in asm.GetExportedTypes().Where(t => t != typeof(Program) && !t.IsAbstract))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var fact = method.GetCustomAttribute<FactAttribute>();
                var theory = method.GetCustomAttribute<TheoryAttribute>();
                if (fact is null && theory is null) continue;

                var inlineDatas = method.GetCustomAttributes<InlineDataAttribute>().ToList();
                var cases = theory is not null && inlineDatas.Count > 0
                    ? inlineDatas.SelectMany(d => d.GetData(method)).ToList()
                    : new List<object?[]> { Array.Empty<object?>() };

                foreach (var rawArgs in cases)
                {
                    var args = rawArgs ?? new object?[] { null };
                    try
                    {
                        var instance = Activator.CreateInstance(type);
                        var result = method.Invoke(instance, args);
                        if (result is Task task)
                            await task;
                        if (instance is IDisposable disp)
                            disp.Dispose();
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
                        Console.Error.WriteLine($"FAIL: {type.Name}.{method.Name}({string.Join(", ", args.Select(a => a?.ToString() ?? "null"))}) -> {inner.GetType().Name}: {inner.Message}");
                    }
                }
            }
        }

        Console.WriteLine($"Test run complete: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}
