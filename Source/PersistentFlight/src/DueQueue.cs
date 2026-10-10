using System;
using System.Collections.Generic;

namespace PersistentFlight
{
    // One indexed entry per key: cancellation never leaves heap tombstones.
    public sealed class DueQueue<T>
    {
        sealed class Entry { public Guid Key; public double UT; public long Sequence; public T Value; public int Index; }
        readonly List<Entry> heap = new List<Entry>();
        readonly Dictionary<Guid, Entry> entries = new Dictionary<Guid, Entry>();
        long sequence;
        public int Count { get { return heap.Count; } }
        public double NextUT { get { return heap.Count == 0 ? double.PositiveInfinity : heap[0].UT; } }
        public void Clear() { heap.Clear(); entries.Clear(); sequence = 0; }
        static bool Before(Entry a, Entry b) { return a.UT < b.UT || (a.UT == b.UT && a.Sequence < b.Sequence); }
        void Swap(int a, int b) { Entry x=heap[a]; heap[a]=heap[b]; heap[b]=x; heap[a].Index=a; heap[b].Index=b; }
        void Up(int i) { while(i>0) { int p=(i-1)/2; if(!Before(heap[i],heap[p]))break; Swap(i,p); i=p; } }
        void Down(int i) { while(true) { int l=i*2+1; if(l>=heap.Count)return; int r=l+1; int n=r<heap.Count&&Before(heap[r],heap[l])?r:l; if(!Before(heap[n],heap[i]))return; Swap(i,n); i=n; } }
        public void Schedule(Guid key, double ut, T value)
        {
            if(double.IsNaN(ut)||double.IsInfinity(ut))throw new ArgumentOutOfRangeException("ut");
            Remove(key);
            var e=new Entry { Key=key, UT=ut, Value=value, Sequence=sequence++, Index=heap.Count };
            entries.Add(key,e); heap.Add(e); Up(e.Index);
        }
        public bool Remove(Guid key)
        {
            Entry e; if(!entries.TryGetValue(key,out e))return false;
            int i=e.Index, last=heap.Count-1; entries.Remove(key);
            if(i==last) { heap.RemoveAt(last); return true; }
            heap[i]=heap[last]; heap[i].Index=i; heap.RemoveAt(last);
            if(i>0&&Before(heap[i],heap[(i-1)/2]))Up(i); else Down(i);
            return true;
        }
        public bool TryTake(double now, out T value)
        {
            if(double.IsNaN(now)||double.IsInfinity(now))throw new ArgumentOutOfRangeException("now");
            if(heap.Count==0||heap[0].UT>now) { value=default(T); return false; }
            Entry e=heap[0]; value=e.Value; Remove(e.Key); return true;
        }
    }
}
