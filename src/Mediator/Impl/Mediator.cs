using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;

namespace Mediator.Impl;

public class Mediator(IServiceProvider provider) : IMediator
{
    private static readonly ConcurrentDictionary<Type, Type> VoidHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> ResultHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> NotificationHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new();

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
        await (Task)method.Invoke(handler, [request, cancellationToken])!;
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
        return await (Task<TResult>)method.Invoke(handler, [request, cancellationToken])!;
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
}

