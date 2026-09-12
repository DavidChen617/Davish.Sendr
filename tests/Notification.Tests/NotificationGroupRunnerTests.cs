using Davish.Sendr;

namespace Notification.Tests;

public class NotificationGroupRunnerTests
{
    [Fact]
    public async Task GivenStepAppendsToCallerList_WhenRunSequence_ThenOriginalStepsStillRunInOrder()
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
    public async Task GivenStepAppendsToCallerList_WhenRunParallel_ThenOriginalStepsStillRun()
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

    [Fact]
    public async Task GivenStepReplacesCallerArrayElement_WhenRunSequence_ThenOriginalStepStillRuns()
    {
        // Given
        var ran = new List<string>();
        var steps = new Func<CancellationToken, Task>[2];
        steps[0] = _ =>
        {
            ran.Add("first");
            // Replaces its own not-yet-run neighbor mid-run, the array analogue of the List
            // mutation the other snapshot tests exercise.
            steps[1] = _ =>
            {
                ran.Add("replacement");
                return Task.CompletedTask;
            };
            return Task.CompletedTask;
        };
        steps[1] = _ =>
        {
            ran.Add("original-second");
            return Task.CompletedTask;
        };

        // When
        await NotificationGroupRunner.RunSequenceAsync(steps, default);

        // Then
        Assert.Equal(["first", "original-second"], ran);
    }

    [Fact]
    public async Task GivenStepReplacesCallerArrayElement_WhenRunParallel_ThenOriginalStepStillRuns()
    {
        // Given
        var ran = new List<string>();
        var steps = new Func<CancellationToken, Task>[2];
        steps[0] = _ =>
        {
            ran.Add("first");
            steps[1] = _ =>
            {
                ran.Add("replacement");
                return Task.CompletedTask;
            };
            return Task.CompletedTask;
        };
        steps[1] = _ =>
        {
            ran.Add("original-second");
            return Task.CompletedTask;
        };

        // When
        await NotificationGroupRunner.RunParallelAsync(steps, default);

        // Then
        Assert.Equal(2, ran.Count);
        Assert.Contains("first", ran);
        Assert.Contains("original-second", ran);
        Assert.DoesNotContain("replacement", ran);
    }

    [Fact]
    public async Task GivenHandlerThrowsEmptyAggregateException_WhenRunBoth_ThenSameExceptionRethrown()
    {
        // Given
        var handlerException = new AggregateException("handler-owned");
        Func<CancellationToken, Task> step = _ => Task.FromException(handlerException);

        // When
        var thrown = await Record.ExceptionAsync(
            () => NotificationGroupRunner.RunBothAsync([step], [], default));

        // Then
        Assert.Same(handlerException, thrown);
    }

    [Fact]
    public async Task GivenHandlerThrowsSingleInnerAggregateException_WhenRunBoth_ThenSameExceptionRethrown()
    {
        // Given
        var handlerException = new AggregateException("handler-owned", new InvalidOperationException("boom"));
        Func<CancellationToken, Task> step = _ => Task.FromException(handlerException);

        // When
        var thrown = await Record.ExceptionAsync(
            () => NotificationGroupRunner.RunBothAsync([step], [], default));

        // Then
        Assert.Same(handlerException, thrown);
    }

    [Fact]
    public async Task GivenHandlerThrowsTwoInnerAggregateException_WhenRunBoth_ThenSameExceptionRethrown()
    {
        // Given
        var handlerException = new AggregateException(
            "handler-owned", new InvalidOperationException("first"), new ArgumentException("second"));
        Func<CancellationToken, Task> step = _ => Task.FromException(handlerException);

        // When
        var thrown = await Record.ExceptionAsync(
            () => NotificationGroupRunner.RunBothAsync([step], [], default));

        // Then
        Assert.Same(handlerException, thrown);
    }

    [Fact]
    public async Task GivenStepTaskFaultedWithTwoExceptions_WhenRunSequence_ThenBothExceptionsAggregate()
    {
        // Given
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");
        var tcs = new TaskCompletionSource();
        tcs.SetException([first, second]);
        Func<CancellationToken, Task> step = _ => tcs.Task;

        // When
        var thrown = await Record.ExceptionAsync(
            () => NotificationGroupRunner.RunSequenceAsync([step], default));

        // Then
        var aggregate = Assert.IsType<AggregateException>(thrown);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Contains(first, aggregate.InnerExceptions);
        Assert.Contains(second, aggregate.InnerExceptions);
    }

    [Fact]
    public async Task GivenStepTaskFaultedWithTwoExceptions_WhenRunParallel_ThenBothExceptionsAggregate()
    {
        // Given
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");
        var tcs = new TaskCompletionSource();
        tcs.SetException([first, second]);
        Func<CancellationToken, Task> step = _ => tcs.Task;

        // When
        var thrown = await Record.ExceptionAsync(
            () => NotificationGroupRunner.RunParallelAsync([step], default));

        // Then
        var aggregate = Assert.IsType<AggregateException>(thrown);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Contains(first, aggregate.InnerExceptions);
        Assert.Contains(second, aggregate.InnerExceptions);
    }
}
