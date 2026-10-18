using System.Reflection;
using Uinsure.Policies.Architecture.Tests.Support;
using Uinsure.Policies.Services;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A15: the layers meet at interfaces. The controller depends on IPolicyService, never on a concrete Services
// class; PolicyService implements IPolicyService and takes its references from IReferenceGenerator, which
// ReferenceGenerator implements.
public sealed class LayerSeamTests
{
    private const string ControllerBaseName = "Microsoft.AspNetCore.Mvc.ControllerBase";

    private static IEnumerable<Type> Controllers =>
        SourceAssemblies.Api.GetTypes().Where(type => !SourceAssemblies.IsCompilerGenerated(type) && InheritsControllerBase(type));

    [Fact]
    public void PoliciesController_takes_IPolicyService()
    {
        var controller = Assert.Single(Controllers, type => type.Name == "PoliciesController");

        Assert.All(controller.GetConstructors(), constructor =>
            Assert.Contains(typeof(IPolicyService), constructor.GetParameters().Select(parameter => parameter.ParameterType)));
    }

    [Fact]
    public void Controllers_take_no_concrete_Services_class()
    {
        var concrete = Controllers
            .SelectMany(controller => controller.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => parameter.ParameterType.Assembly == SourceAssemblies.Services && !parameter.ParameterType.IsInterface)
                .Select(parameter => $"{controller.Name} takes {parameter.ParameterType.FullName}"));

        Assert.Empty(concrete);
    }

    [Fact]
    public void PolicyService_implements_IPolicyService()
    {
        Assert.Contains(typeof(IPolicyService), typeof(PolicyService).GetInterfaces());
    }

    [Fact]
    public void PolicyService_takes_IReferenceGenerator()
    {
        var constructor = Assert.Single(typeof(PolicyService).GetConstructors());

        Assert.Contains(typeof(IReferenceGenerator), constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void ReferenceGenerator_implements_IReferenceGenerator()
    {
        Assert.Contains(typeof(IReferenceGenerator), typeof(ReferenceGenerator).GetInterfaces());
    }

    // Finding A1/I4: IReferenceGenerator is the only way to a reference, so ReferenceGenerator offers no
    // public static entry point that code could call around the injected seam.
    [Fact]
    public void ReferenceGenerator_declares_no_public_static_method()
    {
        var statics = typeof(ReferenceGenerator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);

        Assert.Empty(statics);
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
}
