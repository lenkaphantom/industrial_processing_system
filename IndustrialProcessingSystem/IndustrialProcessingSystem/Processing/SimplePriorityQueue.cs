using System.Collections.Generic;

namespace IndustrialProcessingSystem.Processing
{
    public class SimplePriorityQueue<TElement>
    {
        private readonly SortedList<int, Queue<TElement>> _buckets
            = new SortedList<int, Queue<TElement>>();

        private int _count = 0;

        public int Count => _count;

        public void Enqueue(TElement element, int priority)
        {
            if (!_buckets.ContainsKey(priority))
                _buckets[priority] = new Queue<TElement>();

            _buckets[priority].Enqueue(element);
            _count++;
        }

        public bool TryDequeue(out TElement element, out int priority)
        {
            if (_count == 0)
            {
                element = default(TElement);
                priority = 0;
                return false;
            }

            priority = _buckets.Keys[0];
            var bucket = _buckets[priority];

            element = bucket.Dequeue();
            _count--;

            if (bucket.Count == 0)
                _buckets.Remove(priority);

            return true;
        }

        public IEnumerable<TElement> PeekAll()
        {
            foreach (var bucket in _buckets.Values)
                foreach (var element in bucket)
                    yield return element;
        }
    }
}
