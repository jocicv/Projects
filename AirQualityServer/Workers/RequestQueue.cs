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
                        Logger.Warning($"Enqueue timeout — queue full ({_queue.Count}/{_maxSize})", item.Request.RequestId);
                        return false;
                    }
                    Monitor.Wait(_lock, remaining);
                }

                if (_draining) return false;

                _queue.Enqueue(item);
                Logger.Debug($"Enqueued request (queue size: {_queue.Count}/{_maxSize})", item.Request.RequestId);
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
                Logger.Debug($"Dequeued request (queue size: {_queue.Count}/{_maxSize})", item.Request.RequestId);
                return item;
            }
        }

        public void BeginDrain()
        {
            lock (_lock)
            {
                _draining = true;
                Monitor.PulseAll(_lock);
                Logger.Info("Request queue draining — all workers will terminate after processing pending items.");
            }
        }

        public int Count
        {
            get { lock (_lock) return _queue.Count; }
        }
    }

    public class WorkItem
    {
        public ClientRequest Request { get; set; } = null!;

        private ClientResponse? _result;
        private bool _completed;
        private readonly object _resultLock = new();

        public void SetResult(ClientResponse response)
        {
            lock (_resultLock)
            {
                _result = response;
                _completed = true;
                Monitor.PulseAll(_resultLock);
            }
        }

        public ClientResponse WaitForResult(int timeoutMs = 15000)
        {
            lock (_resultLock)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (!_completed)
                {
                    var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0)
                        return new ClientResponse { Success = false, ErrorMessage = "Obrada zahteva nije zavrsena na vreme." };

                    Monitor.Wait(_resultLock, remaining);
                }
                return _result!;
            }
        }
    }
}
