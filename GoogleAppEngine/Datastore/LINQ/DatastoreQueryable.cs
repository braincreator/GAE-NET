using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;


namespace GoogleAppEngine.Datastore.LINQ
{
    public class DatastoreQueryable<T> : IOrderedQueryable<T>, IOrderedAsyncQueryable<T>
    {
        private DatastoreProvider _provider;
        private Expression _expression;

        public DatastoreQueryable(DatastoreProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            
            this._provider = provider;
            this._expression = Expression.Constant(this);
        }

        public DatastoreQueryable(DatastoreProvider provider, Expression expression)
            : this(provider)
        {
            if (!typeof(IQueryable<T>).GetTypeInfo().IsAssignableFrom(expression.Type.GetTypeInfo()) && !typeof(IAsyncQueryable<T>).GetTypeInfo().IsAssignableFrom(expression.Type.GetTypeInfo()))
                throw new ArgumentOutOfRangeException(nameof(expression));

            this._expression = expression;
        }

        public Type ElementType => typeof (T);
        public Expression Expression => this._expression;
        public IAsyncQueryProvider Provider => _provider;

        Expression IQueryable.Expression => this._expression;
        Type IQueryable.ElementType => typeof(T);
        IQueryProvider IQueryable.Provider => this._provider;

        private object GetExecuteResult()
        {
            var res = _provider.Execute(_expression);
            if (res == null)
                throw new NullReferenceException("Cannot enumerate over a null result.");
            return res;
        }

        private async Task<object> GetExecuteResultAsync()
        {
            var res = await _provider.ExecuteAsync<object>(_expression, default(CancellationToken)).ConfigureAwait(false);
            if (res == null)
                throw new NullReferenceException("Cannot enumerate over a null result.");
            return res;
        }

        //public async Task<IEnumerator<T>> GetEnumeratorAsync() => ((IEnumerable<T>) await GetExecuteResultAsync().ConfigureAwait(false)).GetEnumerator();
        
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)GetExecuteResult()).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)GetExecuteResult()).GetEnumerator();

        public override string ToString() => this._provider.GetQueryText(this._expression);

        IAsyncEnumerator<T> IAsyncEnumerable<T>.GetEnumerator() => ((IAsyncEnumerable<T>) GetExecuteResultAsync()).GetEnumerator();

        public IAsyncEnumerator<T> GetEnumeratorAsync() => ((IAsyncEnumerable<T>)GetExecuteResultAsync()).GetEnumerator();

    }
}
