﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using WorkItem = System.Collections.Generic.KeyValuePair<System.Threading.SendOrPostCallback, object>;

public static class GeneralThreadAffineContext
{
    class WorkQueue
    {
        private readonly BlockingCollection<WorkItem> m_queue = new BlockingCollection<WorkItem>();

        internal void Enqueue(WorkItem item)
        {
            try { m_queue.Add(item); }
            catch (InvalidOperationException) { }
        }

        internal void Shutdown() { m_queue.CompleteAdding(); }

        internal void ExecuteWorkQueueLoop()
        {
            foreach (var currentItem in m_queue.GetConsumingEnumerable())
            {
                currentItem.Key.Invoke(currentItem.Value);
            }
        }
    }

    class Context : SynchronizationContext
    {
        internal readonly WorkQueue WorkQueue;

        internal Context() : this(new WorkQueue()) { }

        protected Context(WorkQueue queue)
        {
            this.WorkQueue = queue;
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            WorkQueue.Enqueue(new WorkItem(callback, state));
        }

        public override SynchronizationContext CreateCopy()
        {
            return new Context(WorkQueue);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            throw new NotImplementedException();
        }
    }

    public static Task<TResult> Run<TResult>(Func<Task<TResult>> asyncMethod)
    {
        return (Task<TResult>)Run((Func<Task>)asyncMethod);
    }

    public static Task Run(Func<Task> asyncMethod)
    {
        using (SynchronizationContextSwitcher.Capture())
        {
            var customContext = new Context();
            SynchronizationContext.SetSynchronizationContext(customContext);

            var task = asyncMethod.Invoke();

            if (task != null)
            {
                // register a continuation with the task, which will shut down the loop when the task completes.
                task.ContinueWith(_ => customContext.WorkQueue.Shutdown(), TaskContinuationOptions.ExecuteSynchronously);
            }
            else
            {
                // the delegate returned a null Task (VB/C# compilers never do this for async methods)
                // we don't have anything to register continuations with in this case, so just return immediately
                return task;
            }

            customContext.WorkQueue.ExecuteWorkQueueLoop();

            task.RethrowForCompletedTasks();

            return task;
        }
    }

    /// <summary>
    /// Runs the action inside a message loop and continues looping work items
    /// as long as any asynchronous operations have been registered
    /// </summary>
    public static void Run(Action asyncAction)
    {
        using (SynchronizationContextSwitcher.Capture())
        {
            var customContext = new VoidContext();
            SynchronizationContext.SetSynchronizationContext(customContext);

            // Do an explicit increment/decrement.
            // Our sync context does a check on decrement, to see if there are any
            // outstanding asynchronous operations (async void methods register this correctly).
            // If there aren't any registerd operations, then it will exit the loop
            customContext.OperationStarted();
            try
            {
                asyncAction.Invoke();
            }
            finally
            {
                customContext.OperationCompleted();
            }

            customContext.WorkQueue.ExecuteWorkQueueLoop();
            // ExecuteWorkQueueLoop() has returned. This must indicate that
            // the operation count has fallen back to zero.
        }
    }

    class VoidContext : Context
    {
        int operationCount;

        /// <summary>Constructor for creating a new AsyncVoidSyncContext. Creates a new shared operation counter.</summary>
        internal VoidContext() { }

        VoidContext(WorkQueue queue) : base(queue) { }

        public override SynchronizationContext CreateCopy()
        {
            return new VoidContext(this.WorkQueue);
        }

        public override void OperationStarted()
        {
            Interlocked.Increment(ref this.operationCount);
        }

        public override void OperationCompleted()
        {
            if (Interlocked.Decrement(ref this.operationCount) == 0)
            {
                WorkQueue.Shutdown();
            }
        }
    }
}
