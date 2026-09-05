using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Kidev.Core.Data;

namespace Kidev.Core;

/// <summary>
/// Configures recurring Kidev jobs during application setup.
/// </summary>
public sealed class Kidev
{
    private readonly List<JobDefinition> jobDefinitions = [];
    private int workerCount = Math.Min(Environment.ProcessorCount, 4);
    private TimeSpan executionHistoryRetention = TimeSpan.FromDays(14);
    private bool isFrozen;

    /// <summary>
    /// Gets or sets the number of concurrent workers that execute Kidev jobs in this application instance.
    /// </summary>
    public int WorkerCount
    {
        get => workerCount;
        set
        {
            ThrowIfFrozen();

            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Worker count must be at least one.");
            }

            workerCount = value;
        }
    }

    /// <summary>Gets or sets how long completed execution history remains in storage.</summary>
    public TimeSpan ExecutionHistoryRetention
    {
        get => executionHistoryRetention;
        set
        {
            ThrowIfFrozen();

            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Execution history retention must be positive.");
            }

            executionHistoryRetention = value;
        }
    }

    /// <summary>
    /// Registers a service method as a recurring job.
    /// </summary>
    /// <typeparam name="TService">The service type resolved when the job executes.</typeparam>
    /// <param name="registrationKey">The stable unique key for the registered job.</param>
    /// <param name="action">The direct service method call to execute.</param>
    /// <returns>A builder for specifying the recurrence schedule.</returns>
    public KidevJobBuilder<TService> Run<TService>(
        string registrationKey,
        Expression<Action<TService>> action)
    {
        if (string.IsNullOrWhiteSpace(registrationKey))
        {
            throw new ArgumentException("Registration keys cannot be null, empty, or whitespace.", nameof(registrationKey));
        }

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
        ThrowIfFrozen();

        if (registrationKey.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(registrationKey), "Registration keys cannot exceed 256 characters.");
        }

        if (action.Body is not MethodCallExpression methodCall || methodCall.Object != action.Parameters[0])
        {
            throw new ArgumentException("Jobs must be direct instance method calls on the service parameter.", nameof(action));
        }

        ParameterInfo[] parameters = methodCall.Method.GetParameters();
        string[] parameterTypeNames = new string[parameters.Length];
        string[] serializedArguments = new string[parameters.Length];

        for (int index = 0; index < parameters.Length; index++)
        {
            Expression argument = UnwrapConversion(methodCall.Arguments[index]);

            if (argument is not ConstantExpression constant)
            {
                throw new ArgumentException("Only constant method arguments are supported.", nameof(action));
            }

            Type parameterType = parameters[index].ParameterType;
            parameterTypeNames[index] = GetTypeName(parameterType);
            serializedArguments[index] = JsonSerializer.Serialize(constant.Value, parameterType);
        }

        Type serviceType = typeof(TService);
        var jobDefinition = new JobDefinition
        {
            RegistrationKey = registrationKey,
            AssemblyName = serviceType.Assembly.GetName().Name
                ?? throw new InvalidOperationException("The service assembly must have a name."),
            ServiceTypeName = GetTypeName(serviceType),
            MethodName = methodCall.Method.Name,
            MethodParameterTypesJson = JsonSerializer.Serialize(parameterTypeNames),
            ArgumentsJson = $"[{string.Join(",", serializedArguments)}]"
        };

        jobDefinitions.Add(jobDefinition);
        return new KidevJobBuilder<TService>(this, jobDefinition);
    }

    internal KidevRegistrationCatalog Freeze()
    {
        ThrowIfFrozen();

        foreach (JobDefinition jobDefinition in jobDefinitions)
        {
            if (string.IsNullOrEmpty(jobDefinition.CronExpression))
            {
                throw new InvalidOperationException($"The job '{jobDefinition.RegistrationKey}' does not have a schedule.");
            }
        }

        isFrozen = true;
        return new KidevRegistrationCatalog(jobDefinitions, workerCount, executionHistoryRetention);
    }

    internal void SetCronExpression(JobDefinition jobDefinition, string cronExpression)
    {
        ThrowIfFrozen();
        jobDefinition.CronExpression = cronExpression;
    }

    private static Expression UnwrapConversion(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            expression = conversion.Operand;
        }

        return expression;
    }

    private static string GetTypeName(Type type)
    {
        return type.AssemblyQualifiedName
            ?? throw new InvalidOperationException($"The type '{type}' does not have an assembly-qualified name.");
    }

    private void ThrowIfFrozen()
    {
        if (isFrozen)
        {
            throw new InvalidOperationException("Kidev job registration is complete.");
        }
    }
}
