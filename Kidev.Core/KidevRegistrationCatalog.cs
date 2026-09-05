using System;
using System.Collections.Generic;
using Kidev.Core.Data;

namespace Kidev.Core;

internal sealed class KidevRegistrationCatalog
{
    private readonly IReadOnlyList<JobDefinition> jobDefinitions;

    internal IReadOnlyList<JobDefinition> JobDefinitions => jobDefinitions;

    internal int WorkerCount { get; }

    internal KidevRegistrationCatalog(IReadOnlyList<JobDefinition> sourceJobDefinitions, int workerCount)
    {
        var copiedJobDefinitions = new JobDefinition[sourceJobDefinitions.Count];

        for (int index = 0; index < sourceJobDefinitions.Count; index++)
        {
            JobDefinition source = sourceJobDefinitions[index];
            copiedJobDefinitions[index] = new JobDefinition
            {
                RegistrationKey = source.RegistrationKey,
                AssemblyName = source.AssemblyName,
                ServiceTypeName = source.ServiceTypeName,
                MethodName = source.MethodName,
                MethodParameterTypesJson = source.MethodParameterTypesJson,
                ArgumentsJson = source.ArgumentsJson,
                CronExpression = source.CronExpression,
                TimeZoneId = source.TimeZoneId,
                LastExecutedAtUtc = source.LastExecutedAtUtc,
                NextExecutionAtUtc = source.NextExecutionAtUtc,
                IsEnabled = source.IsEnabled
            };
        }

        jobDefinitions = Array.AsReadOnly(copiedJobDefinitions);
        WorkerCount = workerCount;
    }
}
