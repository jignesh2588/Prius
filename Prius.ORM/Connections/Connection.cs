﻿using System;
using System.Threading;
using Prius.Contracts.Interfaces;
using Prius.Contracts.Interfaces.Commands;
using Prius.Contracts.Interfaces.Connections;
using Prius.Contracts.Interfaces.Factory;
using Prius.Orm.Utility;

namespace Prius.Orm.Connections
{
    public abstract class Connection
    {
        private readonly IDataEnumeratorFactory _dataEnumeratorFactory;


        public abstract void BeginTransaction();
        public abstract void Commit();
        public abstract void Rollback();
        public abstract void SetCommand(ICommand command);

        public abstract IAsyncResult BeginExecuteReader(AsyncCallback callback);
        public abstract IAsyncResult BeginExecuteNonQuery(AsyncCallback callback);
        public abstract IAsyncResult BeginExecuteScalar(AsyncCallback callback);

        public abstract IDataReader EndExecuteReader(IAsyncResult asyncResult);
        public abstract long EndExecuteNonQuery(IAsyncResult asyncResult);
        public abstract T EndExecuteScalar<T>(IAsyncResult asyncResult);

        public virtual IDataReader ExecuteReader()
        {
            return EndExecuteReader(BeginExecuteReader(null));
        }

        public virtual long ExecuteNonQuery()
        {
            return EndExecuteNonQuery(BeginExecuteNonQuery(null));
        }

        public virtual T ExecuteScalar<T>()
        {
            return EndExecuteScalar<T>(BeginExecuteScalar(null));
        }

        protected Connection(IDataEnumeratorFactory dataEnumeratorFactory)
        {
            _dataEnumeratorFactory = dataEnumeratorFactory;
        }

        public IAsyncResult BeginExecuteEnumerable(AsyncCallback callback)
        {
            return BeginExecuteReader(callback);
        }

        public IDataEnumerator<T> EndExecuteEnumerable<T>(IAsyncResult asyncResult) where T: class
        {
            var reader = EndExecuteReader(asyncResult);
            return _dataEnumeratorFactory.Create<T>(reader, reader.Dispose);
        }

        public IDataEnumerator<T> ExecuteEnumerable<T>() where T : class
        {
            var reader = ExecuteReader();
            return _dataEnumeratorFactory.Create<T>(reader, reader.Dispose);
        }

        protected class AsyncContext
        {
            public object Result;
            public bool InitiallyClosed;
            public long StartTime;
        }
    }
}
