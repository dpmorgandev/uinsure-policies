using System.Reflection;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A7: controllers are sealed, inherit ControllerBase, are named *Controller, and keep Domain types
// out of their public signatures. ControllerBase is matched by name, so this project needs no
// compile-time reference to ASP.NET Core.
public sealed class ControllerTests
{
    private const string ControllerBaseName = "Microsoft.AspNetCore.Mvc.ControllerBase";

    private static IEnumerable<Type> ApiTypes =>
        SourceAssemblies.Api.GetTypes().Where(type => !SourceAssemblies.IsCompilerGenerated(type));

    private static IEnumerable<Type> Controllers => ApiTypes.Where(InheritsControllerBase);

    [Fact]
    public void Controllers_are_sealed()
    {
        Assert.Empty(Controllers.Where(type => !type.IsSealed).Select(type => type.FullName));
    }

    [Fact]
    public void Controllers_are_named_with_the_Controller_suffix()
    {
        Assert.Empty(Controllers
            .Where(type => !type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Select(type => type.FullName));
    }

    [Fact]
    public void Types_named_Controller_inherit_ControllerBase()
    {
        Assert.Empty(ApiTypes
            .Where(type => type.Name.EndsWith("Controller", StringComparison.Ordinal) && !InheritsControllerBase(type))
            .Select(type => type.FullName));
    }

    [Fact]
    public void Controller_public_signatures_use_no_Domain_type()
    {
        const BindingFlags publicDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var leaks = Controllers.SelectMany(controller =>
            controller.GetMethods(publicDeclared).Cast<MethodBase>()
                .Concat(controller.GetConstructors(publicDeclared))
                .SelectMany(member => SignatureTypes(member)
                    .Where(type => type.Assembly == SourceAssemblies.Domain)
                    .Select(type => $"{controller.Name}.{member.Name} uses {type.FullName}")));

        Assert.Empty(leaks);
    }

    private static bool InheritsControllerBase(Type type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.FullName == ControllerBaseName)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> SignatureTypes(MethodBase member)
    {
        var returnTypes = member is MethodInfo method ? new[] { method.ReturnType } : [];
        return returnTypes
            .Concat(member.GetParameters().Select(parameter => parameter.ParameterType))
            .SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return inner;
            }
        }
    }
}
