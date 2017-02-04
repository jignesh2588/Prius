﻿using System;
using System.Data.SqlClient;
using System.Text;
using System.Threading;
using Prius.Contracts.Attributes;
using Prius.Contracts.Interfaces;
using Prius.Contracts.Interfaces.Commands;
using Prius.Contracts.Interfaces.Connections;
using Prius.Contracts.Interfaces.External;
using Prius.Contracts.Interfaces.Factory;
using Prius.Contracts.Utility;

namespace Prius.SqlServer
{
    [Provider("SqlServer", "Microsoft SQL Server connection provider")]
    public class Connection : Disposable, IConnection, IConnectionProvider
    {
        private readonly IErrorReporter _errorReporter;
        private readonly IDataEnumeratorFactory _dataEnumeratorFactory;

        public object RepositoryContext { get; set; }

        private IRepository _repository;
        private ICommand _command;

        private SqlConnection _connection;
        private SqlTransaction _transaction;
        private SqlCommand _sqlCommand;

        #region Lifetime

        public Connection(
            IErrorReporter errorReporter,
            IDataEnumeratorFactory dataEnumeratorFactory)
        {
            _errorReporter = errorReporter;
            _dataEnumeratorFactory = dataEnumeratorFactory;
        }

        public IConnection Open(IRepository repository, ICommand command, string connectionString, string schemaName)
        {
            _repository = repository;
            _connection = new SqlConnection(connectionString);
            _transaction = null;
            SetCommand(command);
            return this;
        }

        protected override void Dispose(bool destructor)
        {
            Commit();
            _connection.Dispose();
        }

        #endregion

        #region Transactions

        public void BeginTransaction()
        {
            Commit();
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                try
                {
                    _connection.Open();
                }
                catch (Exception ex)
                {
                    _repository.RecordFailure(this);
                    _errorReporter.ReportError(ex, _sqlCommand, "Failed to open connection on " + _repository.Name);
                    throw;
                }
            }
            _transaction = _connection.BeginTransaction();
        }

        public void Commit()
        {
            if (_transaction != null)
            {
                _transaction.Commit();
                _transaction = null;
            }
        }

        public void Rollback()
        {
            if (_transaction != null)
            {
                _transaction.Rollback();
                _transaction = null;
            }
        }

        #endregion

        #region Command setup

        public void SetCommand(ICommand command)
        {
            if (command == null) return;
            _command = command;

            _sqlCommand = new SqlCommand(command.CommandText, _connection, _transaction);
            _sqlCommand.CommandType = (System.Data.CommandType)command.CommandType;
            if (command.TimeoutSeconds.HasValue)
                _sqlCommand.CommandTimeout = command.TimeoutSeconds.Value;
            foreach (var parameter in command.GetParameters())
            {
                var sqlParameter = _sqlCommand.Parameters.Add("@" + parameter.Name, parameter.DbType, (int)parameter.Size);
                sqlParameter.Direction = (System.Data.ParameterDirection)parameter.Direction;
                sqlParameter.Value = parameter.Value;

                if (parameter.Direction == ParameterDirection.InputOutput || parameter.Direction == ParameterDirection.Output)
                    parameter.StoreOutputValue = p => p.Value = sqlParameter.Value;
            }
        }

        #endregion

        #region ExecuteReader

        public IAsyncResult BeginExecuteReader(AsyncCallback callback)
        {
            var asyncContext = new AsyncContext 
            { 
                InitiallyClosed = _connection.State == System.Data.ConnectionState.Closed, 
                StartTime = PerformanceTimer.TimeNow 
            };

            try
            {
                if (asyncContext.InitiallyClosed) _connection.Open();
                return _sqlCommand.BeginExecuteReader(callback, asyncContext);
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteReader on SQL Server " + _repository.Name, _repository, this);
                if (asyncContext.InitiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
                throw;
            }
        }

        public IDataReader EndExecuteReader(IAsyncResult asyncResult)
        {
            var asyncContext = (AsyncContext)asyncResult.AsyncState;
            try
            {
                if (asyncContext.Result != null) return (IDataReader)asyncContext.Result;

                var reader = _sqlCommand.EndExecuteReader(asyncResult);

                foreach (var parameter in _command.GetParameters())
                    parameter.StoreOutputValue(parameter);

                var dataShapeName = _connection.DataSource + ":" + _connection.Database + ":" + _sqlCommand.CommandType + ":" + _sqlCommand.CommandText;

                return new DataReader(_errorReporter).Initialize(
                    reader,
                    dataShapeName,
                    () =>
                    {
                        reader.Dispose();
                        _repository.RecordSuccess(this, PerformanceTimer.TicksToSeconds(PerformanceTimer.TimeNow - asyncContext.StartTime));
                        if (asyncContext.InitiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                            _connection.Close();
                    },
                    () =>
                    {
                        _repository.RecordFailure(this);
                        if (asyncContext.InitiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                            _connection.Close();
                    }
                    );
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteReader on SQL Server " + _repository.Name, _repository, this);
                if (asyncContext.InitiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
                throw;
            }
        }

        public IDataReader ExecuteReader()
        {
            var initiallyClosed = _connection.State == System.Data.ConnectionState.Closed;
            var startTime = PerformanceTimer.TimeNow;

            try
            {
                if (initiallyClosed) _connection.Open();
                var reader = _sqlCommand.ExecuteReader();

                foreach (var parameter in _command.GetParameters())
                    parameter.StoreOutputValue(parameter);

                var dataShapeName = _connection.DataSource + ":" + _connection.Database + ":" + _sqlCommand.CommandType + ":" + _sqlCommand.CommandText;

                return new DataReader(_errorReporter).Initialize(
                    reader,
                    dataShapeName,
                    () =>
                        {
                            reader.Dispose();
                            _repository.RecordSuccess(this, PerformanceTimer.TicksToSeconds(PerformanceTimer.TimeNow - startTime));
                            if (initiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                                _connection.Close();
                        },
                    () =>
                        {
                            _repository.RecordFailure(this);
                            if (initiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                                _connection.Close();
                        }
                    );
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteReader on SQL Server " + _repository.Name, _repository, this);
                if (initiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
                throw;
            }
        }

        #endregion

        #region ExecuteEnumerable

        public IAsyncResult BeginExecuteEnumerable(AsyncCallback callback)
        {
            return BeginExecuteReader(callback);
        }

        public IDataEnumerator<T> EndExecuteEnumerable<T>(IAsyncResult asyncResult) where T : class
        {
            var reader = EndExecuteReader(asyncResult);
            return _dataEnumeratorFactory.Create<T>(reader, reader.Dispose);
        }

        public IDataEnumerator<T> ExecuteEnumerable<T>() where T : class
        {
            var reader = ExecuteReader();
            return _dataEnumeratorFactory.Create<T>(reader, reader.Dispose);
        }

        #endregion

        #region ExecuteNonQuery

        public IAsyncResult BeginExecuteNonQuery(AsyncCallback callback)
        {
            var asyncContext = new AsyncContext
            {
                InitiallyClosed = _connection.State == System.Data.ConnectionState.Closed,
                StartTime = PerformanceTimer.TimeNow
            };

            try
            {
                if (asyncContext.InitiallyClosed) _connection.Open();
                return _sqlCommand.BeginExecuteNonQuery(callback, asyncContext);
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteNonQuery on SQL Server " + _repository.Name, _repository, this);
                asyncContext.Result = (long)0;
                throw;
            }
        }

        public long EndExecuteNonQuery(IAsyncResult asyncResult)
        {
            var asyncContext = (AsyncContext)asyncResult.AsyncState;
            try
            {
                if (asyncContext.Result != null) return (long)asyncContext.Result;

                var rowsAffacted = _sqlCommand.EndExecuteNonQuery(asyncResult);
                _repository.RecordSuccess(this, PerformanceTimer.TicksToSeconds(PerformanceTimer.TimeNow - asyncContext.StartTime));

                foreach (var parameter in _command.GetParameters())
                    parameter.StoreOutputValue(parameter);

                return rowsAffacted;
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteNonQuery on SQL Server " + _repository.Name, _repository, this);
                throw;
            }
            finally
            {
                if (asyncContext.InitiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
            }
        }

        public long ExecuteNonQuery()
        {
            var initiallyClosed = _connection.State == System.Data.ConnectionState.Closed;
            var startTime = PerformanceTimer.TimeNow;

            try
            {
                if (initiallyClosed) _connection.Open();
                var rowsAffacted = _sqlCommand.ExecuteNonQuery();
                _repository.RecordSuccess(this, PerformanceTimer.TicksToSeconds(PerformanceTimer.TimeNow - startTime));

                foreach (var parameter in _command.GetParameters())
                    parameter.StoreOutputValue(parameter);

                return rowsAffacted;
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteNonQuery on SQL Server " + _repository.Name, _repository, this);
                throw;
            }
            finally
            {
                if (initiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
            }
        }


        #endregion

        #region ExecuteScalar

        public IAsyncResult BeginExecuteScalar(AsyncCallback callback)
        {
            var asyncContext = new AsyncContext
            {
                InitiallyClosed = _connection.State == System.Data.ConnectionState.Closed,
                StartTime = PerformanceTimer.TimeNow
            };

            try
            {
                if (asyncContext.InitiallyClosed) _connection.Open();
                asyncContext.Result = _sqlCommand.ExecuteScalar();
                _repository.RecordSuccess(this, PerformanceTimer.TicksToSeconds(PerformanceTimer.TimeNow - asyncContext.StartTime));
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteScalar on SQL Server " + _repository.Name, _repository, this);
                throw;
            }
            finally
            {
                if (asyncContext.InitiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
            }
            return new SyncronousResult(asyncContext, callback);
        }

        public T EndExecuteScalar<T>(IAsyncResult asyncResult)
        {
            var asyncContext = (AsyncContext)asyncResult.AsyncState;
            try
            {
                if (asyncContext.Result == null) return default(T);
                var resultType = typeof(T);
                if (resultType.IsNullable()) resultType = resultType.GetGenericArguments()[0];
                return (T)Convert.ChangeType(asyncContext.Result, resultType);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to convert type of result from ExecuteScalar on SQL Server " + _repository.Name, _repository, this);
                throw;
            }
        }

        public T ExecuteScalar<T>()
        {
            var initiallyClosed = _connection.State == System.Data.ConnectionState.Closed;
            var startTime = PerformanceTimer.TimeNow;

            try
            {
                if (initiallyClosed) _connection.Open();
                var result = _sqlCommand.ExecuteScalar();
                _repository.RecordSuccess(this, PerformanceTimer.TicksToSeconds(PerformanceTimer.TimeNow - startTime));

                if (result == null) return default(T);
                var resultType = typeof(T);
                if (resultType.IsNullable()) resultType = resultType.GetGenericArguments()[0];
                return (T)Convert.ChangeType(result, resultType);
            }
            catch (Exception ex)
            {
                _repository.RecordFailure(this);
                _errorReporter.ReportError(ex, _sqlCommand, "Failed to ExecuteScalar on SQL Server " + _repository.Name, _repository, this);
                throw;
            }
            finally
            {
                if (initiallyClosed && _connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
            }
        }

        #endregion

        #region ToString

        public override string ToString()
        {
            var sb = new StringBuilder("SQL Server connection: ");
            sb.AppendFormat("Repository='{0}'; ", _repository.Name);
            sb.AppendFormat("Database='{0}'; ", _connection.Database);
            sb.AppendFormat("DataSource='{0}'; ", _connection.DataSource);
            sb.AppendFormat("CommandType='{0}'; ", _sqlCommand.CommandType);
            sb.AppendFormat("CommandText='{0}'; ", _sqlCommand.CommandText);
            return sb.ToString();
        }

        #endregion

        #region private classes

        private class AsyncContext
        {
            public object Result;
            public bool InitiallyClosed;
            public long StartTime;
        }

        private class SyncronousResult : IAsyncResult
        {
            public WaitHandle AsyncWaitHandle { get; private set; }
            public object AsyncState { get; private set; }
            public bool CompletedSynchronously { get { return true; } }
            public bool IsCompleted { get { return true; } }

            public SyncronousResult(AsyncContext asyncContext, AsyncCallback callback)
            {
                AsyncState = asyncContext;
                AsyncWaitHandle = new ManualResetEvent(true);
                if (callback != null) callback(this);
            }
        }

        #endregion
    }
}
