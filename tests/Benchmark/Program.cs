using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

BenchmarkRunner.Run(typeof(Program).Assembly);

[MemoryDiagnoser]
public class DispatchBenchmark
{
    private readonly DirectCall_Query _directQuery = new();
    private readonly DirectCall_QueryHandler _directQueryHandler = new();
    private readonly DirectCall_Command _directCommand = new();
    private readonly DirectCall_CommandHandler _directCommandHandler = new();
    private readonly SenderCall_Query _senderQuery = new();
    private readonly SenderCall_Command _senderCommand = new();
    private ISender _sender = null!;
    private ISender _generatedSender = null!;

    [GlobalSetup]
    public void SetUp()
    {
        _sender = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SenderCall_Query, SenderCall_QueryDto, SenderCall_QueryHandler>()
            .AddRequestHandler<SenderCall_Command, SenderCall_CommandHandler>()
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        _generatedSender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();
    }

    [Benchmark(Baseline = true)]
    public Task DirectCall_Query() => _directQueryHandler.HandleAsync(_directQuery, default);

    [Benchmark]
    public Task SenderCall_Query() => _sender.SendAsync(_senderQuery, default);

    [Benchmark]
    public Task GeneratedSenderCall_Query() => _generatedSender.SendAsync(_senderQuery, default);

    [Benchmark]
    public Task DirectCall_Command() => _directCommandHandler.HandleAsync(_directCommand, default);

    [Benchmark]
    public Task SenderCall_Command() => _sender.SendAsync(_senderCommand, default);

    [Benchmark]
    public Task GeneratedSenderCall_Command() => _generatedSender.SendAsync(_senderCommand, default);
}

[MemoryDiagnoser]
public class DecoratorBenchmark
{
    private readonly SenderCall_Query _query = new();
    private readonly SenderCall_Command _command = new();
    private readonly GenBench_OneDecoratorQuery _genOneDecoratorQuery = new();
    private readonly GenBench_TwoDecoratorQuery _genTwoDecoratorQuery = new();
    private readonly GenBench_OneDecoratorCommand _genOneDecoratorCommand = new();
    private readonly GenBench_TwoDecoratorCommand _genTwoDecoratorCommand = new();
    private ISender _senderOneDecorator = null!;
    private ISender _senderTwoDecorators = null!;
    private ISender _generatedSender = null!;

    [GlobalSetup]
    public void SetUp()
    {
        _senderOneDecorator = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SenderCall_Query, SenderCall_QueryDto, SenderCall_QueryHandler>(x =>
                x.Decorator.With<SenderCall_LoggingDecorator>())
            .AddRequestHandler<SenderCall_Command, SenderCall_CommandHandler>(x =>
                x.Decorator.With<SenderCall_LoggingDecorator>())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        _senderTwoDecorators = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SenderCall_Query, SenderCall_QueryDto, SenderCall_QueryHandler>(x => x.Decorator
                .With<SenderCall_TransactionDecorator>()
                .With<SenderCall_LoggingDecorator>())
            .AddRequestHandler<SenderCall_Command, SenderCall_CommandHandler>(x => x.Decorator
                .With<SenderCall_TransactionDecorator>()
                .With<SenderCall_LoggingDecorator>())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // [DecorateWith<...>] bakes the decorator set into the handler class, so the generated
        // one-/two-decorator comparison needs its own request/handler pairs (see below).
        _generatedSender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();
    }

    [Benchmark]
    public Task SenderCall_Query_OneDecorator() => _senderOneDecorator.SendAsync(_query, default);

    [Benchmark]
    public Task GeneratedSenderCall_Query_OneDecorator() => _generatedSender.SendAsync(_genOneDecoratorQuery, default);

    [Benchmark]
    public Task SenderCall_Query_TwoDecorators() => _senderTwoDecorators.SendAsync(_query, default);

    [Benchmark]
    public Task GeneratedSenderCall_Query_TwoDecorators() => _generatedSender.SendAsync(_genTwoDecoratorQuery, default);

    [Benchmark]
    public Task SenderCall_Command_OneDecorator() => _senderOneDecorator.SendAsync(_command, default);

    [Benchmark]
    public Task GeneratedSenderCall_Command_OneDecorator() => _generatedSender.SendAsync(_genOneDecoratorCommand, default);

    [Benchmark]
    public Task SenderCall_Command_TwoDecorators() => _senderTwoDecorators.SendAsync(_command, default);

    [Benchmark]
    public Task GeneratedSenderCall_Command_TwoDecorators() => _generatedSender.SendAsync(_genTwoDecoratorCommand, default);
}

[MemoryDiagnoser]
public class StreamBenchmark
{
    private readonly DirectCall_StreamQuery _directQuery = new();
    private readonly DirectCall_StreamHandler _directHandler = new();
    private readonly SenderCall_StreamQuery _senderQuery = new();
    private readonly GenBench_StreamQuery _genDecoratedQuery = new();
    private IStreamSender _streamSender = null!;
    private IStreamSender _decoratedStreamSender = null!;
    private IStreamSender _generatedStreamSender = null!;

    [GlobalSetup]
    public void SetUp()
    {
        _streamSender = new ServiceCollection()
            .AddSendr()
            .AddStreamRequestHandler<SenderCall_StreamQuery, SenderCall_StreamDto, SenderCall_StreamHandler>()
            .BuildServiceProvider()
            .GetRequiredService<IStreamSender>();

        _decoratedStreamSender = new ServiceCollection()
            .AddSendr()
            .AddStreamRequestHandler<SenderCall_StreamQuery, SenderCall_StreamDto, SenderCall_StreamHandler>(x =>
                x.Decorator.With<SenderCall_StreamLoggingDecorator>())
            .BuildServiceProvider()
            .GetRequiredService<IStreamSender>();

        _generatedStreamSender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<IStreamSender>();
    }

    [Benchmark(Baseline = true)]
    public async Task DirectCall_Stream_Enumerate()
    {
        await foreach (var _ in _directHandler.HandleAsync(_directQuery, default))
        {
        }
    }

    [Benchmark]
    public IAsyncEnumerable<SenderCall_StreamDto> SenderCall_Stream_Create()
    {
        return _streamSender.SendStream(_senderQuery, default);
    }

    [Benchmark]
    public IAsyncEnumerable<SenderCall_StreamDto> GeneratedSenderCall_Stream_Create()
    {
        return _generatedStreamSender.SendStream(_senderQuery, default);
    }

    [Benchmark]
    public async Task SenderCall_Stream_Enumerate()
    {
        await foreach (var _ in _streamSender.SendStream(_senderQuery, default))
        {
        }
    }

    [Benchmark]
    public async Task GeneratedSenderCall_Stream_Enumerate()
    {
        await foreach (var _ in _generatedStreamSender.SendStream(_senderQuery, default))
        {
        }
    }

    [Benchmark]
    public async Task SenderCall_Stream_OneDecorator_Enumerate()
    {
        await foreach (var _ in _decoratedStreamSender.SendStream(_senderQuery, default))
        {
        }
    }

    [Benchmark]
    public async Task GeneratedSenderCall_Stream_OneDecorator_Enumerate()
    {
        await foreach (var _ in _generatedStreamSender.SendStream(_genDecoratedQuery, default))
        {
        }
    }
}

public sealed record DirectCall_Query;

public sealed record DirectCall_QueryDto;

public sealed class DirectCall_QueryHandler
{
    public Task<DirectCall_QueryDto> HandleAsync(
        DirectCall_Query query,
        CancellationToken cancellationToken) =>
        Task.FromResult(new DirectCall_QueryDto());
}

public sealed record DirectCall_Command;

public sealed class DirectCall_CommandHandler
{
    public Task HandleAsync(
        DirectCall_Command command,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed record DirectCall_StreamQuery;

public sealed record DirectCall_StreamDto;

public sealed class DirectCall_StreamHandler
{
    public async IAsyncEnumerable<DirectCall_StreamDto> HandleAsync(
        DirectCall_StreamQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new DirectCall_StreamDto();
        await Task.CompletedTask;
    }
}

public sealed record SenderCall_Query : IRequest<SenderCall_QueryDto>;

public sealed record SenderCall_QueryDto;

public sealed class SenderCall_QueryHandler : IRequestHandler<SenderCall_Query, SenderCall_QueryDto>
{
    public Task<SenderCall_QueryDto> HandleAsync(
        SenderCall_Query request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SenderCall_QueryDto());
}

public sealed record SenderCall_Command : IRequest;

public sealed class SenderCall_CommandHandler : IRequestHandler<SenderCall_Command>
{
    public Task HandleAsync(
        SenderCall_Command request,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed record SenderCall_StreamQuery : IStreamRequest<SenderCall_StreamDto>;

public sealed record SenderCall_StreamDto;

public sealed class SenderCall_StreamHandler : IStreamRequestHandler<SenderCall_StreamQuery, SenderCall_StreamDto>
{
    public async IAsyncEnumerable<SenderCall_StreamDto> HandleAsync(
        SenderCall_StreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new SenderCall_StreamDto();
        await Task.CompletedTask;
    }
}

public sealed class SenderCall_LoggingDecorator : IRequestDecorator, IRequestDecorator.WithResponse
{
    public Task HandleAsync<TRequest>(
        TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken)
        where TRequest : IRequest =>
        next();

    public Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse> =>
        next();
}

public sealed class SenderCall_TransactionDecorator : IRequestDecorator, IRequestDecorator.WithResponse
{
    public Task HandleAsync<TRequest>(
        TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken)
        where TRequest : IRequest =>
        next();

    public Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse> =>
        next();
}

public sealed class SenderCall_StreamLoggingDecorator : IStreamRequestDecorator
{
    public async IAsyncEnumerable<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        await foreach (var response in next().WithCancellation(cancellationToken))
            yield return response;
    }
}

// [DecorateWith<...>] bakes its decorator set into the handler class, so the generated-sender
// decorator benchmarks need their own request/handler pairs (one per decorator count) rather
// than reusing SenderCall_Query/SenderCall_Command with different registration-time configs.

public sealed record GenBench_OneDecoratorQuery : IRequest<SenderCall_QueryDto>;

[DecorateWith<SenderCall_LoggingDecorator>]
public sealed class GenBench_OneDecoratorQueryHandler : IRequestHandler<GenBench_OneDecoratorQuery, SenderCall_QueryDto>
{
    public Task<SenderCall_QueryDto> HandleAsync(GenBench_OneDecoratorQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(new SenderCall_QueryDto());
}

public sealed record GenBench_TwoDecoratorQuery : IRequest<SenderCall_QueryDto>;

[DecorateWith<SenderCall_TransactionDecorator, SenderCall_LoggingDecorator>]
public sealed class GenBench_TwoDecoratorQueryHandler : IRequestHandler<GenBench_TwoDecoratorQuery, SenderCall_QueryDto>
{
    public Task<SenderCall_QueryDto> HandleAsync(GenBench_TwoDecoratorQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(new SenderCall_QueryDto());
}

public sealed record GenBench_OneDecoratorCommand : IRequest;

[DecorateWith<SenderCall_LoggingDecorator>]
public sealed class GenBench_OneDecoratorCommandHandler : IRequestHandler<GenBench_OneDecoratorCommand>
{
    public Task HandleAsync(GenBench_OneDecoratorCommand request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed record GenBench_TwoDecoratorCommand : IRequest;

[DecorateWith<SenderCall_TransactionDecorator, SenderCall_LoggingDecorator>]
public sealed class GenBench_TwoDecoratorCommandHandler : IRequestHandler<GenBench_TwoDecoratorCommand>
{
    public Task HandleAsync(GenBench_TwoDecoratorCommand request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed record GenBench_StreamQuery : IStreamRequest<SenderCall_StreamDto>;

[DecorateWith<SenderCall_StreamLoggingDecorator>]
public sealed class GenBench_StreamHandler : IStreamRequestHandler<GenBench_StreamQuery, SenderCall_StreamDto>
{
    public async IAsyncEnumerable<SenderCall_StreamDto> HandleAsync(
        GenBench_StreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new SenderCall_StreamDto();
        await Task.CompletedTask;
    }
}
