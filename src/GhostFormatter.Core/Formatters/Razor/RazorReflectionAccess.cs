using System.Reflection;

namespace GhostFormatter.Core.Formatters.Razor;

/// <summary>
/// Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxNode is declared
/// `internal abstract class SyntaxNode` in the shipped 6.0.36 assembly — confirmed directly
/// from decompiled source. This means the TYPE ITSELF, not just individual members, can
/// never be named in our code: not as a parameter type, not as a local variable's type, not
/// as a pattern-match or cast target. Every node in this formatter is therefore held as
/// plain `object`, and every member access goes through reflection, which invokes members
/// by name at runtime and is unaffected by the declaring type's accessibility.
/// </summary>
internal static class RazorReflectionAccess
{
    private static readonly Dictionary<(Type, string), PropertyInfo?> PropertyCache = [];
    private static readonly Dictionary<(Type, string), MethodInfo?> MethodCache = [];

    private const BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static object? GetProperty(object obj, string propertyName)
    {
        var type = obj.GetType();
        var key = (type, propertyName);
        if (!PropertyCache.TryGetValue(key, out var prop))
        {
            prop = null;
            for (var t = type; t is not null && prop is null; t = t.BaseType)
                prop = t.GetProperty(propertyName, AllInstance | BindingFlags.DeclaredOnly);
            PropertyCache[key] = prop;
        }
        return prop?.GetValue(obj);
    }

    private static object? InvokeMethod(object obj, string methodName)
    {
        var type = obj.GetType();
        var key = (type, methodName);
        if (!MethodCache.TryGetValue(key, out var method))
        {
            method = null;
            for (var t = type; t is not null && method is null; t = t.BaseType)
            {
                method = t.GetMethod(
                    methodName,
                    AllInstance | BindingFlags.DeclaredOnly,
                    Type.EmptyTypes
                );
            }

            MethodCache[key] = method;
        }
        return method?.Invoke(obj, null);
    }

    /// <summary>
    /// node.Kind.ToString() without ever naming SyntaxNode or SyntaxKind. GetProperty
    /// returns the boxed enum value as object; ToString() is inherited from System.Object
    /// and is always callable regardless of the enum's own accessibility.
    /// </summary>
    public static string GetKindName(object node) => GetProperty(node, "Kind")?.ToString() ?? "";

    /// <summary>
    /// node.ToFullString() via reflection. SyntaxNode declares this as a public instance
    /// method, but since the declaring class is internal, writing "node.ToFullString()"
    /// would require a compile-time variable of type SyntaxNode to call it on — exactly
    /// what CS0122 blocks. MethodInfo.Invoke only needs the runtime object and a name.
    /// </summary>
    public static string GetFullString(object node) =>
        InvokeMethod(node, "ToFullString") as string ?? "";

    /// <summary>
    /// Enumerates node.ChildNodes() by duck-typing the foreach contract
    /// (GetEnumerator() → object exposing MoveNext():bool and Current:object) via
    /// reflection, rather than casting the result to any generic or non-generic
    /// IEnumerable — we have no decompiled confirmation of what interfaces the returned
    /// ChildSyntaxList struct implements, only that SyntaxNode.ChildNodes() itself is
    /// public. Duck-typing GetEnumerator/MoveNext/Current is guaranteed correct precisely
    /// because that is the exact shape the C# compiler requires to accept a type in a
    /// foreach statement — and ChildSyntaxList's entire purpose is to be enumerated.
    /// </summary>
    public static IEnumerable<object> GetChildNodes(object node)
    {
        var list = InvokeMethod(node, "ChildNodes");
        if (list is null)
            yield break;

        var enumerator = InvokeMethod(list, "GetEnumerator");
        if (enumerator is null)
            yield break;

        while (InvokeMethod(enumerator, "MoveNext") is true)
        {
            var current = GetProperty(enumerator, "Current");
            if (current is not null)
                yield return current;
        }

        if (enumerator is IDisposable disposable)
            disposable.Dispose();
    }
}
