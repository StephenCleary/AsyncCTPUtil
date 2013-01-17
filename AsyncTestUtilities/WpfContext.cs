using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

/// <summary>
/// Async methods can run in a myriad of contexts - some have a "thread affinity"
/// such that continuations are posted back in a way that ensures that they always
/// execute on the originating thread.
/// 
/// WPF is one of such contexts.
/// </summary>
public static class WpfContext
{
    /// <summary>
    /// Runs the function inside a message loop and continues pumping messages
    /// until the returned task completes. 
    /// </summary>
    /// <returns>The completed task returned by the delegate's invocation</returns>
    public static Task<TResult> Run<TResult>(Func<Task<TResult>> function)
    {
        return ((Task<TResult>)Run((Func<Task>)function));
    }

    /// <summary>
    /// Runs the function inside a message loop and continues pumping messages
    /// until the returned task completes. 
    /// </summary>
    /// <returns>The completed task returned by the delegate's invocation</returns>
    public static Task Run(Func<Task> function)
    {
        using (SynchronizationContextSwitcher.Capture())
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            DispatcherFrame frame = new DispatcherFrame(exitWhenRequested: true);

            var message = new TaskFunctionLaunchMessage(function, dispatcher, frame);

            // queue up our first message before we run the loop
            dispatcher.BeginInvoke(new Action(message.LaunchMessageImpl));

            // run the actual WPF message loop
            Dispatcher.PushFrame(frame);

            // PushFrame() has returned. Because we set Contine = false
            // in a continuation of the task, this can only occur if the task
            // has also completed.

            if (message.ReturnedTask != null)
            {
                message.ReturnedTask.RethrowForCompletedTasks();
            }

            return message.ReturnedTask;
        }
    }

    // a helper class to represent the initial message that
    // we post to the queue to invoke the delegate as well
    // as set up our plumbing to shut down the loop at the right time
    class TaskFunctionLaunchMessage
    {
        public Task ReturnedTask;

        readonly Func<Task> taskFunction;
        readonly Dispatcher dispatcher;
        readonly DispatcherFrame frame;

        public TaskFunctionLaunchMessage(Func<Task> taskFunction, Dispatcher postingContext, DispatcherFrame frame)
        {
            this.taskFunction = taskFunction;
            this.dispatcher = postingContext;
            this.frame = frame;
        }

        public void LaunchMessageImpl()
        {
            // now invoke our taskFunction and store the returned task
            ReturnedTask = taskFunction.Invoke();

            if (ReturnedTask != null)
            {
                // register a continuation with the task, which will shut down the loop when the task completes.
                ReturnedTask.ContinueWith(delegate
                {
                    Action terminationAction = () => { frame.Continue = false; };
                    dispatcher.BeginInvoke(terminationAction);
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            else
            {
                // the delegate returned a null Task (VB/C# compilers never do this for async methods)
                // we don't have anything to register continuations with in this case, so exit out of the message loop
                // immediately
                frame.Continue = false;
            }
        }
    }

    /// <summary>
    /// Runs the action inside a message loop and continues pumping messages
    /// as long as any asynchronous operations have been registered
    /// </summary>
    public static void Run(Action asyncAction)
    {
        using (SynchronizationContextSwitcher.Capture())
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            DispatcherFrame frame = new DispatcherFrame(exitWhenRequested: true);

            var message = new AsyncActionLaunchMessage(asyncAction, dispatcher, frame);

            // queue up our first message before we run the loop
            dispatcher.BeginInvoke(new Action(message.LaunchMessageImpl));

            // run the actual WPF message loop
            Dispatcher.PushFrame(frame);

            // PushFrame() has returned. This must indicate that
            // the operation count has fallen back to zero.
        }
    }

    class AsyncActionLaunchMessage
    {
        readonly Action asyncAction;
        readonly Dispatcher dispatcher;
        readonly DispatcherFrame frame;

        AsyncVoidSyncContext asyncVoidContext;

        public AsyncActionLaunchMessage(Action asyncAction, Dispatcher dispatcher, DispatcherFrame frame)
        {
            this.asyncAction = asyncAction;
            this.dispatcher = dispatcher;
            this.frame = frame;
        }

        public void LaunchMessageImpl()
        {
            // wrap the WPF context in our own decorator context and install that
            this.asyncVoidContext = new AsyncVoidSyncContext(SynchronizationContext.Current, frame);
            SynchronizationContext.SetSynchronizationContext(asyncVoidContext);

            // now invoke our taskFunction and store the result
            // Do an explicit increment/decrement.
            // Our sync context does a check on decrement, to see if there are any
            // outstanding asynchronous operations (async void methods register this correctly).
            // If there aren't any registerd operations, then it will exit the loop
            asyncVoidContext.OperationStarted();
            try
            {
                asyncAction.Invoke();
            }
            finally
            {
                asyncVoidContext.OperationCompleted();
            }
        }
    }

    class AsyncVoidSyncContext : SynchronizationContext
    {
        readonly SynchronizationContext inner;
        readonly DispatcherFrame frame;
        int operationCount;

        /// <summary>Constructor for creating a new AsyncVoidSyncContext. Creates a new shared operation counter.</summary>
        public AsyncVoidSyncContext(SynchronizationContext innerContext, DispatcherFrame frame)
        {
            this.inner = innerContext;
            this.frame = frame;
        }

        public override SynchronizationContext CreateCopy()
        {
            return new AsyncVoidSyncContext(this.inner.CreateCopy(), frame);
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            inner.Post(d, state);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            inner.Send(d, state);
        }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            return inner.Wait(waitHandles, waitAll, millisecondsTimeout);
        }

        public override void OperationStarted()
        {
            inner.OperationStarted();
            Interlocked.Increment(ref this.operationCount);
        }

        public override void OperationCompleted()
        {
            inner.OperationCompleted();
            if (Interlocked.Decrement(ref this.operationCount) == 0)
            {
                this.Post((ignoredState) => { this.frame.Continue = false; }, state: null);
            }
        }
    }
}