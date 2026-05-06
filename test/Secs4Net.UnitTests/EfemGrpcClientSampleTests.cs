using Grpc.Net.Client;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Secs4Net.UnitTests;

public class EfemGrpcClientSampleTests
{
    private readonly ITestOutputHelper _output;

    public EfemGrpcClientSampleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Can_Send_Efem_Requests_To_External_Service()
    {
        var endpoint = Environment.GetEnvironmentVariable("EFEM_GRPC_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _output.WriteLine("Skip external gRPC call sample: env `EFEM_GRPC_ENDPOINT` is not set.");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var channel = GrpcChannel.ForAddress(endpoint);
        var client = new global::SECSGrpcService.EFEM.EFEMClient(channel.CreateCallInvoker());

        var startReply = await client.StartMeasurementAsync(new global::SECSGrpcService.NoParams
        {
        }, cancellationToken: cts.Token);

        var recipeReply = await client.ProcessProgramSelectAsync(new global::SECSGrpcService.RecipeMessage
        {
            PPID = "RECIPE-UT",
            //PPID = "PP-UT-01"
        }, cancellationToken: cts.Token);

        var wafer = new global::SECSGrpcService.WaferMessage { SlotId = "1" };
        var pauseReply = await client.PauseMeasurementAsync(wafer, cancellationToken: cts.Token);
        var resumeReply = await client.ResumeMeasurementAsync(wafer, cancellationToken: cts.Token);
        var stopReply = await client.StopMeasurementAsync(wafer, cancellationToken: cts.Token);

        _output.WriteLine($"StartMeasurement => code={startReply.MessageCode}, message={startReply.Message}");
        _output.WriteLine($"ProcessProgramSelect => code={recipeReply.MessageCode}, message={recipeReply.Message}");
        _output.WriteLine($"PauseMeasurement => code={pauseReply.MessageCode}, message={pauseReply.Message}");
        _output.WriteLine($"ResumeMeasurement => code={resumeReply.MessageCode}, message={resumeReply.Message}");
        _output.WriteLine($"StopMeasurement => code={stopReply.MessageCode}, message={stopReply.Message}");

        Assert.NotNull(startReply);
        Assert.NotNull(recipeReply);
        Assert.NotNull(pauseReply);
        Assert.NotNull(resumeReply);
        Assert.NotNull(stopReply);
    }
}
