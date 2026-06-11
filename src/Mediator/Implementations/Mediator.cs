using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;

namespace Mediator.Implementations;

public class Mediator(IServiceProvider provider) : IMediator
{
    private static readonly ConcurrentDictionary<Type, Type> VoidHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> ResultHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> NotificationHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> BehaviorMethodCache = new();

    public async Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();
        var handlerType = VoidHandlerTypeCache.GetOrAdd(
            requestType,
            t => typeof(IRequestHandler<>).MakeGenericType(t));

        var handler = provider.GetService(handlerType);
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for request of type {request.GetType().Name}.");
        }

        var method = MethodCache.GetOrAdd(handlerType, t => t.GetMethod(nameof(IRequestHandler<>.HandleAsync))!);

        RequestHandlerDelegate handlerDelegate =
            () => (Task)method.Invoke(handler, [request, cancellationToken])!;

        var pipeline = BuildVoidPipeline(requestType, request, handlerDelegate, cancellationToken);
        await pipeline();
    }

    public async Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();
        var handlerType = ResultHandlerTypeCache.GetOrAdd(
            requestType,
            t => typeof(IRequestHandler<,>).MakeGenericType(t, typeof(TResult)));

        var handler = provider.GetService(handlerType);
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for request of type {request.GetType().Name}.");
        }

        var method = MethodCache.GetOrAdd(handlerType, t => t.GetMethod(nameof(IRequestHandler<,>.HandleAsync))!);
        RequestHandlerDelegate<TResult> handlerDelegate =
            () => (Task<TResult>)method.Invoke(handler, [request, cancellationToken])!;

        var pipeline = BuildResultPipeline(requestType, typeof(TResult), request, handlerDelegate, cancellationToken);
        return await pipeline();
    }
    
    public async Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        var notificationType = notification.GetType();
        var handlerType = NotificationHandlerTypeCache.GetOrAdd(
            notificationType,
            t => typeof(INotificationHandler<>).MakeGenericType(t));

        var handlers = provider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler == null) continue;

            var method = MethodCache.GetOrAdd(handler.GetType(), t => t.GetMethod(nameof(INotificationHandler<>.HandleAsync))!);
            await (Task)method.Invoke(handler, [notification, cancellationToken])!;
        }
    }
    
    private RequestHandlerDelegate BuildVoidPipeline(Type requestType, object request, RequestHandlerDelegate handler,
        CancellationToken cancellationToken)
    {
        var behaviorType = typeof(IPipelineBehavior<>).MakeGenericType(requestType);
        var behaviors = provider.GetServices(behaviorType).Reverse();

        return behaviors.Aggregate(handler, (next, behavior) =>
        {
            var b = behavior!;
            var handleMethod = BehaviorMethodCache.GetOrAdd(behaviorType, t => t.GetMethod("HandleAsync")!);

            return () => (Task)handleMethod.Invoke(b, [request, next, cancellationToken])!;
        });
    }

    private RequestHandlerDelegate<TResponse> BuildResultPipeline<TResponse>(Type requestType, Type responseType, object request,
        RequestHandlerDelegate<TResponse> handler, CancellationToken cancellationToken)
    {
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);
        var behaviors = provider.GetServices(behaviorType).Reverse();

        return behaviors.Aggregate(handler, (next, behavior) =>
        {
            var b = behavior!;
            var handleMethod = BehaviorMethodCache.GetOrAdd(behaviorType, t => t.GetMethod("HandleAsync")!);

            return () => (Task<TResponse>)handleMethod.Invoke(b, [request, next, cancellationToken])!;
        });
    }
}

