using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SP.Core.Serialization;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public abstract class CommandHandlerBase<TContext, TProtocol> : ICommandHandler
        where TContext : ICommandContext
        where TProtocol : class, IProtocolData, new()
    {
        private static class ProtocolPool<T> where T : class, IProtocolData, new()
        {
            private const int LocalCapacity = 512;

            [ThreadStatic] private static T[] _localItems;
            [ThreadStatic] private static int _localCount;
            private static readonly ConcurrentQueue<T> _globalQueue = new ConcurrentQueue<T>();
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static T Rent()
            {
                if (_localCount > 0)
                {
                    return _localItems[--_localCount];
                }
            
                return _globalQueue.TryDequeue(out var instance) ? instance : new T();
            }
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Return(T instance)
            {
                if (instance == null) return;

                NetObject<T>.Reset(instance);
                
                _localItems ??= new T[LocalCapacity];
            
                if (_localCount < LocalCapacity)
                {
                    _localItems[_localCount++] = instance;
                }
                else
                {
                    _globalQueue.Enqueue(instance);
                }
            }
        }
        
        public string Name => GetType().Name;
        public Type ContextType => typeof(TContext);

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
                context.Logger.Error(ex, "Command '{0}' deserialize filed. Error: {1}\nStackTrace: {2}"
                    , Name, ex.Message, ex.StackTrace);
                
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
                context.Logger.Error(e, "Command '{0}' execution failed in {0}. Error: {1}\nStacktrace: {2}", 
                    Name, e.Message, e.StackTrace);
            }
            finally
            {
                ProtocolPool<TProtocol>.Return(protocol);
            }

            var deltaTicks = Stopwatch.GetTimestamp() - start;
            return (long)Math.Round((double)(deltaTicks * 1000) / Stopwatch.Frequency);
        }

        protected abstract void ExecuteCommand(TContext context, TProtocol protocol);
    }
}
