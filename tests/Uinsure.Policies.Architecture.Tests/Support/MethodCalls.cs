using System.Reflection;
using System.Reflection.Emit;

namespace Uinsure.Policies.Architecture.Tests.Support;

// The methods a method body refers to (call, callvirt, newobj, ldftn, ldvirtftn), read from its IL with
// reflection only. The IL is walked opcode by opcode, so an operand's bytes are never taken for an opcode.
// Every type in an assembly is read, compiler-generated ones included, so a call from a lambda or an async
// method's state machine counts against the assembly it was written in.
internal static class MethodCalls
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private static readonly HashSet<short> MethodReferences =
    [
        OpCodes.Call.Value, OpCodes.Callvirt.Value, OpCodes.Newobj.Value, OpCodes.Ldftn.Value, OpCodes.Ldvirtftn.Value,
    ];

    // Each (caller, callee) pair in the assembly, the caller named Type.Method.
    public static IEnumerable<(string Caller, MethodBase Callee)> In(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            .SelectMany(method => CalledBy(method).Select(callee => ($"{method.DeclaringType?.FullName}.{method.Name}", callee)));

    public static IEnumerable<MethodBase> CalledBy(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[position++]);
            }

            var opCode = OpCodesByValue[value];
            if (MethodReferences.Contains(opCode.Value) && Resolve(method, BitConverter.ToInt32(il, position)) is { } callee)
            {
                yield return callee;
            }

            position += OperandSize(opCode, il, position);
        }
    }

    private static MethodBase? Resolve(MethodBase method, int token)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        try
        {
            return method.Module.ResolveMethod(token, typeArguments, methodArguments);
        }
        catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException)
        {
            return null;
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
        _ => 4,
    };
}
