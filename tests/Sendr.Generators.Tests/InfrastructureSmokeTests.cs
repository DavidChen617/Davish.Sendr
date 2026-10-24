using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.Generators.Tests;

// Keeps the test run non-empty (MTP exits with code 8 when no test runs) and proves the generator runs as
// an analyzer in this project, until test-source-generation lands real coverage; delete it then.
public class InfrastructureSmokeTests
{
    [Fact]
    public async Task GivenUseGenerators_WhenSendQuery_ThenGeneratedDispatchReachesHandler()
    {
        await new Scenario()
            .Given("A sender built with UseGenerators", () =>
                new ServiceCollection()
                    .AddSendr(o => o.UseGenerators())
                    .BuildServiceProvider()
                    .GetRequiredService<ISender>())
            .When("A smoke query is sent", sender => sender.SendAsync(new SmokeQuery(20), CancellationToken.None))
            .Then("The handler's answer is returned")
                .Equal("The handler doubled the input", 40, answer => answer)
            .RunAsync();
    }
}

public sealed record SmokeQuery(int Value) : IQuery<int>;

public sealed class SmokeQueryHandler : IQueryHandler<SmokeQuery, int>
{
    public Task<int> HandleAsync(SmokeQuery query, CancellationToken cancellationToken)
        => Task.FromResult(query.Value * 2);
}
