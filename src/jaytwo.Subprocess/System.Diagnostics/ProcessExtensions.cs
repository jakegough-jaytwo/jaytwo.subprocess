using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Diagnostics
{
    internal static class ProcessExtensions
    {
        // help from from https://stackoverflow.com/questions/470256/process-waitforexit-asynchronously

        /// <summary>
        /// Waits asynchronously for the process to exit.
        /// </summary>
        /// <param name="process">The process to wait for cancellation.</param>
        /// <param name="cancellationToken">A cancellation token. If invoked, the task will return
        /// immediately as canceled.</param>
        /// <returns>A Task representing waiting for the process to end.</returns>
        public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!process.EnableRaisingEvents)
            {
                throw new InvalidOperationException($"{nameof(process.EnableRaisingEvents)} must be {true} to use {nameof(WaitForExitAsync)}");
            }

            if (cancellationToken != default(CancellationToken))
            {
                var taskCompletionSource = new TaskCompletionSource<object>();

                cancellationToken.Register(() =>
                {
                    taskCompletionSource.TrySetCanceled();
                });

                process.Exited += (sender, args) => taskCompletionSource.TrySetResult(null);

                // just in case the process has already exited before assigning the event
                // (we don't want to check process.HasExited before setting the event becuase in the async world, the process can exit betwen the if check and wiring up the event handler)
                if (process.HasExited)
                {
                    taskCompletionSource.TrySetResult(null);
                }

                await taskCompletionSource.Task;
            }
        }

        /// <summary>
        /// Waits asynchronously for the process to exit.
        /// </summary>
        /// <param name="process">The process to wait for cancellation.</param>
        /// <param name="timeout">Duration after which to stop waiting for the task.</param>
        /// <returns>A Task representing waiting for the process to end.</returns>
        public static async Task WaitForExitAsync(this Process process, TimeSpan timeout)
        {
            using (var taskCompletionSource = new CancellationTokenSource(timeout))
            {
                try
                {
                    await WaitForExitAsync(process, taskCompletionSource.Token);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }
}
