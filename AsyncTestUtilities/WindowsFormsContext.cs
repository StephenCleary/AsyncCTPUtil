using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

/// <summary>
/// Async methods can run in a myriad of contexts - some have a "thread affinity"
/// such that continuations are posted back in a way that ensures that they always
/// execute on the originating thread.
/// 
/// Windows Forms is one of such contexts.
/// </summary>
public static class WindowsFormsContext
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
        using (InstallerAndRestorer.Install())
        {
            // InstallerAndRestorer ensures the WinForms context is installed
            var winFormsContext = SynchronizationContext.Current;

            var message = new TaskFunctionLaunchMessage(function, winFormsContext);

            // queue up our first message before we run the loop
            winFormsContext.Post(message.LaunchMessageImpl, state: null);

            // run the actual WinForms message loop
            Application.Run();

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
        readonly SynchronizationContext postingContext;

        public TaskFunctionLaunchMessage(Func<Task> taskFunction, SynchronizationContext postingContext)
        {
            this.taskFunction = taskFunction;
            this.postingContext = postingContext;
        }

        // this signature is to match SendOrPostCallback
        public void LaunchMessageImpl(object ignoredState)
        {
            // now invoke our taskFunction and store the returned task
            ReturnedTask = taskFunction.Invoke();

            if (ReturnedTask != null)
            {
                // register a continuation with the task, which will shut down the loop when the task completes.
                ReturnedTask.ContinueWith(delegate { postingContext.RequestMessageLoopTermination(); }, TaskContinuationOptions.ExecuteSynchronously);
            }
            else
            {
                // the delegate returned a null Task (VB/C# compilers never do this for async methods)
                // we don't have anything to register continuations with in this case, so exit out of the message loop
                // immediately
                Application.ExitThread();
            }
        }
    }

    /// <summary>
    /// Runs the action inside a message loop and continues pumping messages
    /// as long as any asynchronous operations have been registered
    /// </summary>
    public static void Run(Action asyncAction)
    {
        using (InstallerAndRestorer.Install())
        {
            // InstallerAndRestorer ensures the WinForms context is installed
            // capture that WinFormsContext
            var winFormsContext = SynchronizationContext.Current;

            // wrap the WinForms context in our own decorator context and install that
            var asyncVoidContext = new AsyncVoidSyncContext(winFormsContext);
            SynchronizationContext.SetSynchronizationContext(asyncVoidContext);

            // queue up the first message before we start running the loop
            var message = new AsyncActionLaunchMessage(asyncAction, asyncVoidContext);
            asyncVoidContext.Post(message.LaunchMessageImpl, state: null);

            // run the actual WinForms message loop
            Application.Run();
        }
    }

    class AsyncActionLaunchMessage
    {
        readonly Action asyncAction;
        readonly AsyncVoidSyncContext postingContext;

        public AsyncActionLaunchMessage(Action asyncAction, AsyncVoidSyncContext postingContext)
        {
            this.asyncAction = asyncAction;
            this.postingContext = postingContext;
        }

        // this signature is to match SendOrPostCallback
        public void LaunchMessageImpl(object ignoredState)
        {
            // now invoke our taskFunction and store the result
            // Do an explicit increment/decrement.
            // Our sync context does a check on decrement, to see if there are any
            // outstanding asynchronous operations (async void methods register this correctly).
            // If there aren't any registerd operations, then it will exit the loop
            postingContext.OperationStarted();
            try
            {
                asyncAction.Invoke();
            }
            finally
            {
                postingContext.OperationCompleted();
            }
        }
    }

    static void RequestMessageLoopTermination(this SynchronizationContext syncContext)
    {
        syncContext.Post((state) => Application.ExitThread(), state: null);
    }

    struct InstallerAndRestorer : IDisposable
    {
        private bool originalAutoInstallValue;
        private SynchronizationContext originalSyncContext;
        private Control tempControl;

        public static InstallerAndRestorer Install()
        {
            // save the values to restore
            var iar = new InstallerAndRestorer();
            iar.originalAutoInstallValue = WindowsFormsSynchronizationContext.AutoInstall;
            iar.originalSyncContext = SynchronizationContext.Current;
            WindowsFormsSynchronizationContext.AutoInstall = true; // enable autoinstall of the official WinForms context
            iar.tempControl = new Control { Visible = false }; // create a control, which will cause the WinForms context to become installed
            return iar;
        }

        public void Dispose()
        {
            // dispose our temporary control
            if (tempControl != null)
            {
                tempControl.Dispose();
                tempControl = null;
            }

            // restore the autoinstall value
            WindowsFormsSynchronizationContext.AutoInstall = originalAutoInstallValue;

            // restore the sync context
            SynchronizationContext.SetSynchronizationContext(originalSyncContext);
        }
    }

    class AsyncVoidSyncContext : SynchronizationContext
    {
        readonly SynchronizationContext inner;
        int operationCount;

        /// <summary>Constructor for creating a new AsyncVoidSyncContext. Creates a new shared operation counter.</summary>
        public AsyncVoidSyncContext(SynchronizationContext innerContext)
        {
            this.inner = innerContext;
        }

        public override SynchronizationContext CreateCopy()
        {
            return new AsyncVoidSyncContext(this.inner.CreateCopy());
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
                this.RequestMessageLoopTermination();
            }
        }
    }
}