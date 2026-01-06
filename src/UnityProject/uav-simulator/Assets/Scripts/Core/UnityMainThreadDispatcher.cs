using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UavSimulator.Core
{
    public sealed class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static UnityMainThreadDispatcher instance;
        private static int mainThreadId;
        private readonly ConcurrentQueue<IWorkItem> queue = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            mainThreadId = Thread.CurrentThread.ManagedThreadId;

            var existing = FindFirstObjectByType<UnityMainThreadDispatcher>();
            if (existing != null)
            {
                instance = existing;
                return;
            }

            var go = new GameObject(nameof(UnityMainThreadDispatcher));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UnityMainThreadDispatcher>();
        }

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (instance != null) return instance;

                if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != mainThreadId)
                {
                    throw new InvalidOperationException($"{nameof(UnityMainThreadDispatcher)} is not initialized. Call it from Unity main thread first.");
                }

                var existing = FindFirstObjectByType<UnityMainThreadDispatcher>();
                if (existing != null)
                {
                    instance = existing;
                    return instance;
                }

                var go = new GameObject(nameof(UnityMainThreadDispatcher));
                DontDestroyOnLoad(go);
                instance = go.AddComponent<UnityMainThreadDispatcher>();
                return instance;
            }
        }

        public Task<T> Enqueue<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(new WorkItem<T>(func, tcs));
            return tcs.Task;
        }

        private void Update()
        {
            while (queue.TryDequeue(out var item))
            {
                item.Execute();
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private interface IWorkItem
        {
            void Execute();
        }

        private sealed class WorkItem<T> : IWorkItem
        {
            private readonly Func<T> func;
            private readonly TaskCompletionSource<T> tcs;

            public WorkItem(Func<T> func, TaskCompletionSource<T> tcs)
            {
                this.func = func;
                this.tcs = tcs;
            }

            public void Execute()
            {
                try
                {
                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }
    }
}
