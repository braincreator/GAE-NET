using System.Linq.Expressions;

namespace GoogleAppEngine.Datastore.LINQ
{
    public interface IDatastoreQueryVisitor
    {
        State Translate(Expression expression);
    }
}
