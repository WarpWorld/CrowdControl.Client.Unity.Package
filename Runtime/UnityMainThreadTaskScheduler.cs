using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrowdControl.Client.Unity
{
    public sealed class UnityMainThreadTaskScheduler : TaskScheduler
    {
        private readonly SynchronizationContext m_synchronizationContext;
        private readonly ConcurrentQueue<Task> m_scheduledTasks = new ConcurrentQueue<Task>();

        public UnityMainThreadTaskScheduler(SynchronizationContext synchronizationContext)
            => m_synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        protected override void QueueTask(Task task)
        {
            m_scheduledTasks.Enqueue(task);

            m_synchronizationContext.Post(static state =>
            {
                var (scheduler, t) = ((UnityMainThreadTaskScheduler, Task))state!;
                scheduler.ExecuteQueuedTask(t);
            }, (this, task));
        }

        private void ExecuteQueuedTask(Task task)
        {
            if (m_scheduledTasks.TryDequeue(out Task? dequeued) && !ReferenceEquals(dequeued, task))
                m_scheduledTasks.Enqueue(dequeued);

            TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (SynchronizationContext.Current != m_synchronizationContext)
                return false;

            return TryExecuteTask(task);
        }

        protected override IEnumerable<Task> GetScheduledTasks() => m_scheduledTasks.ToArray();
    }
}