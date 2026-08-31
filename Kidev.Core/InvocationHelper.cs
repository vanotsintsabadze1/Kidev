using System;
using System.Reflection;
using System.Text.Json;
using Kidev.Core.Data;

namespace Kidev.Core;

internal sealed class JobInvocation
{
    internal Type ServiceType { get; }

    internal Type[] ParameterTypes { get; }

    internal MethodInfo Method { get; }

    internal JsonElement[] Arguments { get; }

    internal JobInvocation(Type serviceType, Type[] parameterTypes, MethodInfo method, JsonElement[] arguments)
    {
        ServiceType = serviceType;
        ParameterTypes = parameterTypes;
        Method = method;
        Arguments = arguments;
    }

}

internal static class InvocationHelper
{
    internal static JobInvocation Create(JobDefinition jobDefinition)
    {
        Type serviceType = Type.GetType(jobDefinition.ServiceTypeName, throwOnError: true)
            ?? throw new InvalidOperationException($"The service type '{jobDefinition.ServiceTypeName}' could not be loaded.");
        string[] parameterTypeNames = JsonSerializer.Deserialize<string[]>(jobDefinition.MethodParameterTypesJson)
            ?? throw new InvalidOperationException($"The parameter types for job '{jobDefinition.RegistrationKey}' could not be deserialized.");
        Type[] parameterTypes = Array.ConvertAll(parameterTypeNames, parameterTypeName =>
            Type.GetType(parameterTypeName, throwOnError: true)
            ?? throw new InvalidOperationException($"The parameter type '{parameterTypeName}' could not be loaded."));
        MethodInfo method = serviceType.GetMethod(jobDefinition.MethodName, parameterTypes)
            ?? throw new InvalidOperationException($"The method '{jobDefinition.MethodName}' could not be found on service type '{serviceType}'.");
        JsonElement[] arguments = JsonSerializer.Deserialize<JsonElement[]>(jobDefinition.ArgumentsJson)
            ?? throw new InvalidOperationException($"The arguments for job '{jobDefinition.RegistrationKey}' could not be deserialized.");

        return new JobInvocation(serviceType, parameterTypes, method, arguments);
    }
}
