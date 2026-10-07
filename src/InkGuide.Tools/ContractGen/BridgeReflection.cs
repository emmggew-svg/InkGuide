using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace InkGuide.Tools.ContractGen;

/// <summary>
/// Reflects C# contract DTOs out of the InkGuide.Bridge assembly into
/// TypeScript interfaces. Only public, non-compiler-generated types in the
/// InkGuide.Bridge namespace with at least one public instance property are
/// emitted; everything else falls back to the baseline catalog.
/// </summary>
internal sealed class BridgeReflection
{
    private readonly ILogger<BridgeReflection> _logger;

    public BridgeReflection(ILogger<BridgeReflection> logger)
    {
        _logger = logger;
    }

    public ReflectedContracts Reflect(string bridgeDllPath)
    {
        ArgumentNullException.ThrowIfNull(bridgeDllPath);

        var dtos = new List<ReflectedDto>();
        var enums = new List<ReflectedEnum>();

        if (!File.Exists(bridgeDllPath))
        {
            ToolsLog.BridgeMissing(_logger, bridgeDllPath);
            return new ReflectedContracts(dtos, enums, Loaded: false);
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(bridgeDllPath);
        }
        catch (Exception ex) when (ex is IOException || ex is BadImageFormatException || ex is FileLoadException)
        {
            ToolsLog.StepFailed(_logger, ex, "contract-reflect", "Could not load " + bridgeDllPath);
            return new ReflectedContracts(dtos, enums, Loaded: false);
        }

        foreach (Type type in assembly.GetTypes())
        {
            if (!type.IsPublic || type.Namespace is null || !string.Equals(type.Namespace, "InkGuide.Bridge.Contracts", StringComparison.Ordinal))
            {
                continue;
            }

            if (type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
            {
                continue;
            }

            // Open generics (PagedResult<T>, Result<T>) have no TS equivalent as
            // concrete interfaces; the UI models pagination locally. Skip them
            // instead of emitting `PagedResult`1` / `T` which is not valid TS.
            if (type.IsGenericTypeDefinition)
            {
                continue;
            }

            // Hand-written in EmitTypeScript: BridgeEnvelope/BridgeErrorDto would
            // collide (TS2717/TS2687) with the manual envelope definitions.
            if (string.Equals(type.Name, "BridgeEnvelope", StringComparison.Ordinal) ||
                string.Equals(type.Name, "BridgeErrorDto", StringComparison.Ordinal))
            {
                continue;
            }

            if (type.IsEnum)
            {
                string[] names = Enum.GetNames(type);
                if (names.Length > 0)
                {
                    enums.Add(new ReflectedEnum(type.Name, names));
                }

                continue;
            }

            if (!type.IsClass && !type.IsValueType)
            {
                continue;
            }

            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod is not null && p.GetMethod.IsPublic)
                .ToArray();
            if (props.Length == 0)
            {
                continue;
            }

            var fields = new List<ReflectedField>();
            foreach (PropertyInfo prop in props)
            {
                fields.Add(new ReflectedField(ToCamelCase(prop.Name), MapType(prop.PropertyType)));
            }

            dtos.Add(new ReflectedDto(type.Name, fields));
        }

        ToolsLog.BridgeReflected(_logger, bridgeDllPath, dtos.Count + enums.Count);
        return new ReflectedContracts(dtos, enums, Loaded: true);
    }

    internal static string MapType(Type type)
    {
        // Generic parameters (T in PagedResult<T>) have no concrete TS shape.
        if (type.IsGenericParameter)
        {
            return "unknown";
        }

        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return MapType(underlying) + " | null";
        }

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
            type == typeof(Uri) || type == typeof(Version))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
            type == typeof(double) || type == typeof(decimal))
        {
            return "number";
        }

        if (type.IsEnum)
        {
            return "string";
        }

        if (type.IsArray)
        {
            Type element = type.GetElementType() ?? typeof(object);
            return "Array<" + MapType(element) + ">";
        }

        if (type.IsGenericType)
        {
            Type generic = type.GetGenericTypeDefinition();
            Type[] genericArgs = type.GetGenericArguments();
            if (generic == typeof(List<>) || generic == typeof(IList<>) || generic == typeof(IReadOnlyList<>) ||
                generic == typeof(IEnumerable<>) || generic == typeof(ICollection<>) ||
                generic == typeof(HashSet<>) || generic == typeof(ISet<>))
            {
                return "Array<" + MapType(genericArgs[0]) + ">";
            }

            if (generic == typeof(Dictionary<,>) || generic == typeof(IDictionary<,>) ||
                generic == typeof(IReadOnlyDictionary<,>))
            {
                return "Record<string, " + MapType(genericArgs[1]) + ">";
            }

            if (generic == typeof(KeyValuePair<,>))
            {
                return "{ key: " + MapType(genericArgs[0]) + "; value: " + MapType(genericArgs[1]) + " }";
            }

            // Any other generic (e.g. PagedResult<T>) has no direct TS interface;
            // the UI decodes these per-method. Never emit backtick names.
            return "unknown";
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return "Array<unknown>";
        }

        if (type.Namespace is not null && type.Namespace.StartsWith("InkGuide.Bridge", StringComparison.Ordinal))
        {
            return type.Name;
        }

        return "unknown";
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

internal sealed record ReflectedContracts(
    IReadOnlyList<ReflectedDto> Dtos,
    IReadOnlyList<ReflectedEnum> Enums,
    bool Loaded);

internal sealed record ReflectedDto(string Name, IReadOnlyList<ReflectedField> Fields);
internal sealed record ReflectedField(string Name, string TsType);
internal sealed record ReflectedEnum(string Name, string[] Values);
