using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrowdControl.Client.Unity
{
    public sealed class UnityMainThreadTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _tasks = new();
        private SynchronizationContext m_synchronizationContext;

        public UnityMainThreadTaskScheduler(SynchronizationContext synchronizationContext) => m_synchronizationContext = synchronizationContext;

        protected override void QueueTask(Task task) => _tasks.Enqueue(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (SynchronizationContext.Current != m_synchronizationContext)
                return false;

            return TryExecuteTask(task);
        }

        protected override IEnumerable<Task>? GetScheduledTasks() => _tasks.ToArray();

        public bool ExecuteOne()
        {
            if (_tasks.TryDequeue(out Task task))
                return TryExecuteTask(task);

            return false;
        }

        public int ExecuteAll()
        {
            int count = 0;

            while (_tasks.TryDequeue(out Task task))
            {
                if (TryExecuteTask(task))
                    count++;
            }

            return count;
        }
    }
}