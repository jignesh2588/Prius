﻿using System.Data.SQLite;
using Prius.Contracts.Interfaces.Commands;
using Prius.Contracts.Interfaces.Connections;

namespace Prius.SqLite.Interfaces
{
    /// <summary>
    /// Creates objects that can handle a specific type of request
    /// using the ADO.Net driver for SqLite in System.Data.SQLite
    /// </summary>
    public interface ICommandProcessorFactory
    {
        ICommandProcessor CreateAdo(
            IRepository repository,
            ICommand command,
            SQLiteConnection connection,
            SQLiteTransaction transaction);
    }
}
