using Davish.Sendr;

namespace Microsoft.Extensions.DependencyInjection;

public static class Dependency
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSendr()
        {
            return services.AddScoped<ISender, Sender>();
        }
        
        public IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(Action<RequestHandlerOptions>? configure = null)
            where TRequest : IRequest<TResponse>
            where THandler : class, IRequestHandler<TRequest, TResponse>
        {
            HandlersCache.GetOrCreate(typeof(TRequest), typeof(TResponse));

            services.AddTransient<THandler>();

            var options = new RequestHandlerOptions();
            configure?.Invoke(options);

            services.AddTransient<IRequestHandler<TRequest, TResponse>>(pv =>
            {
                IRequestHandler<TRequest, TResponse> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var closed = decoratorType.MakeGenericType(typeof(TRequest), typeof(TResponse));

                    if (!typeof(IRequestDecorator<TRequest, TResponse>).IsAssignableFrom(closed))
                        throw new InvalidOperationException(
                            $"{decoratorType.Name} Not a valid decorator, must implement IRequestDecorator<TRequest, TResponse>.");

                    handler = (IRequestHandler<TRequest, TResponse>)ActivatorUtilities.CreateInstance(pv, closed, handler);
                }

                return handler;
            });

            return services;
        }
    }
}

public sealed class RequestHandlerOptions
{
    internal List<Type> Decorators { get; } = [];

    public RequestHandlerOptions UseDecorators(params Type[] decoratorTypeDefinitions)
    {
        Decorators.AddRange(decoratorTypeDefinitions);
        return this;
    }
}
