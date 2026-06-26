using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SP.Core.Serialization;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Command
{
    public abstract class CommandHandlerBase<TContext, TProtocol> : ICommandHandler
        where TContext : ICommandContext
        where TProtocol : class, IProtocolData, new()
    {
        private static class ProtocolPool<T> where T : class, IProtocolData, new()
        {
            private const int LocalCapacity = 512;

            [ThreadStatic] private static LocalStack _localStack;
            private static readonly ConcurrentQueue<T> _globalQueue = new ConcurrentQueue<T>();

            private class LocalStack
            {
                public T[] Items;
                public int Count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static T Rent()
            {
                if (_localStack != null && _localStack.Count > 0)
                {
                    return _localStack.Items[--_localStack.Count];
                }
            
                return _globalQueue.TryDequeue(out var instance) ? instance : new T();
            }
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Return(T instance)
            {
                if (instance == null) return;

                NetObject<T>.Reset(instance);

                _localStack ??= new LocalStack { Items = new T[LocalCapacity] };
            
                if (_localStack.Count < LocalCapacity)
                {
                    _localStack.Items[_localStack.Count++] = instance;
                }
                else
                {
                    _globalQueue.Enqueue(instance);
                }
            }
        }
        
        public string Name { get; }
        public Type ContextType => typeof(TContext);

        protected CommandHandlerBase()
        {
            Name = GetType().Name;
        }

        public long Execute(ICommandContext context, IMessage message)
        {
            if (!(context is TContext ctx)) return 0;
            
            var protocol = ProtocolPool<TProtocol>.Rent();

            try
            {
                message.Deserialize(protocol, ctx.Encryptor, ctx.Compressor);
            }
            catch (Exception ex)
            {
                context.Logger.Error(ex, "Command '{0}' deserialize filed.", Name);
                
                ProtocolPool<TProtocol>.Return(protocol);
                return 0;
            }
            
            var start = Stopwatch.GetTimestamp();
            
            try
            {
                ExecuteCommand(ctx, protocol);
            }
            catch (Exception e)
            {
                context.Logger.Error(e, "Command '{0}' execution failed in {0}.", Name);
            }
            finally
            {
                ProtocolPool<TProtocol>.Return(protocol);
            }

            var deltaTicks = Stopwatch.GetTimestamp() - start;
            return deltaTicks * 1000 / Stopwatch.Frequency;
        }

        protected abstract void ExecuteCommand(TContext context, TProtocol protocol);
    }
}
