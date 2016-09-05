using System.Collections.Generic;
using GoogleAppEngine.Datastore.Indexing;

namespace GoogleAppEngine.Datastore.LINQ
{
    public class InMemoryIndexContainer : IIndexContainer
    {
        private List<Index> _indexes = new List<Index>();

        public List<Index> GetIndexList()
        {
            return _indexes;
        }
    }
}
