using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SP.Core.Serialization;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public abstract class CommandBase<TContext, TProtocol> : ICommand
        where TContext : ICommandContext
        where TProtocol : class, IProtocolData, new()
    {
        private static class ProtocolPool<T> where T : class, IProtocolData, new()
        {
            private const int LocalCapacity = 512;
            [ThreadStatic] private static LocalStack _localPool;

            private class LocalStack
            {
                public readonly T[] Items = new T[LocalCapacity];
                public int Count;
            }
            
            private static readonly ConcurrentQueue<T> _globalQueue = new ConcurrentQueue<T>();
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static T Rent()
            {
                var localStack = _localPool;
                if (localStack != null && localStack.Count > 0)
                {
                    return localStack.Items[--localStack.Count];
                }
            
                return _globalQueue.TryDequeue(out var instance) ? instance : new T();
            }
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Return(T instance)
            {
                if (instance == null) return;

                try
                {
                    NetSerializer<T>.Reset(instance);
                }
                catch
                {
                    return;
                }
            
                _localPool ??= new LocalStack();
            
                var localStack = _localPool;
                if (localStack.Count < LocalCapacity)
                {
                    localStack.Items[localStack.Count++] = instance;
                }
                else
                {
                    _globalQueue.Enqueue(instance);
                }
            }
        }
        
        public string Name => GetType().Name;
        public Type ContextType => typeof(TContext);

        public double Execute(ICommandContext context, IMessage message)
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
            double executionTimeMs;
            
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
                var end = Stopwatch.GetTimestamp();
                executionTimeMs = (double)(end - start) / Stopwatch.Frequency * 1000;
                
                ProtocolPool<TProtocol>.Return(protocol);
            }
            
            return executionTimeMs;
        }

        protected abstract void ExecuteCommand(TContext context, TProtocol protocol);
    }
}
