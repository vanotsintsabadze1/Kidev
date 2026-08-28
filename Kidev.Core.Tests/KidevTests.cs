using System;
using FluentAssertions;
using Xunit;

namespace Kidev.Core.Tests;

/// <summary>
/// Tests build-time Kidev job registration.
/// </summary>
public sealed class KidevTests
{
    /// <summary>
    /// Verifies constant arguments and minute schedules are retained in the registration catalog.
    /// </summary>
    [Fact]
    public void RunWithConstantArgumentsRetainsJobMetadata()
    {
        var kidev = new Kidev();

        kidev.Run<IJobService>("send-digest", service => service.SendDigest("weekly", 25))
            .EveryMinute(5);

        KidevRegistrationCatalog catalog = kidev.Freeze();
        Data.JobDefinition jobDefinition = catalog.JobDefinitions.Should().ContainSingle().Subject;

        jobDefinition.RegistrationKey.Should().Be("send-digest");
        jobDefinition.MethodName.Should().Be(nameof(IJobService.SendDigest));
        jobDefinition.CronExpression.Should().Be("*/5 * * * *");
        jobDefinition.ArgumentsJson.Should().Be("[\"weekly\",25]");
        jobDefinition.MethodParameterTypesJson.Should().Contain("System.String");
        jobDefinition.MethodParameterTypesJson.Should().Contain("System.Int32");
    }

    /// <summary>
    /// Verifies captured values are rejected until a broader serialization contract exists.
    /// </summary>
    [Fact]
    public void RunWithCapturedArgumentThrowsArgumentException()
    {
        var kidev = new Kidev();
        string frequency = "weekly";

        Action action = () => kidev.Run<IJobService>("send-digest", service => service.SendDigest(frequency, 25));

        action.Should().Throw<ArgumentException>()
            .WithMessage("Only constant method arguments are supported.*");
    }

    /// <summary>
    /// Defines a service shape used to inspect a registered method call.
    /// </summary>
    public interface IJobService
    {
        /// <summary>
        /// Sends a digest with a supplied frequency and maximum item count.
        /// </summary>
        /// <param name="frequency">The digest frequency.</param>
        /// <param name="maximumItems">The maximum number of items to include.</param>
        void SendDigest(string frequency, int maximumItems);
    }
}
