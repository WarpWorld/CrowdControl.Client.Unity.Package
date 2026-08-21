using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CrowdControl.Client.Unity
{
    /// <summary>Schedules tasks for execution on Unity's main thread.</summary>
    public sealed class UnityMainThreadTaskScheduler : TaskScheduler
    {
        private readonly SynchronizationContext m_synchronizationContext;

        //keyed by task rather than queued, because the posted callbacks do not necessarily
        //arrive in the order the tasks were scheduled when more than one thread is scheduling
        private readonly ConcurrentDictionary<Task, long> m_scheduledTasks = new ConcurrentDictionary<Task, long>();
        private long m_scheduleSequence;

        /// <summary>Initializes a scheduler that posts work to the supplied synchronization context.</summary>
        /// <param name="synchronizationContext">The Unity main-thread synchronization context.</param>
        public UnityMainThreadTaskScheduler(SynchronizationContext synchronizationContext)
            => m_synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        /// <inheritdoc/>
        protected override void QueueTask(Task task)
        {
            m_scheduledTasks[task] = Interlocked.Increment(ref m_scheduleSequence);

            m_synchronizationContext.Post(static state =>
            {
                var (scheduler, t) = ((UnityMainThreadTaskScheduler, Task))state!;
                scheduler.ExecuteQueuedTask(t);
            }, (this, task));
        }

        private void ExecuteQueuedTask(Task task)
        {
            m_scheduledTasks.TryRemove(task, out _);

            TryExecuteTask(task);
        }

        /// <inheritdoc/>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (SynchronizationContext.Current != m_synchronizationContext)
                return false;

            return TryExecuteTask(task);
        }

        protected override IEnumerable<Task> GetScheduledTasks()
            => m_scheduledTasks.OrderBy(static entry => entry.Value).Select(static entry => entry.Key).ToArray();
    }
}