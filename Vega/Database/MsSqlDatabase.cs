﻿/*
 Description: Vega - Fastest ORM with enterprise features
 Author: Ritesh Sutaria
 Date: 9-Dec-2017
 Home Page: https://github.com/aadreja/vega
            http://www.vegaorm.com
*/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace Vega.Data
{
    internal class MsSqlDatabase : Database
    {

        public MsSqlDatabase()
        {

        }

        public override string DEFAULTSCHEMA { get { return "dbo"; } }
        public override string CURRENTDATETIMESQL { get { return "getdate()"; } }
        public override string BITTRUEVALUE { get { return "1"; } }
        public override string BITFALSEVALUE { get { return "0"; } }
        public override string LASTINSERTEDROWIDSQL { get { return "SELECT SCOPE_IDENTITY();"; }}

        //https://msdn.microsoft.com/en-us/library/cc716729(v=vs.100).aspx
        public override Dictionary<DbType, string> DbTypeString
        {
            get
            {
                if (dbTypeString != null)
                    return dbTypeString;

                dbTypeString = new Dictionary<DbType, String>
                {
                    [DbType.String] = "nvarchar(MAX)",
                    [DbType.StringFixedLength] = "nvarchar",
                    [DbType.AnsiString] = "varchar(MAX)",
                    [DbType.AnsiStringFixedLength] = "varchar",
                    [DbType.Guid] = "uniqueidentifier",
                    [DbType.Byte] = "tinyint",
                    [DbType.Int16] = "smallint",
                    [DbType.Int32] = "int",
                    [DbType.Int64] = "bigint",
                    [DbType.Boolean] = "bit",
                    [DbType.Decimal] = "decimal",
                    [DbType.Single] = "single",
                    [DbType.Double] = "double",
                    [DbType.DateTime] = "datetime",
                    [DbType.Binary] = "binary"
                };

                return dbTypeString;
            }
        }

        public override string DBObjectExistsQuery(string name, DBObjectTypeEnum objectType, string schema = null)
        {
            if (schema == null)
                schema = DEFAULTSCHEMA;

            string query = string.Empty;

            if (objectType == DBObjectTypeEnum.Database)
            {
                query = $"SELECT 1 FROM sys.databases WHERE name='{name}'";
            }
            else if (objectType == DBObjectTypeEnum.Schema)
            {
                query = $"SELECT 1 FROM sys.schemas WHERE name='{name}'";
            }
            else if (objectType == DBObjectTypeEnum.Table)
            {
                query = $"SELECT 1 FROM sys.tables WHERE name='{name}' AND schema_id=SCHEMA_ID('{schema}')";
            }
            else if (objectType == DBObjectTypeEnum.View)
            {
                query = $"SELECT 1 FROM sys.views WHERE name='{name}' AND schema_id=SCHEMA_ID('{schema}')"; 
            }
            else if (objectType == DBObjectTypeEnum.Function)
            {
                query = $"SELECT 1 FROM Information_schema.Routines WHERE specific_name='{name}' AND SPECIFIC_SCHEMA='{schema}' AND routine_type='FUNCTION'";
            }
            else if (objectType == DBObjectTypeEnum.Procedure)
            {
                query = $"SELECT 1 FROM Information_schema.Routines WHERE specific_name='{name}' AND SPECIFIC_SCHEMA='{schema}' AND routine_type='PROCEDURE'";
            }

            return query;
        }

        public override string CreateTableQuery(Type entity)
        {
            TableAttribute tableInfo = EntityCache.Get(entity);

            StringBuilder createSQL = new StringBuilder($"CREATE TABLE {tableInfo.FullName} (");

            for (int i = 0; i < tableInfo.Columns.Count; i++)
            {
                ColumnAttribute col = tableInfo.Columns.ElementAt(i).Value;

                if (tableInfo.PrimaryKeyColumn.Name == col.Name)
                {
                    createSQL.Append($"{col.Name} {DbTypeString[col.ColumnDbType]} NOT NULL PRIMARY KEY ");

                    if (tableInfo.PrimaryKeyAttribute.IsIdentity)
                    {
                        createSQL.Append(" IDENTITY ");
                    }
                    createSQL.Append(",");
                }
                else if (col.IgnoreInfo.Insert || col.IgnoreInfo.Update)
                {
                    continue;
                }
                else
                {
                    createSQL.Append($"{col.Name} {GetDBTypeWithSize(col.ColumnDbType, col.NumericPrecision, col.NumericScale)}");

                    if (col.Name == Config.CREATEDON_COLUMN.Name || col.Name == Config.UPDATEDON_COLUMN.Name)
                    {
                        createSQL.Append(" DEFAULT " + CURRENTDATETIMESQL);
                    }
                    createSQL.Append(",");
                }
            }
            createSQL.RemoveLastComma(); //Remove last comma if exists

            createSQL.Append(");");

            return createSQL.ToString();
        }

        public override string CreateIndexQuery(string tableName, string indexName, string columns, bool isUnique)
        {
            return $@"CREATE {(isUnique ? "UNIQUE" : "")} INDEX {indexName} ON {tableName} ({columns})";
        }

        public override string IndexExistsQuery(string tableName, string indexName)
        {
            /*si.object_id AS ObjectId, si.index_id AS IndexId, 
            si.name AS IndexName, si.type AS IndexType, si.type_desc AS IndexTypeDesc, 
            si.is_unique AS IndexIsUnique, si.is_primary_key AS IndexIsPrimarykey, 
            si.fill_factor AS IndexFillFactor, sic.column_id, sc.name*/

            return $@"SELECT 1 FROM sys.indexes AS si
                        WHERE type<> 0 AND name='{indexName}' AND si.object_id IN(SELECT object_id from sys.tables WHERE name='{tableName}')";
        }

        public override string VirtualForeignKeyCheckQuery(ForeignKey vfk)
        {
            StringBuilder query = new StringBuilder();

            query.Append($"SELECT TOP 1 1 FROM {vfk.FullTableName} WHERE {vfk.ColumnName}=@Id");

            if (vfk.ContainsIsActive)
                query.Append($" AND {Config.ISACTIVE_COLUMNNAME}={BITTRUEVALUE}");

            return query.ToString();
        }

        public override void FetchDBServerInfo(IDbConnection connection)
        {
            if (connection == null) throw new Exception("Required valid connection object to initialise database details");

            string query = @"SELECT SERVERPROPERTY('Edition') AS Edition,SERVERPROPERTY('ProductVersion') AS ProductVersion;";
            bool isConOpen = connection.State == ConnectionState.Open;

            try
            {
                if (!isConOpen) connection.Open();

                IDbCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = query;

                using (IDataReader rdr = command.ExecuteReader())
                {
                    rdr.Read();

                    DBVersion = new DBVersionInfo
                    {
                        ProductName = "Microsoft SQL Server",
                        Edition = rdr.GetString(0),
                        Version = new Version(rdr.GetString(1))
                    };

                    //https://sqlserverbuilds.blogspot.in/
                    //https://support.microsoft.com/en-in/help/321185/how-to-determine-the-version--edition-and-update-level-of-sql-server-a
                    if (DBVersion.Version.Major == 14)
                        DBVersion.ProductName += "2017 " + rdr.GetString(0);
                    else if (DBVersion.Version.Major == 13)
                        DBVersion.ProductName += "2016 " + rdr.GetString(0);
                    else if (DBVersion.Version.Major == 12)
                        DBVersion.ProductName += "2014 " + rdr.GetString(0);
                    else if (DBVersion.Version.Major == 11)
                        DBVersion.ProductName += "2012 " + rdr.GetString(0);
                    else if (DBVersion.Version.Major == 10)
                    {
                        if (DBVersion.Version.Minor >= 50)
                            DBVersion.ProductName += "2008 R2 " + rdr.GetString(0);
                        else
                            DBVersion.ProductName += "2008 " + rdr.GetString(0);
                    }
                    else if (DBVersion.Version.Major == 9)
                        DBVersion.ProductName += "2005 " + rdr.GetString(0);
                    else if (DBVersion.Version.Major == 8)
                        DBVersion.ProductName += "2000 " + rdr.GetString(0);
                    else if (DBVersion.Version.Major == 7)
                        DBVersion.ProductName += "7.0 " + rdr.GetString(0);

                    rdr.Close();
                }
            }
            catch
            {
                //ignore error
            }
            finally
            {
                if (!isConOpen && connection.State == ConnectionState.Open) connection.Close();
            }
        }

        internal bool IsOffsetSupported()
        {
            //SQL Server 2012 and above supports offset keyword
            return DBVersion.Version.Major >= 11;
        }

    }
}