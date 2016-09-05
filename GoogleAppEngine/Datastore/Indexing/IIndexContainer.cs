using System.Collections.Generic;
using GoogleAppEngine.Datastore.Indexing;

namespace GoogleAppEngine.Datastore.LINQ
{
    public interface IIndexContainer
    {
        List<Index> GetIndexList();
    }
}
