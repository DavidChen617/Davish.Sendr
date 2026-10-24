using Davish.JustBdd;
using Davish.Sendr;
using Sendr.TestFeatures;

namespace Sendr.Tests;

using Step = Func<CancellationToken, Task>;

/// <summary>
/// notification-runner: NotificationGroupRunner called directly. Generated publishers call it from code compiled
/// into the application, so its semantics are a contract on their own, apart from either publisher.
/// </summary>
[Feature<NotificationRunnerFeature>]
public class NotificationRunnerTests
{
    private const string RunSequence = "RunSequenceAsync";
    private const string RunParallel = "RunParallelAsync";
    private const string RunBoth = "RunBothAsync";

    public static TheoryData<string> Methods => new() { RunSequence, RunParallel, RunBoth };

    /// <summary>Runs <paramref name="steps"/> with the named method; RunBothAsync puts the first step in the Sequence group and the rest in the Parallel group.</summary>
    private static Task Run(string method, IReadOnlyList<Step> steps) => method switch
    {
        RunSequence => NotificationGroupRunner.RunSequenceAsync(steps, CancellationToken.None),
        RunParallel => NotificationGroupRunner.RunParallelAsync(steps, CancellationToken.None),
        _ => NotificationGroupRunner.RunBothAsync(steps.Take(1).ToArray(), steps.Skip(1).ToArray(), CancellationToken.None),
    };

    // ---- Snapshots ----

    public static TheoryData<string, string> SequentialMutations => new()
    {
        { "a step appended to the caller's List", RunSequence },
        { "a step appended to the caller's List", "RunBothAsync (Sequence group)" },
        { "an element of the caller's array replaced", RunSequence },
        { "an element of the caller's array replaced", "RunBothAsync (Sequence group)" },
    };

    [Theory]
    [MemberData(nameof(SequentialMutations))]
    public async Task GivenStepMutatesCallerStepList_WhenRunSequentially_ThenOriginalStepsRunInOriginalOrder(string mutation, string call)
    {
        await new Scenario()
            .Given<RunnerContext>($"Three recording steps, where the first one, while running, causes {mutation}", ctx => ctx.ThreeStepsMutatingCaller(mutation))
            .When($"They are run with {call}", ctx => call == RunSequence
                ? NotificationGroupRunner.RunSequenceAsync(ctx.CallerSteps, CancellationToken.None)
                : NotificationGroupRunner.RunBothAsync(ctx.CallerSteps, Array.Empty<Step>(), CancellationToken.None))
            .Then("Exactly the original steps ran, in their original order")
                .Equal("1, 2, 3 and never the replacement", ["1", "2", "3"], ctx => ctx.Log)
            .RunAsync();
    }

    public static TheoryData<string, string> ParallelMutations => new()
    {
        { "a step appended to the caller's List", RunParallel },
        { "a step appended to the caller's List", "RunBothAsync (Parallel group)" },
        { "an element of the caller's array replaced", RunParallel },
        { "an element of the caller's array replaced", "RunBothAsync (Parallel group)" },
    };

    [Theory]
    [MemberData(nameof(ParallelMutations))]
    public async Task GivenStepMutatesCallerStepList_WhenRunInParallel_ThenExactlyTheOriginalStepsRun(string mutation, string call)
    {
        await new Scenario()
            .Given<RunnerContext>($"Three recording steps, where the first one, when started, causes {mutation}", ctx => ctx.ThreeStepsMutatingCaller(mutation))
            .When($"They are run with {call}", ctx => call == RunParallel
                ? NotificationGroupRunner.RunParallelAsync(ctx.CallerSteps, CancellationToken.None)
                : NotificationGroupRunner.RunBothAsync(Array.Empty<Step>(), ctx.CallerSteps, CancellationToken.None))
            .Then("Exactly the original steps ran, each once")
                .Equal("1, 2 and 3 in any order, and never the replacement", ["1", "2", "3"], ctx => ctx.Log.Order())
            .RunAsync();
    }

    [Fact]
    public async Task GivenSequenceStepClearsParallelListSynchronously_WhenRunBothAsync_ThenParallelStepsStillRun()
    {
        await new Scenario()
            .Given<RunnerContext>("A Sequence step that synchronously clears the Parallel group's List, and one recording Parallel step", ctx => { })
            .When("RunBothAsync is called and awaited", ctx =>
            {
                var parallel = new List<Step> { ctx.Recording("parallel") };
                Step clearParallel = _ =>
                {
                    parallel.Clear();
                    return Task.CompletedTask;
                };
                return NotificationGroupRunner.RunBothAsync([clearParallel], parallel, CancellationToken.None);
            })
            .Then("The Parallel step still ran")
                .Equal("The Parallel step recorded itself", ["parallel"], ctx => ctx.Log)
            .RunAsync();
    }

    // ---- Exception collection ----

    public static TheoryData<int, string> OwnAggregateCases
    {
        get
        {
            var data = new TheoryData<int, string>();
            foreach (var inner in new[] { 0, 1, 2 })
                foreach (var method in new[] { RunSequence, RunParallel, RunBoth })
                    data.Add(inner, method);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OwnAggregateCases))]
    public async Task GivenStepFailsWithItsOwnAggregateException_WhenRun_ThenSameInstanceIsRethrownWithoutFlattening(int innerCount, string method)
    {
        var inners = Enumerable.Range(1, innerCount).Select(i => new InvalidOperationException($"inner {i}")).ToArray();
        var own = new AggregateException(inners);

        await new Scenario()
            .Given($"A single step whose task fails with its own AggregateException holding {innerCount} inner exception(s)", () => new Step[] { _ => Task.FromException(own) })
            .When($"It is run with {method}", steps => Run(method, steps))
            .ThenThrows<AggregateException>("The step's AggregateException reaches the caller")
                .True("It is the very same instance", t => ReferenceEquals(t.Exception, own))
                .Equal("Its inner exceptions are untouched", inners.Cast<Exception>().ToArray(), t => t.Exception.InnerExceptions)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task GivenStepTaskFaultedWithSeveralExceptions_WhenRun_ThenEveryExceptionIsAggregated(string method)
    {
        var a = new InvalidOperationException("a");
        var b = new InvalidOperationException("b");

        await new Scenario()
            .Given("A single step whose task is faulted with two exceptions, a and b", () =>
            {
                var faulted = new TaskCompletionSource();
                faulted.SetException([a, b]);
                return new Step[] { _ => faulted.Task };
            })
            .When($"It is run with {method}", steps => Run(method, steps))
            .ThenThrows<AggregateException>("Both exceptions reach the caller")
                .Equal("The inner exceptions are exactly a and b", new HashSet<Exception> { a, b }, t => t.Exception.InnerExceptions.ToHashSet(),
                    (expected, actual) => expected.SetEquals(actual))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task GivenCanceledStepAndAnotherFailingStep_WhenRun_ThenCancellationIsRecordedAsOneOperationCanceledException(string method)
    {
        var failure = new InvalidOperationException("failed");

        await new Scenario()
            .Given("Two steps: one returns a canceled task, the other fails", () => new Step[]
            {
                _ => Task.FromCanceled(new CancellationToken(canceled: true)),
                _ => Task.FromException(failure),
            })
            .When($"They are run with {method}", steps => Run(method, steps))
            .ThenThrows<AggregateException>("Both outcomes reach the caller together")
                .Equal("Two inner exceptions", 2, t => t.Exception.InnerExceptions.Count)
                .Single("Exactly one of them records the cancellation", t => t.Exception.InnerExceptions, e => e is OperationCanceledException)
                .Contains("The other is the step's own failure", failure, t => t.Exception.InnerExceptions)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task GivenNoSteps_WhenRun_ThenCompletesWithoutException(string method)
    {
        await new Scenario()
            .Given("No steps at all", () => Array.Empty<Step>())
            .When($"They are run with {method}", steps => Run(method, steps))
            .Then("The run completes", _ => { })
            .RunAsync();
    }

    // ---- Execution semantics ----

    [Fact]
    public async Task GivenSequenceStepsWithOneFailing_WhenRunSequenceAsync_ThenStepsRunOneAtATimeInOrderAndAllRun()
    {
        var failure = new InvalidOperationException("second");

        await new Scenario()
            .Given<RunnerContext>("Three steps recording their start and end, where the second fails", ctx => { })
            .When("They are run with RunSequenceAsync", ctx => NotificationGroupRunner.RunSequenceAsync(
                [ctx.StartEnd("1"), ctx.StartEnd("2", failure), ctx.StartEnd("3")], CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The second step's failure reaches the caller")
                .True("It is the step's own exception", t => ReferenceEquals(t.Exception, failure))
                .Equal("Each step finished before the next started, and the third still ran",
                    ["1 start", "1 end", "2 start", "3 start", "3 end"], t => t.State.Log)
            .RunAsync();
    }

    [Fact]
    public async Task GivenParallelStepsWhereOneThrowsWhileStarting_WhenRunParallelAsync_ThenEveryStepIsStartedBeforeAnyIsAwaited()
    {
        var failure = new InvalidOperationException("first");

        await new Scenario()
            .Given<RunnerContext>("Three steps: the first throws as soon as it is invoked, the other two wait (with a timeout) for each other to start", ctx => { })
            .When("They are run with RunParallelAsync", ctx => NotificationGroupRunner.RunParallelAsync(
                [_ => throw failure, ctx.WaitingFor("x", "y"), ctx.WaitingFor("y", "x")], CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The first step's failure reaches the caller")
                .True("It is the step's own exception", t => ReferenceEquals(t.Exception, failure))
                .Equal("The other two saw each other start and both finished", ["x end", "y end"], t => t.State.Log.Order())
            .RunAsync();
    }

    [Fact]
    public async Task GivenSequenceAndParallelGroups_WhenRunBothAsync_ThenGroupsRunConcurrently()
    {
        await new Scenario()
            .Given<RunnerContext>("A Sequence step that waits (with a timeout) for the Parallel step to start", ctx => { })
            .When("RunBothAsync is called and awaited", ctx => NotificationGroupRunner.RunBothAsync(
                [ctx.WaitingFor("sequence", "parallel")], [ctx.WaitingFor("parallel", null)], CancellationToken.None))
            .Then("The Sequence step saw the Parallel step start, and both finished")
                .Equal("Both steps ended", ["parallel end", "sequence end"], ctx => ctx.Log.Order())
            .RunAsync();
    }

    public static TheoryData<int, string> FailureCounts
    {
        get
        {
            var data = new TheoryData<int, string>();
            foreach (var count in new[] { 0, 1, 2 })
                foreach (var method in new[] { RunSequence, RunParallel, RunBoth })
                    data.Add(count, method);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FailureCounts))]
    public async Task GivenSomeFailingSteps_WhenRun_ThenNothingTheExceptionItselfOrAnAggregateIsThrown(int failing, string method)
    {
        var failures = Enumerable.Range(1, failing).Select(i => new InvalidOperationException($"failure {i}")).ToArray();
        var steps = new Step[]
        {
            _ => failing >= 1 ? Task.FromException(failures[0]) : Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => failing >= 2 ? Task.FromException(failures[1]) : Task.CompletedTask,
        };

        await new Scenario()
            .Given($"Three steps, {failing} of which fail", () => steps)
            .When($"They are run with {method}, capturing what is thrown", async s =>
            {
                try
                {
                    await Run(method, s);
                    return (Exception?)null;
                }
                catch (Exception thrown)
                {
                    return thrown;
                }
            })
            .Then("What reaches the caller depends on how many steps failed", thrown =>
            {
                switch (failing)
                {
                    case 0:
                        Assert.Null(thrown);
                        break;
                    case 1:
                        Assert.Same(failures[0], thrown);
                        break;
                    default:
                        var aggregate = Assert.IsType<AggregateException>(thrown);
                        Assert.Equal(failures.Cast<Exception>().ToHashSet(), aggregate.InnerExceptions.ToHashSet());
                        break;
                }
            })
            .RunAsync();
    }

    /// <summary>A log of what the steps did, plus helpers that build recording steps.</summary>
    public sealed class RunnerContext
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, TaskCompletionSource> _started = [];

        public List<string> Log { get; } = [];

        /// <summary>The caller's own collection, which the first step mutates.</summary>
        public IReadOnlyList<Step> CallerSteps { get; private set; } = [];

        private void Write(string entry)
        {
            lock (_gate)
                Log.Add(entry);
        }

        private TaskCompletionSource Signal(string name)
        {
            lock (_gate)
                return _started.TryGetValue(name, out var signal)
                    ? signal
                    : _started[name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Step Recording(string name) => _ =>
        {
            Write(name);
            return Task.CompletedTask;
        };

        public Step StartEnd(string name, Exception? failure = null) => async _ =>
        {
            Write($"{name} start");
            await Task.Yield();
            if (failure is not null)
                throw failure;
            Write($"{name} end");
        };

        /// <summary>Signals its own start, waits for <paramref name="other"/> to start (if any), then ends.</summary>
        public Step WaitingFor(string name, string? other) => async _ =>
        {
            Signal(name).TrySetResult();
            if (other is not null)
                await Signal(other).Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Yield();
            Write($"{name} end");
        };

        public void ThreeStepsMutatingCaller(string mutation)
        {
            var replacement = Recording("replacement");
            if (mutation.Contains("List"))
            {
                var list = new List<Step>();
                list.Add(_ =>
                {
                    list.Add(replacement);
                    Write("1");
                    return Task.CompletedTask;
                });
                list.Add(Recording("2"));
                list.Add(Recording("3"));
                CallerSteps = list;
            }
            else
            {
                var array = new Step[3];
                array[0] = _ =>
                {
                    array[1] = replacement;
                    Write("1");
                    return Task.CompletedTask;
                };
                array[1] = Recording("2");
                array[2] = Recording("3");
                CallerSteps = array;
            }
        }
    }
}
