﻿using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Base;
using SQLite;
using System;
using System.IO;

namespace Dopamine.Data
{
    public class SQLiteConnectionFactory : ISQLiteConnectionFactory
    {
        public string DatabaseFile => Path.Combine(SettingsClient.ApplicationFolder(), ProductInformation.ApplicationName + ".db");

        public SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(this.DatabaseFile) { BusyTimeout = TimeSpan.FromSeconds(10) };
        }
    }
}
