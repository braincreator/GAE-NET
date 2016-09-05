using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Datastore.v1beta3.Data;
using GoogleAppEngine.Datastore.Indexing;
using GoogleAppEngine.Datastore.Serialization;
using GoogleAppEngine.Shared;


namespace GoogleAppEngine.Datastore.LINQ
{
    public class DatastoreTranslatorProvider<T, TQueryVisitor, TSerializer> : DatastoreProvider
        where T : new()
        where TQueryVisitor : IDatastoreQueryVisitor, new()
        where TSerializer : IDatastoreSerializer<T>, new()
    {
        private TQueryVisitor _expvisitor = new TQueryVisitor();
        private TSerializer _serializer = new TSerializer();

        public DatastoreTranslatorProvider(CloudAuthenticator authenticator, DatastoreConfiguration configuration, IIndexContainer indexes)
            : base(authenticator, configuration, indexes)
        {
        }

        //public async override Task<object> ExecuteAsync(Expression expression)
        //{
        //    var state = Translate(expression);

        //    // Perform local indexing if needed
        //    if (Configuration.GenerateIndexYAMLFile)
        //        BuildIndex(state);

        //    // Build a query
        //    var datastore = new Google.Apis.Datastore.v1beta3.DatastoreService(Authenticator.GetInitializer());
        //    var dict = state.Parameters.ToDictionary(x => x.ParameterName, parameter => new GqlQueryParameter()
        //    {
        //        Value = ReadQuery_ConvertTypeToValueType(parameter.ParameterName, parameter.Value, parameter.TypeCode)
        //    });
        //    var gql = new GqlQuery
        //    {
        //        QueryString = state.QueryBuilder.ToString(),
        //        AllowLiterals = false, // enforce parameterized queries
        //        NamedBindings = dict
        //    };

        //    // Grab results
        //    var result = await datastore.Projects.RunQuery(new RunQueryRequest
        //    {
        //        GqlQuery = gql
        //    }, Authenticator.GetProjectId()).ExecuteAsync().ConfigureAwait(false);

        //    // Project if necessary / Select() method
        //    if (state.Projector != null)
        //    {
        //        var elementType = TypeSystem.GetElementType(expression.Type);
        //        var projector = state.Projector.Compile();

        //        return Activator.CreateInstance(
        //            typeof(ProjectionReader<>).MakeGenericType(elementType),
        //            BindingFlags.Instance | BindingFlags.NonPublic, null,
        //            new object[] { result.Batch.EntityResults.Select(x => x.Entity), projector },
        //            null
        //            );
        //    }

        //    // First()/Single() method
        //    if (state.QueryState.HasFlag(QueryState.IsFirst) || state.QueryState.HasFlag(QueryState.IsSingle))
        //    {
        //        if (result.Batch.EntityResults.Count == 0 && !state.QueryState.HasFlag(QueryState.AllowFirstSingleOrDefault))
        //            throw new InvalidOperationException("Sequence contains no elements");

        //        if (result.Batch.EntityResults.Count > 1 && state.QueryState.HasFlag(QueryState.IsSingle))
        //            throw new InvalidOperationException("Sequence contains more than one element");

        //        if (result.Batch.EntityResults.Any())
        //            return _serializer.DeserializeEntity(result.Batch.EntityResults[0].Entity);

        //        return null;
        //    }

        //    // Any() method
        //    if (state.QueryState.HasFlag(QueryState.IsAny))
        //        return result.Batch.EntityResults.Any();

        //    // Regular Where() query
        //    return result.Batch.EntityResults.Select(entityResult => _serializer.DeserializeEntity(entityResult.Entity)).ToList();
        //}

        public override CloudAuthenticator GetAuthenticator() => Authenticator;

        private Expression ReduceExpression(Expression expression)
        {
            while (expression.CanReduce)
                expression = expression.ReduceAndCheck();

            return expression;
        }

        public override string GetQueryText(Expression expression)
        {
            expression = ReduceExpression(expression);

            // Create a new expression visitor instead so the state does not change
            var state = new TQueryVisitor().Translate(expression);
            var query = state.QueryBuilder.ToString();

            // Basic translate into a parameter-less query
            return state.Parameters.Aggregate(query, (current, p) =>
                current.Replace(p.ParameterName, p.TypeCode == TypeCode.DateTime ? QueryHelper.NormalizeDatetime((DateTime)p.Value)
                : Convert.ToString(p.Value)));
        }

        private Value ReadQuery_ConvertTypeToValueType(string paramName, object value, TypeCode type)
        {
            switch (type)
            {
                case TypeCode.Boolean:
                    return new Value { BooleanValue = (bool)value };
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return new Value { IntegerValue = (long)value };
                case TypeCode.DateTime:
                    return new Value { TimestampValue = (DateTime)value };
                case TypeCode.String:
                    return new Value { StringValue = (string)value ?? "" };
                case TypeCode.Double:
                    return new Value { DoubleValue = (double)value };
                case TypeCode.Decimal:
                    return new Value { DoubleValue = Convert.ToDouble((decimal)value) };

                default:
                    throw new NotSupportedException($"The type of `{paramName}` is not supported.");
            }
        }
        
        public override async Task<object> ExecuteAsync(Expression expression, CancellationToken token)
        {
            var state = Translate(expression);

            // Perform local indexing if needed
            if (Configuration.GenerateIndexYAMLFile)
                BuildIndex(state);

            // Build a query
            var datastore = new Google.Apis.Datastore.v1beta3.DatastoreService(Authenticator.GetInitializer());
            var dict = state.Parameters.ToDictionary(x => x.ParameterName, parameter => new GqlQueryParameter()
            {
                Value = ReadQuery_ConvertTypeToValueType(parameter.ParameterName, parameter.Value, parameter.TypeCode)
            });
            var gql = new GqlQuery
            {
                QueryString = state.QueryString,
                AllowLiterals = true, // enforce parameterized queries
                NamedBindings = dict
            };

            // Grab results
            var result = await datastore.Projects.RunQuery(new RunQueryRequest
            {
                GqlQuery = gql
            }, Authenticator.GetProjectId()).ExecuteAsync(token).ConfigureAwait(false);

            // Project if necessary / Select() method
            if (state.Projector != null)
            {
                var elementType = TypeSystem.GetElementType(expression.Type);
                var projector = state.Projector.Compile();

                return (T) Activator.CreateInstance(
                    typeof(ProjectionReader<>).MakeGenericType(elementType),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { result.Batch.EntityResults.Select(x => x.Entity), projector },
                    null
                    );
            }

            // First()/Single() method
            if (state.QueryState.HasFlag(QueryState.IsFirst) || state.QueryState.HasFlag(QueryState.IsSingle))
            {
                if (result.Batch.EntityResults.Count == 0 && !state.QueryState.HasFlag(QueryState.AllowFirstSingleOrDefault))
                    throw new InvalidOperationException("Sequence contains no elements");

                if (result.Batch.EntityResults.Count > 1 && state.QueryState.HasFlag(QueryState.IsSingle))
                    throw new InvalidOperationException("Sequence contains more than one element");

                if (result.Batch.EntityResults.Any())
                    return _serializer.DeserializeEntity(result.Batch.EntityResults[0].Entity);

                return default(T);
            }

            // Any() method
            if (state.QueryState.HasFlag(QueryState.IsAny))
                return result.Batch.EntityResults.Any();

            // Regular Where() query
            return result.Batch.EntityResults.Select(entityResult => _serializer.DeserializeEntity(entityResult.Entity)).ToList();
        }

        public override object Execute(Expression expression)
        {
            var state = Translate(expression);

            // Perform local indexing if needed
            if (Configuration.GenerateIndexYAMLFile)
                BuildIndex(state);

            // Build a query
            var datastore = new Google.Apis.Datastore.v1beta3.DatastoreService(Authenticator.GetInitializer());
            var dict = state.Parameters.ToDictionary(x => x.ParameterName, parameter => new GqlQueryParameter()
            {
                Value = ReadQuery_ConvertTypeToValueType(parameter.ParameterName, parameter.Value, parameter.TypeCode)
            });
            var gql = new GqlQuery
            {
                QueryString = state.QueryBuilder.ToString(),
                AllowLiterals = false, // enforce parameterized queries
                NamedBindings = dict
                
                //NameArgs = state.Parameters.Select(x => new GqlQueryArg { Name = x.ParameterName,
                //    Value = ReadQuery_ConvertTypeToValueType(x.ParameterName, x.Value, x.TypeCode) }).ToList()
            };

            // Grab results
            var result = datastore.Projects.RunQuery(new RunQueryRequest
            {
                GqlQuery = gql
            }, Authenticator.GetProjectId()).Execute();

            // Project if necessary / Select() method
            if (state.Projector != null)
            {
                var elementType = TypeSystem.GetElementType(expression.Type);
                var projector = state.Projector.Compile();

                return Activator.CreateInstance(
                    typeof(ProjectionReader<>).MakeGenericType(elementType),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { result.Batch.EntityResults.Select(x => x.Entity), projector },
                    null
                    );
            }

            // First()/Single() method
            if (state.QueryState.HasFlag(QueryState.IsFirst) || state.QueryState.HasFlag(QueryState.IsSingle))
            {
                if (result.Batch.EntityResults.Count == 0 && !state.QueryState.HasFlag(QueryState.AllowFirstSingleOrDefault))
                    throw new InvalidOperationException("Sequence contains no elements");

                if (result.Batch.EntityResults.Count > 1 && state.QueryState.HasFlag(QueryState.IsSingle))
                    throw new InvalidOperationException("Sequence contains more than one element");

                if (result.Batch.EntityResults.Any())
                    return _serializer.DeserializeEntity(result.Batch.EntityResults[0].Entity);

                return null;
            }

            // Any() method
            if (state.QueryState.HasFlag(QueryState.IsAny))
                return result.Batch.EntityResults.Any();

            // Regular Where() query
            return result.Batch.EntityResults.Select(entityResult => _serializer.DeserializeEntity(entityResult.Entity)).ToList();
        }

        private State Translate(Expression expression)
        {
            expression = ReduceExpression(expression);
            return _expvisitor.Translate(expression);
        }

        /// <summary>
        /// Builds a very basic index configuration in-memory given state.
        /// </summary>
        /// <returns>Contents of the index file.</returns>
        protected override void BuildIndex(State state)
        {
            // Google automatically builds single property indexes
            if (state.QueryBuilder.Count(x => x.ComponentType == QueryComponentType.MemberName
            || (x.ComponentType == QueryComponentType.QueryPartSelectProjection && x.Component != "*")) <= 1)
                return;

            var indexPath = Configuration.IndexFileLocation;
            var indexes = IndexContainer.GetIndexList();

            // Load indexes into memory if necessary
            if (indexes.Count == 0)
            {
                // Create index file it it does not exist
                if (!File.Exists(indexPath))
                    File.WriteAllText(indexPath, string.Empty);

                var indexYaml = File.ReadAllLines(indexPath);
                indexes.AddRange(IndexParser.Deserialize(indexYaml));
            }

            var kind = typeof(T).Name;
            var properties = state.QueryBuilder.Where(x => x.ComponentType == QueryComponentType.MemberName)
                .Select(x => x.Component).ToList();

            // Add projections
            properties.AddRange(state.QueryBuilder.Where(x => x.ComponentType == QueryComponentType.QueryPartSelectProjection)
                .SelectMany(x => x.Component.Split(',').Select(y => y.Trim())));

            // Create index if it does not have the properties with the specified ordering
            // TODO ordering of index properties + orderingtype (sorting) of indexes
            if (!indexes.Any(x => x.Properties.Select(y => y.PropertyName).SequenceEqual(properties)))
            {
                var index = new Index
                {
                    Kind = kind,
                    Properties = properties.Select(
                        x => new Index.IndexProperty
                        {
                            OrderingType = Index.OrderingType.NotSpecified,
                            PropertyName = x
                        })
                        .ToList()
                };
                indexes.Add(index);

                // Save it to file
                File.WriteAllText(indexPath, IndexParser.Serialize(indexes));
            }
        }
    }
}
