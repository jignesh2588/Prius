﻿using System;
using System.Data.SQLite;
using Prius.Contracts.Interfaces;
using Prius.Contracts.Interfaces.External;
using Prius.Contracts.Utility;

namespace Prius.SQLite.CommandProcessing
{
    /// <summary>
    /// This class is used to return result sets to Prius.
    /// It provides forward only access to multiple sets of
    /// data. It maintains a connection to the database until
    /// it is disposed and reads from the database as it is
    /// advanced through the result sets.
    /// It also has lambda expressions that it will call when
    /// the data reader is disposed by the application. These
    /// are used to release resources within Prius when the
    /// application is finished with the data.
    /// This version of the data reader depends on the SQLite
    /// ADO.Net driver in System.Data.SQLite
    /// </summary>
    internal class AdoDataReader : Disposable, IDataReader
    {
        private readonly IErrorReporter _errorReporter;

        private SQLiteDataReader _reader;
        private bool _hasErrors;
        private Action<IDataReader> _errorAction;
        private Action<IDataReader> _closeAction;

        public AdoDataReader(IErrorReporter errorReporter)
        {
            _errorReporter = errorReporter;
        }

        public IDataReader Initialize(
            SQLiteDataReader reader, 
            string dataShapeName, 
            Action<IDataReader> closeAction, 
            Action<IDataReader> errorAction)
        {
            _reader = reader;
            DataShapeName = dataShapeName;
            _closeAction = closeAction;
            _errorAction = errorAction;
            return this;
        }

        protected override void Dispose(bool destructor)
        {
            _reader.Dispose();
            if (_hasErrors) _errorAction?.Invoke(this);
            _closeAction?.Invoke(this);
            base.Dispose(destructor);
        }

        public string DataShapeName { get; private set; }

        public bool IsServerOffline { get { return false; } }

        public Exception ServerOfflineException { get { return null; } }

        public int FieldCount
        {
            get { return _reader.FieldCount; }
        }

        public object this[int fieldIndex]
        {
            get { return _reader[fieldIndex]; }
        }

        public object this[string fieldName]
        {
            get { return _reader[fieldName]; }
        }

        public string GetFieldName(int fieldIndex)
        {
            return _reader.GetName(fieldIndex);
        }

        public int GetFieldIndex(string fieldName)
        {
            fieldName = fieldName.ToLower();
            for (var fieldIndex = 0; fieldIndex < _reader.FieldCount; fieldIndex++)
                if (_reader.GetName(fieldIndex).ToLower() == fieldName) return fieldIndex;
            return -1;
        }

        public bool IsNull(int fieldIndex)
        {
            return _reader.IsDBNull(fieldIndex);
        }

        public bool Read()
        {
            try
            {
                return _reader.Read();
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex, "Failed to read SQL result data");
                _hasErrors = true;
                return false;
            }
        }

        public bool NextResult()
        {
            try
            {
                return _reader.NextResult();
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex, "Failed to read next SQL result set");
                _hasErrors = true;
                return false;
            }
        }

        public object Get(int fieldIndex, object defaultValue, Type type)
        {
            if (fieldIndex < 0 || _reader.IsDBNull(fieldIndex)) return defaultValue;
            if (type.IsNullable())
            {
                if (_reader.IsDBNull(fieldIndex)) return null;
                type = type.GetGenericArguments()[0];
            }
            if (type.IsEnum) type = typeof(int);
            return Convert.ChangeType(_reader[fieldIndex], type);
        }

        public T Get<T>(int fieldIndex, T defaultValue)
        {
            return (T)Get(fieldIndex, defaultValue, typeof(T));
        }

        public T Get<T>(string fieldName, T defaultValue)
        {
            return (T)Get(GetFieldIndex(fieldName), defaultValue, typeof(T));
        }

        public object Get(string fieldName, object defaultValue, Type type)
        {
            return Get(GetFieldIndex(fieldName), defaultValue, type);
        }
    }
}
