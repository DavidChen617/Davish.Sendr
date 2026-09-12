using Davish.Sendr;

namespace Notification.Tests;

public class NotificationGroupRunnerTests
{
    [Fact]
    public async Task Sequence_runner_is_stable_when_caller_mutates_source_list_during_a_step()
    {
        // Given
        var ran = new List<string>();
        List<Func<CancellationToken, Task>> steps = null!;
        steps =
        [
            ct =>
            {
                ran.Add("first");
                steps.Add(ct2 =>
                {
                    ran.Add("appended");
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            },
            ct =>
            {
                ran.Add("second");
                return Task.CompletedTask;
            },
        ];

        // When
        await NotificationGroupRunner.RunSequenceAsync(steps, default);

        // Then
        Assert.Equal(["first", "second"], ran);
    }

    [Fact]
    public async Task Parallel_runner_is_stable_when_caller_mutates_source_list_during_a_step()
    {
        // Given
        var ran = new List<string>();
        List<Func<CancellationToken, Task>> steps = null!;
        steps =
        [
            ct =>
            {
                ran.Add("first");
                steps.Add(ct2 =>
                {
                    ran.Add("appended");
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            },
            ct =>
            {
                ran.Add("second");
                return Task.CompletedTask;
            },
        ];

        // When
        await NotificationGroupRunner.RunParallelAsync(steps, default);

        // Then
        Assert.Equal(2, ran.Count);
        Assert.Contains("first", ran);
        Assert.Contains("second", ran);
    }
}
