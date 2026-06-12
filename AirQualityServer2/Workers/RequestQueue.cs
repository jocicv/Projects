using AirQualityServer.Logging;
using AirQualityServer.Models;

namespace AirQualityServer.Workers
{

    public sealed class RequestQueue
    {
        private readonly Queue<WorkItem> _queue = new();
        private readonly int _maxSize;
        private readonly object _lock = new();
        private bool _draining;

        private static readonly ThreadSafeLogger Logger = ThreadSafeLogger.Instance;

        public RequestQueue(int maxSize = 200)
        {
            _maxSize = maxSize;
        }

        public bool Enqueue(WorkItem item, int timeoutMs = 5000)
        {
            lock (_lock)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

                while (_queue.Count >= _maxSize && !_draining)
                {
                    var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0)
                    {
                        Logger.Warning($"Enqueue timeout — red je pun ({_queue.Count}/{_maxSize})", item.Request.RequestId);
                        return false;
                    }
                    Monitor.Wait(_lock, remaining);
                }

                if (_draining) return false;

                _queue.Enqueue(item);
                Logger.Debug($"Zahtev dodat u red (velicina: {_queue.Count}/{_maxSize})", item.Request.RequestId);
                Monitor.Pulse(_lock);
                return true;
            }
        }

        public WorkItem? Dequeue()
        {
            lock (_lock)
            {
                while (_queue.Count == 0 && !_draining)
                    Monitor.Wait(_lock);

                if (_queue.Count == 0) return null;

                var item = _queue.Dequeue();
                Monitor.Pulse(_lock);
                Logger.Debug($"Zahtev preuzet iz reda (velicina: {_queue.Count}/{_maxSize})", item.Request.RequestId);
                return item;
            }
        }

        public void BeginDrain()
        {
            lock (_lock)
            {
                _draining = true;
                Monitor.PulseAll(_lock);
                Logger.Info("Red zahteva se ispražnjava — dispatcher se gasi nakon obrade preostalih zahteva.");
            }
        }

        public int Count
        {
            get { lock (_lock) return _queue.Count; }
        }
    }

    public class WorkItem
    {
        public ClientRequest Request { get; init; } = null!;

        private readonly TaskCompletionSource<ClientResponse> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ClientResponse> Completion => _completion.Task;

        public void SetResult(ClientResponse response) => _completion.TrySetResult(response);

        public void SetError(string message) =>
            _completion.TrySetResult(new ClientResponse { Success = false, ErrorMessage = message });
    }
}
